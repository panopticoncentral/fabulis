# Story Summaries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every story an editable, background-generated one-paragraph summary that folds in new versions over time and can be force-regenerated.

**Architecture:** Summary state lives as columns on the `Stories` table. A singleton `BackgroundService` (`SummaryService`) sweeps for stories needing work (and is poked on save) while the vault is unlocked, calling OpenRouter via the existing `OpenRouterService.ChatAsync`. Input composition is a pure, unit-tested helper (`StorySummary`) parallel to `TitleGeneration`. A new `/stories/{id}/summary` endpoint group serves GET/PUT/regenerate. The SwiftUI client surfaces the summary behind a toolbar button → sheet, and adds two settings.

**Tech Stack:** ASP.NET Core / .NET 10, EF Core + SQLite (hand-written SQL schema, not migrations), xUnit; SwiftUI client.

Reference spec: `docs/superpowers/specs/2026-06-17-story-summaries-design.md`

---

## File structure

**Server (`src/Fabulis.Server/`)**
- Create `Data/StorySummary.cs` — pure helper (default prompt, body build, input composition, output cleanup, needs-work predicate).
- Create `Data/SummaryService.cs` — singleton `BackgroundService`: sweep + on-save enqueue + force-rebuild + in-flight tracking.
- Modify `Data/Story.cs` — add summary columns.
- Modify `Data/FabulisDbContext.cs` — map new columns + add them to the raw-SQL schema (ALTER for existing vaults).
- Modify `Api/Dtos.cs` — `SummaryDto`; extend `SettingsDto` / `SettingsUpdateRequest`.
- Modify `Api/StoryEndpoints.cs` — GET/PUT/regenerate summary endpoints.
- Modify `Api/SettingsEndpoints.cs` — read/write `SummaryModel` + `SummaryPrompt`.
- Modify `Api/DraftEndpoints.cs` — enqueue summary after save.
- Modify `Program.cs` — register `SummaryService` as singleton + hosted service.

**Tests (`tests/Fabulis.Server.Tests/`)**
- Create `StorySummaryTests.cs` — unit tests for the pure helper.

**Client (`client/Fabulis/`)**
- Modify `Models/APIDtos.swift` — `StorySummaryDetail`; extend `SettingsDto`.
- Modify `Services/FabulisAPIClient.swift` — summary methods + settings params.
- Create `Views/Story/StorySummarySheet.swift` — the sheet.
- Modify `Views/Story/StoryView.swift` — toolbar button + sheet presentation.
- Modify `Views/Settings/SettingsView.swift` — summary model picker + prompt editor.

---

## Task 1: `StorySummary` pure helper (TDD)

**Files:**
- Create: `src/Fabulis.Server/Data/StorySummary.cs`
- Test: `tests/Fabulis.Server.Tests/StorySummaryTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Fabulis.Server.Tests/StorySummaryTests.cs`:

```csharp
using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class StorySummaryTests
{
    [Fact]
    public void BuildVersionBodyJoinsOnlyResponsesInSortOrder()
    {
        var messages = new List<StoryMessage>
        {
            new() { Content = "second response", Role = MessageRole.Response, SortOrder = 3 },
            new() { Content = "the user prompt", Role = MessageRole.Prompt, SortOrder = 0 },
            new() { Content = "first response", Role = MessageRole.Response, SortOrder = 1 },
        };

        Assert.Equal("first response\n\nsecond response", StorySummary.BuildVersionBody(messages));
    }

    [Fact]
    public void ComposeUserMessageReturnsContentOnlyWhenNoPriorSummary()
    {
        Assert.Equal("the story", StorySummary.ComposeUserMessage(null, "the story"));
        Assert.Equal("the story", StorySummary.ComposeUserMessage("   ", "the story"));
    }

    [Fact]
    public void ComposeUserMessageIncludesPriorSummaryWhenPresent()
    {
        var result = StorySummary.ComposeUserMessage("old summary", "new version text");

        Assert.Equal(
            "EXISTING SUMMARY:\nold summary\n\nNEW STORY CONTENT:\nnew version text",
            result);
    }

    [Theory]
    [InlineData("A tidy paragraph.", "A tidy paragraph.")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("line one\n\nline two", "line one line two")]
    [InlineData("line one\n  \nline two\n", "line one line two")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CleanSummaryCollapsesToSingleParagraph(string raw, string expected)
    {
        Assert.Equal(expected, StorySummary.CleanSummary(raw));
    }

    [Theory]
    [InlineData(null, 1, true)]   // never summarized
    [InlineData(0, 2, true)]      // stale: new version exists
    [InlineData(2, 2, false)]     // up to date
    [InlineData(3, 2, false)]     // defensive: ahead, treat as done
    public void NeedsWorkComparesSummarizedVersionToLatest(int? summarizedThrough, int latest, bool expected)
    {
        Assert.Equal(expected, StorySummary.NeedsWork(summarizedThrough, latest));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Server.Tests --filter StorySummaryTests`
Expected: FAIL — `StorySummary` does not exist (compile error).

- [ ] **Step 3: Implement the helper**

Create `src/Fabulis.Server/Data/StorySummary.cs`:

```csharp
namespace Fabulis.Server.Data;

/// <summary>
/// Pure helpers for turning a story's versions into a one-paragraph
/// summary. The LLM call lives in <see cref="SummaryService"/>; everything
/// here is deterministic and unit-tested. Parallel to <see cref="TitleGeneration"/>.
/// </summary>
public static class StorySummary
{
    public const string DefaultPrompt =
        "You write concise summaries of stories. Given the full text of a story — and, when an existing summary is provided, that summary to update — respond with a single paragraph (3 to 5 sentences) capturing the main characters, setting, and arc. Output only the summary paragraph: no preamble, no headings, no quotation marks, no commentary.";

    /// <summary>
    /// Joins a version's assistant responses (in sort order), ignoring the
    /// user-side prompts. Returns "" when the version has no responses.
    /// </summary>
    public static string BuildVersionBody(IEnumerable<StoryMessage> messages) =>
        string.Join("\n\n", messages
            .Where(m => m.Role == MessageRole.Response)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.Content));

    /// <summary>
    /// Builds the user message. With no prior summary the model just sees the
    /// story content; with one it sees the prior summary plus the content to
    /// fold in.
    /// </summary>
    public static string ComposeUserMessage(string? priorSummary, string storyContent)
    {
        if (string.IsNullOrWhiteSpace(priorSummary))
            return storyContent;

        return $"EXISTING SUMMARY:\n{priorSummary}\n\nNEW STORY CONTENT:\n{storyContent}";
    }

    /// <summary>
    /// Normalizes model output to a single paragraph: trims, drops blank
    /// lines, and joins the rest with single spaces.
    /// </summary>
    public static string CleanSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join(" ", lines);
    }

    /// <summary>
    /// True when the story has unsummarized content: either nothing has been
    /// summarized yet, or a newer version exists than the one last summarized.
    /// </summary>
    public static bool NeedsWork(int? summarizedThroughVersion, int latestVersion) =>
        summarizedThroughVersion is not int through || through < latestVersion;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fabulis.Server.Tests --filter StorySummaryTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Data/StorySummary.cs tests/Fabulis.Server.Tests/StorySummaryTests.cs
git commit -m "Add StorySummary helper for summary input composition"
```

---

## Task 2: Story entity + schema columns

**Files:**
- Modify: `src/Fabulis.Server/Data/Story.cs`
- Modify: `src/Fabulis.Server/Data/FabulisDbContext.cs`

- [ ] **Step 1: Add the status enum + properties to `Story`**

Replace the body of `src/Fabulis.Server/Data/Story.cs` with:

```csharp
namespace Fabulis.Server.Data;

public enum SummaryStatus
{
    None = 0,
    Ready = 1,
    Failed = 2,
}

public class Story
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }

    // Summary state (1:1 with the story). "Generating" is NOT stored here —
    // it is tracked in-memory by SummaryService so a restart can't strand a
    // story mid-generation.
    public string? SummaryText { get; set; }
    public SummaryStatus SummaryStatus { get; set; } = SummaryStatus.None;
    public int? SummarizedThroughVersion { get; set; }
    public string? SummaryError { get; set; }
    public DateTime? SummaryUpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public List<StoryVersion> Versions { get; set; } = [];
}
```

- [ ] **Step 2: Map the enum as a string + extend the raw-SQL schema**

In `src/Fabulis.Server/Data/FabulisDbContext.cs`, inside `OnModelCreating`, add the enum→string conversion next to the existing `MessageRole` conversions:

```csharp
        modelBuilder.Entity<Story>()
            .Property(s => s.SummaryStatus)
            .HasConversion<string>();
```

Then, in `EnsureSchemaUpdatedAsync`, find where the `Stories` table is created (the `CREATE TABLE IF NOT EXISTS Stories (...)` block). Immediately AFTER that `ExecuteSqlRawAsync` call, add a column-backfill block mirroring the existing `TitlingPrompt` ALTER pattern:

```csharp
        // Stories gained summary columns after the initial release. CREATE
        // TABLE IF NOT EXISTS never alters an existing table, so add them on
        // vaults created before this feature existed.
        var storyColumns = await Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Stories')")
            .ToListAsync();
        if (!storyColumns.Contains("SummaryText"))
        {
            await Database.ExecuteSqlRawAsync("ALTER TABLE Stories ADD COLUMN SummaryText TEXT NULL");
            await Database.ExecuteSqlRawAsync("ALTER TABLE Stories ADD COLUMN SummaryStatus TEXT NOT NULL DEFAULT 'None'");
            await Database.ExecuteSqlRawAsync("ALTER TABLE Stories ADD COLUMN SummarizedThroughVersion INTEGER NULL");
            await Database.ExecuteSqlRawAsync("ALTER TABLE Stories ADD COLUMN SummaryError TEXT NULL");
            await Database.ExecuteSqlRawAsync("ALTER TABLE Stories ADD COLUMN SummaryUpdatedAt TEXT NULL");
        }
```

> Note: `SummaryStatus` is stored as TEXT because the EF conversion above is `HasConversion<string>()` (matching how `MessageRole` is stored). The default `'None'` matches `SummaryStatus.None`.

If the `Stories` `CREATE TABLE` block does not yet include these columns for fresh databases, also add them to that `CREATE TABLE IF NOT EXISTS Stories` statement:

```
                SummaryText TEXT NULL,
                SummaryStatus TEXT NOT NULL DEFAULT 'None',
                SummarizedThroughVersion INTEGER NULL,
                SummaryError TEXT NULL,
                SummaryUpdatedAt TEXT NULL,
```

(Place them before the `FOREIGN KEY` / closing paren, matching the column list style of that block.)

- [ ] **Step 3: Verify it builds and existing tests pass**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded.

Run: `dotnet test tests/Fabulis.Server.Tests`
Expected: PASS — in-memory `EnsureCreated()` picks up the new entity columns; no test references them yet.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Data/Story.cs src/Fabulis.Server/Data/FabulisDbContext.cs
git commit -m "Add summary columns to Story entity and schema"
```

---

## Task 3: `SummaryService` background worker

**Files:**
- Create: `src/Fabulis.Server/Data/SummaryService.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Implement `SummaryService`**

Create `src/Fabulis.Server/Data/SummaryService.cs`:

```csharp
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

    /// <summary>Wake the worker to (re)summarize a story as soon as possible.</summary>
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
            // the vault.Locked handler cancels the current sweep.
            _lockCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
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

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!_vault.IsUnlocked) return;

            var forced = _forceRebuild.ContainsKey(c.Id);
            if (c.Latest == 0) continue; // no versions yet
            if (!forced && !StorySummary.NeedsWork(c.SummarizedThroughVersion, c.Latest))
                continue;

            await ProcessStoryAsync(db, openRouter, c.Id, model, prompt, fullRebuild: forced, ct);
            _forceRebuild.TryRemove(c.Id, out _);
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
            }
            else
            {
                story.SummaryText = summary;
                story.SummarizedThroughVersion = latest;
                story.SummaryStatus = SummaryStatus.Ready;
                story.SummaryError = null;
                story.SummaryUpdatedAt = DateTime.UtcNow;
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
```

> Design note: failures keep `SummarizedThroughVersion` unchanged, so a failed story stays "needs work" and is retried on the next sweep. There is no backoff — acceptable for a single-user LAN app; see plan tail for the deferred note.

- [ ] **Step 2: Register the service in `Program.cs`**

In `src/Fabulis.Server/Program.cs`, alongside the other singletons (e.g. after `builder.Services.AddSingleton<GenerationManager>();`), add:

```csharp
builder.Services.AddSingleton<SummaryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SummaryService>());
```

> Registering it both as a singleton and resolving the hosted service from that same instance means the endpoints (Task 5) and the background loop share one `SummaryService` (one in-flight set).

- [ ] **Step 3: Verify it builds**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Data/SummaryService.cs src/Fabulis.Server/Program.cs
git commit -m "Add SummaryService background summarizer"
```

---

## Task 4: Settings — SummaryModel + SummaryPrompt

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`
- Modify: `src/Fabulis.Server/Api/SettingsEndpoints.cs`

- [ ] **Step 1: Extend the settings DTOs**

In `src/Fabulis.Server/Api/Dtos.cs`, update the `SettingsDto` and `SettingsUpdateRequest` records to add the two fields (append to each record's parameter list):

```csharp
public sealed record SettingsDto(
    bool ApiKeyIsSet,
    string? AssistantModel,
    string AutoLockSelection, // "1"/"5"/"15"/"30"/"60"/"never"
    bool KokoroBaseUrlIsSet,
    string? NarrationVoice,
    double NarrationSpeed,
    bool NarrationAvailable,
    string? SummaryModel,
    string SummaryPrompt);

public sealed record SettingsUpdateRequest(
    string? ApiKey,             // null = leave alone
    string? AssistantModel,     // null = leave alone
    string? AutoLockSelection,  // null = leave alone, otherwise one of the legal strings
    string? KokoroBaseUrl,      // null = leave alone; empty string = clear
    string? NarrationVoice,     // null = leave alone
    double? NarrationSpeed,     // null = leave alone
    string? SummaryModel,       // null/blank = leave alone
    string? SummaryPrompt);     // null/blank = leave alone
```

- [ ] **Step 2: Read + write the new settings**

In `src/Fabulis.Server/Api/SettingsEndpoints.cs`:

In the GET handler, load the two settings alongside the others and add them to the `SettingsDto`:

```csharp
            var summaryModel = await db.AppSettings.FindAsync(["SummaryModel"], ct);
            var summaryPrompt = await db.AppSettings.FindAsync(["SummaryPrompt"], ct);
```

Then extend the `new SettingsDto(...)` construction with:

```csharp
                SummaryModel: summaryModel?.Value,
                SummaryPrompt: string.IsNullOrWhiteSpace(summaryPrompt?.Value)
                    ? StorySummary.DefaultPrompt
                    : summaryPrompt!.Value);
```

(Add `using Fabulis.Server.Data;` is already present in this file.)

In the PUT handler, after the existing narration handling and before `await db.SaveChangesAsync();`, add:

```csharp
            if (body.SummaryModel is { } summaryModel && !string.IsNullOrWhiteSpace(summaryModel))
                await UpsertAsync(db, "SummaryModel", summaryModel.Trim());

            if (body.SummaryPrompt is { } summaryPrompt && !string.IsNullOrWhiteSpace(summaryPrompt))
                await UpsertAsync(db, "SummaryPrompt", summaryPrompt.Trim());
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs src/Fabulis.Server/Api/SettingsEndpoints.cs
git commit -m "Add SummaryModel and SummaryPrompt settings"
```

---

## Task 5: Summary API endpoints

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`
- Modify: `src/Fabulis.Server/Api/StoryEndpoints.cs`
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs`

- [ ] **Step 1: Add the `SummaryDto` + edit request**

In `src/Fabulis.Server/Api/Dtos.cs`, in the stories section, add:

```csharp
public sealed record SummaryDto(
    string? Text,
    string Status,                 // "none" | "generating" | "ready" | "failed"
    int? SummarizedThroughVersion,
    int LatestVersion,
    bool IsStale,
    DateTime? UpdatedAt,
    string? Error);

public sealed record UpdateSummaryRequest(string Text);
```

- [ ] **Step 2: Add the endpoints**

In `src/Fabulis.Server/Api/StoryEndpoints.cs`, add a shared mapper and three routes inside `MapStoryEndpoints` (after the existing version route, before `return routes;`):

```csharp
        group.MapGet("/{id:int}/summary", async (
            int id, FabulisDbContext db, SummaryService summaries) =>
        {
            var story = await db.Stories
                .Include(s => s.Versions)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (story is null) return Results.NotFound();

            return Results.Ok(ToSummaryDto(story, summaries));
        });

        group.MapPut("/{id:int}/summary", async (
            int id, UpdateSummaryRequest body, FabulisDbContext db, SummaryService summaries) =>
        {
            var story = await db.Stories
                .Include(s => s.Versions)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (story is null) return Results.NotFound();

            var latest = story.Versions.Count > 0 ? story.Versions.Max(v => v.VersionNumber) : 0;
            story.SummaryText = body.Text.Trim();
            story.SummarizedThroughVersion = latest;
            story.SummaryStatus = SummaryStatus.Ready;
            story.SummaryError = null;
            story.SummaryUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(ToSummaryDto(story, summaries));
        });

        group.MapPost("/{id:int}/summary/regenerate", async (
            int id, FabulisDbContext db, SummaryService summaries) =>
        {
            var exists = await db.Stories.AnyAsync(s => s.Id == id);
            if (!exists) return Results.NotFound();

            summaries.EnqueueRebuild(id);
            return Results.Accepted();
        });
```

Add the mapper as a `private static` method in the same class:

```csharp
    private static SummaryDto ToSummaryDto(Story story, SummaryService summaries)
    {
        var latest = story.Versions.Count > 0 ? story.Versions.Max(v => v.VersionNumber) : 0;
        var status = summaries.IsGenerating(story.Id)
            ? "generating"
            : story.SummaryStatus switch
            {
                SummaryStatus.Ready => "ready",
                SummaryStatus.Failed => "failed",
                _ => "none",
            };

        return new SummaryDto(
            Text: story.SummaryText,
            Status: status,
            SummarizedThroughVersion: story.SummarizedThroughVersion,
            LatestVersion: latest,
            IsStale: StorySummary.NeedsWork(story.SummarizedThroughVersion, latest),
            UpdatedAt: story.SummaryUpdatedAt,
            Error: story.SummaryError);
    }
```

(Ensure `using Fabulis.Server.Data;` and `using Microsoft.EntityFrameworkCore;` are present — they already are.)

- [ ] **Step 3: Enqueue summarization after a draft is saved**

In `src/Fabulis.Server/Api/DraftEndpoints.cs`, find the save route handler (the one calling `drafts.SaveToLibraryAsync`, around line 90-105). Add a `SummaryService summaries` parameter to the handler's lambda and enqueue after the save:

```csharp
        group.MapPost("/{id:int}/save", async (
            int id,
            SaveDraftRequest body,
            DraftService drafts,
            SummaryService summaries) =>
        {
            // ... existing validation + SaveToLibraryAsync call ...
            var version = await drafts.SaveToLibraryAsync(id, categoryId, storyId, newStoryTitle);
            summaries.Enqueue(version.StoryId);
            return Results.Ok(new SaveDraftResponse(version.StoryId, version.Id, version.VersionNumber));
        });
```

> Keep the existing parameter names/validation exactly; only add the `SummaryService summaries` parameter and the one `summaries.Enqueue(version.StoryId);` line.

- [ ] **Step 4: Verify it builds and tests pass**

Run: `dotnet build Fabulis.slnx && dotnet test tests/Fabulis.Server.Tests`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 5: Manual smoke test (server)**

Run the server (`dotnet run --project src/Fabulis.Server`), unlock the vault, then with a story id that has at least one version:

```bash
# Replace TOKEN and ID. Expect JSON with "status":"none" then later "ready".
curl -s -H "Authorization: Bearer TOKEN" http://localhost:5288/api/v1/stories/ID/summary
```

Expected: 200 with a `SummaryDto`. Within ~30s (or immediately after a save) the background worker fills `text` and flips `status` to `"ready"` (requires the OpenRouter API key + a model configured in Settings).

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs src/Fabulis.Server/Api/StoryEndpoints.cs src/Fabulis.Server/Api/DraftEndpoints.cs
git commit -m "Add story summary endpoints and enqueue on save"
```

---

## Task 6: Client DTOs + API methods

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Add the client summary model + extend `SettingsDto`**

In `client/Fabulis/Models/APIDtos.swift`, add near the other story models:

```swift
struct StorySummaryDetail: Decodable, Sendable {
    let text: String?
    let status: String          // "none" | "generating" | "ready" | "failed"
    let summarizedThroughVersion: Int?
    let latestVersion: Int
    let isStale: Bool
    let updatedAt: Date?
    let error: String?
}
```

Extend the existing `SettingsDto` struct with two fields (append inside the struct):

```swift
    let summaryModel: String?
    let summaryPrompt: String
```

- [ ] **Step 2: Add API client methods + settings params**

In `client/Fabulis/Services/FabulisAPIClient.swift`, add summary methods near `storyVersion(...)`:

```swift
    func storySummary(id: Int) async throws -> StorySummaryDetail {
        try await request("GET", path: "/stories/\(id)/summary", authed: true)
    }

    func updateStorySummary(id: Int, text: String) async throws -> StorySummaryDetail {
        struct Body: Encodable { let text: String }
        return try await request("PUT", path: "/stories/\(id)/summary", body: Body(text: text), authed: true)
    }

    func regenerateStorySummary(id: Int) async throws {
        try await requestVoid("POST", path: "/stories/\(id)/summary/regenerate", authed: true)
    }
```

Then extend `updateSettings(...)` to carry the two new fields. Add the parameters (with `nil` defaults) and pass them through the inner `Body`:

```swift
    func updateSettings(
        apiKey: String? = nil,
        assistantModel: String? = nil,
        autoLockSelection: String? = nil,
        kokoroBaseUrl: String? = nil,
        narrationVoice: String? = nil,
        narrationSpeed: Double? = nil,
        summaryModel: String? = nil,
        summaryPrompt: String? = nil
    ) async throws {
        struct Body: Encodable {
            let apiKey: String?
            let assistantModel: String?
            let autoLockSelection: String?
            let kokoroBaseUrl: String?
            let narrationVoice: String?
            let narrationSpeed: Double?
            let summaryModel: String?
            let summaryPrompt: String?
        }
        try await requestVoid(
            "PUT",
            path: "/settings",
            body: Body(
                apiKey: apiKey,
                assistantModel: assistantModel,
                autoLockSelection: autoLockSelection,
                kokoroBaseUrl: kokoroBaseUrl,
                narrationVoice: narrationVoice,
                narrationSpeed: narrationSpeed,
                summaryModel: summaryModel,
                summaryPrompt: summaryPrompt),
            authed: true)
    }
```

- [ ] **Step 3: Verify the client builds**

Build the `Fabulis` scheme for an iOS Simulator destination (Xcode, or):

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "Add client summary DTOs and API methods"
```

---

## Task 7: Settings UI — summary model + prompt

**Files:**
- Modify: `client/Fabulis/Views/Settings/SettingsView.swift`

- [ ] **Step 1: Add a Summary section**

In `client/Fabulis/Views/Settings/SettingsView.swift`, add state for the prompt draft near the other `@State` fields:

```swift
    @State private var summaryPromptDraft: String = ""
    @State private var summaryPromptJustSaved = false
```

In `load()`, after `settings = try await ...`, seed the draft:

```swift
            if let settings { summaryPromptDraft = settings.summaryPrompt }
```

Add a new `Section` (place it after the "Storyteller" section), mirroring the existing "Assistant model" + prompt-editor patterns:

```swift
            Section("Story summaries") {
                if let settings, let current = settings.summaryModel {
                    Text(current).font(.callout.monospaced()).foregroundStyle(.secondary)
                }
                NavigationLink {
                    ModelPickerView(currentModel: settings?.summaryModel) { picked in
                        Task { await saveSummaryModel(picked) }
                    }
                } label: {
                    Text(settings?.summaryModel == nil ? "Choose summary model (defaults to assistant model)" : "Change summary model")
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text("Summary prompt").font(.caption).foregroundStyle(.secondary)
                    TextEditor(text: $summaryPromptDraft)
                        .frame(minHeight: 120)
                        .font(.callout)
                }
                Button {
                    Task { await saveSummaryPrompt() }
                } label: {
                    Text("Save prompt")
                }
                .disabled(summaryPromptDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                if summaryPromptJustSaved {
                    Text("Summary prompt saved.").font(.caption).foregroundStyle(.green)
                }
            }
```

Add the two save helpers next to `saveModel(...)`:

```swift
    private func saveSummaryModel(_ model: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(summaryModel: model)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveSummaryPrompt() async {
        let trimmed = summaryPromptDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        do {
            try await FabulisAPIClient.shared.updateSettings(summaryPrompt: trimmed)
            settings = try await FabulisAPIClient.shared.settings()
            summaryPromptJustSaved = true
            Task { try? await Task.sleep(for: .seconds(3)); summaryPromptJustSaved = false }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
```

- [ ] **Step 2: Verify the client builds**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Settings/SettingsView.swift
git commit -m "Add summary model and prompt to Settings"
```

---

## Task 8: Story summary sheet + toolbar button

**Files:**
- Create: `client/Fabulis/Views/Story/StorySummarySheet.swift`
- Modify: `client/Fabulis/Views/Story/StoryView.swift`

- [ ] **Step 1: Create the summary sheet**

Create `client/Fabulis/Views/Story/StorySummarySheet.swift`:

```swift
import SwiftUI

struct StorySummarySheet: View {
    let storyId: Int

    @Environment(\.dismiss) private var dismiss

    @State private var summary: StorySummaryDetail?
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var isEditing = false
    @State private var editDraft: String = ""
    @State private var isSaving = false
    @State private var pollTask: Task<Void, Never>?

    var body: some View {
        NavigationStack {
            Group {
                if isLoading && summary == nil {
                    ProgressView()
                } else if isEditing {
                    editor
                } else {
                    content
                }
            }
            .padding()
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .navigationTitle("Summary")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Done") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    if isEditing {
                        Button("Save") { Task { await saveEdit() } }
                            .disabled(isSaving)
                    } else {
                        Menu {
                            Button("Edit", systemImage: "pencil") { beginEdit() }
                                .disabled(summary?.status == "generating")
                            Button("Regenerate", systemImage: "arrow.clockwise") {
                                Task { await regenerate() }
                            }
                            .disabled(summary?.status == "generating")
                        } label: {
                            Image(systemName: "ellipsis.circle")
                        }
                    }
                }
            }
        }
        .task { await load(); startPollingIfNeeded() }
        .onDisappear { pollTask?.cancel() }
    }

    @ViewBuilder
    private var content: some View {
        if let summary {
            switch summary.status {
            case "generating":
                VStack(spacing: 12) {
                    ProgressView()
                    Text("Generating summary…").foregroundStyle(.secondary)
                }
            case "failed":
                VStack(alignment: .leading, spacing: 12) {
                    Label("Couldn't generate a summary", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                    if let err = summary.error {
                        Text(err).font(.caption).foregroundStyle(.secondary)
                    }
                    Button("Try again") { Task { await regenerate() } }
                }
            case "ready":
                ScrollView {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(summary.text ?? "").textSelection(.enabled)
                        if summary.isStale {
                            Text("A newer version exists — the summary will update shortly.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
            default: // "none"
                VStack(spacing: 12) {
                    Text("No summary yet.").foregroundStyle(.secondary)
                    Text("One will be generated automatically.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load summary").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                Button("Retry") { Task { await load() } }
            }
        }
    }

    private var editor: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Edit summary").font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $editDraft)
                .frame(maxHeight: .infinity)
                .overlay(alignment: .topLeading) {
                    if isSaving { ProgressView().padding(6) }
                }
        }
    }

    private func load() async {
        isLoading = true
        do {
            summary = try await FabulisAPIClient.shared.storySummary(id: storyId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func beginEdit() {
        editDraft = summary?.text ?? ""
        isEditing = true
    }

    private func saveEdit() async {
        isSaving = true; defer { isSaving = false }
        do {
            summary = try await FabulisAPIClient.shared.updateStorySummary(
                id: storyId,
                text: editDraft.trimmingCharacters(in: .whitespacesAndNewlines))
            isEditing = false
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func regenerate() async {
        do {
            try await FabulisAPIClient.shared.regenerateStorySummary(id: storyId)
            await load()
            startPollingIfNeeded()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// While the server reports "generating", re-fetch every few seconds
    /// until the summary settles.
    private func startPollingIfNeeded() {
        pollTask?.cancel()
        guard summary?.status == "generating" else { return }
        pollTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(3))
                if Task.isCancelled { return }
                let latest = try? await FabulisAPIClient.shared.storySummary(id: storyId)
                if let latest {
                    summary = latest
                    if latest.status != "generating" { return }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Add the toolbar button + sheet to `StoryView`**

In `client/Fabulis/Views/Story/StoryView.swift`, add state near the top:

```swift
    @State private var showingSummary = false
```

In the `.toolbar { ... }`, add a second `ToolbarItem` (alongside the existing version `Menu`), shown once the story detail is loaded:

```swift
                if detail != nil {
                    ToolbarItem(placement: .topBarTrailing) {
                        Button {
                            showingSummary = true
                        } label: {
                            Image(systemName: "text.quote")
                        }
                        .accessibilityLabel("Summary")
                    }
                }
```

Add the sheet modifier on the same view that holds `.toolbar` (e.g. after `.safeAreaInset(...)` / before `.task`):

```swift
        .sheet(isPresented: $showingSummary) {
            StorySummarySheet(storyId: storyId)
        }
```

- [ ] **Step 3: Verify the client builds**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Manual end-to-end check**

Run the server, launch the client in the simulator, unlock, open a story with at least one saved version. Tap the summary (quote) button:
- If no summary yet → "No summary yet"; within ~30s the sheet (when reopened or via the generating poll) shows the generated paragraph.
- Edit → change text → Save → text persists on reopen.
- Regenerate → status shows "Generating…", then a fresh paragraph.
- Confirm Settings → Story summaries shows the prompt and lets you change the model.

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Story/StorySummarySheet.swift client/Fabulis/Views/Story/StoryView.swift
git commit -m "Add story summary sheet and toolbar access"
```

---

## Task 9: Update BACKLOG + docs

**Files:**
- Modify: `BACKLOG.md`

- [ ] **Step 1: Note the deferred follow-ups**

Add to `BACKLOG.md` under "Functional gaps":

```markdown
### Summary failure backoff

`SummaryService` retries a failed story on every sweep (~30s) with no
backoff. For a persistently failing story (bad model id, API outage)
this re-hits the model each cycle. Acceptable for single-user LAN use;
add exponential backoff / a max-attempts cap if it becomes noisy.

Originally deferred in the story-summaries plan
(`docs/superpowers/plans/2026-06-17-story-summaries.md`).
```

- [ ] **Step 2: Commit**

```bash
git add BACKLOG.md
git commit -m "Note summary failure backoff in backlog"
```

---

## Self-review notes (spec coverage)

- **Dedicated SummaryModel (fallback AssistantModel)** → Task 4 (settings) + Task 3 `ResolveModelAndPromptAsync`.
- **Editable SummaryPrompt with default** → `StorySummary.DefaultPrompt` (Task 1), settings (Task 4), UI (Task 7).
- **Both sweep + on-save enqueue** → Task 3 (sweep loop) + Task 5 step 3 (enqueue).
- **Incremental fold; first-time = single version; manual regenerate = full rebuild** → Task 3 `ProcessStoryAsync` (`fullRebuild`) + `StorySummary.ComposeUserMessage`.
- **Manual edits refined as base (not locked)** → PUT sets `SummarizedThroughVersion = latest` (Task 5); a later version makes it stale and folds the edited text as `priorSummary`.
- **Response-only text** → `StorySummary.BuildVersionBody` (Task 1).
- **Status without persisted "generating"** → in-memory `_inFlight` set drives `ToSummaryDto` (Tasks 3, 5).
- **Hidden-by-default UI; toolbar button → sheet with view/edit/regenerate + polling** → Task 8.
- **Per-story columns, hand-written SQL schema** → Task 2.
- **API GET/PUT/regenerate** → Task 5.
```
