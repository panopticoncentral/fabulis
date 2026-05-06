using System.Collections.Concurrent;

namespace Fabulis.Server.Data;

/// <summary>
/// Singleton owner of in-flight (and recently-completed) draft generations.
/// Decouples model streaming from the HTTP request: <see cref="Start"/> kicks
/// off a generation that survives client disconnects, and HTTP handlers
/// attach to it via <see cref="Generation.SubscribeAsync"/>.
/// </summary>
public sealed class GenerationManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VaultService _vault;
    private readonly ILogger<GenerationManager> _log;
    private readonly ConcurrentDictionary<int, Generation> _gens = new();

    public GenerationManager(
        IServiceScopeFactory scopeFactory,
        VaultService vault,
        ILogger<GenerationManager> log)
    {
        _scopeFactory = scopeFactory;
        _vault = vault;
        _log = log;
        vault.Locked += CancelAll;
    }

    public Generation? Get(int draftId) =>
        _gens.TryGetValue(draftId, out var g) ? g : null;

    public bool IsRunning(int draftId) =>
        _gens.TryGetValue(draftId, out var g) && g.Status == GenerationStatus.Running;

    public void Remove(int draftId) => _gens.TryRemove(draftId, out _);

    public void CancelAll()
    {
        foreach (var g in _gens.Values)
        {
            try { g.Cts.Cancel(); } catch { }
        }
        _gens.Clear();
    }

    /// <summary>
    /// Starts a new generation for <paramref name="draftId"/>. Caller is
    /// responsible for ensuring no generation is currently running for this
    /// draft (see <see cref="IsRunning"/>); a prior completed generation is
    /// silently replaced.
    /// </summary>
    public Generation Start(int draftId)
    {
        var gen = new Generation(draftId);
        _gens[draftId] = gen;
        _ = Task.Run(() => RunAsync(gen));
        return gen;
    }

    private async Task RunAsync(Generation gen)
    {
        // One scope for the lifetime of this generation. The DbContext is
        // configured with the vault password at scope-creation time, so it
        // remains usable even if the vault locks mid-stream (locking will
        // separately cancel us via Cts).
        using var scope = _scopeFactory.CreateScope();
        DraftService? drafts = null;
        try
        {
            drafts = scope.ServiceProvider.GetRequiredService<DraftService>();
            var openRouter = scope.ServiceProvider.GetRequiredService<OpenRouterService>();

            var draft = await drafts.GetDraftAsync(gen.DraftId);
            if (draft is null) { gen.Fail("draft not found"); return; }

            var st = draft.Storyteller;
            await foreach (var chunk in openRouter.ChatStreamAsync(
                st.ModelName, st.Prompt, draft.Messages.ToList(),
                st.Temperature, st.TopP, st.MaxTokens,
                st.MinP, st.TopK, st.TopA, gen.Cts.Token))
            {
                _vault.RecordActivity();
                gen.Append(chunk.Text, chunk.Kind == StreamChunkKind.Reasoning);
            }

            await SaveAndComplete(gen, drafts, GenerationStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            try
            {
                drafts ??= scope.ServiceProvider.GetRequiredService<DraftService>();
                await SaveAndComplete(gen, drafts, GenerationStatus.Aborted);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save partial response for draft {DraftId} after cancel", gen.DraftId);
                gen.Fail(ex.Message);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Generation failed for draft {DraftId}", gen.DraftId);
            gen.Fail(ex.Message);
        }
    }

    private static async Task SaveAndComplete(Generation gen, DraftService drafts, GenerationStatus terminal)
    {
        int? savedId = null;
        if (gen.ContentLength > 0)
        {
            var saved = await drafts.AddMessageAsync(gen.DraftId, MessageRole.Response, gen.Snapshot);
            savedId = saved.Id;
        }
        gen.Complete(savedId, terminal);
    }
}
