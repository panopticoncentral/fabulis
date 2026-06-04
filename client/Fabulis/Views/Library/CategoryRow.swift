import SwiftUI

/// One row in the category list: name plus a count for the active library kind.
struct CategoryRow: View {
    let category: CategorySummary
    let kind: LibraryKind

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(category.name).font(.body)
            Text(countText)
                .font(.caption2).foregroundStyle(.secondary)
        }
    }

    private var countText: String {
        switch kind {
        case .prompts:
            "\(category.promptCount) \(category.promptCount == 1 ? "prompt" : "prompts")"
        default:
            "\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")"
        }
    }
}
