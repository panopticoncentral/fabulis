import SwiftUI

enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
}

struct LibraryView: View {
    @Environment(AppState.self) private var appState
    @State private var selectedKind: LibraryKind = .prompts
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var actionError: String?
    @State private var creatingDraft = false
    @State private var selection: LibrarySelection?
    @State private var showingNewCategorySheet = false
    @State private var categoryPendingDeletion: CategorySummary?
    @State private var draftPendingDeletion: DraftSummary?
    @State private var search = ""

    private var searchPrompt: String {
        selectedKind == .drafts ? "Filter drafts" : "Filter categories"
    }

    private var filteredDrafts: [DraftSummary] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return drafts }
        return drafts.filter { ($0.title ?? "").lowercased().contains(q) }
    }

    private var filteredCategories: [CategorySummary] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return categories }
        return categories.filter { $0.name.lowercased().contains(q) }
    }

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle("Library")
                .searchable(text: $search, prompt: searchPrompt)
                .toolbar { toolbarContent }
                .onChange(of: selectedKind) { _, _ in selection = nil }
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        Task { await load() }
                    })
                }
                .sheet(isPresented: Binding(
                    get: { appState.showSettings },
                    set: { appState.showSettings = $0 })) {
                    NavigationStack { SettingsView() }
                }
                .onChange(of: appState.newDraftRequested) { _, requested in
                    guard requested else { return }
                    appState.newDraftRequested = false
                    Task { await createDraft() }
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
                            Text(LibraryCopy.deleteCategoryWarning)
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
                .actionErrorAlert($actionError)
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
                .fixedSize()
            case .stories, .prompts, .oneLiners, .tropes:
                Button { showingNewCategorySheet = true } label: {
                    Label("New Category", systemImage: "folder.badge.plus")
                }
                .fixedSize()
            }
        }
        ToolbarItem(placement: .topBarTrailing) {
            Button { Task { await load() } } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            }
            .keyboardShortcut("r", modifiers: .command)
        }
        ToolbarItem(placement: .topBarTrailing) {
            Button { appState.showSettings = true } label: {
                Label("Settings", systemImage: "gear")
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
            LoadFailedView(title: "Couldn't load library",
                           message: errorMessage) { Task { await load() } }
        } else {
            switch selectedKind {
            case .drafts: draftsList
            case .stories, .prompts, .oneLiners, .tropes: categoriesList
            }
        }
    }

    @ViewBuilder
    private var draftsList: some View {
        if drafts.isEmpty {
            ContentUnavailableView("No drafts", systemImage: "doc.text",
                description: Text("Choose \u{201C}New Draft\u{201D} to start a story."))
        } else if filteredDrafts.isEmpty {
            ContentUnavailableView.search(text: search)
        } else {
            List(selection: $selection) {
                Section("\(filteredDrafts.count) Draft\(filteredDrafts.count == 1 ? "" : "s")") {
                    ForEach(filteredDrafts) { draft in
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
    }

    @ViewBuilder
    private var categoriesList: some View {
        if categories.isEmpty {
            ContentUnavailableView("No categories",
                systemImage: "books.vertical",
                description: Text(emptyCategoriesHint))
        } else if filteredCategories.isEmpty {
            ContentUnavailableView.search(text: search)
        } else {
            List(selection: $selection) {
                ForEach(filteredCategories) { category in
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

    private var emptyCategoriesHint: String {
        switch selectedKind {
        case .stories:
            return "Save a draft to a category to see it here."
        case .prompts, .oneLiners, .tropes:
            return "Choose \u{201C}New Category\u{201D} to add one."
        case .drafts:
            return ""
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
                switch selectedKind {
                case .prompts:
                    PromptCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                case .oneLiners:
                    OneLinerCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                case .tropes:
                    TropeCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                default:
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
        } catch {
            let message: String
            if case APIError.unauthorized = error { message = "Session expired." }
            else { message = error.localizedDescription }
            // Only take over the sidebar with a full-screen error when there is
            // nothing to show. A failed refresh with data present surfaces as a
            // transient alert so the user keeps their list and selection.
            if categories.isEmpty && drafts.isEmpty {
                errorMessage = message
            } else {
                actionError = message
            }
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
            actionError = error.localizedDescription
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
            actionError = error.localizedDescription
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
            actionError = error.localizedDescription
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
            && lhs.oneLinerCount == rhs.oneLinerCount
            && lhs.latestOneLinerText == rhs.latestOneLinerText
            && lhs.tropeCount == rhs.tropeCount
            && lhs.latestTropeText == rhs.latestTropeText
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
