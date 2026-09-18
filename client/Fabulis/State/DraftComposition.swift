import Foundation
import Observation

/// Session-only composition storage. Unsent text never leaves the process.
@Observable
final class DraftComposition {
    var text = ""
    var editingMessage: DraftMessageDto?
    var stashedText: String?
}
