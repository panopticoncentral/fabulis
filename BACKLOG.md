# Backlog

Deferred work, consolidated from the "Out of scope" sections of the
phase plans in `docs/superpowers/plans/` and the architecture spec at
`docs/superpowers/specs/2026-05-02-hybrid-architecture-design.md`.

This is the single source of truth — when a deferred item gets shipped,
delete it from here.

## Functional gaps

### Prompt → draft conversion

Prompts can be defined and viewed (own tab, category-grouped, dedicated
editor), but there is no way to turn a prompt into a draft yet — i.e.
seed a new draft's conversation from a prompt's user-side messages and
hand it to a storyteller to generate from. Needs a "Start draft from
prompt" action plus server support to materialize a `Draft` (and its
`DraftMessage`s) from a `Prompt`.

Originally deferred in the Prompts category plan
(`docs/superpowers/plans/2026-06-03-prompts-category.md`).

### Reasoning chunks UI

The SSE protocol carries reasoning chunks (`reasoning: true` envelope
field) and the server emits them for thinking-capable models. The
client silently drops them in the `case "chunk":` branch of
`DraftView.runStream`. A collapsible "Thinking…" section in
`DraftMessageView` would surface them.

Originally deferred in the Phase 3 plan.

### Streaming-resume on reconnect

If the SSE connection drops mid-generation (long backgrounding, network
blip), the client can't re-subscribe to the live stream. The server's
cancellation handler still persists whatever streamed before the drop,
so the partial response shows up on next draft open — but the live
"watch it finish typing" experience is lost.

Would need: a `GET /api/v1/drafts/{id}/stream` endpoint that tails the
in-progress generation, plus client logic to detect a dropped stream
and re-subscribe instead of giving up.

Originally deferred in the Phase 3 plan.

### Background narration playback

Narration stops when you leave the story or draft view. A global
mini-player + `AVAudioSession.Category.playback` survival across
backgrounding, plus `MPNowPlayingInfoCenter` /
`MPRemoteCommandCenter` wiring for lock-screen controls, would let
you keep listening while browsing the library or with the screen
off.

Originally deferred in the narration v1 spec at
`docs/superpowers/specs/2026-05-26-narration-design.md`.

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
