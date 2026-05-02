import Foundation
import Security

enum KeychainError: Error {
    case unknown(OSStatus)
    case invalidData
}

actor KeychainService {
    static let shared = KeychainService()

    private let service = "com.fabulis.server"
    private let serverURLAccount = "server-url"
    private let sessionTokenAccount = "session-token"

    private init() {}

    func saveServerURL(_ url: String) throws { try save(account: serverURLAccount, value: url) }
    func loadServerURL() throws -> String? { try load(account: serverURLAccount) }

    func saveSessionToken(_ token: String) throws { try save(account: sessionTokenAccount, value: token) }
    func loadSessionToken() throws -> String? { try load(account: sessionTokenAccount) }
    func deleteSessionToken() throws { try delete(account: sessionTokenAccount) }

    private func save(account: String, value: String) throws {
        guard let data = value.data(using: .utf8) else { throw KeychainError.invalidData }
        try delete(account: account)
        let attrs: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
        ]
        let status = SecItemAdd(attrs as CFDictionary, nil)
        guard status == errSecSuccess else { throw KeychainError.unknown(status) }
    }

    private func load(account: String) throws -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw KeychainError.unknown(status) }
        guard let data = result as? Data, let value = String(data: data, encoding: .utf8) else {
            throw KeychainError.invalidData
        }
        return value
    }

    private func delete(account: String) throws {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        let status = SecItemDelete(query as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            throw KeychainError.unknown(status)
        }
    }
}
