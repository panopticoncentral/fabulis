import SwiftUI

struct DraftsView: View {
    var onDraftsChanged: () -> Void

    @State private var drafts: [DraftSummary] = []
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if drafts.isEmpty && !isLoading && errorMessage == nil {
                ContentUnavailableView("No drafts", systemImage: "doc.text",
                    description: Text("Tap “New Draft” to start a story."))
            } else if let errorMessage, drafts.isEmpty {
                VStack(spacing: 12) {
                    Text("Couldn't load drafts").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            } else if isLoading && drafts.isEmpty {
                ProgressView()
            } else {
                List(drafts) { draft in
                    NavigationLink(value: draft.id) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text(draft.title ?? "Untitled draft").font(.body)
                            Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                                .font(.caption2).foregroundStyle(.secondary)
                        }
                    }
                    .swipeActions(edge: .trailing) {
                        Button(role: .destructive) {
                            Task { await deleteDraft(draft) }
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                    }
                    .contextMenu {
                        Button(role: .destructive) {
                            Task { await deleteDraft(draft) }
                        } label: {
                            Label("Delete Draft", systemImage: "trash")
                        }
                    }
                }
            }
        }
        .navigationTitle("Drafts")
        .navigationDestination(for: Int.self) { draftId in
            DraftView(draftId: draftId).id(draftId)
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            drafts = try await FabulisAPIClient.shared.listDrafts()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteDraft(_ draft: DraftSummary) async {
        drafts.removeAll { $0.id == draft.id }
        do {
            try await FabulisAPIClient.shared.deleteDraft(id: draft.id)
            onDraftsChanged()
        } catch {
            errorMessage = error.localizedDescription
            await load()
        }
    }
}
