# Backlog

Deferred work, consolidated from the "Out of scope" sections of the
phase plans in `docs/superpowers/plans/` and the architecture spec at
`docs/superpowers/specs/2026-05-02-hybrid-architecture-design.md`.

This is the single source of truth — when a deferred item gets shipped,
delete it from here.

## Functional gaps

### Reasoning chunks UI

The SSE protocol carries reasoning chunks (`reasoning: true` envelope
field) and the server emits them for thinking-capable models. The
client silently drops them in the `case "chunk":` branch of
`DraftView.runStream`. A collapsible "Thinking…" section in
`DraftMessageView` would surface them.

Originally deferred in the Phase 3 plan.

### Summary failure backoff

`SummaryService` retries a failed story on every sweep (~30s) with no
backoff. For a persistently failing story (bad model id, API outage)
this re-hits the model each cycle. Acceptable for single-user LAN use;
add exponential backoff / a max-attempts cap if it becomes noisy.

Originally deferred in the story-summaries plan
(`docs/superpowers/plans/2026-06-17-story-summaries.md`).

## Posture / hardening

### Scoped TLS posture

`client/Fabulis/Info.plist` currently uses `NSAllowsLocalNetworking =
YES`, which permits HTTP to *any* `.local` / private-IP host. Tighter
alternatives:

- A per-host exception in `NSExceptionDomains` (still HTTP, narrower
  trust).
- Terminate proper TLS at the server: self-signed cert + trust pinning
  in the client, OR use Tailscale / similar to get TLS + auth for free.

Originally deferred in the Phase 2 plan
(`docs/superpowers/plans/2026-05-02-phase2-native-client-shell.md`).

## Architectural assumptions

These are baked into the design. Changing any of them is a separate
sub-project, not a fix.

### Mac via Catalyst, not native macOS

One codebase, one App Store record, one bundle ID. A real `os(macOS)`
target would mean adding `#if os(macOS)` branches for menus, settings
windows, file pickers, sidebar styling, focus, keyboard shortcuts.
Worth doing only if Mac becomes the primary platform.

Source: architecture spec.

### Thin client, no offline read

Every navigation hits the server. Caching the library + recent stories
on-device (SwiftData mirror) would enable offline browsing but adds a
sync layer + means plaintext stories live outside the SQLCipher vault
under iOS Data Protection.

Source: architecture spec.

### Single-user, no signup

The vault password is the only credential. No user accounts, no
permission model.

Source: architecture spec.

### LAN-only server

No public-internet exposure design. Running on the open internet would
need TLS (see Scoped TLS posture above) plus a stronger auth posture
and probably rate limiting.

Source: architecture spec.
