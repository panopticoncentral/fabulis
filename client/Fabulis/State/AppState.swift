import Foundation
import Observation

@MainActor
@Observable
final class AppState {
    enum Phase: Equatable {
        case loading
        case needsOnboarding
        case needsAuth
        case ready
    }

    var phase: Phase = .loading

    private let keychain = KeychainService.shared
    private let api = FabulisAPIClient.shared

    func bootstrap() async {
        do {
            let serverURL = try await keychain.loadServerURL()
            guard serverURL != nil, (try await keychain.loadSessionToken()) != nil else {
                phase = (serverURL == nil) ? .needsOnboarding : .needsAuth
                return
            }
            _ = try await api.authStatus()
            phase = .ready
        } catch APIError.unauthorized {
            phase = .needsAuth
        } catch APIError.notConfigured {
            phase = .needsOnboarding
        } catch {
            phase = .ready
        }
    }

    func didCompleteOnboarding() { phase = .ready }
    func didReauthenticate() { phase = .ready }

    func lock() async {
        try? await api.lock()
        phase = .needsAuth
    }
}
