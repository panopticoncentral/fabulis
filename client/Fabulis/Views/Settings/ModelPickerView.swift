import SwiftUI

struct ModelPickerView: View {
    var title: String = "Model"
    let currentModel: String?
    let onPick: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var models: [ModelInfo] = []
    @State private var search: String = ""
    @State private var isLoading = true
    @State private var errorMessage: String?

    private var filtered: [ModelInfo] {
        let q = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return models }
        return models.filter { $0.id.lowercased().contains(q) || $0.name.lowercased().contains(q) }
    }

    var body: some View {
        Group {
            if isLoading {
                ProgressView()
            } else if let errorMessage {
                LoadFailedView(title: "Couldn't load models",
                               message: errorMessage) { Task { await load() } }
            } else if filtered.isEmpty {
                ContentUnavailableView.search(text: search)
            } else {
                List(filtered) { model in
                    Button {
                        onPick(model.id)
                        dismiss()
                    } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(model.id).font(.body.monospaced())
                                Text(model.name).font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if model.id == currentModel {
                                Image(systemName: "checkmark")
                                    .foregroundStyle(.tint)
                                    .accessibilityHidden(true)
                            }
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(model.id == currentModel ? [.isSelected] : [])
                }
            }
        }
        .searchable(text: $search, prompt: "Filter models")
        .navigationTitle(title)
        .task { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            models = try await FabulisAPIClient.shared.models()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
