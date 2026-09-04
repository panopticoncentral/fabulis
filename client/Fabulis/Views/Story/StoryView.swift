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
    @State private var narrationAvailable = false
    @State private var player = NarrationPlayer()
    @State private var showingSummary = false

    var body: some View {
        Group {
            if let detail {
                if detail.versions.isEmpty {
                    ContentUnavailableView("No versions yet", systemImage: "doc.text",
                        description: Text("This story has no saved versions."))
                } else if let versionDetail {
                    ScrollViewReader { proxy in
                        ScrollView {
                            LazyVStack(alignment: .leading, spacing: 12) {
                                ForEach(versionDetail.messages) { message in
                                    StoryMessageView(
                                        message: message,
                                        isCurrentlyPlaying: player.currentBubbleId == message.id,
                                        narrationAvailable: narrationAvailable,
                                        onPlayFromHere: { startNarration(from: message.id) })
                                        .id(message.id)
                                }
                            }
                            .padding()
                            // Cap the reading column and center it so long-form
                            // prose doesn't stretch edge-to-edge on a wide window.
                            .frame(maxWidth: 720)
                            .frame(maxWidth: .infinity)
                        }
                        .onChange(of: player.currentBubbleId) { _, new in
                            if let new {
                                withAnimation { proxy.scrollTo(new, anchor: .center) }
                            }
                        }
                    }
                } else if isLoadingVersion {
                    ProgressView()
                } else if let versionError {
                    LoadFailedView(title: "Couldn't load version", message: versionError) {
                        if let selectedVersion {
                            Task { await loadVersion(selectedVersion) }
                        }
                    }
                }
            } else if isLoadingStory {
                ProgressView()
            } else if let storyError {
                LoadFailedView(title: "Couldn't load story", message: storyError) {
                    Task { await loadStory() }
                }
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .modelSubtitle(versionDetail?.modelName)
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
            if versionDetail != nil {
                ToolbarItem(placement: .topBarTrailing) {
                    ShareLink(item: shareText) {
                        Image(systemName: "square.and.arrow.up")
                    }
                    .accessibilityLabel("Share story")
                    .disabled(shareText.isEmpty)
                }
            }
            if detail != nil {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { Task { await loadStory() } } label: {
                        Label("Refresh", systemImage: "arrow.clockwise")
                    }
                    .keyboardShortcut("r", modifiers: .command)
                }
            }
            if detail != nil {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        showingSummary = true
                    } label: {
                        Image(systemName: "text.quote")
                    }
                    .accessibilityLabel("Summary")
                }
            }
        }
        .safeAreaInset(edge: .bottom) {
            if player.isVisible {
                NarrationBar(player: player)
            }
        }
        .sheet(isPresented: $showingSummary) {
            StorySummarySheet(storyId: storyId)
        }
        .task {
            await loadStory()
            await loadNarrationAvailability()
        }
        .refreshable { await loadStory() }
        .onDisappear { player.stop() }
    }

    /// The current version's prose (response messages), for ShareLink/export.
    private var shareText: String {
        guard let versionDetail else { return "" }
        return versionDetail.messages
            .filter { $0.role == .response }
            .map(\.content)
            .joined(separator: "\n\n")
    }

    private func select(version: Int) {
        guard version != selectedVersion else { return }
        player.stop()
        selectedVersion = version
        Task { await loadVersion(version) }
    }

    private func loadStory() async {
        isLoadingStory = true
        storyError = nil
        do {
            let storyDetail = try await FabulisAPIClient.shared.story(id: storyId)
            detail = storyDetail
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
            guard version == selectedVersion else { return }
            versionDetail = result
        } catch {
            guard version == selectedVersion else { return }
            versionError = error.localizedDescription
        }
        guard version == selectedVersion else { return }
        isLoadingVersion = false
    }

    private func loadNarrationAvailability() async {
        if let s = try? await FabulisAPIClient.shared.settings() {
            narrationAvailable = s.narrationAvailable
        }
    }

    private func startNarration(from bubbleId: Int) {
        guard let versionDetail else { return }
        let responses = versionDetail.messages
            .filter { $0.role == .response }
            .map { (id: $0.id, text: $0.content) }
        player.start(bubbles: responses, from: bubbleId, title: detail?.title ?? fallbackTitle)
    }
}
