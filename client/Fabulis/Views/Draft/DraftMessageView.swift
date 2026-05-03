import SwiftUI

struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let menu: () -> Menu

    init(message: DraftMessageDto, @ViewBuilder menu: @escaping () -> Menu) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
        self.menu = menu
    }

    init(streamingResponse content: String, @ViewBuilder menu: @escaping () -> Menu) {
        self.role = .response
        self.content = content
        self.isStreaming = true
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
            Text(content).font(.body).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .contextMenu { menu() }
    }
}

extension DraftMessageView where Menu == EmptyView {
    init(message: DraftMessageDto) {
        self.init(message: message, menu: { EmptyView() })
    }
    init(streamingResponse content: String) {
        self.init(streamingResponse: content, menu: { EmptyView() })
    }
}
