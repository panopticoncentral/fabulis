# Fabulis

A personal story generator. The data + LLM proxy live in an ASP.NET Core
server; a native SwiftUI client (iPhone, iPad, Mac via Catalyst) talks to it
over an HTTP API on the local network.

## Stack

- **Server:** ASP.NET Core on .NET 10. EF Core with SQLite + SQLCipher
  (encrypted at rest). Solution: `Fabulis.slnx`. Source: `src/Fabulis.Server/`.
- **Client:** SwiftUI, iOS 18.5+ deployment target, Mac Catalyst destination
  on the same target. Bundle ID `AchatesSoftware.Fabulis`. Source:
  `client/Fabulis/`, Xcode project: `client/Fabulis.xcodeproj/`.
- **Wire format:** REST under `/api/v1/*` plus an SSE streaming endpoint for
  story generation (`POST /api/v1/drafts/{id}/messages` and
  `POST /api/v1/drafts/{id}/regenerate`).

## Build & run

Server:
```bash
dotnet build Fabulis.slnx
dotnet run --project src/Fabulis.Server
```
Listens on `http://localhost:5288`. The vault password is supplied at unlock
via `POST /api/v1/auth/unlock`; it is never stored on disk.

Client:
```bash
open client/Fabulis.xcodeproj
```
Build for an iOS Simulator destination or Mac Catalyst. Onboarding asks for
the server URL (e.g. `http://your-mac.local:5288`) and the vault password.

Bulk import / export (CLI, runs on the server machine — see
`src/Fabulis.Cli/README.md`):
```bash
dotnet run --project src/Fabulis.Cli -- export <destination>
dotnet run --project src/Fabulis.Cli -- import <source>
```

## Project structure

- `Fabulis.slnx` — solution file
- `src/Fabulis.Server/`
  - `Api/` — minimal-API endpoint groups (auth, library, story, settings,
    storyteller, drafts, models)
  - `Auth/` — `SessionTokenStore` + `RequireSession` endpoint filter
  - `Data/` — `FabulisDbContext`, entity types, `DraftService`,
    `OpenRouterService`, `VaultService`, `AutoLockService`
- `src/Fabulis.Cli/` — bulk import/export CLI; project-references the
  server for entity types and `FabulisDbContext`
- `client/Fabulis/`
  - `Models/APIDtos.swift` — Codable mirrors of server DTOs
  - `Services/` — `KeychainService` (serverURL + sessionToken),
    `FabulisAPIClient` (URLSession + SSE)
  - `State/AppState.swift` — auth state machine
  - `Views/` — `Onboarding/`, `Auth/`, `Library/`, `Story/`, `Draft/`,
    `Settings/`
- `docs/superpowers/` — design specs and implementation plans

## Architecture spec

`docs/superpowers/specs/2026-05-02-hybrid-architecture-design.md`

## Deferred work

`BACKLOG.md` at the repo root — single source of truth for items
deferred from each phase. When something gets shipped, delete its
entry there.
