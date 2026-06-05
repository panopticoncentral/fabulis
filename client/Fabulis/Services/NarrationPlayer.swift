import AVFoundation
import Foundation
import MediaPlayer
import Observation

/// Per-view audio player for a story or draft. For each response bubble:
///   1. Hit POST /narration/prepare with the text → get a one-shot token.
///   2. Build a GET URL with that token and let AVPlayer fetch it natively.
/// AVPlayer streams the chunked MP3 directly over HTTP — playback begins
/// as soon as it has enough frames buffered (typically 1-2s) rather than
/// waiting for the full bubble.
///
/// Bubbles are fed through an AVQueuePlayer: while the current bubble plays
/// we prefetch the next one (token + URL) and enqueue it, so the queue
/// player buffers it ahead of time and advances gaplessly. The expensive
/// Kokoro synthesis happens on the GET /play request, so enqueueing early
/// is what actually warms the next bubble — and the continuous audio keeps
/// the session alive when the screen is locked or the app is backgrounded.
@MainActor
@Observable
final class NarrationPlayer: NSObject {

    enum State: Equatable {
        case idle
        case preparing(bubbleId: Int)
        case playing(bubbleId: Int)
        case paused(bubbleId: Int)
    }

    private(set) var state: State = .idle
    private(set) var currentTime: TimeInterval = 0
    /// 0 when the duration isn't yet known (streaming, parser hasn't
    /// read enough frames). UI should show only currentTime in that case.
    private(set) var duration: TimeInterval = 0
    private(set) var lastError: String?

    var currentBubbleIndex: Int? {
        guard let id = currentBubbleId, let i = bubbles.firstIndex(where: { $0.id == id })
        else { return nil }
        return i + 1
    }
    var totalBubbles: Int { bubbles.count }
    var currentBubbleId: Int? {
        switch state {
        case .idle: return nil
        case .preparing(let id), .playing(let id), .paused(let id): return id
        }
    }

    var isVisible: Bool {
        state != .idle || lastError != nil
    }

    func dismissError() { lastError = nil }

    private var bubbles: [(id: Int, text: String)] = []
    /// Shown as the title on the lock screen / Control Center.
    private var narrationTitle: String = "Narration"
    @ObservationIgnored private var remoteCommandsConfigured = false
    private var player: AVQueuePlayer?
    /// Maps each enqueued AVPlayerItem to the bubble it narrates, so we can
    /// recover the bubble id when the queue advances to a new current item.
    @ObservationIgnored private var itemBubbleId: [ObjectIdentifier: Int] = [:]
    @ObservationIgnored private var currentItemObservation: NSKeyValueObservation?
    @ObservationIgnored private var statusObservation: NSKeyValueObservation?
    /// Bubble ids whose token/URL fetch is in flight, to avoid double-loading.
    @ObservationIgnored private var inFlight: Set<Int> = []
    @ObservationIgnored private var loadTasks: [Task<Void, Never>] = []
    @ObservationIgnored private var timer: Timer?

    deinit {
        timer?.invalidate()
    }

    // MARK: - Public API

    func start(bubbles: [(id: Int, text: String)], from bubbleId: Int, title: String? = nil) {
        stop()
        lastError = nil
        self.bubbles = bubbles
        self.narrationTitle = title?.isEmpty == false ? title! : "Narration"
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        configureAudioSession()
        setupRemoteCommandsIfNeeded()
        beginPlayback(from: bubbleId)
    }

    func togglePlayPause() {
        switch state {
        case .playing(let id):
            player?.pause()
            state = .paused(bubbleId: id)
            stopTimer()
            updateNowPlayingInfo()
        case .paused(let id):
            player?.play()
            state = .playing(bubbleId: id)
            startTimer()
            updateNowPlayingInfo()
        case .idle, .preparing:
            return
        }
    }

    func seek(by delta: TimeInterval) {
        guard let player else { return }
        let now = CMTimeGetSeconds(player.currentTime())
        let upper = duration > 0 ? duration : .greatestFiniteMagnitude
        let target = max(0, min(now + delta, upper))
        player.seek(to: CMTime(seconds: target, preferredTimescale: 600))
        currentTime = target
        updateNowPlayingInfo()
    }

    func jumpTo(bubbleId: Int) {
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        tearDownPlayback()
        beginPlayback(from: bubbleId)
    }

    func stop() {
        tearDownPlayback()
        currentTime = 0
        duration = 0
        state = .idle
        clearNowPlayingInfo()
    }

    // MARK: - Internals

    private func tearDownPlayback() {
        for task in loadTasks { task.cancel() }
        loadTasks.removeAll()
        inFlight.removeAll()
        // Invalidate the currentItem observer before draining the queue so
        // removeAllItems() (currentItem → nil) doesn't re-enter our handler.
        currentItemObservation?.invalidate()
        currentItemObservation = nil
        statusObservation?.invalidate()
        statusObservation = nil
        player?.pause()
        player?.removeAllItems()
        player = nil
        itemBubbleId.removeAll()
        stopTimer()
    }

    private func scheduleErrorClear() {
        Task { @MainActor [weak self] in
            try? await Task.sleep(for: .seconds(3))
            self?.lastError = nil
        }
    }

    private func configureAudioSession() {
        #if canImport(UIKit)
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio)
            try AVAudioSession.sharedInstance().setActive(true)
        } catch {
            // Non-fatal.
        }
        #endif
    }

    // MARK: - Lock screen / remote controls

    /// Wires the Control Center / lock-screen transport buttons once. The
    /// command center is process-global, so adding targets on every start
    /// would stack duplicate handlers.
    private func setupRemoteCommandsIfNeeded() {
        guard !remoteCommandsConfigured else { return }
        remoteCommandsConfigured = true
        let center = MPRemoteCommandCenter.shared()

        center.playCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, case .paused = self.state else {
                    return .commandFailed
                }
                self.togglePlayPause()
                return .success
            }
        }
        center.pauseCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, case .playing = self.state else {
                    return .commandFailed
                }
                self.togglePlayPause()
                return .success
            }
        }
        center.togglePlayPauseCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                self?.togglePlayPause()
                return .success
            }
        }

        center.skipForwardCommand.preferredIntervals = [10]
        center.skipForwardCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                self?.seek(by: 10)
                return .success
            }
        }
        center.skipBackwardCommand.preferredIntervals = [10]
        center.skipBackwardCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                self?.seek(by: -10)
                return .success
            }
        }

        center.nextTrackCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, self.canGoToNext else { return .noSuchContent }
                self.advanceToNext()
                return .success
            }
        }
        center.previousTrackCommand.addTarget { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, self.canGoToPrevious else { return .noSuchContent }
                self.goToPrevious()
                return .success
            }
        }
    }

    private var canGoToNext: Bool {
        guard let id = currentBubbleId,
              let idx = bubbles.firstIndex(where: { $0.id == id }) else { return false }
        return idx + 1 < bubbles.count
    }

    private var canGoToPrevious: Bool {
        guard let id = currentBubbleId,
              let idx = bubbles.firstIndex(where: { $0.id == id }) else { return false }
        return idx > 0
    }

    private func goToPrevious() {
        guard let id = currentBubbleId,
              let idx = bubbles.firstIndex(where: { $0.id == id }),
              idx > 0
        else { return }
        jumpTo(bubbleId: bubbles[idx - 1].id)
    }

    /// Pushes the current title, position, and play/pause rate to the system
    /// so the lock screen and Control Center reflect what's playing.
    private func updateNowPlayingInfo() {
        var info: [String: Any] = [:]
        info[MPMediaItemPropertyTitle] = narrationTitle
        if let idx = currentBubbleIndex {
            info[MPMediaItemPropertyArtist] = "Part \(idx) of \(totalBubbles)"
        }
        info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = currentTime
        if duration > 0 {
            info[MPMediaItemPropertyPlaybackDuration] = duration
        }
        let rate: Double = { if case .playing = state { return 1.0 } else { return 0.0 } }()
        info[MPNowPlayingInfoPropertyPlaybackRate] = rate
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    private func clearNowPlayingInfo() {
        MPNowPlayingInfoCenter.default().nowPlayingInfo = nil
    }

    // MARK: - Queue management

    private func beginPlayback(from bubbleId: Int) {
        let player = AVQueuePlayer()
        player.automaticallyWaitsToMinimizeStalling = true
        self.player = player

        // The queue advances itself when an item finishes; we watch
        // currentItem to learn which bubble is now playing and to drive the
        // state machine + the next prefetch.
        currentItemObservation = player.observe(\.currentItem, options: [.new]) { [weak self] _, _ in
            Task { @MainActor in self?.currentItemChanged() }
        }

        state = .preparing(bubbleId: bubbleId)
        currentTime = 0
        duration = 0
        updateNowPlayingInfo()
        // Inserting into the empty queue makes this the current item, which
        // fires currentItemChanged() → status observation → playback.
        loadAndEnqueue(bubbleId: bubbleId, leading: true)
    }

    /// Fetches a token + URL for `bubbleId` and appends it to the queue.
    /// `leading` marks the item we're waiting on right now (nothing else is
    /// playing) — its failure is surfaced to the user, whereas a prefetch
    /// failure stays silent and is retried if the queue later drains onto it.
    private func loadAndEnqueue(bubbleId: Int, leading: Bool) {
        guard !inFlight.contains(bubbleId),
              !itemBubbleId.values.contains(bubbleId) else { return }
        inFlight.insert(bubbleId)
        let text = bubbles.first(where: { $0.id == bubbleId })?.text ?? ""

        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            defer { self.inFlight.remove(bubbleId) }
            do {
                let token = try await FabulisAPIClient.shared.prepareNarration(
                    text: text, voice: nil, speed: nil)
                try Task.checkCancellation()
                let url = try await FabulisAPIClient.shared.playNarrationURL(token: token)
                try Task.checkCancellation()
                // The bubble may have been dropped, or another path may have
                // enqueued it, while we were awaiting.
                guard self.player != nil,
                      self.bubbles.contains(where: { $0.id == bubbleId }),
                      !self.itemBubbleId.values.contains(bubbleId) else { return }
                let item = AVPlayerItem(asset: AVURLAsset(url: url))
                self.itemBubbleId[ObjectIdentifier(item)] = bubbleId
                self.player?.insert(item, after: nil)
            } catch is CancellationError {
                // Torn down or jumped away; nothing to surface.
            } catch {
                if leading {
                    self.lastError = error.localizedDescription
                    self.stop()
                    self.scheduleErrorClear()
                }
            }
        }
        loadTasks.append(task)
    }

    /// Called whenever the queue player's current item changes — on initial
    /// load, on auto-advance to a prefetched bubble, and when the queue
    /// drains at the end.
    private func currentItemChanged() {
        statusObservation?.invalidate()
        statusObservation = nil
        stopTimer()

        guard let player else { return }
        let previousId = currentBubbleId

        guard let item = player.currentItem else {
            // Queue drained. Either we finished the last bubble, or the next
            // bubble's prefetch hasn't landed yet (lost the race / failed).
            if let previousId, let next = bubbleAfter(previousId) {
                state = .preparing(bubbleId: next)
                currentTime = 0
                duration = 0
                updateNowPlayingInfo()
                // If a prefetch is in flight it will insert and become current,
                // firing this handler again; otherwise kick off the load now.
                if !inFlight.contains(next) {
                    loadAndEnqueue(bubbleId: next, leading: true)
                }
            } else {
                stop()
            }
            return
        }

        pruneItemMap()
        guard let bubbleId = itemBubbleId[ObjectIdentifier(item)] else { return }
        currentTime = 0
        duration = 0

        // Promote immediately if the prefetched item is already buffered,
        // otherwise wait on its status. The observer's .initial covers the
        // already-ready case too; the `.preparing` guard avoids double-start.
        state = .preparing(bubbleId: bubbleId)
        updateNowPlayingInfo()
        observeStatus(of: item, bubbleId: bubbleId)

        if let next = bubbleAfter(bubbleId) {
            loadAndEnqueue(bubbleId: next, leading: false)
        }
    }

    private func observeStatus(of item: AVPlayerItem, bubbleId: Int) {
        statusObservation = item.observe(\.status, options: [.initial, .new]) { [weak self] item, _ in
            let status = item.status
            let err = item.error
            Task { @MainActor in
                guard let self, self.player?.currentItem === item else { return }
                switch status {
                case .readyToPlay:
                    if case .preparing = self.state {
                        self.state = .playing(bubbleId: bubbleId)
                        self.player?.play()
                        self.startTimer()
                        self.updateNowPlayingInfo()
                    }
                case .failed:
                    self.lastError = err?.localizedDescription ?? "Playback failed"
                    self.stop()
                    self.scheduleErrorClear()
                case .unknown:
                    break
                @unknown default:
                    break
                }
            }
        }
    }

    /// Drops map entries for items the queue player has already released.
    private func pruneItemMap() {
        guard let player else { itemBubbleId.removeAll(); return }
        let live = Set(player.items().map { ObjectIdentifier($0) })
        itemBubbleId = itemBubbleId.filter { live.contains($0.key) }
    }

    private func bubbleAfter(_ bubbleId: Int) -> Int? {
        guard let idx = bubbles.firstIndex(where: { $0.id == bubbleId }),
              idx + 1 < bubbles.count else { return nil }
        return bubbles[idx + 1].id
    }

    /// Manual skip (lock-screen next button). Rebuilds the queue from the
    /// following bubble rather than waiting for the current one to finish.
    private func advanceToNext() {
        guard let id = currentBubbleId, let next = bubbleAfter(id) else { stop(); return }
        jumpTo(bubbleId: next)
    }

    private func startTimer() {
        stopTimer()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                if case .playing = self.state, let p = self.player, let item = p.currentItem {
                    self.currentTime = CMTimeGetSeconds(p.currentTime())
                    let d = CMTimeGetSeconds(item.duration)
                    if d.isFinite, d > 0, d != self.duration {
                        // Streaming: duration becomes known once enough frames
                        // are buffered. Refresh Now Playing so the lock-screen
                        // scrubber gets a real total.
                        self.duration = d
                        self.updateNowPlayingInfo()
                    }
                }
            }
        }
        if let timer { RunLoop.main.add(timer, forMode: .common) }
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }
}
