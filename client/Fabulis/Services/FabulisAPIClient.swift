import Foundation

enum APIError: Error, LocalizedError {
    case notConfigured
    case invalidURL
    case unauthorized
    case server(status: Int, body: String?)
    case decoding(Error)
    case transport(Error)

    var errorDescription: String? {
        switch self {
        case .notConfigured: return "No server URL configured."
        case .invalidURL: return "The server URL is malformed."
        case .unauthorized: return "The session is no longer valid."
        case .server(let status, let body): return "Server returned \(status). \(body ?? "")"
        case .decoding(let err): return "Could not decode response: \(err.localizedDescription)"
        case .transport(let err): return "Network error: \(err.localizedDescription)"
        }
    }
}

actor FabulisAPIClient {
    static let shared = FabulisAPIClient()

    private let session: URLSession
    private let decoder: JSONDecoder
    private let encoder: JSONEncoder
    private let keychain = KeychainService.shared

    private init() {
        let config = URLSessionConfiguration.default
        config.waitsForConnectivity = true
        config.timeoutIntervalForRequest = 30
        self.session = URLSession(configuration: config)

        self.decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601

        self.encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
    }

    func unlock(serverURL: String, password: String) async throws -> UnlockResponse {
        try await keychain.saveServerURL(serverURL)
        struct Body: Encodable { let password: String }
        let resp: UnlockResponse = try await request("POST", path: "/auth/unlock", body: Body(password: password), authed: false)
        try await keychain.saveSessionToken(resp.token)
        return resp
    }

    func authStatus() async throws -> AuthStatusResponse {
        try await request("GET", path: "/auth/status", authed: true)
    }

    func lock() async throws {
        try await requestVoid("POST", path: "/auth/lock", authed: true)
        try? await keychain.deleteSessionToken()
    }

    func library() async throws -> LibraryResponse {
        try await request("GET", path: "/library", authed: true)
    }

    func category(id: Int) async throws -> CategoryDetail {
        try await request("GET", path: "/categories/\(id)", authed: true)
    }

    func story(id: Int) async throws -> StoryDetail {
        try await request("GET", path: "/stories/\(id)", authed: true)
    }

    func storyVersion(storyId: Int, version: Int) async throws -> StoryVersionDetail {
        try await request("GET", path: "/stories/\(storyId)/versions/\(version)", authed: true)
    }

    func listDrafts() async throws -> [DraftSummary] {
        try await request("GET", path: "/drafts", authed: true)
    }

    func createDraft() async throws -> DraftDetail {
        struct Empty: Encodable {}
        return try await request("POST", path: "/drafts", body: Empty(), authed: true)
    }

    func getDraft(id: Int) async throws -> DraftDetail {
        try await request("GET", path: "/drafts/\(id)", authed: true)
    }

    func deleteDraft(id: Int) async throws {
        try await requestVoid("DELETE", path: "/drafts/\(id)", authed: true)
    }

    func saveDraft(id: Int, request body: SaveDraftRequest) async throws -> SaveDraftResponse {
        try await self.request("POST", path: "/drafts/\(id)/save", body: body, authed: true)
    }

    /// Streams `StreamEnvelope` events from POST /drafts/{id}/messages.
    /// Caller stops by cancelling the consuming Task.
    func streamMessage(draftId: Int, prompt: String) -> AsyncThrowingStream<StreamEnvelope, Error> {
        let session = self.session
        return AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    struct Body: Encodable { let prompt: String }
                    let req = try await self.buildRequest(method: "POST", path: "/drafts/\(draftId)/messages", body: Body(prompt: prompt), authed: true)
                    let (bytes, response) = try await session.bytes(for: req)
                    if let http = response as? HTTPURLResponse, http.statusCode == 401 {
                        continuation.finish(throwing: APIError.unauthorized); return
                    }
                    if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                        continuation.finish(throwing: APIError.server(status: http.statusCode, body: nil)); return
                    }
                    let dec = JSONDecoder(); dec.dateDecodingStrategy = .iso8601
                    for try await line in bytes.lines {
                        try Task.checkCancellation()
                        guard line.hasPrefix("data: ") else { continue }
                        let payload = String(line.dropFirst("data: ".count))
                        if let data = payload.data(using: .utf8) {
                            do {
                                let env = try dec.decode(StreamEnvelope.self, from: data)
                                continuation.yield(env)
                                if env.kind == "done" || env.kind == "error" { break }
                            } catch {
                                // skip malformed line
                            }
                        }
                    }
                    continuation.finish()
                } catch is CancellationError {
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    private func request<T: Decodable>(_ method: String, path: String, authed: Bool) async throws -> T {
        return try await request(method, path: path, body: Optional<EmptyBody>.none, authed: authed)
    }

    private func request<T: Decodable, B: Encodable>(_ method: String, path: String, body: B?, authed: Bool) async throws -> T {
        let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
        do { return try decoder.decode(T.self, from: data) } catch { throw APIError.decoding(error) }
    }

    private func requestVoid(_ method: String, path: String, authed: Bool) async throws {
        let req = try await buildRequest(method: method, path: path, body: Optional<EmptyBody>.none, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
    }

    private func buildRequest<B: Encodable>(method: String, path: String, body: B?, authed: Bool) async throws -> URLRequest {
        guard let serverURL = try await keychain.loadServerURL() else { throw APIError.notConfigured }
        guard var components = URLComponents(string: serverURL) else { throw APIError.invalidURL }
        let trimmedExisting = components.path.hasSuffix("/") ? String(components.path.dropLast()) : components.path
        components.path = trimmedExisting + "/api/v1" + path
        guard let url = components.url else { throw APIError.invalidURL }

        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if body != nil {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = try encoder.encode(body)
        }
        if authed {
            guard let token = try await keychain.loadSessionToken() else { throw APIError.unauthorized }
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        return req
    }

    private func transport(_ req: URLRequest) async throws -> (Data, URLResponse) {
        do { return try await session.data(for: req) }
        catch { throw APIError.transport(error) }
    }

    private func validate(response: URLResponse, data: Data) throws {
        guard let http = response as? HTTPURLResponse else { throw APIError.server(status: -1, body: nil) }
        if http.statusCode == 401 { throw APIError.unauthorized }
        if !(200..<300).contains(http.statusCode) {
            let body = String(data: data, encoding: .utf8)
            throw APIError.server(status: http.statusCode, body: body)
        }
    }

    private struct EmptyBody: Encodable {}
}
