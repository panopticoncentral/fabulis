import Foundation
import SwiftData

@Model
final class Storyteller {
    var id: UUID = UUID()
    var name: String = ""
    var subtitle: String = ""
    var iconName: String = ""
    var accentColorHex: String = ""

    var systemPrompt: String = ""
    var selectedModelId: String = ""

    var isBuiltIn: Bool = false
    var createdAt: Date = Date()
    var updatedAt: Date = Date()

    @Relationship(deleteRule: .cascade, inverse: \Story.storyteller)
    var stories: [Story]?

    init(
        name: String,
        subtitle: String,
        iconName: String,
        accentColorHex: String,
        systemPrompt: String,
        selectedModelId: String,
        isBuiltIn: Bool = false
    ) {
        self.id = UUID()
        self.name = name
        self.subtitle = subtitle
        self.iconName = iconName
        self.accentColorHex = accentColorHex
        self.systemPrompt = systemPrompt
        self.selectedModelId = selectedModelId
        self.isBuiltIn = isBuiltIn
        self.createdAt = Date()
        self.updatedAt = Date()
    }
}
