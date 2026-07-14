import SwiftUI

struct StorytellerEditorView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var existing: StorytellerDto?
    @State private var name: String = ""
    @State private var prompt: String = ""
    @State private var titlingPrompt: String = ""
    @State private var modelName: String = ""
    @State private var temperature: Double = 0.7
    @State private var topP: String = ""
    @State private var maxTokens: String = ""
    @State private var minP: String = ""
    @State private var topK: String = ""
    @State private var topA: String = ""
    @State private var isSaving = false
    @State private var isLoading = true
    @State private var savedAt: Date?
    @State private var errorMessage: String?
    @State private var showingDiscardConfirm = false

    // Snapshot of the loaded values (joined into one string over all fields),
    // to detect unsaved edits before the pushed editor is popped by Back.
    @State private var originalSignature = ""

    private var currentSignature: String {
        [name, prompt, titlingPrompt, modelName, String(temperature),
         topP, maxTokens, minP, topK, topA].joined(separator: "\u{1}")
    }

    private var hasChanges: Bool { !isLoading && currentSignature != originalSignature }

    var body: some View {
        Form {
            Section("Identity") {
                TextField("Name", text: $name).textInputAutocapitalization(.words)
            }
            Section("System prompt") {
                TextEditor(text: $prompt).frame(minHeight: 120)
            }
            Section("Titling prompt") {
                TextEditor(text: $titlingPrompt).frame(minHeight: 100)
            }
            Section("Model") {
                NavigationLink {
                    ModelPickerView(title: "Storyteller Model", currentModel: modelName) { picked in
                        modelName = picked
                    }
                } label: {
                    LabeledContent("Model", value: modelName.isEmpty ? "—" : modelName)
                }
            }
            Section("Sampling") {
                HStack {
                    Text("Temperature")
                    Slider(value: $temperature, in: 0...2, step: 0.05) {
                        Text("Temperature")
                    }
                    .accessibilityValue(String(format: "%.2f", temperature))
                    Text(String(format: "%.2f", temperature)).font(.caption.monospacedDigit())
                }
                LabeledNumberField(label: "top_p (0-1)", value: $topP)
                LabeledNumberField(label: "max_tokens", value: $maxTokens)
                LabeledNumberField(label: "min_p (0-1)", value: $minP)
                LabeledNumberField(label: "top_k (int)", value: $topK)
                LabeledNumberField(label: "top_a (0-1)", value: $topA)
            }
            if let savedAt {
                Section { Text("Saved \(savedAt.formatted(date: .omitted, time: .standard))").font(.caption).foregroundStyle(.green) }
            }
            if let errorMessage {
                Section { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .disabled(isLoading)
        .overlay { if isLoading { ProgressView().controlSize(.large) } }
        .navigationTitle("Storyteller")
        .navigationBarBackButtonHidden(hasChanges)
        .toolbar {
            if hasChanges {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { showingDiscardConfirm = true }.fixedSize()
                }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                    .disabled(!canSave || isSaving)
                    .fixedSize()
            }
        }
        .confirmationDialog("Discard changes?", isPresented: $showingDiscardConfirm,
                            titleVisibility: .visible) {
            Button("Discard Changes", role: .destructive) { dismiss() }
            Button("Keep Editing", role: .cancel) {}
        }
        .task { await load() }
    }

    private var canSave: Bool {
        !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !modelName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    private func load() async {
        do {
            let s = try await FabulisAPIClient.shared.getStoryteller()
            existing = s
            name = s.name
            prompt = s.prompt
            titlingPrompt = s.titlingPrompt
            modelName = s.modelName
            temperature = s.temperature
            topP = s.topP.map { String($0) } ?? ""
            maxTokens = s.maxTokens.map { String($0) } ?? ""
            minP = s.minP.map { String($0) } ?? ""
            topK = s.topK.map { String($0) } ?? ""
            topA = s.topA.map { String($0) } ?? ""
            originalSignature = currentSignature
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            try await FabulisAPIClient.shared.updateStoryteller(StorytellerUpdateRequest(
                name: name.trimmingCharacters(in: .whitespacesAndNewlines),
                prompt: prompt,
                titlingPrompt: titlingPrompt,
                modelName: modelName.trimmingCharacters(in: .whitespacesAndNewlines),
                temperature: temperature,
                topP: Double(topP),
                maxTokens: Int(maxTokens),
                minP: Double(minP),
                topK: Int(topK),
                topA: Double(topA)))
            savedAt = Date()
            originalSignature = currentSignature
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

private struct LabeledNumberField: View {
    let label: String
    @Binding var value: String

    var body: some View {
        LabeledContent(label) {
            TextField("blank = unset", text: $value)
                .keyboardType(.decimalPad)
                .multilineTextAlignment(.trailing)
        }
    }
}
