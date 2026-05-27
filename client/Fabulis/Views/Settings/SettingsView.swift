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
    @State private var kokoroUrlDraft: String = ""
    @State private var isSavingKokoroUrl = false
    @State private var kokoroUrlJustSaved = false
    @State private var speedDraft: Double = 1.0

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

            Section("Narration") {
                if let settings, settings.kokoroBaseUrlIsSet {
                    Text("Server URL is set").foregroundStyle(.secondary)
                }
                TextField("http://localhost:8880", text: $kokoroUrlDraft)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled(true)
                    .keyboardType(.URL)
                Button {
                    Task { await saveKokoroUrl() }
                } label: {
                    HStack {
                        if isSavingKokoroUrl { ProgressView().controlSize(.mini) }
                        Text("Save URL")
                    }
                }
                .disabled(kokoroUrlDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSavingKokoroUrl)
                if kokoroUrlJustSaved {
                    Text("Saved.").font(.caption).foregroundStyle(.green)
                }

                NavigationLink {
                    NarrationVoicePickerView(currentVoice: settings?.narrationVoice) { picked in
                        Task { await saveVoice(picked) }
                    }
                } label: {
                    HStack {
                        Text("Voice")
                        Spacer()
                        Text(settings?.narrationVoice ?? "Not set").foregroundStyle(.secondary)
                    }
                }
                .disabled(settings?.kokoroBaseUrlIsSet != true)

                HStack {
                    Text("Speed")
                    Spacer()
                    Text(String(format: "%.2f×", speedDraft))
                        .monospacedDigit().foregroundStyle(.secondary)
                }
                Slider(
                    value: $speedDraft, in: 0.5...2.0, step: 0.25,
                    onEditingChanged: { editing in
                        if !editing { Task { await saveSpeed(speedDraft) } }
                    }
                )

                if let settings, settings.kokoroBaseUrlIsSet, !settings.narrationAvailable {
                    if settings.narrationVoice == nil {
                        Text("Pick a voice to enable narration.")
                            .font(.caption).foregroundStyle(.orange)
                    } else {
                        Text("Narration server unreachable.")
                            .font(.caption).foregroundStyle(.orange)
                    }
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
            if let settings { speedDraft = settings.narrationSpeed }
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
            try await FabulisAPIClient.shared.updateSettings(apiKey: key)
            apiKeyDraft = ""
            apiKeyJustSaved = true
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveModel(_ model: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(assistantModel: model)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveAutoLock(_ selection: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(autoLockSelection: selection)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveKokoroUrl() async {
        let trimmed = kokoroUrlDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        isSavingKokoroUrl = true
        do {
            try await FabulisAPIClient.shared.updateSettings(kokoroBaseUrl: trimmed)
            settings = try await FabulisAPIClient.shared.settings()
            kokoroUrlDraft = ""
            kokoroUrlJustSaved = true
            Task { try? await Task.sleep(for: .seconds(3)); kokoroUrlJustSaved = false }
        } catch {
            errorMessage = error.localizedDescription
        }
        isSavingKokoroUrl = false
    }

    private func saveVoice(_ voice: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(narrationVoice: voice)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveSpeed(_ speed: Double) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(narrationSpeed: speed)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
