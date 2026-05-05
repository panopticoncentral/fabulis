import SwiftUI

struct ContentView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        Group {
            switch appState.phase {
            case .loading:
                ProgressView()
            case .needsOnboarding:
                OnboardingView()
            case .needsAuth:
                UnlockPromptView()
            case .unreachable(let url, let message):
                ServerUnreachableView(serverURL: url, message: message)
            case .ready:
                LibraryView()
            }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await appState.bootstrap() } }
        }
    }
}
