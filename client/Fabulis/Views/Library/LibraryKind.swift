import Foundation

/// A switchable category of library content. The single extensibility point
/// for the library kind-switcher: add a `case` (and its detail view) to grow.
enum LibraryKind: String, CaseIterable, Identifiable {
    case drafts
    case stories
    // future: case outlines

    var id: String { rawValue }

    var label: String {
        switch self {
        case .drafts: "Drafts"
        case .stories: "Stories"
        }
    }

    /// Whether this kind organizes its items under the shared category
    /// taxonomy. Drafts are a flat list; stories (and future kinds) are
    /// grouped by category.
    var hasCategories: Bool {
        switch self {
        case .drafts: false
        case .stories: true
        }
    }
}
