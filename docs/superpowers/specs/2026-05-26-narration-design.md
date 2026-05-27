# Narration via Kokoro TTS

Play back stories and drafts as audio, using a Kokoro-FastAPI server
(Remsky) running on the same machine as the Fabulis server. A global
voice and speed live in app settings; per-view playback controls
(play/pause, ±10s, "play from here") drive synthesis-on-demand for
the response bubbles only.

## Goals

- Listen to any saved story version or any saved-message draft.
- One global voice and one global speed in Settings — no per-story
  overrides.
- Standard playback controls: play, pause, back 10s, forward 10s.
- "Play from here" on any response bubble, including mid-session.
- Narrate only response bubbles. Prompts and metadata stay silent.
- Narration is optional infrastructure. If Kokoro isn't configured or
  is unreachable, the rest of the app behaves exactly as today —
  narration UI simply doesn't appear.

## Non-goals (deferred)

- **Background / lock-screen playback.** Audio stops when you leave
  the story or draft view. No `MPNowPlayingInfoCenter` /
  `MPRemoteCommandCenter` wiring, no global mini-player. Likely a
  follow-up; called out in BACKLOG once shipped.
- **Per-story voice overrides.** Global only.
- **Cross-bubble seek by ±10s.** The seek buttons clamp at the
  current bubble's bounds; they don't auto-advance.
- **Server-side audio caching.** All caches are in-memory, scoped to
  the open view. Re-narration re-synthesises.
- **Narration during draft streaming.** Only saved messages are
  playable; the live streaming bubble is excluded.

## Architecture

```
┌──────────────────┐  HTTP+session   ┌──────────────────────┐  HTTP   ┌──────────┐
│ SwiftUI client   │ ───────────────▶│ Fabulis.Server       │ ──────▶ │ Kokoro   │
│ (NarrationPlayer │  /api/v1/       │  KokoroService       │  /v1/   │ FastAPI  │
│  + UI controls)  │  narration/...  │  NarrationEndpoints  │  audio/ │          │
└──────────────────┘                 └──────────────────────┘  speech └──────────┘
```

The client never talks to Kokoro directly. The Fabulis server proxies
all narration traffic, in the same pattern as `OpenRouterService`:

- Kokoro URL stored as an `AppSetting`, editable at runtime.
- Markdown stripping happens server-side before TTS.
- All narration endpoints sit under `RequireSession()`.

## Server

### New code

**`src/Fabulis.Server/Data/KokoroService.cs`** — singleton, takes
`IHttpClientFactory` and `IServiceProvider`. Reads the current
`KokoroBaseUrl` from the DB on each call (so PUT settings takes
effect without a restart) by creating a short-lived scope inside
the service. Gets a fresh `HttpClient` from the factory per call.
Singleton lifetime is required because the voices-list and probe
caches live on the instance — a scoped service would discard them
between requests.

```csharp
public sealed class KokoroService
{
    Task<IReadOnlyList<KokoroVoice>> ListVoicesAsync(CancellationToken ct);
    Task<byte[]> SynthesizeAsync(string text, string voice, double speed, CancellationToken ct);
    Task<bool> ProbeAsync(CancellationToken ct);   // GET /health, 1.5s timeout
}
```

- `SynthesizeAsync` POSTs `/v1/audio/speech` with
  `{model: "kokoro", input, voice, response_format: "mp3", speed}`
  and returns the response body as `byte[]`.
- Internal 5-minute cache for the voices list. Internal 30-second
  cache for the probe result, invalidated when `KokoroBaseUrl`
  changes via PUT settings.
- Timeouts: 60s on `SynthesizeAsync`, 1.5s on `ProbeAsync`, default
  on `ListVoicesAsync`.

**`src/Fabulis.Server/Data/MarkdownStripper.cs`** — pure static util.
One method: `string ToPlainText(string markdown)`. Behaviour:

| Markdown | Output |
| --- | --- |
| `**bold**`, `*em*`, `_em_` | unwrapped |
| `# Heading` | "Heading" (newline-separated) |
| `[link](http://x)` | `link` |
| `` ``` … ``` `` fenced code | dropped entirely |
| `` `inline` `` code | unwrapped |
| `> quote` | "quote" |
| `<tag>…</tag>` HTML | tags stripped, contents kept |
| `[^1]` footnote refs | dropped |
| Adjacent whitespace runs | collapsed to a single space |

Implement using `Markdig` with a plain-text renderer
(`NormalizeRenderer` from `Markdig.Renderers.Normalize` or a small
custom plaintext renderer). Add `Markdig` as a `PackageReference`
on `Fabulis.Server` if it isn't already present. The behaviour table
above is the contract; the renderer choice is an implementation
detail.

**`src/Fabulis.Server/Api/NarrationEndpoints.cs`** — new endpoint
group:

```
GET  /api/v1/narration/voices
POST /api/v1/narration/synthesize
```

`GET /voices` → 200 JSON:
```json
{ "voices": [
    { "id": "af_bella", "displayName": "Bella (American Female)", "language": "en-us" }
]}
```
- 503 if Kokoro is unreachable.

`POST /synthesize` body:
```json
{ "text": "It was a dark and stormy night.", "voice": "af_bella", "speed": 1.0 }
```
- `text` required, non-empty after `MarkdownStripper.ToPlainText` +
  trim, ≤ 8000 chars. 400 otherwise.
- `voice` and `speed` optional. Fall back to the `NarrationVoice` /
  `NarrationSpeed` `AppSetting` values. If `voice` is still unset
  after defaulting, return 400 `{ "error": "no voice configured" }`
  — the client should not have offered narration UI in that state.
  If `speed` is unset after defaulting, use `1.0`.
- `speed` must be in `[0.5, 2.0]` after defaulting. 400 otherwise.
- Returns `audio/mpeg` with `Content-Length` set; body is MP3 bytes.
  Not streamed/chunked.
- 503 if Kokoro is unreachable; 502 if Kokoro returns 5xx; 504 on
  timeout. Upstream error bodies are logged but not surfaced to the
  client.

Both endpoints sit under `RequireSession()`.

### Settings changes

New `AppSetting` keys: `KokoroBaseUrl`, `NarrationVoice`,
`NarrationSpeed`. The `AppSettings` table is already a generic
key/value store, so no schema migration.

**`GET /api/v1/settings` extended:**
```json
{
  "apiKeyIsSet": true,
  "assistantModel": "...",
  "autoLockSelection": "15",
  "kokoroBaseUrlIsSet": true,
  "narrationVoice": "af_bella",
  "narrationSpeed": 1.0,
  "narrationAvailable": true
}
```
- `kokoroBaseUrlIsSet` follows `apiKeyIsSet` — never returns the URL
  itself.
- `narrationAvailable` is computed each GET via
  `KokoroService.ProbeAsync()` (result cached 30s server-side).
  False if no URL is set, the probe fails, or the probe times out.

**`PUT /api/v1/settings` extended** to accept `kokoroBaseUrl`,
`narrationVoice`, `narrationSpeed`. All optional. `kokoroBaseUrl`
must parse as a URL with scheme `http` or `https` (400 otherwise).
Setting it invalidates the probe cache so the next GET reflects
reality.

## Client

### Narration session model

Each view (`StoryView`, `DraftView`) owns its own
`NarrationPlayer`. When the view disappears, the player stops. There
is no cross-view player.

**`client/Fabulis/Services/NarrationPlayer.swift`** — new file,
`@Observable`, `NSObject` to satisfy `AVAudioPlayerDelegate`:

```swift
@Observable
final class NarrationPlayer: NSObject, AVAudioPlayerDelegate {
    enum State {
        case idle
        case preparing(bubbleId: Int)
        case playing(bubbleId: Int)
        case paused(bubbleId: Int)
    }

    private(set) var state: State = .idle
    private(set) var currentTime: TimeInterval = 0
    private(set) var duration: TimeInterval = 0
    var currentBubbleId: Int? { /* extracts from state */ }

    func start(bubbles: [(id: Int, text: String)], from bubbleId: Int)
    func togglePlayPause()
    func seek(by delta: TimeInterval)   // clamped to [0, duration]
    func jumpTo(bubbleId: Int)
    func stop()
}
```

Internals:

- One `AVAudioPlayer` for the active bubble.
- Single-slot prefetch cache: `(bubbleId, Data)` for the bubble
  immediately after the active one.
- One `Task` handle for in-flight synthesis; replaced on every
  transition that invalidates it. `start` / `jumpTo` / `stop` cancel
  the previous task.
- MP3 bytes written to `FileManager.default.temporaryDirectory` with
  a UUID name. Deleted on bubble swap or `stop()`.
- `currentTime` ticks via a `CADisplayLink` only while `.playing`,
  ~30 Hz. Stopped when `.paused` / `.idle`.
- On first `start()`, set `AVAudioSession.Category.playback` so
  silent-mode switch doesn't mute narration. Revert on `stop()`.

### Data flow

1. User taps "Play from here" on response bubble *k* in a list of
   response-only bubbles `[r1 … rN]`.
2. View calls `player.start(bubbles:, from: rk.id)`.
3. Player → `POST /api/v1/narration/synthesize` with `{text: rk.content}`.
4. Server → `MarkdownStripper.ToPlainText` → `KokoroService.SynthesizeAsync`
   → MP3 bytes back to client.
5. Player writes MP3 to a temp file, hands to `AVAudioPlayer`,
   transitions to `.playing(rk.id)`. UI: bubble *k* gets the playing
   border, scrolls into view, narration bar slides up.
6. **Prefetch:** while `rk` plays, the player POSTs `/synthesize`
   for `rk+1` in parallel. Result lands in the single-slot cache.
7. On `audioPlayerDidFinishPlaying`, the player swaps to `rk+1` from
   the cache (or transitions to `.preparing(rk+1.id)` if the prefetch
   hasn't landed yet). Then prefetches `rk+2`.

**Pause / resume** — local to `AVAudioPlayer`. No server traffic.

**Back / forward 10s** — `player.currentTime ± 10`, clamped to
`[0, duration]`. Does *not* advance to a neighbouring bubble.

**Jump to bubble** (context menu while `state != .idle`) — cancels
current synthesis if `.preparing`, discards prefetch, kicks off the
start flow for the new bubble.

**Stop / leave view** — `.onDisappear` calls `player.stop()`, which
cancels in-flight synthesis, releases `AVAudioPlayer`, drops the
cache, and transitions to `.idle`.

### UI

**`client/Fabulis/Views/Narration/NarrationBar.swift`** — new file.
Pinned to the bottom of the host view via `.safeAreaInset(edge: .bottom)`,
visible only when `player.state != .idle`.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⏪10   ▶/⏸   ⏩10        Bubble 3 / 12 · 0:42 / 1:18      ✕    │
└──────────────────────────────────────────────────────────────────┘
```

- `✕` calls `player.stop()`.
- During `.preparing`, play/pause shows a small `ProgressView` and
  is disabled.
- Bubble counter uses the **absolute** 1-based index of the active
  bubble in the response-only list passed to `start()`, regardless
  of where the user pressed "Play from here". So starting at bubble
  3 of 12 shows "3 / 12" (not "1 / 10"); finishing 3 advances to
  "4 / 12".

**`StoryView` / `DraftView` additions:**

- `@State private var player = NarrationPlayer()`.
- `@State private var narrationAvailable = false` — set from the
  settings fetch on `.task`.
- `.safeAreaInset(edge: .bottom) { if player.state != .idle { NarrationBar(player: player) } }`.
- Pass `currentlyPlayingId: player.currentBubbleId` (type `Int?`,
  nil when `state == .idle`) and `narrationAvailable: narrationAvailable`
  into each message view.
- `.onDisappear { player.stop() }`.
- `.onChange(of: player.currentBubbleId)` scrolls the playing bubble
  into view via the existing `ScrollPosition` machinery on `DraftView`
  / a `ScrollViewReader` on `StoryView`.

**`StoryMessageView` / `DraftMessageView` changes:**

- New params: `isCurrentlyPlaying: Bool`, `narrationAvailable: Bool`,
  `onPlayFromHere: () -> Void`.
- When `isCurrentlyPlaying`, overlay a 2pt `Color.accentColor`
  `RoundedRectangle(cornerRadius: 10)` stroke on top of the existing
  rounded rect.
- Context menu item, gated on `narrationAvailable && role == .response`:

  ```swift
  Button {
      onPlayFromHere()
  } label: {
      Label("Play from here", systemImage: "play.fill")
  }
  ```

  `StoryMessageView` has no context menu today — add one wrapping
  just this item. `DraftMessageView`'s existing `@ViewBuilder menu`
  param gains this item above Edit/Regenerate/Delete.

**`SettingsView.swift`** — new "Narration" section between
"Assistant model" and "Storyteller":

- **Kokoro server URL** — `TextField` + Save button. Same pattern as
  the OpenRouter API key, but the URL is not a secret so it's not
  `SecureField`. On save: `PUT /settings` with `kokoroBaseUrl`.
- **Voice** — `NavigationLink` to `NarrationVoicePickerView`. Label
  shows the current voice display name. Disabled with subtitle
  "Set Kokoro URL first" if `!kokoroBaseUrlIsSet`.
- **Speed** — `Slider(value: $speed, in: 0.5...2.0, step: 0.25)`,
  current value shown at right (e.g. `1.25×`). On change-end:
  `PUT /settings` with `narrationSpeed`.
- Footer "Narration server unreachable." appears when
  `kokoroBaseUrlIsSet && !narrationAvailable`.

**`client/Fabulis/Views/Settings/NarrationVoicePickerView.swift`** —
new file. Fetches `/api/v1/narration/voices`, renders a `List`
grouped by language, tap selects + `PUT /settings` + dismiss.
Mirrors `ModelPickerView`.

### API client additions

**`client/Fabulis/Services/FabulisAPIClient.swift`** — new methods:

- `func narrationVoices() async throws -> [NarrationVoice]`
- `func synthesize(text: String, voice: String?, speed: Double?) async throws -> Data`

Plus DTO updates in `client/Fabulis/Models/APIDtos.swift` to mirror
the extended `SettingsDto`.

## Error handling

- **Synthesis error mid-session** — player transitions to `.idle`;
  the narration bar briefly shows "Narration unavailable" for ~3s
  before sliding away. No automatic retry — narration is cheap to
  re-trigger manually.
- **`AVAudioPlayer` decode failure** — same as synthesis error.
- **`narrationAvailable` flips to false between page loads** — new
  sessions don't start; an in-progress session continues until its
  next failure.
- **Empty bubble after stripping** (e.g. response was just a code
  fence) — server returns 400; client treats it as a synthesis
  error. Loud-fail rather than silently skip.
- **Draft mutation mid-narration** (edit, regenerate, delete) — call
  `player.stop()` at the start of each existing mutation path in
  `DraftView`. No graceful continue.
- **Streaming draft response** — play UI never appears on the
  streaming-content bubble or the in-flight prompt bubble (synthetic
  ids `-1` / `-2` / `-3` are easy to exclude).
- **Kokoro slow / hangs** — 60s `CancellationToken` on
  `SynthesizeAsync` → server returns 504 → client treats as generic
  synthesis failure.
- **Settings changed mid-session** — already-loaded MP3s stay as
  they are; the new voice/speed applies on the next synthesis (next
  bubble or next jump).
- **Auto-lock** — next `/synthesize` returns 401 → existing
  `APIError.unauthorized` path → narration goes `.idle` → app's
  existing unauthorised handling sends the user to the unlock screen.

## Testing

### Server (xUnit)

- **`MarkdownStripper`** — table-driven, covering every row in the
  behaviour table above plus combinations (e.g. bold inside a
  heading, link inside a list).
- **`NarrationEndpoints.Synthesize`** — validation (missing text,
  empty-after-strip, oversize, bad speed) all return 400. Voice and
  speed default correctly from `AppSetting`. Upstream errors map
  503/502/504. `KokoroService` mocked via `HttpMessageHandler` stub.
- **`NarrationEndpoints.Voices`** — happy path returns normalised
  shape; 503 when Kokoro unreachable; 5-minute cache verified.
- **`SettingsEndpoints`** — GET surfaces the new fields; PUT updates
  them; invalid URL rejected; `narrationAvailable` reflects the
  mocked probe and is cached for 30s; PUT clears the probe cache.
- **`KokoroService`** — request body serialises correctly; non-200
  responses map to the documented status codes; probe timeout
  returns false.

### Client

- **`NarrationPlayer`** — with a fake synthesis closure (no real
  HTTP), exercise: `start` → `.preparing` → `.playing`;
  `togglePlayPause` round-trips; `seek` clamps at both ends;
  bubble-finish advances and triggers the next prefetch; `jumpTo`
  cancels in-flight + clears cache; `stop` from any state lands on
  `.idle`. `AVAudioPlayer` wrapped behind a small protocol so tests
  don't need real audio.
- No SwiftUI view tests — there's no view-test infra in the project
  today.

### Manual smoke list

- Voice picker populates from Kokoro.
- Play / pause / back-10 / forward-10 all behave on a multi-bubble
  story.
- "Play from here" mid-playback jumps cleanly.
- Kokoro offline → narration UI hidden in Settings, no "Play from
  here" menu item appears on bubbles.
- Navigate away mid-narration → audio stops.
- Auto-lock mid-narration → narration stops, returns to unlock.
- Editing a draft message mid-narration stops playback.

## Backlog impact

When background-playback ships, remove its entry from `BACKLOG.md`.
Add it now if shipping this design as v1:

```
### Background narration playback

Narration stops when you leave the story or draft view. A global
mini-player + `AVAudioSession.Category.playback` survival across
backgrounding, plus `MPNowPlayingInfoCenter` /
`MPRemoteCommandCenter` wiring for lock-screen controls, would let
you keep listening while browsing the library or with the screen
off. Touched by the v1 narration spec at
docs/superpowers/specs/2026-05-26-narration-design.md.
```
