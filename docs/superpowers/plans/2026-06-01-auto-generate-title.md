# Auto-generate Story Title Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Generate" button to the Save Draft dialog that produces a story title from the draft's story text using the storyteller's model, driven by an editable "titling prompt" that ships with a sensible default.

**Architecture:** Server gains a `TitlingPrompt` field on the single `Storyteller` row (persisted via idempotent raw-SQL schema update) and a one-shot `POST /api/v1/drafts/{id}/generate-title` endpoint that joins the draft's assistant responses, calls the existing `OpenRouterService.ChatAsync`, and returns a cleaned title. The SwiftUI client surfaces the titling prompt in the storyteller editor and a Generate button in the save sheet.

**Tech Stack:** ASP.NET Core minimal APIs on .NET 10, EF Core + SQLite, xUnit; SwiftUI client (iOS 18.5+/Mac Catalyst).

---

## File Structure

**Server (`src/Fabulis.Server/`):**
- `Data/TitleGeneration.cs` — **new.** Pure helpers: `BuildStoryBody(messages)` and `CleanTitle(raw)`. Unit-tested.
- `Data/Storyteller.cs` — **modify.** Add `TitlingPrompt` property + `DefaultTitlingPrompt` constant.
- `Data/FabulisDbContext.cs` — **modify.** Add `TitlingPrompt` to the `CREATE TABLE`, an idempotent `ALTER TABLE … ADD COLUMN`, and set it when seeding.
- `Api/Dtos.cs` — **modify.** Add `TitlingPrompt` to `StorytellerDto`/`StorytellerUpdateRequest`; add `GenerateTitleResponse`.
- `Api/StorytellerEndpoints.cs` — **modify.** Read/write `TitlingPrompt`.
- `Api/DraftEndpoints.cs` — **modify.** Add the `generate-title` endpoint.

**Server tests (`tests/Fabulis.Server.Tests/`):**
- `TitleGenerationTests.cs` — **new.** xUnit tests for both helpers.

**Client (`client/Fabulis/`):**
- `Models/APIDtos.swift` — **modify.** Add `titlingPrompt` to the two storyteller DTOs; add `GenerateTitleResponse`.
- `Services/FabulisAPIClient.swift` — **modify.** Add `generateTitle(draftId:)`.
- `Views/Settings/StorytellerEditorView.swift` — **modify.** Add a "Titling prompt" section.
- `Views/Draft/SaveDraftSheet.swift` — **modify.** Add the Generate button.

---

## Task 1: `TitleGeneration` helpers (pure, TDD)

**Files:**
- Create: `src/Fabulis.Server/Data/TitleGeneration.cs`
- Test: `tests/Fabulis.Server.Tests/TitleGenerationTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Fabulis.Server.Tests/TitleGenerationTests.cs`:

```csharp
using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class TitleGenerationTests
{
    [Fact]
    public void BuildStoryBodyJoinsOnlyResponsesInSortOrder()
    {
        var messages = new List<DraftMessage>
        {
            new() { Content = "second response", Role = MessageRole.Response, SortOrder = 3 },
            new() { Content = "the user prompt", Role = MessageRole.Prompt, SortOrder = 0 },
            new() { Content = "first response", Role = MessageRole.Response, SortOrder = 1 },
        };

        Assert.Equal("first response\n\nsecond response", TitleGeneration.BuildStoryBody(messages));
    }

    [Fact]
    public void BuildStoryBodyReturnsEmptyWhenNoResponses()
    {
        var messages = new List<DraftMessage>
        {
            new() { Content = "only a prompt", Role = MessageRole.Prompt, SortOrder = 0 },
        };

        Assert.Equal("", TitleGeneration.BuildStoryBody(messages));
    }

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("  Hello World  ", "Hello World")]
    [InlineData("\"Hello World\"", "Hello World")]
    [InlineData("'Hello World'", "Hello World")]
    [InlineData("“Hello World”", "Hello World")]
    [InlineData("Hello World\n\nextra commentary", "Hello World")]
    [InlineData("\n\n  \"The Quiet Hour\"  ", "The Quiet Hour")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CleanTitleNormalizesModelOutput(string raw, string expected)
    {
        Assert.Equal(expected, TitleGeneration.CleanTitle(raw));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: FAIL to **compile** with "The name 'TitleGeneration' does not exist".

- [ ] **Step 3: Write the implementation**

Create `src/Fabulis.Server/Data/TitleGeneration.cs`:

```csharp
namespace Fabulis.Server.Data;

/// <summary>
/// Pure helpers for turning a draft's story text into a title. The
/// LLM call itself lives in the generate-title endpoint; everything
/// here is deterministic and unit-tested.
/// </summary>
public static class TitleGeneration
{
    /// <summary>
    /// Joins the assistant-generated story responses (in sort order),
    /// ignoring user prompts. Returns "" when there is no story yet.
    /// </summary>
    public static string BuildStoryBody(IEnumerable<DraftMessage> messages) =>
        string.Join("\n\n", messages
            .Where(m => m.Role == MessageRole.Response)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.Content));

    /// <summary>
    /// Normalizes a model's title output: takes the first non-empty line
    /// and strips a single pair of surrounding quotes.
    /// </summary>
    public static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var firstLine = raw
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";

        return TrimSurroundingQuotes(firstLine);
    }

    private static string TrimSurroundingQuotes(string s)
    {
        if (s.Length < 2) return s;
        char first = s[0], last = s[^1];
        bool matched =
            (first == '"' && last == '"') ||
            (first == '\'' && last == '\'') ||
            (first == '“' && last == '”'); // “ … ”
        return matched ? s[1..^1].Trim() : s;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: PASS (all `TitleGenerationTests` green, existing tests still green).

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Data/TitleGeneration.cs tests/Fabulis.Server.Tests/TitleGenerationTests.cs
git commit -m "Add TitleGeneration helpers for story-body and title cleanup"
```

---

## Task 2: `TitlingPrompt` on the Storyteller entity + schema

**Files:**
- Modify: `src/Fabulis.Server/Data/Storyteller.cs`
- Modify: `src/Fabulis.Server/Data/FabulisDbContext.cs:63-79` (CREATE TABLE), add ALTER, `:122-129` (seed)

- [ ] **Step 1: Add the property and default constant**

In `src/Fabulis.Server/Data/Storyteller.cs`, add the constant at the top of the class and the property after `Prompt`:

```csharp
namespace Fabulis.Server.Data;

public class Storyteller
{
    public const string DefaultTitlingPrompt =
        "You write titles for stories. Given the full text of a story, respond with a single short, evocative title — 2 to 6 words. Output only the title itself: no quotation marks, no trailing punctuation, no commentary.";

    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
    public required string TitlingPrompt { get; set; }
    public required string ModelName { get; set; }
    public double Temperature { get; set; } = 0.7;
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public double? MinP { get; set; }
    public int? TopK { get; set; }
    public double? TopA { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

> Note: `DefaultTitlingPrompt` must not contain a single-quote (`'`) character — it is interpolated into raw SQL `DEFAULT '…'` clauses in the next step. The text above is apostrophe-free; keep it that way.

- [ ] **Step 2: Add the column to CREATE TABLE and seed it**

In `src/Fabulis.Server/Data/FabulisDbContext.cs`, change the Storytellers `CREATE TABLE` (currently lines 65-79) to an **interpolated** raw string that includes `TitlingPrompt`. Replace the existing block:

```csharp
        await Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS Storytellers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Prompt TEXT NOT NULL,
                TitlingPrompt TEXT NOT NULL DEFAULT '{Storyteller.DefaultTitlingPrompt}',
                ModelName TEXT NOT NULL,
                Temperature REAL NOT NULL DEFAULT 0.7,
                TopP REAL NULL,
                MaxTokens INTEGER NULL,
                MinP REAL NULL,
                TopK INTEGER NULL,
                TopA REAL NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            )
            """);
```

(The only changes vs. the original are the `$` prefix and the new `TitlingPrompt` line.)

Then in `SeedDefaultStorytellerIfMissingAsync` (lines 122-129), set `TitlingPrompt` on the seeded row:

```csharp
        Storytellers.Add(new Storyteller
        {
            Name = "Storyteller",
            Prompt = "You are a helpful storyteller.",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = string.IsNullOrWhiteSpace(assistantModel) ? "anthropic/claude-sonnet-4" : assistantModel,
            Temperature = 0.7,
            CreatedAt = DateTime.UtcNow,
        });
```

- [ ] **Step 3: Add the idempotent ALTER for existing vaults**

In `EnsureSchemaUpdatedAsync`, immediately **after** the Storytellers `CREATE TABLE` block and **before** the `AppSettings` CREATE TABLE, insert:

```csharp
        // Storytellers gained TitlingPrompt after the initial release.
        // CREATE TABLE IF NOT EXISTS above never alters an existing table,
        // so add the column on vaults created before this field existed.
        var storytellerColumns = await Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Storytellers')")
            .ToListAsync();
        if (!storytellerColumns.Contains("TitlingPrompt"))
        {
            await Database.ExecuteSqlRawAsync(
                $"ALTER TABLE Storytellers ADD COLUMN TitlingPrompt TEXT NOT NULL DEFAULT '{Storyteller.DefaultTitlingPrompt}'");
        }
```

> `SqlQueryRaw<string>` requires the projected column to be named `Value`; the `AS Value` alias handles that. `Microsoft.EntityFrameworkCore` is already imported in this file.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded. (The `required TitlingPrompt` forces every `new Storyteller { … }` to set it — the seed in Step 2 is the only construction site in the server; if the build flags another, add `TitlingPrompt = Storyteller.DefaultTitlingPrompt`.)

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Data/Storyteller.cs src/Fabulis.Server/Data/FabulisDbContext.cs
git commit -m "Persist TitlingPrompt on the storyteller with a default"
```

---

## Task 3: Storyteller DTOs + endpoints carry `TitlingPrompt`

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs:85-106`
- Modify: `src/Fabulis.Server/Api/StorytellerEndpoints.cs:19-48`

- [ ] **Step 1: Add `TitlingPrompt` to both storyteller DTOs**

In `src/Fabulis.Server/Api/Dtos.cs`, add `string TitlingPrompt` to both records (placed after `Prompt`):

```csharp
public sealed record StorytellerDto(
    int Id,
    string Name,
    string Prompt,
    string TitlingPrompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);

public sealed record StorytellerUpdateRequest(
    string Name,
    string Prompt,
    string TitlingPrompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);
```

- [ ] **Step 2: Read and write `TitlingPrompt` in the endpoints**

In `src/Fabulis.Server/Api/StorytellerEndpoints.cs`, update the `GET` response (line 19) to include `s.TitlingPrompt`:

```csharp
            return Results.Ok(new StorytellerDto(
                s.Id, s.Name, s.Prompt, s.TitlingPrompt, s.ModelName,
                s.Temperature, s.TopP, s.MaxTokens, s.MinP, s.TopK, s.TopA));
```

In the `PUT` handler, persist it (add after the `s.Prompt = body.Prompt;` line, ~line 38):

```csharp
            s.Prompt = body.Prompt;
            s.TitlingPrompt = body.TitlingPrompt;
```

> Leave the existing validation as-is. `TitlingPrompt` is allowed to be empty (the client always sends the default), so it is intentionally not added to the required-field check.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs src/Fabulis.Server/Api/StorytellerEndpoints.cs
git commit -m "Carry TitlingPrompt through the storyteller API"
```

---

## Task 4: `generate-title` endpoint

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs` (add `GenerateTitleResponse`)
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs:73` (add endpoint near `/save`)

- [ ] **Step 1: Add the response DTO**

In `src/Fabulis.Server/Api/Dtos.cs`, in the `// ---------- drafts ----------` section (e.g. right after `SaveDraftResponse`), add:

```csharp
public sealed record GenerateTitleResponse(string Title);
```

- [ ] **Step 2: Add the endpoint**

In `src/Fabulis.Server/Api/DraftEndpoints.cs`, add this inside `MapDraftEndpoints`, immediately after the `/{id:int}/save` endpoint block (after line 105, before the `MapDelete(".../messages/{messageId}")` block):

```csharp
        group.MapPost("/{id:int}/generate-title", async (
            int id,
            DraftService drafts,
            OpenRouterService openRouter) =>
        {
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            var body = TitleGeneration.BuildStoryBody(draft.Messages);
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { error = "the draft has no story content to title yet" });

            var raw = await openRouter.ChatAsync(
                draft.Storyteller.ModelName,
                draft.Storyteller.TitlingPrompt,
                body,
                temperature: 0.3,
                maxTokens: 32);

            return Results.Ok(new GenerateTitleResponse(TitleGeneration.CleanTitle(raw)));
        });
```

> `DraftService`, `OpenRouterService`, and `TitleGeneration` are all in the already-imported `Fabulis.Server.Data` namespace. `GetDraftAsync` eager-loads `Storyteller` and ordered `Messages` (see `DraftService.cs:30-35`), so no extra loading is needed.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Manually verify the endpoint end-to-end**

This requires a configured vault + OpenRouter API key. Start the server and exercise the flow:

```bash
dotnet run --project src/Fabulis.Server
```

Then, in another shell (replace `<pw>` with the vault password and `<draftId>` with a draft that has at least one generated story response):

```bash
TOKEN=$(curl -s -X POST http://localhost:5288/api/v1/auth/unlock \
  -H 'Content-Type: application/json' -d '{"password":"<pw>"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["sessionToken"])')
curl -s -X POST http://localhost:5288/api/v1/drafts/<draftId>/generate-title \
  -H "Authorization: Bearer $TOKEN"
```

Expected: `{"title":"…"}` with a short, quote-free title. Hitting a draft with no responses returns HTTP 400 with the "no story content" error.

> The exact unlock-response field name (`sessionToken`) and auth header scheme can be confirmed against `Api/AuthEndpoints.cs` if the command errors. This is a manual smoke test; if no API key is configured, note that and defer to the in-app verification in Task 7.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs src/Fabulis.Server/Api/DraftEndpoints.cs
git commit -m "Add POST /drafts/{id}/generate-title endpoint"
```

---

## Task 5: Client DTOs + API method

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift:150-173`
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift` (near `getStoryteller`, ~line 295)

- [ ] **Step 1: Add `titlingPrompt` to the storyteller DTOs and a response DTO**

In `client/Fabulis/Models/APIDtos.swift`, add `titlingPrompt` (after `prompt`) to both structs and add the response struct:

```swift
struct StorytellerDto: Decodable, Sendable {
    let id: Int
    let name: String
    let prompt: String
    let titlingPrompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct StorytellerUpdateRequest: Encodable, Sendable {
    let name: String
    let prompt: String
    let titlingPrompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct GenerateTitleResponse: Decodable, Sendable {
    let title: String
}
```

> The decoder uses snake_case conversion (matching the server's camelCase JSON via its key strategy) consistent with the existing DTOs — `titlingPrompt` maps to the server's `titlingPrompt` exactly as `modelName` already does. No `CodingKeys` needed.

- [ ] **Step 2: Add the API method**

In `client/Fabulis/Services/FabulisAPIClient.swift`, add right after `updateStoryteller` (~line 302):

```swift
    func generateTitle(draftId: Int) async throws -> String {
        let resp: GenerateTitleResponse = try await request(
            "POST", path: "/drafts/\(draftId)/generate-title", authed: true)
        return resp.title
    }
```

> This uses the existing `request<T: Decodable>(_:path:authed:timeout:)` overload (line 408), which sends no body — correct for this endpoint.

- [ ] **Step 3: Build the client to verify it compiles**

Run:
```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build
```
Expected: `** BUILD SUCCEEDED **`.

> If the scheme or simulator name differs, list schemes with `xcodebuild -project client/Fabulis.xcodeproj -list` and available simulators with `xcrun simctl list devices available`, then substitute.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "Add client titlingPrompt fields and generateTitle API"
```

---

## Task 6: Titling-prompt section in the storyteller editor

**Files:**
- Modify: `client/Fabulis/Views/Settings/StorytellerEditorView.swift`

- [ ] **Step 1: Add state, UI, load, and save wiring**

In `client/Fabulis/Views/Settings/StorytellerEditorView.swift`:

Add a state var after `prompt` (line 6):

```swift
    @State private var prompt: String = ""
    @State private var titlingPrompt: String = ""
```

Add a new section after the "System prompt" section (after line 25):

```swift
            Section("System prompt") {
                TextEditor(text: $prompt).frame(minHeight: 120)
            }
            Section("Titling prompt") {
                TextEditor(text: $titlingPrompt).frame(minHeight: 100)
            }
```

In `load()`, set it after `prompt = s.prompt` (line 75):

```swift
            prompt = s.prompt
            titlingPrompt = s.titlingPrompt
```

In `save()`, pass it in the `StorytellerUpdateRequest` (after `prompt: prompt,`, line 93):

```swift
                prompt: prompt,
                titlingPrompt: titlingPrompt,
```

> Leave `canSave` unchanged — the titling prompt is allowed to be blank.

- [ ] **Step 2: Build the client to verify it compiles**

Run:
```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build
```
Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Settings/StorytellerEditorView.swift
git commit -m "Add editable titling prompt to the storyteller editor"
```

---

## Task 7: Generate button in the Save Draft sheet

**Files:**
- Modify: `client/Fabulis/Views/Draft/SaveDraftSheet.swift`

- [ ] **Step 1: Add the in-progress state**

In `client/Fabulis/Views/Draft/SaveDraftSheet.swift`, add a state var after `isSaving` (line 14):

```swift
    @State private var isSaving = false
    @State private var isGeneratingTitle = false
```

- [ ] **Step 2: Add the Generate button to the Story title section**

Replace the existing "Story title" section (lines 45-49):

```swift
                if selectedStoryId == nil {
                    Section("Story title") {
                        TextField("Story title", text: $newStoryTitle)
                        Button {
                            Task { await generateTitle() }
                        } label: {
                            if isGeneratingTitle {
                                HStack { ProgressView(); Text("Generating…") }
                            } else {
                                Label("Generate", systemImage: "sparkles")
                            }
                        }
                        .disabled(isGeneratingTitle)
                    }
                }
```

- [ ] **Step 3: Add the `generateTitle()` method**

Add this method after `loadStories(in:)` (after line 97), before `save()`:

```swift
    private func generateTitle() async {
        errorMessage = nil
        isGeneratingTitle = true
        defer { isGeneratingTitle = false }
        do {
            let title = try await FabulisAPIClient.shared.generateTitle(draftId: draftId)
            newStoryTitle = title
        } catch {
            errorMessage = error.localizedDescription
        }
    }
```

- [ ] **Step 4: Build the client to verify it compiles**

Run:
```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build
```
Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 5: Manual end-to-end verification**

With the server running (`dotnet run --project src/Fabulis.Server`) and the app running against it:
1. Open or generate a draft that has at least one story response.
2. Tap Save to open the Save Draft sheet; choose/enter a category and leave "— New story —" selected so the Story title field shows.
3. Tap **Generate** → the field fills with a short title; the button shows a spinner while working.
4. Edit the title if desired, then Save — confirm the story is saved under that title.
5. In Settings → Storyteller, edit the **Titling prompt**, save, regenerate a title, and confirm the new prompt changes the result.

Expected: title generation works, the prompt is editable and takes effect, and a draft with no responses surfaces the "no story content" error rather than crashing.

- [ ] **Step 6: Commit**

```bash
git add client/Fabulis/Views/Draft/SaveDraftSheet.swift
git commit -m "Add Generate-title button to the Save Draft sheet"
```

---

## Final verification

- [ ] Server: `dotnet build Fabulis.slnx` → Build succeeded.
- [ ] Server: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj` → all green.
- [ ] Client: `xcodebuild … build` → BUILD SUCCEEDED.
- [ ] Manual flow from Task 7 Step 5 passes.
- [ ] Delete this plan's BACKLOG entry if one exists (none expected for new work).
