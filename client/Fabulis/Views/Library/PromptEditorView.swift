import SwiftUI

struct PromptEditorView: View {
    let promptId: Int
    /// Called after a successful save. The presenter is responsible for
    /// dismissing the editor (e.g. by clearing the binding that presented it)
    /// and refreshing any affected lists.
    var onSaved: (() -> Void)? = nil

    private struct EditableMessage: Identifiable {
        let id = UUID()
        var text: String
    }

    @Environment(AppState.self) private var appState
    @State private var startingDraft = false
    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var categoryId: Int?
    @State private var categories: [CategorySummary] = []
    @State private var messages: [EditableMessage] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?
    @State private var showingDiscardConfirm = false
    @State private var didLoad = false
    @State private var editMode: EditMode = .inactive

    // Snapshot of the loaded values, to detect unsaved edits before the
    // (pushed) editor is popped by the system Back button.
    @State private var originalTitle = ""
    @State private var originalCategoryId: Int?
    @State private var originalMessageTexts: [String] = []

    private var hasChanges: Bool {
        title != originalTitle
            || categoryId != originalCategoryId
            || messages.map(\.text) != originalMessageTexts
    }

    var body: some View {
        Form {
            Section("Title") {
                TextField("Title", text: $title)
            }
            Section("Category") {
                Picker("Category", selection: $categoryId) {
                    ForEach(categories) { cat in
                        Text(cat.name).tag(Optional(cat.id))
                    }
                }
            }
            Section("Messages") {
                ForEach($messages) { $message in
                    TextField("Message", text: $message.text, axis: .vertical)
                        .lineLimit(1...10)
                }
                .onMove { messages.move(fromOffsets: $0, toOffset: $1) }
                .onDelete { messages.remove(atOffsets: $0) }

                Button {
                    messages.append(EditableMessage(text: ""))
                } label: {
                    Label("Add Message", systemImage: "plus")
                }
            }
        }
        .disabled(isLoading || saving)
        .focusedSceneValue(\.contentActions, ContentActions(saveChanges: !saving && !isLoading && categoryId != nil && !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? { Task { await save() } } : nil))
        .protectUnsavedChanges(hasChanges && !isLoading)
        .environment(\.editMode, $editMode)
        .navigationTitle("Edit Prompt")
        .navigationBarBackButtonHidden(hasChanges)
        .toolbar {
            if hasChanges {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { showingDiscardConfirm = true }.fixedSize().disabled(saving)
                }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button {
                    Task { await save() }
                } label: {
                    if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                }
                .disabled(saving || isLoading || categoryId == nil || title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                .fixedSize()
            }
            ToolbarItem(placement: .secondaryAction) {
                Button("Start Draft", systemImage: "square.and.pencil") { Task { await startDraft() } }
                    .disabled(hasChanges || isLoading || startingDraft || messages.isEmpty)
                    .help(hasChanges ? "Save changes before starting a draft" : "Create a draft using these messages")
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button(editMode.isEditing ? "Done Organizing" : "Organize Messages") {
                    editMode = editMode.isEditing ? .inactive : .active
                }
            }
        }
        .overlay {
            if isLoading { ProgressView() }
        }
        .confirmationDialog("Discard changes?", isPresented: $showingDiscardConfirm,
                            titleVisibility: .visible) {
            Button("Discard Changes", role: .destructive) { dismiss() }
            Button("Keep Editing", role: .cancel) {}
        }
        .alert("Couldn't load or save prompt", isPresented: Binding(
            get: { errorMessage != nil },
            set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(errorMessage ?? "")
        }
        .task { if !didLoad { await load() } }
    }

    private func startDraft() async {
        guard !startingDraft && !hasChanges else { return }
        startingDraft = true; defer { startingDraft = false }
        do { appState.draftRequestedToOpen = try await FabulisAPIClient.shared.createDraftFromPrompt(id: promptId) }
        catch { errorMessage = error.localizedDescription }
    }

    private func load() async {
        do {
            async let lib = FabulisAPIClient.shared.library()
            async let detail = FabulisAPIClient.shared.prompt(id: promptId)
            categories = try await lib.categories
            let prompt = try await detail
            title = prompt.title
            categoryId = prompt.categoryId
            messages = prompt.messages
                .sorted { $0.sortOrder < $1.sortOrder }
                .map { EditableMessage(text: $0.content) }
            didLoad = true
            originalTitle = title
            originalCategoryId = categoryId
            originalMessageTexts = messages.map(\.text)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        guard let categoryId else { return }
        saving = true; defer { saving = false }
        do {
            _ = try await FabulisAPIClient.shared.updatePrompt(
                id: promptId,
                title: title,
                categoryId: categoryId,
                messages: messages.map(\.text))
            onSaved?()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
