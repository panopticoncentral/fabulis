import MarkdownUI
import SwiftUI

struct StoryMessageView: View {
    let message: StoryMessage
    var isCurrentlyPlaying: Bool = false
    var narrationAvailable: Bool = false
    var onPlayFromHere: (() -> Void)? = nil

    private var roleLabel: String {
        switch message.role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    private var roleColor: Color {
        switch message.role {
        case .prompt: return .secondary
        case .response: return .accentColor
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(roleLabel.uppercased()).font(.caption.bold()).foregroundStyle(roleColor)
                Spacer()
                if narrationAvailable, message.role == .response, let onPlayFromHere {
                    Button("Listen from Here", systemImage: "play.circle", action: onPlayFromHere)
                        .labelStyle(.iconOnly).touchTarget().help("Listen from here")
                }
            }
            Markdown(message.content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .messageSurface(role: message.role, highlighted: isCurrentlyPlaying)
        .accessibilityValue(isCurrentlyPlaying ? "Currently playing" : "")
        .contextMenu {
            if narrationAvailable, message.role == .response, let onPlayFromHere {
                Button { onPlayFromHere() } label: {
                    Label("Play from here", systemImage: "play.fill")
                }
            }
        }
    }
}
