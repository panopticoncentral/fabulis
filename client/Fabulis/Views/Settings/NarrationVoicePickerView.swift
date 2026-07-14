import SwiftUI

struct NarrationVoicePickerView: View {
    let currentVoice: String?
    let onPicked: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var voices: [NarrationVoice] = []
    @State private var isLoading = true
    @State private var errorMessage: String?

    private var grouped: [(String, [NarrationVoice])] {
        let groups = Dictionary(grouping: voices, by: \.language)
        return groups.keys.sorted().map { ($0, groups[$0]!.sorted { $0.displayName < $1.displayName }) }
    }

    var body: some View {
        Group {
            if isLoading {
                ProgressView()
            } else if let errorMessage {
                LoadFailedView(title: "Couldn't load voices",
                               message: errorMessage) { Task { await load() } }
            } else {
                List {
                    ForEach(grouped, id: \.0) { language, items in
                        Section(language) {
                            ForEach(items) { voice in
                                Button {
                                    onPicked(voice.id)
                                    dismiss()
                                } label: {
                                    HStack {
                                        Text(voice.displayName)
                                        Spacer()
                                        if voice.id == currentVoice {
                                            Image(systemName: "checkmark")
                                                .foregroundStyle(.tint)
                                                .accessibilityHidden(true)
                                        }
                                    }
                                    .contentShape(Rectangle())
                                }
                                .accessibilityAddTraits(voice.id == currentVoice ? [.isSelected] : [])
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("Voice")
        .task { await load() }
    }

    private func load() async {
        do {
            voices = try await FabulisAPIClient.shared.narrationVoices()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
