import MarkdownUI
import SwiftUI

struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let isCurrentlyPlaying: Bool
    let isEditing: Bool
    let isDimmed: Bool
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

    init(streamingResponse content: String, @ViewBuilder menu: @escaping () -> Menu) {
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
                    .font(.caption2.bold())
                    .foregroundStyle(role == .response ? Color.accentColor : .secondary)
                if isStreaming { ProgressView().controlSize(.mini) }
            }
            Markdown(content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .opacity(isDimmed ? 0.4 : 1)
        .overlay {
            if isEditing || isCurrentlyPlaying {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(Color.accentColor, lineWidth: 2)
            }
        }
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
    init(streamingResponse content: String) {
        self.init(streamingResponse: content, menu: { EmptyView() })
    }
}
