import Foundation

enum ReadingPreferences {
    static var store: UserDefaults {
        #if DEBUG
        if UIFixtures.enabled { return UserDefaults(suiteName: "Fabulis.UIFixtures")! }
        #endif
        return .standard
    }
}
