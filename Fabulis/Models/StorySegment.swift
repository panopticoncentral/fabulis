import Foundation
import SwiftData

enum SegmentRole: String, Codable {
    case system
    case user
    case assistant
}

@Model
final class StorySegment {
    var id: UUID = UUID()
    var content: String = ""
    var role: SegmentRole = SegmentRole.user
    var orderIndex: Int = 0
    var createdAt: Date = Date()

    var tokenCount: Int?

    var story: Story?

    init(content: String, role: SegmentRole, orderIndex: Int, story: Story? = nil) {
        self.id = UUID()
        self.content = content
        self.role = role
        self.orderIndex = orderIndex
        self.story = story
        self.createdAt = Date()
    }
}
