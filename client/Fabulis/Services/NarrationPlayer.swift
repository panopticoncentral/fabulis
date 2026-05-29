import AVFoundation
import Foundation
import Observation

/// Per-view audio player for a story or draft. For each response bubble:
///   1. Hit POST /narration/prepare with the text → get a one-shot token.
///   2. Build a GET URL with that token and let AVPlayer fetch it natively.
/// AVPlayer streams the chunked MP3 directly over HTTP — playback begins
/// as soon as it has enough frames buffered (typically 1-2s) rather than
/// waiting for the full bubble.
///
/// One bubble plays at a time. Bubbles auto-advance on
/// AVPlayerItem.didPlayToEndTimeNotification. No prefetch — with
/// streaming, first-audio latency is dominated by Kokoro warmup, not
/// file size, so the previous prefetch slot bought little.
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
    private var player: AVPlayer?
    private var currentItem: AVPlayerItem?
    private var statusObservation: NSKeyValueObservation?
    @ObservationIgnored private var endObserver: NSObjectProtocol?
    private var prepareTask: Task<Void, Never>?
    @ObservationIgnored private var timer: Timer?

    deinit {
        timer?.invalidate()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
    }

    // MARK: - Public API

    func start(bubbles: [(id: Int, text: String)], from bubbleId: Int) {
        stop()
        lastError = nil
        self.bubbles = bubbles
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        configureAudioSession()
        loadAndPlay(bubbleId: bubbleId)
    }

    func togglePlayPause() {
        switch state {
        case .playing(let id):
            player?.pause()
            state = .paused(bubbleId: id)
            stopTimer()
        case .paused(let id):
            player?.play()
            state = .playing(bubbleId: id)
            startTimer()
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
    }

    func jumpTo(bubbleId: Int) {
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        tearDownPlayback()
        loadAndPlay(bubbleId: bubbleId)
    }

    func stop() {
        tearDownPlayback()
        currentTime = 0
        duration = 0
        state = .idle
    }

    // MARK: - Internals

    private func tearDownPlayback() {
        prepareTask?.cancel()
        prepareTask = nil
        player?.pause()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = nil
        statusObservation?.invalidate()
        statusObservation = nil
        currentItem = nil
        player = nil
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

    private func loadAndPlay(bubbleId: Int) {
        state = .preparing(bubbleId: bubbleId)
        let text = bubbles.first(where: { $0.id == bubbleId })?.text ?? ""

        prepareTask = Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let token = try await FabulisAPIClient.shared.prepareNarration(
                    text: text, voice: nil, speed: nil)
                if Task.isCancelled { return }
                guard self.currentBubbleId == bubbleId else { return }
                let url = try await FabulisAPIClient.shared.playNarrationURL(token: token)
                if Task.isCancelled { return }
                guard self.currentBubbleId == bubbleId else { return }
                self.startPlayback(bubbleId: bubbleId, url: url)
            } catch is CancellationError {
                // Cancelled by stop/jump; nothing to surface.
            } catch {
                self.lastError = error.localizedDescription
                self.stop()
                self.scheduleErrorClear()
            }
        }
    }

    private func startPlayback(bubbleId: Int, url: URL) {
        let asset = AVURLAsset(url: url)
        let item = AVPlayerItem(asset: asset)
        let player = AVPlayer(playerItem: item)
        player.automaticallyWaitsToMinimizeStalling = true
        self.currentItem = item
        self.player = player

        statusObservation = item.observe(\.status, options: [.initial, .new]) { [weak self] item, _ in
            let status = item.status
            let err = item.error
            Task { @MainActor in
                guard let self, self.currentItem === item else { return }
                switch status {
                case .readyToPlay:
                    if case .preparing = self.state {
                        self.state = .playing(bubbleId: bubbleId)
                        self.player?.play()
                        self.startTimer()
                    }
                case .failed:
                    let msg = err?.localizedDescription ?? "Playback failed"
                    self.lastError = msg
                    self.stop()
                    self.scheduleErrorClear()
                case .unknown:
                    break
                @unknown default:
                    break
                }
            }
        }

        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime,
            object: item,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in
                self?.advanceToNext()
            }
        }
    }

    private func advanceToNext() {
        guard let id = currentBubbleId,
              let idx = bubbles.firstIndex(where: { $0.id == id }),
              idx + 1 < bubbles.count
        else { stop(); return }
        let next = bubbles[idx + 1]
        tearDownPlayback()
        loadAndPlay(bubbleId: next.id)
    }

    private func startTimer() {
        stopTimer()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                if case .playing = self.state, let p = self.player, let item = self.currentItem {
                    self.currentTime = CMTimeGetSeconds(p.currentTime())
                    let d = CMTimeGetSeconds(item.duration)
                    if d.isFinite, d > 0 {
                        self.duration = d
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
