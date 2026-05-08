import SwiftUI

enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
}

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var selection: LibrarySelection?
    @State private var showingNewCategorySheet = false
    @State private var showingSettingsSheet = false

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
                .task { await load() }
                .refreshable { await load() }
        } detail: {
            detail
        }
    }

    @ViewBuilder
    private var sidebar: some View {
        if isLoading && categories.isEmpty && drafts.isEmpty {
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
                if !drafts.isEmpty {
                    Section("Drafts") {
                        ForEach(drafts) { draft in
                            VStack(alignment: .leading, spacing: 4) {
                                Text(draft.title ?? "Untitled draft").font(.body)
                                Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                            .tag(LibrarySelection.draft(id: draft.id))
                            .swipeActions(edge: .trailing) {
                                Button(role: .destructive) {
                                    Task { await deleteDraft(draft) }
                                } label: {
                                    Label("Delete", systemImage: "trash")
                                }
                            }
                            .contextMenu {
                                Button(role: .destructive) {
                                    Task { await deleteDraft(draft) }
                                } label: {
                                    Label("Delete Draft", systemImage: "trash")
                                }
                            }
                        }
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
                        }
                    }
                }
            }
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch selection {
        case .draft(let id):
            NavigationStack { DraftView(draftId: id).id(id) }
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
                description: Text("Pick a draft to keep working, or open a category to read its stories."))
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            async let lib = FabulisAPIClient.shared.library()
            async let drafs = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            drafts = try await drafs
        } catch APIError.unauthorized {
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteDraft(_ draft: DraftSummary) async {
        if case .draft(let id) = selection, id == draft.id {
            selection = nil
        }
        drafts.removeAll { $0.id == draft.id }
        do {
            try await FabulisAPIClient.shared.deleteDraft(id: draft.id)
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
            selection = .draft(id: draft.id)
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
