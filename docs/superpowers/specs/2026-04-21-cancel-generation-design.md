# Cancel in-progress generation

## Problem

On the draft page, once the user hits Send (or triggers Regenerate / Save & Resubmit), the LLM response streams in and the user has no way to stop it. If partway through they realize they want to edit the prompt or a prior message, they must wait for the stream to finish. For long generations this is a real friction point.

## Goal

Let the user cancel a streaming generation at any point. Whatever text has already streamed is preserved as a normal Response message — editable, deletable, regeneratable — so the user can immediately go edit the prompt or tweak the partial response.

## Non-goals

- No cancellation for non-streaming calls (`ChatAsync` used by category import). Only the draft conversation loop.
- No "pause/resume". Cancel is terminal.
- No server-side persistence of streaming state across reconnects.

## UX

**Button states on [DraftPage.razor](../../../src/Fabulis.Server/Components/Pages/DraftPage.razor):**

| State | Button label | Action on click | Input textarea |
|-------|-------------|-----------------|----------------|
| Idle, input empty | `Send` (disabled) | — | enabled |
| Idle, input non-empty | `Send` | submit | enabled |
| Streaming | `Stop` | cancel current stream | disabled |

The single submit button toggles between Send and Stop based on `IsStreaming`. No separate button. No keyboard shortcut for stop in v1 — click-only. (Escape-to-cancel is listed under follow-ups.)

**On cancel:**
- Streaming stops immediately.
- If ≥1 chunk was streamed, the accumulated text is saved as a normal `MessageRole.Response` message (identical to a completed stream). The user can then Edit / Delete / Regenerate it like any other response.
- If 0 chunks were streamed (cancelled before anything came back), nothing is saved — the draft returns to its pre-send state. No error shown.
- No "cancelled" marker on the message. The partial response is indistinguishable from a short completed one; the user is the authoritative judge of whether it's usable.

## Architecture

### OpenRouterService

Add a `CancellationToken` parameter (defaulted to `default`) to `ChatStreamAsync`:

```csharp
public async IAsyncEnumerable<string> ChatStreamAsync(
    string model, string systemPrompt, List<DraftMessage> messages,
    double temperature = 0.7, double? topP = null, int? maxTokens = null,
    [EnumeratorCancellation] CancellationToken ct = default)
```

- Pass `ct` to `client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)` so the HTTP request can be aborted during header read.
- Pass `ct` to `reader.ReadLineAsync(ct)` so the per-line read aborts mid-response. `.NET 7+` supports `ReadLineAsync(CancellationToken)` directly.
- No other changes — once cancellation throws `OperationCanceledException`, it propagates out of the `await foreach` naturally.

### DraftPage

New state:
- `private CancellationTokenSource? Cts;` — recreated at the start of each `SubmitToLLM` call, disposed in `finally`.

New method:
```csharp
private void CancelGeneration() => Cts?.Cancel();
```

Modified `SubmitToLLM`:
- Create `Cts = new CancellationTokenSource()` before the try block.
- Pass `Cts.Token` into `ChatStreamAsync(...)`.
- Catch `OperationCanceledException` separately from `Exception`:
  - If `StreamingContent.Length > 0`, call `Drafts.AddMessageAsync(..., MessageRole.Response, StreamingContent.ToString())`, then reload `CurrentDraft`.
  - Do **not** set `ErrorMessage`.
- Generic `Exception` handler stays as-is (sets `ErrorMessage`).
- `finally` block: dispose `Cts`, set `Cts = null`, clear streaming content, set `IsStreaming = false`, `StateHasChanged()`.

Lifetime: `DraftPage` implements `IDisposable`. On dispose, call `Cts?.Cancel()` and `Cts?.Dispose()` so navigating away mid-stream aborts the HTTP request promptly and doesn't try to save a partial message on a disposed component.

### Flows that benefit

All three call sites of `SubmitToLLM` automatically get cancellation:
1. `SendMessage` — fresh prompt + response
2. `RegenerateLastResponse` — delete last response, stream new one
3. `SaveEditAndResubmit` — edit a prompt, delete subsequent, stream new response

For cases 2 and 3, the prior messages have already been deleted by the time streaming starts. If the user cancels with 0 chunks streamed, the deletion stands (the old response is gone; the user asked for a new one and got none). That's acceptable — they can always Regenerate again or undo via edit history if they re-type. We accept this as the natural consequence of their sequence of actions; no special handling.

## Error handling

- `OperationCanceledException` during HTTP send or read → save partial (if any), no error message shown.
- Any other exception → existing behavior (set `ErrorMessage`, clear streaming content, `IsStreaming = false`).
- If `AddMessageAsync` itself throws while saving the partial, surface that as an error message. Unlikely but handled.

## Testing

Manual verification (this project has no test suite yet):
1. Start generation on a slow model, click Stop after a few tokens → partial appears as Response, editable, no error.
2. Start generation, click Stop immediately before any tokens arrive → no message added, no error, input re-enabled.
3. Cancel during Regenerate → partial (or nothing) saved as the new response; prior response stays deleted.
4. Cancel during Save & Resubmit → same as above; subsequent messages stay deleted.
5. Navigate away mid-stream → HTTP request aborts (verify via network tab or server logs), no exception spam.

## Out of scope / follow-ups

- `Escape` key to cancel. Requires JS interop or a focused wrapper element; deferred to keep v1 scope tight.
- Cancelling category import generation (uses non-streaming `ChatAsync`).
- Visual indication that a message was produced via cancellation vs. natural completion.
- Retry-from-cancel ("keep generating where I left off"). OpenRouter doesn't support this natively and our model prompts aren't structured for continuation.
