import SwiftUI

struct SettingsView: View {
    @Environment(AppState.self) private var appState

    @State private var serverURL: String = ""
    @State private var settings: SettingsDto?
    @State private var apiKeyDraft: String = ""
    @State private var apiKeyJustSaved = false
    @State private var isSavingApiKey = false
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var isLocking = false

    private let autoLockOptions: [(label: String, value: String)] = [
        ("1 minute", "1"), ("5 minutes", "5"), ("15 minutes", "15"),
        ("30 minutes", "30"), ("1 hour", "60"), ("Never", "never")
    ]

    var body: some View {
        Form {
            Section("Server") { LabeledContent("URL", value: serverURL) }

            Section("OpenRouter API key") {
                if let settings, settings.apiKeyIsSet { Text("Key is set").foregroundStyle(.secondary) }
                SecureField("sk-or-...", text: $apiKeyDraft)
                Button {
                    Task { await saveApiKey() }
                } label: {
                    HStack {
                        if isSavingApiKey { ProgressView().controlSize(.mini) }
                        Text("Save key")
                    }
                }
                .disabled(apiKeyDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSavingApiKey)
                if apiKeyJustSaved {
                    Text("API key saved.").font(.caption).foregroundStyle(.green)
                }
            }

            Section("Assistant model") {
                if let settings, let current = settings.assistantModel {
                    Text(current).font(.callout.monospaced()).foregroundStyle(.secondary)
                }
                NavigationLink {
                    ModelPickerView(currentModel: settings?.assistantModel) { picked in
                        Task { await saveModel(picked) }
                    }
                } label: {
                    Text(settings?.assistantModel == nil ? "Choose model" : "Change model")
                }
            }

            Section("Storyteller") {
                NavigationLink("Edit storyteller", destination: StorytellerEditorView())
            }

            Section("Auto-lock") {
                if let settings {
                    Picker("After", selection: Binding(
                        get: { settings.autoLockSelection },
                        set: { newValue in Task { await saveAutoLock(newValue) } }
                    )) {
                        ForEach(autoLockOptions, id: \.value) { opt in
                            Text(opt.label).tag(opt.value)
                        }
                    }
                }
            }

            Section("Vault") {
                Button(role: .destructive) {
                    Task {
                        isLocking = true
                        await appState.lock()
                        isLocking = false
                    }
                } label: {
                    HStack { Image(systemName: "lock.fill"); Text("Lock vault") }
                }
                .disabled(isLocking)
            }

            if let errorMessage {
                Section { Text(errorMessage).foregroundStyle(.red) }
            }
        }
        .navigationTitle("Settings")
        .task { await load() }
    }

    private func load() async {
        do {
            serverURL = (try? await KeychainService.shared.loadServerURL()) ?? ""
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func saveApiKey() async {
        let key = apiKeyDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !key.isEmpty else { return }
        isSavingApiKey = true; defer { isSavingApiKey = false }
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: key, assistantModel: nil, autoLockSelection: nil))
            apiKeyDraft = ""
            apiKeyJustSaved = true
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveModel(_ model: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: nil, assistantModel: model, autoLockSelection: nil))
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveAutoLock(_ selection: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(SettingsUpdateRequest(apiKey: nil, assistantModel: nil, autoLockSelection: selection))
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
