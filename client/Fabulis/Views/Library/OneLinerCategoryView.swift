import SwiftUI

struct OneLinerCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the one-liner count changes so the Library sidebar can
    /// refresh this category's count.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: OneLinerCategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var newText = ""
    @State private var adding = false
    @State private var editingOneLiner: OneLinerSummary?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var oneLinerPendingDeletion: OneLinerSummary?

    var body: some View {
        VStack(spacing: 0) {
            composeBar
            Divider()
            content
        }
        .navigationTitle(detail?.name ?? categoryName)
        .sheet(item: $editingOneLiner) { oneLiner in
            OneLinerEditSheet(oneLiner: oneLiner, categoryId: categoryId, onChanged: {
                Task { await load() }
                onChanged?()
            })
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
                    Text("This deletes the category and all its stories, prompts, and one-liners. This cannot be undone.")
               })
        .alert("Delete one-liner?",
               isPresented: Binding(
                    get: { oneLinerPendingDeletion != nil },
                    set: { if !$0 { oneLinerPendingDeletion = nil } }),
               presenting: oneLinerPendingDeletion,
               actions: { oneLiner in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deleteOneLiner(oneLiner) }
                    }
               },
               message: { _ in
                    Text("This deletes the one-liner. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private var composeBar: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("New one-liner", text: $newText, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(1...4)
                .onSubmit { Task { await add() } }
            Button {
                Task { await add() }
            } label: {
                if adding {
                    ProgressView().controlSize(.mini)
                } else {
                    Image(systemName: "plus.circle.fill").font(.title2)
                }
            }
            .disabled(adding
                || newText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private var content: some View {
        if let detail {
            if detail.oneLiners.isEmpty {
                ContentUnavailableView("No one-liners", systemImage: "quote.bubble",
                    description: Text("Type a line above and tap + to add one."))
            } else {
                List(detail.oneLiners) { oneLiner in
                    Button {
                        editingOneLiner = oneLiner
                    } label: {
                        HStack {
                            Text(oneLiner.text)
                                .font(.body)
                                .lineLimit(1...3)
                            Spacer()
                            Image(systemName: "chevron.right")
                                .font(.caption).foregroundStyle(.tertiary)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .swipeActions(edge: .trailing) {
                        Button(role: .destructive) {
                            oneLinerPendingDeletion = oneLiner
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                    }
                    .contextMenu {
                        Button(role: .destructive) {
                            oneLinerPendingDeletion = oneLiner
                        } label: {
                            Label("Delete One-liner", systemImage: "trash")
                        }
                    }
                }
            }
        } else if isLoading {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load one-liners").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                Button("Retry") { Task { await load() } }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.categoryOneLiners(categoryId: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func add() async {
        let trimmed = newText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !adding else { return }
        adding = true; defer { adding = false }
        do {
            _ = try await FabulisAPIClient.shared.createOneLiner(categoryId: categoryId, text: trimmed)
            newText = ""
            await load()
            onChanged?()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deleteOneLiner(_ oneLiner: OneLinerSummary) async {
        do {
            try await FabulisAPIClient.shared.deleteOneLiner(id: oneLiner.id)
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
