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

    @State private var title = ""
    @State private var categoryId: Int?
    @State private var categories: [CategorySummary] = []
    @State private var messages: [EditableMessage] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?

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
        .navigationTitle("Edit Prompt")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) { EditButton() }
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    Task { await save() }
                } label: {
                    if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                }
                .disabled(saving || isLoading || categoryId == nil)
            }
        }
        .overlay {
            if isLoading { ProgressView() }
        }
        .alert("Couldn't save", isPresented: Binding(
            get: { errorMessage != nil },
            set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(errorMessage ?? "")
        }
        .task { await load() }
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
