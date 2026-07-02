import Foundation

/// A switchable category of library content. The single extensibility point
/// for the library kind-switcher: add a `case` (and its detail view) to grow.
enum LibraryKind: String, CaseIterable, Identifiable {
    case prompts
    case oneLiners
    case tropes
    case drafts
    case stories

    var id: String { rawValue }

    var label: String {
        switch self {
        case .drafts: "Drafts"
        case .stories: "Stories"
        case .prompts: "Prompts"
        case .oneLiners: "One-liners"
        case .tropes: "Tropes"
        }
    }

    /// Whether this kind organizes its items under the shared category
    /// taxonomy. Drafts are a flat list; stories, prompts, one-liners, and
    /// tropes are grouped by category.
    var hasCategories: Bool {
        switch self {
        case .drafts: false
        case .stories: true
        case .prompts: true
        case .oneLiners: true
        case .tropes: true
        }
    }
}
