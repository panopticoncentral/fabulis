import SwiftUI

enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
}

struct LibraryView: View {
    @State private var selectedKind: LibraryKind = .prompts
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var selection: LibrarySelection?
    @State private var showingNewCategorySheet = false
    @State private var showingSettingsSheet = false
    @State private var categoryPendingDeletion: CategorySummary?
    @State private var draftPendingDeletion: DraftSummary?

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle("Library")
                .toolbar { toolbarContent }
                .onChange(of: selectedKind) { _, _ in selection = nil }
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
                            Text("This deletes the category and all its stories and prompts. This cannot be undone.")
                       })
                .alert("Delete draft?",
                       isPresented: Binding(
                            get: { draftPendingDeletion != nil },
                            set: { if !$0 { draftPendingDeletion = nil } }),
                       presenting: draftPendingDeletion,
                       actions: { draft in
                            Button("Cancel", role: .cancel) {}
                            Button("Delete", role: .destructive) {
                                Task { await deleteDraft(draft) }
                            }
                       },
                       message: { _ in
                            Text("This deletes the draft and its messages. This cannot be undone.")
                       })
                .task { await load() }
                .refreshable { await load() }
        } detail: {
            detail
        }
    }

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            switch selectedKind {
            case .drafts:
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
            case .stories, .prompts:
                Button { showingNewCategorySheet = true } label: {
                    Label("New Category", systemImage: "folder.badge.plus")
                }
            }
        }
        ToolbarItem(placement: .topBarTrailing) {
            Button { showingSettingsSheet = true } label: {
                Image(systemName: "gear")
            }
        }
    }

    @ViewBuilder
    private var sidebar: some View {
        VStack(spacing: 0) {
            Picker("Kind", selection: $selectedKind) {
                ForEach(LibraryKind.allCases) { kind in
                    Text(kind.label).tag(kind)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            .padding(.vertical, 8)

            sidebarList
        }
    }

    @ViewBuilder
    private var sidebarList: some View {
        if isLoading && categories.isEmpty && drafts.isEmpty {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
        } else {
            switch selectedKind {
            case .drafts: draftsList
            case .stories, .prompts: categoriesList
            }
        }
    }

    @ViewBuilder
    private var draftsList: some View {
        if drafts.isEmpty {
            ContentUnavailableView("No drafts", systemImage: "doc.text",
                description: Text("Tap \u{201C}New Draft\u{201D} to start a story."))
        } else {
            List(selection: $selection) {
                ForEach(drafts) { draft in
                    DraftRow(draft: draft)
                        .tag(LibrarySelection.draft(id: draft.id))
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                draftPendingDeletion = draft
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                        .contextMenu {
                            Button(role: .destructive) {
                                draftPendingDeletion = draft
                            } label: {
                                Label("Delete Draft", systemImage: "trash")
                            }
                        }
                }
            }
        }
    }

    @ViewBuilder
    private var categoriesList: some View {
        if categories.isEmpty {
            ContentUnavailableView("No categories",
                systemImage: "books.vertical",
                description: Text("Save a draft to a category to see it here."))
        } else {
            List(selection: $selection) {
                ForEach(categories) { category in
                    CategoryRow(category: category, kind: selectedKind)
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

    @ViewBuilder
    private var detail: some View {
        switch selection {
        case .draft(let id):
            NavigationStack {
                DraftView(draftId: id, onDraftChanged: { summary in
                    if let idx = drafts.firstIndex(where: { $0.id == summary.id }) {
                        drafts[idx] = summary
                    }
                }, onLibraryChanged: {
                    Task { await load() }
                })
                .id(id)
            }
        case .category(let id, let name):
            NavigationStack {
                if selectedKind == .prompts {
                    PromptCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                } else {
                    CategoryView(categoryId: id, categoryName: name, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                }
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
            async let draftList = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            drafts = try await draftList
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
            selectedKind = .drafts
            selection = .draft(id: draft.id)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

// Full value equality (not id-only): SwiftUI compares a row view's stored
// properties via Equatable to decide whether to re-render. An id-only `==`
// makes a reloaded summary with a changed count look unchanged, so the
// sidebar count goes stale. The hash stays id-based — equal values share an
// id, so this remains consistent with `==`.
extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool {
        lhs.id == rhs.id
            && lhs.name == rhs.name
            && lhs.createdAt == rhs.createdAt
            && lhs.storyCount == rhs.storyCount
            && lhs.latestStoryTitle == rhs.latestStoryTitle
            && lhs.promptCount == rhs.promptCount
            && lhs.latestPromptTitle == rhs.latestPromptTitle
    }
}

extension DraftSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: DraftSummary, rhs: DraftSummary) -> Bool {
        lhs.id == rhs.id
            && lhs.title == rhs.title
            && lhs.createdAt == rhs.createdAt
            && lhs.updatedAt == rhs.updatedAt
            && lhs.messageCount == rhs.messageCount
    }
}
