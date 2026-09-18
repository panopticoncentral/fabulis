import SwiftUI

struct DraftView: View {
    let draftId: Int
    /// Called when the draft settles to a new server-truth state so the
    /// Library sidebar can refresh this draft's row (title, message count)
    /// without waiting for a full reload.
    var onDraftChanged: (DraftSummary) -> Void = { _ in }
    /// Called when saving the draft into the library changes category contents
    /// (a new story, or a new category) so the Library sidebar can refresh its
    /// counts and category list.
    var onLibraryChanged: () -> Void = {}

    @State private var draft: DraftDetail?
    @Environment(AppState.self) private var appState
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State var composition: DraftComposition
    private var prompt: String {
        get { composition.text }
        nonmutating set { composition.text = newValue }
    }
    @State private var inFlightPrompt: String?
    @State private var streamingContent: String = ""
    @State private var isStreaming = false
    @State private var streamTask: Task<Void, Never>?
    @State private var errorMessage: String?
    @State private var showSaveSheet = false
    private var editingMessage: DraftMessageDto? {
        get { composition.editingMessage }
        nonmutating set { composition.editingMessage = newValue }
    }
    private var stashedPrompt: String? {
        get { composition.stashedText }
        nonmutating set { composition.stashedText = newValue }
    }
    @State private var showingResubmitConfirm = false
    @State private var isSavingEdit = false
    @State private var generationStatus = "Preparing response…"
    @State private var narrationAvailable = false
    @State private var messagePendingDeletion: DraftMessageDto?
    @State private var messagePendingRegenerate: DraftMessageDto?
    private var player: NarrationPlayer { appState.narration }
    private var narrationSource: String { "draft:\(draftId)" }
    private var playingBubbleId: Int? {
        appState.narrationSource == narrationSource ? player.currentBubbleId : nil
    }
    /// Starts unset. `loadDraft` flips it to the .bottom edge once messages
    /// arrive — initializing with .bottom directly is a no-op (the ScrollView
    /// applies it against empty content, then sees no binding change when the
    /// data lands). Subsequent user scrolls turn it into a fixed point so
    /// streaming chunks don't drag them back; scrolling near the bottom
    /// re-snaps to the .bottom edge so rotation re-anchors there.
    @State private var scrollPosition = ScrollPosition()
    // A plain @State Bool, not @FocusState: the prompt field is now a UIKit-
    // backed PromptComposer (it intercepts Return on Mac Catalyst), which
    // tracks focus through this binding instead of SwiftUI's focus system.
    @State private var promptFocused = false

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        if let draft {
                            if !draft.modelName.isEmpty {
                                Text("Model: \(draft.modelName)")
                                    .font(.caption).foregroundStyle(.secondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            ForEach(draft.messages, id: \.id) { msg in
                                DraftMessageView(
                                    message: msg,
                                    isCurrentlyPlaying: playingBubbleId == msg.id,
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
                                            .disabled(isStreaming || editingMessage != nil)
                                    }
                                    if msg.role == .prompt, msg.id >= 0 {
                                        Button {
                                            requestRegenerate(msg)
                                        } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
                                            .disabled(isStreaming || editingMessage != nil)
                                    }
                                    if msg.id >= 0 {
                                        Divider()
                                        Button(role: .destructive) {
                                            messagePendingDeletion = msg
                                        } label: { Label("Delete from Here…", systemImage: "trash") }
                                            .disabled(isStreaming || editingMessage != nil)
                                    }
                                }
                                .id(msg.id)
                            }
                        }
                        if let inFlightPrompt {
                            DraftMessageView(message: DraftMessageDto(
                                id: -1, role: .prompt, content: inFlightPrompt, sortOrder: Int.max))
                        }
                        if isStreaming {
                            DraftMessageView(streamingResponse: streamingContent, status: generationStatus)
                        }
                    }
                    .padding()
                    // Cap the reading column and center it: unconstrained bubbles
                    // produce very long lines in a wide Mac/iPad window.
                    .frame(maxWidth: 720)
                    .frame(maxWidth: .infinity)
                }
                .scrollPosition($scrollPosition, anchor: .bottom)
                .onChange(of: playingBubbleId) { _, new in
                    if let new {
                        withAnimation(reduceMotion ? nil : .default) { proxy.scrollTo(new, anchor: .center) }
                    }
                }
                .overlay { draftPlaceholder }
            }

            if draft != nil, let errorMessage {
                errorBanner(errorMessage)
            }
            Divider()
            inputBar
        }
        .navigationTitle(draft?.title ?? "Untitled Draft")
        .navigationBarTitleDisplayMode(.inline)
        .focusedSceneValue(\.contentActions, ContentActions(
            saveChanges: editingMessage != nil && canSaveEdit ? { Task { await saveEdit() } } : nil,
            saveToLibrary: !(draft?.messages.isEmpty ?? true) && !isStreaming && editingMessage == nil ? { showSaveSheet = true } : nil))
        .alert("Delete message?",
               isPresented: Binding(
                    get: { messagePendingDeletion != nil },
                    set: { if !$0 { messagePendingDeletion = nil } }),
               presenting: messagePendingDeletion,
               actions: { msg in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deleteMessage(msg.id) }
                    }
               },
               message: { msg in
                    Text(deleteMessageWarning(for: msg))
               })
        .alert("Regenerate from here?",
               isPresented: Binding(
                    get: { messagePendingRegenerate != nil },
                    set: { if !$0 { messagePendingRegenerate = nil } }),
               presenting: messagePendingRegenerate,
               actions: { msg in
                    Button("Cancel", role: .cancel) {}
                    Button("Regenerate", role: .destructive) {
                        Task { await editAndResubmit(messageId: msg.id, content: msg.content) }
                    }
               },
               message: { msg in
                    Text(regenerateWarning(for: msg))
               })
        .toolbar {
            if let last = draft?.messages.last, last.role == .prompt {
                ToolbarItem(placement: .primaryAction) {
                    Button("Generate Response", systemImage: "sparkles") { requestRegenerate(last) }
                        .disabled(isStreaming || editingMessage != nil)
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button("Save to Library…", systemImage: "square.and.arrow.down") { showSaveSheet = true }
                    .disabled((draft?.messages.isEmpty ?? true) || isStreaming || editingMessage != nil)
                    .help("Save a story or a new version to the library")
            }
        }
        .sheet(isPresented: $showSaveSheet) {
            SaveDraftSheet(draftId: draftId, draftTitle: draft?.title, onSaved: onLibraryChanged)
        }
        .task { await loadDraft() }
        .onDisappear {
            streamTask?.cancel()
        }
        .task { await loadNarrationAvailability() }
        .alert("Regenerate from here?", isPresented: $showingResubmitConfirm) {
            Button("Cancel", role: .cancel) {}
            Button("Regenerate", role: .destructive) { Task { await resubmitEdit() } }
        } message: {
            if let editingMessage { Text(regenerateWarning(for: editingMessage)) }
        }
    }

    /// Loading spinner while the draft is first fetched, and a starter empty
    /// state for a brand-new draft with nothing generating yet.
    @ViewBuilder
    private var draftPlaceholder: some View {
        if draft == nil, let errorMessage {
            LoadFailedView(title: "Couldn't load draft", message: errorMessage) {
                self.errorMessage = nil
                Task { await loadDraft() }
            }
        } else if draft == nil {
            ProgressView("Loading draft…")
        } else if draft?.messages.isEmpty == true, inFlightPrompt == nil, !isStreaming {
            ContentUnavailableView("Start your story", systemImage: "sparkles",
                description: Text("Type a prompt below to begin."))
        }
    }

    private var inputBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let editingMessage {
                HStack(spacing: 6) {
                    Image(systemName: "pencil")
                    Text(DraftEditLogic.bannerText(
                        role: editingMessage.role,
                        messagesAfter: DraftEditLogic.messagesAfter(
                            draft?.messages ?? [], editingId: editingMessage.id)))
                        .fixedSize(horizontal: false, vertical: true)
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
            HStack(alignment: .bottom, spacing: 8) {
                PromptComposer(
                    text: Binding(get: { prompt }, set: { prompt = $0 }),
                    placeholder: editingMessage?.role == .response ? "Edit response" : (editingMessage == nil ? "Write a prompt…" : "Edit prompt"),
                    isFocused: $promptFocused,
                    isEditable: !isStreaming && !isSavingEdit && draft != nil,
                    onReturn: handleReturn,
                    handlesEscape: editingMessage != nil,
                    onEscape: cancelEdit)
                if editingMessage == nil {
                    sendButton
                } else if sizeClass != .compact {
                    editButtons
                }
            }
            if editingMessage != nil && sizeClass == .compact {
                ViewThatFits(in: .horizontal) {
                    HStack { Spacer(); editButtons }
                    VStack(alignment: .trailing, spacing: 8) {
                        HStack { cancelEditButton; Spacer(); saveEditButton }
                        regenerateEditButton
                    }
                    .frame(maxWidth: .infinity, alignment: .trailing)
                }
            }
            #if targetEnvironment(macCatalyst)
            Text(editingMessage == nil ? "Return to send · Shift-Return for a new line" : "Return to save · Shift-Return for a new line")
                .font(.caption).foregroundStyle(.secondary)
            #endif
        }
        .padding()
        .frame(maxWidth: 720)
        .frame(maxWidth: .infinity)
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
        .buttonBorderShape(.circle)
        .touchTarget()
        .tint(isStreaming ? .red : .accentColor)
        .disabled(!isStreaming && (draft == nil || prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty))
        .accessibilityLabel(isStreaming ? "Stop generating" : "Send")
    }

    private func errorBanner(_ message: String) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
            Text(message)
                .font(.callout)
                .textSelection(.enabled)
            Spacer(minLength: 8)
            Button {
                errorMessage = nil
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.secondary)
                    .touchTarget()
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Dismiss error")
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.orange.opacity(0.12))
    }

    /// Routes context-menu Regenerate: it silently drops every message after the
    /// prompt, so confirm first when there is anything to lose; otherwise run it
    /// directly (regenerating the last prompt destroys nothing).
    private func requestRegenerate(_ msg: DraftMessageDto) {
        let after = DraftEditLogic.messagesAfter(draft?.messages ?? [], editingId: msg.id)
        if after > 0 {
            messagePendingRegenerate = msg
        } else {
            Task { await editAndResubmit(messageId: msg.id, content: msg.content) }
        }
    }

    private func deleteMessageWarning(for msg: DraftMessageDto) -> String {
        let after = DraftEditLogic.messagesAfter(draft?.messages ?? [], editingId: msg.id)
        if after == 0 {
            return "This deletes this message. This cannot be undone."
        }
        let noun = after == 1 ? "message" : "messages"
        return "This deletes this message and the \(after) \(noun) after it. This cannot be undone."
    }

    private func regenerateWarning(for msg: DraftMessageDto) -> String {
        let after = DraftEditLogic.messagesAfter(draft?.messages ?? [], editingId: msg.id)
        let noun = after == 1 ? "message" : "messages"
        return "This regenerates from this prompt and deletes the \(after) \(noun) after it. This cannot be undone."
    }

    @ViewBuilder
    private var editButtons: some View {
        cancelEditButton
        saveEditButton
        regenerateEditButton
    }

    private var canSaveEdit: Bool {
        !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !isSavingEdit && !isStreaming
    }

    private var cancelEditButton: some View {
        Button("Cancel") { cancelEdit() }
            .buttonStyle(.bordered)
            .disabled(isSavingEdit)
    }

    private var saveEditButton: some View {
        Button(isSavingEdit ? "Saving…" : "Save Changes") { Task { await saveEdit() } }
            .buttonStyle(.borderedProminent)
            .disabled(!canSaveEdit)
    }

    @ViewBuilder
    private var regenerateEditButton: some View {
        if editingMessage?.role == .prompt {
            Button {
                if let editingMessage, DraftEditLogic.messagesAfter(draft?.messages ?? [], editingId: editingMessage.id) > 0 {
                    showingResubmitConfirm = true
                } else { Task { await resubmitEdit() } }
            } label: {
                Label("Regenerate from Here…", systemImage: "arrow.clockwise")
            }
            .buttonStyle(.bordered)
            .disabled(!canSaveEdit)
        }
    }

    private func loadDraft() async {
        errorMessage = nil
        do {
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
            // Pin to the bottom edge. The ScrollView already laid out at
            // offset 0 against the (then-empty) content, so we have to push
            // it explicitly here — and we couldn't initialize at .bottom
            // because that would already match this assignment, leaving the
            // binding unchanged and the scroll un-applied.
            scrollPosition.scrollTo(edge: .bottom)
            promptFocused = draft?.messages.isEmpty == true || editingMessage != nil
            notifyDraftChanged()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func reloadDraft() async {
        do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) }
        catch { errorMessage = error.localizedDescription }
        notifyDraftChanged()
    }

    /// Push the draft's current summary up to the Library sidebar. Counts only
    /// persisted messages (id >= 0) so optimistic placeholders don't inflate
    /// the row's count; title/timestamps come straight from the server detail.
    private func notifyDraftChanged() {
        guard let draft else { return }
        onDraftChanged(DraftSummary(
            id: draft.id,
            title: draft.title,
            createdAt: draft.createdAt,
            updatedAt: draft.updatedAt,
            messageCount: draft.messages.filter { $0.id >= 0 }.count))
    }

    /// Plain Return: submit a new prompt, or save an in-progress edit. Empty
    /// prompts and Return during streaming are no-ops. (Shift+Return never
    /// reaches here — it's handled by the field as a newline.)
    private func handleReturn() {
        guard !isStreaming && !isSavingEdit else { return }
        let trimmed = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        if editingMessage != nil {
            Task { await saveEdit() }
            return
        }
        guard !isStreaming && !isSavingEdit else { return }
        Task { await submit() }
    }

    private func submit() async {
        guard !isStreaming, draft != nil else { return }
        player.stop()
        let pending = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !pending.isEmpty else { return }
        prompt = ""
        isStreaming = true
        let stream = await FabulisAPIClient.shared.streamMessage(draftId: draftId, prompt: pending)
        runStream(inFlight: pending, initial: stream)
    }

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
        guard !isSavingEdit else { return }
        isSavingEdit = true; defer { isSavingEdit = false }
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
        await editAndResubmit(messageId: msg.id, content: content)
        // Retain the edit buffer until the server accepts and completes it.

    }

    private func editAndResubmit(messageId: Int, content: String) async {
        guard !isStreaming else { return }
        isStreaming = true
        player.stop()
        // Optimistically reflect the server-side mutation: rewrite the edited
        // message's content and drop everything after it. The streamed response
        // arrives via the streaming pane; no separate inFlightPrompt needed
        // because the user's prompt already lives in draft.messages.
        if var d = draft, let idx = d.messages.firstIndex(where: { $0.id == messageId }) {
            let original = d.messages[idx]
            let edited = DraftMessageDto(
                id: original.id,
                role: original.role,
                content: content,
                sortOrder: original.sortOrder)
            d.messages = Array(d.messages.prefix(idx)) + [edited]
            draft = d
        }
        let stream = await FabulisAPIClient.shared.editAndResubmit(
            draftId: draftId, messageId: messageId, content: content)
        runStream(inFlight: nil, initial: stream)
    }

    /// Drives the streaming UI. The server-side generation is decoupled from
    /// the HTTP request, so a dropped connection (phone locked, app
    /// backgrounded) is recoverable: we re-attach via `streamReattach` which
    /// replays a `snapshot` of the content so far and continues with deltas.
    /// We exit on `done`/`error` envelopes from the server, on user Stop, or
    /// when reattach reports 404 (no in-flight generation — fall through to
    /// refreshing the draft and showing whatever the server saved).
    private func runStream(inFlight: String?, initial: AsyncThrowingStream<StreamEnvelope, Error>?) {
        errorMessage = nil
        streamingContent = ""
        generationStatus = "Preparing response…"
        inFlightPrompt = inFlight
        isStreaming = true

        streamTask = Task {
            var current = initial
            var done = false
            var stoppedByUser = false
            // Bound the retry loop so a downed server can't trap us forever.
            // Reset to 0 every time we successfully receive an envelope.
            var consecutiveFailures = 0
            let maxFailures = 5

            while !done {
                if Task.isCancelled { stoppedByUser = true; break }

                if current == nil {
                    // streamReattach returns the stream synchronously (no
                    // throw) — errors surface on iteration below.
                    current = await FabulisAPIClient.shared.streamReattach(draftId: draftId)
                }

                do {
                    for try await env in current! {
                        if Task.isCancelled { stoppedByUser = true; break }
                        consecutiveFailures = 0
                        switch env.kind {
                        case "snapshot":
                            streamingContent = env.text ?? ""
                        case "chunk":
                            generationStatus = "Generating…"
                            if env.reasoning != true, let text = env.text {
                                streamingContent += text
                            }
                        case "done":
                            done = true
                        case "error":
                            errorMessage = env.text ?? "Unknown error"
                            done = true
                        default: break
                        }
                    }
                    if !done && !stoppedByUser {
                        // Stream ended without a terminal envelope (network
                        // drop). Loop back to reattach.
                        current = nil
                    }
                } catch is CancellationError {
                    stoppedByUser = true
                } catch APIError.server(let status, _) where status == 404 {
                    // Nothing in flight server-side. Generation finished
                    // before we reattached — getDraft will pick up whatever
                    // was saved.
                    break
                } catch {
                    if Task.isCancelled { stoppedByUser = true; break }
                    generationStatus = "Reconnecting…"
                    consecutiveFailures += 1
                    if consecutiveFailures >= maxFailures {
                        errorMessage = error.localizedDescription
                        break
                    }
                    // Transient network failure. Brief backoff, then reattach.
                    try? await Task.sleep(for: .seconds(1))
                    current = nil
                }
            }

            if stoppedByUser && !streamingContent.isEmpty {
                // The server cancels generation asynchronously and persists
                // the partial response a beat later, so an immediate getDraft
                // races (and loses) that save — clearing streamingContent
                // here would make the bubble vanish until a later refetch.
                // Instead, promote the text we already streamed to a real
                // message so it stays on screen, then reconcile to the
                // server's authoritative copy (real ids) once it lands.
                if var d = draft {
                    if let inFlightPrompt {
                        d.messages.append(DraftMessageDto(
                            id: -3, role: .prompt, content: inFlightPrompt,
                            sortOrder: Int.max - 1))
                    }
                    d.messages.append(DraftMessageDto(
                        id: -2, role: .response, content: streamingContent,
                        sortOrder: Int.max))
                    draft = d
                }
                let optimisticCount = draft?.messages.count ?? 0
                inFlightPrompt = nil
                streamingContent = ""
                isStreaming = false
                for _ in 0..<20 {
                    if let fetched = try? await FabulisAPIClient.shared.getDraft(id: draftId),
                       fetched.messages.count >= optimisticCount {
                        draft = fetched
                        break
                    }
                    try? await Task.sleep(for: .milliseconds(250))
                }
                notifyDraftChanged()
            } else {
                do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
                inFlightPrompt = nil
                streamingContent = ""
                isStreaming = false
                notifyDraftChanged()
                // Streaming content mutates silently for VoiceOver; announce the
                // terminal state so it's perceivable without watching the screen.
                if errorMessage == nil, !stoppedByUser {
                    if editingMessage != nil {
                        prompt = stashedPrompt ?? ""
                        stashedPrompt = nil
                        editingMessage = nil
                    }
                    AccessibilityNotification.Announcement("Response complete").post()
                }
            }
        }
    }

    private func deleteMessage(_ messageId: Int) async {
        player.stop()
        do {
            try await FabulisAPIClient.shared.deleteDraftMessage(draftId: draftId, messageId: messageId)
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
            notifyDraftChanged()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func loadNarrationAvailability() async {
        if let s = try? await FabulisAPIClient.shared.settings() {
            narrationAvailable = s.narrationAvailable
        }
    }

    private func startNarration(from bubbleId: Int) {
        guard let draft else { return }
        let responses = draft.messages
            .filter { $0.role == .response && $0.id >= 0 }
            .map { (id: $0.id, text: $0.content) }
        appState.narrationSource = narrationSource
        player.start(bubbles: responses, from: bubbleId, title: draft.title)
    }
}
