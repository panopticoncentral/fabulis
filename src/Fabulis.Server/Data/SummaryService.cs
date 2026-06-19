using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

/// <summary>
/// Singleton background worker that keeps story summaries up to date. It
/// sweeps on an interval while the vault is unlocked, is poked immediately
/// when a story is saved, and handles explicit full-rebuild requests. The
/// set of stories currently generating is held in memory (never persisted),
/// so a restart cannot leave a story stranded mid-generation.
/// </summary>
public sealed class SummaryService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VaultService _vault;
    private readonly ILogger<SummaryService> _log;

    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();
    private readonly ConcurrentDictionary<int, byte> _forceRebuild = new();
    private CancellationTokenSource _lockCts = new();

    public SummaryService(
        IServiceScopeFactory scopeFactory,
        VaultService vault,
        ILogger<SummaryService> log)
    {
        _scopeFactory = scopeFactory;
        _vault = vault;
        _log = log;
        // Cancel any in-flight summary work the instant the vault locks; the
        // DbContext for that scope becomes unusable once the password is gone.
        vault.Locked += () =>
        {
            try { _lockCts.Cancel(); } catch { }
        };
    }

    public bool IsGenerating(int storyId) => _inFlight.ContainsKey(storyId);

    /// <summary>
    /// Wake the worker to pick up new work as soon as possible. The sweep
    /// re-scans all stories, so <paramref name="storyId"/> is only a hint about
    /// what changed and isn't routed individually.
    /// </summary>
    public void Enqueue(int storyId) => Wake();

    /// <summary>Request a full from-scratch rebuild of a story's summary.</summary>
    public void EnqueueRebuild(int storyId)
    {
        _forceRebuild[storyId] = 0;
        Wake();
    }

    private void Wake()
    {
        // Release at most one pending permit; extra wake-ups just cause a
        // redundant (cheap) sweep.
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            if (!_vault.IsUnlocked) continue;

            // Fresh per-sweep token chained to shutdown; replaced reference so
            // the vault.Locked handler cancels the current sweep. Dispose the
            // previous source (each holds an OS wait handle) before replacing it.
            //
            // Race note: the Locked handler reads _lockCts from another thread.
            // If a lock fires right after this assignment it cancels the new
            // source and cuts a just-started sweep short — harmless here, since
            // the next wake re-runs the sweep within SweepInterval, and we must
            // not summarize against a locked (password-less) DbContext anyway.
            var previousCts = _lockCts;
            _lockCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            previousCts.Dispose();
            try
            {
                await SweepAsync(_lockCts.Token);
            }
            catch (OperationCanceledException) { /* vault locked or shutting down */ }
            catch (Exception ex)
            {
                _log.LogError(ex, "Summary sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
        var openRouter = scope.ServiceProvider.GetRequiredService<OpenRouterService>();

        var (model, prompt) = await ResolveModelAndPromptAsync(db);
        if (string.IsNullOrWhiteSpace(model))
            return; // No model configured; nothing we can do until settings change.

        // Stories with a newer version than last summarized, OR explicitly
        // queued for a full rebuild.
        var candidates = await db.Stories
            .Select(s => new
            {
                s.Id,
                Latest = s.Versions.Max(v => (int?)v.VersionNumber) ?? 0,
                s.SummarizedThroughVersion,
            })
            .ToListAsync(ct);

        var toProcess = new List<(int Id, bool Forced)>();
        foreach (var c in candidates)
        {
            var forced = _forceRebuild.ContainsKey(c.Id);
            if (c.Latest == 0)
            {
                // No versions to summarize yet; clear any stale rebuild flag so
                // it doesn't linger forever.
                _forceRebuild.TryRemove(c.Id, out _);
                continue;
            }
            if (!forced && !StorySummary.NeedsWork(c.SummarizedThroughVersion, c.Latest))
                continue;

            toProcess.Add((c.Id, forced));
        }

        if (toProcess.Count == 0) return;
        _log.LogInformation("Summary sweep: {Count} story/ies need summarization.", toProcess.Count);

        foreach (var (id, forced) in toProcess)
        {
            ct.ThrowIfCancellationRequested();
            if (!_vault.IsUnlocked) return;

            await ProcessStoryAsync(db, openRouter, id, model, prompt, fullRebuild: forced, ct);
            _forceRebuild.TryRemove(id, out _);
        }
    }

    private async Task ProcessStoryAsync(
        FabulisDbContext db, OpenRouterService openRouter,
        int storyId, string model, string prompt, bool fullRebuild, CancellationToken ct)
    {
        _inFlight[storyId] = 0;
        try
        {
            var story = await db.Stories
                .Include(s => s.Versions).ThenInclude(v => v.Messages)
                .FirstOrDefaultAsync(s => s.Id == storyId, ct);
            if (story is null || story.Versions.Count == 0) return;

            var versions = story.Versions.OrderBy(v => v.VersionNumber).ToList();
            var latest = versions[^1].VersionNumber;

            _log.LogInformation(
                "Summarizing story {StoryId} through version {Version} ({Mode}).",
                storyId, latest, fullRebuild ? "full rebuild" : "incremental");

            string? priorSummary;
            IEnumerable<StoryVersion> versionsToInclude;
            if (fullRebuild || string.IsNullOrWhiteSpace(story.SummaryText))
            {
                priorSummary = null;
                versionsToInclude = versions; // first-time OR full rebuild: all versions
            }
            else
            {
                priorSummary = story.SummaryText;
                versionsToInclude = versions
                    .Where(v => v.VersionNumber > (story.SummarizedThroughVersion ?? 0));
            }

            var content = string.Join("\n\n---\n\n",
                versionsToInclude
                    .Select(v => StorySummary.BuildVersionBody(v.Messages))
                    .Where(body => body.Length > 0));

            if (string.IsNullOrWhiteSpace(content))
            {
                // Nothing summarizable; mark caught up so we don't spin on it.
                story.SummarizedThroughVersion = latest;
                await db.SaveChangesAsync(ct);
                _log.LogInformation("Story {StoryId} has no summarizable content; marked caught up.", storyId);
                return;
            }

            var userMessage = StorySummary.ComposeUserMessage(priorSummary, content);

            var raw = await openRouter.ChatAsync(
                model, prompt, userMessage,
                temperature: 0.3, disableReasoning: true);
            var summary = StorySummary.CleanSummary(raw);

            if (string.IsNullOrWhiteSpace(summary))
            {
                story.SummaryStatus = SummaryStatus.Failed;
                story.SummaryError = "The model returned an empty summary.";
                _log.LogWarning("Story {StoryId} summary failed: the model returned an empty summary.", storyId);
            }
            else
            {
                story.SummaryText = summary;
                story.SummarizedThroughVersion = latest;
                story.SummaryStatus = SummaryStatus.Ready;
                story.SummaryError = null;
                story.SummaryUpdatedAt = DateTime.UtcNow;
                _log.LogInformation("Summarized story {StoryId} (through version {Version}).", storyId, latest);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Summarizing story {StoryId} failed.", storyId);
            try
            {
                var story = await db.Stories.FindAsync([storyId], ct);
                if (story is not null)
                {
                    story.SummaryStatus = SummaryStatus.Failed;
                    story.SummaryError = ex.Message;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch { /* best effort */ }
        }
        finally
        {
            _inFlight.TryRemove(storyId, out _);
        }
    }

    private static async Task<(string? model, string prompt)> ResolveModelAndPromptAsync(FabulisDbContext db)
    {
        var summaryModel = await db.AppSettings.FindAsync("SummaryModel");
        var assistant = await db.AppSettings.FindAsync("AssistantModel");
        var model = !string.IsNullOrWhiteSpace(summaryModel?.Value)
            ? summaryModel!.Value
            : assistant?.Value;

        var promptSetting = await db.AppSettings.FindAsync("SummaryPrompt");
        var prompt = string.IsNullOrWhiteSpace(promptSetting?.Value)
            ? StorySummary.DefaultPrompt
            : promptSetting!.Value;

        return (model, prompt);
    }
}
