import SwiftUI

struct SaveDraftSheet: View {
    let draftId: Int
    let draftTitle: String?
    /// Called after a successful save so the Library sidebar can refresh its
    /// category list and story counts.
    var onSaved: () -> Void = {}

    @Environment(\.dismiss) private var dismiss
    @State private var categories: [CategorySummary] = []
    @State private var selectedCategoryId: Int? = nil
    @State private var newCategoryName: String = ""
    @State private var storiesInCategory: [StorySummary] = []
    @State private var selectedStoryId: Int? = nil
    @State private var newStoryTitle: String = ""
    @State private var isSaving = false
    @State private var isGeneratingTitle = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section("Category") {
                    Picker("Category", selection: $selectedCategoryId) {
                        Text("— New category —").tag(Int?.none)
                        ForEach(categories) { cat in
                            Text(cat.name).tag(Int?(cat.id))
                        }
                    }
                    if selectedCategoryId == nil {
                        TextField("New category name", text: $newCategoryName)
                            .textInputAutocapitalization(.words)
                    }
                }

                if let catId = selectedCategoryId {
                    Section("Story") {
                        Picker("Story", selection: $selectedStoryId) {
                            Text("— New story —").tag(Int?.none)
                            ForEach(storiesInCategory) { s in
                                Text(s.title).tag(Int?(s.id))
                            }
                        }
                        .task(id: catId) { await loadStories(in: catId) }
                    }
                }

                if selectedStoryId == nil {
                    Section("Story title") {
                        TextField("Story title", text: $newStoryTitle)
                        Button {
                            Task { await generateTitle() }
                        } label: {
                            if isGeneratingTitle {
                                HStack { ProgressView(); Text("Generating…") }
                            } else {
                                Label("Generate", systemImage: "sparkles")
                            }
                        }
                        .disabled(isGeneratingTitle)
                    }
                }

                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Save to Library")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                        .disabled(!canSave || isSaving)
                }
            }
            .task {
                await loadCategories()
                newStoryTitle = draftTitle ?? ""
            }
        }
    }

    private var canSave: Bool {
        let categoryReady = selectedCategoryId != nil
            || !newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let storyReady = selectedStoryId != nil
            || !newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        return categoryReady && storyReady
    }

    private func loadCategories() async {
        do {
            let resp = try await FabulisAPIClient.shared.library()
            categories = resp.categories
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func loadStories(in categoryId: Int) async {
        do {
            let detail = try await FabulisAPIClient.shared.category(id: categoryId)
            storiesInCategory = detail.stories
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func generateTitle() async {
        errorMessage = nil
        isGeneratingTitle = true
        defer { isGeneratingTitle = false }
        do {
            let title = try await FabulisAPIClient.shared.generateTitle(draftId: draftId)
            newStoryTitle = title
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func save() async {
        errorMessage = nil
        isSaving = true
        defer { isSaving = false }
        do {
            let req = SaveDraftRequest(
                categoryId: selectedCategoryId,
                newCategoryName: selectedCategoryId == nil ? newCategoryName.trimmingCharacters(in: .whitespacesAndNewlines) : nil,
                storyId: selectedStoryId,
                newStoryTitle: selectedStoryId == nil ? newStoryTitle.trimmingCharacters(in: .whitespacesAndNewlines) : nil)
            _ = try await FabulisAPIClient.shared.saveDraft(id: draftId, request: req)
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
