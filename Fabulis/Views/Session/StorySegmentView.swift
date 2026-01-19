import SwiftUI

struct StorySegmentView: View {
    let content: String
    let role: SegmentRole
    var isStreaming: Bool = false

    init(segment: StorySegment) {
        self.content = segment.content
        self.role = segment.role
        self.isStreaming = false
    }

    init(content: String, role: SegmentRole, isStreaming: Bool = false) {
        self.content = content
        self.role = role
        self.isStreaming = isStreaming
    }

    var body: some View {
        HStack(alignment: .top) {
            if role == .user {
                Spacer(minLength: 60)
            }

            VStack(alignment: role == .user ? .trailing : .leading, spacing: 4) {
                Text(content)
                    .textSelection(.enabled)

                if isStreaming {
                    HStack(spacing: 4) {
                        Circle()
                            .frame(width: 4, height: 4)
                        Circle()
                            .frame(width: 4, height: 4)
                        Circle()
                            .frame(width: 4, height: 4)
                    }
                    .foregroundStyle(.secondary)
                    .opacity(0.6)
                }
            }
            .padding()
            .background(role == .user ? Color.accentColor.opacity(0.15) : Color(.systemGray6))
            .clipShape(RoundedRectangle(cornerRadius: 16))

            if role == .assistant {
                Spacer(minLength: 60)
            }
        }
    }
}

#Preview {
    VStack(spacing: 16) {
        StorySegmentView(
            content: "Tell me a story about a brave knight",
            role: .user
        )

        StorySegmentView(
            content: "Once upon a time, in a kingdom far away, there lived a brave knight named Sir Edmund...",
            role: .assistant
        )

        StorySegmentView(
            content: "The knight drew his sword and...",
            role: .assistant,
            isStreaming: true
        )
    }
    .padding()
}
