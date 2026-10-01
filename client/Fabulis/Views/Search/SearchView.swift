import SwiftUI

struct SearchView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.dismiss) private var dismiss
    @State private var query = ""
    @State private var results: [SearchResult] = []
    @State private var hasMore = false
    @State private var isLoading = false
    @State private var error: String?
    @State private var offset = 0
    @State private var nextOffset = 0
    @State private var retry = 0
    @State private var path: [SearchResult] = []
    @State private var presentedItem: SearchResult?
    @State private var confirmingClose = false

    private struct Request: Equatable {
        let query: String
        let offset: Int
        let retry: Int
    }
    private var request: Request { Request(query: query, offset: offset, retry: retry) }

    var body: some View {
        NavigationStack(path: $path) {
            searchContent
                .navigationTitle("Search Everything")
                .searchable(text: $query, prompt: "Search all library content")
                .navigationDestination(for: SearchResult.self) { result in
                    destination(result)
                }
                .task(id: request) { await search(request) }
                .onChange(of: query) { _, _ in
                    offset = 0
                    results = []
                    error = nil
                    hasMore = false
                }
                .onChange(of: path) { _, new in
                    if new.isEmpty { refresh() }
                }
                .toolbar {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Done") {
                            if appState.hasUnsavedChanges { confirmingClose = true }
                            else { dismiss() }
                        }
                    }
                }
        }
        .sheet(item: $presentedItem, onDismiss: refresh) { result in
            SearchTextItemSheet(result: result)
                .environment(appState)
        }
        .discardChangesConfirmation(isPresented: $confirmingClose) { dismiss() }
        .onChange(of: appState.draftRequestedToOpen) { _, id in
            if id != nil { dismiss() }
        }
    }

    @ViewBuilder
    private var searchContent: some View {
        if query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            ContentUnavailableView("Search your library", systemImage: "magnifyingglass",
                description: Text("Find stories, every saved version, summaries, drafts, prompts, one-liners, tropes, categories, and writing instructions. Enter words or the beginning of a word."))
        } else if isLoading && results.isEmpty {
            ProgressView("Searching…").frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let error, results.isEmpty {
            LoadFailedView(title: "Couldn't search", message: error) { retry += 1 }
        } else if results.isEmpty {
            ContentUnavailableView.search(text: query)
        } else {
            List {
                ForEach(results) { result in
                    Button { open(result) } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            Text(result.title).font(.headline).foregroundStyle(.primary)
                            Text([result.kindLabel, result.categoryName].compactMap { $0 }.joined(separator: " · "))
                                .font(.caption).foregroundStyle(.secondary)
                            Text(SearchSnippet.attributed(result.snippet))
                                .font(.subheadline).foregroundStyle(.primary).lineLimit(4)
                        }
                        .padding(.vertical, 4)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                if let error {
                    Text(error).foregroundStyle(.secondary)
                    Button("Retry") { retry += 1 }
                } else if isLoading {
                    ProgressView("Loading more…")
                } else if hasMore {
                    Button("Load More Results") { offset = nextOffset }
                }
            }
        }
    }

    private func open(_ result: SearchResult) {
        if result.kind == "oneLiner" || result.kind == "trope" {
            presentedItem = result
        } else {
            path.append(result)
        }
    }

    @ViewBuilder
    private func destination(_ result: SearchResult) -> some View {
        switch result.kind {
        case "story", "storyVersion":
            StoryView(storyId: result.itemId, fallbackTitle: result.title,
                      initialVersion: result.versionNumber, showSummary: result.matchInSummary)
        case "draft":
            DraftView(draftId: result.itemId, composition: appState.composition(for: result.itemId))
        case "prompt":
            PromptEditorView(promptId: result.itemId, onSaved: { path.removeLast() })
        case "category":
            SearchCategoryView(categoryId: result.itemId, categoryName: result.title)
        case "storyteller":
            StorytellerEditorView(storytellerId: result.itemId)
        case "summaryPrompt":
            SettingsView()
        default:
            ContentUnavailableView("Item unavailable", systemImage: "questionmark.folder")
        }
    }

    private func refresh() { offset = 0; retry += 1 }

    private func search(_ requested: Request) async {
        if requested.offset == 0 { results = []; hasMore = false }
        error = nil
        guard !requested.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            isLoading = false
            return
        }
        isLoading = true
        do {
            if requested.offset == 0 { try await Task.sleep(for: .milliseconds(250)) }
            try Task.checkCancellation()
            let response = try await FabulisAPIClient.shared.search(query: requested.query, offset: requested.offset)
            try Task.checkCancellation()
            guard request == requested else { return }
            if requested.offset == 0 { results = response.results }
            else {
                let existing = Set(results.map(\.id))
                results += response.results.filter { !existing.contains($0.id) }
            }
            nextOffset = requested.offset + response.results.count
            hasMore = response.hasMore
            isLoading = false
        } catch {
            guard !Task.isCancelled, request == requested else { return }
            self.error = error.localizedDescription
            isLoading = false
        }
    }
}

/// FTS markers become emphasis without interpreting library text as markup.
enum SearchSnippet {
    static func attributed(_ snippet: String) -> AttributedString {
        var result = AttributedString()
        var highlighted = false
        var buffer = ""
        func append() {
            var run = AttributedString(buffer)
            if highlighted { run.font = .body.bold(); run.foregroundColor = .accentColor }
            result.append(run)
            buffer = ""
        }
        for character in snippet {
            if character == "\u{2}" || character == "\u{3}" {
                append()
                highlighted = character == "\u{2}"
            } else { buffer.append(character) }
        }
        append()
        return result
    }
}

private struct SearchCategoryView: View {
    let categoryId: Int
    let categoryName: String

    var body: some View {
        List {
            NavigationLink("Stories") { CategoryView(categoryId: categoryId, categoryName: categoryName) }
            NavigationLink("Prompts") { PromptCategoryView(categoryId: categoryId, categoryName: categoryName) }
            NavigationLink("One-liners") { OneLinerCategoryView(categoryId: categoryId, categoryName: categoryName) }
            NavigationLink("Tropes") { TropeCategoryView(categoryId: categoryId, categoryName: categoryName) }
        }
        .navigationTitle(categoryName)
    }
}

/// Fetch current full text before editing; a highlighted search excerpt is never
/// used as the editor's source, and a deleted result produces a normal load error.
private struct SearchTextItemSheet: View {
    let result: SearchResult
    @Environment(\.dismiss) private var dismiss
    @State private var oneLiner: OneLinerDetail?
    @State private var trope: TropeDetail?
    @State private var error: String?

    var body: some View {
        Group {
            if let oneLiner {
                OneLinerEditSheet(oneLiner: OneLinerSummary(id: oneLiner.id, text: oneLiner.text, createdAt: oneLiner.createdAt),
                                  categoryId: oneLiner.categoryId)
            } else if let trope {
                TropeEditSheet(trope: TropeSummary(id: trope.id, text: trope.text, createdAt: trope.createdAt),
                               categoryId: trope.categoryId)
            } else {
                NavigationStack {
                    Group {
                        if let error {
                            LoadFailedView(title: "Couldn't open result", message: error) { Task { await load() } }
                        } else { ProgressView() }
                    }
                    .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } } }
                }
            }
        }
        .task { await load() }
    }

    private func load() async {
        error = nil
        do {
            if result.kind == "oneLiner" { oneLiner = try await FabulisAPIClient.shared.oneLiner(id: result.itemId) }
            else { trope = try await FabulisAPIClient.shared.trope(id: result.itemId) }
        } catch { self.error = error.localizedDescription }
    }
}
