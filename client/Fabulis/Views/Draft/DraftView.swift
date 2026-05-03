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
            do { draft = try await FabulisAPIClient.shared.getDraft(id: draftId) } catch {}
            streamingContent = ""
            isStreaming = false
        }
    }
}
