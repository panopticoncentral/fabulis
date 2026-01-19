import Foundation
import SwiftUI

@Observable
final class OnboardingViewModel {
    var apiKey: String = ""
    var isValidating: Bool = false
    var validationError: String?
    var isValid: Bool = false

    private let openRouter = OpenRouterService.shared
    private let keychain = KeychainService.shared

    var canSubmit: Bool {
        !apiKey.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !isValidating
    }

    @MainActor
    func validateAndSave() async -> Bool {
        let trimmedKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)

        guard !trimmedKey.isEmpty else {
            validationError = "Please enter your API key"
            return false
        }

        isValidating = true
        validationError = nil

        do {
            let valid = try await openRouter.validateAPIKey(trimmedKey)

            if valid {
                try await keychain.saveAPIKey(trimmedKey)
                isValid = true
                isValidating = false
                return true
            } else {
                validationError = "Invalid API key. Please check and try again."
                isValidating = false
                return false
            }
        } catch {
            validationError = "Failed to validate: \(error.localizedDescription)"
            isValidating = false
            return false
        }
    }

    func checkExistingKey() async -> Bool {
        await keychain.hasAPIKey()
    }
}
