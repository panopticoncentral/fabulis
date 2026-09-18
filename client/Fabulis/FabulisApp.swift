import SwiftUI

@main
struct FabulisApp: App {
    @State private var appState = AppState()
    @FocusedValue(\.contentActions) private var contentActions

    init() {
        #if DEBUG
        if UIFixtures.enabled { ReadingPreferences.store.removePersistentDomain(forName: "Fabulis.UIFixtures") }
        #endif
    }

    private var isReady: Bool { appState.phase == .ready }

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(appState)
                .task { await appState.bootstrap() }
        }
        .commands {
            CommandGroup(replacing: .appSettings) {
                Button("Settings…") { appState.showSettings = true }
                    .keyboardShortcut(",", modifiers: .command)
                    .disabled(!isReady)
            }
            // Replace (not augment) the New group: on Mac Catalyst SwiftUI's
            // WindowGroup contributes a "New Window" item on ⌘N, and a second
            // ⌘N command crashes the menu builder at launch. Replacing removes
            // New Window and gives ⌘N to New Draft.
            CommandGroup(replacing: .newItem) {
                Button("New Draft") { appState.newDraftRequested = true }
                    .keyboardShortcut("n", modifiers: .command)
                    .disabled(!isReady)
            }
            CommandGroup(replacing: .saveItem) {
                Button("Save Changes") { contentActions?.saveChanges?() }
                    .keyboardShortcut("s", modifiers: .command)
                    .disabled(!isReady || contentActions?.saveChanges == nil)
                Button("Save to Library…") { contentActions?.saveToLibrary?() }
                    .keyboardShortcut("s", modifiers: [.command, .shift])
                    .disabled(!isReady || contentActions?.saveToLibrary == nil)
            }
            CommandMenu("Story") {
                Button("Listen") { contentActions?.listen?() }
                    .disabled(!isReady || contentActions?.listen == nil)
                Button("Summary") { contentActions?.summary?() }
                    .disabled(!isReady || contentActions?.summary == nil)
                Divider()
                Button("Refresh") { contentActions?.refresh?() }
                    .keyboardShortcut("r", modifiers: .command)
                    .disabled(!isReady || contentActions?.refresh == nil)
            }
            CommandGroup(after: .appSettings) {
                Button("Lock Vault") { Task { await appState.lock() } }
                    .keyboardShortcut("l", modifiers: [.command, .shift])
                    .disabled(!isReady)
            }
        }
    }
}
