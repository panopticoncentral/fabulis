import Foundation

private func describeDecoding(_ err: Error) -> String {
    guard let de = err as? DecodingError else { return err.localizedDescription }
    func path(_ ctx: DecodingError.Context) -> String {
        ctx.codingPath.map(\.stringValue).joined(separator: ".")
    }
    switch de {
    case .dataCorrupted(let ctx):
        return "data corrupted at '\(path(ctx))': \(ctx.debugDescription)"
    case .keyNotFound(let key, let ctx):
        return "missing key '\(key.stringValue)' at '\(path(ctx))'"
    case .typeMismatch(let type, let ctx):
        return "type mismatch (expected \(type)) at '\(path(ctx))': \(ctx.debugDescription)"
    case .valueNotFound(let type, let ctx):
        return "missing value (expected \(type)) at '\(path(ctx))'"
    @unknown default:
        return de.localizedDescription
    }
}

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
        case .decoding(let err): return "Could not decode response: \(describeDecoding(err))"
        case .transport(let err): return "Network error: \(err.localizedDescription)"
        }
    }
}

/// Parses the two ISO 8601 shapes .NET emits. `ISO8601DateFormatter` is
/// documented as thread-safe for parsing but isn't marked `Sendable`, so this
/// wrapper is `@unchecked Sendable` to let the `@Sendable` date-decoding
/// closure capture a single value rather than two bare formatters.
private struct ISO8601DateParser: @unchecked Sendable {
    private let withFractionalSeconds: ISO8601DateFormatter
    private let plain: ISO8601DateFormatter

    init() {
        withFractionalSeconds = ISO8601DateFormatter()
        withFractionalSeconds.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
    }

    func date(from str: String) -> Date? {
        withFractionalSeconds.date(from: str) ?? plain.date(from: str)
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

        // .NET's default System.Text.Json emits ISO 8601 with up to 7-digit
        // fractional seconds (e.g. "2026-05-02T13:24:45.1234567Z"). Swift's
        // .iso8601 strategy uses ISO8601DateFormatter without
        // .withFractionalSeconds, so it rejects those strings. Try both.
        let dateParser = ISO8601DateParser()

        self.decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { dec in
            let str = try dec.singleValueContainer().decode(String.self)
            if let d = dateParser.date(from: str) { return d }
            throw DecodingError.dataCorruptedError(
                in: try dec.singleValueContainer(),
                debugDescription: "Unrecognized ISO 8601 date: \(str)")
        }

        self.encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
    }

    func unlock(serverURL: String, password: String) async throws -> UnlockResponse {
        // Hit the server FIRST so a failed unlock (bad URL, server down, wrong
        // password) doesn't leave a half-configured identity in Keychain that
        // would trap the user on UnlockPromptView with no way to edit the URL.
        let resp = try await postUnlock(serverURL: serverURL, password: password)
        try await keychain.saveServerURL(serverURL)
        try await keychain.saveSessionToken(resp.token)
        return resp
    }

    private func postUnlock(serverURL: String, password: String) async throws -> UnlockResponse {
        guard var components = URLComponents(string: serverURL) else { throw APIError.invalidURL }
        let base = components.path.hasSuffix("/") ? String(components.path.dropLast()) : components.path
        components.path = base + "/api/v1/auth/unlock"
        guard let url = components.url else { throw APIError.invalidURL }

        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        struct Body: Encodable { let password: String }
        req.httpBody = try encoder.encode(Body(password: password))
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
        do { return try decoder.decode(UnlockResponse.self, from: data) } catch { throw APIError.decoding(error) }
    }

    func authStatus(timeout: TimeInterval? = nil) async throws -> AuthStatusResponse {
        // URLSessionConfiguration.waitsForConnectivity = true makes URLSession
        // ignore timeoutIntervalForRequest (and per-request timeoutInterval)
        // while it waits for a route to the host, so a per-request timeout
        // alone is not enough to fail fast when the server's machine is
        // asleep. Race the call against Task.sleep so we actually unblock.
        guard let timeout else {
            return try await request("GET", path: "/auth/status", authed: true)
        }
        return try await withTransportTimeout(seconds: timeout) {
            try await self.request("GET", path: "/auth/status", authed: true, timeout: timeout)
        }
    }

    private func withTransportTimeout<T: Sendable>(
        seconds: TimeInterval,
        operation: @Sendable @escaping () async throws -> T
    ) async throws -> T {
        try await withThrowingTaskGroup(of: T.self) { group in
            group.addTask { try await operation() }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                throw APIError.transport(URLError(.timedOut))
            }
            defer { group.cancelAll() }
            return try await group.next()!
        }
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

    func categoryPrompts(categoryId: Int) async throws -> PromptCategoryDetail {
        try await request("GET", path: "/categories/\(categoryId)/prompts", authed: true)
    }

    func prompt(id: Int) async throws -> PromptDetail {
        try await request("GET", path: "/prompts/\(id)", authed: true)
    }

    func createPrompt(categoryId: Int, title: String?) async throws -> PromptDetail {
        let body = CreatePromptRequest(categoryId: categoryId, title: title)
        return try await request("POST", path: "/prompts", body: body, authed: true)
    }

    func updatePrompt(id: Int, title: String, categoryId: Int, messages: [String]) async throws -> PromptDetail {
        let body = UpdatePromptRequest(title: title, categoryId: categoryId, messages: messages)
        return try await request("PUT", path: "/prompts/\(id)", body: body, authed: true)
    }

    func deletePrompt(id: Int) async throws {
        try await requestVoid("DELETE", path: "/prompts/\(id)", authed: true)
    }

    func categoryOneLiners(categoryId: Int) async throws -> OneLinerCategoryDetail {
        try await request("GET", path: "/categories/\(categoryId)/one-liners", authed: true)
    }

    func createOneLiner(categoryId: Int, text: String) async throws -> OneLinerDetail {
        let body = CreateOneLinerRequest(categoryId: categoryId, text: text)
        return try await request("POST", path: "/one-liners", body: body, authed: true)
    }

    func updateOneLiner(id: Int, text: String, categoryId: Int) async throws -> OneLinerDetail {
        let body = UpdateOneLinerRequest(text: text, categoryId: categoryId)
        return try await request("PUT", path: "/one-liners/\(id)", body: body, authed: true)
    }

    func deleteOneLiner(id: Int) async throws {
        try await requestVoid("DELETE", path: "/one-liners/\(id)", authed: true)
    }

    func categoryTropes(categoryId: Int) async throws -> TropeCategoryDetail {
        try await request("GET", path: "/categories/\(categoryId)/tropes", authed: true)
    }

    func createTrope(categoryId: Int, text: String) async throws -> TropeDetail {
        let body = CreateTropeRequest(categoryId: categoryId, text: text)
        return try await request("POST", path: "/tropes", body: body, authed: true)
    }

    func updateTrope(id: Int, text: String, categoryId: Int) async throws -> TropeDetail {
        let body = UpdateTropeRequest(text: text, categoryId: categoryId)
        return try await request("PUT", path: "/tropes/\(id)", body: body, authed: true)
    }

    func deleteTrope(id: Int) async throws {
        try await requestVoid("DELETE", path: "/tropes/\(id)", authed: true)
    }

    func story(id: Int) async throws -> StoryDetail {
        try await request("GET", path: "/stories/\(id)", authed: true)
    }

    func storyVersion(storyId: Int, version: Int) async throws -> StoryVersionDetail {
        try await request("GET", path: "/stories/\(storyId)/versions/\(version)", authed: true)
    }

    func storySummary(id: Int) async throws -> StorySummaryDetail {
        try await request("GET", path: "/stories/\(id)/summary", authed: true)
    }

    func updateStorySummary(id: Int, text: String) async throws -> StorySummaryDetail {
        struct Body: Encodable { let text: String }
        return try await request("PUT", path: "/stories/\(id)/summary", body: Body(text: text), authed: true)
    }

    func regenerateStorySummary(id: Int) async throws {
        try await requestVoid("POST", path: "/stories/\(id)/summary/regenerate", authed: true)
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

    func deleteDraftMessage(draftId: Int, messageId: Int) async throws {
        try await requestVoid("DELETE", path: "/drafts/\(draftId)/messages/\(messageId)", authed: true)
    }

    func editDraftMessage(draftId: Int, messageId: Int, content: String) async throws {
        try await requestVoid(
            "PUT",
            path: "/drafts/\(draftId)/messages/\(messageId)",
            body: UpdateMessageRequest(content: content),
            authed: true)
    }

    func editAndResubmit(draftId: Int, messageId: Int, content: String) -> AsyncThrowingStream<StreamEnvelope, Error> {
        let session = self.session
        return AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    let req = try await self.buildRequest(
                        method: "POST",
                        path: "/drafts/\(draftId)/messages/\(messageId)/edit-and-resubmit",
                        body: UpdateMessageRequest(content: content),
                        authed: true)
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
                            } catch { /* skip */ }
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

    func createCategory(name: String) async throws -> CategorySummary {
        try await request("POST", path: "/categories", body: CreateCategoryRequest(name: name), authed: true)
    }

    func renameCategory(id: Int, name: String) async throws {
        try await requestVoid("PUT", path: "/categories/\(id)", body: RenameCategoryRequest(name: name), authed: true)
    }

    func deleteCategory(id: Int) async throws {
        try await requestVoid("DELETE", path: "/categories/\(id)", authed: true)
    }

    func settings() async throws -> SettingsDto {
        try await request("GET", path: "/settings", authed: true)
    }

    func updateSettings(
        apiKey: String? = nil,
        assistantModel: String? = nil,
        autoLockSelection: String? = nil,
        kokoroBaseUrl: String? = nil,
        narrationVoice: String? = nil,
        narrationSpeed: Double? = nil,
        summaryModel: String? = nil,
        summaryPrompt: String? = nil
    ) async throws {
        struct Body: Encodable {
            let apiKey: String?
            let assistantModel: String?
            let autoLockSelection: String?
            let kokoroBaseUrl: String?
            let narrationVoice: String?
            let narrationSpeed: Double?
            let summaryModel: String?
            let summaryPrompt: String?
        }
        try await requestVoid(
            "PUT",
            path: "/settings",
            body: Body(
                apiKey: apiKey,
                assistantModel: assistantModel,
                autoLockSelection: autoLockSelection,
                kokoroBaseUrl: kokoroBaseUrl,
                narrationVoice: narrationVoice,
                narrationSpeed: narrationSpeed,
                summaryModel: summaryModel,
                summaryPrompt: summaryPrompt),
            authed: true)
    }

    func models() async throws -> [ModelInfo] {
        try await request("GET", path: "/models", authed: true)
    }

    func getStoryteller() async throws -> StorytellerDto {
        try await request("GET", path: "/storyteller", authed: true)
    }

    func updateStoryteller(_ body: StorytellerUpdateRequest) async throws {
        try await requestVoid("PUT", path: "/storyteller", body: body, authed: true)
    }

    func generateTitle(draftId: Int) async throws -> String {
        let resp: GenerateTitleResponse = try await request(
            "POST", path: "/drafts/\(draftId)/generate-title", authed: true)
        return resp.title
    }

    /// Re-attaches to an in-flight (or recently-completed) generation for
    /// `draftId`. The first envelope is `snapshot` (full content so far),
    /// followed by live deltas, and a terminal `done`/`error` envelope.
    /// Throws `APIError.server(status: 404, …)` if there's nothing to attach
    /// to — caller should refresh the draft via `getDraft`.
    func streamReattach(draftId: Int) -> AsyncThrowingStream<StreamEnvelope, Error> {
        let session = self.session
        return AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    let req = try await self.buildRequest(method: "GET", path: "/drafts/\(draftId)/stream", authed: true)
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
                            } catch { /* skip malformed */ }
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

    /// Tells the server to stop the in-flight generation for `draftId` and
    /// save whatever has been produced so far. Idempotent: returns 204 even
    /// if nothing is in flight.
    func abortStream(draftId: Int) async throws {
        try await requestVoid("DELETE", path: "/drafts/\(draftId)/stream", authed: true)
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

    // Returns raw response bytes for endpoints whose payload is not JSON
    // (e.g. /narration/synthesize returns audio/mpeg).
    private func requestBytes(
        _ method: String,
        path: String,
        body: (some Encodable)? = Optional<EmptyBody>.none,
        authed: Bool
    ) async throws -> Data {
        let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
        return data
    }

    private func request<T: Decodable>(_ method: String, path: String, authed: Bool, timeout: TimeInterval? = nil) async throws -> T {
        return try await request(method, path: path, body: Optional<EmptyBody>.none, authed: authed, timeout: timeout)
    }

    private func request<T: Decodable, B: Encodable>(_ method: String, path: String, body: B?, authed: Bool, timeout: TimeInterval? = nil) async throws -> T {
        var req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        if let timeout { req.timeoutInterval = timeout }
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
        do { return try decoder.decode(T.self, from: data) } catch { throw APIError.decoding(error) }
    }

    private func requestVoid(_ method: String, path: String, authed: Bool) async throws {
        let req = try await buildRequest(method: method, path: path, body: Optional<EmptyBody>.none, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
    }

    private func requestVoid<B: Encodable>(_ method: String, path: String, body: B, authed: Bool) async throws {
        let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
    }

    private func buildRequest(method: String, path: String, authed: Bool) async throws -> URLRequest {
        try await buildRequest(method: method, path: path, body: Optional<EmptyBody>.none, authed: authed)
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

    // MARK: - Narration

    func narrationVoices() async throws -> [NarrationVoice] {
        let resp: VoicesResponse = try await request("GET", path: "/narration/voices", authed: true)
        return resp.voices
    }

    /// Validates the synthesis params with the server (which strips markdown,
    /// resolves voice/speed defaults, and checks the text length) and gets
    /// back a one-shot token. Use `playNarrationURL(token:)` to construct
    /// the GET URL that AVPlayer can fetch natively.
    func prepareNarration(text: String, voice: String?, speed: Double?) async throws -> String {
        struct Body: Encodable {
            let text: String
            let voice: String?
            let speed: Double?
        }
        struct Response: Decodable { let token: String }
        let resp: Response = try await request(
            "POST",
            path: "/narration/prepare",
            body: Body(text: text, voice: voice, speed: speed),
            authed: true)
        return resp.token
    }

    /// Builds the public GET URL for the streaming audio. The token itself
    /// is the credential (one-shot, 5-minute TTL), so this URL doesn't need
    /// an Authorization header — which lets AVPlayer fetch it via its
    /// native HTTP path with no special configuration.
    func playNarrationURL(token: String) async throws -> URL {
        guard let serverURL = try await keychain.loadServerURL() else { throw APIError.notConfigured }
        guard var components = URLComponents(string: serverURL) else { throw APIError.invalidURL }
        let trimmed = components.path.hasSuffix("/") ? String(components.path.dropLast()) : components.path
        components.path = trimmed + "/api/v1/narration/play/" + token
        guard let url = components.url else { throw APIError.invalidURL }
        return url
    }
}
