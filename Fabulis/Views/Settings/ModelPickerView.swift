import SwiftUI

struct ModelPickerView: View {
    @Environment(\.dismiss) private var dismiss
    @Environment(LibraryViewModel.self) private var viewModel

    @Binding var selectedModelId: String
    @State private var searchText = ""

    var filteredModels: [OpenRouterModel] {
        if searchText.isEmpty {
            return viewModel.availableModels
        }
        return viewModel.availableModels.filter {
            $0.displayName.localizedCaseInsensitiveContains(searchText) ||
            $0.id.localizedCaseInsensitiveContains(searchText)
        }
    }

    var groupedModels: [String: [OpenRouterModel]] {
        Dictionary(grouping: filteredModels) { $0.providerName.capitalized }
    }

    var sortedProviders: [String] {
        groupedModels.keys.sorted()
    }

    var body: some View {
        NavigationStack {
            List {
                if searchText.isEmpty {
                    Section("Recommended for Storytelling") {
                        ForEach(viewModel.availableModels.filter {
                            StorytellerDefaults.recommendedModelIds.contains($0.id)
                        }) { model in
                            ModelRow(model: model, isSelected: model.id == selectedModelId)
                                .contentShape(Rectangle())
                                .onTapGesture {
                                    selectedModelId = model.id
                                    dismiss()
                                }
                        }
                    }
                }

                ForEach(sortedProviders, id: \.self) { provider in
                    Section(provider) {
                        ForEach(groupedModels[provider] ?? []) { model in
                            ModelRow(model: model, isSelected: model.id == selectedModelId)
                                .contentShape(Rectangle())
                                .onTapGesture {
                                    selectedModelId = model.id
                                    dismiss()
                                }
                        }
                    }
                }
            }
            .searchable(text: $searchText, prompt: "Search models...")
            .navigationTitle("Select Model")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .overlay {
                if viewModel.isLoadingModels {
                    ProgressView("Loading models...")
                } else if let error = viewModel.modelsError {
                    ContentUnavailableView(
                        "Failed to Load Models",
                        systemImage: "exclamationmark.triangle",
                        description: Text(error)
                    )
                }
            }
        }
    }
}

struct ModelRow: View {
    let model: OpenRouterModel
    let isSelected: Bool

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 4) {
                Text(model.modelName)
                    .font(.body)

                HStack(spacing: 8) {
                    Text("\(model.contextLength / 1000)K context")
                    Text("$\(model.pricing.promptCostPerMillion, specifier: "%.2f")/M")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }

            Spacer()

            if isSelected {
                Image(systemName: "checkmark")
                    .foregroundStyle(.tint)
            }
        }
    }
}

#Preview {
    ModelPickerView(selectedModelId: .constant("anthropic/claude-sonnet-4"))
        .environment(LibraryViewModel())
}
