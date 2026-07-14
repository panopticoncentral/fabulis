import SwiftUI

struct NarrationBar: View {
    let player: NarrationPlayer

    var body: some View {
        if case .idle = player.state, let err = player.lastError {
            HStack(spacing: 8) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                Text(err)
                    .font(.caption)
                    .lineLimit(2)
                Spacer()
                Button { player.dismissError() } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.secondary)
                }
                .accessibilityLabel("Dismiss")
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(.bar)
        } else {
            HStack(spacing: 16) {
                Button { player.seek(by: -10) } label: {
                    Image(systemName: "gobackward.10")
                        .frame(minWidth: 44, minHeight: 44)
                }
                .disabled(isPreparingOrIdle)
                .accessibilityLabel("Skip back 10 seconds")

                Button { player.togglePlayPause() } label: {
                    Group {
                        if case .preparing = player.state {
                            ProgressView().controlSize(.small)
                        } else {
                            Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                        }
                    }
                    .frame(minWidth: 44, minHeight: 44)
                }
                .disabled(isPreparingOrIdle && !isPlayable)
                .accessibilityLabel(isPlaying ? "Pause" : "Play")

                Button { player.seek(by: 10) } label: {
                    Image(systemName: "goforward.10")
                        .frame(minWidth: 44, minHeight: 44)
                }
                .disabled(isPreparingOrIdle)
                .accessibilityLabel("Skip forward 10 seconds")

                Spacer()

                VStack(alignment: .trailing, spacing: 2) {
                    if let idx = player.currentBubbleIndex {
                        Text("Part \(idx) of \(player.totalBubbles)")
                            .font(.caption.weight(.medium))
                    }
                    Text(timeLabel)
                        .font(.caption2.monospacedDigit())
                        .foregroundStyle(.secondary)
                }

                Button { player.stop() } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.secondary)
                }
                .accessibilityLabel("Stop narration")
            }
            .font(.title3)
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(.bar)
        }
    }

    private var isPlaying: Bool {
        if case .playing = player.state { return true }
        return false
    }

    private var isPreparingOrIdle: Bool {
        switch player.state {
        case .preparing, .idle: return true
        default: return false
        }
    }

    private var isPlayable: Bool {
        switch player.state {
        case .playing, .paused: return true
        default: return false
        }
    }

    private func format(_ t: TimeInterval) -> String {
        guard t.isFinite, t >= 0 else { return "0:00" }
        let total = Int(t.rounded())
        return String(format: "%d:%02d", total / 60, total % 60)
    }

    /// While streaming, duration is unknown until enough of the file is
    /// buffered for the asset parser to know. Show just the elapsed time
    /// in that case; once duration becomes known, show "elapsed / total".
    private var timeLabel: String {
        if player.duration > 0 {
            return "\(format(player.currentTime)) / \(format(player.duration))"
        } else {
            return format(player.currentTime)
        }
    }
}
