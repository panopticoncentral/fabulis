import SwiftUI

extension View {
    func messageSurface(role: MessageRole, highlighted: Bool, dimmed: Bool = false) -> some View {
        self
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(12)
            .background(role == .response ? Color(.systemBackground) : Color(.secondarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .opacity(dimmed ? 0.65 : 1)
            .overlay {
                RoundedRectangle(cornerRadius: 12)
                    .strokeBorder(highlighted ? Color.accentColor : Color(.separator).opacity(0.3), lineWidth: highlighted ? 2 : 0.5)
            }
    }
}
