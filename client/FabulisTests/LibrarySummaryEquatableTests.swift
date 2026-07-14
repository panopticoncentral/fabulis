import Foundation
import Testing
@testable import Fabulis

/// SwiftUI decides whether to re-render a row by comparing the view's stored
/// properties via `Equatable`. If `CategorySummary`/`DraftSummary` compare only
/// by `id`, a reloaded summary with the same id but a changed count is treated
/// as unchanged and the sidebar count goes stale. These tests pin the required
/// value semantics: same id + different contents must be unequal.
struct LibrarySummaryEquatableTests {
    @Test func categorySummaryDiffersWhenPromptCountChanges() {
        let before = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 2, latestPromptTitle: "A",
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        let after = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 3, latestPromptTitle: "A",
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        #expect(before != after)
    }

    @Test func categorySummaryDiffersWhenStoryCountChanges() {
        let before = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 1, latestStoryTitle: "S",
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        let after = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 2, latestStoryTitle: "S",
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        #expect(before != after)
    }

    @Test func categorySummaryDiffersWhenTropeCountChanges() {
        let before = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 1, latestTropeText: "T")
        let after = CategorySummary(
            id: 1, name: "Bedtime", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 2, latestTropeText: "T")
        #expect(before != after)
    }

    @Test func draftSummaryDiffersWhenMessageCountChanges() {
        let before = DraftSummary(
            id: 1, title: "Untitled", createdAt: Date(timeIntervalSince1970: 0),
            updatedAt: Date(timeIntervalSince1970: 0), messageCount: 1)
        let after = DraftSummary(
            id: 1, title: "Untitled", createdAt: Date(timeIntervalSince1970: 0),
            updatedAt: Date(timeIntervalSince1970: 0), messageCount: 2)
        #expect(before != after)
    }

    @Test func equalCategorySummariesShareAHash() {
        let a = CategorySummary(
            id: 7, name: "X", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        let b = CategorySummary(
            id: 7, name: "X", createdAt: Date(timeIntervalSince1970: 0),
            storyCount: 0, latestStoryTitle: nil,
            promptCount: 0, latestPromptTitle: nil,
            oneLinerCount: 0, latestOneLinerText: nil,
            tropeCount: 0, latestTropeText: nil)
        #expect(a == b)
        #expect(a.hashValue == b.hashValue)
    }
}
