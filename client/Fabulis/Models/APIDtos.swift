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
    let promptCount: Int
    let latestPromptTitle: String?
    let oneLinerCount: Int
    let latestOneLinerText: String?
    let tropeCount: Int
    let latestTropeText: String?
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

struct StorySummaryDetail: Decodable, Sendable {
    let text: String?
    let status: String          // "none" | "generating" | "ready" | "failed"
    let summarizedThroughVersion: Int?
    let latestVersion: Int
    let isStale: Bool
    let updatedAt: Date?
    let error: String?
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

// MARK: - Prompts

struct PromptSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String
    let createdAt: Date
    let messageCount: Int
}

struct PromptCategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let prompts: [PromptSummary]
}

struct PromptDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let title: String
    let createdAt: Date
    let updatedAt: Date
    let messages: [PromptMessage]
}

struct PromptMessage: Decodable, Identifiable, Sendable {
    let id: Int
    let content: String
    let sortOrder: Int
}

struct CreatePromptRequest: Encodable, Sendable {
    let categoryId: Int
    let title: String?
}

struct UpdatePromptRequest: Encodable, Sendable {
    let title: String
    let categoryId: Int
    let messages: [String]
}

// MARK: - One-liners

struct OneLinerSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let text: String
    let createdAt: Date
}

struct OneLinerCategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let oneLiners: [OneLinerSummary]
}

struct OneLinerDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let text: String
    let createdAt: Date
    let updatedAt: Date
}

struct CreateOneLinerRequest: Encodable, Sendable {
    let categoryId: Int
    let text: String
}

struct UpdateOneLinerRequest: Encodable, Sendable {
    let text: String
    let categoryId: Int
}

// MARK: - Tropes

struct TropeSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let text: String
    let createdAt: Date
}

struct TropeCategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let tropes: [TropeSummary]
}

struct TropeDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let text: String
    let createdAt: Date
    let updatedAt: Date
}

struct CreateTropeRequest: Encodable, Sendable {
    let categoryId: Int
    let text: String
}

struct UpdateTropeRequest: Encodable, Sendable {
    let text: String
    let categoryId: Int
}

struct SettingsDto: Decodable, Sendable {
    let apiKeyIsSet: Bool
    let assistantModel: String?
    let autoLockSelection: String
    let kokoroBaseUrlIsSet: Bool
    let narrationVoice: String?
    let narrationSpeed: Double
    let narrationAvailable: Bool
    let summaryModel: String?
    let summaryPrompt: String
}

struct NarrationVoice: Decodable, Identifiable, Sendable {
    let id: String
    let displayName: String
    let language: String
}

struct VoicesResponse: Decodable, Sendable {
    let voices: [NarrationVoice]
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
    var messages: [DraftMessageDto]
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
    let titlingPrompt: String
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
    let titlingPrompt: String
    let modelName: String
    let temperature: Double
    let topP: Double?
    let maxTokens: Int?
    let minP: Double?
    let topK: Int?
    let topA: Double?
}

struct GenerateTitleResponse: Decodable, Sendable {
    let title: String
}

struct CreateCategoryRequest: Encodable, Sendable { let name: String }
struct RenameCategoryRequest: Encodable, Sendable { let name: String }
struct UpdateMessageRequest: Encodable, Sendable { let content: String }

struct SettingsUpdateRequest: Encodable, Sendable {
    let apiKey: String?
    let assistantModel: String?
    let autoLockSelection: String?
}
