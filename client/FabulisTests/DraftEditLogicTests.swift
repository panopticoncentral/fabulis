import Testing
@testable import Fabulis

struct DraftEditLogicTests {
    private func msgs() -> [DraftMessageDto] {
        [
            DraftMessageDto(id: 1, role: .prompt, content: "p1", sortOrder: 0),
            DraftMessageDto(id: 2, role: .response, content: "r1", sortOrder: 1),
            DraftMessageDto(id: 3, role: .prompt, content: "p2", sortOrder: 2),
            DraftMessageDto(id: 4, role: .response, content: "r2", sortOrder: 3),
        ]
    }

    @Test func messagesAfterCountsFollowingBubbles() {
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 1) == 3)
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 3) == 1)
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 4) == 0)
    }

    @Test func messagesAfterReturnsZeroForUnknownId() {
        #expect(DraftEditLogic.messagesAfter(msgs(), editingId: 999) == 0)
    }

    @Test func bannerTextForResponse() {
        #expect(DraftEditLogic.bannerText(role: .response, messagesAfter: 3)
                == "Editing response")
    }

    @Test func bannerTextForPromptWithNoFollowers() {
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 0)
                == "Editing prompt")
    }

    @Test func bannerTextForPromptPluralizes() {
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 1)
                == "Editing prompt \u{2014} Resubmit will delete 1 message after it")
        #expect(DraftEditLogic.bannerText(role: .prompt, messagesAfter: 3)
                == "Editing prompt \u{2014} Resubmit will delete 3 messages after it")
    }

    @Test func dimmedOnlyForBubblesAfterAnEditedPrompt() {
        let m = msgs()
        // Editing prompt id 1: bubbles 2,3,4 dimmed, 1 not.
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 1) == false)
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 2) == true)
        #expect(DraftEditLogic.isDimmed(m, editingId: 1, editingRole: .prompt, bubbleId: 4) == true)
    }

    @Test func dimmedFalseWhenEditingResponse() {
        let m = msgs()
        #expect(DraftEditLogic.isDimmed(m, editingId: 2, editingRole: .response, bubbleId: 3) == false)
    }

    @Test func dimmedFalseWhenNotEditing() {
        let m = msgs()
        #expect(DraftEditLogic.isDimmed(m, editingId: nil, editingRole: nil, bubbleId: 3) == false)
    }
}
