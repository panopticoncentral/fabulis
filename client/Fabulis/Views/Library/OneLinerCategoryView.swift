import SwiftUI

/// One-liner category screen. A thin wrapper over `TextItemCategoryView` that
/// supplies the one-liner-specific strings, API calls, and edit sheet.
struct OneLinerCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the one-liner count changes so the Library sidebar can
    /// refresh this category's count.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    var body: some View {
        TextItemCategoryView(
            categoryId: categoryId,
            categoryName: categoryName,
            config: TextItemConfig(
                newFieldPrompt: "New one-liner",
                searchPrompt: "Filter one-liners",
                emptyIcon: "quote.bubble",
                emptyTitle: "No one-liners",
                emptyHint: "Type a line above and press Return to add one.",
                loadErrorTitle: "Couldn't load one-liners",
                deleteItemTitle: "Delete one-liner?",
                deleteItemMessage: "This deletes the one-liner. This cannot be undone.",
                deleteItemContextLabel: "Delete One-liner"),
            fetch: { id in
                let detail = try await FabulisAPIClient.shared.categoryOneLiners(categoryId: id)
                return (detail.name, detail.oneLiners)
            },
            create: { id, text in
                _ = try await FabulisAPIClient.shared.createOneLiner(categoryId: id, text: text)
            },
            delete: { id in
                try await FabulisAPIClient.shared.deleteOneLiner(id: id)
            },
            editSheet: { oneLiner, onChanged in
                OneLinerEditSheet(oneLiner: oneLiner, categoryId: categoryId, onChanged: onChanged)
            },
            onChanged: onChanged,
            onDeleted: onDeleted)
    }
}
