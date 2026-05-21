import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var selectedVersion: Int?
    @State private var versionDetail: StoryVersionDetail?
    @State private var storyError: String?
    @State private var versionError: String?
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
                } else if let versionError {
                    errorView("Couldn't load version", versionError) {
                        if let selectedVersion {
                            Task { await loadVersion(selectedVersion) }
                        }
                    }
                }
            } else if isLoadingStory {
                ProgressView()
            } else if let storyError {
                errorView("Couldn't load story", storyError) {
                    Task { await loadStory() }
                }
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
    private func errorView(_ headline: String, _ message: String, retry: @escaping () -> Void) -> some View {
        VStack(spacing: 12) {
            Text(headline).font(.headline)
            Text(message).font(.caption).foregroundStyle(.secondary)
            Button("Retry", action: retry)
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
        storyError = nil
        do {
            let storyDetail = try await FabulisAPIClient.shared.story(id: storyId)
            detail = storyDetail
            // Preserve the current selection across refresh if it still exists;
            // otherwise default to the latest (versions are ordered descending, so .first).
            let versionNumbers = storyDetail.versions.map(\.versionNumber)
            let target = selectedVersion.flatMap { versionNumbers.contains($0) ? $0 : nil }
                ?? storyDetail.versions.first?.versionNumber
            if let target {
                selectedVersion = target
                await loadVersion(target)
            }
        } catch {
            storyError = error.localizedDescription
        }
        isLoadingStory = false
    }

    private func loadVersion(_ version: Int) async {
        isLoadingVersion = true
        versionError = nil
        versionDetail = nil
        do {
            let result = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
            // Discard a stale result if the user has since selected a different version.
            guard version == selectedVersion else { return }
            versionDetail = result
        } catch {
            guard version == selectedVersion else { return }
            versionError = error.localizedDescription
        }
        guard version == selectedVersion else { return }
        isLoadingVersion = false
    }
}
