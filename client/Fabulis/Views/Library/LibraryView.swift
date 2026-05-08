import SwiftUI

enum LibrarySelection: Hashable {
    case draftsRoot
    case category(id: Int, name: String)
}

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var draftCount: Int = 0
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var selection: LibrarySelection?
    @State private var draftsPath = NavigationPath()
    @State private var showingNewCategorySheet = false
    @State private var showingSettingsSheet = false
    @State private var categoryPendingDeletion: CategorySummary?

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle("Library")
                .toolbar {
                    ToolbarItem(placement: .topBarLeading) {
                        Button {
                            Task { await createDraft() }
                        } label: {
                            HStack(spacing: 4) {
                                if creatingDraft { ProgressView().controlSize(.mini) }
                                else { Image(systemName: "plus") }
                                Text("New Draft")
                            }
                        }
                        .disabled(creatingDraft)
                    }
                    ToolbarItemGroup(placement: .topBarTrailing) {
                        Button { showingNewCategorySheet = true } label: {
                            Image(systemName: "folder.badge.plus")
                        }
                        Button { showingSettingsSheet = true } label: {
                            Image(systemName: "gear")
                        }
                    }
                }
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        Task { await load() }
                    })
                }
                .sheet(isPresented: $showingSettingsSheet) {
                    NavigationStack { SettingsView() }
                }
                .alert("Delete category?",
                       isPresented: Binding(
                            get: { categoryPendingDeletion != nil },
                            set: { if !$0 { categoryPendingDeletion = nil } }),
                       presenting: categoryPendingDeletion,
                       actions: { category in
                            Button("Cancel", role: .cancel) {}
                            Button("Delete", role: .destructive) {
                                Task { await deleteCategory(category) }
                            }
                       },
                       message: { _ in
                            Text("This deletes the category and all its stories. This cannot be undone.")
                       })
                .task { await load() }
                .refreshable { await load() }
        } detail: {
            detail
        }
    }

    @ViewBuilder
    private var sidebar: some View {
        if isLoading && categories.isEmpty && draftCount == 0 {
            ProgressView()
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .padding()
        } else {
            List(selection: $selection) {
                if draftCount > 0 {
                    Section("Drafts") {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Drafts").font(.body)
                            Text("\(draftCount) \(draftCount == 1 ? "draft" : "drafts")")
                                .font(.caption2).foregroundStyle(.secondary)
                        }
                        .tag(LibrarySelection.draftsRoot)
                    }
                }
                if categories.isEmpty {
                    Section {
                        ContentUnavailableView("No categories",
                            systemImage: "books.vertical",
                            description: Text("Save a draft to a category to see it here."))
                    }
                } else {
                    Section("Library") {
                        ForEach(categories) { category in
                            VStack(alignment: .leading, spacing: 4) {
                                Text(category.name).font(.body)
                                Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                            .tag(LibrarySelection.category(id: category.id, name: category.name))
                            .swipeActions(edge: .trailing) {
                                Button(role: .destructive) {
                                    categoryPendingDeletion = category
                                } label: {
                                    Label("Delete", systemImage: "trash")
                                }
                            }
                            .contextMenu {
                                Button(role: .destructive) {
                                    categoryPendingDeletion = category
                                } label: {
                                    Label("Delete Category", systemImage: "trash")
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch selection {
        case .draftsRoot:
            NavigationStack(path: $draftsPath) {
                DraftsView(onDraftsChanged: {
                    Task { await load() }
                })
            }
        case .category(let id, let name):
            NavigationStack {
                CategoryView(categoryId: id, categoryName: name, onDeleted: {
                    selection = nil
                    Task { await load() }
                })
                .id(id)
            }
        case .none:
            ContentUnavailableView("Select a draft or category",
                systemImage: "books.vertical",
                description: Text("Pick a category to read its stories, or open Drafts to keep working."))
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            async let lib = FabulisAPIClient.shared.library()
            async let drafs = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            draftCount = try await drafs.count
        } catch APIError.unauthorized {
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteCategory(_ category: CategorySummary) async {
        if case .category(let id, _) = selection, id == category.id {
            selection = nil
        }
        categories.removeAll { $0.id == category.id }
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: category.id)
        } catch {
            errorMessage = error.localizedDescription
            await load()
        }
    }

    private func createDraft() async {
        creatingDraft = true
        defer { creatingDraft = false }
        do {
            let draft = try await FabulisAPIClient.shared.createDraft()
            await load()
            selection = .draftsRoot
            draftsPath = NavigationPath()
            draftsPath.append(draft.id)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool { lhs.id == rhs.id }
}

extension DraftSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: DraftSummary, rhs: DraftSummary) -> Bool { lhs.id == rhs.id }
}
