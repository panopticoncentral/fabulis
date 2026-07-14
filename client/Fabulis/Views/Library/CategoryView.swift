import SwiftUI

struct CategoryView: View {
    let categoryId: Int
    let categoryName: String
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: CategoryDetail?
    @State private var errorMessage: String?
    @State private var actionError: String?
    @State private var isLoading = true
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var deleting = false
    @State private var search = ""

    private var filteredStories: [StorySummary] {
        guard let stories = detail?.stories else { return [] }
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return stories }
        return stories.filter { $0.title.lowercased().contains(q) }
    }

    var body: some View {
        Group {
            if let detail {
                if detail.stories.isEmpty {
                    ContentUnavailableView("No stories", systemImage: "doc.text",
                        description: Text("Stories saved into this category will appear here."))
                } else if filteredStories.isEmpty {
                    ContentUnavailableView.search(text: search)
                } else {
                    List(filteredStories) { story in
                        NavigationLink(value: story) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(story.title).font(.body)
                                Text(story.createdAt.formatted(date: .abbreviated, time: .omitted))
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                LoadFailedView(title: "Couldn't load category",
                               message: errorMessage) { Task { await load() } }
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .searchable(text: $search, prompt: "Filter stories")
        .navigationDestination(for: StorySummary.self) { story in
            StoryView(storyId: story.id, fallbackTitle: story.title)
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
        .actionErrorAlert($actionError)
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.category(id: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteCategory() async {
        deleting = true; defer { deleting = false }
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: categoryId)
            if let onDeleted {
                onDeleted()
            } else {
                dismiss()
            }
        } catch {
            actionError = error.localizedDescription
        }
    }
}

extension StorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StorySummary, rhs: StorySummary) -> Bool { lhs.id == rhs.id }
}
