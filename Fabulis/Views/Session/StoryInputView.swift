import SwiftUI

struct StoryInputView: View {
    @Binding var userInput: String
    let isGenerating: Bool
    let canRegenerate: Bool
    let lastUserPrompt: String?
    let onContinue: () -> Void
    let onSubmit: (String) -> Void
    let onRegenerate: () -> Void
    let onRegenerateWithEdit: (String) -> Void

    @State private var showingEditPrompt = false
    @State private var editedPrompt = ""

    var body: some View {
        VStack(spacing: 12) {
            HStack(spacing: 12) {
                Button {
                    onContinue()
                } label: {
                    Label("Continue", systemImage: "arrow.right.circle.fill")
                }
                .buttonStyle(.bordered)
                .disabled(isGenerating)

                if canRegenerate {
                    Button {
                        onRegenerate()
                    } label: {
                        Label("Regenerate", systemImage: "arrow.clockwise")
                    }
                    .buttonStyle(.bordered)
                    .disabled(isGenerating)

                    Button {
                        editedPrompt = lastUserPrompt ?? ""
                        showingEditPrompt = true
                    } label: {
                        Label("Edit & Regenerate", systemImage: "pencil")
                    }
                    .buttonStyle(.bordered)
                    .disabled(isGenerating)
                }

                Spacer()
            }
            .padding(.horizontal)
            .padding(.top, 8)

            HStack(spacing: 12) {
                TextField("Guide the story...", text: $userInput, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .lineLimit(1...4)
                    .disabled(isGenerating)

                Button {
                    if !userInput.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        onSubmit(userInput)
                    }
                } label: {
                    if isGenerating {
                        ProgressView()
                    } else {
                        Image(systemName: "paperplane.fill")
                    }
                }
                .buttonStyle(.borderedProminent)
                .disabled(userInput.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isGenerating)
            }
            .padding(.horizontal)
            .padding(.bottom, 8)
        }
        .background(.bar)
        .sheet(isPresented: $showingEditPrompt) {
            NavigationStack {
                VStack(alignment: .leading, spacing: 16) {
                    Text("Edit your prompt and regenerate the response.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)

                    TextEditor(text: $editedPrompt)
                        .frame(minHeight: 150)
                        .padding(8)
                        .background(Color(.systemGray6))
                        .clipShape(RoundedRectangle(cornerRadius: 12))

                    Spacer()
                }
                .padding()
                .navigationTitle("Edit Prompt")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Cancel") {
                            showingEditPrompt = false
                        }
                    }
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Regenerate") {
                            showingEditPrompt = false
                            onRegenerateWithEdit(editedPrompt)
                        }
                        .disabled(editedPrompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                }
            }
            .presentationDetents([.medium])
        }
    }
}

#Preview {
    VStack {
        Spacer()
        StoryInputView(
            userInput: .constant(""),
            isGenerating: false,
            canRegenerate: true,
            lastUserPrompt: "Tell me a story about a dragon",
            onContinue: {},
            onSubmit: { _ in },
            onRegenerate: {},
            onRegenerateWithEdit: { _ in }
        )
    }
}
