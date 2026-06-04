# Prompts Category Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a third library category, **Prompts** — story-shaped content containing only the user's side, grouped under the shared category taxonomy and edited via a dedicated editor — visible as its own tab alongside Drafts and Stories.

**Architecture:** New flat `Prompt` + `PromptMessage` entities (no versions, no roles) reusing the existing `Category` taxonomy. Server logic lives in a `PromptService` (mirroring `DraftService`) behind thin minimal-API endpoints. The SwiftUI client gains a `.prompts` `LibraryKind`, a category-scoped prompt list, and a dedicated prompt editor.

**Tech Stack:** ASP.NET Core minimal APIs on .NET 10, EF Core + SQLite/SQLCipher, xUnit (server tests over in-memory SQLite), SwiftUI (client, build-verified).

**Spec:** `docs/superpowers/specs/2026-06-03-prompts-category-design.md`

**Conventions to follow:**
- Every `DateTime` written uses `DateTime.UtcNow` (DB stores UTC by convention).
- New entities must be added to BOTH the EF model (`DbSet`, for fresh vaults via `EnsureCreatedAsync`) AND `EnsureSchemaUpdatedAsync` raw SQL (for existing vaults), exactly like Drafts/DraftMessages.
- Endpoints delegate to a DI-registered service; group uses `.RequireSession()`.

---

## Task 1: Server entities, DbContext, and schema bootstrap

**Files:**
- Create: `src/Fabulis.Server/Data/Prompt.cs`
- Create: `src/Fabulis.Server/Data/PromptMessage.cs`
- Modify: `src/Fabulis.Server/Data/Category.cs`
- Modify: `src/Fabulis.Server/Data/FabulisDbContext.cs`

- [ ] **Step 1: Create the `Prompt` entity**

Create `src/Fabulis.Server/Data/Prompt.cs`:

```csharp
namespace Fabulis.Server.Data;

public class Prompt
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public List<PromptMessage> Messages { get; set; } = [];
}
```

- [ ] **Step 2: Create the `PromptMessage` entity**

Create `src/Fabulis.Server/Data/PromptMessage.cs`:

```csharp
namespace Fabulis.Server.Data;

public class PromptMessage
{
    public int Id { get; set; }
    public int PromptId { get; set; }
    public required string Content { get; set; }
    public int SortOrder { get; set; }

    public Prompt Prompt { get; set; } = null!;
}
```

- [ ] **Step 3: Add the `Prompts` navigation to `Category`**

In `src/Fabulis.Server/Data/Category.cs`, add the navigation alongside `Stories`:

```csharp
namespace Fabulis.Server.Data;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Story> Stories { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
}
```

- [ ] **Step 4: Register DbSets**

In `src/Fabulis.Server/Data/FabulisDbContext.cs`, add after the `DraftMessages` DbSet (around line 47):

```csharp
    public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<PromptMessage> PromptMessages => Set<PromptMessage>();
```

- [ ] **Step 5: Add schema bootstrap SQL for existing vaults**

In `EnsureSchemaUpdatedAsync`, insert these two statements immediately after the `DraftMessages` `CREATE TABLE` block and before `await SeedDefaultStorytellerIfMissingAsync();`:

```csharp
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS Prompts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
            )
            """);

        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS PromptMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PromptId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (PromptId) REFERENCES Prompts(Id) ON DELETE CASCADE
            )
            """);
```

- [ ] **Step 6: Build the server**

Run: `dotnet build src/Fabulis.Server/Fabulis.Server.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Fabulis.Server/Data/Prompt.cs src/Fabulis.Server/Data/PromptMessage.cs src/Fabulis.Server/Data/Category.cs src/Fabulis.Server/Data/FabulisDbContext.cs
git commit -m "Add Prompt and PromptMessage entities with schema bootstrap"
```

---

## Task 2: Server DTOs

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`

- [ ] **Step 1: Extend `CategorySummaryDto` with prompt fields**

In `src/Fabulis.Server/Api/Dtos.cs`, replace the existing `CategorySummaryDto` record (lines ~13-18) with:

```csharp
public sealed record CategorySummaryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    int StoryCount,
    string? LatestStoryTitle,
    int PromptCount,
    string? LatestPromptTitle);
```

- [ ] **Step 2: Add prompt DTOs**

In the same file, in the `// ---------- library / categories / stories ----------` section (after the `StoryMessageDto` record, around line 58), add:

```csharp
// ---------- prompts ----------
public sealed record PromptSummaryDto(
    int Id,
    string Title,
    DateTime CreatedAt,
    int MessageCount);

public sealed record PromptCategoryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<PromptSummaryDto> Prompts);

public sealed record PromptDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<PromptMessageDto> Messages);

public sealed record PromptMessageDto(
    int Id,
    string Content,
    int SortOrder);

public sealed record CreatePromptRequest(int CategoryId, string? Title);

public sealed record UpdatePromptRequest(
    string Title,
    int CategoryId,
    IReadOnlyList<string> Messages);
```

- [ ] **Step 3: Build the server**

Run: `dotnet build src/Fabulis.Server/Fabulis.Server.csproj`
Expected: Build fails ONLY where `CategorySummaryDto` is constructed in `LibraryEndpoints.cs` (now missing two args). This is expected and fixed in Task 4. If any OTHER errors appear, fix them.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs
git commit -m "Add prompt DTOs and extend CategorySummaryDto with prompt counts"
```

---

## Task 3: PromptService (TDD)

**Files:**
- Create: `src/Fabulis.Server/Data/PromptService.cs`
- Create: `tests/Fabulis.Server.Tests/PromptServiceTests.cs`
- Modify: `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`

- [ ] **Step 1: Add EF Core SQLite packages to the test project**

In `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`, add to the existing `PackageReference` `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
```

(If `dotnet build` reports a version mismatch, match the EF Core version already resolved by the server project — run `dotnet list src/Fabulis.Server package` to confirm, and use that version.)

- [ ] **Step 2: Write the failing test**

Create `tests/Fabulis.Server.Tests/PromptServiceTests.cs`:

```csharp
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class PromptServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public PromptServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FabulisDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new FabulisDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Category> SeedCategoryAsync(string name = "Fairy Tales")
    {
        var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task CreatePromptUsesDefaultTitleWhenNull()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);

        var prompt = await svc.CreatePromptAsync(cat.Id, null);

        Assert.Equal("Untitled Prompt", prompt.Title);
        Assert.Equal(cat.Id, prompt.CategoryId);
        Assert.Empty(prompt.Messages);
    }

    [Fact]
    public async Task UpdatePromptReplacesMessagesAndReindexesSortOrder()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Original");

        await svc.UpdatePromptAsync(prompt.Id, "Original", cat.Id, ["A", "B"]);
        var updated = await svc.UpdatePromptAsync(prompt.Id, "Renamed", cat.Id, ["C", "D", "E"]);

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Title);
        Assert.Equal(
            new[] { "C", "D", "E" },
            updated.Messages.OrderBy(m => m.SortOrder).Select(m => m.Content).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, updated.Messages.OrderBy(m => m.SortOrder).Select(m => m.SortOrder).ToArray());
    }

    [Fact]
    public async Task DeletePromptRemovesItAndMessages()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Doomed");
        await svc.UpdatePromptAsync(prompt.Id, "Doomed", cat.Id, ["X"]);

        var deleted = await svc.DeletePromptAsync(prompt.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.Prompts.ToListAsync());
        Assert.Empty(await _db.PromptMessages.ToListAsync());
    }

    [Fact]
    public async Task DeletingCategoryCascadesToPrompts()
    {
        var cat = await SeedCategoryAsync();
        var svc = new PromptService(_db);
        var prompt = await svc.CreatePromptAsync(cat.Id, "Child");
        await svc.UpdatePromptAsync(prompt.Id, "Child", cat.Id, ["msg"]);

        var loaded = await _db.Categories
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .FirstAsync(c => c.Id == cat.Id);
        _db.Categories.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Prompts.ToListAsync());
        Assert.Empty(await _db.PromptMessages.ToListAsync());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: Compilation FAILS — `PromptService` does not exist yet.

- [ ] **Step 4: Implement `PromptService`**

Create `src/Fabulis.Server/Data/PromptService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class PromptService(FabulisDbContext db)
{
    public async Task<Prompt> CreatePromptAsync(int categoryId, string? title)
    {
        var now = DateTime.UtcNow;
        var prompt = new Prompt
        {
            CategoryId = categoryId,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Prompt" : title.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Prompts.Add(prompt);
        await db.SaveChangesAsync();
        return prompt;
    }

    public async Task<Category?> GetCategoryWithPromptsAsync(int categoryId)
    {
        return await db.Categories
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<Prompt?> GetPromptAsync(int id)
    {
        return await db.Prompts
            .Include(p => p.Category)
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Prompt?> UpdatePromptAsync(
        int id, string title, int categoryId, IReadOnlyList<string> messages)
    {
        var prompt = await db.Prompts
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (prompt is null) return null;

        prompt.Title = string.IsNullOrWhiteSpace(title) ? "Untitled Prompt" : title.Trim();
        prompt.CategoryId = categoryId;
        prompt.UpdatedAt = DateTime.UtcNow;

        db.PromptMessages.RemoveRange(prompt.Messages);
        prompt.Messages = messages
            .Select((content, index) => new PromptMessage
            {
                Content = content,
                SortOrder = index,
            })
            .ToList();

        await db.SaveChangesAsync();
        return await GetPromptAsync(id);
    }

    public async Task<bool> DeletePromptAsync(int id)
    {
        var prompt = await db.Prompts
            .Include(p => p.Messages)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (prompt is null) return false;
        db.Prompts.Remove(prompt);
        await db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: All tests PASS (including the four new `PromptServiceTests`).

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Data/PromptService.cs tests/Fabulis.Server.Tests/PromptServiceTests.cs tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj
git commit -m "Add PromptService with create/update/delete and cascade tests"
```

---

## Task 4: Prompt endpoints + library integration

**Files:**
- Create: `src/Fabulis.Server/Api/PromptEndpoints.cs`
- Modify: `src/Fabulis.Server/Api/LibraryEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Register `PromptService` in DI**

In `src/Fabulis.Server/Program.cs`, add after `builder.Services.AddScoped<DraftService>();` (around line 30):

```csharp
builder.Services.AddScoped<PromptService>();
```

- [ ] **Step 2: Wire up the prompt endpoints**

In `src/Fabulis.Server/Program.cs`, add after `api.MapDraftEndpoints();` (around line 54):

```csharp
api.MapPromptEndpoints();
```

- [ ] **Step 3: Create `PromptEndpoints`**

Create `src/Fabulis.Server/Api/PromptEndpoints.cs`:

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class PromptEndpoints
{
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/prompts").RequireSession();

        group.MapGet("/{id:int}", async (int id, PromptService prompts) =>
        {
            var prompt = await prompts.GetPromptAsync(id);
            return prompt is null ? Results.NotFound() : Results.Ok(ToDto(prompt));
        });

        group.MapPost("", async (CreatePromptRequest body, PromptService prompts) =>
        {
            var prompt = await prompts.CreatePromptAsync(body.CategoryId, body.Title);
            var full = await prompts.GetPromptAsync(prompt.Id);
            return Results.Ok(ToDto(full!));
        });

        group.MapPut("/{id:int}", async (int id, UpdatePromptRequest body, PromptService prompts) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "title is required" });
            var updated = await prompts.UpdatePromptAsync(id, body.Title, body.CategoryId, body.Messages);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:int}", async (int id, PromptService prompts) =>
        {
            return await prompts.DeletePromptAsync(id) ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static PromptDto ToDto(Prompt p) => new(
        p.Id,
        p.CategoryId,
        p.Category?.Name ?? "",
        p.Title,
        p.CreatedAt,
        p.UpdatedAt,
        p.Messages
            .OrderBy(m => m.SortOrder)
            .Select(m => new PromptMessageDto(m.Id, m.Content, m.SortOrder))
            .ToList());
}
```

- [ ] **Step 4: Add the category-prompts list endpoint and prompt counts to `LibraryEndpoints`**

In `src/Fabulis.Server/Api/LibraryEndpoints.cs`:

(a) In the `GET /library` handler, add `.Include(c => c.Prompts)` to the query and populate the two new `CategorySummaryDto` fields. Replace the existing handler body's query + projection with:

```csharp
        group.MapGet("/library", async (FabulisDbContext db) =>
        {
            var categories = await db.Categories
                .Include(c => c.Stories)
                .Include(c => c.Prompts)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var dto = new LibraryResponse(categories
                .Select(c => new CategorySummaryDto(
                    c.Id,
                    c.Name,
                    c.CreatedAt,
                    c.Stories.Count,
                    c.Stories.OrderByDescending(s => s.CreatedAt).FirstOrDefault()?.Title,
                    c.Prompts.Count,
                    c.Prompts.OrderByDescending(p => p.CreatedAt).FirstOrDefault()?.Title))
                .ToList());

            return Results.Ok(dto);
        });
```

(b) The `POST /categories` handler also constructs a `CategorySummaryDto` (a new category has zero of each). Update its return (around line 61) to pass the two new args:

```csharp
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null, 0, null));
```

(c) Add a new endpoint immediately after the `GET /categories/{id:int}` handler:

```csharp
        group.MapGet("/categories/{id:int}/prompts", async (int id, PromptService prompts) =>
        {
            var category = await prompts.GetCategoryWithPromptsAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new PromptCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Prompts
                    .OrderBy(p => p.Title)
                    .Select(p => new PromptSummaryDto(p.Id, p.Title, p.CreatedAt, p.Messages.Count))
                    .ToList());

            return Results.Ok(dto);
        });
```

- [ ] **Step 5: Build the server**

Run: `dotnet build src/Fabulis.Server/Fabulis.Server.csproj`
Expected: Build succeeded, 0 errors (the Task 2 `CategorySummaryDto` arity error is now resolved).

- [ ] **Step 6: Manually verify the endpoints round-trip**

Start the server (`dotnet run --project src/Fabulis.Server`), unlock the vault, then:

```bash
# create a category, then a prompt in it (replace TOKEN + CAT_ID)
curl -s -X POST http://localhost:5288/api/v1/categories -H "Authorization: Bearer TOKEN" -H 'Content-Type: application/json' -d '{"name":"Test"}'
curl -s -X POST http://localhost:5288/api/v1/prompts -H "Authorization: Bearer TOKEN" -H 'Content-Type: application/json' -d '{"categoryId":CAT_ID,"title":null}'
curl -s -X PUT http://localhost:5288/api/v1/prompts/1 -H "Authorization: Bearer TOKEN" -H 'Content-Type: application/json' -d '{"title":"Hi","categoryId":CAT_ID,"messages":["first","second"]}'
curl -s http://localhost:5288/api/v1/categories/CAT_ID/prompts -H "Authorization: Bearer TOKEN"
curl -s http://localhost:5288/api/v1/library -H "Authorization: Bearer TOKEN"
```

Expected: the prompt is created with title "Untitled Prompt", updated to two messages with sortOrder 0/1, listed under the category, and `/library` shows `promptCount: 1`.

- [ ] **Step 7: Commit**

```bash
git add src/Fabulis.Server/Api/PromptEndpoints.cs src/Fabulis.Server/Api/LibraryEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "Add prompt endpoints and wire prompt counts into library"
```

---

## Task 5: Client DTOs

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`

- [ ] **Step 1: Extend `CategorySummary` with prompt fields**

In `client/Fabulis/Models/APIDtos.swift`, replace the `CategorySummary` struct (lines ~17-23) with:

```swift
struct CategorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let storyCount: Int
    let latestStoryTitle: String?
    let promptCount: Int
    let latestPromptTitle: String?
}
```

- [ ] **Step 2: Add prompt DTOs**

In the same file, after the `StoryMessage` struct (around line 74), add:

```swift
// MARK: - Prompts

struct PromptSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let title: String
    let createdAt: Date
    let messageCount: Int
}

struct PromptCategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let prompts: [PromptSummary]
}

struct PromptDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let title: String
    let createdAt: Date
    let updatedAt: Date
    let messages: [PromptMessage]
}

struct PromptMessage: Decodable, Identifiable, Sendable {
    let id: Int
    let content: String
    let sortOrder: Int
}

struct CreatePromptRequest: Encodable, Sendable {
    let categoryId: Int
    let title: String?
}

struct UpdatePromptRequest: Encodable, Sendable {
    let title: String
    let categoryId: Int
    let messages: [String]
}
```

- [ ] **Step 3: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift
git commit -m "Add prompt client DTOs and extend CategorySummary"
```

---

## Task 6: Client API methods

**Files:**
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Add prompt request methods**

In `client/Fabulis/Services/FabulisAPIClient.swift`, add after the `category(id:)` method (around line 162):

```swift
    func categoryPrompts(categoryId: Int) async throws -> PromptCategoryDetail {
        try await request("GET", path: "/categories/\(categoryId)/prompts", authed: true)
    }

    func prompt(id: Int) async throws -> PromptDetail {
        try await request("GET", path: "/prompts/\(id)", authed: true)
    }

    func createPrompt(categoryId: Int, title: String?) async throws -> PromptDetail {
        let body = CreatePromptRequest(categoryId: categoryId, title: title)
        return try await request("POST", path: "/prompts", body: body, authed: true)
    }

    func updatePrompt(id: Int, title: String, categoryId: Int, messages: [String]) async throws -> PromptDetail {
        let body = UpdatePromptRequest(title: title, categoryId: categoryId, messages: messages)
        return try await request("PUT", path: "/prompts/\(id)", body: body, authed: true)
    }

    func deletePrompt(id: Int) async throws {
        try await requestVoid("DELETE", path: "/prompts/\(id)", authed: true)
    }
```

- [ ] **Step 2: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "Add prompt API client methods"
```

---

## Task 7: LibraryKind + CategoryRow become prompt-aware

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryKind.swift`
- Modify: `client/Fabulis/Views/Library/CategoryRow.swift`

- [ ] **Step 1: Add the `.prompts` kind**

Replace `client/Fabulis/Views/Library/LibraryKind.swift` with:

```swift
import Foundation

/// A switchable category of library content. The single extensibility point
/// for the library kind-switcher: add a `case` (and its detail view) to grow.
enum LibraryKind: String, CaseIterable, Identifiable {
    case drafts
    case stories
    case prompts

    var id: String { rawValue }

    var label: String {
        switch self {
        case .drafts: "Drafts"
        case .stories: "Stories"
        case .prompts: "Prompts"
        }
    }

    /// Whether this kind organizes its items under the shared category
    /// taxonomy. Drafts are a flat list; stories and prompts are grouped by
    /// category.
    var hasCategories: Bool {
        switch self {
        case .drafts: false
        case .stories: true
        case .prompts: true
        }
    }
}
```

- [ ] **Step 2: Make `CategoryRow` show the count for the active kind**

Replace `client/Fabulis/Views/Library/CategoryRow.swift` with:

```swift
import SwiftUI

/// One row in the category list: name plus a count for the active library kind.
struct CategoryRow: View {
    let category: CategorySummary
    let kind: LibraryKind

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(category.name).font(.body)
            Text(countText)
                .font(.caption2).foregroundStyle(.secondary)
        }
    }

    private var countText: String {
        switch kind {
        case .prompts:
            "\(category.promptCount) \(category.promptCount == 1 ? "prompt" : "prompts")"
        default:
            "\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")"
        }
    }
}
```

- [ ] **Step 3: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD FAILS in `LibraryView.swift` only — `CategoryRow` now requires a `kind:` argument. Fixed in Task 10. If any OTHER errors appear, fix them.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Views/Library/LibraryKind.swift client/Fabulis/Views/Library/CategoryRow.swift
git commit -m "Add prompts library kind and make CategoryRow kind-aware"
```

---

## Task 8: PromptCategoryView

**Files:**
- Create: `client/Fabulis/Views/Library/PromptCategoryView.swift`

- [ ] **Step 1: Create the prompt-category list view**

Create `client/Fabulis/Views/Library/PromptCategoryView.swift`. This mirrors `CategoryView` (rename/delete category controls) but lists prompts and adds a **New Prompt** button that creates an empty prompt and navigates into the editor:

```swift
import SwiftUI

struct PromptCategoryView: View {
    let categoryId: Int
    let categoryName: String
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: PromptCategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var creating = false
    @State private var newPromptId: Int?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false

    var body: some View {
        Group {
            if let detail {
                if detail.prompts.isEmpty {
                    ContentUnavailableView("No prompts", systemImage: "text.bubble",
                        description: Text("Tap \u{201C}New Prompt\u{201D} to add one."))
                } else {
                    List(detail.prompts) { prompt in
                        NavigationLink(value: prompt) {
                            VStack(alignment: .leading, spacing: 4) {
                                Text(prompt.title).font(.body)
                                Text("\(prompt.messageCount) \(prompt.messageCount == 1 ? "message" : "messages")")
                                    .font(.caption2).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load prompts").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.name ?? categoryName)
        .navigationDestination(for: PromptSummary.self) { prompt in
            PromptEditorView(promptId: prompt.id, onChanged: { Task { await load() } })
        }
        .navigationDestination(item: $newPromptId) { id in
            PromptEditorView(promptId: id, onChanged: { Task { await load() } })
        }
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    Task { await createPrompt() }
                } label: {
                    HStack(spacing: 4) {
                        if creating { ProgressView().controlSize(.mini) }
                        else { Image(systemName: "plus") }
                        Text("New Prompt")
                    }
                }
                .disabled(creating)
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button { showingRenameSheet = true } label: { Label("Rename", systemImage: "pencil") }
                    Button(role: .destructive) { showingDeleteConfirm = true } label: { Label("Delete", systemImage: "trash") }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
            }
        }
        .sheet(isPresented: $showingRenameSheet) {
            EditCategorySheet(
                mode: .rename(id: categoryId),
                initialName: detail?.name ?? categoryName,
                onSaved: { Task { await load() } })
        }
        .alert("Delete category?",
               isPresented: $showingDeleteConfirm,
               actions: {
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) { Task { await deleteCategory() } }
               },
               message: {
                    Text("This deletes the category and all its stories and prompts. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.categoryPrompts(categoryId: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func createPrompt() async {
        creating = true; defer { creating = false }
        do {
            let created = try await FabulisAPIClient.shared.createPrompt(categoryId: categoryId, title: nil)
            await load()
            newPromptId = created.id
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deleteCategory() async {
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: categoryId)
            if let onDeleted { onDeleted() } else { dismiss() }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

extension PromptSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: PromptSummary, rhs: PromptSummary) -> Bool { lhs.id == rhs.id }
}
```

- [ ] **Step 2: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD FAILS — `PromptEditorView` is undefined (created in Task 9), plus the pre-existing `CategoryRow` failure from Task 7. Both are resolved by Task 9/10. If any OTHER errors appear in `PromptCategoryView.swift`, fix them.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Library/PromptCategoryView.swift
git commit -m "Add PromptCategoryView listing prompts in a category"
```

---

## Task 9: PromptEditorView

**Files:**
- Create: `client/Fabulis/Views/Library/PromptEditorView.swift`

- [ ] **Step 1: Create the dedicated prompt editor**

Create `client/Fabulis/Views/Library/PromptEditorView.swift`. Title field, category picker, and an add/edit/reorder/delete list of message blocks, with Save (replace-on-save):

```swift
import SwiftUI

struct PromptEditorView: View {
    let promptId: Int
    var onChanged: (() -> Void)? = nil

    private struct EditableMessage: Identifiable {
        let id = UUID()
        var text: String
    }

    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var categoryId: Int?
    @State private var categories: [CategorySummary] = []
    @State private var messages: [EditableMessage] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        Form {
            Section("Title") {
                TextField("Title", text: $title)
            }
            Section("Category") {
                Picker("Category", selection: $categoryId) {
                    ForEach(categories) { cat in
                        Text(cat.name).tag(Optional(cat.id))
                    }
                }
            }
            Section("Messages") {
                ForEach($messages) { $message in
                    TextField("Message", text: $message.text, axis: .vertical)
                        .lineLimit(1...10)
                }
                .onMove { messages.move(fromOffsets: $0, toOffset: $1) }
                .onDelete { messages.remove(atOffsets: $0) }

                Button {
                    messages.append(EditableMessage(text: ""))
                } label: {
                    Label("Add Message", systemImage: "plus")
                }
            }
        }
        .navigationTitle("Edit Prompt")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) { EditButton() }
            ToolbarItem(placement: .topBarLeading) {
                Button {
                    Task { await save() }
                } label: {
                    if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                }
                .disabled(saving || isLoading || categoryId == nil)
            }
        }
        .overlay {
            if isLoading { ProgressView() }
        }
        .alert("Couldn't save", isPresented: Binding(
            get: { errorMessage != nil },
            set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(errorMessage ?? "")
        }
        .task { await load() }
    }

    private func load() async {
        do {
            async let lib = FabulisAPIClient.shared.library()
            async let detail = FabulisAPIClient.shared.prompt(id: promptId)
            categories = try await lib.categories
            let prompt = try await detail
            title = prompt.title
            categoryId = prompt.categoryId
            messages = prompt.messages
                .sorted { $0.sortOrder < $1.sortOrder }
                .map { EditableMessage(text: $0.content) }
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        guard let categoryId else { return }
        saving = true; defer { saving = false }
        do {
            _ = try await FabulisAPIClient.shared.updatePrompt(
                id: promptId,
                title: title,
                categoryId: categoryId,
                messages: messages.map(\.text))
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD FAILS only on the pre-existing `CategoryRow` call-site error in `LibraryView.swift` (fixed in Task 10). `PromptEditorView.swift` and `PromptCategoryView.swift` should compile. If any OTHER errors appear, fix them.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Library/PromptEditorView.swift
git commit -m "Add dedicated PromptEditorView"
```

---

## Task 10: Wire the Prompts tab into LibraryView

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`

- [ ] **Step 1: Pass the active kind into `CategoryRow`**

In `client/Fabulis/Views/Library/LibraryView.swift`, in `categoriesList`, update the `CategoryRow` construction (around line 172):

```swift
                    CategoryRow(category: category, kind: selectedKind)
```

- [ ] **Step 2: Add the Prompts case to the toolbar**

In `toolbarContent`, the leading `ToolbarItem` switches on `selectedKind`. Change the `.stories` case to also cover `.prompts` (both create categories) — replace `case .stories:` with:

```swift
            case .stories, .prompts:
```

- [ ] **Step 3: Route category selection to the right detail view**

In the `detail` computed property, the `.category(let id, let name)` case currently always shows `CategoryView`. Replace that case with a branch on `selectedKind`:

```swift
        case .category(let id, let name):
            NavigationStack {
                if selectedKind == .prompts {
                    PromptCategoryView(categoryId: id, categoryName: name, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                } else {
                    CategoryView(categoryId: id, categoryName: name, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                }
            }
```

- [ ] **Step 4: Update the shared category-delete copy**

The category-delete alert in `LibraryView` (around line 47) still says "all its stories". Replace that message text with:

```swift
                            Text("This deletes the category and all its stories and prompts. This cannot be undone.")
```

Also update the same copy in `client/Fabulis/Views/Library/CategoryView.swift` (the alert message around line 71) to:

```swift
                    Text("This deletes the category and all its stories and prompts. This cannot be undone.")
```

- [ ] **Step 5: Build the client**

Run: `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build`
Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Manual end-to-end verification**

Run the server (`dotnet run --project src/Fabulis.Server`), launch the client against it, unlock, then:
1. Confirm the library picker now shows **Drafts / Stories / Prompts**.
2. On the Prompts tab, confirm categories appear with a prompt count.
3. Open a category → tap **New Prompt** → the editor opens.
4. Set a title, pick a category, add two messages, reorder them, **Save**.
5. Re-open the prompt and confirm the title and messages persisted in the new order.
6. Confirm the prompt count on the Prompts tab incremented.
7. Confirm the Stories tab still works unchanged.

- [ ] **Step 7: Commit**

```bash
git add client/Fabulis/Views/Library/LibraryView.swift client/Fabulis/Views/Library/CategoryView.swift
git commit -m "Wire Prompts tab into LibraryView"
```

---

## Task 11: Final verification

- [ ] **Step 1: Run the full server test suite**

Run: `dotnet test Fabulis.slnx`
Expected: All tests PASS.

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Build the client for both destinations**

Run:
```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=iOS Simulator' build
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'generic/platform=macOS,variant=Mac Catalyst' build
```
Expected: BUILD SUCCEEDED for both.

- [ ] **Step 4: Confirm the deferral is recorded**

The prompt-to-draft conversion is intentionally out of scope. If `BACKLOG.md` does not already mention it, add a one-line entry: "Convert a Prompt into a Draft (deferred from the Prompts category work — see docs/superpowers/specs/2026-06-03-prompts-category-design.md)."

---

## Notes for the implementer

- **No versions for prompts** — this is intentional. A prompt is a flat ordered list of your messages.
- **Replace-on-save** — `PUT /prompts/{id}` deletes and re-inserts all messages each save; message IDs are not stable across saves. The editor never relies on server-assigned message IDs.
- **Shared categories** — a category can hold both stories and prompts. Deleting a category cascades to both. That is why the delete copy changed in three places (`LibraryView`, `CategoryView`, `PromptCategoryView`).
- **Schema in two places** — fresh vaults get tables from the EF model (`EnsureCreatedAsync`); existing vaults get them from the raw SQL in `EnsureSchemaUpdatedAsync`. Both were updated in Task 1.
```
