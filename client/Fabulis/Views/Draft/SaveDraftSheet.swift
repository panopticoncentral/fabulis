import SwiftUI

struct SaveDraftSheet: View {
    let draftId: Int
    let draftTitle: String?
    var onSaved: () -> Void = {}

    @Environment(\.dismiss) private var dismiss
    @State private var categories: [CategorySummary] = []
    @State private var selectedCategoryId: Int?
    @State private var newCategoryName = ""
    @State private var storiesInCategory: [StorySummary] = []
    @State private var selectedStoryId: Int?
    @State private var saveAsVersion = false
    @State private var newStoryTitle = ""
    @State private var isLoading = true
    @State private var categoriesLoaded = false
    @State private var isLoadingStories = false
    @State private var storiesLoaded = false
    @State private var isSaving = false
    @State private var isGeneratingTitle = false
    @State private var errorMessage: String?
    @State private var saved: SaveDraftResponse?
    @State private var showingSavedStory = false
    @State private var showingDiscardConfirm = false
    @State private var didInitialize = false

    private var hasChanges: Bool {
        saved == nil && (!newCategoryName.isEmpty || newStoryTitle != (draftTitle ?? "")) && didInitialize
    }

    var body: some View {
        NavigationStack {
            Group {
                if let saved {
                    ContentUnavailableView {
                        Label("Saved to Library", systemImage: "checkmark.circle")
                    } description: {
                        Text("Version \(saved.versionNumber) is ready to read.")
                    } actions: {
                        Button("Open Story") { showingSavedStory = true }.buttonStyle(.borderedProminent)
                    }
                } else if isLoading {
                    ProgressView("Loading categories…")
                } else if !categoriesLoaded {
                    LoadFailedView(title: "Couldn't load categories", message: errorMessage ?? "Try again.") { Task { await loadCategories() } }
                } else {
                    saveForm
                }
            }
            .navigationTitle(saved == nil ? "Save to Library" : "Story Saved")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(saved == nil ? "Cancel" : "Done") {
                        if hasChanges { showingDiscardConfirm = true } else { dismiss() }
                    }
                    .disabled(isSaving)
                    .keyboardShortcut(.cancelAction)
                }
                if saved == nil {
                    ToolbarItem(placement: .confirmationAction) {
                        Button(isSaving ? "Saving…" : (saveAsVersion ? "Save New Version" : "Create Story")) {
                            Task { await save() }
                        }
                        .disabled(!canSave || isSaving || isGeneratingTitle)
                    }
                }
            }
            .navigationDestination(isPresented: $showingSavedStory) {
                if let saved { StoryView(storyId: saved.storyId, fallbackTitle: newStoryTitle) }
            }
            .protectUnsavedChanges(hasChanges)
            .discardChangesConfirmation(isPresented: $showingDiscardConfirm) { dismiss() }
            .task {
                guard !didInitialize else { return }
                newStoryTitle = draftTitle ?? ""
                didInitialize = true
                await loadCategories()
            }
        }
        .frame(idealWidth: 560, idealHeight: 520)
    }

    private var saveForm: some View {
        Form {
            Section("Save as") {
                Picker("Destination", selection: $saveAsVersion) {
                    Text("New Story").tag(false)
                    Text("New Version of Existing Story").tag(true)
                }
            }
            Section("Category") {
                Picker("Category", selection: $selectedCategoryId) {
                    Text("New Category…").tag(Int?.none)
                    ForEach(categories) { Text($0.name).tag(Optional($0.id)) }
                }
                if selectedCategoryId == nil {
                    TextField("New category name", text: $newCategoryName)
                        .textInputAutocapitalization(.words)
                }
            }
            if saveAsVersion {
                Section {
                    if selectedCategoryId == nil {
                        Text("Choose an existing category to save a new story version.").foregroundStyle(.secondary)
                    } else if isLoadingStories {
                        ProgressView("Loading stories…")
                    } else if !storiesLoaded {
                        Button("Retry Loading Stories") {
                            if let selectedCategoryId { Task { await loadStories(in: selectedCategoryId) } }
                        }
                    } else if storiesInCategory.isEmpty {
                        Text("This category has no stories. Choose another category or create a new story.").foregroundStyle(.secondary)
                    } else {
                        Picker("Story", selection: $selectedStoryId) {
                            Text("Choose a story").tag(Int?.none)
                            ForEach(storiesInCategory) { Text($0.title).tag(Optional($0.id)) }
                        }
                    }
                } header: { Text("Existing story") } footer: {
                    Text("A new version will be added. Previous versions remain available.")
                }
            } else {
                Section("Story title") {
                    TextField("Story title", text: $newStoryTitle)
                    Button {
                        Task { await generateTitle() }
                    } label: {
                        if isGeneratingTitle { ProgressView("Generating title…") }
                        else { Label("Generate Title", systemImage: "sparkles") }
                    }
                    .disabled(isGeneratingTitle)
                }
            }
            if let errorMessage {
                Section {
                    Label(errorMessage, systemImage: "exclamationmark.triangle").foregroundStyle(.red)
                    if categories.isEmpty { Button("Retry Loading Categories") { Task { await loadCategories() } } }
                }
            }
        }
        .disabled(isSaving)
        .task(id: selectedCategoryId) {
            selectedStoryId = nil
            storiesInCategory = []
            storiesLoaded = false
            if let selectedCategoryId { await loadStories(in: selectedCategoryId) }
        }
    }

    private var canSave: Bool {
        guard !isLoading && categoriesLoaded else { return false }
        if saveAsVersion {
            return storiesLoaded && !isLoadingStories && selectedCategoryId != nil
                && storiesInCategory.contains { $0.id == selectedStoryId }
        }
        return (selectedCategoryId != nil || !newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            && !newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func loadCategories() async {
        isLoading = true
        defer { isLoading = false }
        do {
            categories = try await FabulisAPIClient.shared.library().categories
            categoriesLoaded = true
            selectedCategoryId = categories.first?.id
            errorMessage = nil
        } catch { errorMessage = error.localizedDescription }
    }

    private func loadStories(in categoryId: Int) async {
        isLoadingStories = true
        storiesLoaded = false
        selectedStoryId = nil
        storiesInCategory = []
        do {
            let detail = try await FabulisAPIClient.shared.category(id: categoryId)
            guard !Task.isCancelled, selectedCategoryId == categoryId else { return }
            storiesInCategory = detail.stories
            storiesLoaded = true
            errorMessage = nil
        } catch {
            guard !Task.isCancelled, selectedCategoryId == categoryId else { return }
            errorMessage = error.localizedDescription
        }
        if selectedCategoryId == categoryId { isLoadingStories = false }
    }

    private func generateTitle() async {
        let original = newStoryTitle
        isGeneratingTitle = true; defer { isGeneratingTitle = false }
        do {
            let title = try await FabulisAPIClient.shared.generateTitle(draftId: draftId)
            if newStoryTitle == original { newStoryTitle = title }
        } catch { errorMessage = error.localizedDescription }
    }

    private func save() async {
        guard canSave, !isSaving else { return }
        isSaving = true; defer { isSaving = false }
        errorMessage = nil
        do {
            saved = try await FabulisAPIClient.shared.saveDraft(id: draftId, request: SaveDraftRequest(
                categoryId: selectedCategoryId,
                newCategoryName: selectedCategoryId == nil ? newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines) : nil,
                storyId: saveAsVersion ? selectedStoryId : nil,
                newStoryTitle: saveAsVersion ? nil : newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines)))
            onSaved()
        } catch { errorMessage = error.localizedDescription }
    }
}
