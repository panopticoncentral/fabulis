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
            Text(roleLabel.uppercased())
                .font(.caption2.bold())
                .foregroundStyle(roleColor)
            Markdown(message.content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(message.role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay {
            if isCurrentlyPlaying {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(Color.accentColor, lineWidth: 2)
            }
        }
        .contextMenu {
            if narrationAvailable, message.role == .response, let onPlayFromHere {
                Button { onPlayFromHere() } label: {
                    Label("Play from here", systemImage: "play.fill")
                }
            }
        }
    }
}
