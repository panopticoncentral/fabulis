# Vault auto-lock on idle

## Problem

Once the user unlocks the vault with their password, the `VaultService` holds the password in memory indefinitely. If the user walks away from the machine, anyone with access to the browser can read the library, change settings, or exfiltrate data. The only current way to re-protect the vault is the manual "Lock" button on [Settings](../../../src/Fabulis.Server/Components/Pages/Settings.razor).

## Goal

Automatically lock the vault after a configurable period of user inactivity. The user picks the timeout from a fixed preset list (including a "Never" option) in Settings.

## Non-goals

- No client-side activity tracking (mouse/keyboard). Server-observed requests are the activity signal.
- No cross-tab coordination. A single server-side flag flip locks all tabs; the next request in any tab redirects.
- No custom/user-entered timeout values. Presets only.
- No warning banner or "you will be locked in 30s" UX. Lock is silent; the next request redirects.
- No change to how the manual Lock button works.

## UX

**Settings → Security section** gets a new row above the existing "Lock vault" row:

| Row | Control |
|-----|---------|
| Auto-lock after | `<select>` with options: `1 minute`, `5 minutes`, `15 minutes`, `30 minutes`, `1 hour`, `Never` |

- Default selection when the setting has never been saved: **15 minutes**.
- Changing the dropdown saves immediately (no separate Save button), mirroring the existing "Assistant model" picker pattern. A brief confirmation line appears under the row ("Auto-lock updated.") and clears after a subsequent change.

When the timeout expires, the vault locks silently. The next navigation or form submission lands on `/unlock` via the existing `Vault.IsUnlocked` check in page `OnInitialized*` methods.

## Data model

New `AppSetting` row:

- Key: `AutoLockMinutes`
- Value: one of `"1"`, `"5"`, `"15"`, `"30"`, `"60"`, or `"never"`

If the row is missing, behavior is "15 minutes".

## Architecture

### VaultService additions

[VaultService.cs](../../../src/Fabulis.Server/Data/VaultService.cs) gains:

```csharp
public DateTime LastActivityAt { get; private set; }
public TimeSpan? AutoLockTimeout { get; private set; }  // null = never

public void RecordActivity();                     // sets LastActivityAt = UtcNow
public void ConfigureAutoLock(int? minutes);      // null => AutoLockTimeout = null; else TimeSpan.FromMinutes(minutes)
```

- `Unlock(...)` sets `LastActivityAt = UtcNow` and leaves `AutoLockTimeout` unset (the Unlock page configures it after loading the setting).
- `Lock()` clears `AutoLockTimeout` (so it is re-loaded on the next unlock).

### Activity middleware

A small middleware registered in [Program.cs](../../../src/Fabulis.Server/Program.cs) before `UseAntiforgery`:

- If `Vault.IsUnlocked` and the request path does not start with `/_blazor`, `/_framework`, `/_content`, or the mapped static-assets prefix, call `Vault.RecordActivity()`.
- The SignalR circuit path (`/_blazor`) is excluded deliberately: the circuit keeps sending messages even when the user is idle, so counting it would defeat the purpose.

**What this captures:** full-page navigations (every cross-page nav in this app uses `Nav.NavigateSafe` which forces a full reload) and form POSTs submitted by non-interactive pages (e.g. `/unlock`).

**What this does not capture:** button clicks, form submits, and input changes that happen inside a page rendered `@rendermode InteractiveServer` — those flow over the circuit, not HTTP. This is consistent with the chosen "server-side only: any page navigation" activity definition. An interactive page with no navigations for longer than the timeout will lock. The generation-stream `RecordActivity()` hook (below) covers the main long-running interactive case.

### AutoLockService background worker

New `AutoLockService : BackgroundService` in `src/Fabulis.Server/Data/`:

- Ticks every 15 seconds.
- On each tick: if `Vault.IsUnlocked` and `Vault.AutoLockTimeout is { } timeout` and `DateTime.UtcNow - Vault.LastActivityAt > timeout`, call `Vault.Lock()`.
- Registered via `builder.Services.AddHostedService<AutoLockService>()`.

### Unlock flow

[Unlock.razor](../../../src/Fabulis.Server/Components/Pages/Unlock.razor) is updated so that, on successful `EnsureCreatedAsync` / schema update, it reads the `AutoLockMinutes` setting from the DB and calls `Vault.ConfigureAutoLock(...)` before navigating to `/library`. Parsing:

- Missing row → `ConfigureAutoLock(15)`
- `"never"` → `ConfigureAutoLock(null)`
- Any integer in {1, 5, 15, 30, 60} → `ConfigureAutoLock(value)`
- Anything else → treat as 15 (defensive).

### Settings UI

[Settings.razor](../../../src/Fabulis.Server/Components/Pages/Settings.razor) Security section gains an "Auto-lock after" row:

- Reads the current `AutoLockMinutes` value in `OnInitializedAsync`.
- On change: persist to `AppSettings` (upsert pattern already used for `AssistantModel`) and call `Vault.ConfigureAutoLock(...)`.

### Long-running operations

Story generation streams responses via [OpenRouterService](../../../src/Fabulis.Server/Data/OpenRouterService.cs). Per the brainstorming discussion, the streaming path will call `Vault.RecordActivity()` on each received chunk (injected `VaultService` into `OpenRouterService`) so a long generation does not lock the vault mid-stream. This only touches the streaming path; the non-streaming `ChatAsync` path is unaffected.

## Data flow

1. User enters password on `/unlock` → `Vault.Unlock(password)` → DB opens → load `AutoLockMinutes` → `Vault.ConfigureAutoLock(...)` → navigate to `/library`.
2. Each non-asset HTTP request → middleware → `Vault.RecordActivity()`.
3. `AutoLockService` tick every 15s: idle longer than timeout? → `Vault.Lock()`.
4. Next request → page's `Vault.IsUnlocked` check → redirect to `/unlock`.
5. User changes "Auto-lock after" in Settings → DB upsert + `Vault.ConfigureAutoLock(...)` applied immediately.

## Testing

Manual verification (no automated tests exist in this project today):

- Set auto-lock to 1 minute, leave the app idle, confirm it redirects to `/unlock` on next click after ≥1 minute.
- Set to Never, leave idle for several minutes, confirm it stays unlocked.
- While a story is streaming, confirm the timer is kept fresh (set timeout to 1 min, run a >1 min generation, confirm no lock mid-stream).
- Change the setting while unlocked; confirm the new timeout takes effect without needing to re-unlock.
- Lock manually via the existing Lock button; confirm `AutoLockTimeout` is cleared and re-loaded on the next unlock.

## Follow-ups (out of scope)

- Countdown / pre-lock warning.
- Client-side mouse/keyboard activity tracking for "still reading a long story" cases beyond generation streams.
- Lock-on-browser-close / lock-on-tab-close.
