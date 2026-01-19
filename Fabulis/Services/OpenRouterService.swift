import Foundation

enum OpenRouterServiceError: LocalizedError {
    case noAPIKey
    case invalidURL
    case invalidResponse
    case httpError(Int, String)
    case decodingError(Error)
    case networkError(Error)

    var errorDescription: String? {
        switch self {
        case .noAPIKey:
            return "No API key configured. Please add your OpenRouter API key in settings."
        case .invalidURL:
            return "Invalid API URL."
        case .invalidResponse:
            return "Invalid response from server."
        case .httpError(let code, let message):
            return "API Error (\(code)): \(message)"
        case .decodingError(let error):
            return "Failed to decode response: \(error.localizedDescription)"
        case .networkError(let error):
            return "Network error: \(error.localizedDescription)"
        }
    }
}

actor OpenRouterService {
    static let shared = OpenRouterService()

    private let baseURL = "https://openrouter.ai/api/v1"
    private let keychain = KeychainService.shared

    private var cachedModels: [OpenRouterModel]?
    private var modelsCacheDate: Date?
    private let cacheValidityDuration: TimeInterval = 3600

    private init() {}

    // MARK: - Models

    func fetchModels(forceRefresh: Bool = false) async throws -> [OpenRouterModel] {
        if !forceRefresh,
           let cached = cachedModels,
           let cacheDate = modelsCacheDate,
           Date().timeIntervalSince(cacheDate) < cacheValidityDuration {
            return cached
        }

        let apiKey = try await keychain.getAPIKey()

        guard let url = URL(string: "\(baseURL)/models") else {
            throw OpenRouterServiceError.invalidURL
        }

        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Fabulis iOS App", forHTTPHeaderField: "X-Title")

        let (data, response) = try await URLSession.shared.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw OpenRouterServiceError.invalidResponse
        }

        if httpResponse.statusCode != 200 {
            if let errorResponse = try? JSONDecoder().decode(OpenRouterError.self, from: data) {
                throw OpenRouterServiceError.httpError(httpResponse.statusCode, errorResponse.error.message)
            }
            throw OpenRouterServiceError.httpError(httpResponse.statusCode, "Unknown error")
        }

        let modelsResponse = try JSONDecoder().decode(OpenRouterModelsResponse.self, from: data)

        let textModels = modelsResponse.data
            .filter { model in
                guard let modalities = model.architecture?.modalities else { return true }
                let hasTextInput = modalities.input?.contains("text") ?? true
                let hasTextOutput = modalities.output?.contains("text") ?? true
                return hasTextInput && hasTextOutput
            }
            .sorted { $0.displayName < $1.displayName }

        cachedModels = textModels
        modelsCacheDate = Date()

        return textModels
    }

    // MARK: - Chat Completion

    func sendChatCompletion(
        model: String,
        messages: [ChatMessage],
        temperature: Double = 0.8,
        maxTokens: Int? = nil
    ) async throws -> ChatCompletionResponse {
        let apiKey = try await keychain.getAPIKey()

        guard let url = URL(string: "\(baseURL)/chat/completions") else {
            throw OpenRouterServiceError.invalidURL
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Fabulis iOS App", forHTTPHeaderField: "X-Title")

        let requestBody = ChatCompletionRequest(
            model: model,
            messages: messages,
            stream: false,
            temperature: temperature,
            maxTokens: maxTokens
        )

        request.httpBody = try JSONEncoder().encode(requestBody)

        let (data, response) = try await URLSession.shared.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw OpenRouterServiceError.invalidResponse
        }

        if httpResponse.statusCode != 200 {
            if let errorResponse = try? JSONDecoder().decode(OpenRouterError.self, from: data) {
                throw OpenRouterServiceError.httpError(httpResponse.statusCode, errorResponse.error.message)
            }
            throw OpenRouterServiceError.httpError(httpResponse.statusCode, "Unknown error")
        }

        return try JSONDecoder().decode(ChatCompletionResponse.self, from: data)
    }

    // MARK: - Streaming Chat Completion

    func streamChatCompletion(
        model: String,
        messages: [ChatMessage],
        temperature: Double = 0.8,
        maxTokens: Int? = nil
    ) -> AsyncThrowingStream<String, Error> {
        AsyncThrowingStream { continuation in
            Task {
                do {
                    let apiKey = try await keychain.getAPIKey()

                    guard let url = URL(string: "\(baseURL)/chat/completions") else {
                        continuation.finish(throwing: OpenRouterServiceError.invalidURL)
                        return
                    }

                    var request = URLRequest(url: url)
                    request.httpMethod = "POST"
                    request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
                    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                    request.setValue("Fabulis iOS App", forHTTPHeaderField: "X-Title")

                    let requestBody = ChatCompletionRequest(
                        model: model,
                        messages: messages,
                        stream: true,
                        temperature: temperature,
                        maxTokens: maxTokens
                    )

                    request.httpBody = try JSONEncoder().encode(requestBody)

                    let (bytes, response) = try await URLSession.shared.bytes(for: request)

                    guard let httpResponse = response as? HTTPURLResponse else {
                        continuation.finish(throwing: OpenRouterServiceError.invalidResponse)
                        return
                    }

                    if httpResponse.statusCode != 200 {
                        continuation.finish(throwing: OpenRouterServiceError.httpError(httpResponse.statusCode, "Request failed"))
                        return
                    }

                    for try await line in bytes.lines {
                        if line.hasPrefix("data: ") {
                            let jsonString = String(line.dropFirst(6))

                            if jsonString == "[DONE]" {
                                break
                            }

                            if let data = jsonString.data(using: .utf8),
                               let chunk = try? JSONDecoder().decode(ChatCompletionResponse.self, from: data),
                               let content = chunk.choices.first?.delta?.content {
                                continuation.yield(content)
                            }
                        }
                    }

                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    // MARK: - API Key Validation

    func validateAPIKey(_ key: String) async throws -> Bool {
        guard let url = URL(string: "\(baseURL)/models") else {
            throw OpenRouterServiceError.invalidURL
        }

        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")

        let (_, response) = try await URLSession.shared.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            return false
        }

        return httpResponse.statusCode == 200
    }
}
