import SwiftUI

struct StorytellerEditorView: View {
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
    @State private var savedAt: Date?
    @State private var errorMessage: String?

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
                    ModelPickerView(currentModel: modelName) { picked in
                        modelName = picked
                    }
                } label: {
                    LabeledContent("Model", value: modelName.isEmpty ? "—" : modelName)
                }
            }
            Section("Sampling") {
                HStack {
                    Text("Temperature").frame(width: 110, alignment: .leading)
                    Slider(value: $temperature, in: 0...2, step: 0.05)
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
        .navigationTitle("Storyteller")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button(isSaving ? "Saving…" : "Save") { Task { await save() } }
                    .disabled(!canSave || isSaving)
            }
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
        } catch {
            errorMessage = error.localizedDescription
        }
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
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

private struct LabeledNumberField: View {
    let label: String
    @Binding var value: String

    var body: some View {
        HStack {
            Text(label).frame(width: 110, alignment: .leading)
            TextField("blank = unset", text: $value)
                .keyboardType(.decimalPad)
                .multilineTextAlignment(.trailing)
        }
    }
}
