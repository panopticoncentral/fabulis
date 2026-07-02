import SwiftUI

struct TropeCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the trope count changes so the Library sidebar can refresh
    /// this category's count.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: TropeCategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var newText = ""
    @State private var adding = false
    @State private var editingTrope: TropeSummary?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var tropePendingDeletion: TropeSummary?

    var body: some View {
        VStack(spacing: 0) {
            composeBar
            Divider()
            content
        }
        .navigationTitle(detail?.name ?? categoryName)
        .sheet(item: $editingTrope) { trope in
            TropeEditSheet(trope: trope, categoryId: categoryId, onChanged: {
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
                    Text("This deletes the category and all its stories, prompts, one-liners, and tropes. This cannot be undone.")
               })
        .alert("Delete trope?",
               isPresented: Binding(
                    get: { tropePendingDeletion != nil },
                    set: { if !$0 { tropePendingDeletion = nil } }),
               presenting: tropePendingDeletion,
               actions: { trope in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deleteTrope(trope) }
                    }
               },
               message: { _ in
                    Text("This deletes the trope. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private var composeBar: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("New trope", text: $newText, axis: .vertical)
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
            if detail.tropes.isEmpty {
                ContentUnavailableView("No tropes", systemImage: "theatermasks",
                    description: Text("Type a fragment above and tap + to add one."))
            } else {
                List(detail.tropes) { trope in
                    Button {
                        editingTrope = trope
                    } label: {
                        HStack {
                            Text(trope.text)
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
                            tropePendingDeletion = trope
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                    }
                    .contextMenu {
                        Button(role: .destructive) {
                            tropePendingDeletion = trope
                        } label: {
                            Label("Delete Trope", systemImage: "trash")
                        }
                    }
                }
            }
        } else if isLoading {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load tropes").font(.headline)
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
            detail = try await FabulisAPIClient.shared.categoryTropes(categoryId: categoryId)
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
            _ = try await FabulisAPIClient.shared.createTrope(categoryId: categoryId, text: trimmed)
            newText = ""
            await load()
            onChanged?()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deleteTrope(_ trope: TropeSummary) async {
        do {
            try await FabulisAPIClient.shared.deleteTrope(id: trope.id)
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
