import SwiftUI

struct CategoryView: View {
    let categoryId: Int
    let categoryName: String
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: CategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var deleting = false

    var body: some View {
        Group {
            if let detail {
                if detail.stories.isEmpty {
                    ContentUnavailableView("No stories", systemImage: "doc.text",
                        description: Text("Stories saved into this category will appear here."))
                } else {
                    List(detail.stories) { story in
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
                VStack(spacing: 12) {
                    Text("Couldn't load category").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .navigationDestination(for: StorySummary.self) { story in
            StoryView(storyId: story.id, fallbackTitle: story.title)
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
            errorMessage = error.localizedDescription
        }
    }
}

extension StorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StorySummary, rhs: StorySummary) -> Bool { lhs.id == rhs.id }
}
