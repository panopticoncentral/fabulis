# Vault Auto-Lock — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock the vault automatically after a configurable period of server-observed inactivity, with an "Auto-lock after" dropdown in Settings that includes a Never option.

**Architecture:** `VaultService` (singleton) grows an activity timestamp and a nullable timeout. A small ASP.NET Core middleware records activity on every non-SignalR request. A `BackgroundService` polls every 15 seconds and calls `Lock()` when the idle window is exceeded. The timeout is persisted as an `AppSetting` row, loaded on unlock, and re-applied whenever the Settings dropdown changes. Streaming generations call `RecordActivity()` per chunk to avoid locking mid-stream.

**Tech Stack:** ASP.NET Core Blazor Server on .NET 10, Entity Framework Core, `IHostedService` / `BackgroundService`, `AppSetting` key-value table.

**Spec:** `docs/superpowers/specs/2026-04-24-vault-auto-lock-design.md`

**Note on testing:** This project has no test suite. Each implementation task ends with a `dotnet build` check to catch compile errors; the final task is manual verification in the browser.

---

## File Structure

One new file, four existing files modified.

- **Create** `src/Fabulis.Server/Data/AutoLockService.cs` — `BackgroundService` that polls and calls `Vault.Lock()` on idle expiry.
- **Modify** `src/Fabulis.Server/Data/VaultService.cs` — add `LastActivityAt`, `AutoLockTimeout`, `RecordActivity()`, `ConfigureAutoLock()`; make `Lock()` clear the timeout.
- **Modify** `src/Fabulis.Server/Program.cs` — register `AutoLockService`, add activity-recording middleware.
- **Modify** `src/Fabulis.Server/Components/Pages/Unlock.razor` — read `AutoLockMinutes` AppSetting and call `Vault.ConfigureAutoLock(...)` after a successful unlock.
- **Modify** `src/Fabulis.Server/Components/Pages/Settings.razor` — add "Auto-lock after" row to the Security section.
- **Modify** `src/Fabulis.Server/Data/OpenRouterService.cs` — inject `VaultService` and call `RecordActivity()` per streamed chunk.

---

### Task 1: Extend VaultService with activity tracking and auto-lock timeout

**Files:**
- Modify: `src/Fabulis.Server/Data/VaultService.cs`

- [ ] **Step 1: Replace the file contents**

Replace `src/Fabulis.Server/Data/VaultService.cs` with exactly this content:

```csharp
namespace Fabulis.Server.Data;

public class VaultService
{
    public bool IsUnlocked { get; private set; }
    public string? Password { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public TimeSpan? AutoLockTimeout { get; private set; }

    public void Unlock(string password)
    {
        Password = password;
        IsUnlocked = true;
        LastActivityAt = DateTime.UtcNow;
    }

    public void Lock()
    {
        Password = null;
        IsUnlocked = false;
        AutoLockTimeout = null;
    }

    public void RecordActivity()
    {
        if (IsUnlocked)
            LastActivityAt = DateTime.UtcNow;
    }

    public void ConfigureAutoLock(int? minutes)
    {
        AutoLockTimeout = minutes is int m && m > 0
            ? TimeSpan.FromMinutes(m)
            : null;
    }
}
```

Notes:
- `AutoLockTimeout == null` means "never lock".
- `Lock()` intentionally clears `AutoLockTimeout` so that the next unlock re-reads the setting fresh.
- `RecordActivity()` is a no-op when the vault is already locked, which is harmless if the middleware fires during the tiny window between `Lock()` and a redirect.

- [ ] **Step 2: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Data/VaultService.cs
git commit -m "Extend VaultService with activity tracking and auto-lock timeout"
```

---

### Task 2: Add the AutoLockService background worker

**Files:**
- Create: `src/Fabulis.Server/Data/AutoLockService.cs`

- [ ] **Step 1: Create the file**

Create `src/Fabulis.Server/Data/AutoLockService.cs` with exactly this content:

```csharp
using Microsoft.Extensions.Hosting;

namespace Fabulis.Server.Data;

public class AutoLockService(VaultService vault, ILogger<AutoLockService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (vault.IsUnlocked && vault.AutoLockTimeout is { } timeout)
                {
                    var idle = DateTime.UtcNow - vault.LastActivityAt;
                    if (idle > timeout)
                    {
                        logger.LogInformation("Auto-locking vault after {Idle} of inactivity (timeout {Timeout}).", idle, timeout);
                        vault.Lock();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-lock poll failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds (the service is not registered yet, but it compiles on its own).

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Data/AutoLockService.cs
git commit -m "Add AutoLockService background worker"
```

---

### Task 3: Wire up Program.cs (register hosted service + activity middleware)

**Files:**
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Register the hosted service**

In `src/Fabulis.Server/Program.cs`, find this line:

```csharp
builder.Services.AddSingleton<VaultService>();
```

Add the hosted-service registration immediately below it, so those two lines read:

```csharp
builder.Services.AddSingleton<VaultService>();
builder.Services.AddHostedService<AutoLockService>();
```

- [ ] **Step 2: Add the activity middleware**

In the same file, find this block:

```csharp
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
```

Replace it with:

```csharp
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var isInfra = path is not null && (
        path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase));

    if (!isInfra)
    {
        var vault = context.RequestServices.GetRequiredService<VaultService>();
        vault.RecordActivity();
    }

    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();
```

Notes:
- The middleware sits after `UseHttpsRedirection` and before `UseAntiforgery`, so static assets served by `MapStaticAssets` (mapped later) do not hit it — but even if they did, `RecordActivity()` is cheap and no-ops when locked.
- Excluded prefixes match the Blazor framework paths (`/_blazor` is the SignalR circuit, `/_framework` is runtime/assembly assets, `/_content` is RCL assets).

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Program.cs
git commit -m "Register AutoLockService and record activity on non-infra requests"
```

---

### Task 4: Load AutoLockMinutes after a successful unlock

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Unlock.razor`

- [ ] **Step 1: Add the load helper and call it from TryUnlock**

In `src/Fabulis.Server/Components/Pages/Unlock.razor`, find the `TryUnlock` method:

```csharp
    private async Task TryUnlock()
    {
        if (string.IsNullOrWhiteSpace(Password))
            return;

        Vault.Unlock(Password);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureSchemaUpdatedAsync();
            Nav.NavigateTo("/library");
        }
        catch (NavigationException)
        {
            throw;
        }
        catch
        {
            Vault.Lock();
            ErrorMessage = "Could not open the vault. Is the password correct?";
        }
    }
```

Replace it with:

```csharp
    private async Task TryUnlock()
    {
        if (string.IsNullOrWhiteSpace(Password))
            return;

        Vault.Unlock(Password);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureSchemaUpdatedAsync();
            await LoadAutoLockSettingAsync(db);
            Nav.NavigateTo("/library");
        }
        catch (NavigationException)
        {
            throw;
        }
        catch
        {
            Vault.Lock();
            ErrorMessage = "Could not open the vault. Is the password correct?";
        }
    }

    private async Task LoadAutoLockSettingAsync(FabulisDbContext db)
    {
        var setting = await db.AppSettings.FindAsync("AutoLockMinutes");
        Vault.ConfigureAutoLock(ParseAutoLockMinutes(setting?.Value));
    }

    internal static int? ParseAutoLockMinutes(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return null;

        if (int.TryParse(raw, out var parsed) && parsed is 1 or 5 or 15 or 30 or 60)
            return parsed;

        return 15;
    }
```

Notes:
- `ParseAutoLockMinutes` is `internal static` so the Settings page can reuse it without duplicating the whitelist.
- A missing row, malformed value, or out-of-list value all fall back to 15 minutes.
- `"never"` returns `null`, which `ConfigureAutoLock` interprets as "no timeout".

- [ ] **Step 2: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Unlock.razor
git commit -m "Load AutoLockMinutes and configure vault timeout on unlock"
```

---

### Task 5: Add the "Auto-lock after" row to Settings

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Settings.razor`

- [ ] **Step 1: Add the UI row in the Security section**

In `src/Fabulis.Server/Components/Pages/Settings.razor`, find the Security section:

```razor
<section class="settings-section">
    <h2>Security</h2>

    <div class="settings-item">
        <div>
            <div class="settings-label">Lock vault</div>
            <p class="settings-description">Lock the database and require the password to be re-entered.</p>
        </div>
        <EditForm Model="this" OnValidSubmit="LockVault" FormName="lockVault" style="margin: 0;">
            <button type="submit" class="btn btn-secondary">Lock</button>
        </EditForm>
    </div>
</section>
```

Replace it with:

```razor
<section class="settings-section">
    <h2>Security</h2>

    <div class="settings-item">
        <div>
            <div class="settings-label">Auto-lock after</div>
            <p class="settings-description">Automatically lock the vault after a period of inactivity.</p>
        </div>
        <select class="form-select" value="@AutoLockSelection" @onchange="OnAutoLockChanged">
            <option value="1">1 minute</option>
            <option value="5">5 minutes</option>
            <option value="15">15 minutes</option>
            <option value="30">30 minutes</option>
            <option value="60">1 hour</option>
            <option value="never">Never</option>
        </select>
    </div>

    @if (AutoLockSaved)
    {
        <p style="color: var(--gilt-deep); font-size: 0.85rem;">Auto-lock updated.</p>
    }

    <div class="settings-item">
        <div>
            <div class="settings-label">Lock vault</div>
            <p class="settings-description">Lock the database and require the password to be re-entered.</p>
        </div>
        <EditForm Model="this" OnValidSubmit="LockVault" FormName="lockVault" style="margin: 0;">
            <button type="submit" class="btn btn-secondary">Lock</button>
        </EditForm>
    </div>
</section>
```

- [ ] **Step 2: Add the code-behind state and handlers**

In the same file, find the field declarations near the top of `@code`:

```csharp
    private List<ModelInfo> Models { get; set; } = [];
    private bool IsLoadingModels { get; set; }
    private string? ModelsError { get; set; }
    private string? ModelSearch { get; set; }
    private string? SelectedModelId { get; set; }
```

Add these two fields immediately after them:

```csharp
    private string AutoLockSelection { get; set; } = "15";
    private bool AutoLockSaved { get; set; }
```

- [ ] **Step 3: Load the current value in OnInitializedAsync**

In the same file, find `OnInitializedAsync`:

```csharp
    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        var existingKey = await Db.AppSettings.FindAsync("OpenRouterApiKey");
        if (existingKey is not null)
        {
            ApiKeyPlaceholder = "••••••••  (key is set)";
        }

        var existingModel = await Db.AppSettings.FindAsync("AssistantModel");
        if (existingModel is not null)
        {
            CurrentAssistantModel = existingModel.Value;
        }
    }
```

Replace it with:

```csharp
    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        var existingKey = await Db.AppSettings.FindAsync("OpenRouterApiKey");
        if (existingKey is not null)
        {
            ApiKeyPlaceholder = "••••••••  (key is set)";
        }

        var existingModel = await Db.AppSettings.FindAsync("AssistantModel");
        if (existingModel is not null)
        {
            CurrentAssistantModel = existingModel.Value;
        }

        var existingAutoLock = await Db.AppSettings.FindAsync("AutoLockMinutes");
        AutoLockSelection = NormalizeAutoLock(existingAutoLock?.Value);
    }

    private static string NormalizeAutoLock(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return "never";

        if (int.TryParse(raw, out var parsed) && parsed is 1 or 5 or 15 or 30 or 60)
            return parsed.ToString();

        return "15";
    }
```

- [ ] **Step 4: Add the change handler**

In the same file, find `LockVault`:

```csharp
    private void LockVault()
    {
        Vault.Lock();
        Nav.NavigateSafe("/unlock");
    }
```

Insert the auto-lock handler immediately above it, so the two methods read:

```csharp
    private async Task OnAutoLockChanged(ChangeEventArgs e)
    {
        var raw = e.Value?.ToString();
        AutoLockSelection = NormalizeAutoLock(raw);

        var existing = await Db.AppSettings.FindAsync("AutoLockMinutes");
        if (existing is not null)
        {
            existing.Value = AutoLockSelection;
        }
        else
        {
            Db.AppSettings.Add(new AppSetting { Key = "AutoLockMinutes", Value = AutoLockSelection });
        }

        await Db.SaveChangesAsync();

        var minutes = AutoLockSelection == "never" ? (int?)null : int.Parse(AutoLockSelection);
        Vault.ConfigureAutoLock(minutes);
        AutoLockSaved = true;
    }

    private void LockVault()
    {
        Vault.Lock();
        Nav.NavigateSafe("/unlock");
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Settings.razor
git commit -m "Add Auto-lock after setting to Settings page"
```

---

### Task 6: Keep the vault alive during streamed generation

**Files:**
- Modify: `src/Fabulis.Server/Data/OpenRouterService.cs`

- [ ] **Step 1: Inject VaultService into OpenRouterService**

In `src/Fabulis.Server/Data/OpenRouterService.cs`, change the class declaration:

```csharp
public class OpenRouterService(IHttpClientFactory httpClientFactory, IServiceProvider services)
```

to:

```csharp
public class OpenRouterService(IHttpClientFactory httpClientFactory, IServiceProvider services, VaultService vault)
```

- [ ] **Step 2: Call RecordActivity on each streamed chunk**

In the same file, find the bottom of the `while (true)` loop inside `ChatStreamAsync`:

```csharp
            if (!string.IsNullOrEmpty(reasoning))
                yield return new StreamChunk(StreamChunkKind.Reasoning, reasoning);
            if (!string.IsNullOrEmpty(content))
                yield return new StreamChunk(StreamChunkKind.Content, content);
        }
    }
```

Replace it with:

```csharp
            if (!string.IsNullOrEmpty(reasoning))
            {
                vault.RecordActivity();
                yield return new StreamChunk(StreamChunkKind.Reasoning, reasoning);
            }
            if (!string.IsNullOrEmpty(content))
            {
                vault.RecordActivity();
                yield return new StreamChunk(StreamChunkKind.Content, content);
            }
        }
    }
```

Notes:
- `VaultService` is a singleton, so constructor-injecting it into the scoped `OpenRouterService` is safe.
- Only the streaming path is touched; `ChatAsync` (used by category import, usually short) is not modified.

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Data/OpenRouterService.cs
git commit -m "Record vault activity on each streamed chunk"
```

---

### Task 7: Manual verification

**Files:** none

- [ ] **Step 1: Run the app**

Run: `dotnet run --project src/Fabulis.Server`
Open the browser to the displayed URL and unlock the vault.

- [ ] **Step 2: Verify 1-minute auto-lock**

In Settings → Security, change "Auto-lock after" to `1 minute`. Confirm the "Auto-lock updated." line appears.
Leave the browser idle (no clicks/navigations) for ~75 seconds. Then click any nav link.
Expected: redirected to `/unlock`.

- [ ] **Step 3: Verify Never**

Unlock, set "Auto-lock after" to `Never`, leave idle for ~2 minutes, then navigate.
Expected: still unlocked, no redirect.

- [ ] **Step 4: Verify navigation resets the timer**

Unlock, set to `1 minute`, then navigate between pages every ~40 seconds for a few minutes.
Expected: never locks.

- [ ] **Step 5: Verify stream keeps the vault alive**

Set "Auto-lock after" to `1 minute`. Start a story generation that takes longer than 1 minute (pick a slow model or longer prompt).
Expected: generation completes without the vault locking mid-stream.

- [ ] **Step 6: Verify persistence across relock/unlock**

Set to `5 minutes`. Click "Lock" manually. Unlock again.
Expected: Settings shows `5 minutes` still selected, and the 5-minute idle timer is in effect.
