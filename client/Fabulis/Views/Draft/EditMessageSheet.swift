import SwiftUI

struct EditMessageSheet: View {
    let draftId: Int
    let message: DraftMessageDto
    /// Called after a plain-save succeeds — parent should reload the draft.
    let onSaved: () -> Void
    /// Called when the user taps Save & Resubmit. Parent kicks off the SSE
    /// `editAndResubmit` flow with the new content.
    let onSaveAndResubmit: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var content: String = ""
    @State private var isSaving = false
    @State private var errorMessage: String?

    private var roleLabel: String {
        switch message.role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Content") {
                    TextEditor(text: $content).frame(minHeight: 160)
                }
                if message.role == .prompt {
                    Section {
                        Button {
                            Task { await saveOnly() }
                        } label: {
                            Label("Save changes", systemImage: "checkmark")
                        }
                        .disabled(!canSave || isSaving)

                        Button {
                            saveAndResubmit()
                        } label: {
                            Label("Save & resubmit", systemImage: "arrow.clockwise")
                        }
                        .disabled(!canSave || isSaving)
                    } footer: {
                        Text("Save & resubmit deletes every message after this prompt and starts a new response from your edited text.")
                    }
                } else {
                    Section {
                        Button {
                            Task { await saveOnly() }
                        } label: {
                            Label("Save changes", systemImage: "checkmark")
                        }
                        .disabled(!canSave || isSaving)
                    }
                }
                if let errorMessage {
                    Section { Text(errorMessage).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Edit \(roleLabel)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
            }
            .onAppear { content = message.content }
        }
    }

    private var canSave: Bool {
        !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func saveOnly() async {
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            try await FabulisAPIClient.shared.editDraftMessage(
                draftId: draftId, messageId: message.id, content: content)
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveAndResubmit() {
        // Hand the new content to the parent; it owns the streaming UI.
        // Dismiss immediately so the user sees the in-flight prompt + stream.
        onSaveAndResubmit(content)
        dismiss()
    }
}
