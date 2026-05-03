import Foundation

struct UnlockResponse: Decodable, Sendable {
    let token: String
    let issuedAt: Date
}

struct AuthStatusResponse: Decodable, Sendable {
    let isUnlocked: Bool
    let autoLockMinutes: Int?
}

struct LibraryResponse: Decodable, Sendable {
    let categories: [CategorySummary]
}

struct CategorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let storyCount: Int
    let latestStoryTitle: String?
}

struct CategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let stories: [StorySummary]
}

struct StorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String
    let createdAt: Date
    let versionCount: Int
}

struct StoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let title: String
    let createdAt: Date
    let versions: [StoryVersionSummary]
}

struct StoryVersionSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let versionNumber: Int
    let modelName: String
    let createdAt: Date
}

struct StoryVersionDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let storyId: Int
    let versionNumber: Int
    let modelName: String
    let createdAt: Date
    let messages: [StoryMessage]
}

enum MessageRole: String, Decodable, Sendable {
    case prompt = "Prompt"
    case response = "Response"
}

struct StoryMessage: Decodable, Identifiable, Sendable {
    let id: Int
    let role: MessageRole
    let content: String
    let sortOrder: Int
}

struct SettingsDto: Decodable, Sendable {
    let apiKeyIsSet: Bool
    let assistantModel: String?
    let autoLockSelection: String
}

// MARK: - Drafts

struct DraftSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String?
    let createdAt: Date
    let updatedAt: Date
    let messageCount: Int
}

struct DraftDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String?
    let createdAt: Date
    let updatedAt: Date
    let storytellerName: String
    let modelName: String
    let messages: [DraftMessageDto]
}

struct DraftMessageDto: Decodable, Identifiable, Sendable {
    let id: Int
    let role: MessageRole
    let content: String
    let sortOrder: Int
}

struct StreamEnvelope: Decodable, Sendable {
    let kind: String
    let text: String?
    let reasoning: Bool?
    let messageId: Int?
}

struct SaveDraftRequest: Encodable, Sendable {
    let categoryId: Int?
    let newCategoryName: String?
    let storyId: Int?
    let newStoryTitle: String?
}

struct SaveDraftResponse: Decodable, Sendable {
    let storyId: Int
    let versionId: Int
    let versionNumber: Int
}

// MARK: - Settings, models, storyteller

struct ModelInfo: Decodable, Identifiable, Sendable {
    let id: String
    let name: String
}

struct StorytellerDto: Decodable, Sendable {
    let id: Int
    let name: String
    let prompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct StorytellerUpdateRequest: Encodable, Sendable {
    let name: String
    let prompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct CreateCategoryRequest: Encodable, Sendable { let name: String }
struct RenameCategoryRequest: Encodable, Sendable { let name: String }

struct SettingsUpdateRequest: Encodable, Sendable {
    let apiKey: String?
    let assistantModel: String?
    let autoLockSelection: String?
}
