import SwiftUI

/// A library item that is just a line of text under a category (a trope or a
/// one-liner). Lets `TextItemCategoryView` drive both from one implementation.
protocol TextLibraryItem: Identifiable {
    var id: Int { get }
    var text: String { get }
}

extension TropeSummary: TextLibraryItem {}
extension OneLinerSummary: TextLibraryItem {}

/// Per-kind strings for `TextItemCategoryView`.
struct TextItemConfig {
    let newFieldPrompt: String
    let searchPrompt: String
    let emptyIcon: String
    let emptyTitle: String
    let emptyHint: String
    let loadErrorTitle: String
    let deleteItemTitle: String
    let deleteItemMessage: String
    let deleteItemContextLabel: String
}

/// Shared implementation for the trope and one-liner category screens: a
/// compose bar, a searchable list with swipe/context delete and tap-to-edit,
/// plus category rename/delete. `TropeCategoryView` and `OneLinerCategoryView`
/// are thin wrappers that supply the kind-specific strings, API calls, and edit
/// sheet — the two were byte-for-byte duplicates before and had already drifted.
struct TextItemCategoryView<Item: TextLibraryItem, EditSheet: View>: View {
    let categoryId: Int
    let categoryName: String
    let config: TextItemConfig
    let fetch: (Int) async throws -> (name: String, items: [Item])
    let create: (Int, String) async throws -> Void
    let delete: (Int) async throws -> Void
    @ViewBuilder let editSheet: (Item, @escaping () -> Void) -> EditSheet
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var name: String?
    @State private var items: [Item]?
    @State private var errorMessage: String?
    @State private var actionError: String?
    @State private var isLoading = true
    @State private var newText = ""
    @State private var adding = false
    @State private var editingItem: Item?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var itemPendingDeletion: Item?
    @State private var search = ""

    private var filteredItems: [Item] {
        guard let items else { return [] }
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return items }
        return items.filter { $0.text.lowercased().contains(q) }
    }

    var body: some View {
        VStack(spacing: 0) {
            composeBar
            Divider()
            content
        }
        .navigationTitle(name ?? categoryName)
        .searchable(text: $search, prompt: config.searchPrompt)
        .sheet(item: $editingItem) { item in
            editSheet(item) {
                Task { await load() }
                onChanged?()
            }
        }
        .toolbar {
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
                initialName: name ?? categoryName,
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
        .alert(config.deleteItemTitle,
               isPresented: Binding(
                    get: { itemPendingDeletion != nil },
                    set: { if !$0 { itemPendingDeletion = nil } }),
               presenting: itemPendingDeletion,
               actions: { item in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deleteItem(item) }
                    }
               },
               message: { _ in Text(config.deleteItemMessage) })
        .actionErrorAlert($actionError)
        .task { await load() }
        .refreshable { await load() }
    }

    private var composeBar: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField(config.newFieldPrompt, text: $newText, axis: .vertical)
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
            .accessibilityLabel("Add")
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private var content: some View {
        if let items {
            if items.isEmpty {
                ContentUnavailableView(config.emptyTitle, systemImage: config.emptyIcon,
                    description: Text(config.emptyHint))
            } else if filteredItems.isEmpty {
                ContentUnavailableView.search(text: search)
            } else {
                List(filteredItems) { item in
                    Button {
                        editingItem = item
                    } label: {
                        Text(item.text)
                            .font(.body)
                            .lineLimit(1...3)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .hoverEffect(.highlight)
                    .swipeActions(edge: .trailing) {
                        Button(role: .destructive) {
                            itemPendingDeletion = item
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                    }
                    .contextMenu {
                        Button(role: .destructive) {
                            itemPendingDeletion = item
                        } label: {
                            Label(config.deleteItemContextLabel, systemImage: "trash")
                        }
                    }
                }
            }
        } else if isLoading {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            LoadFailedView(title: config.loadErrorTitle,
                           message: errorMessage) { Task { await load() } }
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            let result = try await fetch(categoryId)
            name = result.name
            items = result.items
        } catch {
            if items == nil {
                errorMessage = error.localizedDescription
            } else {
                actionError = error.localizedDescription
            }
        }
        isLoading = false
    }

    private func add() async {
        let trimmed = newText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !adding else { return }
        adding = true; defer { adding = false }
        do {
            try await create(categoryId, trimmed)
            newText = ""
            await load()
            onChanged?()
        } catch {
            actionError = error.localizedDescription
        }
    }

    private func deleteItem(_ item: Item) async {
        do {
            try await delete(item.id)
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
