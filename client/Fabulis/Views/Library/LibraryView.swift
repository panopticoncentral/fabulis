import SwiftUI

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            content
                .navigationTitle("Library")
                .toolbar {
                    ToolbarItem(placement: .topBarTrailing) {
                        NavigationLink(destination: SettingsView()) {
                            Image(systemName: "gear")
                        }
                    }
                }
                .navigationDestination(for: CategorySummary.self) { category in
                    CategoryView(categoryId: category.id, categoryName: category.name)
                }
                .task { await load() }
                .refreshable { await load() }
        }
    }

    @ViewBuilder
    private var content: some View {
        if isLoading && categories.isEmpty {
            ProgressView()
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .padding()
        } else if categories.isEmpty {
            ContentUnavailableView("Empty library", systemImage: "books.vertical",
                description: Text("Create categories from the web UI to see them here."))
        } else {
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: 16)], spacing: 16) {
                    ForEach(categories) { category in
                        NavigationLink(value: category) {
                            CategoryCard(category: category)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding()
            }
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            let resp = try await FabulisAPIClient.shared.library()
            categories = resp.categories
        } catch APIError.unauthorized {
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}

extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool { lhs.id == rhs.id }
}
