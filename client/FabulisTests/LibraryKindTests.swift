import Testing
@testable import Fabulis

struct LibraryKindTests {
    @Test func labelsAreHumanReadable() {
        #expect(LibraryKind.drafts.label == "Drafts")
        #expect(LibraryKind.stories.label == "Stories")
    }

    @Test func draftsHaveNoCategories() {
        #expect(LibraryKind.drafts.hasCategories == false)
    }

    @Test func storiesHaveCategories() {
        #expect(LibraryKind.stories.hasCategories == true)
    }

    @Test func allCasesInSwitcherOrder() {
        #expect(LibraryKind.allCases == [.prompts, .oneLiners, .tropes, .drafts, .stories])
    }

    @Test func promptsHaveCategories() {
        #expect(LibraryKind.prompts.hasCategories == true)
    }
}
