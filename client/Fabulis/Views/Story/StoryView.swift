import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var selectedVersion: Int?
    @State private var versionDetail: StoryVersionDetail?
    @State private var errorMessage: String?
    @State private var isLoadingStory = true
    @State private var isLoadingVersion = false

    var body: some View {
        Group {
            if let detail {
                if detail.versions.isEmpty {
                    ContentUnavailableView("No versions yet", systemImage: "doc.text",
                        description: Text("This story has no saved versions."))
                } else if let versionDetail {
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 12) {
                            ForEach(versionDetail.messages) { message in
                                StoryMessageView(message: message)
                            }
                        }
                        .padding()
                    }
                } else if isLoadingVersion {
                    ProgressView()
                } else if let errorMessage {
                    errorView(errorMessage)
                }
            } else if isLoadingStory {
                ProgressView()
            } else if let errorMessage {
                errorView(errorMessage)
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .toolbar {
            if let detail, !detail.versions.isEmpty, let selectedVersion {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        ForEach(detail.versions) { version in
                            Button {
                                select(version: version.versionNumber)
                            } label: {
                                if version.versionNumber == selectedVersion {
                                    Label("Version \(version.versionNumber)", systemImage: "checkmark")
                                } else {
                                    Text("Version \(version.versionNumber)")
                                }
                            }
                        }
                    } label: {
                        HStack(spacing: 2) {
                            Text("Version \(selectedVersion)")
                            Image(systemName: "chevron.down").font(.caption2)
                        }
                    }
                }
            }
        }
        .task { await loadStory() }
        .refreshable { await loadStory() }
    }

    @ViewBuilder
    private func errorView(_ message: String) -> some View {
        VStack(spacing: 12) {
            Text("Couldn't load story").font(.headline)
            Text(message).font(.caption).foregroundStyle(.secondary)
            Button("Retry") { Task { await loadStory() } }
        }
        .padding()
    }

    private func select(version: Int) {
        guard version != selectedVersion else { return }
        selectedVersion = version
        Task { await loadVersion(version) }
    }

    private func loadStory() async {
        isLoadingStory = true
        do {
            errorMessage = nil
            let storyDetail = try await FabulisAPIClient.shared.story(id: storyId)
            detail = storyDetail
            // Server returns versions ordered VersionNumber descending, so .first is the latest.
            if let latest = storyDetail.versions.first?.versionNumber {
                selectedVersion = latest
                await loadVersion(latest)
            }
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoadingStory = false
    }

    private func loadVersion(_ version: Int) async {
        isLoadingVersion = true
        versionDetail = nil
        do {
            errorMessage = nil
            versionDetail = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoadingVersion = false
    }
}
