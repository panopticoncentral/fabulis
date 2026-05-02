import SwiftUI

struct CategoryView: View {
    let categoryId: Int
    let categoryName: String

    @State private var detail: CategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

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
                                Text(formatDate(story.createdAt))
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
        .navigationTitle(categoryName)
        .navigationDestination(for: StorySummary.self) { story in
            StoryView(storyId: story.id, fallbackTitle: story.title)
        }
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
}

extension StorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StorySummary, rhs: StorySummary) -> Bool { lhs.id == rhs.id }
}

private func formatDate(_ date: Date) -> String {
    date.formatted(date: .abbreviated, time: .omitted)
}
