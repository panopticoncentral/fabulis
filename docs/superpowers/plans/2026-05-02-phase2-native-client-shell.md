# Phase 2: Native Client Shell + Read Flows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring up a native SwiftUI client (iOS + iPadOS + Mac Catalyst) that talks to the Phase 1 server API, with onboarding (server URL + vault password), library browse, and read-only story viewing — but no story generation yet.

**Architecture:** Revive the `first-draft` Xcode project under `client/` preserving the existing bundle ID and team (so TestFlight continuity holds). Strip out SwiftData and the direct-OpenRouter service layer; replace with a `FabulisAPIClient` (URLSession-backed) and a `KeychainService` that holds `serverURL` + `sessionToken`. Views fetch data with `.task` and render plain `Codable` DTOs that mirror the server's JSON shapes. App routes through an `AppState` enum that handles `loading`/`needsOnboarding`/`needsAuth`/`ready` transitions, with foreground-revalidation against `GET /api/v1/auth/status`.

**Tech Stack:** SwiftUI, Swift 5/6, Xcode 16+ (uses `PBXFileSystemSynchronizedRootGroup` so adding/removing `.swift` files needs no `.pbxproj` edits), URLSession, Keychain Services, Mac Catalyst destination on the iOS target.

## File Structure

**Bundle/team continuity from `first-draft`:**
- Bundle ID `AchatesSoftware.Fabulis`, team `5KAYG269JK`, marketing version bumps to `2.0.0`, build to `1`. Keeping these intact preserves the TestFlight pipeline.

**Create (under `client/`):**
- `client/Fabulis.xcodeproj/...` — copied verbatim from `first-draft` then patched for Mac Catalyst + version bump
- `client/Fabulis/FabulisApp.swift` — app entry, no SwiftData
- `client/Fabulis/ContentView.swift` — routes on `AppState`
- `client/Fabulis/Info.plist` — adds `NSAppTransportSecurity` exception
- `client/Fabulis/Fabulis.entitlements` — kept; iCloud entries removed (we don't use CloudKit)
- `client/Fabulis/Assets.xcassets/...` — copied verbatim
- `client/Fabulis/Models/APIDtos.swift` — Codable structs mirroring server DTOs
- `client/Fabulis/Services/KeychainService.swift` — serverURL + sessionToken (replaces first-draft version)
- `client/Fabulis/Services/FabulisAPIClient.swift` — URLSession HTTP client; throws `APIError.unauthorized` on 401
- `client/Fabulis/State/AppState.swift` — `@Observable` state holder for the auth machine
- `client/Fabulis/Views/Onboarding/OnboardingView.swift`
- `client/Fabulis/Views/Auth/UnlockPromptView.swift` — shown when token expires mid-session
- `client/Fabulis/Views/Library/LibraryView.swift`
- `client/Fabulis/Views/Library/CategoryCard.swift` — visual based on first-draft `StorytellerCard`
- `client/Fabulis/Views/Library/CategoryView.swift`
- `client/Fabulis/Views/Story/StoryView.swift` — story header + version list
- `client/Fabulis/Views/Story/StoryVersionView.swift` — message reader
- `client/Fabulis/Views/Story/StoryMessageView.swift` — single-message bubble
- `client/Fabulis/Views/Settings/SettingsView.swift` — minimal: server URL display + Lock button
- `client/FabulisTests/FabulisTests.swift` — kept as the empty stub from first-draft
- `client/FabulisUITests/...` — kept as the empty stubs from first-draft

**Files to NOT bring over from first-draft:**
- `Models/Storyteller.swift`, `Models/Story.swift`, `Models/StorySegment.swift`, `Models/OpenRouterModel.swift` (SwiftData / OpenRouter)
- `Services/OpenRouterService.swift`, `Services/StorytellerDefaults.swift`
- `ViewModels/LibraryViewModel.swift`, `ViewModels/OnboardingViewModel.swift`, `ViewModels/StorySessionViewModel.swift`
- `Views/Library/StorytellerCard.swift`, `StorytellerDetailView.swift`, `StorytellerEditorView.swift`
- `Views/Session/StorySessionView.swift`, `StorySegmentView.swift`, `StoryInputView.swift`
- `Views/Settings/ModelPickerView.swift`, `Views/Settings/SettingsView.swift` (we'll write a new minimal one)

The synchronized folder feature means: just don't copy them in. The pbxproj does not need edits.

## Notes for the implementer

- **Don't touch `client/Fabulis.xcodeproj/project.pbxproj` for source-file additions/removals.** Synchronized folders auto-pick up `.swift` files. The only pbxproj edits needed are: marketing version bump, Mac Catalyst destination flags, and removing the `Storyteller.swift`/etc. as `PBXFileSystemSynchronizedExceptionSet` if they're listed (they shouldn't be).
- **Server URL is mutable.** The user types `http://hostname:5288` in onboarding; we never hard-code anything. Store in Keychain alongside the token.
- **DTO field naming.** Swift's `JSONDecoder` with `.convertFromSnakeCase` is NOT what we want — the server uses PascalCase by default (.NET Minimal API). Use either explicit `CodingKeys` or set `JSONDecoder.keyDecodingStrategy` to `.convertFromPascalCase`-equivalent. Easiest: System.Text.Json on the server lowercases first-letter by default → server outputs camelCase → match with default Swift decoder. Verify by curl-inspecting one response before committing the API client.
- **NavigationStack usage.** All read flows live under one `NavigationStack` rooted at `LibraryView`. Push `CategoryView` → `StoryView` → `StoryVersionView` via `navigationDestination`.
- **Foreground revalidation.** `AppState` observes `ScenePhase` and on transition to `.active` calls `apiClient.authStatus()`. On 401, transitions to `.needsAuth` so the unlock prompt sheet appears over current navigation.
- **Mac Catalyst.** Enable via pbxproj flags (`SUPPORTS_MACCATALYST = YES;`, `DERIVE_MACCATALYST_PRODUCT_BUNDLE_IDENTIFIER = NO;`), keep iOS as the primary destination. Avoid `UIPasteboard`-only APIs in this phase since we don't have story-copy yet.
- **NSAppTransportSecurity.** Phase 2 uses `NSAllowsLocalNetworking = YES` (broader, but documented and accepted for local-network apps). A scoped per-host exception comes in Phase 4.
- **Per-task verification.** Each task ends with `xcodebuild` succeeding for the iOS Simulator destination. Mac Catalyst verification is once at the end. Live app testing (does it reach the server?) is the developer's manual smoke test at the end of Section F.

---

## Section A — Set up the client tree

### Task 1: Copy `first-draft` Xcode skeleton + assets

**Files:**
- Create: `client/Fabulis.xcodeproj/` (from `first-draft`)
- Create: `client/Fabulis/Assets.xcassets/` (from `first-draft`)
- Create: `client/Fabulis/Info.plist` (from `first-draft`, will edit later)
- Create: `client/Fabulis/Fabulis.entitlements` (modified)
- Create: `client/FabulisTests/`, `client/FabulisUITests/` (empty stubs from `first-draft`)

- [ ] **Step 1: Create the client directory and extract files from `first-draft`**

```bash
mkdir -p client/Fabulis/{Models,Services,State,Views/{Onboarding,Auth,Library,Story,Settings}}
git --work-tree=client checkout first-draft -- \
  Fabulis.xcodeproj \
  Fabulis/Assets.xcassets \
  Fabulis/Info.plist \
  Fabulis/Fabulis.entitlements \
  FabulisTests \
  FabulisUITests
# undo the index changes git checkout makes against the main worktree:
git reset HEAD -- Fabulis.xcodeproj Fabulis FabulisTests FabulisUITests
```

After this, `client/` should contain `Fabulis.xcodeproj/`, `Fabulis/Assets.xcassets/`, `Fabulis/Info.plist`, `Fabulis/Fabulis.entitlements`, `FabulisTests/FabulisTests.swift`, `FabulisUITests/...`.

- [ ] **Step 2: Replace the entitlements file (drop CloudKit + sandbox)**

We don't use CloudKit. Drop those entries; sandbox stays for Catalyst. Replace `client/Fabulis/Fabulis.entitlements` with:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>com.apple.security.app-sandbox</key>
	<true/>
	<key>com.apple.security.network.client</key>
	<true/>
</dict>
</plist>
```

`network.client` is required so the app can open outbound HTTP from the Mac Catalyst sandbox.

- [ ] **Step 3: Bump version + bundle id sanity check + Mac Catalyst flags in `project.pbxproj`**

Open `client/Fabulis.xcodeproj/project.pbxproj` and apply these in-place edits:

```bash
cd client
sed -i '' 's/MARKETING_VERSION = 1.0;/MARKETING_VERSION = 2.0.0;/g' Fabulis.xcodeproj/project.pbxproj
# Mac Catalyst: turn it on for both Debug and Release config blocks of the Fabulis target
# (Tests targets stay iOS-only.)
# We add SUPPORTS_MACCATALYST and DERIVE_MACCATALYST_PRODUCT_BUNDLE_IDENTIFIER inside the
# Fabulis target build config blocks. Manual edit in the pbxproj is fine — see Step 4.
cd ..
```

- [ ] **Step 4: Add `SUPPORTS_MACCATALYST = YES;` and `DERIVE_MACCATALYST_PRODUCT_BUNDLE_IDENTIFIER = NO;` to the Fabulis target build settings**

Open `client/Fabulis.xcodeproj/project.pbxproj` in an editor. Find both build config blocks for the `Fabulis` target (NOT `FabulisTests` or `FabulisUITests`). They look like:

```
		<buildConfigurationHash> /* Debug */ = {
			isa = XCBuildConfiguration;
			buildSettings = {
				...
				PRODUCT_BUNDLE_IDENTIFIER = AchatesSoftware.Fabulis;
				...
			};
			name = Debug;
		};
```

In each (Debug AND Release) for the Fabulis target, add INSIDE `buildSettings = { ... };`:

```
				SUPPORTS_MACCATALYST = YES;
				DERIVE_MACCATALYST_PRODUCT_BUNDLE_IDENTIFIER = NO;
				SUPPORTED_PLATFORMS = "iphoneos iphonesimulator macosx";
				MACOSX_DEPLOYMENT_TARGET = 14.0;
```

- [ ] **Step 5: Verify the project still parses**

Run from the repo root:

```bash
xcodebuild -project client/Fabulis.xcodeproj -list
```

Expected: Targets list includes `Fabulis`, `FabulisTests`, `FabulisUITests`. No "could not parse" error.

- [ ] **Step 6: Commit**

```bash
git add client/
git commit -m "Bring up client/ from first-draft skeleton, enable Mac Catalyst"
```

---

### Task 2: Replace Info.plist with NSAllowsLocalNetworking

**Files:**
- Modify: `client/Fabulis/Info.plist`

- [ ] **Step 1: Replace contents**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>ITSAppUsesNonExemptEncryption</key>
	<false/>
	<key>NSAppTransportSecurity</key>
	<dict>
		<key>NSAllowsLocalNetworking</key>
		<true/>
	</dict>
	<key>NSLocalNetworkUsageDescription</key>
	<string>Fabulis connects to your Fabulis server on the local network.</string>
</dict>
</plist>
```

- [ ] **Step 2: Build sanity check**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -15
```

Expected: `BUILD SUCCEEDED` (the project at this point still has first-draft's source; build will succeed because we haven't deleted anything yet).

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Info.plist
git commit -m "Allow local-network HTTP for the Fabulis server connection"
```

---

### Task 3: Strip first-draft Swift sources we are replacing

**Files (delete from `client/Fabulis/`):**
- All `.swift` files except `Assets.xcassets/`, `Info.plist`, `Fabulis.entitlements`

These will be replaced one section at a time. In the meantime the project will not build, so this task is committed as one big delete; subsequent tasks rebuild incrementally.

- [ ] **Step 1: Delete**

```bash
rm -rf client/Fabulis/{Models,Services,ViewModels,Views} \
       client/Fabulis/FabulisApp.swift \
       client/Fabulis/ContentView.swift
mkdir -p client/Fabulis/{Models,Services,State,Views/{Onboarding,Auth,Library,Story,Settings}}
```

- [ ] **Step 2: Confirm only assets + plist + entitlements remain**

```bash
find client/Fabulis -maxdepth 2 -type f -not -path '*/Assets.xcassets/*' | sort
```

Expected output (exactly these files at top of `Fabulis/`):
```
client/Fabulis/Fabulis.entitlements
client/Fabulis/Info.plist
```

- [ ] **Step 3: Commit**

```bash
git add -A client/Fabulis/
git commit -m "Strip first-draft Swift sources; will rebuild against the API"
```

---

## Section B — Foundations (services + DTOs)

### Task 4: KeychainService for serverURL + sessionToken

**Files:**
- Create: `client/Fabulis/Services/KeychainService.swift`

- [ ] **Step 1: Create the file**

```swift
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

    // -- internals --

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
```

- [ ] **Step 2: Verify the file is on disk** (no build yet — too many sources missing still)

```bash
test -f client/Fabulis/Services/KeychainService.swift && echo OK
```

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Services/KeychainService.swift
git commit -m "Add KeychainService for serverURL + sessionToken"
```

---

### Task 5: API DTOs

**Files:**
- Create: `client/Fabulis/Models/APIDtos.swift`

ASP.NET Core's default System.Text.Json policy lowercases the first letter of each property, producing camelCase JSON (`isUnlocked`, `categoryId`, etc.). Swift's default `JSONDecoder` matches camelCase fields automatically against `camelCase` Swift properties, so no `CodingKeys` are needed.

`MessageRole` on the server is the enum `Prompt | Response`, serialized as the string `"Prompt"` or `"Response"`. Mirror exactly.

- [ ] **Step 1: Create the file**

```swift
import Foundation

// ---------- auth ----------
struct UnlockResponse: Decodable, Sendable {
    let token: String
    let issuedAt: Date
}

struct AuthStatusResponse: Decodable, Sendable {
    let isUnlocked: Bool
    let autoLockMinutes: Int?
}

// ---------- library / categories / stories ----------
struct LibraryResponse: Decodable, Sendable {
    let categories: [CategorySummary]
}

struct CategorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let storyCount: Int
    let latestStoryTitle: String?
}

struct CategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let stories: [StorySummary]
}

struct StorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String
    let createdAt: Date
    let versionCount: Int
}

struct StoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let title: String
    let createdAt: Date
    let versions: [StoryVersionSummary]
}

struct StoryVersionSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let versionNumber: Int
    let modelName: String
    let createdAt: Date
}

struct StoryVersionDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let storyId: Int
    let versionNumber: Int
    let modelName: String
    let createdAt: Date
    let messages: [StoryMessage]
}

enum MessageRole: String, Decodable, Sendable {
    case prompt = "Prompt"
    case response = "Response"
}

struct StoryMessage: Decodable, Identifiable, Sendable {
    let id: Int
    let role: MessageRole
    let content: String
    let sortOrder: Int
}

// ---------- settings ----------
struct SettingsDto: Decodable, Sendable {
    let apiKeyIsSet: Bool
    let assistantModel: String?
    let autoLockSelection: String
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift
git commit -m "Add Codable DTOs mirroring server /api/v1 responses"
```

---

### Task 6: FabulisAPIClient

**Files:**
- Create: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Create the file**

```swift
import Foundation

enum APIError: Error, LocalizedError {
    case notConfigured
    case invalidURL
    case unauthorized
    case server(status: Int, body: String?)
    case decoding(Error)
    case transport(Error)

    var errorDescription: String? {
        switch self {
        case .notConfigured: return "No server URL configured."
        case .invalidURL: return "The server URL is malformed."
        case .unauthorized: return "The session is no longer valid."
        case .server(let status, let body): return "Server returned \(status). \(body ?? "")"
        case .decoding(let err): return "Could not decode response: \(err.localizedDescription)"
        case .transport(let err): return "Network error: \(err.localizedDescription)"
        }
    }
}

actor FabulisAPIClient {
    static let shared = FabulisAPIClient()

    private let session: URLSession
    private let decoder: JSONDecoder
    private let encoder: JSONEncoder
    private let keychain = KeychainService.shared

    private init() {
        let config = URLSessionConfiguration.default
        config.waitsForConnectivity = true
        config.timeoutIntervalForRequest = 30
        self.session = URLSession(configuration: config)

        self.decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601

        self.encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
    }

    // -- auth --

    func unlock(serverURL: String, password: String) async throws -> UnlockResponse {
        try await keychain.saveServerURL(serverURL)
        struct Body: Encodable { let password: String }
        let resp: UnlockResponse = try await request("POST", path: "/auth/unlock", body: Body(password: password), authed: false)
        try await keychain.saveSessionToken(resp.token)
        return resp
    }

    func authStatus() async throws -> AuthStatusResponse {
        try await request("GET", path: "/auth/status", authed: true)
    }

    func lock() async throws {
        try await requestVoid("POST", path: "/auth/lock", authed: true)
        try? await keychain.deleteSessionToken()
    }

    // -- library --

    func library() async throws -> LibraryResponse {
        try await request("GET", path: "/library", authed: true)
    }

    func category(id: Int) async throws -> CategoryDetail {
        try await request("GET", path: "/categories/\(id)", authed: true)
    }

    // -- stories --

    func story(id: Int) async throws -> StoryDetail {
        try await request("GET", path: "/stories/\(id)", authed: true)
    }

    func storyVersion(storyId: Int, version: Int) async throws -> StoryVersionDetail {
        try await request("GET", path: "/stories/\(storyId)/versions/\(version)", authed: true)
    }

    // -- internals --

    private func request<T: Decodable>(_ method: String, path: String, authed: Bool) async throws -> T {
        return try await request(method, path: path, body: Optional<EmptyBody>.none, authed: authed)
    }

    private func request<T: Decodable, B: Encodable>(_ method: String, path: String, body: B?, authed: Bool) async throws -> T {
        let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
        do { return try decoder.decode(T.self, from: data) } catch { throw APIError.decoding(error) }
    }

    private func requestVoid(_ method: String, path: String, authed: Bool) async throws {
        let req = try await buildRequest(method: method, path: path, body: Optional<EmptyBody>.none, authed: authed)
        let (data, response) = try await transport(req)
        try validate(response: response, data: data)
    }

    private func buildRequest<B: Encodable>(method: String, path: String, body: B?, authed: Bool) async throws -> URLRequest {
        guard let serverURL = try await keychain.loadServerURL() else { throw APIError.notConfigured }
        guard var components = URLComponents(string: serverURL) else { throw APIError.invalidURL }
        components.path = (components.path.hasSuffix("/") ? String(components.path.dropLast()) : components.path) + "/api/v1" + path
        guard let url = components.url else { throw APIError.invalidURL }

        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if body != nil {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = try encoder.encode(body)
        }
        if authed {
            guard let token = try await keychain.loadSessionToken() else { throw APIError.unauthorized }
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        return req
    }

    private func transport(_ req: URLRequest) async throws -> (Data, URLResponse) {
        do { return try await session.data(for: req) }
        catch { throw APIError.transport(error) }
    }

    private func validate(response: URLResponse, data: Data) throws {
        guard let http = response as? HTTPURLResponse else { throw APIError.server(status: -1, body: nil) }
        if http.statusCode == 401 { throw APIError.unauthorized }
        if !(200..<300).contains(http.statusCode) {
            let body = String(data: data, encoding: .utf8)
            throw APIError.server(status: http.statusCode, body: body)
        }
    }

    private struct EmptyBody: Encodable {}
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "Add FabulisAPIClient: URLSession-backed API + Keychain auth"
```

---

### Task 7: AppState

**Files:**
- Create: `client/Fabulis/State/AppState.swift`

- [ ] **Step 1: Create the file**

```swift
import Foundation
import Observation

@MainActor
@Observable
final class AppState {
    enum Phase: Equatable {
        case loading
        case needsOnboarding
        case needsAuth          // we have a server URL but the token is invalid
        case ready
    }

    var phase: Phase = .loading

    private let keychain = KeychainService.shared
    private let api = FabulisAPIClient.shared

    /// Decide initial state on launch, or after returning to foreground.
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
            // Network errors keep us in .ready; per-view fetches will show errors.
            phase = .ready
        }
    }

    func didCompleteOnboarding() { phase = .ready }
    func didReauthenticate() { phase = .ready }

    /// Manual lock from Settings.
    func lock() async {
        try? await api.lock()
        phase = .needsAuth
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Fabulis/State/AppState.swift
git commit -m "Add AppState: launch + foreground auth state machine"
```

---

## Section C — App shell

### Task 8: FabulisApp + ContentView

**Files:**
- Create: `client/Fabulis/FabulisApp.swift`
- Create: `client/Fabulis/ContentView.swift`

- [ ] **Step 1: `FabulisApp.swift`**

```swift
import SwiftUI

@main
struct FabulisApp: App {
    @State private var appState = AppState()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(appState)
                .task { await appState.bootstrap() }
        }
    }
}
```

- [ ] **Step 2: `ContentView.swift`**

```swift
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
            case .ready:
                LibraryView()
            }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await appState.bootstrap() } }
        }
    }
}
```

- [ ] **Step 3: Commit (app won't build yet — `OnboardingView`, `UnlockPromptView`, `LibraryView` come next)**

```bash
git add client/Fabulis/FabulisApp.swift client/Fabulis/ContentView.swift
git commit -m "Add app entry + ContentView routing on AppState"
```

---

## Section D — Onboarding + auth prompts

### Task 9: OnboardingView

**Files:**
- Create: `client/Fabulis/Views/Onboarding/OnboardingView.swift`

- [ ] **Step 1: Create the file**

```swift
import SwiftUI

struct OnboardingView: View {
    @Environment(AppState.self) private var appState
    @State private var serverURL: String = "http://"
    @State private var password: String = ""
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @FocusState private var focused: Field?

    enum Field { case url, password }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 32) {
                    VStack(spacing: 12) {
                        Image(systemName: "book.pages.fill")
                            .font(.system(size: 64))
                            .foregroundStyle(.tint)
                        Text("Fabulis")
                            .font(.largeTitle.bold())
                        Text("Connect to your Fabulis server")
                            .font(.title3)
                            .foregroundStyle(.secondary)
                    }
                    .padding(.top, 40)

                    VStack(alignment: .leading, spacing: 16) {
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Server URL").font(.headline)
                            TextField("http://hostname:5288", text: $serverURL)
                                .textFieldStyle(.roundedBorder)
                                .keyboardType(.URL)
                                .textContentType(.URL)
                                .autocorrectionDisabled()
                                .textInputAutocapitalization(.never)
                                .focused($focused, equals: .url)
                        }
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Vault password").font(.headline)
                            SecureField("Vault password", text: $password)
                                .textFieldStyle(.roundedBorder)
                                .textContentType(.password)
                                .focused($focused, equals: .password)
                        }
                        if let errorMessage {
                            Text(errorMessage).font(.caption).foregroundStyle(.red)
                        }
                    }
                    .padding(.horizontal)

                    Button {
                        Task { await submit() }
                    } label: {
                        Group {
                            if isSubmitting { ProgressView() }
                            else { Text("Connect") }
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(!canSubmit)
                    .padding(.horizontal)
                }
            }
        }
        .onAppear { focused = .url }
    }

    private var canSubmit: Bool {
        !isSubmitting
            && !serverURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && serverURL != "http://"
            && !password.isEmpty
    }

    private func submit() async {
        errorMessage = nil
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            _ = try await FabulisAPIClient.shared.unlock(
                serverURL: serverURL.trimmingCharacters(in: .whitespacesAndNewlines),
                password: password)
            appState.didCompleteOnboarding()
        } catch APIError.unauthorized {
            errorMessage = "Wrong password."
        } catch let APIError.server(status, _) {
            errorMessage = "Server returned \(status)."
        } catch let APIError.transport(err) {
            errorMessage = "Could not reach the server: \(err.localizedDescription)"
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add client/Fabulis/Views/Onboarding/OnboardingView.swift
git commit -m "Add OnboardingView: server URL + vault password"
```

---

### Task 10: UnlockPromptView (re-prompt after auto-lock)

**Files:**
- Create: `client/Fabulis/Views/Auth/UnlockPromptView.swift`

- [ ] **Step 1: Create the file**

```swift
import SwiftUI

struct UnlockPromptView: View {
    @Environment(AppState.self) private var appState
    @State private var password: String = ""
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @State private var serverURL: String?
    @FocusState private var focused: Bool

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                Image(systemName: "lock.fill")
                    .font(.system(size: 56))
                    .foregroundStyle(.tint)
                Text("Locked").font(.title.bold())
                if let serverURL { Text(serverURL).font(.callout).foregroundStyle(.secondary) }
                SecureField("Vault password", text: $password)
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .padding(.horizontal)
                if let errorMessage {
                    Text(errorMessage).font(.caption).foregroundStyle(.red)
                }
                Button { Task { await submit() } } label: {
                    Group { if isSubmitting { ProgressView() } else { Text("Unlock") } }
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(password.isEmpty || isSubmitting)
                .padding(.horizontal)
            }
            .padding(.top, 80)
            .task {
                serverURL = try? await KeychainService.shared.loadServerURL()
                focused = true
            }
        }
    }

    private func submit() async {
        guard let url = serverURL else { return }
        errorMessage = nil
        isSubmitting = true
        defer { isSubmitting = false }
        do {
            _ = try await FabulisAPIClient.shared.unlock(serverURL: url, password: password)
            appState.didReauthenticate()
        } catch APIError.unauthorized {
            errorMessage = "Wrong password."
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Build (now we have a coherent shell)**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -10
```

Expected: BUILD SUCCEEDED. (`LibraryView` is still missing, so this fails — actually wait, ContentView references it. Add a stub now.)

Stub at `client/Fabulis/Views/Library/LibraryView.swift`:

```swift
import SwiftUI

struct LibraryView: View {
    var body: some View { Text("Library").navigationTitle("Library") }
}
```

Re-run build. Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Auth/UnlockPromptView.swift client/Fabulis/Views/Library/LibraryView.swift
git commit -m "Add UnlockPromptView + Library stub; project builds for iOS"
```

---

## Section E — Read flows

### Task 11: LibraryView + CategoryCard

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`
- Create: `client/Fabulis/Views/Library/CategoryCard.swift`

- [ ] **Step 1: Replace `LibraryView.swift`**

```swift
import SwiftUI

struct LibraryView: View {
    @State private var categories: [CategorySummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            content
                .navigationTitle("Library")
                .toolbar {
                    ToolbarItem(placement: .topBarTrailing) {
                        NavigationLink(destination: SettingsView()) {
                            Image(systemName: "gear")
                        }
                    }
                }
                .navigationDestination(for: CategorySummary.self) { category in
                    CategoryView(categoryId: category.id, categoryName: category.name)
                }
                .task { await load() }
                .refreshable { await load() }
        }
    }

    @ViewBuilder
    private var content: some View {
        if isLoading && categories.isEmpty {
            ProgressView()
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .padding()
        } else if categories.isEmpty {
            ContentUnavailableView("Empty library", systemImage: "books.vertical",
                description: Text("Create categories from the web UI to see them here."))
        } else {
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200), spacing: 16)], spacing: 16) {
                    ForEach(categories) { category in
                        NavigationLink(value: category) {
                            CategoryCard(category: category)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding()
            }
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            let resp = try await FabulisAPIClient.shared.library()
            categories = resp.categories
        } catch APIError.unauthorized {
            // AppState foreground revalidation will catch this on next active tick
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}

extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool { lhs.id == rhs.id }
}
```

- [ ] **Step 2: Create `CategoryCard.swift`**

```swift
import SwiftUI

struct CategoryCard: View {
    let category: CategorySummary

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Image(systemName: "books.vertical.fill")
                .font(.title)
                .foregroundStyle(.tint)

            VStack(alignment: .leading, spacing: 4) {
                Text(category.name).font(.headline).lineLimit(1)
                if let latest = category.latestStoryTitle {
                    Text(latest).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                } else {
                    Text("No stories yet").font(.caption).foregroundStyle(.tertiary).italic()
                }
            }

            Spacer(minLength: 0)

            Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                .font(.caption2)
                .foregroundStyle(.tertiary)
        }
        .padding()
        .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading)
        .background(.thinMaterial)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(.quaternary, lineWidth: 1))
    }
}
```

- [ ] **Step 3: Build (still missing `CategoryView` and `SettingsView`)**

Add stubs:

`client/Fabulis/Views/Library/CategoryView.swift`:
```swift
import SwiftUI
struct CategoryView: View {
    let categoryId: Int
    let categoryName: String
    var body: some View { Text(categoryName).navigationTitle(categoryName) }
}
```

`client/Fabulis/Views/Settings/SettingsView.swift`:
```swift
import SwiftUI
struct SettingsView: View {
    var body: some View { Text("Settings").navigationTitle("Settings") }
}
```

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -10
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Views/Library/ client/Fabulis/Views/Settings/SettingsView.swift
git commit -m "LibraryView fetches /api/v1/library; CategoryView+Settings stubs"
```

---

### Task 12: CategoryView (full)

**Files:**
- Modify: `client/Fabulis/Views/Library/CategoryView.swift`

- [ ] **Step 1: Replace contents**

```swift
import SwiftUI

struct CategoryView: View {
    let categoryId: Int
    let categoryName: String

    @State private var detail: CategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let detail {
                if detail.stories.isEmpty {
                    ContentUnavailableView("No stories", systemImage: "doc.text",
                        description: Text("Stories saved into this category will appear here."))
                } else {
                    List(detail.stories) { story in
                        NavigationLink(value: story) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(story.title).font(.body)
                                Text(formatDate(story.createdAt))
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load category").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(categoryName)
        .navigationDestination(for: StorySummary.self) { story in
            StoryView(storyId: story.id, fallbackTitle: story.title)
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.category(id: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}

extension StorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StorySummary, rhs: StorySummary) -> Bool { lhs.id == rhs.id }
}

private func formatDate(_ date: Date) -> String {
    date.formatted(date: .abbreviated, time: .omitted)
}
```

- [ ] **Step 2: Add `StoryView` stub so build passes**

`client/Fabulis/Views/Story/StoryView.swift`:

```swift
import SwiftUI
struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String
    var body: some View { Text(fallbackTitle).navigationTitle(fallbackTitle) }
}
```

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -10
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Library/CategoryView.swift client/Fabulis/Views/Story/StoryView.swift
git commit -m "CategoryView fetches /api/v1/categories/{id}; StoryView stub"
```

---

### Task 13: StoryView (full) + StoryVersionView + StoryMessageView

**Files:**
- Modify: `client/Fabulis/Views/Story/StoryView.swift`
- Create: `client/Fabulis/Views/Story/StoryVersionView.swift`
- Create: `client/Fabulis/Views/Story/StoryMessageView.swift`

- [ ] **Step 1: `StoryView.swift`**

```swift
import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let detail {
                List {
                    Section {
                        VStack(alignment: .leading, spacing: 6) {
                            Text(detail.title).font(.title2.bold())
                            Text(detail.categoryName).font(.subheadline).foregroundStyle(.secondary)
                            Text("\(detail.versions.count) \(detail.versions.count == 1 ? "version" : "versions")")
                                .font(.caption).foregroundStyle(.tertiary)
                        }
                    }
                    if detail.versions.isEmpty {
                        Text("No versions yet").font(.subheadline).foregroundStyle(.secondary)
                    } else {
                        Section("Versions") {
                            ForEach(detail.versions) { version in
                                NavigationLink(value: version) {
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text("Version \(version.versionNumber)").font(.body.bold())
                                        Text(version.modelName).font(.caption).foregroundStyle(.secondary)
                                        Text(version.createdAt.formatted())
                                            .font(.caption2).foregroundStyle(.tertiary)
                                    }
                                }
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load story").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .navigationDestination(for: StoryVersionSummary.self) { version in
            StoryVersionView(storyId: storyId, version: version.versionNumber, modelName: version.modelName)
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.story(id: storyId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}

extension StoryVersionSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StoryVersionSummary, rhs: StoryVersionSummary) -> Bool { lhs.id == rhs.id }
}
```

- [ ] **Step 2: `StoryVersionView.swift`**

```swift
import SwiftUI

struct StoryVersionView: View {
    let storyId: Int
    let version: Int
    let modelName: String

    @State private var detail: StoryVersionDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let detail {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 12) {
                        ForEach(detail.messages) { message in
                            StoryMessageView(message: message)
                        }
                    }
                    .padding()
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load version").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle("Version \(version)")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Text(modelName).font(.caption).foregroundStyle(.secondary)
            }
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
```

- [ ] **Step 3: `StoryMessageView.swift`**

```swift
import SwiftUI

struct StoryMessageView: View {
    let message: StoryMessage

    private var roleLabel: String {
        switch message.role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    private var roleColor: Color {
        switch message.role {
        case .prompt: return .secondary
        case .response: return .accentColor
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(roleLabel.uppercased())
                .font(.caption2.bold())
                .foregroundStyle(roleColor)
            Text(message.content)
                .font(.body)
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(message.role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }
}
```

- [ ] **Step 4: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -10
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Story/
git commit -m "StoryView + StoryVersionView + StoryMessageView read flows"
```

---

### Task 14: SettingsView (lock + server URL display)

**Files:**
- Modify: `client/Fabulis/Views/Settings/SettingsView.swift`

- [ ] **Step 1: Replace contents**

```swift
import SwiftUI

struct SettingsView: View {
    @Environment(AppState.self) private var appState
    @State private var serverURL: String = ""
    @State private var isLocking = false

    var body: some View {
        Form {
            Section("Server") {
                LabeledContent("URL", value: serverURL)
            }
            Section("Vault") {
                Button(role: .destructive) {
                    Task {
                        isLocking = true
                        await appState.lock()
                        isLocking = false
                    }
                } label: {
                    HStack {
                        Image(systemName: "lock.fill")
                        Text("Lock vault")
                    }
                }
                .disabled(isLocking)
            }
        }
        .navigationTitle("Settings")
        .task {
            serverURL = (try? await KeychainService.shared.loadServerURL()) ?? ""
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -10
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Settings/SettingsView.swift
git commit -m "SettingsView: server URL display + Lock vault button"
```

---

## Section F — Mac Catalyst + final verification

### Task 15: Build for Mac Catalyst

- [ ] **Step 1: Catalyst build**

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=macOS,variant=Mac Catalyst' -configuration Debug build CODE_SIGNING_ALLOWED=NO 2>&1 | tail -15
```

Expected: BUILD SUCCEEDED. If a Catalyst-specific symbol is missing (e.g., a UIKit-only API), fix it inline; the only place this is likely is `topBarTrailing` toolbar placement which works on Catalyst too. If `topBarTrailing` fails, change to `.primaryAction`.

- [ ] **Step 2: Commit any Catalyst fixes (if needed)**

```bash
git add -A client/
git commit -m "Catalyst build fixes" --allow-empty
```

---

### Task 16: End-to-end manual smoke (developer)

This is the developer's hands-on validation. The agent cannot drive a simulator launch with credential entry.

- [ ] **Step 1: Start the dev server**

```bash
dotnet run --project src/Fabulis.Server &
```

Wait for "Now listening on: http://localhost:5288".

- [ ] **Step 2: Open the project in Xcode**

```bash
open client/Fabulis.xcodeproj
```

In Xcode:
1. Pick the Fabulis scheme + an iPhone Simulator destination, run.
2. Onboarding screen appears. Server URL: `http://localhost:5288`. Password: your vault password. Tap **Connect**.
3. Library appears with categories. Tap one → category page with stories. Tap one → story page with versions. Tap one → version reader with messages.
4. Tap gear → Settings shows the server URL. Tap **Lock vault** → returns to UnlockPromptView.
5. Re-enter the password → returns to library.

- [ ] **Step 3: Repeat with Mac Catalyst destination**

In Xcode, switch destination to `My Mac (Designed for iPad)` → actually choose `My Mac (Mac Catalyst)`. Run. Same flow.

- [ ] **Step 4: Auto-lock interaction**

Open Settings in the running app, set autolock to 1 minute via the Blazor browser UI (`http://localhost:5288/settings`). Wait > 1 minute without interacting with the iOS app. Bring it to foreground → ContentView should detect 401 from `authStatus()` and show UnlockPromptView.

- [ ] **Step 5: Stop the dev server**

```bash
kill %1
```

- [ ] **Step 6: Phase 2 wrap-up commit**

```bash
git commit --allow-empty -m "Phase 2 complete: native client shell + read flows

iOS + iPadOS + Mac Catalyst SwiftUI client (bundle ID
AchatesSoftware.Fabulis preserved from first-draft → existing App
Store Connect record + TestFlight pipeline carry over).

Onboarding (server URL + password) → token in Keychain → library
browse → category → story → version reader. UnlockPromptView re-prompts
when foreground revalidation detects an expired token. Settings has
a Lock button. Phase 3 (drafts + SSE streaming) and Phase 4
(parity + Blazor retirement) still pending."
```

---

## Self-review notes

- **Spec coverage.** Every Phase 2 deliverable from the architecture doc has a task: revive Xcode project ✓ (Task 1), onboarding ✓ (Task 9), library browse ✓ (Tasks 11–12), story view ✓ (Tasks 12–13), Mac Catalyst destination ✓ (Tasks 1, 15), foreground revalidation ✓ (Task 7 + Task 8 ContentView), TestFlight build of v2.0.0 ✓ (Task 1 marketing-version bump). Settings is intentionally minimal in this phase (just Lock + URL display) — full settings UI is Phase 4 work since it depends on the model picker.
- **Type consistency.** `CategorySummary`, `StorySummary`, `StoryVersionSummary` all conform to `Hashable` (declared in their consuming view files); each is used as a `navigationDestination` value type. `MessageRole` matches the server's enum string values exactly (`"Prompt"`, `"Response"`).
- **Placeholder scan.** Every code block is complete and runnable. No TODO / TBD.
- **Risks.** (1) `topBarTrailing` toolbar placement may not exist on Catalyst — the plan calls out the fix. (2) `Foundation.JSONDecoder` ISO-8601 strategy is strict; .NET's default ISO-8601 includes a sub-second precision that Foundation tolerates, but if a date fails to decode, switch to a custom DateFormatter and document. (3) `NSAllowsLocalNetworking = YES` permits broader access than a per-host exception; an audit-tightened version is Phase 4 work.
