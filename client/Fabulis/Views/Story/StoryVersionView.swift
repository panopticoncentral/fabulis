import SwiftUI

struct StoryVersionView: View {
    let storyId: Int
    let version: Int
    let modelName: String

    @State private var detail: StoryVersionDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let detail {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        ForEach(detail.messages) { message in
                            StoryMessageView(message: message)
                        }
                    }
                    .padding()
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load version").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle("Version \(version)")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Text(modelName).font(.caption).foregroundStyle(.secondary)
            }
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
