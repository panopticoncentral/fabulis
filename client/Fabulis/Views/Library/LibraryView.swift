import SwiftUI

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var pendingNewDraftId: Int?
    @State private var showingNewCategorySheet = false

    var body: some View {
        NavigationStack {
            content
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
                        NavigationLink(destination: SettingsView()) {
                            Image(systemName: "gear")
                        }
                    }
                }
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        Task { await load() }
                    })
                }
                .navigationDestination(for: CategorySummary.self) { category in
                    CategoryView(categoryId: category.id, categoryName: category.name)
                }
                .navigationDestination(for: DraftSummary.self) { draft in
                    DraftView(draftId: draft.id)
                }
                .navigationDestination(isPresented: Binding(
                    get: { pendingNewDraftId != nil },
                    set: { if !$0 { pendingNewDraftId = nil } }
                )) {
                    if let id = pendingNewDraftId {
                        DraftView(draftId: id)
                    }
                }
                .task { await load() }
                .refreshable { await load() }
        }
    }

    @ViewBuilder
    private var content: some View {
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
            List {
                if !drafts.isEmpty {
                    Section("Drafts") {
                        ForEach(drafts) { draft in
                            NavigationLink(value: draft) {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(draft.title ?? "Untitled draft").font(.body)
                                    Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                                        .font(.caption2).foregroundStyle(.secondary)
                                }
                            }
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
                            NavigationLink(value: category) {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(category.name).font(.body)
                                    Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                                        .font(.caption2).foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
            }
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
            pendingNewDraftId = draft.id
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
