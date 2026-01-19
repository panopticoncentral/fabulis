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
        if role == .assistant {
            // Story content - rendered like an eReader
            VStack(alignment: .leading, spacing: 16) {
                ForEach(Array(contentParagraphs.enumerated()), id: \.offset) { _, paragraph in
                    Text(parseMarkdown(paragraph.text))
                        .font(paragraph.isHeading ? nil : .system(.body, design: .serif))
                        .lineSpacing(6)
                        .textSelection(.enabled)
                }

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
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            // User direction - subtle indicator
            HStack(spacing: 8) {
                Image(systemName: "arrow.turn.down.right")
                    .font(.caption)
                    .foregroundStyle(.secondary)

                Text(content)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .italic()
                    .textSelection(.enabled)
            }
            .padding(.vertical, 8)
            .padding(.horizontal, 12)
            .background(Color(.systemGray6).opacity(0.5))
            .clipShape(RoundedRectangle(cornerRadius: 8))
        }
    }

    private struct Paragraph {
        let text: String
        let isHeading: Bool
    }

    private var contentParagraphs: [Paragraph] {
        content
            .components(separatedBy: "\n\n")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .map { Paragraph(text: $0, isHeading: $0.hasPrefix("#")) }
    }

    private func parseMarkdown(_ text: String) -> AttributedString {
        do {
            var attributed = try AttributedString(markdown: text, options: AttributedString.MarkdownParsingOptions(interpretedSyntax: .full))

            // Apply heading styles based on presentation intent
            for run in attributed.runs {
                guard let intentBlock = run.presentationIntent else { continue }

                for intent in intentBlock.components {
                    let range = run.range
                    switch intent.kind {
                    case .header(let level):
                        switch level {
                        case 1:
                            attributed[range].font = .system(.title, design: .serif).bold()
                        case 2:
                            attributed[range].font = .system(.title2, design: .serif).bold()
                        case 3:
                            attributed[range].font = .system(.title3, design: .serif).bold()
                        default:
                            attributed[range].font = .system(.headline, design: .serif).bold()
                        }
                    default:
                        break
                    }
                }
            }

            return attributed
        } catch {
            return AttributedString(text)
        }
    }
}

#Preview {
    ScrollView {
        VStack(alignment: .leading, spacing: 20) {
            StorySegmentView(
                content: "Tell me a story about a brave knight",
                role: .user
            )

            StorySegmentView(
                content: """
                # Chapter One: The Call

                Once upon a time, in a kingdom far away, there lived a brave knight named **Sir Edmund the Bold**. He was known throughout the land for his *unwavering courage* and his legendary sword, _Starfire_.

                ## The Shadow Falls

                The kingdom had prospered under his protection for many years, but dark clouds were gathering on the horizon. A shadow had fallen upon the eastern mountains, and whispers spoke of an ancient evil awakening.

                ### The Wizard's Warning

                "The time has come," the old wizard told him, his voice grave. "You must journey to the *Caverns of Despair* and face what lies within."
                """,
                role: .assistant
            )

            StorySegmentView(
                content: "Have him meet a mysterious stranger on the road",
                role: .user
            )

            StorySegmentView(
                content: "The knight drew his sword and...",
                role: .assistant,
                isStreaming: true
            )
        }
        .padding()
    }
}
