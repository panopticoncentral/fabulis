import SwiftUI

struct DraftView: View {
    let draftId: Int

    @State private var draft: DraftDetail?
    @State private var prompt: String = ""
    @State private var inFlightPrompt: String?
    @State private var streamingContent: String = ""
    @State private var isStreaming = false
    @State private var streamTask: Task<Void, Never>?
    @State private var errorMessage: String?
    @State private var showSaveSheet = false
    @State private var editingMessage: DraftMessageDto?
    @State private var stashedPrompt: String?
    @State private var narrationAvailable = false
    @State private var player = NarrationPlayer()
    /// Starts unset. `loadDraft` flips it to the .bottom edge once messages
    /// arrive — initializing with .bottom directly is a no-op (the ScrollView
    /// applies it against empty content, then sees no binding change when the
    /// data lands). Subsequent user scrolls turn it into a fixed point so
    /// streaming chunks don't drag them back; scrolling near the bottom
    /// re-snaps to the .bottom edge so rotation re-anchors there.
    @State private var scrollPosition = ScrollPosition()
    @FocusState private var promptFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        if let draft {
                            ForEach(draft.messages, id: \.id) { msg in
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
                                .id(msg.id)
                            }
                        }
                        if let inFlightPrompt {
                            DraftMessageView(message: DraftMessageDto(
                                id: -1, role: .prompt, content: inFlightPrompt, sortOrder: Int.max))
                        }
                        if isStreaming {
                            DraftMessageView(streamingResponse: streamingContent)
                        }
                        if let errorMessage {
                            Text(errorMessage).foregroundStyle(.red).padding(.top, 8)
                        }
                    }
                    .padding()
                }
                .scrollPosition($scrollPosition, anchor: .bottom)
                .onChange(of: player.currentBubbleId) { _, new in
                    if let new {
                        withAnimation { proxy.scrollTo(new, anchor: .center) }
                    }
                }
            }

            Divider()
            inputBar
        }
        .navigationTitle(draft?.title ?? "New Draft")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button("Save") { showSaveSheet = true }
                    .disabled((draft?.messages.isEmpty ?? true) || isStreaming)
            }
        }
        .sheet(isPresented: $showSaveSheet) {
            SaveDraftSheet(draftId: draftId, draftTitle: draft?.title)
        }
        .task { await loadDraft() }
        .onDisappear {
            streamTask?.cancel()
            player.stop()
        }
        .safeAreaInset(edge: .bottom) {
            if player.isVisible {
                NarrationBar(player: player)
            }
        }
        .task { await loadNarrationAvailability() }
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

    private func loadDraft() async {
        do {
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
            // Pin to the bottom edge. The ScrollView already laid out at
            // offset 0 against the (then-empty) content, so we have to push
            // it explicitly here — and we couldn't initialize at .bottom
            // because that would already match this assignment, leaving the
            // binding unchanged and the scroll un-applied.
            scrollPosition.scrollTo(edge: .bottom)
            promptFocused = true
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func reloadDraft() async {
        do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) }
        catch { errorMessage = error.localizedDescription }
    }

    private func submit() async {
        player.stop()
        let pending = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !pending.isEmpty else { return }
        prompt = ""
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

    private func editAndResubmit(messageId: Int, content: String) async {
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
            } else {
                do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
                inFlightPrompt = nil
                streamingContent = ""
                isStreaming = false
            }
        }
    }

    private func deleteMessage(_ messageId: Int) async {
        player.stop()
        do {
            try await FabulisAPIClient.shared.deleteDraftMessage(draftId: draftId, messageId: messageId)
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
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
        player.start(bubbles: responses, from: bubbleId)
    }
}
