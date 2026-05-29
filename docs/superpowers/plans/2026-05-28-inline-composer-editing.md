# Inline Composer Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the full-screen `EditMessageSheet` with inline editing in the draft view's bottom composer, supporting Cancel / Save / Resubmit.

**Architecture:** Pure copy/dimming logic is extracted into a testable `DraftEditLogic` enum (unit-tested with Swift Testing). `DraftView` reuses its existing `prompt` text field as the edit surface, stashing any in-progress new-prompt text; `DraftMessageView` gains highlight/dim flags. No server changes — the `editDraftMessage` (PUT) and `editAndResubmit` (SSE) client methods already exist.

**Tech Stack:** SwiftUI (iOS 18.5+ / Mac Catalyst), Swift Testing for unit tests, `xcodebuild` for compile/test verification.

---

## Spec

`docs/superpowers/specs/2026-05-28-inline-composer-editing-design.md`

## File Structure

- **Create** `client/Fabulis/Views/Draft/DraftEditLogic.swift` — pure, testable
  helpers: messages-after count, banner copy, dim predicate. No SwiftUI, no I/O.
- **Create** `client/FabulisTests/DraftEditLogicTests.swift` — Swift Testing
  coverage for `DraftEditLogic`.
- **Modify** `client/Fabulis/Views/Draft/DraftMessageView.swift` — add
  `isEditing` (highlight) and `isDimmed` (opacity) flags.
- **Modify** `client/Fabulis/Views/Draft/DraftView.swift` — edit state +
  helpers, reworked `inputBar`, per-bubble flags, context-menu guards, remove
  the `EditMessageSheet` presentation.
- **Delete** `client/Fabulis/Views/Draft/EditMessageSheet.swift`.

The Xcode project uses file-system-synchronized groups, so creating a file in
these folders adds it to the target automatically and deleting a file removes
it. No `project.pbxproj` editing is required.

## Build & test commands

Compile-check (no signing, fast):

```bash
xcodebuild build -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'generic/platform=iOS Simulator' -quiet
```

Run unit tests (substitute any booted/available simulator from
`xcrun simctl list devices available` if `iPhone 16` is absent):

```bash
xcodebuild test -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  -only-testing:FabulisTests/DraftEditLogicTests -quiet
```

---

## Task 1: Pure edit-logic helpers (`DraftEditLogic`)

**Files:**
- Create: `client/Fabulis/Views/Draft/DraftEditLogic.swift`
- Test: `client/FabulisTests/DraftEditLogicTests.swift`

- [ ] **Step 1: Write the failing tests**

Create `client/FabulisTests/DraftEditLogicTests.swift`:

```swift
import Testing
@testable import Fabulis

struct DraftEditLogicTests {
    private func msgs() -> [DraftMessageDto] {
        [
            DraftMessageDto(id: 1, role: .prompt, content: "p1", sortOrder: 0),
            DraftMessageDto(id: 2, role: .response, content: "r1", sortOrder: 1),
            DraftMessageDto(id: 3, role: .prompt, content: "p2", sortOrder: 2),
            DraftMessageDto(id: 4, role: .response, content: "r2", sortOrder: 3),
        ]
    }

    @Test func messagesAfterCountsFollowingBubbles() {
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 1) == 3)
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 3) == 1)
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 4) == 0)
    }

    @Test func messagesAfterReturnsZeroForUnknownId() {
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 999) == 0)
    }

    @Test func bannerTextForResponse() {
        #expect(DraftEditLogic.bannerText(role: .response, messagesAfter: 3)
                == "Editing response")
    }

    @Test func bannerTextForPromptWithNoFollowers() {
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 0)
                == "Editing prompt")
    }

    @Test func bannerTextForPromptPluralizes() {
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 1)
                == "Editing prompt — Resubmit will delete 1 message after it")
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 3)
                == "Editing prompt — Resubmit will delete 3 messages after it")
    }

    @Test func dimmedOnlyForBubblesAfterAnEditedPrompt() {
        let m = msgs()
        // Editing prompt id 1: bubbles 2,3,4 dimmed, 1 not.
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 1) == false)
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 2) == true)
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 4) == true)
    }

    @Test func dimmedFalseWhenEditingResponse() {
        let m = msgs()
        #expect(DraftEditLogic.isDimmed(m, editingId: 2, editingRole: .response, bubbleId: 3) == false)
    }

    @Test func dimmedFalseWhenNotEditing() {
        let m = msgs()
        #expect(DraftEditLogic.isDimmed(m, editingId: nil, editingRole: nil, bubbleId: 3) == false)
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
xcodebuild test -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  -only-testing:FabulisTests/DraftEditLogicTests -quiet
```

Expected: build/test FAILS — `cannot find 'DraftEditLogic' in scope`.

- [ ] **Step 3: Write the implementation**

Create `client/Fabulis/Views/Draft/DraftEditLogic.swift`:

```swift
import Foundation

/// Pure helpers for the inline draft-editing composer. Kept free of SwiftUI
/// and I/O so the banner copy and dimming rules can be unit-tested directly.
enum DraftEditLogic {
    /// Number of messages that sort after the message with `editingId`.
    /// Returns 0 when the id is not present.
    static func messagesAfter(_ messages: [DraftMessageDto], editingId: Int) -> Int {
        guard let idx = messages.firstIndex(where: { $0.id == editingId }) else { return 0 }
        return messages.count - idx - 1
    }

    /// Context-banner copy shown above the composer while editing.
    static func bannerText(role: MessageRole, messagesAfter: Int) -> String {
        switch role {
        case .response:
            return "Editing response"
        case .prompt:
            if messagesAfter == 0 { return "Editing prompt" }
            let noun = messagesAfter == 1 ? "message" : "messages"
            return "Editing prompt — Resubmit will delete \(messagesAfter) \(noun) after it"
        }
    }

    /// Whether `bubbleId` should be dimmed: true only when a prompt is being
    /// edited and this bubble sorts after the edited prompt (preview of what
    /// Resubmit will remove). Editing a response never dims anything.
    static func isDimmed(
        _ messages: [DraftMessageDto],
        editingId: Int?,
        editingRole: MessageRole?,
        bubbleId: Int
    ) -> Bool {
        guard let editingId, editingRole == .prompt,
              let editIdx = messages.firstIndex(where: { $0.id == editingId }),
              let thisIdx = messages.firstIndex(where: { $0.id == bubbleId })
        else { return false }
        return thisIdx > editIdx
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
xcodebuild test -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  -only-testing:FabulisTests/DraftEditLogicTests -quiet
```

Expected: TEST SUCCEEDED (8 tests pass).

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Draft/DraftEditLogic.swift \
        client/FabulisTests/DraftEditLogicTests.swift
git commit -m "Add DraftEditLogic helpers for inline composer editing"
```

---

## Task 2: Highlight/dim flags on `DraftMessageView`

**Files:**
- Modify: `client/Fabulis/Views/Draft/DraftMessageView.swift`

- [ ] **Step 1: Add the two flags and thread them through both initializers**

Replace the stored properties and the two `init`s (lines 4–29) with:

```swift
struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let isCurrentlyPlaying: Bool
    let isEditing: Bool
    let isDimmed: Bool
    let menu: () -> Menu

    init(
        message: DraftMessageDto,
        isCurrentlyPlaying: Bool = false,
        isEditing: Bool = false,
        isDimmed: Bool = false,
        @ViewBuilder menu: @escaping () -> Menu
    ) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
        self.isCurrentlyPlaying = isCurrentlyPlaying
        self.isEditing = isEditing
        self.isDimmed = isDimmed
        self.menu = menu
    }

    init(streamingResponse content: String, @ViewBuilder menu: @escaping () -> Menu) {
        self.role = .response
        self.content = content
        self.isStreaming = true
        self.isCurrentlyPlaying = false
        self.isEditing = false
        self.isDimmed = false
        self.menu = menu
    }
```

- [ ] **Step 2: Apply dimming and the editing highlight in `body`**

Replace the `.overlay { ... }` modifier block (lines 54–59) with this, and add
the `.opacity` modifier immediately before it (right after `.clipShape(...)`):

```swift
        .opacity(isDimmed ? 0.4 : 1)
        .overlay {
            if isEditing || isCurrentlyPlaying {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(Color.accentColor, lineWidth: 2)
            }
        }
        .contextMenu { menu() }
```

- [ ] **Step 3: Update the `EmptyView` convenience extension**

Replace the extension (lines 64–71) with:

```swift
extension DraftMessageView where Menu == EmptyView {
    init(
        message: DraftMessageDto,
        isCurrentlyPlaying: Bool = false,
        isEditing: Bool = false,
        isDimmed: Bool = false
    ) {
        self.init(
            message: message,
            isCurrentlyPlaying: isCurrentlyPlaying,
            isEditing: isEditing,
            isDimmed: isDimmed,
            menu: { EmptyView() })
    }
    init(streamingResponse content: String) {
        self.init(streamingResponse: content, menu: { EmptyView() })
    }
}
```

- [ ] **Step 4: Verify it compiles**

```bash
xcodebuild build -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'generic/platform=iOS Simulator' -quiet
```

Expected: BUILD SUCCEEDED (existing call sites still compile — the new
parameters default to `false`).

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Draft/DraftMessageView.swift
git commit -m "Add isEditing/isDimmed flags to DraftMessageView"
```

---

## Task 3: Inline editing in `DraftView`

**Files:**
- Modify: `client/Fabulis/Views/Draft/DraftView.swift`

- [ ] **Step 1: Add the `stashedPrompt` state**

After the `@State private var editingMessage: DraftMessageDto?` line (line 14),
add:

```swift
    @State private var stashedPrompt: String?
```

- [ ] **Step 2: Remove the `EditMessageSheet` presentation**

Delete the entire `.fullScreenCover(item: $editingMessage) { ... }` modifier
(lines 93–101):

```swift
        .fullScreenCover(item: $editingMessage) { msg in
            EditMessageSheet(
                draftId: draftId,
                message: msg,
                onSaved: { Task { await reloadDraft() } },
                onSaveAndResubmit: { newContent in
                    Task { await editAndResubmit(messageId: msg.id, content: newContent) }
                })
        }
```

(Leave the `.sheet(isPresented: $showSaveSheet)` modifier directly above it
untouched — that is the unrelated Save-draft sheet.)

- [ ] **Step 3: Pass highlight/dim flags and guard the Edit menu item**

Replace the `DraftMessageView(...)` call and its context-menu closure inside the
`ForEach` (lines 33–55) with:

```swift
                                DraftMessageView(
                                    message: msg,
                                    isCurrentlyPlaying: player.currentBubbleId == msg.id,
                                    isEditing: editingMessage?.id == msg.id,
                                    isDimmed: DraftEditLogic.isDimmed(
                                        draft.messages,
                                        editingId: editingMessage?.id,
                                        editingRole: editingMessage?.role,
                                        bubbleId: msg.id)
                                ) {
                                    if narrationAvailable, msg.role == .response, msg.id >= 0 {
                                        Button { startNarration(from: msg.id) } label: {
                                            Label("Play from here", systemImage: "play.fill")
                                        }
                                        Divider()
                                    }
                                    if msg.id >= 0 {
                                        Button {
                                            beginEdit(msg)
                                        } label: { Label("Edit", systemImage: "pencil") }
                                            .disabled(isStreaming)
                                    }
                                    if msg.role == .prompt, msg.id >= 0 {
                                        Button {
                                            Task { await editAndResubmit(messageId: msg.id, content: msg.content) }
                                        } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
                                    }
                                    Divider()
                                    Button(role: .destructive) {
                                        Task { await deleteMessage(msg.id) }
                                    } label: { Label("Delete and after", systemImage: "trash") }
                                }
```

(`if let draft` on line 31 already binds `draft` in this scope, so
`draft.messages` is available here.)

- [ ] **Step 4: Rework `inputBar` into banner + mode-dependent buttons**

Replace the entire `inputBar` computed property (lines 115–150) with:

```swift
    private var inputBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let editingMessage {
                HStack(spacing: 6) {
                    Image(systemName: "pencil")
                    Text(DraftEditLogic.bannerText(
                        role: editingMessage.role,
                        messagesAfter: DraftEditLogic.messagesAfter(
                            draft?.messages ?? [], editingId: editingMessage.id)))
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
            HStack(alignment: .bottom, spacing: 8) {
                TextField("Prompt", text: $prompt, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .lineLimit(1...5)
                    .focused($promptFocused)
                    .disabled(isStreaming && editingMessage == nil)
                    .onKeyPress(keys: [.return]) { keyPress in
                        if keyPress.modifiers.contains(.shift) { return .ignored }
                        let trimmed = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
                        guard !trimmed.isEmpty else { return .ignored }
                        if editingMessage != nil {
                            Task { await saveEdit() }
                            return .handled
                        }
                        guard !isStreaming else { return .ignored }
                        Task { await submit() }
                        return .handled
                    }
                    .onKeyPress(.escape) {
                        guard editingMessage != nil else { return .ignored }
                        cancelEdit()
                        return .handled
                    }
                if editingMessage == nil {
                    sendButton
                } else {
                    editButtons
                }
            }
        }
        .padding()
    }

    private var sendButton: some View {
        Button {
            if isStreaming {
                // Generation runs server-side independent of the HTTP
                // request, so cancelling the local Task alone won't stop
                // it. Tell the server to abort, then drop the stream
                // locally — the server's "done" envelope (with the
                // partial response saved) may not reach us once we cancel.
                Task { try? await FabulisAPIClient.shared.abortStream(draftId: draftId) }
                streamTask?.cancel()
            } else {
                Task { await submit() }
            }
        } label: {
            Image(systemName: isStreaming ? "stop.fill" : "paperplane.fill")
                .padding(.horizontal, 4)
        }
        .buttonStyle(.borderedProminent)
        .tint(isStreaming ? .red : .accentColor)
        .disabled(!isStreaming && prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
    }

    @ViewBuilder
    private var editButtons: some View {
        let canSave = !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        Button("Cancel") { cancelEdit() }
            .buttonStyle(.bordered)
        Button("Save") { Task { await saveEdit() } }
            .buttonStyle(.borderedProminent)
            .disabled(!canSave)
        if editingMessage?.role == .prompt {
            Button {
                Task { await resubmitEdit() }
            } label: {
                Label("Resubmit", systemImage: "arrow.clockwise")
            }
            .buttonStyle(.bordered)
            .disabled(!canSave)
        }
    }
```

- [ ] **Step 5: Add the edit lifecycle helpers**

Immediately after the `submit()` function (after line 179), add:

```swift
    private func beginEdit(_ msg: DraftMessageDto) {
        player.stop()
        stashedPrompt = prompt
        prompt = msg.content
        editingMessage = msg
        promptFocused = true
    }

    private func cancelEdit() {
        prompt = stashedPrompt ?? ""
        stashedPrompt = nil
        editingMessage = nil
    }

    private func saveEdit() async {
        guard let msg = editingMessage else { return }
        let content = prompt
        guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        do {
            try await FabulisAPIClient.shared.editDraftMessage(
                draftId: draftId, messageId: msg.id, content: content)
            prompt = stashedPrompt ?? ""
            stashedPrompt = nil
            editingMessage = nil
            await reloadDraft()
        } catch {
            // Stay in edit mode so the user's text is preserved.
            errorMessage = error.localizedDescription
        }
    }

    private func resubmitEdit() async {
        guard let msg = editingMessage else { return }
        let content = prompt
        guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        prompt = stashedPrompt ?? ""
        stashedPrompt = nil
        editingMessage = nil
        await editAndResubmit(messageId: msg.id, content: content)
    }
```

- [ ] **Step 6: Verify it compiles**

```bash
xcodebuild build -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'generic/platform=iOS Simulator' -quiet
```

Expected: BUILD SUCCEEDED. (`EditMessageSheet.swift` is now unreferenced but
still present — it is removed in Task 4.)

- [ ] **Step 7: Commit**

```bash
git add client/Fabulis/Views/Draft/DraftView.swift
git commit -m "Edit draft bubbles inline in the composer (Cancel/Save/Resubmit)"
```

---

## Task 4: Delete the obsolete `EditMessageSheet`

**Files:**
- Delete: `client/Fabulis/Views/Draft/EditMessageSheet.swift`

- [ ] **Step 1: Remove the file**

```bash
git rm client/Fabulis/Views/Draft/EditMessageSheet.swift
```

- [ ] **Step 2: Verify nothing references it and it still compiles**

```bash
grep -rn "EditMessageSheet" client/Fabulis || echo "no references"
xcodebuild build -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'generic/platform=iOS Simulator' -quiet
```

Expected: `no references`, then BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Remove EditMessageSheet, replaced by inline composer editing"
```

---

## Task 5: Manual verification (Mac Catalyst + iOS Simulator)

No code changes. Run the server (`dotnet run --project src/Fabulis.Server`),
launch the client, unlock the vault, open a draft with several messages, and
confirm each row of the spec's Testing section:

- [ ] Edit a **prompt → Save**: text changes in place; later bubbles unchanged.
- [ ] Edit a **prompt → Resubmit**: bubbles after it disappear; a new response streams.
- [ ] Edit a **prompt → Cancel**: nothing changes.
- [ ] Type a few words into the composer, then Edit a bubble, then **Cancel** /
      **Save**: the half-typed text is restored to the composer afterward.
- [ ] Edit a **response → Save**: text changes; **no Resubmit button** is shown
      and the banner reads "Editing response".
- [ ] In edit mode: **Return** commits Save; **Shift+Return** inserts a newline;
      **Esc** cancels.
- [ ] While editing a prompt: bubbles after it are **dimmed**, the edited bubble
      is **highlighted**; both clear after exiting edit mode.
- [ ] The **Edit** context-menu item is absent on an in-flight/optimistic bubble
      and disabled while a response is streaming.

- [ ] **Commit** (only if manual testing surfaced fixes; otherwise nothing to commit).

---

## Notes for the implementer

- This codebase has no existing unit tests beyond a placeholder; `DraftView` /
  `DraftMessageView` are view-layer and verified by compiling + the manual pass.
  The genuinely testable logic (banner copy, dim predicate, after-count) lives
  in `DraftEditLogic` and is covered by Task 1.
- The server already supports every operation; do **not** add or change
  endpoints. `editDraftMessage` → `PUT` (in-place), `editAndResubmit` → SSE
  (truncate + regenerate) are both pre-existing on `FabulisAPIClient`.
- `editAndResubmit(messageId:content:)` on `DraftView` is the existing method
  that optimistically truncates and drives the stream — reuse it as-is.
```