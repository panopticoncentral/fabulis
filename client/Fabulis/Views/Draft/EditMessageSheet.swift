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
            VStack(spacing: 0) {
                TextEditor(text: $content)
                    .font(.body)
                    .padding(8)
                    .scrollContentBackground(.hidden)
                    .background(Color(.secondarySystemBackground))

                if message.role == .prompt {
                    Text("Save & resubmit deletes every message after this prompt and starts a new response from your edited text.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal)
                        .padding(.top, 8)
                }

                if let errorMessage {
                    Text(errorMessage)
                        .font(.caption)
                        .foregroundStyle(.red)
                        .padding(.horizontal)
                        .padding(.top, 4)
                }

                Divider().padding(.top, 8)

                HStack(spacing: 12) {
                    Button("Cancel") { dismiss() }
                        .buttonStyle(.bordered)
                        .disabled(isSaving)
                    Spacer()
                    if message.role == .prompt {
                        Button {
                            saveAndResubmit()
                        } label: {
                            Label("Save & Resubmit", systemImage: "arrow.clockwise")
                        }
                        .buttonStyle(.bordered)
                        .disabled(!canSave || isSaving)
                    }
                    Button {
                        Task { await saveOnly() }
                    } label: {
                        if isSaving {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Save")
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!canSave || isSaving)
                }
                .padding()
            }
            .navigationTitle("Edit \(roleLabel)")
            .navigationBarTitleDisplayMode(.inline)
            .interactiveDismissDisabled(isSaving)
            .onAppear { content = message.content }
        }
        .frame(minWidth: 480, minHeight: 360)
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
