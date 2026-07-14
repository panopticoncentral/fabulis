import SwiftUI

/// Trope category screen. A thin wrapper over `TextItemCategoryView` that
/// supplies the trope-specific strings, API calls, and edit sheet.
struct TropeCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the trope count changes so the Library sidebar can refresh
    /// this category's count.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    var body: some View {
        TextItemCategoryView(
            categoryId: categoryId,
            categoryName: categoryName,
            config: TextItemConfig(
                newFieldPrompt: "New trope",
                searchPrompt: "Filter tropes",
                emptyIcon: "theatermasks",
                emptyTitle: "No tropes",
                emptyHint: "Type a fragment above and press Return to add one.",
                loadErrorTitle: "Couldn't load tropes",
                deleteItemTitle: "Delete trope?",
                deleteItemMessage: "This deletes the trope. This cannot be undone.",
                deleteItemContextLabel: "Delete Trope"),
            fetch: { id in
                let detail = try await FabulisAPIClient.shared.categoryTropes(categoryId: id)
                return (detail.name, detail.tropes)
            },
            create: { id, text in
                _ = try await FabulisAPIClient.shared.createTrope(categoryId: id, text: text)
            },
            delete: { id in
                try await FabulisAPIClient.shared.deleteTrope(id: id)
            },
            editSheet: { trope, onChanged in
                TropeEditSheet(trope: trope, categoryId: categoryId, onChanged: onChanged)
            },
            onChanged: onChanged,
            onDeleted: onDeleted)
    }
}
