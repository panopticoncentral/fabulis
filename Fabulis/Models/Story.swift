import Foundation
import SwiftData

@Model
final class Story {
    var id: UUID = UUID()
    var title: String = ""
    var createdAt: Date = Date()
    var updatedAt: Date = Date()

    var modelIdSnapshot: String = ""

    var storyteller: Storyteller?

    @Relationship(deleteRule: .cascade, inverse: \StorySegment.story)
    var segments: [StorySegment]?

    var orderedSegments: [StorySegment] {
        (segments ?? []).sorted { $0.orderIndex < $1.orderIndex }
    }

    var initialPrompt: String {
        orderedSegments.first(where: { $0.role == .user })?.content ?? ""
    }

    init(title: String, storyteller: Storyteller, modelIdSnapshot: String) {
        self.id = UUID()
        self.title = title
        self.storyteller = storyteller
        self.modelIdSnapshot = modelIdSnapshot
        self.createdAt = Date()
        self.updatedAt = Date()
    }
}
