# Phase 3: Drafts + SSE Streaming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End-to-end story generation in the native client. Add server endpoints for draft CRUD plus an SSE streaming endpoint for prompt → assistant generation, then a client UX (drafts list on Library, DraftView with prompt input + live streaming, SaveDraftSheet to convert a draft to a story).

**Architecture:** Server delegates to existing `DraftService` and `OpenRouterService.ChatStreamAsync` — no business logic moves. The streaming endpoint emits one `data:` line per chunk in `text/event-stream` format with a small JSON envelope. Client uses `URLSession.bytes(for:)` to consume the SSE stream as an `AsyncSequence`, accumulating chunks into a `@State` string while the response builds. Cancellation is supported via `Task` cancellation propagating through `URLSession`.

**Tech Stack:** ASP.NET minimal APIs (Server-Sent Events via `IAsyncEnumerable`), Swift `URLSession` byte-streaming + `AsyncSequence`, no new packages.

## Out of scope for Phase 3

- Per-message editing, deletion, regenerate (the Blazor draft page has these — they're a Phase 4 polish task)
- Streaming-resume on reconnect (deferred — the simpler "if you lose the connection mid-stream, the partial assistant message is persisted but you can't resume the stream itself" behavior is the v1 contract)
- Cancel-in-flight UI (a stop button) — defer to Phase 4 if needed; Task cancellation works at the Swift level (e.g., when the view disappears) but no explicit user-facing cancel button
- Reasoning content rendering as a separate UI affordance — for v1, reasoning chunks are silently dropped on the client side. The server emits them in the stream so a future iteration can surface them.

## File Structure

**Create (server):**
- `src/Fabulis.Server/Api/DraftEndpoints.cs` — all `/api/v1/drafts/*` endpoints (CRUD + streaming + save)

**Modify (server):**
- `src/Fabulis.Server/Api/Dtos.cs` — add draft-related DTOs
- `src/Fabulis.Server/Program.cs` — `api.MapDraftEndpoints()`

**Create (client):**
- `client/Fabulis/Views/Draft/DraftView.swift` — main draft UX (messages + prompt input + streaming pane)
- `client/Fabulis/Views/Draft/DraftMessageView.swift` — single-message bubble, parameterized like `StoryMessageView`
- `client/Fabulis/Views/Draft/SaveDraftSheet.swift` — modal to choose category + story title

**Modify (client):**
- `client/Fabulis/Models/APIDtos.swift` — add `DraftSummary`, `DraftDetail`, `DraftMessageDto`, `StreamEnvelope`, `SaveDraftRequest`, `SaveDraftResponse`
- `client/Fabulis/Services/FabulisAPIClient.swift` — add `listDrafts`, `createDraft`, `getDraft`, `deleteDraft`, `streamMessage(draftId, prompt)` returning `AsyncThrowingStream<StreamEnvelope, Error>`, `saveDraft`
- `client/Fabulis/Views/Library/LibraryView.swift` — add a "Drafts" section above categories, plus a "+ New Draft" button; navigate to the new `DraftView`

## Notes for the implementer

- **MessageRole again.** Server enum `Prompt` / `Response` (already in `MessageRole.swift` on the client side as `MessageRole`). DraftMessageDto reuses it.
- **Storyteller is a singleton.** `DraftService.CreateDraftAsync()` already pulls the single storyteller. `POST /api/v1/drafts` takes no body.
- **SSE format on the wire.** One JSON object per SSE event. Envelope:
  ```json
  {"kind":"chunk","text":"Once upon","reasoning":false,"messageId":null}
  {"kind":"chunk","text":"thinking...","reasoning":true,"messageId":null}
  {"kind":"done","text":null,"reasoning":null,"messageId":42}
  ```
  Lines on the wire: `data: <json>\n\n`. No event types — keep it flat. Single Swift `StreamEnvelope` struct, all fields optional except `kind`.
- **Streaming endpoint must NOT use the cached scoped DbContext to write the assistant message.** Inside the stream the server holds an open response; resolving DbContext for the duration of the stream + a final SaveChanges is fine because the request scope is still alive, but be mindful that calling DbContext from inside the IAsyncEnumerable consumer is normal.
- **Anti-buffering.** `Response.Body.FlushAsync()` after each chunk so iOS sees the bytes immediately. Otherwise Kestrel may buffer.
- **HTTP error handling for streaming.** If OpenRouter / no-API-key throws BEFORE the first chunk, return a regular non-200. If it throws mid-stream, surface a `{"kind":"error","text":"...","reasoning":null,"messageId":null}` envelope and end the stream.
- **Per-task verification uses curl + xcodebuild.** Server tasks: curl-test with bearer token. Client tasks: `xcodebuild` for both iOS Simulator and Mac Catalyst at the end.
- **Sequencing.** All server-side tasks first (Section A), then all client-side tasks (Section B). Within each section, tasks build cumulatively.

---

## Section A — Server: drafts API

### Task 1: Draft DTOs

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`

- [ ] **Step 1: Append draft DTOs at the bottom**

```csharp
// ---------- drafts ----------
public sealed record DraftSummaryDto(
    int Id,
    string? Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount);

public sealed record DraftDto(
    int Id,
    string? Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string StorytellerName,
    string ModelName,
    IReadOnlyList<DraftMessageDto> Messages);

public sealed record DraftMessageDto(
    int Id,
    MessageRole Role,
    string Content,
    int SortOrder);

public sealed record StreamPromptRequest(string Prompt);

public sealed record StreamEnvelope(
    string Kind,        // "chunk" | "done" | "error"
    string? Text,       // chunk text or error message
    bool? Reasoning,    // true if this chunk is reasoning content
    int? MessageId);    // only set on "done"

public sealed record SaveDraftRequest(
    int? CategoryId,
    string? NewCategoryName,
    int? StoryId,
    string? NewStoryTitle);

public sealed record SaveDraftResponse(
    int StoryId,
    int VersionId,
    int VersionNumber);
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -8
```

Expected: BUILD SUCCEEDED.

---

### Task 2: Draft CRUD endpoints (no streaming, no save)

**Files:**
- Create: `src/Fabulis.Server/Api/DraftEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `DraftEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class DraftEndpoints
{
    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/drafts").RequireSession();

        group.MapGet("", async (DraftService drafts) =>
        {
            var all = await drafts.GetDraftsAsync();
            var dto = all.Select(d => new DraftSummaryDto(
                d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.Messages.Count)).ToList();
            return Results.Ok(dto);
        });

        group.MapPost("", async (DraftService drafts) =>
        {
            var draft = await drafts.CreateDraftAsync();
            var loaded = await drafts.GetDraftAsync(draft.Id);
            return Results.Ok(ToDto(loaded!));
        });

        group.MapGet("/{id:int}", async (int id, DraftService drafts) =>
        {
            var draft = await drafts.GetDraftAsync(id);
            return draft is null ? Results.NotFound() : Results.Ok(ToDto(draft));
        });

        group.MapDelete("/{id:int}", async (int id, DraftService drafts) =>
        {
            await drafts.DeleteDraftAsync(id);
            return Results.NoContent();
        });

        return routes;
    }

    internal static DraftDto ToDto(Draft d) => new(
        d.Id, d.Title, d.CreatedAt, d.UpdatedAt,
        d.Storyteller.Name, d.Storyteller.ModelName,
        d.Messages.OrderBy(m => m.SortOrder)
            .Select(m => new DraftMessageDto(m.Id, m.Role, m.Content, m.SortOrder))
            .ToList());
}
```

- [ ] **Step 2: Wire into `Program.cs`**

After `api.MapStorytellerEndpoints();` add:

```csharp
api.MapDraftEndpoints();
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -8
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Smoke test (server up)**

Start server, unlock and capture token (see Phase 1 plan), then:

```bash
echo "--- create draft ---"
curl -sS -X POST http://localhost:5288/api/v1/drafts -H "Authorization: Bearer $TOK"
echo ""
echo "--- list drafts ---"
curl -sS http://localhost:5288/api/v1/drafts -H "Authorization: Bearer $TOK"
echo ""
# pick the new id, then:
echo "--- get draft 1 ---"
curl -sS http://localhost:5288/api/v1/drafts/1 -H "Authorization: Bearer $TOK"
echo "--- delete draft 1 ---"
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE http://localhost:5288/api/v1/drafts/1 -H "Authorization: Bearer $TOK"
```

Expected: create returns DraftDto JSON; list returns array; get returns same DraftDto; delete returns 204.

---

### Task 3: SSE streaming endpoint

**Files:**
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs`

- [ ] **Step 1: Add inside `MapDraftEndpoints`, before the `return routes;` line**

```csharp
        group.MapPost("/{id:int}/messages", async (
            int id,
            StreamPromptRequest body,
            DraftService drafts,
            OpenRouterService openRouter,
            FabulisDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Prompt))
                return Results.BadRequest(new { error = "prompt is required" });

            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            await drafts.AddMessageAsync(id, MessageRole.Prompt, body.Prompt.Trim());
            draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var content = new System.Text.StringBuilder();
            var storyteller = draft.Storyteller;

            try
            {
                await foreach (var chunk in openRouter.ChatStreamAsync(
                    storyteller.ModelName,
                    storyteller.Prompt,
                    draft.Messages.ToList(),
                    storyteller.Temperature,
                    storyteller.TopP,
                    storyteller.MaxTokens,
                    storyteller.MinP,
                    storyteller.TopK,
                    storyteller.TopA,
                    ct))
                {
                    var isReasoning = chunk.Kind == StreamChunkKind.Reasoning;
                    if (!isReasoning) content.Append(chunk.Text);
                    var env = new StreamEnvelope("chunk", chunk.Text, isReasoning, null);
                    await WriteEnvelope(http, env, ct);
                }

                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    await WriteEnvelope(http, new StreamEnvelope("done", null, null, saved.Id), ct);
                }
                else
                {
                    await WriteEnvelope(http, new StreamEnvelope("done", null, null, null), ct);
                }
            }
            catch (OperationCanceledException)
            {
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    await WriteEnvelope(http, new StreamEnvelope("done", null, null, saved.Id), CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                await WriteEnvelope(http, new StreamEnvelope("error", ex.Message, null, null), CancellationToken.None);
            }

            return Results.Empty;
        }).DisableAntiforgery();
```

And at the bottom of the class, add the helper:

```csharp
    private static async Task WriteEnvelope(HttpContext http, StreamEnvelope env, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(env, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        });
        var line = $"data: {json}\n\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        await http.Response.Body.WriteAsync(bytes, ct);
        await http.Response.Body.FlushAsync(ct);
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -8
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Smoke test the stream**

Start server. Unlock and capture token. Create a draft (returns id). Then:

```bash
curl -sS -N -X POST http://localhost:5288/api/v1/drafts/1/messages \
  -H "Authorization: Bearer $TOK" \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"Tell me a one-sentence story."}'
```

Expected: `data: {"kind":"chunk","text":"...","reasoning":false,"messageId":null}` lines streaming in real time, then `data: {"kind":"done","text":null,"reasoning":null,"messageId":N}`. Total time depends on the model.

If the OpenRouter API key isn't configured, expect: `data: {"kind":"error","text":"OpenRouter API key is not configured. Set it in Settings.","reasoning":null,"messageId":null}`.

After completion, `curl http://localhost:5288/api/v1/drafts/1 -H "Authorization: Bearer $TOK"` should show the prompt + assistant response in the messages array.

---

### Task 4: Save endpoint

**Files:**
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs`

- [ ] **Step 1: Add inside `MapDraftEndpoints`, before `return routes;`**

```csharp
        group.MapPost("/{id:int}/save", async (
            int id,
            SaveDraftRequest body,
            DraftService drafts,
            FabulisDbContext db) =>
        {
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null) return Results.NotFound();

            int categoryId;
            if (body.CategoryId is int existingCategoryId)
            {
                categoryId = existingCategoryId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.NewCategoryName))
                    return Results.BadRequest(new { error = "categoryId or newCategoryName is required" });
                var newCat = new Category { Name = body.NewCategoryName.Trim(), CreatedAt = DateTime.UtcNow };
                db.Categories.Add(newCat);
                await db.SaveChangesAsync();
                categoryId = newCat.Id;
            }

            int? storyId = body.StoryId;
            string? newStoryTitle = body.NewStoryTitle?.Trim();

            if (storyId is null && string.IsNullOrWhiteSpace(newStoryTitle))
                return Results.BadRequest(new { error = "storyId or newStoryTitle is required" });

            var version = await drafts.SaveToLibraryAsync(id, categoryId, storyId, newStoryTitle);

            return Results.Ok(new SaveDraftResponse(version.StoryId, version.Id, version.VersionNumber));
        });
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -8
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Smoke test**

After completing the streaming test (so a draft has both prompt and response):

```bash
echo "--- save draft 1 to a new category 'Quick Tests' as a new story ---"
curl -sS -X POST http://localhost:5288/api/v1/drafts/1/save \
  -H "Authorization: Bearer $TOK" \
  -H 'Content-Type: application/json' \
  -d '{"categoryId":null,"newCategoryName":"Quick Tests","storyId":null,"newStoryTitle":"Smoke Test Story"}'
```

Expected: `{"storyId":N,"versionId":N,"versionNumber":1}`. Then verify via library: `curl http://localhost:5288/api/v1/library -H "Authorization: Bearer $TOK"` should show "Quick Tests".

---

## Section B — Client: drafts UX

### Task 5: API DTOs (client side)

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`

- [ ] **Step 1: Append at the bottom**

```swift
// MARK: - Drafts

struct DraftSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String?
    let createdAt: Date
    let updatedAt: Date
    let messageCount: Int
}

struct DraftDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String?
    let createdAt: Date
    let updatedAt: Date
    let storytellerName: String
    let modelName: String
    let messages: [DraftMessageDto]
}

struct DraftMessageDto: Decodable, Identifiable, Sendable {
    let id: Int
    let role: MessageRole
    let content: String
    let sortOrder: Int
}

struct StreamEnvelope: Decodable, Sendable {
    let kind: String           // "chunk" | "done" | "error"
    let text: String?
    let reasoning: Bool?
    let messageId: Int?
}

struct SaveDraftRequest: Encodable, Sendable {
    let categoryId: Int?
    let newCategoryName: String?
    let storyId: Int?
    let newStoryTitle: String?
}

struct SaveDraftResponse: Decodable, Sendable {
    let storyId: Int
    let versionId: Int
    let versionNumber: Int
}
```

- [ ] **Step 2: xcodebuild**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
```

Expected: BUILD SUCCEEDED.

---

### Task 6: API client additions

**Files:**
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Add new methods to the `actor FabulisAPIClient`**

Insert after `func storyVersion(storyId:version:)`:

```swift
    func listDrafts() async throws -> [DraftSummary] {
        try await request("GET", path: "/drafts", authed: true)
    }

    func createDraft() async throws -> DraftDetail {
        struct Empty: Encodable {}
        return try await request("POST", path: "/drafts", body: Empty(), authed: true)
    }

    func getDraft(id: Int) async throws -> DraftDetail {
        try await request("GET", path: "/drafts/\(id)", authed: true)
    }

    func deleteDraft(id: Int) async throws {
        try await requestVoid("DELETE", path: "/drafts/\(id)", authed: true)
    }

    func saveDraft(id: Int, request body: SaveDraftRequest) async throws -> SaveDraftResponse {
        try await request("POST", path: "/drafts/\(id)/save", body: body, authed: true)
    }

    /// Streams `StreamEnvelope` events from POST /drafts/{id}/messages.
    /// Caller stops by cancelling the consuming Task.
    func streamMessage(draftId: Int, prompt: String) -> AsyncThrowingStream<StreamEnvelope, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    let req = try await buildStreamRequest(draftId: draftId, prompt: prompt)
                    let (bytes, response) = try await session.bytes(for: req)
                    if let http = response as? HTTPURLResponse, http.statusCode == 401 {
                        continuation.finish(throwing: APIError.unauthorized); return
                    }
                    if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                        continuation.finish(throwing: APIError.server(status: http.statusCode, body: nil)); return
                    }
                    let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
                    for try await line in bytes.lines {
                        try Task.checkCancellation()
                        guard line.hasPrefix("data: ") else { continue }
                        let payload = String(line.dropFirst("data: ".count))
                        if let data = payload.data(using: .utf8) {
                            do {
                                let env = try dec.decode(StreamEnvelope.self, from: data)
                                continuation.yield(env)
                                if env.kind == "done" || env.kind == "error" { break }
                            } catch {
                                // skip malformed line
                            }
                        }
                    }
                    continuation.finish()
                } catch is CancellationError {
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    private func buildStreamRequest(draftId: Int, prompt: String) async throws -> URLRequest {
        struct Body: Encodable { let prompt: String }
        return try await buildRequest(method: "POST", path: "/drafts/\(draftId)/messages", body: Body(prompt: prompt), authed: true)
    }
```

- [ ] **Step 2: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
```

Expected: BUILD SUCCEEDED.

---

### Task 7: DraftView + DraftMessageView

**Files:**
- Create: `client/Fabulis/Views/Draft/DraftView.swift`
- Create: `client/Fabulis/Views/Draft/DraftMessageView.swift`

- [ ] **Step 1: Make the directory**

```bash
mkdir -p client/Fabulis/Views/Draft
```

- [ ] **Step 2: `DraftMessageView.swift`**

```swift
import SwiftUI

struct DraftMessageView: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool

    init(message: DraftMessageDto) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
    }

    init(streamingResponse content: String) {
        self.role = .response
        self.content = content
        self.isStreaming = true
    }

    private var roleLabel: String {
        switch role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(roleLabel.uppercased()).font(.caption2.bold()).foregroundStyle(role == .response ? Color.accentColor : .secondary)
                if isStreaming {
                    ProgressView().controlSize(.mini)
                }
            }
            Text(content).font(.body).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}
```

- [ ] **Step 3: `DraftView.swift`**

```swift
import SwiftUI

struct DraftView: View {
    let draftId: Int

    @State private var draft: DraftDetail?
    @State private var prompt: String = ""
    @State private var streamingContent: String = ""
    @State private var isStreaming = false
    @State private var streamTask: Task<Void, Never>?
    @State private var errorMessage: String?
    @State private var showSaveSheet = false
    @FocusState private var promptFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        if let draft {
                            ForEach(draft.messages) { msg in
                                DraftMessageView(message: msg).id(msg.id)
                            }
                        }
                        if isStreaming {
                            DraftMessageView(streamingResponse: streamingContent).id("streaming")
                        }
                        if let errorMessage {
                            Text(errorMessage).foregroundStyle(.red).padding(.top, 8)
                        }
                    }
                    .padding()
                }
                .onChange(of: streamingContent) {
                    withAnimation { proxy.scrollTo("streaming", anchor: .bottom) }
                }
                .onChange(of: draft?.messages.count ?? 0) {
                    if let last = draft?.messages.last { withAnimation { proxy.scrollTo(last.id, anchor: .bottom) } }
                }
            }

            Divider()

            HStack(alignment: .bottom, spacing: 8) {
                TextField("Prompt", text: $prompt, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .lineLimit(1...5)
                    .focused($promptFocused)
                    .disabled(isStreaming)
                Button {
                    Task { await submit() }
                } label: {
                    Image(systemName: "paperplane.fill").padding(.horizontal, 4)
                }
                .buttonStyle(.borderedProminent)
                .disabled(prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isStreaming)
            }
            .padding()
        }
        .navigationTitle(draft?.title ?? "New Draft")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button("Save") { showSaveSheet = true }
                    .disabled(draft?.messages.isEmpty ?? true || isStreaming)
            }
        }
        .sheet(isPresented: $showSaveSheet) {
            SaveDraftSheet(draftId: draftId, draftTitle: draft?.title)
        }
        .task { await loadDraft() }
        .onDisappear { streamTask?.cancel() }
    }

    private func loadDraft() async {
        do {
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
            promptFocused = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func submit() async {
        let pending = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !pending.isEmpty else { return }
        prompt = ""
        errorMessage = nil
        streamingContent = ""
        isStreaming = true

        let stream = await FabulisAPIClient.shared.streamMessage(draftId: draftId, prompt: pending)
        streamTask = Task {
            do {
                for try await env in stream {
                    if Task.isCancelled { break }
                    switch env.kind {
                    case "chunk":
                        if env.reasoning != true, let text = env.text {
                            streamingContent += text
                        }
                    case "done":
                        break
                    case "error":
                        errorMessage = env.text ?? "Unknown error"
                    default:
                        break
                    }
                }
            } catch {
                errorMessage = error.localizedDescription
            }
            // Reload from server to pick up the persisted assistant message
            do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
            streamingContent = ""
            isStreaming = false
        }
    }
}
```

- [ ] **Step 4: Build (will fail until SaveDraftSheet exists; add stub if needed)**

If the build fails on `SaveDraftSheet`, add a stub at `client/Fabulis/Views/Draft/SaveDraftSheet.swift`:

```swift
import SwiftUI
struct SaveDraftSheet: View {
    let draftId: Int
    let draftTitle: String?
    var body: some View { Text("Save").padding() }
}
```

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
```

Expected: BUILD SUCCEEDED.

---

### Task 8: SaveDraftSheet (full)

**Files:**
- Modify: `client/Fabulis/Views/Draft/SaveDraftSheet.swift`

- [ ] **Step 1: Replace the stub**

```swift
import SwiftUI

struct SaveDraftSheet: View {
    let draftId: Int
    let draftTitle: String?

    @Environment(\.dismiss) private var dismiss
    @State private var categories: [CategorySummary] = []
    @State private var selectedCategoryId: Int? = nil   // nil = "New category"
    @State private var newCategoryName: String = ""
    @State private var storiesInCategory: [StorySummary] = []
    @State private var selectedStoryId: Int? = nil      // nil = "New story"
    @State private var newStoryTitle: String = ""
    @State private var isSaving = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section("Category") {
                    Picker("Category", selection: $selectedCategoryId) {
                        Text("— New category —").tag(Int?.none)
                        ForEach(categories) { cat in
                            Text(cat.name).tag(Int?(cat.id))
                        }
                    }
                    if selectedCategoryId == nil {
                        TextField("New category name", text: $newCategoryName)
                            .textInputAutocapitalization(.words)
                    }
                }

                if let catId = selectedCategoryId {
                    Section("Story") {
                        Picker("Story", selection: $selectedStoryId) {
                            Text("— New story —").tag(Int?.none)
                            ForEach(storiesInCategory) { s in
                                Text(s.title).tag(Int?(s.id))
                            }
                        }
                        .task(id: catId) { await loadStories(in: catId) }
                    }
                }

                if selectedStoryId == nil {
                    Section("Story title") {
                        TextField("Story title", text: $newStoryTitle)
                    }
                }

                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Save to Library")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                        .disabled(!canSave || isSaving)
                }
            }
            .task { await loadCategories(); newStoryTitle = draftTitle ?? "" }
        }
    }

    private var canSave: Bool {
        let categoryReady = selectedCategoryId != nil
            || !newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let storyReady = selectedStoryId != nil
            || !newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        return categoryReady && storyReady
    }

    private func loadCategories() async {
        do {
            let resp = try await FabulisAPIClient.shared.library()
            categories = resp.categories
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func loadStories(in categoryId: Int) async {
        do {
            let detail = try await FabulisAPIClient.shared.category(id: categoryId)
            storiesInCategory = detail.stories
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func save() async {
        errorMessage = nil
        isSaving = true
        defer { isSaving = false }
        do {
            let req = SaveDraftRequest(
                categoryId: selectedCategoryId,
                newCategoryName: selectedCategoryId == nil ? newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines) : nil,
                storyId: selectedStoryId,
                newStoryTitle: selectedStoryId == nil ? newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines) : nil)
            _ = try await FabulisAPIClient.shared.saveDraft(id: draftId, request: req)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
```

Expected: BUILD SUCCEEDED.

---

### Task 9: Drafts section + "New Draft" on LibraryView

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`

The Library view currently has only categories. Add a "Drafts" section above categories and a "+ New Draft" button in the toolbar that creates a draft and navigates into it.

- [ ] **Step 1: Replace the entire `LibraryView.swift`**

```swift
import SwiftUI

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var pendingNewDraftId: Int?

    var body: some View {
        NavigationStack {
            content
                .navigationTitle("Library")
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        Button {
                            Task { await createDraft() }
                        } label: {
                            HStack(spacing: 4) {
                                if creatingDraft { ProgressView().controlSize(.mini) }
                                else { Image(systemName: "plus") }
                                Text("New Draft")
                            }
                        }
                        .disabled(creatingDraft)
                    }
                    ToolbarItem(placement: .topBarTrailing) {
                        NavigationLink(destination: SettingsView()) {
                            Image(systemName: "gear")
                        }
                    }
                }
                .navigationDestination(for: CategorySummary.self) { category in
                    CategoryView(categoryId: category.id, categoryName: category.name)
                }
                .navigationDestination(for: DraftSummary.self) { draft in
                    DraftView(draftId: draft.id)
                }
                .navigationDestination(isPresented: Binding(
                    get: { pendingNewDraftId != nil },
                    set: { if !$0 { pendingNewDraftId = nil } }
                )) {
                    if let id = pendingNewDraftId {
                        DraftView(draftId: id)
                    }
                }
                .task { await load() }
                .refreshable { await load() }
        }
    }

    @ViewBuilder
    private var content: some View {
        if isLoading && categories.isEmpty && drafts.isEmpty {
            ProgressView()
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .padding()
        } else {
            List {
                if !drafts.isEmpty {
                    Section("Drafts") {
                        ForEach(drafts) { draft in
                            NavigationLink(value: draft) {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(draft.title ?? "Untitled draft").font(.body)
                                    Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                                        .font(.caption2).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
                if categories.isEmpty {
                    Section {
                        ContentUnavailableView("No categories",
                            systemImage: "books.vertical",
                            description: Text("Save a draft to a category to see it here."))
                    }
                } else {
                    Section("Library") {
                        ForEach(categories) { category in
                            NavigationLink(value: category) {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(category.name).font(.body)
                                    Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                                        .font(.caption2).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            async let lib = FabulisAPIClient.shared.library()
            async let drafs = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            drafts = try await drafs
        } catch APIError.unauthorized {
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func createDraft() async {
        creatingDraft = true
        defer { creatingDraft = false }
        do {
            let draft = try await FabulisAPIClient.shared.createDraft()
            pendingNewDraftId = draft.id
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool { lhs.id == rhs.id }
}

extension DraftSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: DraftSummary, rhs: DraftSummary) -> Bool { lhs.id == rhs.id }
}
```

Note: this replaces the previous LibraryView's grid layout with a `List` so it handles drafts + categories naturally. The `CategoryCard` view is no longer used by LibraryView but stays in the codebase (could be deleted in a follow-up).

- [ ] **Step 2: Build for both targets**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=macOS,variant=Mac Catalyst' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -5
```

Expected: BUILD SUCCEEDED for both.

---

## Section C — Wrap-up

### Task 10: End-to-end manual smoke (developer)

This is the developer's hands-on validation.

- [ ] **Step 1: Server up**

```bash
dotnet run --project src/Fabulis.Server &
```

- [ ] **Step 2: Build + open client in Xcode**

```bash
open client/Fabulis.xcodeproj
```

In Xcode: pick Fabulis scheme, run on iPhone Simulator (or Mac Catalyst).

- [ ] **Step 3: Manual flow**
  1. Onboard with `http://localhost:5288` + your vault password.
  2. Library shows your existing categories.
  3. Tap **+ New Draft** in the top-left → new empty DraftView.
  4. Type a prompt, tap send. The response should stream in chunk-by-chunk in real time.
  5. After completion, the Save button (top-right) becomes enabled.
  6. Tap **Save** → SaveDraftSheet. Either pick an existing category or create a new one; same for story title. Tap Save.
  7. Sheet dismisses. Pop back to Library — your new story shows up under the right category.
  8. Drill into the saved story to verify the content matches.

- [ ] **Step 4: Stop server**

```bash
kill %1
```

- [ ] **Step 5: Phase 3 wrap-up commit**

```bash
git commit --allow-empty -m "Phase 3 complete: drafts + SSE streaming end-to-end"
```

---

## Self-review notes

- **Spec coverage.** New draft + view + stream + save end-to-end is covered. Streaming-resume on reconnect is explicitly deferred (called out under "Out of scope") — the partial assistant message is still persisted on cancellation, which is the safety property we cared about.
- **Type consistency.** `MessageRole`, `CategorySummary`, `StorySummary` reused from Phase 2. New types: `DraftSummary`, `DraftDetail`, `DraftMessageDto`, `StreamEnvelope`, `SaveDraftRequest`, `SaveDraftResponse`. Names match server-side records exactly with the .NET → camelCase JSON convention.
- **No placeholders.** Every code block is complete and runnable.
- **Risks.** (1) `bytes.lines` on `URLSession.AsyncBytes` requires iOS 15+ which we have — fine. (2) The streaming endpoint currently swallows mid-stream errors as in-band envelopes; if the user has no API key configured, they'll see the error in the UI (good). (3) The `CategoryCard` view becomes orphan after Task 9 — left in place intentionally; cleanup is a Phase 4 job.
