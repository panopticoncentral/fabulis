# Additional sampling parameters (min_p, top_k, top_a)

## Problem

Storytellers currently expose only `Temperature`, `TopP`, and `MaxTokens` to the underlying model. OpenRouter supports several additional sampling knobs — notably `min_p`, `top_k`, and `top_a` — that meaningfully affect creative output. Users tuning a storyteller for a particular style can't reach them today.

## Goal

Let each storyteller configure `min_p`, `top_k`, and `top_a` alongside the existing sampling parameters. Values are optional; unset values are omitted from the API call so the provider applies its default.

## Non-goals

- No other OpenRouter parameters in this pass (`frequency_penalty`, `presence_penalty`, `repetition_penalty`, `seed`, `stop`, `reasoning`, `provider`, `transforms`, `models`, `tools`). Easy to add later using the same pattern.
- No backward compatibility with existing databases. Option B was chosen: the user will delete `src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db` before running the new build.
- No UI reorganization (e.g. "Advanced" disclosure). The form stays flat with three more fields under Max Tokens.

## Architecture

### Entity — `src/Fabulis.Server/Data/Storyteller.cs`

Three nullable properties following the `TopP`/`MaxTokens` style:

```csharp
public double? MinP { get; set; }
public int? TopK { get; set; }
public double? TopA { get; set; }
```

### Schema — `src/Fabulis.Server/Data/FabulisDbContext.cs`

Extend the `Storytellers` CREATE TABLE in `EnsureSchemaUpdatedAsync`:

```sql
MinP REAL NULL,
TopK INTEGER NULL,
TopA REAL NULL,
```

No `ALTER TABLE` logic — per Option B, the existing DB file is deleted before the first run of the new build.

### Service — `src/Fabulis.Server/Data/OpenRouterService.cs`

Add three optional parameters to both `ChatAsync` and `ChatStreamAsync`:

```csharp
double? minP = null, int? topK = null, double? topA = null
```

Inside each method, add to `requestBody` only when `.HasValue`, using the OpenRouter wire names:

```csharp
if (minP.HasValue) requestBody["min_p"] = minP.Value;
if (topK.HasValue) requestBody["top_k"] = topK.Value;
if (topA.HasValue) requestBody["top_a"] = topA.Value;
```

`ChatAsync` is only called from the prompt-generator on `StorytellerPage` and does not need to pass storyteller-specific sampling. The service signatures are kept symmetric purely for API consistency.

### UI — `src/Fabulis.Server/Components/Pages/StorytellerPage.razor`

Three `<div class="form-group">` blocks inserted immediately after Max Tokens, same "Optional. Leave empty for provider default." hint:

| Field  | Input attributes                        |
|--------|-----------------------------------------|
| Min P  | `step="0.01" min="0" max="1"`           |
| Top K  | integer, `min="0"`, no upper bound      |
| Top A  | `step="0.01" min="0" max="1"`           |

Three corresponding `[SupplyParameterFromForm]` properties:

```csharp
[SupplyParameterFromForm] private double? MinP { get; set; }
[SupplyParameterFromForm] private int?    TopK { get; set; }
[SupplyParameterFromForm] private double? TopA { get; set; }
```

`OnInitializedAsync` — load when editing an existing storyteller:

```csharp
MinP ??= Storyteller.MinP;
TopK ??= Storyteller.TopK;
TopA ??= Storyteller.TopA;
```

Save handler — write the three fields to both the new-entity path and the update path, mirroring how `MaxTokens` is handled today.

### Draft flow — `src/Fabulis.Server/Components/Pages/DraftPage.razor`

Pass the three fields from `storyteller` into the existing `ChatStreamAsync` call, alongside `storyteller.MaxTokens`:

```csharp
OpenRouter.ChatStreamAsync(
    storyteller.ModelName,
    storyteller.Prompt,
    messages,
    storyteller.Temperature,
    storyteller.TopP,
    storyteller.MaxTokens,
    storyteller.MinP,
    storyteller.TopK,
    storyteller.TopA,
    Cts.Token)
```

## Error handling

No new error paths. If the provider rejects a combination of sampling parameters, that surfaces through the existing `ErrorMessage` path in `DraftPage`.

## Testing

Manual verification (no test suite):

1. Create a new storyteller leaving Min P / Top K / Top A blank → generation works; inspect the request body (e.g. via OpenRouter dashboard or a proxy) and confirm the three fields are absent.
2. Edit the storyteller, set Min P=0.05, Top K=40, Top A=0.2, save → reload the edit page and confirm the values round-trip from the database.
3. Generate a draft → confirm the request body now contains `min_p: 0.05`, `top_k: 40`, `top_a: 0.2`.
4. Clear Top K back to blank, save, generate → confirm `top_k` is absent from the request again, but `min_p` and `top_a` still present.

## Out of scope / follow-ups

- Additional OpenRouter parameters (`frequency_penalty`, `presence_penalty`, `seed`, `stop`, etc.). Add in follow-up passes using the same pattern established here.
- An "Advanced parameters" collapsible section once the form has more than ~8 fields.
- Context-window awareness (reading `context_length` from `/models` and warning when conversation history approaches it). Separate concern.
