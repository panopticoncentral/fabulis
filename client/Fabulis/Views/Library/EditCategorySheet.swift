import SwiftUI

struct EditCategorySheet: View {
    enum Mode { case create, rename(id: Int) }

    let mode: Mode
    let initialName: String
    let onSaved: () -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var name: String = ""
    @State private var isSaving = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name", text: $name).textInputAutocapitalization(.words)
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle(title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.fixedSize()
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                        .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSaving)
                        .fixedSize()
                }
            }
            .onAppear { name = initialName }
        }
    }

    private var title: String {
        switch mode {
        case .create: return "New Category"
        case .rename: return "Rename Category"
        }
    }

    private func save() async {
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
            switch mode {
            case .create:
                _ = try await FabulisAPIClient.shared.createCategory(name: trimmed)
            case .rename(let id):
                try await FabulisAPIClient.shared.renameCategory(id: id, name: trimmed)
            }
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
