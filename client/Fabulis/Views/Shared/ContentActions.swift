import SwiftUI

struct ContentActions {
    var saveChanges: (() -> Void)?
    var saveToLibrary: (() -> Void)?
    var refresh: (() -> Void)?
    var listen: (() -> Void)?
    var summary: (() -> Void)?
}

private struct ContentActionsKey: FocusedValueKey {
    typealias Value = ContentActions
}

extension FocusedValues {
    var contentActions: ContentActions? {
        get { self[ContentActionsKey.self] }
        set { self[ContentActionsKey.self] = newValue }
    }
}
