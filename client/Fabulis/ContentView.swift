import SwiftUI

struct ContentView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        content
            .onChange(of: scenePhase) { _, phase in
                guard phase == .active else { return }
                // Don't re-bootstrap while the user is actively onboarding or
                // typing their password — it would disrupt that flow.
                switch appState.phase {
                case .needsOnboarding, .needsAuth: return
                default: Task { await appState.bootstrap() }
                }
            }
    }

    /// Once a library has been shown, keep the SAME `LibraryView` mounted across
    /// ready ⇄ locked/unreachable and cover it with the auth UI. This must be a
    /// single structural branch — putting `.ready` and `.needsAuth` in separate
    /// `switch` cases would give SwiftUI distinct identities and tear the library
    /// (and the user's open draft, selection, scroll) down on every lock.
    @ViewBuilder
    private var content: some View {
        if showLibrary {
            LibraryView()
                .fullScreenCover(isPresented: authCoverPresented) { authCover }
        } else {
            switch appState.phase {
            case .needsOnboarding:
                OnboardingView()
            case .needsAuth, .unreachable:
                // Cold start: no library to preserve, so show auth directly.
                authCover
            default:
                ProgressView()
            }
        }
    }

    private var showLibrary: Bool {
        switch appState.phase {
        case .ready: return true
        case .needsAuth, .unreachable: return appState.hasSession
        default: return false
        }
    }

    @ViewBuilder
    private var authCover: some View {
        switch appState.phase {
        case .needsAuth:
            UnlockPromptView()
        case .unreachable(let url, let message):
            ServerUnreachableView(serverURL: url, message: message)
        default:
            EmptyView()
        }
    }

    private var authCoverPresented: Binding<Bool> {
        Binding(
            get: {
                switch appState.phase {
                case .needsAuth, .unreachable: return true
                default: return false
                }
            },
            set: { _ in })
    }
}
