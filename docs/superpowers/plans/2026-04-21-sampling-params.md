# Additional Sampling Parameters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `min_p`, `top_k`, and `top_a` as per-storyteller sampling parameters that get forwarded to OpenRouter when set.

**Architecture:** Bottom-up change through the existing layers — entity → schema → service → UI → draft-flow. All three parameters are optional (nullable); they're omitted from the API request when unset so the provider applies its default. Follows the same pattern already established for `TopP` and `MaxTokens`.

**Tech Stack:** ASP.NET Core / .NET 10, Blazor Server, EF Core with SQLite + SQLCipher. No test framework is configured for this project — verification is via `dotnet build` plus manual smoke testing, consistent with every prior feature in this repo (see `docs/superpowers/specs/2026-04-21-cancel-generation-design.md` for precedent).

**Spec:** [docs/superpowers/specs/2026-04-21-sampling-params-design.md](../specs/2026-04-21-sampling-params-design.md)

---

### Task 1: Add entity properties and schema columns

Add the three nullable fields to the `Storyteller` entity and the corresponding columns to the raw-SQL schema. These are paired changes and ship in one commit.

**Files:**
- Modify: `src/Fabulis.Server/Data/Storyteller.cs`
- Modify: `src/Fabulis.Server/Data/FabulisDbContext.cs:36-47`

- [ ] **Step 1: Add three properties to `Storyteller`**

Open `src/Fabulis.Server/Data/Storyteller.cs`. After the existing `MaxTokens` property (line 11), insert:

```csharp
public double? MinP { get; set; }
public int? TopK { get; set; }
public double? TopA { get; set; }
```

Full expected file:

```csharp
namespace Fabulis.Server.Data;

public class Storyteller
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
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

- [ ] **Step 2: Add columns to the `Storytellers` CREATE TABLE**

In `src/Fabulis.Server/Data/FabulisDbContext.cs`, replace the first `ExecuteSqlRawAsync` block (lines 36-47) with:

```csharp
await Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS Storytellers (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        Prompt TEXT NOT NULL,
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

- [ ] **Step 3: Build and verify**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeds with no errors. No callers reference the new properties yet, so there are no usage sites to adjust.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Data/Storyteller.cs src/Fabulis.Server/Data/FabulisDbContext.cs
git commit -m "$(cat <<'EOF'
Add MinP, TopK, TopA properties and schema columns to Storyteller

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Extend OpenRouterService to forward the new parameters

Add `minP`, `topK`, `topA` optional parameters to both public methods and include them in the request body only when the caller supplied a value. Uses the OpenRouter wire names `min_p`, `top_k`, `top_a`.

**Files:**
- Modify: `src/Fabulis.Server/Data/OpenRouterService.cs:15-16` (signature of `ChatAsync`)
- Modify: `src/Fabulis.Server/Data/OpenRouterService.cs:35-38` (request-body block of `ChatAsync`)
- Modify: `src/Fabulis.Server/Data/OpenRouterService.cs:54-56` (signature of `ChatStreamAsync`)
- Modify: `src/Fabulis.Server/Data/OpenRouterService.cs:85-88` (request-body block of `ChatStreamAsync`)

- [ ] **Step 1: Update `ChatAsync` signature**

Replace lines 15-16 of `OpenRouterService.cs` with:

```csharp
public async Task<string> ChatAsync(string model, string systemPrompt, string userMessage,
    double temperature = 0.7, double? topP = null, int? maxTokens = null,
    double? minP = null, int? topK = null, double? topA = null)
```

- [ ] **Step 2: Update `ChatAsync` request-body block**

Replace lines 35-38 (the current `if (topP.HasValue) ... if (maxTokens.HasValue) ...` block) with:

```csharp
if (topP.HasValue)
    requestBody["top_p"] = topP.Value;
if (maxTokens.HasValue)
    requestBody["max_tokens"] = maxTokens.Value;
if (minP.HasValue)
    requestBody["min_p"] = minP.Value;
if (topK.HasValue)
    requestBody["top_k"] = topK.Value;
if (topA.HasValue)
    requestBody["top_a"] = topA.Value;
```

- [ ] **Step 3: Update `ChatStreamAsync` signature**

Replace lines 54-56 with:

```csharp
public async IAsyncEnumerable<string> ChatStreamAsync(string model, string systemPrompt,
    List<DraftMessage> messages, double temperature = 0.7, double? topP = null, int? maxTokens = null,
    double? minP = null, int? topK = null, double? topA = null,
    [EnumeratorCancellation] CancellationToken ct = default)
```

- [ ] **Step 4: Update `ChatStreamAsync` request-body block**

Replace lines 85-88 (the current `if (topP.HasValue) ... if (maxTokens.HasValue) ...` block) with:

```csharp
if (topP.HasValue)
    requestBody["top_p"] = topP.Value;
if (maxTokens.HasValue)
    requestBody["max_tokens"] = maxTokens.Value;
if (minP.HasValue)
    requestBody["min_p"] = minP.Value;
if (topK.HasValue)
    requestBody["top_k"] = topK.Value;
if (topA.HasValue)
    requestBody["top_a"] = topA.Value;
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeds. Existing callers (`StorytellerPage.GeneratePrompt` and `DraftPage.SubmitToLLM`) don't pass the new parameters, which is fine because all three have defaults of `null`.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Data/OpenRouterService.cs
git commit -m "$(cat <<'EOF'
Forward min_p, top_k, top_a to OpenRouter when set

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Thread the new parameters through `DraftPage.SubmitToLLM`

Pass the storyteller's `MinP`, `TopK`, `TopA` into the streaming call so draft generations actually use them.

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/DraftPage.razor:274-281`

- [ ] **Step 1: Update the `ChatStreamAsync` call**

Replace the existing call (lines 274-281):

```csharp
await foreach (var chunk in OpenRouter.ChatStreamAsync(
    storyteller.ModelName,
    storyteller.Prompt,
    CurrentDraft.Messages.ToList(),
    storyteller.Temperature,
    storyteller.TopP,
    storyteller.MaxTokens,
    Cts.Token))
```

With:

```csharp
await foreach (var chunk in OpenRouter.ChatStreamAsync(
    storyteller.ModelName,
    storyteller.Prompt,
    CurrentDraft.Messages.ToList(),
    storyteller.Temperature,
    storyteller.TopP,
    storyteller.MaxTokens,
    storyteller.MinP,
    storyteller.TopK,
    storyteller.TopA,
    Cts.Token))
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/DraftPage.razor
git commit -m "$(cat <<'EOF'
Pass storyteller min_p, top_k, top_a into draft generation stream

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Add form fields, form-bound properties, and load/save wiring in `StorytellerPage`

Add the three optional inputs to the edit form, with form-bound properties, round-trip load in `OnInitializedAsync`, and persistence in both the new-entity and update branches of `Save`.

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor:111-115` (form markup — insert new groups after Max Tokens)
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor:160-161` (form-bound properties — insert after `MaxTokens`)
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor:200-202` (OnInitializedAsync load)
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor:268-277` (Save — new-entity branch)
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor:283-288` (Save — update branch)

- [ ] **Step 1: Add form input groups for Min P, Top K, Top A**

Find the existing Max Tokens form group (starting at line 111) ending with `</div>` at line 115. Immediately after that closing `</div>`, insert:

```razor
            <div class="form-group">
                <label for="minp">Min P</label>
                <InputNumber id="minp" @bind-Value="MinP" step="0.01" min="0" max="1" />
                <p class="form-hint">Optional. Leave empty for provider default.</p>
            </div>

            <div class="form-group">
                <label for="topk">Top K</label>
                <InputNumber id="topk" @bind-Value="TopK" min="0" />
                <p class="form-hint">Optional. Leave empty for provider default.</p>
            </div>

            <div class="form-group">
                <label for="topa">Top A</label>
                <InputNumber id="topa" @bind-Value="TopA" step="0.01" min="0" max="1" />
                <p class="form-hint">Optional. Leave empty for provider default.</p>
            </div>
```

- [ ] **Step 2: Add form-bound properties**

Find the existing `MaxTokens` form property (lines 160-161):

```csharp
    [SupplyParameterFromForm]
    private int? MaxTokens { get; set; }
```

Immediately after it, insert:

```csharp

    [SupplyParameterFromForm]
    private double? MinP { get; set; }

    [SupplyParameterFromForm]
    private int? TopK { get; set; }

    [SupplyParameterFromForm]
    private double? TopA { get; set; }
```

- [ ] **Step 3: Load values in `OnInitializedAsync`**

Find the load block (lines 197-202) and extend it so the final version reads:

```csharp
            Storyteller = await Db.Storytellers.FindAsync(Id);
            if (Storyteller is not null)
            {
                Name ??= Storyteller.Name;
                Prompt ??= Storyteller.Prompt;
                ModelName ??= Storyteller.ModelName;
                Temperature = Storyteller.Temperature;
                TopP ??= Storyteller.TopP;
                MaxTokens ??= Storyteller.MaxTokens;
                MinP ??= Storyteller.MinP;
                TopK ??= Storyteller.TopK;
                TopA ??= Storyteller.TopA;
            }
```

- [ ] **Step 4: Persist values in the new-entity branch of `Save`**

Replace the existing `Db.Storytellers.Add(new Storyteller { ... })` block (lines 268-277) with:

```csharp
            Db.Storytellers.Add(new Storyteller
            {
                Name = Name.Trim(),
                Prompt = Prompt.Trim(),
                ModelName = ModelName.Trim(),
                Temperature = Temperature,
                TopP = TopP,
                MaxTokens = MaxTokens,
                MinP = MinP,
                TopK = TopK,
                TopA = TopA,
                CreatedAt = DateTime.UtcNow
            });
```

- [ ] **Step 5: Persist values in the update branch of `Save`**

Replace the existing update block (lines 283-288) with:

```csharp
            Storyteller.Name = Name.Trim();
            Storyteller.Prompt = Prompt.Trim();
            Storyteller.ModelName = ModelName.Trim();
            Storyteller.Temperature = Temperature;
            Storyteller.TopP = TopP;
            Storyteller.MaxTokens = MaxTokens;
            Storyteller.MinP = MinP;
            Storyteller.TopK = TopK;
            Storyteller.TopA = TopA;
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/StorytellerPage.razor
git commit -m "$(cat <<'EOF'
Expose Min P, Top K, Top A in storyteller edit form

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Wipe the database and run manual verification

Because Option B was chosen (no migration logic), the existing DB must be deleted so the new `CREATE TABLE IF NOT EXISTS` can run with the new columns on first startup.

**Files:** none (runtime only)

- [ ] **Step 1: Stop any running server**

If a `dotnet run` is active, stop it (Ctrl+C in the terminal where it's running).

- [ ] **Step 2: Delete the existing SQLCipher database**

Run: `rm src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db`

The server recreates this file on the next startup. You'll be prompted for a fresh password at `/unlock`.

- [ ] **Step 3: Start the server**

Run: `dotnet run --project src/Fabulis.Server`
Expected: Server listens (no schema-related exceptions in output).

- [ ] **Step 4: Verify round-trip of blank values**

1. Browse to the app and unlock with a fresh password.
2. Create a new storyteller, fill in Name / Prompt / Model, leave Min P / Top K / Top A blank, save.
3. Open the storyteller for edit — confirm Min P / Top K / Top A remain blank.
4. Create a draft from this storyteller and generate a response. Confirm the stream works normally. (Optional deeper check: watch the OpenRouter dashboard activity or sniff the outgoing request; `min_p`, `top_k`, `top_a` should be absent from the JSON body.)

- [ ] **Step 5: Verify round-trip of set values**

1. Edit the same storyteller. Set Min P = `0.05`, Top K = `40`, Top A = `0.2`, save.
2. Re-open the edit page — confirm all three values persisted.
3. Generate a fresh draft response. Confirm streaming still works. (Optional deeper check: the JSON body should now contain `"min_p": 0.05, "top_k": 40, "top_a": 0.2`.)

- [ ] **Step 6: Verify partial clearing**

1. Edit the storyteller. Clear Top K back to empty. Leave Min P and Top A populated. Save.
2. Re-open edit — confirm Top K is blank and the other two still have their values.
3. Generate again. Confirm stream works. (Optional: JSON body contains `min_p` and `top_a` but not `top_k`.)

- [ ] **Step 7: No commit**

Manual verification produces no code changes. If you discovered a bug during verification, open a new task — do not patch silently as part of this one.
