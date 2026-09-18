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
    @State private var reasoningEffort: ReasoningEffort?
    @State private var isSaving = false
    @State private var isLoading = true
    @State private var savedAt: Date?
    @State private var errorMessage: String?
    @State private var showingDiscardConfirm = false
    // load() must run exactly once. SwiftUI restarts .task whenever the view
    // reappears — including on the pop back from the pushed model picker —
    // and a second load would overwrite unsaved edits with the stored values.
    @State private var didLoad = false

    // Snapshot of the loaded values (joined into one string over all fields),
    // to detect unsaved edits before the pushed editor is popped by Back.
    @State private var originalSignature = ""

    private var currentSignature: String {
        [name, prompt, titlingPrompt, modelName, String(temperature),
         topP, maxTokens, minP, topK, topA,
         reasoningEffort?.rawValue ?? ""].joined(separator: "\u{1}")
    }

    private var hasChanges: Bool { !isLoading && currentSignature != originalSignature }

    var body: some View {
        Form {
            Section("Identity") {
                TextField("Name", text: $name).textInputAutocapitalization(.words)
            }
            Section("System prompt") {
                TextEditor(text: $prompt).accessibilityLabel("Writing instructions").frame(minHeight: 120)
            }
            Section("Titling prompt") {
                TextEditor(text: $titlingPrompt).accessibilityLabel("Title instructions").frame(minHeight: 100)
            }
            Section {
                NavigationLink {
                    ModelPickerView(title: "Storyteller Model", currentModel: modelName) { picked in
                        modelName = picked
                    }
                } label: {
                    LabeledContent("Model", value: modelName.isEmpty ? "—" : modelName)
                }
                Picker("Reasoning", selection: $reasoningEffort) {
                    Text("Model default").tag(ReasoningEffort?.none)
                    ForEach(ReasoningEffort.allCases, id: \.self) { effort in
                        Text(effort.label).tag(ReasoningEffort?.some(effort))
                    }
                }
            } header: {
                Text("Model")
            } footer: {
                Text("Reasoning applies to story generation only. Titles and summaries never use it.")
            }
            Section("Creativity") {
                HStack {
                    Text("Temperature")
                    Slider(value: $temperature, in: 0...2, step: 0.05) {
                        Text("Temperature")
                    }
                    .accessibilityValue(String(format: "%.2f", temperature))
                    Text(String(format: "%.2f", temperature)).font(.caption.monospacedDigit())
                }
            }
            Section {
                DisclosureGroup("Advanced Sampling") {
                LabeledNumberField(label: "Top P (0–1)", value: $topP)
                LabeledNumberField(label: "Maximum tokens", value: $maxTokens, integer: true)
                LabeledNumberField(label: "Min P (0–1)", value: $minP)
                LabeledNumberField(label: "Top K (integer)", value: $topK, integer: true)
                LabeledNumberField(label: "Top A (0–1)", value: $topA)
            }
            }
            if let validationError {
                Section { Text(validationError).foregroundStyle(.red) }
            }
            if let savedAt {
                Section { Text("Saved \(savedAt.formatted(date: .omitted, time: .standard))").font(.caption).foregroundStyle(.green) }
            }
            if let errorMessage {
                Section { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .disabled(isLoading || isSaving)
        .focusedSceneValue(\.contentActions, ContentActions(saveChanges: canSave && !isSaving ? { Task { await save() } } : nil))
        .protectUnsavedChanges(hasChanges)
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
        .task {
            guard !didLoad else { return }
            await load()
        }
    }

    private var validationError: String? {
        SamplingValidation.error(topP: topP, maxTokens: maxTokens, minP: minP, topK: topK, topA: topA)
    }

    private var canSave: Bool {
        validationError == nil && existing != nil && !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
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
            reasoningEffort = s.reasoningEffort
            originalSignature = currentSignature
            didLoad = true
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        guard canSave else { return }
        errorMessage = nil; isSaving = true; defer { isSaving = false }
        do {
            try await FabulisAPIClient.shared.updateStoryteller(StorytellerUpdateRequest(
                name: name.trimmingCharacters(in: .whitespacesAndNewlines),
                prompt: prompt,
                titlingPrompt: titlingPrompt,
                modelName: modelName.trimmingCharacters(in: .whitespacesAndNewlines),
                temperature: temperature,
                topP: Double(topP.trimmingCharacters(in: .whitespacesAndNewlines)),
                maxTokens: Int(maxTokens.trimmingCharacters(in: .whitespacesAndNewlines)),
                minP: Double(minP.trimmingCharacters(in: .whitespacesAndNewlines)),
                topK: Int(topK.trimmingCharacters(in: .whitespacesAndNewlines)),
                topA: Double(topA.trimmingCharacters(in: .whitespacesAndNewlines)),
                reasoningEffort: reasoningEffort))
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
    var integer = false

    var body: some View {
        LabeledContent(label) {
            TextField("Model default", text: $value)
                .keyboardType(integer ? .numberPad : .decimalPad)
                .multilineTextAlignment(.trailing)
        }
    }
}
