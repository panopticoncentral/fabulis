# Phase 4: Parity + Blazor Retirement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the native client to feature-parity with the Blazor UI in the areas that matter (categories CRUD, full settings with model picker, storyteller editor, per-message draft editing), then delete the Blazor UI and orphaned services. After this phase Fabulis is purely an API + native client app.

**Architecture:** Same patterns as Phases 1–3. Add the missing `/api/v1/*` endpoints; add the matching SwiftUI screens; then strip out `Components/`, `wwwroot/`, the Razor SDK pieces of `Program.cs`, the `Markdig` package, and the now-orphaned `CategoryImportService` / `CategoryExportService`.

**Tech Stack:** Same as previous phases. No new dependencies on either side.

## Out of scope for Phase 4

- **Import/export.** The existing services are filesystem-path-based and don't translate to a network-accessible NAS deployment. The Blazor `/import` and `/export` pages are deleted as part of Blazor retirement; the underlying services are deleted alongside them. Bulk operations remain a "SSH to the server" workflow until a v2 of import/export is designed.
- **Inline message editing** (the Blazor draft page lets you edit a prompt and resubmit). Phase 4 ports the destructive operations only: delete a message + everything after it, and regenerate the last response. Inline editing can be a follow-up.
- **Streaming-resume on reconnect** (still deferred).
- **Reasoning chunks UI** (still silently dropped client-side).
- **Scoped TLS posture** (still v2 work).

## File Structure

**Server — create:**
- (none new — endpoints get added to existing `LibraryEndpoints.cs`, `DraftEndpoints.cs`, plus a new `ModelEndpoints.cs`)

**Server — modify:**
- `src/Fabulis.Server/Api/LibraryEndpoints.cs` — POST/PUT/DELETE for categories
- `src/Fabulis.Server/Api/DraftEndpoints.cs` — DELETE single message, POST regenerate
- `src/Fabulis.Server/Api/Dtos.cs` — `CreateCategoryRequest`, `RenameCategoryRequest`, `ModelInfoDto`
- `src/Fabulis.Server/Program.cs` — register `MapModelEndpoints`; later, strip Razor wiring

**Server — create new:**
- `src/Fabulis.Server/Api/ModelEndpoints.cs` — GET /api/v1/models

**Server — delete (final task):**
- `src/Fabulis.Server/Components/` — entire directory
- `src/Fabulis.Server/wwwroot/` — entire directory
- `src/Fabulis.Server/Data/CategoryImportService.cs`
- `src/Fabulis.Server/Data/CategoryExportService.cs`
- Razor + Markdig packages from `src/Fabulis.Server/Fabulis.Server.csproj`
- All Razor wiring from `Program.cs` (`AddRazorComponents`, `MapRazorComponents`, `UseAntiforgery`, `MapStaticAssets`, the `App` import, `app.UseHttpsRedirection`)

**Client — create:**
- `client/Fabulis/Views/Library/EditCategorySheet.swift` — create + rename in one sheet
- `client/Fabulis/Views/Settings/ModelPickerView.swift` — full model list with search
- `client/Fabulis/Views/Settings/StorytellerEditorView.swift` — full storyteller form

**Client — modify:**
- `client/Fabulis/Models/APIDtos.swift` — `ModelInfo`, `StorytellerUpdateRequest`
- `client/Fabulis/Services/FabulisAPIClient.swift` — `createCategory`, `renameCategory`, `deleteCategory`, `models`, `getStoryteller`, `updateStoryteller`, `updateSettings`, `deleteDraftMessage`, `regenerate`
- `client/Fabulis/Views/Library/LibraryView.swift` — "+ Category" button (in addition to "+ Draft")
- `client/Fabulis/Views/Library/CategoryView.swift` — Rename + Delete actions
- `client/Fabulis/Views/Settings/SettingsView.swift` — full settings (API key, model picker link, autolock, storyteller link, lock)
- `client/Fabulis/Views/Draft/DraftView.swift` — message context menu (delete + regenerate)
- `client/Fabulis/Views/Draft/DraftMessageView.swift` — accept a context-menu trailing closure parameter (or callbacks)

**Project root — modify:**
- `CLAUDE.md` — drop the "web app" framing; describe Fabulis as an API + native client

## Notes for the implementer

- **Sequencing matters.** Do the parity endpoints + UI first (Sections A and B). Then verify everything works end-to-end. Only then do the Blazor retirement (Section C). If retirement breaks something, you have a clean diff.
- **Delete = delete-all-after.** For `DELETE /api/v1/drafts/{id}/messages/{messageId}`, use the existing `DraftService.DeleteMessageAndSubsequentAsync`. The native UI presents this as "Delete this and everything after."
- **Regenerate.** New endpoint `POST /api/v1/drafts/{id}/regenerate`. Server: deletes the last assistant message, then re-streams using the existing OpenRouter call. Reuses the SSE envelope from Phase 3.
- **Models endpoint** is a thin pass-through to `OpenRouterService.GetModelsAsync()`. No caching server-side; the client may cache.
- **Settings update.** `/api/v1/settings PUT` already exists from Phase 1 — it accepts partial updates. The new SettingsView calls it for API key + autolock; assistant model is set the same way. Storyteller updates use the existing `/api/v1/storyteller PUT`.
- **Don't break Razor mid-flight.** Sections A and B keep Blazor running. Test in the browser if you want to. Section C is the deletion.
- **csproj surgery.** Before deleting Razor, `Markdig` is the only package solely used by Razor. Removing it cleanly requires that no API code newly references it. Verify with `grep`.
- **Per-task verification.** Server tasks: `dotnet build` + curl 401 spot-checks. Client tasks: `xcodebuild` for both iOS Simulator and Mac Catalyst. Final task: full app smoke is the developer's manual step.

---

## Section A — Server parity endpoints

### Task 1: Categories CRUD endpoints

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`
- Modify: `src/Fabulis.Server/Api/LibraryEndpoints.cs`

- [ ] **Step 1: Append DTOs to `Dtos.cs`**

```csharp
public sealed record CreateCategoryRequest(string Name);
public sealed record RenameCategoryRequest(string Name);
public sealed record ModelInfoDto(string Id, string Name);
```

- [ ] **Step 2: Add three endpoints inside `LibraryEndpoints.MapLibraryEndpoints`, before `return routes;`**

```csharp
        group.MapPost("/categories", async (CreateCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            var cat = new Category { Name = body.Name.Trim(), CreatedAt = DateTime.UtcNow };
            db.Categories.Add(cat);
            await db.SaveChangesAsync();
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null));
        });

        group.MapPut("/categories/{id:int}", async (int id, RenameCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            var cat = await db.Categories.FindAsync(id);
            if (cat is null) return Results.NotFound();
            cat.Name = body.Name.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var cat = await db.Categories.Include(c => c.Stories).FirstOrDefaultAsync(c => c.Id == id);
            if (cat is null) return Results.NotFound();
            db.Categories.Remove(cat);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
```

- [ ] **Step 3: Build + curl spot-check**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -5
```
Expected: BUILD SUCCEEDED.

Start server, then unauthenticated should give 401 for the new routes:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5288/api/v1/categories -H 'Content-Type: application/json' -d '{"name":"x"}'
curl -sS -o /dev/null -w '%{http_code}\n' -X PUT http://localhost:5288/api/v1/categories/1 -H 'Content-Type: application/json' -d '{"name":"x"}'
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE http://localhost:5288/api/v1/categories/9999
```
Expected: all 401. Stop server.

---

### Task 2: Models endpoint

**Files:**
- Create: `src/Fabulis.Server/Api/ModelEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `ModelEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class ModelEndpoints
{
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/models").RequireSession();

        group.MapGet("", async (OpenRouterService openRouter) =>
        {
            try
            {
                var models = await openRouter.GetModelsAsync();
                return Results.Ok(models.Select(m => new ModelInfoDto(m.Id, m.Name)).ToList());
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502);
            }
        });

        return routes;
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`** — after `api.MapDraftEndpoints();` add:

```csharp
api.MapModelEndpoints();
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -5
```
Expected: BUILD SUCCEEDED.

---

### Task 3: Draft message editing endpoints

**Files:**
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs`

- [ ] **Step 1: Add two endpoints inside `DraftEndpoints.MapDraftEndpoints`, before `return routes;`**

The "delete message and subsequent" endpoint:

```csharp
        group.MapDelete("/{draftId:int}/messages/{messageId:int}", async (
            int draftId,
            int messageId,
            DraftService drafts) =>
        {
            await drafts.DeleteMessageAndSubsequentAsync(messageId);
            return Results.NoContent();
        });
```

The "regenerate" endpoint — same SSE shape as POST /messages, but starts by deleting the last assistant message:

```csharp
        group.MapPost("/{id:int}/regenerate", async (
            int id,
            DraftService drafts,
            OpenRouterService openRouter,
            HttpContext http,
            CancellationToken ct) =>
        {
            var initial = await drafts.GetDraftAsync(id);
            if (initial is null) return Results.NotFound();

            await drafts.DeleteLastResponseAsync(id);
            var draft = await drafts.GetDraftAsync(id);
            if (draft is null || draft.Messages.Count == 0) return Results.BadRequest(new { error = "no messages to regenerate from" });

            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var content = new System.Text.StringBuilder();
            var storyteller = draft.Storyteller;

            try
            {
                await foreach (var chunk in openRouter.ChatStreamAsync(
                    storyteller.ModelName, storyteller.Prompt, draft.Messages.ToList(),
                    storyteller.Temperature, storyteller.TopP, storyteller.MaxTokens,
                    storyteller.MinP, storyteller.TopK, storyteller.TopA, ct))
                {
                    var isReasoning = chunk.Kind == StreamChunkKind.Reasoning;
                    if (!isReasoning) content.Append(chunk.Text);
                    await WriteEnvelope(http, new StreamEnvelope("chunk", chunk.Text, isReasoning, null), ct);
                }

                int? savedId = null;
                if (content.Length > 0)
                {
                    var saved = await drafts.AddMessageAsync(id, MessageRole.Response, content.ToString());
                    savedId = saved.Id;
                }
                await WriteEnvelope(http, new StreamEnvelope("done", null, null, savedId), ct);
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
        });
```

(`WriteEnvelope` is the private helper already on `DraftEndpoints` from Phase 3.)

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -5
```
Expected: BUILD SUCCEEDED.

---

## Section B — Client parity UX

### Task 4: API client additions

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Append DTOs to `APIDtos.swift`**

```swift
struct ModelInfo: Decodable, Identifiable, Sendable {
    let id: String
    let name: String
}

struct StorytellerUpdateRequest: Encodable, Sendable {
    let name: String
    let prompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct CreateCategoryRequest: Encodable, Sendable { let name: String }
struct RenameCategoryRequest: Encodable, Sendable { let name: String }
struct SettingsUpdateRequest: Encodable, Sendable {
    let apiKey: String?
    let assistantModel: String?
    let autoLockSelection: String?
}
```

- [ ] **Step 2: Add API client methods (after `saveDraft`)**

```swift
    // -- categories --

    func createCategory(name: String) async throws -> CategorySummary {
        try await request("POST", path: "/categories", body: CreateCategoryRequest(name: name), authed: true)
    }

    func renameCategory(id: Int, name: String) async throws {
        try await requestVoid("PUT", path: "/categories/\(id)", body: RenameCategoryRequest(name: name), authed: true)
    }

    func deleteCategory(id: Int) async throws {
        try await requestVoid("DELETE", path: "/categories/\(id)", authed: true)
    }

    // -- settings + models + storyteller --

    func settings() async throws -> SettingsDto {
        try await request("GET", path: "/settings", authed: true)
    }

    func updateSettings(_ body: SettingsUpdateRequest) async throws {
        try await requestVoid("PUT", path: "/settings", body: body, authed: true)
    }

    func models() async throws -> [ModelInfo] {
        try await request("GET", path: "/models", authed: true)
    }

    func getStoryteller() async throws -> StorytellerDto {
        try await request("GET", path: "/storyteller", authed: true)
    }

    func updateStoryteller(_ body: StorytellerUpdateRequest) async throws {
        try await requestVoid("PUT", path: "/storyteller", body: body, authed: true)
    }

    // -- drafts --

    func deleteDraftMessage(draftId: Int, messageId: Int) async throws {
        try await requestVoid("DELETE", path: "/drafts/\(draftId)/messages/\(messageId)", authed: true)
    }

    func regenerate(draftId: Int) -> AsyncThrowingStream<StreamEnvelope, Error> {
        let session = self.session
        return AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    struct Empty: Encodable {}
                    let req = try await self.buildRequest(method: "POST", path: "/drafts/\(draftId)/regenerate", body: Empty(), authed: true)
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
                            } catch { /* skip */ }
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
```

Note: there's an existing `SettingsDto` in `APIDtos.swift` — only DTOs we ADD are in Step 1. `request<T,B>(method:path:body:authed:)` and `requestVoid<B>(method:path:body:authed:)` from Phase 1 / Phase 3 are already on the actor; the `requestVoid` overload that takes a body is required — if it doesn't yet exist, add it now (matching `request` signature minus the return decode).

- [ ] **Step 3: Add `requestVoid` body-taking overload if missing**

In `FabulisAPIClient.swift`, near the existing `requestVoid(_ method:path:authed:)`, add:

```swift
    private func requestVoid<B: Encodable>(_ method: String, path: String, body: B, authed: Bool) async throws {
        let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
    }
```

- [ ] **Step 4: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
```
Expected: BUILD SUCCEEDED.

---

### Task 5: Category create + rename + delete UI

**Files:**
- Create: `client/Fabulis/Views/Library/EditCategorySheet.swift`
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`
- Modify: `client/Fabulis/Views/Library/CategoryView.swift`

- [ ] **Step 1: `EditCategorySheet.swift`**

```swift
import SwiftUI

struct EditCategorySheet: View {
    enum Mode { case create, rename(id: Int) }

    let mode: Mode
    let initialName: String
    let onSaved: () -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var name: String = ""
    @State private var isSaving = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $name).textInputAutocapitalization(.words)
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                        .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSaving)
                }
            }
            .onAppear { name = initialName }
        }
    }

    private var title: String {
        switch mode {
        case .create: return "New Category"
        case .rename: return "Rename Category"
        }
    }

    private func save() async {
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
            switch mode {
            case .create:
                _ = try await FabulisAPIClient.shared.createCategory(name: trimmed)
            case .rename(let id):
                try await FabulisAPIClient.shared.renameCategory(id: id, name: trimmed)
            }
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Add a "+ Category" toolbar button to `LibraryView`**

In `LibraryView.swift`, add new state + sheet wiring. Find the existing `@State private var pendingNewDraftId: Int?` line and add right after:

```swift
    @State private var showingNewCategorySheet = false
```

Find the Trailing toolbar item with the gear and replace the `.toolbar { ... }` block with:

```swift
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
                    ToolbarItemGroup(placement: .topBarTrailing) {
                        Button { showingNewCategorySheet = true } label: {
                            Image(systemName: "folder.badge.plus")
                        }
                        NavigationLink(destination: SettingsView()) {
                            Image(systemName: "gear")
                        }
                    }
                }
```

Then anywhere inside the `NavigationStack { content.navigationTitle(...) ... }` chain, add:

```swift
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        Task { await load() }
                    })
                }
```

- [ ] **Step 3: Add Rename + Delete to `CategoryView`**

Replace the entire `CategoryView.swift` body. The toolbar gets a Menu with Rename and Delete; the Delete shows a confirmation alert.

```swift
import SwiftUI

struct CategoryView: View {
    let categoryId: Int
    let categoryName: String

    @Environment(\.dismiss) private var dismiss
    @State private var detail: CategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var deleting = false

    var body: some View {
        Group {
            if let detail {
                if detail.stories.isEmpty {
                    ContentUnavailableView("No stories", systemImage: "doc.text",
                        description: Text("Stories saved into this category will appear here."))
                } else {
                    List(detail.stories) { story in
                        NavigationLink(value: story) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(story.title).font(.body)
                                Text(story.createdAt.formatted(date: .abbreviated, time: .omitted))
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load category").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .navigationDestination(for: StorySummary.self) { story in
            StoryView(storyId: story.id, fallbackTitle: story.title)
        }
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { showingRenameSheet = true } label: { Label("Rename", systemImage: "pencil") }
                    Button(role: .destructive) { showingDeleteConfirm = true } label: { Label("Delete", systemImage: "trash") }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
            }
        }
        .sheet(isPresented: $showingRenameSheet) {
            EditCategorySheet(
                mode: .rename(id: categoryId),
                initialName: detail?.name ?? categoryName,
                onSaved: { Task { await load() } })
        }
        .alert("Delete category?",
               isPresented: $showingDeleteConfirm,
               actions: {
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) { Task { await deleteCategory() } }
               },
               message: {
                    Text("This deletes the category and all its stories. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.category(id: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteCategory() async {
        deleting = true; defer { deleting = false }
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: categoryId)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

extension StorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StorySummary, rhs: StorySummary) -> Bool { lhs.id == rhs.id }
}
```

- [ ] **Step 4: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
```
Expected: BUILD SUCCEEDED.

---

### Task 6: Full SettingsView + ModelPickerView

**Files:**
- Create: `client/Fabulis/Views/Settings/ModelPickerView.swift`
- Modify: `client/Fabulis/Views/Settings/SettingsView.swift`

- [ ] **Step 1: `ModelPickerView.swift`**

```swift
import SwiftUI

struct ModelPickerView: View {
    let currentModel: String?
    let onPick: (String) -> Void

    @State private var models: [ModelInfo] = []
    @State private var search: String = ""
    @State private var isLoading = true
    @State private var errorMessage: String?

    private var filtered: [ModelInfo] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return models }
        return models.filter { $0.id.lowercased().contains(q) || $0.name.lowercased().contains(q) }
    }

    var body: some View {
        Group {
            if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load models").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            } else {
                List(filtered) { model in
                    Button { onPick(model.id) } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(model.id).font(.body.monospaced())
                                Text(model.name).font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if model.id == currentModel {
                                Image(systemName: "checkmark").foregroundStyle(.tint)
                            }
                        }
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .searchable(text: $search, prompt: "Filter models")
        .navigationTitle("Assistant Model")
        .task { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            models = try await FabulisAPIClient.shared.models()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
```

- [ ] **Step 2: Replace `SettingsView.swift`**

```swift
import SwiftUI

struct SettingsView: View {
    @Environment(AppState.self) private var appState

    @State private var serverURL: String = ""
    @State private var settings: SettingsDto?
    @State private var apiKeyDraft: String = ""
    @State private var apiKeyJustSaved = false
    @State private var isSavingApiKey = false
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var isLocking = false

    private let autoLockOptions: [(label: String, value: String)] = [
        ("1 minute", "1"), ("5 minutes", "5"), ("15 minutes", "15"),
        ("30 minutes", "30"), ("1 hour", "60"), ("Never", "never")
    ]

    var body: some View {
        Form {
            Section("Server") { LabeledContent("URL", value: serverURL) }

            Section("OpenRouter API key") {
                if let settings, settings.apiKeyIsSet { Text("Key is set").foregroundStyle(.secondary) }
                SecureField("sk-or-...", text: $apiKeyDraft)
                Button {
                    Task { await saveApiKey() }
                } label: {
                    HStack {
                        if isSavingApiKey { ProgressView().controlSize(.mini) }
                        Text("Save key")
                    }
                }
                .disabled(apiKeyDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSavingApiKey)
                if apiKeyJustSaved {
                    Text("API key saved.").font(.caption).foregroundStyle(.green)
                }
            }

            Section("Assistant model") {
                if let settings, let current = settings.assistantModel {
                    Text(current).font(.callout.monospaced()).foregroundStyle(.secondary)
                }
                NavigationLink {
                    ModelPickerView(currentModel: settings?.assistantModel) { picked in
                        Task { await saveModel(picked) }
                    }
                } label: {
                    Text(settings?.assistantModel == nil ? "Choose model" : "Change model")
                }
            }

            Section("Storyteller") {
                NavigationLink("Edit storyteller", destination: StorytellerEditorView())
            }

            Section("Auto-lock") {
                if let settings {
                    Picker("After", selection: Binding(
                        get: { settings.autoLockSelection },
                        set: { newValue in Task { await saveAutoLock(newValue) } }
                    )) {
                        ForEach(autoLockOptions, id: \.value) { opt in
                            Text(opt.label).tag(opt.value)
                        }
                    }
                }
            }

            Section("Vault") {
                Button(role: .destructive) {
                    Task {
                        isLocking = true
                        await appState.lock()
                        isLocking = false
                    }
                } label: {
                    HStack { Image(systemName: "lock.fill"); Text("Lock vault") }
                }
                .disabled(isLocking)
            }

            if let errorMessage {
                Section { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .navigationTitle("Settings")
        .task { await load() }
    }

    private func load() async {
        do {
            serverURL = (try? await KeychainService.shared.loadServerURL()) ?? ""
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func saveApiKey() async {
        let key = apiKeyDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { return }
        isSavingApiKey = true; defer { isSavingApiKey = false }
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: key, assistantModel: nil, autoLockSelection: nil))
            apiKeyDraft = ""
            apiKeyJustSaved = true
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveModel(_ model: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: nil, assistantModel: model, autoLockSelection: nil))
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveAutoLock(_ selection: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: nil, assistantModel: nil, autoLockSelection: selection))
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 3: Add `StorytellerEditorView` stub so it compiles** (Task 7 fills this in)

`client/Fabulis/Views/Settings/StorytellerEditorView.swift`:

```swift
import SwiftUI
struct StorytellerEditorView: View {
    var body: some View { Text("Storyteller").navigationTitle("Storyteller") }
}
```

- [ ] **Step 4: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
```
Expected: BUILD SUCCEEDED.

---

### Task 7: StorytellerEditorView (full)

**Files:**
- Modify: `client/Fabulis/Views/Settings/StorytellerEditorView.swift`

- [ ] **Step 1: Replace contents**

```swift
import SwiftUI

struct StorytellerEditorView: View {
    @State private var existing: StorytellerDto?
    @State private var name: String = ""
    @State private var prompt: String = ""
    @State private var modelName: String = ""
    @State private var temperature: Double = 0.7
    @State private var topP: String = ""
    @State private var maxTokens: String = ""
    @State private var minP: String = ""
    @State private var topK: String = ""
    @State private var topA: String = ""
    @State private var isSaving = false
    @State private var savedAt: Date?
    @State private var errorMessage: String?

    var body: some View {
        Form {
            Section("Identity") {
                TextField("Name", text: $name).textInputAutocapitalization(.words)
            }
            Section("System prompt") {
                TextEditor(text: $prompt).frame(minHeight: 120)
            }
            Section("Model") {
                NavigationLink {
                    ModelPickerView(currentModel: modelName) { picked in
                        modelName = picked
                    }
                } label: {
                    LabeledContent("Model", value: modelName.isEmpty ? "—" : modelName)
                }
            }
            Section("Sampling") {
                HStack {
                    Text("Temperature").frame(width: 110, alignment: .leading)
                    Slider(value: $temperature, in: 0...2, step: 0.05)
                    Text(String(format: "%.2f", temperature)).font(.caption.monospacedDigit())
                }
                LabeledNumberField(label: "top_p (0-1)", value: $topP)
                LabeledNumberField(label: "max_tokens", value: $maxTokens)
                LabeledNumberField(label: "min_p (0-1)", value: $minP)
                LabeledNumberField(label: "top_k (int)", value: $topK)
                LabeledNumberField(label: "top_a (0-1)", value: $topA)
            }
            if let savedAt {
                Section { Text("Saved \(savedAt.formatted(date: .omitted, time: .standard))").font(.caption).foregroundStyle(.green) }
            }
            if let errorMessage {
                Section { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .navigationTitle("Storyteller")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                    .disabled(!canSave || isSaving)
            }
        }
        .task { await load() }
    }

    private var canSave: Bool {
        !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !modelName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func load() async {
        do {
            let s = try await FabulisAPIClient.shared.getStoryteller()
            existing = s
            name = s.name
            prompt = s.prompt
            modelName = s.modelName
            temperature = s.temperature
            topP = s.topP.map { String($0) } ?? ""
            maxTokens = s.maxTokens.map { String($0) } ?? ""
            minP = s.minP.map { String($0) } ?? ""
            topK = s.topK.map { String($0) } ?? ""
            topA = s.topA.map { String($0) } ?? ""
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func save() async {
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            try await FabulisAPIClient.shared.updateStoryteller(StorytellerUpdateRequest(
                name: name.trimmingCharacters(in: .whitespacesAndNewlines),
                prompt: prompt,
                modelName: modelName.trimmingCharacters(in: .whitespacesAndNewlines),
                temperature: temperature,
                topP: Double(topP),
                maxTokens: Int(maxTokens),
                minP: Double(minP),
                topK: Int(topK),
                topA: Double(topA)))
            savedAt = Date()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

private struct LabeledNumberField: View {
    let label: String
    @Binding var value: String

    var body: some View {
        HStack {
            Text(label).frame(width: 110, alignment: .leading)
            TextField("blank = unset", text: $value)
                .keyboardType(.decimalPad)
                .multilineTextAlignment(.trailing)
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
```
Expected: BUILD SUCCEEDED.

---

### Task 8: Draft message context menu (delete + regenerate)

**Files:**
- Modify: `client/Fabulis/Views/Draft/DraftMessageView.swift`
- Modify: `client/Fabulis/Views/Draft/DraftView.swift`

- [ ] **Step 1: Replace `DraftMessageView.swift` to support a trailing context menu**

```swift
import SwiftUI

struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let menu: () -> Menu

    init(message: DraftMessageDto, @ViewBuilder menu: @escaping () -> Menu = { EmptyView() }) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
        self.menu = menu
    }

    init(streamingResponse content: String, @ViewBuilder menu: @escaping () -> Menu = { EmptyView() }) {
        self.role = .response
        self.content = content
        self.isStreaming = true
        self.menu = menu
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
                Text(roleLabel.uppercased())
                    .font(.caption2.bold())
                    .foregroundStyle(role == .response ? Color.accentColor : .secondary)
                if isStreaming { ProgressView().controlSize(.mini) }
            }
            Text(content).font(.body).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .contextMenu { menu() }
    }
}

extension DraftMessageView where Menu == EmptyView {
    init(message: DraftMessageDto) {
        self.init(message: message, menu: { EmptyView() })
    }
    init(streamingResponse content: String) {
        self.init(streamingResponse: content, menu: { EmptyView() })
    }
}
```

- [ ] **Step 2: Update `DraftView.swift` to wire the menu**

In `DraftView.swift`, find:

```swift
                            ForEach(draft.messages) { msg in
                                DraftMessageView(message: msg).id(msg.id)
                            }
```

Replace with:

```swift
                            if let draft {
                                ForEach(Array(draft.messages.enumerated()), id: \.element.id) { idx, msg in
                                    let isLast = idx == draft.messages.count - 1
                                    let isLastResponse = isLast && msg.role == .response
                                    DraftMessageView(message: msg) {
                                        Button(role: .destructive) {
                                            Task { await deleteMessage(msg.id) }
                                        } label: { Label("Delete and after", systemImage: "trash") }
                                        if isLastResponse {
                                            Button {
                                                Task { await regenerate() }
                                            } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
                                        }
                                    }
                                    .id(msg.id)
                                }
                            }
```

(Replace BOTH the outer `if let draft { ForEach... }` block and remove the now-redundant outer `if let draft` since we put it inside.)

Then add these two methods at the bottom of `DraftView`, after `submit()`:

```swift
    private func deleteMessage(_ messageId: Int) async {
        do {
            try await FabulisAPIClient.shared.deleteDraftMessage(draftId: draftId, messageId: messageId)
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func regenerate() async {
        errorMessage = nil
        streamingContent = ""
        isStreaming = true

        let stream = await FabulisAPIClient.shared.regenerate(draftId: draftId)
        streamTask = Task {
            do {
                for try await env in stream {
                    if Task.isCancelled { break }
                    switch env.kind {
                    case "chunk":
                        if env.reasoning != true, let text = env.text { streamingContent += text }
                    case "done": break
                    case "error": errorMessage = env.text ?? "Unknown error"
                    default: break
                    }
                }
            } catch {
                errorMessage = error.localizedDescription
            }
            do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
            streamingContent = ""
            isStreaming = false
        }
    }
```

- [ ] **Step 3: Build for both targets**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=macOS,variant=Mac Catalyst' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -10
```
Expected: BUILD SUCCEEDED for both.

---

## Section C — Retire Blazor

After Section B, the native client has full parity. Now we delete the Blazor surface and the orphaned import/export services.

### Task 9: Delete Razor sources and assets

**Files:**
- Delete: `src/Fabulis.Server/Components/` (entire directory)
- Delete: `src/Fabulis.Server/wwwroot/` (entire directory)
- Delete: `src/Fabulis.Server/Data/CategoryImportService.cs`
- Delete: `src/Fabulis.Server/Data/CategoryExportService.cs`

- [ ] **Step 1: Delete**

```bash
rm -rf src/Fabulis.Server/Components src/Fabulis.Server/wwwroot
rm src/Fabulis.Server/Data/CategoryImportService.cs src/Fabulis.Server/Data/CategoryExportService.cs
```

- [ ] **Step 2: Verify nothing else references the deleted services**

```bash
grep -rn "CategoryImportService\|CategoryExportService" src/ || echo "no remaining references"
```

Expected: `no remaining references`. If any survive (e.g., in `Program.cs` DI registrations), Task 10 catches them.

---

### Task 10: Strip `Program.cs` of Razor wiring

**Files:**
- Modify: `src/Fabulis.Server/Program.cs`

Today's `Program.cs` has Razor registrations + middleware. After Phase 4 it should only contain: builder + DI for services we still use, the activity-tracking middleware, and the API group.

- [ ] **Step 1: Replace contents**

```csharp
using Fabulis.Server.Api;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Fabulis.Server.Auth.SessionTokenStore>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddHostedService<AutoLockService>();
builder.Services.AddDbContext<FabulisDbContext>((sp, options) =>
{
    var vault = sp.GetRequiredService<VaultService>();
    if (vault.IsUnlocked)
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "fabulis.db");
        options.UseSqlite($"Data Source={dbPath};Password={vault.Password}");
    }
});

builder.Services.AddHttpClient();
builder.Services.AddScoped<OpenRouterService>();
builder.Services.AddScoped<DraftService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is null || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    var vault = context.RequestServices.GetRequiredService<VaultService>();
    vault.RecordActivity();
    await next();
});

var api = app.MapGroup("/api/v1").DisableAntiforgery();
api.MapAuthEndpoints();
api.MapLibraryEndpoints();
api.MapStoryEndpoints();
api.MapSettingsEndpoints();
api.MapStorytellerEndpoints();
api.MapDraftEndpoints();
api.MapModelEndpoints();

app.Run();
```

Notes on what's removed:
- `using Fabulis.Server.Components;` (the App import)
- `AddScoped<CategoryImportService>()` and `AddScoped<CategoryExportService>()`
- `AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(...)`
- `app.UseHsts()` and `app.UseHttpsRedirection()` (we serve HTTP-only on LAN per the architecture spec)
- `app.UseAntiforgery()` and `app.MapStaticAssets()`
- `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()`
- The activity middleware now scopes to `/api/*` only (no more `/_blazor`/`/_framework`/`/_content` exclusions needed)

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -8
```
Expected: BUILD SUCCEEDED.

---

### Task 11: Strip Razor + Markdig packages from `Fabulis.Server.csproj`

**Files:**
- Modify: `src/Fabulis.Server/Fabulis.Server.csproj`

`Microsoft.NET.Sdk.Web` is required (we still need ASP.NET Core). What we remove is the `Markdig` package. The Razor SDK pieces are part of `Sdk.Web`, so no SDK change is needed — but the `<RazorComponents>` MSBuild item resolution becomes a no-op since we have no `.razor` files.

- [ ] **Step 1: Verify what's there**

```bash
cat src/Fabulis.Server/Fabulis.Server.csproj
```

- [ ] **Step 2: Edit to remove `Markdig`**

Replace:
```xml
    <PackageReference Include="Markdig" Version="1.1.2" />
```
with nothing (delete that line).

- [ ] **Step 3: Build (and confirm Razor-related build steps are skipped)**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -10
```
Expected: BUILD SUCCEEDED, no Markdig in output.

---

### Task 12: Server smoke test the post-retirement state

- [ ] **Step 1: Run the server, check the surface**

```bash
dotnet run --project src/Fabulis.Server &
```

Wait for "Now listening". Then:

```bash
# /unlock no longer exists (was a Razor page)
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/unlock          # expect 404
# /api/v1 still works
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/auth/status  # expect 401
# Static assets gone
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/css/base.css     # expect 404
```

Expected: 404, 401, 404.

```bash
kill %1
```

---

### Task 13: Update `CLAUDE.md`

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Replace project description framing**

The current CLAUDE.md describes Fabulis as a "Web app for generating stories...". Update to describe the new architecture: ASP.NET Core API + native SwiftUI client.

Specifically replace the file with:

```markdown
# Fabulis

A personal story generator. The data + LLM proxy live in an ASP.NET
Core server; a native SwiftUI client (iPhone, iPad, Mac via Catalyst)
talks to it over an HTTP API on the local network.

## Stack

- **Server:** ASP.NET Core on .NET 10. EF Core with SQLite +
  SQLCipher (encrypted at rest). Solution: `Fabulis.slnx`.
  Source: `src/Fabulis.Server/`.
- **Client:** SwiftUI, iOS 18.5+ deployment target, Mac Catalyst
  destination on the same target. Source: `client/Fabulis/`,
  Xcode project: `client/Fabulis.xcodeproj/`.
- **Wire format:** REST under `/api/v1/*` plus an SSE streaming
  endpoint for story generation.

## Run

```bash
dotnet run --project src/Fabulis.Server
```

Listens on `http://localhost:5288`. The vault password is supplied
at unlock; it is never stored on disk.

For the client, open `client/Fabulis.xcodeproj` in Xcode and run on
a Simulator or Mac Catalyst destination. Onboarding asks for the
server URL (e.g. `http://your-mac.local:5288`) and the vault
password.

## Project structure

- `Fabulis.slnx` — solution file
- `src/Fabulis.Server/`
  - `Api/` — minimal-API endpoint groups
  - `Auth/` — session token store + `RequireSession` filter
  - `Data/` — `FabulisDbContext`, entity types, services
- `client/Fabulis/`
  - `Models/APIDtos.swift`
  - `Services/` — Keychain + HTTP client
  - `State/AppState.swift`
  - `Views/` — `Onboarding`, `Auth`, `Library`, `Story`, `Draft`,
    `Settings`
- `docs/superpowers/` — design specs and implementation plans

## Architecture spec

`docs/superpowers/specs/2026-05-02-hybrid-architecture-design.md`
```

- [ ] **Step 2: Verify it reads well**

```bash
cat CLAUDE.md
```

---

### Task 14: Final wrap-up commit

- [ ] **Step 1: Build everything one more time end-to-end**

```bash
dotnet build Fabulis.slnx --nologo 2>&1 | tail -5
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -5
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=macOS,variant=Mac Catalyst' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | grep -E "error:|BUILD" | head -5
```

All three: BUILD SUCCEEDED.

- [ ] **Step 2: Commit (single squashed Phase 4 commit, per the user's pattern from previous phases)**

```bash
git add -A
git commit -m "Phase 4 complete: parity + Blazor retirement"
```

---

## Self-review notes

- **Spec coverage.** Categories CRUD ✓ (Task 1), model picker ✓ (Tasks 2, 6), storyteller editor ✓ (Tasks 7), draft message edit/regenerate ✓ (Tasks 3, 8), Blazor retirement ✓ (Tasks 9–11), CLAUDE.md update ✓ (Task 13). Import/export explicitly out of scope and called out at the top.
- **Type consistency.** New DTOs (`CreateCategoryRequest`, `RenameCategoryRequest`, `ModelInfoDto`, `SettingsUpdateRequest`, `StorytellerUpdateRequest`, `ModelInfo`) match field-for-field across server and client, with .NET → camelCase JSON convention.
- **Placeholder scan.** No TBDs. Every code block is complete.
- **Risks.**
  1. The `EditCategorySheet.swift` sheet pattern uses `onSaved` callback; if SwiftUI re-creates the sheet between presentations, state could glitch. Tested mentally: `@State` on the sheet view is fresh per presentation, and `onSaved` is called via `dismiss()` flow — should be fine.
  2. The Blazor delete in Task 9 also removes the import/export services. If anyone tries to use them, they're gone. The architecture spec acknowledges this — the rebuild is a future v2.
  3. `app.UseHttpsRedirection()` removed in Task 10. The native client uses `http://...` per Phase 2's `NSAllowsLocalNetworking` Info.plist exception. If a developer browses `https://localhost:5288/api/v1/auth/status`, they get a connection-refused. Acceptable: Phase 4 is post-Blazor — there's no browser flow anymore.
  4. The `DraftMessageView` becomes generic on `Menu: View`. Existing call sites that don't pass a menu still work via the `EmptyView` extension. Verified by reading current call sites.
