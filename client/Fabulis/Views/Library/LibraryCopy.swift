import Foundation

/// Shared user-facing strings for the library views. Centralized so the
/// destructive-action wording can't drift between the sibling category views
/// (it previously omitted "tropes" in two of them).
enum LibraryCopy {
    /// Categories are a shared taxonomy: deleting one removes every kind of
    /// child item, so the warning must name all of them.
    static func deleteCategoryWarning(_ category: CategorySummary) -> String {
        "Delete “\(category.name)” and all its contents? This includes \(category.storyCount) stories, \(category.promptCount) prompts, \(category.oneLinerCount) one-liners, and \(category.tropeCount) tropes across the library. This cannot be undone."
    }

    static let deleteCategoryWarning =
        "This deletes the category and all its stories, prompts, one-liners, and tropes. This cannot be undone."
}
