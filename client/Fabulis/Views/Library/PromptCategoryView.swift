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
    @State private var actionError: String?
    @State private var isLoading = true
    @State private var creating = false
    @State private var editingPromptId: Int?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var promptPendingDeletion: PromptSummary?
    @State private var search = ""

    private var filteredPrompts: [PromptSummary] {
        guard let prompts = detail?.prompts else { return [] }
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return prompts }
        return prompts.filter { $0.title.lowercased().contains(q) }
    }

    var body: some View {
        Group {
            if let detail {
                if detail.prompts.isEmpty {
                    ContentUnavailableView("No prompts", systemImage: "text.bubble",
                        description: Text("Choose \u{201C}New Prompt\u{201D} to add one."))
                } else if filteredPrompts.isEmpty {
                    ContentUnavailableView.search(text: search)
                } else {
                    List(filteredPrompts) { prompt in
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
                        .hoverEffect(.highlight)
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
                LoadFailedView(title: "Couldn't load prompts",
                               message: errorMessage) { Task { await load() } }
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .searchable(text: $search, prompt: "Filter prompts")
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
                .fixedSize()
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button { Task { await load() } } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                .keyboardShortcut("r", modifiers: .command)
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { showingRenameSheet = true } label: { Label("Rename", systemImage: "pencil") }
                    Button(role: .destructive) { showingDeleteConfirm = true } label: { Label("Delete", systemImage: "trash") }
                } label: {
                    Label("Category Options", systemImage: "ellipsis.circle")
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
                    Text(LibraryCopy.deleteCategoryWarning)
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
        .actionErrorAlert($actionError)
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
            actionError = error.localizedDescription
        }
    }

    private func deletePrompt(_ prompt: PromptSummary) async {
        do {
            try await FabulisAPIClient.shared.deletePrompt(id: prompt.id)
            await load()
            onChanged?()
        } catch {
            actionError = error.localizedDescription
        }
    }

    private func deleteCategory() async {
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: categoryId)
            if let onDeleted { onDeleted() } else { dismiss() }
        } catch {
            actionError = error.localizedDescription
        }
    }
}
