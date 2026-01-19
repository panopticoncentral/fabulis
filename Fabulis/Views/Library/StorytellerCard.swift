import SwiftUI

struct StorytellerCard: View {
    let storyteller: Storyteller

    var accentColor: Color {
        Color(hex: storyteller.accentColorHex) ?? .accentColor
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Image(systemName: storyteller.iconName)
                    .font(.title)
                    .foregroundStyle(accentColor)

                Spacer()

                if storyteller.isBuiltIn {
                    Image(systemName: "checkmark.seal.fill")
                        .foregroundStyle(.secondary)
                        .font(.caption)
                }
            }

            VStack(alignment: .leading, spacing: 4) {
                Text(storyteller.name)
                    .font(.headline)
                    .lineLimit(1)

                Text(storyteller.subtitle)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }

            Spacer(minLength: 0)

            Text("\((storyteller.stories ?? []).count) stories")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .padding()
        .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading)
        .background(accentColor.opacity(0.1))
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .stroke(accentColor.opacity(0.3), lineWidth: 1)
        )
    }
}

extension Color {
    init?(hex: String) {
        var hexSanitized = hex.trimmingCharacters(in: .whitespacesAndNewlines)
        hexSanitized = hexSanitized.replacingOccurrences(of: "#", with: "")

        var rgb: UInt64 = 0
        guard Scanner(string: hexSanitized).scanHexInt64(&rgb) else { return nil }

        let r = Double((rgb & 0xFF0000) >> 16) / 255.0
        let g = Double((rgb & 0x00FF00) >> 8) / 255.0
        let b = Double(rgb & 0x0000FF) / 255.0

        self.init(red: r, green: g, blue: b)
    }
}

#Preview {
    StorytellerCard(storyteller: Storyteller(
        name: "The Bard",
        subtitle: "Epic fantasy adventures",
        iconName: "book.closed.fill",
        accentColorHex: "#8B4513",
        systemPrompt: "You are a storyteller",
        selectedModelId: "anthropic/claude-sonnet-4",
        isBuiltIn: true
    ))
    .frame(width: 180)
    .padding()
}
