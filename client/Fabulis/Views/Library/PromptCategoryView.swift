import SwiftUI

struct PromptCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the prompt count changes (e.g. a prompt is created) so the
    /// Library sidebar can refresh this category's count without a full reload.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: PromptCategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var creating = false
    @State private var editingPromptId: Int?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var promptPendingDeletion: PromptSummary?

    var body: some View {
        Group {
            if let detail {
                if detail.prompts.isEmpty {
                    ContentUnavailableView("No prompts", systemImage: "text.bubble",
                        description: Text("Tap \u{201C}New Prompt\u{201D} to add one."))
                } else {
                    List(detail.prompts) { prompt in
                        Button {
                            editingPromptId = prompt.id
                        } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(prompt.title).font(.body)
                                    Text("\(prompt.messageCount) \(prompt.messageCount == 1 ? "message" : "messages")")
                                        .font(.caption2).foregroundStyle(.secondary)
                                }
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .font(.caption).foregroundStyle(.tertiary)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                promptPendingDeletion = prompt
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                        .contextMenu {
                            Button(role: .destructive) {
                                promptPendingDeletion = prompt
                            } label: {
                                Label("Delete Prompt", systemImage: "trash")
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load prompts").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .navigationDestination(item: $editingPromptId) { id in
            PromptEditorView(promptId: id, onSaved: {
                editingPromptId = nil
                Task { await load() }
            })
        }
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    Task { await createPrompt() }
                } label: {
                    HStack(spacing: 4) {
                        if creating { ProgressView().controlSize(.mini) }
                        else { Image(systemName: "plus") }
                        Text("New Prompt")
                    }
                }
                .disabled(creating)
            }
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
                    Text("This deletes the category and all its stories and prompts. This cannot be undone.")
               })
        .alert("Delete prompt?",
               isPresented: Binding(
                    get: { promptPendingDeletion != nil },
                    set: { if !$0 { promptPendingDeletion = nil } }),
               presenting: promptPendingDeletion,
               actions: { prompt in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deletePrompt(prompt) }
                    }
               },
               message: { _ in
                    Text("This deletes the prompt and its messages. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.categoryPrompts(categoryId: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func createPrompt() async {
        creating = true; defer { creating = false }
        do {
            let created = try await FabulisAPIClient.shared.createPrompt(categoryId: categoryId, title: nil)
            await load()
            onChanged?()
            editingPromptId = created.id
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deletePrompt(_ prompt: PromptSummary) async {
        do {
            try await FabulisAPIClient.shared.deletePrompt(id: prompt.id)
            await load()
            onChanged?()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deleteCategory() async {
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: categoryId)
            if let onDeleted { onDeleted() } else { dismiss() }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
