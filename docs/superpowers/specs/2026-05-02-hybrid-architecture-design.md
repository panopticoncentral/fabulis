# Hybrid Architecture: Server Backend + Native iOS/Mac Frontend

## Context

Fabulis began on the `first-draft` branch as a SwiftUI iOS app that called OpenRouter directly and persisted stories with SwiftData. That was abandoned in favor of the current ASP.NET Core / Blazor Server web app on `main`, which centralizes data in an encrypted SQLite (SQLCipher) vault and proxies LLM calls through `OpenRouterService`.

The web frontend has stopped earning its keep: the user wants to use the app from phone, iPad, and a Mac on the same home network, and a browser is a worse experience on each of those than a native app would be. At the same time, the server still has real value — encrypted-at-rest storage, a single OpenRouter key, an auto-locking vault, and a uniform place to manage settings.

The decision is to **keep the server as the data and LLM backend** but **replace the Blazor UI with a native SwiftUI client** that talks to it over a new HTTP API. The existing `first-draft` Xcode project is revived in place, preserving its bundle ID (`AchatesSoftware.Fabulis`, team `5KAYG269JK`) so the existing App Store Connect record and TestFlight pipeline carry over. Mac is supported via Mac Catalyst on the same target.

## Foundational decisions (locked in)

- **Topology**: server runs on the home network (e.g., always-on Mac mini / NAS); phone, iPad, Mac Catalyst client all reach it on the LAN.
- **Mac**: Mac Catalyst on the iOS target. One codebase, one App Store Connect record, TestFlight covers both.
- **Source of truth**: thin client. Server owns all data; client holds nothing across launches except `serverURL` and a session token.
- **Auth**: opaque session token issued at unlock, stored in iOS Keychain. Tokens are invalidated when the vault auto-locks, so the client re-prompts for the password — preserving the auto-lock guarantee.
- **Blazor**: stays working through the migration. Deleted in the final phase, after the native app reaches parity.
- **Network**: HTTP allowed for v1 via a scoped Info.plist NSAppTransportSecurity exception for the user's home server hostname. TLS is a v2 concern.

## Architecture

```
┌─────────────────────────────────────────┐       ┌─────────────────────┐
│  iPhone / iPad / Mac (Catalyst)         │       │  ASP.NET Server     │
│  ─────────────────────────────────       │ HTTP  │  on home network    │
│  SwiftUI thin client                     │ ◄───► │                     │
│  Keychain: serverURL + sessionToken      │       │  /api/v1/* (REST    │
│  No SwiftData, no on-device persistence  │       │  + SSE for stream)  │
└─────────────────────────────────────────┘       │  Existing services  │
                                                  │  (DraftService,     │
                                                  │  OpenRouterService, │
                                                  │  VaultService, …)   │
                                                  │  SQLite + SQLCipher │
                                                  │  Blazor UI parallel │
                                                  └─────────────────────┘
```

## Server changes

New folder: `src/Fabulis.Server/Api/` — minimal-API endpoint groups, one file per resource, delegating to existing services.

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/auth/unlock` | Body `{password}` → `{token, expiresAt}` |
| `POST /api/v1/auth/lock` | Invalidate token, lock vault |
| `GET /api/v1/auth/status` | `{isUnlocked, autoLockMinutes}` (foreground revalidation) |
| `GET /api/v1/library` | Categories + stories tree |
| `GET /api/v1/categories/{id}` | One category w/ stories |
| `GET /api/v1/stories/{id}` | Latest version |
| `GET /api/v1/stories/{id}/versions/{v}` | Specific version |
| `POST /api/v1/drafts` | Create new draft |
| `GET /api/v1/drafts/{id}` | Draft state + messages |
| `POST /api/v1/drafts/{id}/messages` | **SSE response.** Streams chunks; persists assistant message as it streams |
| `POST /api/v1/drafts/{id}/save` | Convert draft → story version |
| `GET/PUT /api/v1/settings` | API key, model, autolock minutes |
| `GET /api/v1/storyteller` / `PUT /api/v1/storyteller` | Single configurable storyteller |
| `POST /api/v1/categories/import` | Multipart JSON upload |
| `GET /api/v1/categories/{id}/export` | JSON download |
| `GET /api/v1/models` | Pass-through to OpenRouter model list |

New file: `src/Fabulis.Server/Auth/SessionTokenStore.cs` — `ConcurrentDictionary<string, TokenInfo>` of opaque tokens (32-byte base64url). Cleared whenever `VaultService.Lock()` runs. Backed by a small `[Authorize]`-style endpoint filter that checks `Authorization: Bearer <token>` on `/api/v1/*` (excluding `/api/v1/auth/unlock`).

Modified: `src/Fabulis.Server/Data/VaultService.cs` — `Lock()` also invalidates all session tokens (inject `SessionTokenStore`).

Modified: `src/Fabulis.Server/Program.cs` — add `app.MapGroup("/api/v1")` with the new endpoint registrations and the session-token middleware. The existing `vault.RecordActivity()` middleware is extended to count API requests for auto-lock idle tracking.

Reuse (no changes): `DraftService`, `OpenRouterService` (including its `IAsyncEnumerable<StreamChunk>` streaming), `CategoryImportService`, `CategoryExportService`, `FabulisDbContext`, `AutoLockService`. Endpoints delegate to these the same way Razor components do today.

## Client (SwiftUI) changes

Reuse from `first-draft`:
- Xcode project (`Fabulis.xcodeproj`), bundle ID `AchatesSoftware.Fabulis`, team `5KAYG269JK`, asset catalog, entitlements scaffold
- View shells: `LibraryView`, `StorytellerCard`/`StorytellerDetailView`/`StorytellerEditorView`, `OnboardingView`, `StoryInputView`, `StorySegmentView`, `StorySessionView`, `ModelPickerView`, `SettingsView`

Replace:
- `Fabulis/Services/KeychainService.swift` → stores `serverURL` + `sessionToken` (key namespace becomes `com.fabulis.server`); no more OpenRouter API key on device
- `Fabulis/Services/OpenRouterService.swift` → `FabulisAPIClient` that targets the user's server; reuses the existing SSE-parsing logic (lines parsed from `URLSession.bytes(for:)`) since the server passes through OpenRouter's SSE format
- `Fabulis/Services/StorytellerDefaults.swift` → deleted; storyteller comes from the server
- `Fabulis/Models/*.swift` (currently SwiftData `@Model` types) → replaced with plain `Codable` DTO structs that mirror server response shapes

New:
- `ServerConfigViewModel` — first-launch flow: enter server URL (e.g., `http://nas.local:5000`), enter vault password, exchange for token, store both in Keychain
- Foreground revalidation on `scenePhase == .active` → `GET /api/v1/auth/status`; on 401, re-prompt for password
- Mac Catalyst destination on the iOS target. `Commands { ... }` block at the App level for menu bar (File > New Story, View > Library, etc.)
- `Info.plist` — `NSAppTransportSecurity` exception scoped to the user's chosen server hostname so HTTP works on the LAN

## Streaming-resume (free upgrade)

Today's Blazor app loses in-progress generations if the page reloads. The server already persists assistant content to `DraftMessage` as chunks arrive. The native client lets us close the loop: backgrounding the app mid-generation, switching apps, or reopening the draft from another device picks up where you left off, because re-subscribing to `/api/v1/drafts/{id}/messages` (or a `/stream` companion route) replays persisted chunks then continues live. Designed for in Phase 3.

## Phased delivery

This work is too large for one plan. It decomposes into four sub-projects, each gets its own spec → plan → execute cycle.

1. **Phase 1: Server API foundation** — `auth/unlock`, `SessionTokenStore`, the `[Authorize]` endpoint filter, read-only library/category/story/settings/storyteller endpoints. Blazor untouched. Verifiable end-to-end with `curl`. *This is the next plan after this design.*
2. **Phase 2: Native shell + read flows** — revive Xcode project, onboarding (server URL + password), library browse, story view, Mac Catalyst destination. No generation yet. TestFlight build of v2.0.0.
3. **Phase 3: Drafts + streaming** — server SSE endpoint for `POST /api/v1/drafts/{id}/messages`, native generation UX, streaming-resume on reconnect.
4. **Phase 4: Parity + Blazor retirement** — categories CRUD, import/export, storyteller editor, model picker. Then delete `src/Fabulis.Server/Components/`, `src/Fabulis.Server/wwwroot/`, related Razor wiring. Update `CLAUDE.md` to drop "web app" framing.

## Critical files to read before each phase

For Phase 1 specifically:
- `src/Fabulis.Server/Program.cs` — DI/middleware registration site
- `src/Fabulis.Server/Data/VaultService.cs` — extend `Lock()`, integrate token store
- `src/Fabulis.Server/Data/FabulisDbContext.cs` — entity shapes for DTO design
- `src/Fabulis.Server/Data/DraftService.cs` — confirm draft/message read API
- `src/Fabulis.Server/Data/OpenRouterService.cs` — `ChatStreamAsync()` shape for the eventual SSE endpoint
- `src/Fabulis.Server/Components/Pages/Unlock.razor` — current unlock flow as the contract for `/api/v1/auth/unlock`
- `src/Fabulis.Server/Components/Pages/Library.razor` (and `Settings.razor`) — current library/settings shape as the contract for the read endpoints

## Verification

Per-phase smoke tests; each phase is self-contained.

**Phase 1 (server API)**:
- `dotnet build Fabulis.slnx` succeeds.
- Browser flow at `/unlock` still works (Blazor regression check).
- `curl -X POST http://localhost:5000/api/v1/auth/unlock -d '{"password":"…"}'` returns a token.
- `curl -H "Authorization: Bearer …" http://localhost:5000/api/v1/library` returns categories+stories matching what `/library` shows in the browser.
- After `auto-lock` window elapses, the same `curl` returns 401.
- `curl -X POST .../api/v1/auth/lock` invalidates the token immediately.

**Phase 2 (native shell)**:
- App launches on Simulator and Mac Catalyst from a clean install.
- Onboarding accepts a `http://localhost:5000` server URL and the vault password, lands on the library screen.
- Library list and individual story view render data from the server.
- Backgrounding the app for longer than the auto-lock window and returning re-prompts for the password.
- TestFlight build uploads to the existing App Store Connect record without errors.

**Phase 3 (streaming)**:
- Streaming a generation in the native app produces incremental text identical to what the Blazor draft page produces for the same prompt.
- Killing and re-opening the app mid-generation resumes from where it left off (or surfaces the persisted partial content).
- Save-to-library flow produces a story version readable from both clients.

**Phase 4 (retirement)**:
- After deleting `Components/` + `wwwroot/`, `dotnet build` still succeeds and the server starts.
- All native app flows continue to work.
- `CLAUDE.md` updated to describe Fabulis as an "API + native client" app.

## Out of scope for this design

- TLS / HTTPS posture (deferred to v2; v1 ships HTTP on LAN with NSAppTransportSecurity exception)
- Multi-user / sign-up flows (single-user app, vault password is the only credential)
- Offline reading and on-device caching (revisit only if the user reports a real need)
- Real native macOS target (Catalyst is the v1 Mac story; a true `os(macOS)` target is a separate future spec)
- Public internet exposure of the server (LAN-only assumption)
