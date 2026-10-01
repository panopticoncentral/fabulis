import SwiftUI

enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
    var categoryID: Int? {
        if case .category(let id, _) = self { return id }
        return nil
    }
}

private enum LibraryTab: String, CaseIterable {
    case drafts, stories, resources
}

struct LibraryView: View {
    @Environment(AppState.self) private var appState
    @State private var tab: LibraryTab = .drafts
    @State private var creatingDraft = false
    @State private var createError: String?
    @State private var draftToOpen: Int?
    @State private var libraryRevision = 0
    @State private var showingSearch = false

    private var tabSelection: Binding<LibraryTab> {
        Binding(get: { tab }, set: { newTab in appState.navigate { tab = newTab } })
    }

    var body: some View {
        Group {
            #if targetEnvironment(macCatalyst)
            browser(kind: .drafts, active: true)
            #else
            TabView(selection: tabSelection) {
                browser(kind: .drafts, active: tab == .drafts)
                    .tabItem { Label("Drafts", systemImage: "square.and.pencil") }.tag(LibraryTab.drafts)
                browser(kind: .stories, active: tab == .stories)
                    .tabItem { Label("Stories", systemImage: "books.vertical") }.tag(LibraryTab.stories)
                browser(kind: .prompts, active: tab == .resources)
                    .tabItem { Label("Resources", systemImage: "lightbulb") }.tag(LibraryTab.resources)
            }
            #endif
        }
        .sheet(isPresented: Binding(get: { appState.showSettings }, set: { appState.showSettings = $0 })) {
            NavigationStack { SettingsView() }
                .interactiveDismissDisabled(appState.hasUnsavedChanges)
                .frame(idealWidth: 620, idealHeight: 650)
        }
        .sheet(isPresented: $showingSearch, onDismiss: { libraryRevision += 1 }) {
            SearchView()
                // Catalyst can create the sheet outside the presenting view's
                // environment tree. Supply the same model explicitly.
                .environment(appState)
                .interactiveDismissDisabled(appState.hasUnsavedChanges)
                .frame(idealWidth: 760, idealHeight: 720)
        }
        .confirmationDialog("Discard changes?", isPresented: Binding(
            get: { appState.showingNavigationConfirmation },
            set: { appState.showingNavigationConfirmation = $0 }), titleVisibility: .visible) {
                Button("Discard Changes", role: .destructive) { appState.discardAndNavigate() }
                Button("Keep Editing", role: .cancel) { appState.pendingNavigation = nil }
        }
        .onChange(of: appState.draftRequestedToOpen) { _, id in
            guard let id else { return }
            appState.draftRequestedToOpen = nil
            tab = .drafts
            libraryRevision += 1
            draftToOpen = id
        }
        .onChange(of: appState.newDraftRequested) { _, requested in
            guard requested else { return }
            appState.newDraftRequested = false
            appState.navigate { Task { await createDraft() } }
        }
        .actionErrorAlert($createError, title: "Couldn't create draft")
    }

    private func browser(kind: LibraryKind, active: Bool) -> some View {
        LibraryBrowser(initialKind: kind, isActive: active, creatingDraft: creatingDraft,
                       draftToOpen: draftToOpen, revision: libraryRevision,
                       onLibraryChanged: { libraryRevision += 1 },
                       onSearch: { appState.navigate { showingSearch = true } })
    }

    private func createDraft() async {
        guard !creatingDraft else { return }
        creatingDraft = true; defer { creatingDraft = false }
        do {
            let draft = try await FabulisAPIClient.shared.createDraft()
            tab = .drafts
            libraryRevision += 1
            draftToOpen = draft.id
        } catch { createError = error.localizedDescription }
    }
}

private struct LibraryBrowser: View {
    @Environment(AppState.self) private var appState
    let isActive: Bool
    let creatingDraft: Bool
    let draftToOpen: Int?
    let revision: Int
    let onLibraryChanged: () -> Void
    let onSearch: () -> Void
    @State private var selectedKind: LibraryKind
    @State private var selections: [LibraryKind: LibrarySelection] = [:]
    @State private var searches: [LibraryKind: String] = [:]

    init(initialKind: LibraryKind, isActive: Bool, creatingDraft: Bool, draftToOpen: Int?, revision: Int,
         onLibraryChanged: @escaping () -> Void, onSearch: @escaping () -> Void) {
        self.isActive = isActive
        self.creatingDraft = creatingDraft
        self.draftToOpen = draftToOpen
        self.revision = revision
        self.onLibraryChanged = onLibraryChanged
        self.onSearch = onSearch
        _selectedKind = State(initialValue: initialKind)
    }

    private var guardedSelection: Binding<LibrarySelection?> {
        Binding(get: { selection }, set: { newValue in appState.navigate { selection = newValue } })
    }

    private func selectKind(_ kind: LibraryKind) {
        guard selectedKind != kind else { return }
        appState.navigate {
            selections[selectedKind] = selection
            searches[selectedKind] = search
            selectedKind = kind
            selection = selections[kind]
            search = searches[kind] ?? ""
        }
    }
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var actionError: String?
    @State private var selection: LibrarySelection?
    @State private var showingNewCategorySheet = false
    @State private var categoryPendingDeletion: CategorySummary?
    @State private var draftPendingDeletion: DraftSummary?
    @State private var search = ""
    @State private var lastOpenedDraft: Int?
    @State private var selectedStory: StorySummary?

    private var searchPrompt: String {
        selectedKind == .drafts ? "Filter drafts" : "Filter categories"
    }

    private var filteredDrafts: [DraftSummary] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return drafts }
        return drafts.filter { ($0.title ?? "Untitled Draft").lowercased().contains(q) }
    }

    private var filteredCategories: [CategorySummary] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return categories }
        return categories.filter { $0.name.lowercased().contains(q) }
    }

    var body: some View {
        Group {
        #if targetEnvironment(macCatalyst)
        if selectedKind == .stories || selectedKind == .drafts {
            NavigationSplitView {
                sidebarPanel
            } content: {
                Group {
                    if selectedKind == .drafts { draftListColumn }
                    else { storyListColumn }
                }
                .navigationSplitViewColumnWidth(min: 220, ideal: 280, max: 420)
            } detail: {
                if selectedKind == .drafts {
                    detail
                } else {
                    NavigationStack {
                        if let selectedStory {
                            StoryView(storyId: selectedStory.id, fallbackTitle: selectedStory.title).id(selectedStory.id)
                        } else {
                            ContentUnavailableView("Choose a story", systemImage: "book", description: Text("Select a story to start reading."))
                        }
                    }
                }
            }
        } else { twoColumnBrowser }
        #else
        twoColumnBrowser
        #endif
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            if appState.narration.isVisible { NarrationBar(player: appState.narration) }
        }
    }

    private var twoColumnBrowser: some View {
        NavigationSplitView { sidebarPanel } detail: { detail }
    }

    private var sidebarPanel: some View {
            sidebar
                .navigationTitle(selectedKind.label)
                .librarySearch(text: $search, prompt: searchPrompt)
                .toolbar { toolbarContent }
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        onLibraryChanged()
                        Task { await load() }
                    })
                }
                .onChange(of: draftToOpen) { _, id in
                    guard isActive, let id, lastOpenedDraft != id else { return }
                    lastOpenedDraft = id
                    selectedKind = .drafts
                    selection = .draft(id: id)
                    Task { await load() }
                }
                .onChange(of: isActive) { _, active in
                    if active, selectedKind == .drafts, let draftToOpen, lastOpenedDraft != draftToOpen {
                        lastOpenedDraft = draftToOpen
                        selection = .draft(id: draftToOpen)
                    }
                }
                .onChange(of: revision) { _, _ in Task { await load() } }
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
                       message: { category in
                            Text(LibraryCopy.deleteCategoryWarning(category))
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
            .onChange(of: selection) { old, new in
                if old?.categoryID != new?.categoryID { selectedStory = nil }
            }
    }

    private var draftListColumn: some View {
        VStack(spacing: 0) {
            TextField("Filter drafts", text: $search)
                .textFieldStyle(.roundedBorder)
                .accessibilityLabel("Filter drafts")
                .padding(8)
            libraryList
        }
        .navigationTitle("Drafts")
    }

    @ViewBuilder
    private var storyListColumn: some View {
        if case .category(let id, let name) = selection {
            CategoryView(categoryId: id, categoryName: name, onChanged: onLibraryChanged,
                         storySelection: Binding(get: { selectedStory }, set: { story in
                             appState.navigate { selectedStory = story }
                         }), onDeleted: {
                selection = nil
                Task { await load() }
            })
            .id(id)
        } else {
            ContentUnavailableView("Choose a category", systemImage: "books.vertical",
                description: Text("Browse your stories by category."))
        }
    }

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem(placement: .primaryAction) {
            Button(action: onSearch) { Label("Search Everything", systemImage: "magnifyingglass") }
                .keyboardShortcut("f", modifiers: [.command, .shift])
                .help("Search everything (⇧⌘F)")
                .accessibilityIdentifier("search-everything")
        }
        ToolbarItem(placement: .primaryAction) {
            Button { appState.newDraftRequested = true } label: {
                Label("New Draft", systemImage: "square.and.pencil")
            }
            .disabled(creatingDraft)
            .help("Start a new draft (⌘N)")
        }
        ToolbarItem(placement: .topBarTrailing) {
            Menu {
                if selectedKind.hasCategories {
                    Button { showingNewCategorySheet = true } label: {
                        Label("New Category…", systemImage: "folder.badge.plus")
                    }
                }
                Button { Task { await load() } } label: { Label("Refresh", systemImage: "arrow.clockwise") }
                Button { appState.showSettings = true } label: { Label("Settings…", systemImage: "gear") }
            } label: { Label("Library Options", systemImage: "ellipsis.circle") }
            .accessibilityIdentifier("library-options")
            .help("Library options")
        }
    }

    private var sidebar: some View {
        VStack(spacing: 0) {
            #if targetEnvironment(macCatalyst)
            VStack(alignment: .leading, spacing: 4) {
                ForEach([LibraryKind.drafts, .stories, .prompts, .oneLiners, .tropes]) { kind in
                    if kind == .prompts {
                        Text("Resources").font(.caption).foregroundStyle(.secondary).padding(.top, 12)
                    }
                    Button { selectKind(kind) } label: {
                        Label(kind.label, systemImage: kind.symbol)
                            .frame(maxWidth: .infinity, alignment: .leading).padding(8)
                            .background(selectedKind == kind ? Color.accentColor.opacity(0.14) : .clear,
                                        in: RoundedRectangle(cornerRadius: 6))
                            // Plain buttons otherwise hit-test only the visible
                            // label when this row's background is transparent.
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(selectedKind == kind ? .isSelected : [])
                }
            }
            .padding(12)
            if selectedKind.hasCategories {
                Divider()
                TextField(searchPrompt, text: $search)
                    .textFieldStyle(.roundedBorder)
                    .accessibilityLabel(searchPrompt)
                    .padding(8)
                libraryList
            } else {
                Spacer(minLength: 0)
            }
            #else
            if [.prompts, .oneLiners, .tropes].contains(selectedKind) {
                Picker("Resource type", selection: Binding(get: { selectedKind }, set: selectKind)) {
                    ForEach([LibraryKind.prompts, .oneLiners, .tropes]) { kind in
                        Text(kind.label).tag(kind)
                    }
                }
                .pickerStyle(.menu)
                .padding(8)
            }
            libraryList
            #endif
        }
        .navigationSplitViewColumnWidth(min: 220, ideal: 270, max: 360)
    }

    private var libraryList: some View {
        sidebarList
            .focusedSceneValue(\.contentActions, isActive ? ContentActions(refresh: { Task { await load() } }) : nil)
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
            ContentUnavailableView {
                Label("Start your first story", systemImage: "square.and.pencil")
            } description: { Text("Create a draft and begin with an idea.") } actions: {
                Button("New Draft") { appState.newDraftRequested = true }.buttonStyle(.borderedProminent)
            }
        } else if filteredDrafts.isEmpty {
            ContentUnavailableView.search(text: search)
        } else {
            List(selection: guardedSelection) {
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
            ContentUnavailableView {
                Label("No categories", systemImage: "books.vertical")
            } description: { Text(emptyCategoriesHint) } actions: {
                Button("New Category") { showingNewCategorySheet = true }.buttonStyle(.borderedProminent)
            }
        } else if filteredCategories.isEmpty {
            ContentUnavailableView.search(text: search)
        } else {
            List(selection: guardedSelection) {
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
                    onLibraryChanged()
                    Task { await load() }
                }, composition: appState.composition(for: id))
                .id(id)
            }
        case .category(let id, let name):
            NavigationStack {
                switch selectedKind {
                case .prompts:
                    PromptCategoryView(categoryId: id, categoryName: name, onChanged: {
                        onLibraryChanged()
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                case .oneLiners:
                    OneLinerCategoryView(categoryId: id, categoryName: name, onChanged: {
                        onLibraryChanged()
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                case .tropes:
                    TropeCategoryView(categoryId: id, categoryName: name, onChanged: {
                        onLibraryChanged()
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                default:
                    CategoryView(categoryId: id, categoryName: name, onChanged: onLibraryChanged, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                }
            }
        case .none:
            ContentUnavailableView(selectedKind == .drafts ? "Choose a draft" : "Choose a category",
                systemImage: selectedKind.symbol,
                description: Text(selectedKind == .drafts ? "Continue writing, or create a new draft." : "Browse your \(selectedKind.label.lowercased()) by category."))
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            async let lib = FabulisAPIClient.shared.library()
            async let draftList = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            drafts = try await draftList
            if case .category(let id, _) = selection, let category = categories.first(where: { $0.id == id }) {
                selection = .category(id: id, name: category.name)
            }
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
            onLibraryChanged()
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

private extension View {
    @ViewBuilder
    func librarySearch(text: Binding<String>, prompt: String) -> some View {
        #if targetEnvironment(macCatalyst)
        self
        #else
        searchable(text: text, prompt: prompt)
        #endif
    }
}
