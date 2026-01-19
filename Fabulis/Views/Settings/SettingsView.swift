import SwiftUI
import SwiftData

struct SettingsView: View {
    @Environment(\.modelContext) private var modelContext
    @State private var showingAPIKeyChange = false
    @State private var showingDeleteConfirmation = false

    var body: some View {
        NavigationStack {
            List {
                Section("Account") {
                    Button {
                        showingAPIKeyChange = true
                    } label: {
                        Label("Change API Key", systemImage: "key.fill")
                    }

                    Link(destination: URL(string: "https://openrouter.ai/activity")!) {
                        Label("View Usage & Credits", systemImage: "chart.bar.fill")
                    }
                }

                Section("About") {
                    HStack {
                        Text("Version")
                        Spacer()
                        Text("1.0.0")
                            .foregroundStyle(.secondary)
                    }

                    Link(destination: URL(string: "https://openrouter.ai")!) {
                        Label("Powered by OpenRouter", systemImage: "link")
                    }
                }

                Section {
                    Button(role: .destructive) {
                        showingDeleteConfirmation = true
                    } label: {
                        Label("Delete All Data", systemImage: "trash")
                    }
                }
            }
            .navigationTitle("Settings")
            .sheet(isPresented: $showingAPIKeyChange) {
                APIKeyChangeView()
            }
            .confirmationDialog(
                "Delete All Data?",
                isPresented: $showingDeleteConfirmation,
                titleVisibility: .visible
            ) {
                Button("Delete Everything", role: .destructive) {
                    deleteAllData()
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("This will delete all your stories and custom storytellers. This cannot be undone.")
            }
        }
    }

    private func deleteAllData() {
        do {
            try modelContext.delete(model: Story.self)
            try modelContext.delete(model: StorySegment.self)

            let descriptor = FetchDescriptor<Storyteller>(
                predicate: #Predicate { !$0.isBuiltIn }
            )
            let customStorytellers = try modelContext.fetch(descriptor)
            for storyteller in customStorytellers {
                modelContext.delete(storyteller)
            }

            try modelContext.save()
        } catch {
            print("Failed to delete data: \(error)")
        }
    }
}

struct APIKeyChangeView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var viewModel = OnboardingViewModel()

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    SecureField("New API Key", text: $viewModel.apiKey)
                        .textContentType(.password)

                    if let error = viewModel.validationError {
                        Text(error)
                            .foregroundStyle(.red)
                            .font(.caption)
                    }
                } footer: {
                    Text("Enter your new OpenRouter API key. The old key will be replaced.")
                }
            }
            .navigationTitle("Change API Key")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") {
                        Task {
                            if await viewModel.validateAndSave() {
                                dismiss()
                            }
                        }
                    }
                    .disabled(!viewModel.canSubmit)
                }
            }
        }
    }
}

#Preview {
    SettingsView()
        .modelContainer(for: [Storyteller.self, Story.self, StorySegment.self], inMemory: true)
}
