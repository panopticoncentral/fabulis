import SwiftUI

@main
struct FabulisApp: App {
    @State private var appState = AppState()

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
            CommandGroup(after: .appSettings) {
                Button("Lock Vault") { Task { await appState.lock() } }
                    .keyboardShortcut("l", modifiers: [.command, .shift])
                    .disabled(!isReady)
            }
        }
    }
}
