import Foundation

// MARK: - Models List Response

struct OpenRouterModelsResponse: Codable {
    let data: [OpenRouterModel]
}

struct OpenRouterModel: Codable, Identifiable, Hashable {
    let id: String
    let name: String
    let description: String?
    let contextLength: Int
    let pricing: OpenRouterPricing
    let architecture: OpenRouterArchitecture?

    enum CodingKeys: String, CodingKey {
        case id, name, description, pricing, architecture
        case contextLength = "context_length"
    }

    var displayName: String {
        name.isEmpty ? id : name
    }

    var providerName: String {
        id.components(separatedBy: "/").first ?? "Unknown"
    }

    var modelName: String {
        id.components(separatedBy: "/").dropFirst().joined(separator: "/")
    }
}

struct OpenRouterPricing: Codable, Hashable {
    let prompt: String
    let completion: String

    var promptCostPerMillion: Double {
        (Double(prompt) ?? 0) * 1_000_000
    }

    var completionCostPerMillion: Double {
        (Double(completion) ?? 0) * 1_000_000
    }
}

struct OpenRouterArchitecture: Codable, Hashable {
    let tokenizer: String?
    let instructionType: String?
    let modalities: Modalities?

    struct Modalities: Codable, Hashable {
        let input: [String]?
        let output: [String]?
    }

    enum CodingKeys: String, CodingKey {
        case tokenizer
        case instructionType = "instruction_type"
        case modalities
    }
}

// MARK: - Chat Completion Request/Response

struct ChatCompletionRequest: Codable {
    let model: String
    let messages: [ChatMessage]
    let stream: Bool
    let temperature: Double?
    let maxTokens: Int?

    enum CodingKeys: String, CodingKey {
        case model, messages, stream, temperature
        case maxTokens = "max_tokens"
    }
}

struct ChatMessage: Codable {
    let role: String
    let content: String
}

struct ChatCompletionResponse: Codable {
    let id: String
    let choices: [ChatChoice]
    let model: String
    let usage: ChatUsage?
}

struct ChatChoice: Codable {
    let index: Int
    let message: ChatResponseMessage?
    let delta: ChatResponseMessage?
    let finishReason: String?

    enum CodingKeys: String, CodingKey {
        case index, message, delta
        case finishReason = "finish_reason"
    }
}

struct ChatResponseMessage: Codable {
    let role: String?
    let content: String?
}

struct ChatUsage: Codable {
    let promptTokens: Int
    let completionTokens: Int
    let totalTokens: Int

    enum CodingKeys: String, CodingKey {
        case promptTokens = "prompt_tokens"
        case completionTokens = "completion_tokens"
        case totalTokens = "total_tokens"
    }
}

// MARK: - Error Response

struct OpenRouterError: Codable {
    let error: OpenRouterErrorDetail
}

struct OpenRouterErrorDetail: Codable {
    let message: String
    let code: Int?
}
