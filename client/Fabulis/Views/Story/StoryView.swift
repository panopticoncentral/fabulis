import SwiftUI
import MarkdownUI

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
    @Environment(AppState.self) private var appState
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @AppStorage("readingConversation", store: ReadingPreferences.store) private var showConversation = false
    @AppStorage("readingTextSize", store: ReadingPreferences.store) private var readingTextSize = 18.0
    @State private var actionError: String?
    private var player: NarrationPlayer { appState.narration }
    private var narrationSource: String { "story:\(storyId):\(selectedVersion ?? 0)" }
    private var playingBubbleId: Int? {
        appState.narrationSource == narrationSource ? player.currentBubbleId : nil
    }
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
                            LazyVStack(alignment: .leading, spacing: 20) {
                                VStack(alignment: .leading, spacing: 8) {
                                    if !showConversation {
                                        Text(detail.title).font(.largeTitle.weight(.semibold)).accessibilityAddTraits(.isHeader)
                                    }
                                    Text("Version \(selectedVersion ?? 1) · \(detail.categoryName)")
                                        .font(.subheadline).foregroundStyle(.secondary)
                                    if let versionSource {
                                        Text(versionSource)
                                            .font(.caption).foregroundStyle(.secondary)
                                            .fixedSize(horizontal: false, vertical: true)
                                    }
                                }
                                .padding(.bottom, showConversation ? 0 : 12)
                                ForEach(versionDetail.messages.filter { showConversation || $0.role == .response }) { message in
                                    if showConversation {
                                    StoryMessageView(
                                        message: message,
                                        isCurrentlyPlaying: playingBubbleId == message.id,
                                        narrationAvailable: narrationAvailable,
                                        onPlayFromHere: { startNarration(from: message.id) })
                                        .id(message.id)
                                    } else {
                                        Markdown(message.content)
                                            .markdownTextStyle { FontSize(readingTextSize) }
                                            .textSelection(.enabled)
                                            .padding(.vertical, 8)
                                            .frame(maxWidth: .infinity, alignment: .leading)
                                            .background(playingBubbleId == message.id ? Color.accentColor.opacity(0.08) : .clear)
                                            .accessibilityValue(playingBubbleId == message.id ? "Currently playing" : "")
                                            .contextMenu {
                                                if narrationAvailable {
                                                    Button("Listen from Here", systemImage: "play.fill") { startNarration(from: message.id) }
                                                }
                                            }
                                            .id(message.id)
                                    }
                                }
                            }
                            .padding()
                            // Cap the reading column and center it so long-form
                            // prose doesn't stretch edge-to-edge on a wide window.
                            .frame(maxWidth: 720)
                            .frame(maxWidth: .infinity)
                        }
                        .onChange(of: playingBubbleId) { _, new in
                            if let new {
                                withAnimation(reduceMotion ? nil : .default) { proxy.scrollTo(new, anchor: .center) }
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
        .navigationBarTitleDisplayMode(.inline)
        .focusedSceneValue(\.contentActions, ContentActions(
            refresh: { Task { await loadStory() } },
            listen: narrationAvailable && versionDetail?.messages.contains(where: { $0.role == .response }) == true ? {
                if let first = versionDetail?.messages.first(where: { $0.role == .response }) { startNarration(from: first.id) }
            } : nil,
            summary: detail != nil ? { showingSummary = true } : nil))
        .toolbar {
            if versionDetail != nil {
                ToolbarItem(placement: .topBarTrailing) {
                    ShareLink(item: shareText) {
                        Image(systemName: "square.and.arrow.up")
                    }
                    .accessibilityLabel("Share story")
                    .disabled(shareText.isEmpty)
                }
            }
            if narrationAvailable, let first = versionDetail?.messages.first(where: { $0.role == .response }) {
                ToolbarItem(placement: .primaryAction) {
                    Button("Listen", systemImage: "play.circle") { startNarration(from: first.id) }
                        .help("Listen to this story")
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    if let detail, let selectedVersion {
                        Picker("Version", selection: Binding(get: { selectedVersion }, set: select)) {
                            ForEach(detail.versions) { version in
                                Text("Version \(version.versionNumber)").tag(version.versionNumber)
                            }
                        }
                    }
                    Picker("Presentation", selection: $showConversation) {
                        Text("Read").tag(false)
                        Text("Conversation").tag(true)
                    }
                    if !showConversation {
                        ControlGroup {
                            Button("Smaller Text", systemImage: "textformat.size.smaller") { readingTextSize = max(14, readingTextSize - 2) }
                            Button("Larger Text", systemImage: "textformat.size.larger") { readingTextSize = min(32, readingTextSize + 2) }
                        }
                    }
                    Divider()
                    Button("Summary", systemImage: "text.quote") { showingSummary = true }
                        .disabled(detail == nil)
                    Button("Refresh", systemImage: "arrow.clockwise") { Task { await loadStory() } }
                } label: { Label("Reading Options", systemImage: "ellipsis.circle") }
                .accessibilityIdentifier("reading-options")
                .help("Reading options and story summary")
            }
        }
        .actionErrorAlert($actionError, title: "Couldn't refresh story")
        .sheet(isPresented: $showingSummary) {
            StorySummarySheet(storyId: storyId)
        }
        .task {
            await loadStory()
            await loadNarrationAvailability()
        }
        .refreshable { await loadStory() }
    }

    private var versionSource: String? {
        guard let versionDetail, !versionDetail.modelName.isEmpty else { return nil }
        return "Model: \(versionDetail.modelName)"
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
        if appState.narrationSource == narrationSource { player.stop() }
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
            if detail == nil { storyError = error.localizedDescription }
            else { actionError = error.localizedDescription }
        }
        isLoadingStory = false
    }

    private func loadVersion(_ version: Int) async {
        isLoadingVersion = true
        versionError = nil
        let refreshingCurrentVersion = versionDetail?.versionNumber == version
        if !refreshingCurrentVersion { versionDetail = nil }
        do {
            let result = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
            guard version == selectedVersion else { return }
            versionDetail = result
        } catch {
            guard version == selectedVersion else { return }
            if refreshingCurrentVersion { actionError = error.localizedDescription }
            else { versionError = error.localizedDescription }
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
        appState.narrationSource = narrationSource
        player.start(bubbles: responses, from: bubbleId, title: detail?.title ?? fallbackTitle)
    }
}
