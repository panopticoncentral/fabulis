#if DEBUG
import Foundation

/// Deterministic, process-local sample data for UI tests. Release builds do not
/// contain this path, and fixture runs never authenticate with a real server.
enum UIFixtures {
    static var enabled: Bool { ProcessInfo.processInfo.arguments.contains("-ui-testing") || ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] != nil }
    private static let date = "2026-09-17T12:00:00Z"
    private static var category: [String: Any] {
        ["id": 1, "name": "Nightfall", "createdAt": date, "storyCount": 1,
         "promptCount": 1, "oneLinerCount": 1, "tropeCount": 1]
    }
    private static var messages: [[String: Any]] {
        [["id": 1, "role": "Prompt", "content": "Write a quiet story about a lighthouse keeper.", "sortOrder": 0],
         ["id": 2, "role": "Response", "content": "The last light in the village belonged to Mara.\n\nEvery evening, she climbed the lighthouse stairs with a fresh wick and a small brass key. Below her, the harbor settled into blue shadows.\n\nTonight, a letter waited on the windowsill. It was addressed in her own handwriting.\n\n**For the morning**, it said.\n\nShe turned the key once and watched the beam sweep across the water.", "sortOrder": 1]]
    }
    private static var story: [String: Any] {
        ["id": 1, "title": "The Lantern Keeper", "createdAt": date, "versionCount": 2, "origin": "Generated"]
    }
    private static func draft(_ id: Int) -> [String: Any] {
        ["id": id, "title": id == 1 ? "Lantern draft" : "A second beginning", "createdAt": date,
         "updatedAt": date, "storytellerName": "Storyteller", "modelName": "Sample model",
         "messages": messages]
    }
    static func response(_ method: String, path: String, queryItems: [URLQueryItem] = []) throws -> Data {
        if path == "/settings", ProcessInfo.processInfo.arguments.contains("-ui-settings-failure") {
            throw APIError.server(status: 503, body: "The sample server is unavailable. Try again.")
        }
        let value: Any
        switch path {
        case "/search":
            let query = queryItems.first(where: { $0.name == "q" })?.value ?? ""
            let matches = "lighthouse".hasPrefix(query.lowercased())
            value = ["results": matches ? [[
                "kind": "storyVersion", "entityId": 1, "itemId": 1,
                "title": "The Lantern Keeper", "categoryId": 1, "categoryName": "Nightfall",
                "versionNumber": 1, "snippet": "Write a quiet story about a \u{2}lighthouse\u{3} keeper.",
                "matchInSummary": false
            ]] : [], "hasMore": false]
        case "/one-liners/1": value = ["id": 1, "categoryId": 1, "categoryName": "Nightfall", "text": "A letter in your own handwriting.", "createdAt": date, "updatedAt": date]
        case "/tropes/1": value = ["id": 1, "categoryId": 1, "categoryName": "Nightfall", "text": "The reluctant guardian", "createdAt": date, "updatedAt": date]
        case "/library": value = ["categories": [category]]
        case "/drafts":
            if method == "POST" { value = draft(3) }
            else {
                value = [1, 2].map { id in
                    ["id": id, "title": id == 1 ? "Lantern draft" : "A second beginning",
                     "createdAt": date, "updatedAt": date, "messageCount": 2] as [String: Any]
                }
            }
        case "/drafts/1", "/drafts/2", "/drafts/3": value = draft(Int(path.split(separator: "/").last!)!)
        case "/drafts/1/save": value = ["storyId": 1, "versionId": 2, "versionNumber": 2]
        case "/drafts/1/generate-title": value = ["title": "A Light Beyond the Harbor"]
        case "/categories/1": value = ["id": 1, "name": "Nightfall", "createdAt": date, "stories": [story]]
        case "/categories/1/prompts": value = ["id": 1, "name": "Nightfall", "prompts": [["id": 1, "title": "A mysterious letter", "createdAt": date, "messageCount": 1]]]
        case "/categories/1/one-liners": value = ["id": 1, "name": "Nightfall", "oneLiners": [["id": 1, "text": "A letter in your own handwriting.", "createdAt": date]]]
        case "/categories/1/tropes": value = ["id": 1, "name": "Nightfall", "tropes": [["id": 1, "text": "The reluctant guardian", "createdAt": date]]]
        case "/prompts/1/draft": value = ["draftId": 3]
        case "/prompts/1": value = ["id": 1, "categoryId": 1, "categoryName": "Nightfall", "title": "A mysterious letter", "createdAt": date, "updatedAt": date, "messages": [["id": 1, "content": "Begin with a letter in the protagonist’s handwriting.", "sortOrder": 0]]]
        case "/stories/1": value = ["id": 1, "categoryId": 1, "categoryName": "Nightfall", "title": "The Lantern Keeper", "createdAt": date, "versions": [["id": 2, "versionNumber": 2, "origin": "Generated", "modelName": "Sample model", "createdAt": date], ["id": 1, "versionNumber": 1, "origin": "Generated", "modelName": "Sample model", "createdAt": date]]]
        case "/stories/1/versions/1", "/stories/1/versions/2": value = ["id": 2, "storyId": 1, "versionNumber": Int(path.suffix(1))!, "origin": "Generated", "modelName": "Sample model", "createdAt": date, "messages": messages]
        case "/stories/1/summary": value = ["text": "A lighthouse keeper finds a letter in her own handwriting.", "status": "ready", "summarizedThroughVersion": 2, "latestVersion": 2, "isStale": false, "updatedAt": date]
        case "/settings": value = ["apiKeyIsSet": true, "autoLockSelection": "15", "kokoroBaseUrlIsSet": true, "narrationVoice": "Sample voice", "narrationSpeed": 1, "narrationAvailable": true, "summaryPrompt": "Summarize the story briefly."]
        case "/models": value = [["id": "sample/model", "name": "Sample model"]]
        case "/storyteller": value = ["id": 1, "name": "Storyteller", "prompt": "Tell thoughtful stories.", "titlingPrompt": "Choose a short title.", "modelName": "sample/model", "temperature": 0.7]
        default: throw APIError.server(status: 404, body: "No UI fixture for \(path)")
        }
        return try JSONSerialization.data(withJSONObject: value)
    }
}
#endif
