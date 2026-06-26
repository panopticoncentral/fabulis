# One-liners Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fourth library kind, **One-liners** — single untitled lines of text, organized under the shared `Category` taxonomy, created and edited through a lightweight inline UI.

**Architecture:** Mirror the existing Prompts vertical slice but flat: a single `OneLiner` entity (no title, no child-message table), a `OneLinerService`, REST endpoints under `/one-liners`, and SwiftUI views that capture lines via an inline compose field and edit them in a small sheet. Each commit keeps both the server (`dotnet build` + xUnit) and the client (`xcodebuild`) green.

**Tech Stack:** ASP.NET Core (.NET 10) minimal APIs, EF Core + SQLite/SQLCipher, xUnit; SwiftUI client (iOS 18.5+ / Mac Catalyst).

**Reference spec:** `docs/superpowers/specs/2026-06-25-one-liners-design.md`

---

## File Structure

**Server — create:**
- `src/Fabulis.Server/Data/OneLiner.cs` — the entity.
- `src/Fabulis.Server/Data/OneLinerService.cs` — CRUD over `OneLiner`.
- `src/Fabulis.Server/Api/OneLinerEndpoints.cs` — `/one-liners` endpoint group.

**Server — modify:**
- `src/Fabulis.Server/Data/Category.cs` — add `OneLiners` navigation.
- `src/Fabulis.Server/Data/FabulisDbContext.cs` — `DbSet` + schema bootstrap.
- `src/Fabulis.Server/Api/Dtos.cs` — one-liner DTOs + extend `CategorySummaryDto`.
- `src/Fabulis.Server/Api/LibraryEndpoints.cs` — counts + per-category listing.
- `src/Fabulis.Server/Program.cs` — register service + endpoints.

**Tests — create:**
- `tests/Fabulis.Server.Tests/OneLinerServiceTests.cs`.

**Client — create:**
- `client/Fabulis/Views/Library/OneLinerCategoryView.swift` — list + compose bar.
- `client/Fabulis/Views/Library/OneLinerEditSheet.swift` — edit/move/delete sheet.

**Client — modify:**
- `client/Fabulis/Models/APIDtos.swift` — Swift DTOs + extend `CategorySummary`.
- `client/Fabulis/Services/FabulisAPIClient.swift` — API methods.
- `client/Fabulis/Views/Library/LibraryKind.swift` — new `.oneLiners` case.
- `client/Fabulis/Views/Library/CategoryRow.swift` — count string.
- `client/Fabulis/Views/Library/LibraryView.swift` — wiring + `==` + copy.

**Note on Xcode:** the project uses synchronized file groups, so newly created `.swift` files under `client/Fabulis/` are compiled automatically — no `project.pbxproj` edits needed.

---

## Task 1: `OneLiner` entity, `Category` relationship, and DbContext registration

**Files:**
- Create: `src/Fabulis.Server/Data/OneLiner.cs`
- Modify: `src/Fabulis.Server/Data/Category.cs`
- Modify: `src/Fabulis.Server/Data/FabulisDbContext.cs`

- [ ] **Step 1: Create the entity**

Create `src/Fabulis.Server/Data/OneLiner.cs`:

```csharp
namespace Fabulis.Server.Data;

public class OneLiner
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
```

- [ ] **Step 2: Add the navigation to `Category`**

In `src/Fabulis.Server/Data/Category.cs`, add the `OneLiners` list next to `Prompts`:

```csharp
    public List<Story> Stories { get; set; } = [];
    public List<Prompt> Prompts { get; set; } = [];
    public List<OneLiner> OneLiners { get; set; } = [];
```

- [ ] **Step 3: Register the `DbSet`**

In `src/Fabulis.Server/Data/FabulisDbContext.cs`, add the `DbSet` after `PromptMessages` (around line 49):

```csharp
    public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<PromptMessage> PromptMessages => Set<PromptMessage>();
    public DbSet<OneLiner> OneLiners => Set<OneLiner>();
```

- [ ] **Step 4: Add the schema-bootstrap table**

In `EnsureSchemaUpdatedAsync`, immediately after the `PromptMessages` `CREATE TABLE` block and before `await SeedDefaultStorytellerIfMissingAsync();`, add:

```csharp
        await Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS OneLiners (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CategoryId INTEGER NOT NULL,
                Text TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
            )
            """);
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Data/OneLiner.cs src/Fabulis.Server/Data/Category.cs src/Fabulis.Server/Data/FabulisDbContext.cs
git commit -m "$(cat <<'EOF'
Add OneLiner entity and schema

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `OneLinerService` (TDD)

**Files:**
- Create: `tests/Fabulis.Server.Tests/OneLinerServiceTests.cs`
- Create: `src/Fabulis.Server/Data/OneLinerService.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Fabulis.Server.Tests/OneLinerServiceTests.cs`:

```csharp
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Server.Tests;

public class OneLinerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FabulisDbContext _db;

    public OneLinerServiceTests()
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

    private async Task<Category> SeedCategoryAsync(string name = "Openers")
    {
        var cat = new Category { Name = name, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return cat;
    }

    [Fact]
    public async Task CreateOneLinerTrimsAndStoresText()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);

        var line = await svc.CreateOneLinerAsync(cat.Id, "  She set fire to the document.  ");

        Assert.Equal("She set fire to the document.", line.Text);
        Assert.Equal(cat.Id, line.CategoryId);
        Assert.NotEqual(default, line.CreatedAt);
        Assert.Equal(line.CreatedAt, line.UpdatedAt);
    }

    [Fact]
    public async Task UpdateOneLinerChangesTextAndCategory()
    {
        var from = await SeedCategoryAsync("From");
        var to = await SeedCategoryAsync("To");
        var svc = new OneLinerService(_db);
        var line = await svc.CreateOneLinerAsync(from.Id, "Original line.");

        var updated = await svc.UpdateOneLinerAsync(line.Id, "  Edited line.  ", to.Id);

        Assert.NotNull(updated);
        Assert.Equal("Edited line.", updated!.Text);
        Assert.Equal(to.Id, updated.CategoryId);
        Assert.Equal("To", updated.Category.Name);
    }

    [Fact]
    public async Task UpdateOneLinerReturnsNullForUnknownId()
    {
        var svc = new OneLinerService(_db);

        var updated = await svc.UpdateOneLinerAsync(9999, "x", 1);

        Assert.Null(updated);
    }

    [Fact]
    public async Task DeleteOneLinerRemovesIt()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);
        var line = await svc.CreateOneLinerAsync(cat.Id, "Doomed.");

        var deleted = await svc.DeleteOneLinerAsync(line.Id);

        Assert.True(deleted);
        Assert.Empty(await _db.OneLiners.ToListAsync());
    }

    [Fact]
    public async Task CategoryExistsReflectsSeededState()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);

        Assert.True(await svc.CategoryExistsAsync(cat.Id));
        Assert.False(await svc.CategoryExistsAsync(9999));
    }

    [Fact]
    public async Task DeletingCategoryCascadesToOneLiners()
    {
        var cat = await SeedCategoryAsync();
        var svc = new OneLinerService(_db);
        await svc.CreateOneLinerAsync(cat.Id, "Child line.");

        var loaded = await _db.Categories
            .Include(c => c.OneLiners)
            .FirstAsync(c => c.Id == cat.Id);
        _db.Categories.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.OneLiners.ToListAsync());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter OneLinerServiceTests`
Expected: FAIL — `OneLinerService` does not exist (compile error).

- [ ] **Step 3: Implement the service**

Create `src/Fabulis.Server/Data/OneLinerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class OneLinerService(FabulisDbContext db)
{
    public async Task<OneLiner> CreateOneLinerAsync(int categoryId, string text)
    {
        var now = DateTime.UtcNow;
        var oneLiner = new OneLiner
        {
            CategoryId = categoryId,
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.OneLiners.Add(oneLiner);
        await db.SaveChangesAsync();
        return oneLiner;
    }

    public async Task<Category?> GetCategoryWithOneLinersAsync(int categoryId)
    {
        return await db.Categories
            .Include(c => c.OneLiners)
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<OneLiner?> GetOneLinerAsync(int id)
    {
        return await db.OneLiners
            .Include(o => o.Category)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<OneLiner?> UpdateOneLinerAsync(int id, string text, int categoryId)
    {
        var oneLiner = await db.OneLiners.FirstOrDefaultAsync(o => o.Id == id);
        if (oneLiner is null) return null;

        oneLiner.Text = text.Trim();
        oneLiner.CategoryId = categoryId;
        oneLiner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return await GetOneLinerAsync(id);
    }

    public async Task<bool> CategoryExistsAsync(int categoryId)
    {
        return await db.Categories.AnyAsync(c => c.Id == categoryId);
    }

    public async Task<bool> DeleteOneLinerAsync(int id)
    {
        var oneLiner = await db.OneLiners.FirstOrDefaultAsync(o => o.Id == id);
        if (oneLiner is null) return false;
        db.OneLiners.Remove(oneLiner);
        await db.SaveChangesAsync();
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter OneLinerServiceTests`
Expected: PASS — 6 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Data/OneLinerService.cs tests/Fabulis.Server.Tests/OneLinerServiceTests.cs
git commit -m "$(cat <<'EOF'
Add OneLinerService with tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: DTOs, `CategorySummaryDto` extension, and `LibraryEndpoints`

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`
- Modify: `src/Fabulis.Server/Api/LibraryEndpoints.cs`

- [ ] **Step 1: Add the one-liner DTOs**

In `src/Fabulis.Server/Api/Dtos.cs`, after the `// ---------- prompts ----------` block (after `UpdatePromptRequest`), add:

```csharp
// ---------- one-liners ----------
public sealed record OneLinerSummaryDto(
    int Id,
    string Text,
    DateTime CreatedAt);

public sealed record OneLinerCategoryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<OneLinerSummaryDto> OneLiners);

public sealed record OneLinerDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string Text,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateOneLinerRequest(int CategoryId, string Text);

public sealed record UpdateOneLinerRequest(string Text, int CategoryId);
```

- [ ] **Step 2: Extend `CategorySummaryDto`**

In `src/Fabulis.Server/Api/Dtos.cs`, replace the `CategorySummaryDto` record with:

```csharp
public sealed record CategorySummaryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    int StoryCount,
    string? LatestStoryTitle,
    int PromptCount,
    string? LatestPromptTitle,
    int OneLinerCount,
    string? LatestOneLinerText);
```

- [ ] **Step 3: Populate the new fields in `GET /library`**

In `src/Fabulis.Server/Api/LibraryEndpoints.cs`, in the `/library` handler, add the `OneLiners` include and the two new constructor arguments:

```csharp
            var categories = await db.Categories
                .Include(c => c.Stories)
                .Include(c => c.Prompts)
                .Include(c => c.OneLiners)
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
                    c.Prompts.OrderByDescending(p => p.CreatedAt).FirstOrDefault()?.Title,
                    c.OneLiners.Count,
                    c.OneLiners.OrderByDescending(o => o.CreatedAt).FirstOrDefault()?.Text))
                .ToList());
```

- [ ] **Step 4: Fix the `POST /categories` constructor call**

In the same file, the `POST /categories` handler returns a `CategorySummaryDto` that now needs the two extra arguments. Change:

```csharp
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null, 0, null));
```

to:

```csharp
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null, 0, null, 0, null));
```

- [ ] **Step 5: Add the per-category listing endpoint**

In the same file, after the `GET /categories/{id:int}/prompts` handler, add:

```csharp
        group.MapGet("/categories/{id:int}/one-liners", async (int id, OneLinerService oneLiners) =>
        {
            var category = await oneLiners.GetCategoryWithOneLinersAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new OneLinerCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.OneLiners
                    .OrderByDescending(o => o.CreatedAt)
                    .Select(o => new OneLinerSummaryDto(o.Id, o.Text, o.CreatedAt))
                    .ToList());

            return Results.Ok(dto);
        });
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build Fabulis.slnx`
Expected: Build succeeded, 0 errors. (If the build complains about `CategorySummaryDto` arguments anywhere else, search for other construction sites: `grep -rn "new CategorySummaryDto" src/` — there should be exactly the two handled above.)

- [ ] **Step 7: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs src/Fabulis.Server/Api/LibraryEndpoints.cs
git commit -m "$(cat <<'EOF'
Add one-liner DTOs and library wiring

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `OneLinerEndpoints` and startup registration

**Files:**
- Create: `src/Fabulis.Server/Api/OneLinerEndpoints.cs`
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Create the endpoint group**

Create `src/Fabulis.Server/Api/OneLinerEndpoints.cs`:

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class OneLinerEndpoints
{
    public static IEndpointRouteBuilder MapOneLinerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/one-liners").RequireSession();

        group.MapPost("", async (CreateOneLinerRequest body, OneLinerService oneLiners) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await oneLiners.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var created = await oneLiners.CreateOneLinerAsync(body.CategoryId, body.Text);
            var full = await oneLiners.GetOneLinerAsync(created.Id);
            return Results.Ok(ToDto(full!));
        });

        group.MapPut("/{id:int}", async (int id, UpdateOneLinerRequest body, OneLinerService oneLiners) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await oneLiners.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var updated = await oneLiners.UpdateOneLinerAsync(id, body.Text, body.CategoryId);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:int}", async (int id, OneLinerService oneLiners) =>
        {
            return await oneLiners.DeleteOneLinerAsync(id) ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static OneLinerDto ToDto(OneLiner o) => new(
        o.Id,
        o.CategoryId,
        o.Category?.Name ?? "",
        o.Text,
        o.CreatedAt,
        o.UpdatedAt);
}
```

- [ ] **Step 2: Register the service**

In `src/Fabulis.Server/Program.cs`, add the service registration after `builder.Services.AddScoped<PromptService>();`:

```csharp
builder.Services.AddScoped<PromptService>();
builder.Services.AddScoped<OneLinerService>();
```

- [ ] **Step 3: Map the endpoints**

In the same file, add the endpoint mapping after `api.MapPromptEndpoints();`:

```csharp
api.MapPromptEndpoints();
api.MapOneLinerEndpoints();
```

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build Fabulis.slnx && dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: Build succeeded; all tests pass (including the 6 `OneLinerServiceTests`).

- [ ] **Step 5: (Optional) Smoke-test the endpoints manually**

Start the server (`dotnet run --project src/Fabulis.Server`), unlock the vault, then with a valid session token:

```bash
# Create a one-liner in category 1
curl -s -X POST http://localhost:5288/api/v1/one-liners \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"categoryId":1,"text":"She smiled as she set fire to the only document proving his innocence."}'
# List them
curl -s http://localhost:5288/api/v1/categories/1/one-liners -H "Authorization: Bearer $TOKEN"
```

Expected: the POST returns the created `OneLinerDto`; the GET returns it in the `oneLiners` array.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Api/OneLinerEndpoints.cs src/Fabulis.Server/Program.cs
git commit -m "$(cat <<'EOF'
Add one-liner REST endpoints

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Client DTOs, `CategorySummary` extension, and API methods

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

> **Client build command (used in every client task):**
> `xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' build`
> If the scheme or simulator name differs, discover them with
> `xcodebuild -list -project client/Fabulis.xcodeproj` and
> `xcrun simctl list devices available`, then substitute.

- [ ] **Step 1: Extend `CategorySummary`**

In `client/Fabulis/Models/APIDtos.swift`, replace the `CategorySummary` struct with:

```swift
struct CategorySummary: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let storyCount: Int
    let latestStoryTitle: String?
    let promptCount: Int
    let latestPromptTitle: String?
    let oneLinerCount: Int
    let latestOneLinerText: String?
}
```

- [ ] **Step 2: Add one-liner DTOs**

In the same file, after the `// MARK: - Prompts` block (after `UpdatePromptRequest`), add:

```swift
// MARK: - One-liners

struct OneLinerSummary: Decodable, Identifiable, Sendable {
    let id: Int
    let text: String
    let createdAt: Date
}

struct OneLinerCategoryDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let name: String
    let createdAt: Date
    let oneLiners: [OneLinerSummary]
}

struct OneLinerDetail: Decodable, Identifiable, Sendable {
    let id: Int
    let categoryId: Int
    let categoryName: String
    let text: String
    let createdAt: Date
    let updatedAt: Date
}

struct CreateOneLinerRequest: Encodable, Sendable {
    let categoryId: Int
    let text: String
}

struct UpdateOneLinerRequest: Encodable, Sendable {
    let text: String
    let categoryId: Int
}
```

- [ ] **Step 3: Update the `CategorySummary` equality**

In `client/Fabulis/Views/Library/LibraryView.swift`, the `CategorySummary: Hashable` extension's `==` must compare the new fields or sidebar counts will go stale. Replace its `==` body with:

```swift
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool {
        lhs.id == rhs.id
            && lhs.name == rhs.name
            && lhs.createdAt == rhs.createdAt
            && lhs.storyCount == rhs.storyCount
            && lhs.latestStoryTitle == rhs.latestStoryTitle
            && lhs.promptCount == rhs.promptCount
            && lhs.latestPromptTitle == rhs.latestPromptTitle
            && lhs.oneLinerCount == rhs.oneLinerCount
            && lhs.latestOneLinerText == rhs.latestOneLinerText
    }
```

- [ ] **Step 4: Add the API client methods**

In `client/Fabulis/Services/FabulisAPIClient.swift`, after `deletePrompt(id:)`, add:

```swift
    func categoryOneLiners(categoryId: Int) async throws -> OneLinerCategoryDetail {
        try await request("GET", path: "/categories/\(categoryId)/one-liners", authed: true)
    }

    func createOneLiner(categoryId: Int, text: String) async throws -> OneLinerDetail {
        let body = CreateOneLinerRequest(categoryId: categoryId, text: text)
        return try await request("POST", path: "/one-liners", body: body, authed: true)
    }

    func updateOneLiner(id: Int, text: String, categoryId: Int) async throws -> OneLinerDetail {
        let body = UpdateOneLinerRequest(text: text, categoryId: categoryId)
        return try await request("PUT", path: "/one-liners/\(id)", body: body, authed: true)
    }

    func deleteOneLiner(id: Int) async throws {
        try await requestVoid("DELETE", path: "/one-liners/\(id)", authed: true)
    }
```

- [ ] **Step 5: Build the client to verify it compiles**

Run the client build command (top of this task).
Expected: BUILD SUCCEEDED. (No UI references the new types yet; this verifies the DTOs and API methods compile.)

- [ ] **Step 6: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift client/Fabulis/Views/Library/LibraryView.swift client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "$(cat <<'EOF'
Add one-liner client DTOs and API methods

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: `OneLinerEditSheet`

**Files:**
- Create: `client/Fabulis/Views/Library/OneLinerEditSheet.swift`

- [ ] **Step 1: Create the edit sheet**

Create `client/Fabulis/Views/Library/OneLinerEditSheet.swift`:

```swift
import SwiftUI

/// A small sheet for editing a single one-liner: change its text and/or move it
/// to another category. Seeded from the summary already in the list, so it does
/// not need to fetch the one-liner itself — only the category list for the
/// picker.
struct OneLinerEditSheet: View {
    let oneLinerId: Int
    /// Called after a successful save or delete so the presenter can reload.
    var onChanged: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var text: String
    @State private var categoryId: Int
    @State private var categories: [CategorySummary] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?

    init(oneLiner: OneLinerSummary, categoryId: Int, onChanged: (() -> Void)? = nil) {
        self.oneLinerId = oneLiner.id
        self.onChanged = onChanged
        _text = State(initialValue: oneLiner.text)
        _categoryId = State(initialValue: categoryId)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("One-liner") {
                    TextField("One-liner", text: $text, axis: .vertical)
                        .lineLimit(2...8)
                }
                Section("Category") {
                    Picker("Category", selection: $categoryId) {
                        ForEach(categories) { cat in
                            Text(cat.name).tag(cat.id)
                        }
                    }
                }
                Section {
                    Button(role: .destructive) {
                        Task { await delete() }
                    } label: {
                        Label("Delete One-liner", systemImage: "trash")
                    }
                    .disabled(saving)
                }
            }
            .navigationTitle("Edit One-liner")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await save() }
                    } label: {
                        if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                    }
                    .disabled(saving || isLoading
                        || text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .overlay { if isLoading { ProgressView() } }
            .alert("Couldn't save", isPresented: Binding(
                get: { errorMessage != nil },
                set: { if !$0 { errorMessage = nil } })) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(errorMessage ?? "")
            }
            .task { await load() }
        }
    }

    private func load() async {
        do {
            categories = try await FabulisAPIClient.shared.library().categories
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        saving = true; defer { saving = false }
        do {
            _ = try await FabulisAPIClient.shared.updateOneLiner(
                id: oneLinerId, text: text, categoryId: categoryId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func delete() async {
        saving = true; defer { saving = false }
        do {
            try await FabulisAPIClient.shared.deleteOneLiner(id: oneLinerId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
```

- [ ] **Step 2: Build the client to verify it compiles**

Run the client build command (Task 5 header).
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Library/OneLinerEditSheet.swift
git commit -m "$(cat <<'EOF'
Add OneLinerEditSheet

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: `OneLinerCategoryView`

**Files:**
- Create: `client/Fabulis/Views/Library/OneLinerCategoryView.swift`

- [ ] **Step 1: Create the category view**

Create `client/Fabulis/Views/Library/OneLinerCategoryView.swift`:

```swift
import SwiftUI

struct OneLinerCategoryView: View {
    let categoryId: Int
    let categoryName: String
    /// Called when the one-liner count changes so the Library sidebar can
    /// refresh this category's count.
    var onChanged: (() -> Void)? = nil
    var onDeleted: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var detail: OneLinerCategoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true
    @State private var newText = ""
    @State private var adding = false
    @State private var editingOneLiner: OneLinerSummary?
    @State private var showingRenameSheet = false
    @State private var showingDeleteConfirm = false
    @State private var oneLinerPendingDeletion: OneLinerSummary?

    var body: some View {
        VStack(spacing: 0) {
            composeBar
            Divider()
            content
        }
        .navigationTitle(detail?.name ?? categoryName)
        .sheet(item: $editingOneLiner) { oneLiner in
            OneLinerEditSheet(oneLiner: oneLiner, categoryId: categoryId, onChanged: {
                Task { await load() }
                onChanged?()
            })
        }
        .toolbar {
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
                    Text("This deletes the category and all its stories, prompts, and one-liners. This cannot be undone.")
               })
        .alert("Delete one-liner?",
               isPresented: Binding(
                    get: { oneLinerPendingDeletion != nil },
                    set: { if !$0 { oneLinerPendingDeletion = nil } }),
               presenting: oneLinerPendingDeletion,
               actions: { oneLiner in
                    Button("Cancel", role: .cancel) {}
                    Button("Delete", role: .destructive) {
                        Task { await deleteOneLiner(oneLiner) }
                    }
               },
               message: { _ in
                    Text("This deletes the one-liner. This cannot be undone.")
               })
        .task { await load() }
        .refreshable { await load() }
    }

    private var composeBar: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("New one-liner", text: $newText, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(1...4)
                .onSubmit { Task { await add() } }
            Button {
                Task { await add() }
            } label: {
                if adding {
                    ProgressView().controlSize(.mini)
                } else {
                    Image(systemName: "plus.circle.fill").font(.title2)
                }
            }
            .disabled(adding
                || newText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
    }

    @ViewBuilder
    private var content: some View {
        if let detail {
            if detail.oneLiners.isEmpty {
                ContentUnavailableView("No one-liners", systemImage: "quote.bubble",
                    description: Text("Type a line above and tap + to add one."))
            } else {
                List(detail.oneLiners) { oneLiner in
                    Button {
                        editingOneLiner = oneLiner
                    } label: {
                        HStack {
                            Text(oneLiner.text)
                                .font(.body)
                                .lineLimit(1...3)
                            Spacer()
                            Image(systemName: "chevron.right")
                                .font(.caption).foregroundStyle(.tertiary)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .swipeActions(edge: .trailing) {
                        Button(role: .destructive) {
                            oneLinerPendingDeletion = oneLiner
                        } label: {
                            Label("Delete", systemImage: "trash")
                        }
                    }
                    .contextMenu {
                        Button(role: .destructive) {
                            oneLinerPendingDeletion = oneLiner
                        } label: {
                            Label("Delete One-liner", systemImage: "trash")
                        }
                    }
                }
            }
        } else if isLoading {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load one-liners").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                Button("Retry") { Task { await load() } }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.categoryOneLiners(categoryId: categoryId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func add() async {
        let trimmed = newText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !adding else { return }
        adding = true; defer { adding = false }
        do {
            _ = try await FabulisAPIClient.shared.createOneLiner(categoryId: categoryId, text: trimmed)
            newText = ""
            await load()
            onChanged?()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func deleteOneLiner(_ oneLiner: OneLinerSummary) async {
        do {
            try await FabulisAPIClient.shared.deleteOneLiner(id: oneLiner.id)
            await load()
            onChanged?()
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
```

- [ ] **Step 2: Build the client to verify it compiles**

Run the client build command (Task 5 header).
Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Views/Library/OneLinerCategoryView.swift
git commit -m "$(cat <<'EOF'
Add OneLinerCategoryView with inline compose

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Wire One-liners into the library kind switcher

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryKind.swift`
- Modify: `client/Fabulis/Views/Library/CategoryRow.swift`
- Modify: `client/Fabulis/Views/Library/LibraryView.swift`

- [ ] **Step 1: Add the `.oneLiners` kind**

In `client/Fabulis/Views/Library/LibraryKind.swift`, add the case (after `prompts`) and extend both `switch`es:

```swift
enum LibraryKind: String, CaseIterable, Identifiable {
    case prompts
    case oneLiners
    case drafts
    case stories

    var id: String { rawValue }

    var label: String {
        switch self {
        case .drafts: "Drafts"
        case .stories: "Stories"
        case .prompts: "Prompts"
        case .oneLiners: "One-liners"
        }
    }

    /// Whether this kind organizes its items under the shared category
    /// taxonomy. Drafts are a flat list; stories, prompts, and one-liners are
    /// grouped by category.
    var hasCategories: Bool {
        switch self {
        case .drafts: false
        case .stories: true
        case .prompts: true
        case .oneLiners: true
        }
    }
}
```

The picker order follows declaration order, so the tabs become **Prompts · One-liners · Drafts · Stories**.

- [ ] **Step 2: Add the `.oneLiners` count string**

In `client/Fabulis/Views/Library/CategoryRow.swift`, replace `countText` with:

```swift
    private var countText: String {
        switch kind {
        case .prompts:
            "\(category.promptCount) \(category.promptCount == 1 ? "prompt" : "prompts")"
        case .oneLiners:
            "\(category.oneLinerCount) \(category.oneLinerCount == 1 ? "one-liner" : "one-liners")"
        default:
            "\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")"
        }
    }
```

- [ ] **Step 3: Extend the `LibraryView` toolbar and sidebar switches**

In `client/Fabulis/Views/Library/LibraryView.swift`:

In `toolbarContent`, change the New Category case to include `.oneLiners`:

```swift
            case .stories, .prompts, .oneLiners:
                Button { showingNewCategorySheet = true } label: {
                    Label("New Category", systemImage: "folder.badge.plus")
                }
```

In `sidebarList`, change the categories case to include `.oneLiners`:

```swift
            switch selectedKind {
            case .drafts: draftsList
            case .stories, .prompts, .oneLiners: categoriesList
            }
```

- [ ] **Step 4: Route the detail to `OneLinerCategoryView`**

In `LibraryView`'s `detail` builder, replace the `if/else` inside `case .category(let id, let name):` with a `switch` on `selectedKind`:

```swift
        case .category(let id, let name):
            NavigationStack {
                switch selectedKind {
                case .prompts:
                    PromptCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                case .oneLiners:
                    OneLinerCategoryView(categoryId: id, categoryName: name, onChanged: {
                        Task { await load() }
                    }, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                default:
                    CategoryView(categoryId: id, categoryName: name, onDeleted: {
                        selection = nil
                        Task { await load() }
                    })
                    .id(id)
                }
            }
```

- [ ] **Step 5: Update the delete-category copy**

The category-delete confirmation still says "stories and prompts". Update both known sites to mention one-liners. In `client/Fabulis/Views/Library/LibraryView.swift` and `client/Fabulis/Views/Library/PromptCategoryView.swift`, change:

```swift
Text("This deletes the category and all its stories and prompts. This cannot be undone.")
```

to:

```swift
Text("This deletes the category and all its stories, prompts, and one-liners. This cannot be undone.")
```

Confirm there are no other occurrences: `grep -rn "stories and prompts" client/` should return nothing after this step.

- [ ] **Step 6: Build the client to verify it compiles**

Run the client build command (Task 5 header).
Expected: BUILD SUCCEEDED.

- [ ] **Step 7: Manual verification in the simulator**

Launch the app against a running server, unlock, and confirm:
- The library picker shows **Prompts · One-liners · Drafts · Stories**.
- The One-liners tab lists categories with a "N one-liners" subtitle.
- Opening a category shows the compose bar; typing a line and tapping **+** adds it to the top of the list and the sidebar count increments.
- Tapping a row opens the edit sheet; editing the text saves; changing the category moves the line out of the current list.
- Swipe-to-delete removes a line and decrements the count.

- [ ] **Step 8: Commit**

```bash
git add client/Fabulis/Views/Library/LibraryKind.swift client/Fabulis/Views/Library/CategoryRow.swift client/Fabulis/Views/Library/LibraryView.swift client/Fabulis/Views/Library/PromptCategoryView.swift
git commit -m "$(cat <<'EOF'
Wire One-liners into the library kind switcher

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification

- [ ] **Server:** `dotnet build Fabulis.slnx && dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj` — all green.
- [ ] **Client:** the client build command succeeds for an iOS Simulator destination. If convenient, also build the Mac Catalyst destination.
- [ ] **End-to-end:** the manual checks in Task 8, Step 7 all pass.
- [ ] No stray references: `grep -rn "stories and prompts" client/` returns nothing.
