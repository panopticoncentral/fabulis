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
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 12) {
                    if let draft {
                        ForEach(Array(draft.messages.enumerated()), id: \.element.id) { idx, msg in
                            let isLast = idx == draft.messages.count - 1
                            let isLastResponse = isLast && msg.role == .response
                            DraftMessageView(message: msg) {
                                Button {
                                    editingMessage = msg
                                } label: { Label("Edit", systemImage: "pencil") }
                                if isLastResponse {
                                    Button {
                                        Task { await regenerate() }
                                    } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
                                }
                                Divider()
                                Button(role: .destructive) {
                                    Task { await deleteMessage(msg.id) }
                                } label: { Label("Delete and after", systemImage: "trash") }
                            }
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
        .fullScreenCover(item: $editingMessage) { msg in
            EditMessageSheet(
                draftId: draftId,
                message: msg,
                onSaved: { Task { await reloadDraft() } },
                onSaveAndResubmit: { newContent in
                    Task { await editAndResubmit(messageId: msg.id, content: newContent) }
                })
        }
        .task { await loadDraft() }
        .onDisappear { streamTask?.cancel() }
    }

    private var inputBar: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("Prompt", text: $prompt, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(1...5)
                .focused($promptFocused)
                .disabled(isStreaming)
                .onKeyPress(keys: [.return]) { keyPress in
                    if keyPress.modifiers.contains(.shift) { return .ignored }
                    let trimmed = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !trimmed.isEmpty, !isStreaming else { return .ignored }
                    Task { await submit() }
                    return .handled
                }
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
        .padding()
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
        let pending = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !pending.isEmpty else { return }
        prompt = ""
        let stream = await FabulisAPIClient.shared.streamMessage(draftId: draftId, prompt: pending)
        runStream(inFlight: pending, initial: stream)
    }

    private func editAndResubmit(messageId: Int, content: String) async {
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

    private func regenerate() async {
        if var d = draft, d.messages.last?.role == .response {
            d.messages.removeLast()
            draft = d
        }
        let stream = await FabulisAPIClient.shared.regenerate(draftId: draftId)
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

            do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
            inFlightPrompt = nil
            streamingContent = ""
            isStreaming = false
        }
    }

    private func deleteMessage(_ messageId: Int) async {
        do {
            try await FabulisAPIClient.shared.deleteDraftMessage(draftId: draftId, messageId: messageId)
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
