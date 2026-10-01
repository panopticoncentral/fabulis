import Foundation
import SwiftUI
import Testing
@testable import Fabulis

struct SearchTests {
    @Test func snippetsPreserveLiteralTextAndUnicode() {
        let value = SearchSnippet.attributed("A **literal** <tag> \u{2}café 🌙\u{3}.")
        #expect(String(value.characters) == "A **literal** <tag> café 🌙.")
        let highlighted = value.runs.filter { $0.foregroundColor != nil }
        #expect(highlighted.count == 1)
        #expect(highlighted.first.map { String(value[$0.range].characters) } == "café 🌙")
    }

    @Test func searchWireFormatKeepsVersionAndDistinctKinds() throws {
        let data = Data(#"{"results":[{"kind":"storyVersion","entityId":7,"itemId":3,"title":"Moonrise","categoryId":2,"categoryName":"Space","versionNumber":1,"snippet":"A moon","matchInSummary":false},{"kind":"draft","entityId":7,"itemId":7,"title":"Draft","snippet":"A moon","matchInSummary":false}],"hasMore":true}"#.utf8)
        let response = try JSONDecoder().decode(SearchResponse.self, from: data)
        #expect(response.hasMore)
        #expect(response.results[0].versionNumber == 1)
        #expect(response.results[0].itemId == 3)
        #expect(response.results[0].id != response.results[1].id)
        #expect(response.results[1].categoryId == nil)
    }
}
