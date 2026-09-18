import SwiftUI

private enum SettingsPane: String, CaseIterable, Identifiable {
    case general = "General", writing = "Writing", narration = "Narration", connection = "Connection & Vault"
    var id: String { rawValue }
    var symbol: String {
        switch self {
        case .general: "gear"
        case .writing: "square.and.pencil"
        case .narration: "speaker.wave.2"
        case .connection: "lock.shield"
        }
    }
}

struct SettingsView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(AppState.self) private var appState
    var body: some View {
        List(SettingsPane.allCases) { pane in
            NavigationLink { SettingsPaneView(pane: pane) } label: {
                Label(pane.rawValue, systemImage: pane.symbol)
            }
        }
        .navigationTitle("Settings")
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Done") { dismiss() }.disabled(appState.hasUnsavedChanges)
            }
        }
    }
}

private struct SettingsPaneView: View {
    let pane: SettingsPane
    @Environment(AppState.self) private var appState
    @Environment(\.dismiss) private var dismiss

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
    @State private var summaryPromptDraft: String = ""
    @State private var summaryPromptJustSaved = false
    @State private var didLoad = false
    @State private var showingDiscardConfirm = false
    @State private var isSavingSummaryPrompt = false
    private var hasChanges: Bool {
        didLoad && (!apiKeyDraft.isEmpty || !kokoroUrlDraft.isEmpty || summaryPromptDraft != (settings?.summaryPrompt ?? ""))
    }

    private let autoLockOptions: [(label: String, value: String)] = [
        ("1 minute", "1"), ("5 minutes", "5"), ("15 minutes", "15"),
        ("30 minutes", "30"), ("1 hour", "60"), ("Never", "never")
    ]

    var body: some View {
        Group {
            if isLoading { ProgressView("Loading settings…") }
            else if settings == nil {
                LoadFailedView(title: "Couldn't load settings", message: errorMessage ?? "Try again.") { Task { await load() } }
            } else {
                Form {
                    switch pane {
                    case .general: generalSettings
                    case .writing: writingSettings
                    case .narration: narrationSettings
                    case .connection: connectionSettings
                    }
                    if let errorMessage {
                        Section { Label(errorMessage, systemImage: "exclamationmark.triangle").foregroundStyle(.red) }
                    }
                }
            }
        }
        .navigationTitle(pane.rawValue)
        .navigationBarBackButtonHidden(hasChanges)
        .protectUnsavedChanges(hasChanges)
        .toolbar {
            if hasChanges {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { showingDiscardConfirm = true }
                }
            }
        }
        .discardChangesConfirmation(isPresented: $showingDiscardConfirm) { dismiss() }
        .onChange(of: apiKeyDraft) { _, value in if !value.isEmpty { apiKeyJustSaved = false } }
        .onChange(of: kokoroUrlDraft) { _, value in if !value.isEmpty { kokoroUrlJustSaved = false } }
        .onChange(of: summaryPromptDraft) { _, _ in summaryPromptJustSaved = false }
        .task { if !didLoad { await load() } }
    }

    private var generalSettings: some View {
        Group {
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

        }
    }

    private var writingSettings: some View {
        Group {
            Section("Storyteller") {
                NavigationLink("Edit storyteller", destination: StorytellerEditorView())
            }

            Section("Story summaries") {
                if let settings, let current = settings.summaryModel {
                    Text(current).font(.callout.monospaced()).foregroundStyle(.secondary)
                }
                NavigationLink {
                    ModelPickerView(title: "Summary Model",
                                    currentModel: settings?.summaryModel) { picked in
                        Task { await saveSummaryModel(picked) }
                    }
                } label: {
                    Text(settings?.summaryModel == nil ? "Choose summary model (defaults to the storyteller model)" : "Change summary model")
                }

                VStack(alignment: .leading, spacing: 4) {
                    Text("Summary prompt").font(.caption).foregroundStyle(.secondary)
                    TextEditor(text: $summaryPromptDraft)
                        .accessibilityLabel("Summary instructions")
                        .frame(minHeight: 120)
                        .font(.callout)
                }
                Button {
                    Task { await saveSummaryPrompt() }
                } label: {
                    Text(isSavingSummaryPrompt ? "Saving…" : "Save Instructions")
                }
                .disabled(isSavingSummaryPrompt || summaryPromptDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || summaryPromptDraft == settings?.summaryPrompt)
                if summaryPromptJustSaved {
                    Text("Summary prompt saved.").font(.caption).foregroundStyle(.green)
                }
            }

        }
    }

    private var narrationSettings: some View {
        Group {
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
                ) { Text("Narration speed") }
                .accessibilityValue(String(format: "%.2f×", speedDraft))

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

        }
    }

    private var connectionSettings: some View {
        Group {
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

            Section("Vault") {
                Button {
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

        }
    }

    private func load() async {
        isLoading = true
        errorMessage = nil
        do {
            #if DEBUG
            if UIFixtures.enabled { serverURL = "Sample server" }
            else { serverURL = (try? await KeychainService.shared.loadServerURL()) ?? "" }
            #else
            serverURL = (try? await KeychainService.shared.loadServerURL()) ?? ""
            #endif
            settings = try await FabulisAPIClient.shared.settings()
            if let settings { speedDraft = settings.narrationSpeed }
            if let settings { summaryPromptDraft = settings.summaryPrompt }
            didLoad = true
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func saveApiKey() async {
        errorMessage = nil
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

    private func saveSummaryModel(_ model: String) async {
        do {
            try await FabulisAPIClient.shared.updateSettings(summaryModel: model)
            settings = try await FabulisAPIClient.shared.settings()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func saveSummaryPrompt() async {
        errorMessage = nil
        guard !isSavingSummaryPrompt else { return }
        isSavingSummaryPrompt = true; defer { isSavingSummaryPrompt = false }
        let trimmed = summaryPromptDraft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        do {
            try await FabulisAPIClient.shared.updateSettings(summaryPrompt: trimmed)
            settings = try await FabulisAPIClient.shared.settings()
            summaryPromptDraft = trimmed
            summaryPromptJustSaved = true
            Task { try? await Task.sleep(for: .seconds(3)); summaryPromptJustSaved = false }
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
        errorMessage = nil
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
            if let settings { speedDraft = settings.narrationSpeed }
            errorMessage = error.localizedDescription
        }
    }
}
