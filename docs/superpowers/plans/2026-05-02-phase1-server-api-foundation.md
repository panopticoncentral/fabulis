# Phase 1: Server API Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a versioned REST API (`/api/v1/*`) to the existing ASP.NET server that supports vault unlock with session tokens and read-only access to the library, individual stories, settings, and storyteller config — without breaking the existing Blazor UI.

**Architecture:** Minimal-API endpoint groups under `Api/`, an in-memory `SessionTokenStore` whose tokens are invalidated whenever `VaultService.Lock()` runs, and a small endpoint extension (`RequireSession`) that enforces `Authorization: Bearer <token>` on protected routes. Endpoints delegate to the existing scoped services and `FabulisDbContext` — no business logic moves.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 with SQLite + SQLCipher, no new packages.

## File Structure

**Create:**
- `src/Fabulis.Server/Auth/SessionTokenStore.cs` — concurrent in-memory token store, generate/validate/invalidate
- `src/Fabulis.Server/Auth/SessionTokenAuthExtensions.cs` — `RequireSession()` endpoint extension that returns 401 unless `Authorization: Bearer <token>` matches a live token AND vault is unlocked
- `src/Fabulis.Server/Api/Dtos.cs` — request/response records for every API endpoint (one file is fine for now; split later if it grows)
- `src/Fabulis.Server/Api/AuthEndpoints.cs` — `MapAuthEndpoints(this IEndpointRouteBuilder)`
- `src/Fabulis.Server/Api/LibraryEndpoints.cs` — `MapLibraryEndpoints(...)`
- `src/Fabulis.Server/Api/StoryEndpoints.cs` — `MapStoryEndpoints(...)`
- `src/Fabulis.Server/Api/SettingsEndpoints.cs` — `MapSettingsEndpoints(...)`
- `src/Fabulis.Server/Api/StorytellerEndpoints.cs` — `MapStorytellerEndpoints(...)`

**Modify:**
- `src/Fabulis.Server/Data/VaultService.cs` — `Lock()` also invalidates all session tokens
- `src/Fabulis.Server/Program.cs` — register `SessionTokenStore`, mount `app.MapGroup("/api/v1")` with all endpoint registrations

**Untouched:** `DraftService`, `OpenRouterService`, `CategoryImportService`, `CategoryExportService`, `FabulisDbContext`, `AutoLockService`, `Components/`, `wwwroot/`. Blazor must keep working unchanged.

## Notes for the implementer

- **Do not rename or refactor existing services.** Endpoints map directly to the same shapes Razor pages use. If a refactor seems tempting, save it for a follow-up plan.
- **`MessageRole` is `Prompt | Response`.** Not `User | Assistant`. Match this in DTOs.
- **DbContext lifetime gotcha.** `Program.cs` only configures the SQLite provider when `vault.IsUnlocked` at the time the DbContext options factory runs. If the auth filter doesn't reject locked-vault requests *before* the endpoint resolves the DbContext, EF will throw. The `RequireSession` filter only depends on singleton services (`SessionTokenStore`, `VaultService`) — never resolve `FabulisDbContext` from inside it.
- **Activity tracking.** The existing middleware in `Program.cs` already calls `vault.RecordActivity()` on every non-infra request. `/api/v1/*` is non-infra → already counted. No change needed.
- **Anti-forgery.** Apply `.DisableAntiforgery()` at the `/api/v1` group level — JSON bearer-token endpoints do not need CSRF protection and the `app.UseAntiforgery()` call upstream covers Razor only.
- **Per-task verification uses curl.** The executor should start the server with `dotnet run --project src/Fabulis.Server &` (or `run_in_background`), wait for it to listen on `http://localhost:5288`, run the curl(s), then kill the server before moving on. The vault password used during verification is the developer's own.

---

## Task 1: Add `SessionTokenStore`

**Files:**
- Create: `src/Fabulis.Server/Auth/SessionTokenStore.cs`

- [ ] **Step 1: Create the file with the store**

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Fabulis.Server.Auth;

public sealed record TokenInfo(string Token, DateTime IssuedAt);

public sealed class SessionTokenStore
{
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new(StringComparer.Ordinal);

    public TokenInfo Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var info = new TokenInfo(token, DateTime.UtcNow);
        _tokens[token] = info;
        return info;
    }

    public bool IsValid(string? token)
    {
        return token is not null && _tokens.ContainsKey(token);
    }

    public void Revoke(string token) => _tokens.TryRemove(token, out _);

    public void RevokeAll() => _tokens.Clear();
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Auth/SessionTokenStore.cs
git commit -m "Add in-memory SessionTokenStore for API auth"
```

---

## Task 2: Wire `SessionTokenStore` into `VaultService.Lock()`

**Files:**
- Modify: `src/Fabulis.Server/Data/VaultService.cs`

- [ ] **Step 1: Inject the store and clear it on lock**

Replace the contents of `src/Fabulis.Server/Data/VaultService.cs` with:

```csharp
using Fabulis.Server.Auth;

namespace Fabulis.Server.Data;

public class VaultService(SessionTokenStore tokens)
{
    private long _lastActivityTicks;
    private int _autoLockMinutes;
    private int _isUnlocked;
    private string? _password;

    public bool IsUnlocked => Volatile.Read(ref _isUnlocked) != 0;

    public string? Password => Volatile.Read(ref _password);

    public DateTime LastActivityAt =>
        new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);

    public TimeSpan? AutoLockTimeout
    {
        get
        {
            var minutes = Volatile.Read(ref _autoLockMinutes);
            return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
        }
    }

    public void Unlock(string password)
    {
        Volatile.Write(ref _password, password);
        Volatile.Write(ref _isUnlocked, 1);
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void Lock()
    {
        Volatile.Write(ref _password, null);
        Volatile.Write(ref _isUnlocked, 0);
        Volatile.Write(ref _autoLockMinutes, 0);
        tokens.RevokeAll();
    }

    public void RecordActivity()
    {
        if (IsUnlocked)
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void ConfigureAutoLock(int? minutes)
    {
        var value = minutes is int m && m > 0 ? m : 0;
        Volatile.Write(ref _autoLockMinutes, value);
    }
}
```

- [ ] **Step 2: Register the store in `Program.cs` so DI can satisfy `VaultService`'s new dependency**

Edit `src/Fabulis.Server/Program.cs`. Find the line:

```csharp
builder.Services.AddSingleton<VaultService>();
```

Replace it with:

```csharp
builder.Services.AddSingleton<Fabulis.Server.Auth.SessionTokenStore>();
builder.Services.AddSingleton<VaultService>();
```

- [ ] **Step 3: Verify the existing Blazor flow still builds and unlocks**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

Manual: `dotnet run --project src/Fabulis.Server`, browse to `http://localhost:5288/unlock`, enter your vault password, confirm `/library` loads. (No regression — `Lock()` now also calls `tokens.RevokeAll()` but the store is empty.)

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Data/VaultService.cs src/Fabulis.Server/Program.cs
git commit -m "Invalidate API session tokens when vault locks"
```

---

## Task 3: Add `RequireSession` endpoint extension

**Files:**
- Create: `src/Fabulis.Server/Auth/SessionTokenAuthExtensions.cs`

- [ ] **Step 1: Create the file**

```csharp
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;

namespace Fabulis.Server.Auth;

public static class SessionTokenAuthExtensions
{
    private const string BearerPrefix = "Bearer ";

    public static TBuilder RequireSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var tokens = ctx.HttpContext.RequestServices.GetRequiredService<SessionTokenStore>();
            var vault = ctx.HttpContext.RequestServices.GetRequiredService<VaultService>();

            if (!vault.IsUnlocked)
                return Results.Unauthorized();

            var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
                return Results.Unauthorized();

            var token = header[BearerPrefix.Length..].Trim();
            if (!tokens.IsValid(token))
                return Results.Unauthorized();

            return await next(ctx);
        });
        return builder;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Auth/SessionTokenAuthExtensions.cs
git commit -m "Add RequireSession endpoint filter for /api/v1"
```

---

## Task 4: Define API DTOs

**Files:**
- Create: `src/Fabulis.Server/Api/Dtos.cs`

- [ ] **Step 1: Create the file with all DTOs Phase 1 needs**

```csharp
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

// ---------- auth ----------
public sealed record UnlockRequest(string Password);
public sealed record UnlockResponse(string Token, DateTime IssuedAt);
public sealed record AuthStatusResponse(bool IsUnlocked, int? AutoLockMinutes);

// ---------- library / categories / stories ----------
public sealed record LibraryResponse(IReadOnlyList<CategorySummaryDto> Categories);

public sealed record CategorySummaryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    int StoryCount,
    string? LatestStoryTitle);

public sealed record CategoryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<StorySummaryDto> Stories);

public sealed record StorySummaryDto(
    int Id,
    string Title,
    DateTime CreatedAt,
    int VersionCount);

public sealed record StoryDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string Title,
    DateTime CreatedAt,
    IReadOnlyList<StoryVersionSummaryDto> Versions);

public sealed record StoryVersionSummaryDto(
    int Id,
    int VersionNumber,
    string ModelName,
    DateTime CreatedAt);

public sealed record StoryVersionDto(
    int Id,
    int StoryId,
    int VersionNumber,
    string ModelName,
    DateTime CreatedAt,
    IReadOnlyList<StoryMessageDto> Messages);

public sealed record StoryMessageDto(
    int Id,
    MessageRole Role,
    string Content,
    int SortOrder);

// ---------- settings ----------
public sealed record SettingsDto(
    bool ApiKeyIsSet,
    string? AssistantModel,
    string AutoLockSelection); // "1"/"5"/"15"/"30"/"60"/"never"

public sealed record SettingsUpdateRequest(
    string? ApiKey,            // null = leave alone
    string? AssistantModel,    // null = leave alone
    string? AutoLockSelection); // null = leave alone, otherwise one of the legal strings

// ---------- storyteller ----------
public sealed record StorytellerDto(
    int Id,
    string Name,
    string Prompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);

public sealed record StorytellerUpdateRequest(
    string Name,
    string Prompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs
git commit -m "Add API DTOs for Phase 1 endpoints"
```

---

## Task 5: Auth endpoints (`/api/v1/auth/*`)

**Files:**
- Create: `src/Fabulis.Server/Api/AuthEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `AuthEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Components.Pages;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth");

        group.MapPost("/unlock", async (
            UnlockRequest body,
            VaultService vault,
            SessionTokenStore tokens,
            IServiceProvider services) =>
        {
            if (string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest(new { error = "password is required" });

            vault.Unlock(body.Password);

            try
            {
                await using var scope = services.CreateAsyncScope();
                await using var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
                await db.Database.EnsureCreatedAsync();
                await db.EnsureSchemaUpdatedAsync();

                var setting = await db.AppSettings.FindAsync("AutoLockMinutes");
                vault.ConfigureAutoLock(Unlock.ParseAutoLockMinutes(setting?.Value));
            }
            catch
            {
                vault.Lock();
                return Results.Unauthorized();
            }

            var info = tokens.Issue();
            return Results.Ok(new UnlockResponse(info.Token, info.IssuedAt));
        });

        group.MapPost("/lock", (VaultService vault) =>
        {
            vault.Lock();
            return Results.NoContent();
        }).RequireSession();

        group.MapGet("/status", (VaultService vault) =>
        {
            var minutes = vault.AutoLockTimeout is { } t ? (int?)t.TotalMinutes : null;
            return Results.Ok(new AuthStatusResponse(vault.IsUnlocked, minutes));
        }).RequireSession();

        return routes;
    }
}
```

- [ ] **Step 2: Mount the API group in `Program.cs`**

Edit `src/Fabulis.Server/Program.cs`. Find the block that starts:

```csharp
app.UseAntiforgery();
app.MapStaticAssets();
```

Insert *immediately before* `app.UseAntiforgery();`:

```csharp
var api = app.MapGroup("/api/v1").DisableAntiforgery();
api.MapAuthEndpoints();
```

Also add this `using` near the top of `Program.cs`:

```csharp
using Fabulis.Server.Api;
```

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 4: Smoke test with curl**

Start server: `dotnet run --project src/Fabulis.Server &` and wait for "Now listening on: http://localhost:5288".

Replace `YOURPASS` with your vault password in the commands below.

Unlock:
```bash
curl -sS -X POST http://localhost:5288/api/v1/auth/unlock \
  -H 'Content-Type: application/json' \
  -d '{"password":"YOURPASS"}'
```
Expected: JSON body like `{"token":"...","issuedAt":"..."}`. HTTP 200.

Status with that token:
```bash
TOK="<paste token>"
curl -sS http://localhost:5288/api/v1/auth/status -H "Authorization: Bearer $TOK"
```
Expected: `{"isUnlocked":true,"autoLockMinutes":15}` (or whatever your setting is). HTTP 200.

Status without a token:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/auth/status
```
Expected: `401`.

Lock:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5288/api/v1/auth/lock -H "Authorization: Bearer $TOK"
```
Expected: `204`.

Status again with the now-revoked token:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/auth/status -H "Authorization: Bearer $TOK"
```
Expected: `401`.

Stop the server: `kill %1`.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/AuthEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add /api/v1/auth/{unlock,lock,status} endpoints"
```

---

## Task 6: Library + category read endpoints

**Files:**
- Create: `src/Fabulis.Server/Api/LibraryEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `LibraryEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("").RequireSession();

        group.MapGet("/library", async (FabulisDbContext db) =>
        {
            var categories = await db.Categories
                .Include(c => c.Stories)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var dto = new LibraryResponse(categories
                .Select(c => new CategorySummaryDto(
                    c.Id,
                    c.Name,
                    c.CreatedAt,
                    c.Stories.Count,
                    c.Stories.OrderByDescending(s => s.CreatedAt).FirstOrDefault()?.Title))
                .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var category = await db.Categories
                .Include(c => c.Stories)
                    .ThenInclude(s => s.Versions)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
                return Results.NotFound();

            var dto = new CategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Stories
                    .OrderBy(s => s.Title)
                    .Select(s => new StorySummaryDto(s.Id, s.Title, s.CreatedAt, s.Versions.Count))
                    .ToList());

            return Results.Ok(dto);
        });

        return routes;
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

In `Program.cs`, after the line `api.MapAuthEndpoints();` add:

```csharp
api.MapLibraryEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 4: Smoke test**

Start server. Unlock and capture a token (see Task 5). Then:

```bash
curl -sS http://localhost:5288/api/v1/library -H "Authorization: Bearer $TOK" | head -c 500
```
Expected: JSON with a `categories` array matching what the browser shows at `/library`.

Pick one category id from the response, then:
```bash
curl -sS http://localhost:5288/api/v1/categories/1 -H "Authorization: Bearer $TOK"
```
Expected: JSON with the category and its stories. 404 if id doesn't exist.

Without token:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/library
```
Expected: `401`.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/LibraryEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add /api/v1/library and /api/v1/categories/{id} read endpoints"
```

---

## Task 7: Story + version read endpoints

**Files:**
- Create: `src/Fabulis.Server/Api/StoryEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `StoryEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class StoryEndpoints
{
    public static IEndpointRouteBuilder MapStoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/stories").RequireSession();

        group.MapGet("/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var story = await db.Stories
                .Include(s => s.Category)
                .Include(s => s.Versions)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (story is null)
                return Results.NotFound();

            var dto = new StoryDto(
                story.Id,
                story.CategoryId,
                story.Category.Name,
                story.Title,
                story.CreatedAt,
                story.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new StoryVersionSummaryDto(v.Id, v.VersionNumber, v.ModelName, v.CreatedAt))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/{storyId:int}/versions/{version:int}", async (
            int storyId,
            int version,
            FabulisDbContext db) =>
        {
            var v = await db.StoryVersions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.StoryId == storyId && x.VersionNumber == version);

            if (v is null)
                return Results.NotFound();

            var dto = new StoryVersionDto(
                v.Id,
                v.StoryId,
                v.VersionNumber,
                v.ModelName,
                v.CreatedAt,
                v.Messages
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new StoryMessageDto(m.Id, m.Role, m.Content, m.SortOrder))
                    .ToList());

            return Results.Ok(dto);
        });

        return routes;
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

After `api.MapLibraryEndpoints();` add:

```csharp
api.MapStoryEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 4: Smoke test**

Start server, unlock, capture token. Pick a real `{storyId}` from `/api/v1/library` output. Then:

```bash
curl -sS http://localhost:5288/api/v1/stories/1 -H "Authorization: Bearer $TOK"
```
Expected: JSON for the story including its versions array.

```bash
curl -sS http://localhost:5288/api/v1/stories/1/versions/1 -H "Authorization: Bearer $TOK"
```
Expected: JSON including the messages list. Each message's `role` should be `"Prompt"` or `"Response"`.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/stories/9999 -H "Authorization: Bearer $TOK"
```
Expected: `404`.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/StoryEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add /api/v1/stories/{id} and /versions/{v} read endpoints"
```

---

## Task 8: Settings GET/PUT endpoints

**Files:**
- Create: `src/Fabulis.Server/Api/SettingsEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create `SettingsEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class SettingsEndpoints
{
    private static readonly HashSet<string> LegalAutoLock =
        new(StringComparer.OrdinalIgnoreCase) { "1", "5", "15", "30", "60", "never" };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/settings").RequireSession();

        group.MapGet("", async (FabulisDbContext db) =>
        {
            var apiKey = await db.AppSettings.FindAsync("OpenRouterApiKey");
            var assistantModel = await db.AppSettings.FindAsync("AssistantModel");
            var autoLock = await db.AppSettings.FindAsync("AutoLockMinutes");

            var dto = new SettingsDto(
                ApiKeyIsSet: apiKey is not null && !string.IsNullOrEmpty(apiKey.Value),
                AssistantModel: assistantModel?.Value,
                AutoLockSelection: NormalizeAutoLock(autoLock?.Value));

            return Results.Ok(dto);
        });

        group.MapPut("", async (
            SettingsUpdateRequest body,
            FabulisDbContext db,
            VaultService vault) =>
        {
            if (body.ApiKey is { } apiKey && !string.IsNullOrWhiteSpace(apiKey))
                await UpsertAsync(db, "OpenRouterApiKey", apiKey.Trim());

            if (body.AssistantModel is { } model && !string.IsNullOrWhiteSpace(model))
                await UpsertAsync(db, "AssistantModel", model.Trim());

            if (body.AutoLockSelection is { } autoLock)
            {
                if (!LegalAutoLock.Contains(autoLock))
                    return Results.BadRequest(new { error = "autoLockSelection must be one of 1, 5, 15, 30, 60, or never" });

                await UpsertAsync(db, "AutoLockMinutes", autoLock);
                vault.ConfigureAutoLock(autoLock.Equals("never", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(autoLock));
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }

    private static async Task UpsertAsync(FabulisDbContext db, string key, string value)
    {
        var existing = await db.AppSettings.FindAsync(key);
        if (existing is not null)
            existing.Value = value;
        else
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
    }

    private static string NormalizeAutoLock(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return "never";
        if (int.TryParse(raw, out var parsed) && LegalAutoLock.Contains(parsed.ToString()))
            return parsed.ToString();
        return "15";
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

After `api.MapStoryEndpoints();` add:

```csharp
api.MapSettingsEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 4: Smoke test**

Start server, unlock, capture token.

```bash
curl -sS http://localhost:5288/api/v1/settings -H "Authorization: Bearer $TOK"
```
Expected: `{"apiKeyIsSet":true|false,"assistantModel":"...","autoLockSelection":"15"}` matching what the browser settings page shows.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X PUT http://localhost:5288/api/v1/settings \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOK" \
  -d '{"autoLockSelection":"30"}'
```
Expected: `204`. Then re-GET settings to confirm `autoLockSelection` is now `"30"`.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X PUT http://localhost:5288/api/v1/settings \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOK" \
  -d '{"autoLockSelection":"bogus"}'
```
Expected: `400`.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/SettingsEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add /api/v1/settings GET and PUT endpoints"
```

---

## Task 9: Storyteller GET/PUT endpoints

**Files:**
- Create: `src/Fabulis.Server/Api/StorytellerEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

The schema's seed code (`SeedDefaultStorytellerIfMissingAsync`) ensures exactly one storyteller exists. The API treats it as a singleton resource.

- [ ] **Step 1: Create `StorytellerEndpoints.cs`**

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class StorytellerEndpoints
{
    public static IEndpointRouteBuilder MapStorytellerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/storyteller").RequireSession();

        group.MapGet("", async (FabulisDbContext db) =>
        {
            var s = await db.Storytellers.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            return Results.Ok(new StorytellerDto(
                s.Id, s.Name, s.Prompt, s.ModelName,
                s.Temperature, s.TopP, s.MaxTokens, s.MinP, s.TopK, s.TopA));
        });

        group.MapPut("", async (StorytellerUpdateRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Prompt) ||
                string.IsNullOrWhiteSpace(body.ModelName))
            {
                return Results.BadRequest(new { error = "name, prompt, and modelName are required" });
            }

            var s = await db.Storytellers.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (s is null)
                return Results.NotFound();

            s.Name = body.Name.Trim();
            s.Prompt = body.Prompt;
            s.ModelName = body.ModelName.Trim();
            s.Temperature = body.Temperature;
            s.TopP = body.TopP;
            s.MaxTokens = body.MaxTokens;
            s.MinP = body.MinP;
            s.TopK = body.TopK;
            s.TopA = body.TopA;

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

After `api.MapSettingsEndpoints();` add:

```csharp
api.MapStorytellerEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build Fabulis.slnx`
Expected: 0 errors.

- [ ] **Step 4: Smoke test**

Start server, unlock, capture token.

```bash
curl -sS http://localhost:5288/api/v1/storyteller -H "Authorization: Bearer $TOK"
```
Expected: JSON for the seeded storyteller (name, prompt, modelName, temperature, etc.).

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X PUT http://localhost:5288/api/v1/storyteller \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOK" \
  -d '{"name":"Storyteller","prompt":"You are a helpful storyteller.","modelName":"anthropic/claude-sonnet-4","temperature":0.7,"topP":null,"maxTokens":null,"minP":null,"topK":null,"topA":null}'
```
Expected: `204`. Re-GET to confirm fields persist.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X PUT http://localhost:5288/api/v1/storyteller \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOK" \
  -d '{"name":"","prompt":"","modelName":"","temperature":0.7,"topP":null,"maxTokens":null,"minP":null,"topK":null,"topA":null}'
```
Expected: `400`.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Api/StorytellerEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add /api/v1/storyteller GET and PUT endpoints"
```

---

## Task 10: Final verification — Blazor regression + auto-lock interaction

The new code is a pure addition to the request pipeline; Blazor should be untouched. This task confirms that and verifies the auto-lock-invalidates-tokens guarantee that the spec calls out as a required property.

- [ ] **Step 1: Blazor regression check**

Start the server. In a browser:
- Visit `http://localhost:5288` → should redirect/route to `/unlock`.
- Enter the vault password → should land on `/library`.
- Click into a category → category page loads.
- Click into a story → story page loads with version list.
- Open `/settings` → API key, assistant model, auto-lock settings all render.

If any of these break, the API plumbing accidentally affected the existing UI — investigate before continuing.

- [ ] **Step 2: Auto-lock invalidates tokens**

Lower auto-lock to 1 minute via the browser settings page. With the server still running:

```bash
curl -sS -X POST http://localhost:5288/api/v1/auth/unlock \
  -H 'Content-Type: application/json' \
  -d '{"password":"YOURPASS"}'
TOK="<paste token>"
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/auth/status -H "Authorization: Bearer $TOK"
```
Expected: 200 immediately.

Wait > 1 minute without sending any requests, then:
```bash
curl -sS -o /dev/null -w '%{http_code}\n' http://localhost:5288/api/v1/auth/status -H "Authorization: Bearer $TOK"
```
Expected: `401` — because `AutoLockService` called `vault.Lock()` which called `tokens.RevokeAll()`.

Reset auto-lock to your normal value via the browser settings page.

- [ ] **Step 3: Wrong-password protection**

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5288/api/v1/auth/unlock \
  -H 'Content-Type: application/json' \
  -d '{"password":"definitely-wrong"}'
```
Expected: `401`. The vault should be locked afterward (re-test by trying a correct unlock — it should still work).

- [ ] **Step 4: Stop the server and commit a Phase 1 wrap-up note**

This commit is just a marker — no code changes.

```bash
git commit --allow-empty -m "Phase 1 complete: server REST API foundation

Endpoints under /api/v1: auth/unlock,lock,status; library; categories/{id};
stories/{id}; stories/{id}/versions/{v}; settings GET+PUT;
storyteller GET+PUT. Bearer token auth via SessionTokenStore;
tokens invalidated on vault lock (manual or auto). Blazor UI
untouched. Phase 2 (native client) and Phase 3 (drafts + SSE
streaming) still pending."
```

---

## Self-review notes

- Spec coverage: every endpoint listed in the design under Phase 1 has a task. Endpoints out of scope for Phase 1 (drafts, SSE messages, save, models, import, export) intentionally not included — they belong to Phase 3 / Phase 4.
- Type consistency: `MessageRole` stays as the existing `Prompt | Response` enum throughout; DTO record names match across files; `RequireSession` extension is referenced in Tasks 5–9 exactly as defined in Task 3.
- No placeholders. Every code block is complete.
- Risk: the `AuthEndpoints.cs` file calls `Unlock.ParseAutoLockMinutes(...)` — that method is `internal static` on the existing `Unlock.razor` `@code` block in `src/Fabulis.Server/Components/Pages/`. Same assembly, accessible. If a future cleanup removes Razor pages before the API is updated, this becomes dead. Acceptable for Phase 1; revisit in Phase 4 (Blazor retirement) by inlining the parser into a shared helper.
