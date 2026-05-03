import SwiftUI

struct DraftMessageView: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool

    init(message: DraftMessageDto) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
    }

    init(streamingResponse content: String) {
        self.role = .response
        self.content = content
        self.isStreaming = true
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
                if isStreaming {
                    ProgressView().controlSize(.mini)
                }
            }
            Text(content).font(.body).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}
