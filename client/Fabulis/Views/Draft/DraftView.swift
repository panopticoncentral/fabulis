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
    @FocusState private var promptFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
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
                                .id(msg.id)
                            }
                        }
                        if let inFlightPrompt {
                            DraftMessageView(message: DraftMessageDto(
                                id: -1, role: .prompt, content: inFlightPrompt, sortOrder: Int.max))
                            .id("inFlightPrompt")
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
                    if let last = draft?.messages.last {
                        withAnimation { proxy.scrollTo(last.id, anchor: .bottom) }
                    }
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

    private func loadDraft() async {
        do {
            draft = try await FabulisAPIClient.shared.getDraft(id: draftId)
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
        runStream(inFlight: pending, from: stream)
    }

    private func editAndResubmit(messageId: Int, content: String) async {
        let stream = await FabulisAPIClient.shared.editAndResubmit(
            draftId: draftId, messageId: messageId, content: content)
        runStream(inFlight: content, from: stream)
    }

    private func regenerate() async {
        let stream = await FabulisAPIClient.shared.regenerate(draftId: draftId)
        runStream(inFlight: nil, from: stream)
    }

    private func runStream(inFlight: String?, from stream: AsyncThrowingStream<StreamEnvelope, Error>) {
        errorMessage = nil
        streamingContent = ""
        inFlightPrompt = inFlight
        isStreaming = true

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
