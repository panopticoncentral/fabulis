import SwiftUI

/// One row in the category list: name plus a story count.
struct CategoryRow: View {
    let category: CategorySummary

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(category.name).font(.body)
            Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                .font(.caption2).foregroundStyle(.secondary)
        }
    }
}
