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

    @Test func allCasesOrderedPromptsDraftsStories() {
        #expect(LibraryKind.allCases == [.prompts, .drafts, .stories])
    }

    @Test func promptsHaveCategories() {
        #expect(LibraryKind.prompts.hasCategories == true)
    }
}
