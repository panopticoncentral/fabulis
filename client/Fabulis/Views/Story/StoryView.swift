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
        }
        .safeAreaInset(edge: .bottom) {
            if player.isVisible {
                NarrationBar(player: player)
            }
        }
        .task {
            await loadStory()
            await loadNarrationAvailability()
        }
        .refreshable { await loadStory() }
        .onDisappear { player.stop() }
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

private extension View {
    /// Shows the model name beneath the navigation title.
    ///
    /// On iPhone we use the native `navigationSubtitle` (iOS 26+), which renders
    /// under the nav-bar title. On Mac Catalyst that modifier only feeds the window
    /// title bar, which isn't visible here, so we pin a subtitle bar under the nav bar.
    @ViewBuilder
    func modelSubtitle(_ subtitle: String?) -> some View {
        let model = (subtitle?.isEmpty == false) ? subtitle : nil
        #if targetEnvironment(macCatalyst)
        // Pin the model strip under the nav bar. The title must use inline display
        // mode here: a large title renders in the same top region and the pinned
        // bar would draw over it (you'd briefly see the title, then only the model).
        Group {
            if let model {
                safeAreaInset(edge: .top, spacing: 0) {
                    VStack(spacing: 0) {
                        Text(model)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal)
                            .padding(.vertical, 6)
                        Divider()
                    }
                    .background(.bar)
                }
            } else {
                self
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        #else
        if #available(iOS 26.0, *), let model {
            navigationSubtitle(model)
        } else {
            self
        }
        #endif
    }
}
