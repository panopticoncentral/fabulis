import Foundation
import Observation

@MainActor
@Observable
final class AppState {
    enum Phase: Equatable {
        case loading
        case needsOnboarding
        case needsAuth
        case unreachable(serverURL: String, message: String)
        case ready
    }

    var phase: Phase = .loading

    private let keychain = KeychainService.shared
    private let api = FabulisAPIClient.shared

    func bootstrap() async {
        let savedURL: String?
        do {
            savedURL = try await keychain.loadServerURL()
        } catch {
            // A *thrown* Keychain error means the item is temporarily
            // unreadable — e.g. the device just woke and the keychain isn't
            // accessible yet — NOT that no server is configured. Treating it as
            // the latter would wrongly drop the user back to onboarding and
            // "forget" their server. Surface it as a retryable state instead;
            // `loadServerURL()` returns nil (not throws) when truly absent.
            phase = .unreachable(serverURL: "", message: error.localizedDescription)
            return
        }
        do {
            guard let savedURL, (try await keychain.loadSessionToken()) != nil else {
                phase = (savedURL == nil) ? .needsOnboarding : .needsAuth
                return
            }
            _ = try await api.authStatus(timeout: 5)
            phase = .ready
        } catch APIError.unauthorized {
            phase = .needsAuth
        } catch APIError.notConfigured {
            phase = .needsOnboarding
        } catch APIError.transport(let err) {
            phase = .unreachable(serverURL: savedURL ?? "", message: err.localizedDescription)
        } catch {
            phase = .unreachable(serverURL: savedURL ?? "", message: error.localizedDescription)
        }
    }

    func didCompleteOnboarding() { phase = .ready }
    func didReauthenticate() { phase = .ready }

    func lock() async {
        try? await api.lock()
        phase = .needsAuth
    }

    func resetServer() async {
        try? await api.lock()
        try? await keychain.deleteSessionToken()
        try? await keychain.deleteServerURL()
        phase = .needsOnboarding
    }
}
