import Foundation
import Testing
@testable import Fabulis

struct UIStateTests {
    @Test @MainActor func unsentTextIsPreservedAndIsolatedBetweenDrafts() {
        let app = AppState()
        app.composition(for: 1).text = "An unfinished thought"
        app.composition(for: 2).text = "Another story"
        #expect(app.composition(for: 1).text == "An unfinished thought")
        #expect(app.composition(for: 2).text == "Another story")
        #expect(app.composition(for: 1) === app.composition(for: 1))
    }

    @Test @MainActor func navigationWaitsForExplicitDiscard() {
        let app = AppState()
        var didNavigate = false
        app.dirtyEditors.insert(UUID())
        app.navigate { didNavigate = true }
        #expect(!didNavigate)
        #expect(app.showingNavigationConfirmation)
        app.discardAndNavigate()
        #expect(didNavigate)
        #expect(!app.hasUnsavedChanges)
        #expect(app.pendingNavigation == nil)
    }

    @Test func invalidSamplingNeverLooksLikeAnUnsetDefault() {
        #expect(SamplingValidation.error(topP: "oops", maxTokens: "", minP: "", topK: "", topA: "") != nil)
        #expect(SamplingValidation.error(topP: "NaN", maxTokens: "", minP: "", topK: "", topA: "") != nil)
        #expect(SamplingValidation.error(topP: "0.5", maxTokens: "2.5", minP: "", topK: "", topA: "") != nil)
        #expect(SamplingValidation.error(topP: "", maxTokens: "", minP: "", topK: "", topA: "") == nil)
        #expect(SamplingValidation.error(topP: " 0.5 ", maxTokens: "4000", minP: "0", topK: "20", topA: "1") == nil)
    }
}
