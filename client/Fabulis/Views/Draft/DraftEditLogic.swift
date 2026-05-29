import Foundation

/// Pure helpers for the inline draft-editing composer. Kept free of SwiftUI
/// and I/O so the banner copy and dimming rules can be unit-tested directly.
enum DraftEditLogic {
    /// Number of messages that sort after the message with `editingId`.
    /// Returns 0 when the id is not present.
    static func messagesAfter(_ messages: [DraftMessageDto], editingId: Int) -> Int {
        guard let idx = messages.firstIndex(where: { $0.id == editingId }) else { return 0 }
        return messages.count - idx - 1
    }

    /// Context-banner copy shown above the composer while editing.
    static func bannerText(role: MessageRole, messagesAfter: Int) -> String {
        switch role {
        case .response:
            return "Editing response"
        case .prompt:
            if messagesAfter == 0 { return "Editing prompt" }
            let noun = messagesAfter == 1 ? "message" : "messages"
            return "Editing prompt \u{2014} Resubmit will delete \(messagesAfter) \(noun) after it"
        }
    }

    /// Whether `bubbleId` should be dimmed: true only when a prompt is being
    /// edited and this bubble sorts after the edited prompt (preview of what
    /// Resubmit will remove). Editing a response never dims anything.
    static func isDimmed(
        _ messages: [DraftMessageDto],
        editingId: Int?,
        editingRole: MessageRole?,
        bubbleId: Int
    ) -> Bool {
        guard let editingId, editingRole == .prompt,
              let editIdx = messages.firstIndex(where: { $0.id == editingId }),
              let thisIdx = messages.firstIndex(where: { $0.id == bubbleId })
        else { return false }
        return thisIdx > editIdx
    }
}
