import MarkdownUI
import SwiftUI

struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let isCurrentlyPlaying: Bool
    let isEditing: Bool
    let isDimmed: Bool
    var status = "Generating…"
    let menu: () -> Menu

    init(
        message: DraftMessageDto,
        isCurrentlyPlaying: Bool = false,
        isEditing: Bool = false,
        isDimmed: Bool = false,
        @ViewBuilder menu: @escaping () -> Menu
    ) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
        self.isCurrentlyPlaying = isCurrentlyPlaying
        self.isEditing = isEditing
        self.isDimmed = isDimmed
        self.menu = menu
    }

    init(streamingResponse content: String, status: String = "Generating…", @ViewBuilder menu: @escaping () -> Menu) {
        self.status = status
        self.role = .response
        self.content = content
        self.isStreaming = true
        self.isCurrentlyPlaying = false
        self.isEditing = false
        self.isDimmed = false
        self.menu = menu
    }

    private var roleLabel: String {
        switch role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(roleLabel.uppercased())
                    .font(.caption.bold())
                    .foregroundStyle(role == .response ? Color.accentColor : .secondary)
                if isStreaming {
                    ProgressView().controlSize(.mini)
                    Text(status).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                if Menu.self != EmptyView.self {
                    SwiftUI.Menu(content: menu) {
                        Image(systemName: "ellipsis").touchTarget()
                    }
                    .accessibilityLabel("Message actions")
                    .help("Edit, listen, or regenerate this message")
                }
            }
            Markdown(content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .messageSurface(role: role, highlighted: isEditing || isCurrentlyPlaying, dimmed: isDimmed)
        .accessibilityValue(isEditing ? "Editing" : (isCurrentlyPlaying ? "Currently playing" : ""))
        .contextMenu { menu() }
    }
}

extension DraftMessageView where Menu == EmptyView {
    init(
        message: DraftMessageDto,
        isCurrentlyPlaying: Bool = false,
        isEditing: Bool = false,
        isDimmed: Bool = false
    ) {
        self.init(
            message: message,
            isCurrentlyPlaying: isCurrentlyPlaying,
            isEditing: isEditing,
            isDimmed: isDimmed,
            menu: { EmptyView() })
    }
    init(streamingResponse content: String, status: String = "Generating…") {
        self.init(streamingResponse: content, status: status, menu: { EmptyView() })
    }
}
