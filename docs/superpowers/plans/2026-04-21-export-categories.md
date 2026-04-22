# Export Categories — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new `/export` Razor page that writes every Category, Story, StoryVersion, and StoryMessage in the database to a directory tree of markdown files, matching the format that Import reads.

**Architecture:** A new scoped `CategoryExportService` walks the DB, creates one directory per category and per story, and writes one markdown file per version using the same `**Me:**` / `**StoryTeller:**` delimiters Import's regex accepts. A new `Export.razor` page mirrors `Import.razor`: a path textbox, a submit button, and a vault-unlocked guard. The destination path must not already exist.

**Tech Stack:** ASP.NET Core Blazor Server on .NET 10, Entity Framework Core, `System.IO` (`Directory.CreateDirectory`, `File.WriteAllTextAsync`).

**Spec:** `docs/superpowers/specs/2026-04-21-export-categories-design.md`

**Note on testing:** This project has no test suite. Each implementation task ends with a `dotnet build` check to catch compile errors; the final task is a manual round-trip verification in the browser.

---

## File Structure

Two new files, two existing files modified.

- **Create** `src/Fabulis.Server/Data/CategoryExportService.cs` — scoped service with `ExportAsync(FabulisDbContext db, string destinationPath)` and `ExportResult` result class.
- **Create** `src/Fabulis.Server/Components/Pages/Export.razor` — Razor page at `/export` that calls the service and shows the result or error.
- **Modify** `src/Fabulis.Server/Program.cs` — register `CategoryExportService` as scoped (one line added).
- **Modify** `src/Fabulis.Server/Components/Layout/MainLayout.razor` — add an `Export` nav link next to `Import` (one line added).

---

### Task 1: Create CategoryExportService

**Files:**
- Create: `src/Fabulis.Server/Data/CategoryExportService.cs`

- [ ] **Step 1: Create the file with the full service implementation**

Create `src/Fabulis.Server/Data/CategoryExportService.cs` with exactly this content:

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class CategoryExportService(ILogger<CategoryExportService> logger)
{
    public async Task<ExportResult> ExportAsync(FabulisDbContext db, string destinationPath)
    {
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            throw new IOException($"Destination already exists: {destinationPath}");

        var categories = await db.Categories
            .Include(c => c.Stories)
                .ThenInclude(s => s.Versions)
                    .ThenInclude(v => v.Messages)
            .OrderBy(c => c.Name)
            .ToListAsync();

        Directory.CreateDirectory(destinationPath);

        var result = new ExportResult();

        foreach (var category in categories)
        {
            var exportableStories = category.Stories
                .Where(s => s.Versions.Any(v => v.Messages.Count > 0))
                .OrderBy(s => s.Title)
                .ToList();

            if (exportableStories.Count == 0)
                continue;

            var categoryDir = Path.Combine(destinationPath, category.Name);
            Directory.CreateDirectory(categoryDir);
            result.CategoriesExported++;

            foreach (var story in exportableStories)
            {
                var exportableVersions = story.Versions
                    .Where(v => v.Messages.Count > 0)
                    .OrderBy(v => v.VersionNumber)
                    .ToList();

                var storyDir = Path.Combine(categoryDir, story.Title);
                Directory.CreateDirectory(storyDir);
                result.StoriesExported++;

                foreach (var version in exportableVersions)
                {
                    var fileName = $"Version {version.VersionNumber} [{version.ModelName}].md";
                    var filePath = Path.Combine(storyDir, fileName);
                    var content = FormatConversation(version.Messages);
                    await File.WriteAllTextAsync(filePath, content);
                    result.VersionsExported++;
                    logger.LogInformation("Wrote {FilePath}", filePath);
                }
            }
        }

        return result;
    }

    private static string FormatConversation(List<StoryMessage> messages)
    {
        var ordered = messages.OrderBy(m => m.SortOrder).ToList();
        var sb = new StringBuilder();

        foreach (var message in ordered)
        {
            var label = message.Role switch
            {
                MessageRole.Prompt => "**Me:**",
                MessageRole.Response => "**StoryTeller:**",
                _ => throw new InvalidOperationException($"Unknown role: {message.Role}")
            };

            sb.AppendLine(label);
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public class ExportResult
{
    public int CategoriesExported { get; set; }
    public int StoriesExported { get; set; }
    public int VersionsExported { get; set; }
}
```

Notes on why the code looks like this (do not add these as comments in the file):
- `Directory.Exists` AND `File.Exists` because a path with no extension could be either on disk.
- Eager `Include` chain loads the whole graph in one round trip — the export does not stream; the DB is expected to be small.
- `exportableStories.Count == 0` skip prevents creating an empty category directory.
- `OrderBy` on name, title, and version number makes the on-disk layout deterministic.
- `AppendLine` after both the label and the content produces exactly one blank line between them and one blank line between messages; a final `AppendLine` produces the trailing newline at EOF required by the spec.
- `File.WriteAllTextAsync` defaults to UTF-8.
- `MessageRole` values (`Prompt`, `Response`) come from `src/Fabulis.Server/Data/MessageRole.cs`.

- [ ] **Step 2: Build the project**

Run from the repo root:

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds. The new class is not yet wired up, so unused-symbol warnings are acceptable but there should be no errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Data/CategoryExportService.cs
git commit -m "Add CategoryExportService"
```

---

### Task 2: Register CategoryExportService in Program.cs

**Files:**
- Modify: `src/Fabulis.Server/Program.cs:20`

- [ ] **Step 1: Add the service registration**

Open `src/Fabulis.Server/Program.cs`. Line 20 currently reads:

```csharp
builder.Services.AddScoped<CategoryImportService>();
```

Insert a new line immediately after it so that lines 20–21 read:

```csharp
builder.Services.AddScoped<CategoryImportService>();
builder.Services.AddScoped<CategoryExportService>();
```

- [ ] **Step 2: Build the project**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Program.cs
git commit -m "Register CategoryExportService in DI"
```

---

### Task 3: Create the Export Razor page

**Files:**
- Create: `src/Fabulis.Server/Components/Pages/Export.razor`

- [ ] **Step 1: Create the page**

Create `src/Fabulis.Server/Components/Pages/Export.razor` with exactly this content:

```razor
@page "/export"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject CategoryExportService Exporter
@inject NavigationManager Nav
@rendermode InteractiveServer

<h1>Export All</h1>

@if (ErrorMessage is not null)
{
    <p class="error-message">@ErrorMessage</p>
}

@if (Result is not null)
{
    <div class="success-message">
        <p>Export complete!</p>
        <ul>
            <li>Categories exported: @Result.CategoriesExported</li>
            <li>Stories exported: @Result.StoriesExported</li>
            <li>Versions exported: @Result.VersionsExported</li>
        </ul>
        <a href="/library">Go to Library</a>
    </div>
}

<div class="add-form">
    <EditForm Model="this" OnValidSubmit="RunExport" FormName="export">
        <InputText @bind-Value="DirectoryPath" placeholder="Destination directory (must not exist)..." />
        <button type="submit" disabled="@IsExporting">
            @(IsExporting ? "Exporting..." : "Export")
        </button>
    </EditForm>
</div>

<p class="empty-message">Enter the full path to a directory that does not yet exist. It will be created and every category, story, and version in the database will be written as markdown files.</p>

@code {
    [SupplyParameterFromForm]
    private string? DirectoryPath { get; set; }

    private ExportResult? Result { get; set; }
    private string? ErrorMessage { get; set; }
    private bool IsExporting { get; set; }

    protected override void OnInitialized()
    {
        if (!Vault.IsUnlocked)
            Nav.NavigateTo("/unlock");
    }

    private async Task RunExport()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        ErrorMessage = null;
        Result = null;
        IsExporting = true;

        try
        {
            Result = await Exporter.ExportAsync(Db, DirectoryPath.Trim());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }
}
```

This is a direct mirror of `Components/Pages/Import.razor` with the service, form name, field names, button labels, and result properties changed for export. The vault guard, the try/catch pattern, the `IsExporting` disable flag, and the navigation link to `/library` all match Import's behavior.

- [ ] **Step 2: Build the project**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds. The page resolves `CategoryExportService` via DI registered in Task 2 and `FabulisDbContext` / `VaultService` already registered in `Program.cs`.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Export.razor
git commit -m "Add /export page"
```

---

### Task 4: Add Export link to the main nav

**Files:**
- Modify: `src/Fabulis.Server/Components/Layout/MainLayout.razor:8`

- [ ] **Step 1: Add the nav link**

Open `src/Fabulis.Server/Components/Layout/MainLayout.razor`. Line 8 currently reads:

```razor
        <a href="/import">Import</a>
```

Insert a new line immediately after it so that lines 8–9 read:

```razor
        <a href="/import">Import</a>
        <a href="/export">Export</a>
```

- [ ] **Step 2: Build the project**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Layout/MainLayout.razor
git commit -m "Add Export link to main nav"
```

---

### Task 5: Manual round-trip verification

No code changes. This task validates that export produces output Import can read back.

- [ ] **Step 1: Start the server**

```bash
dotnet run --project src/Fabulis.Server
```

Note the URL it binds to (typically `https://localhost:7xxx`). Leave it running while you complete the remaining steps in a browser.

- [ ] **Step 2: Unlock the vault**

Navigate to the server URL. If prompted, go through `/unlock` and enter the database password.

- [ ] **Step 3: Confirm the Export link appears in the nav**

On any page, the nav should show: `Fabulis | Library | New Story | Import | Export | Storytellers | Settings`. Click `Export`.

- [ ] **Step 4: Confirm the destination-exists error**

Enter a path that already exists, e.g. `/tmp` (macOS/Linux). Click **Export**. The page should show an error like `Destination already exists: /tmp`. The success panel should NOT appear.

- [ ] **Step 5: Run a successful export**

Pick a path that does not exist, e.g. `/tmp/fabulis-export-test-1`. Click **Export**. The page should show:

- `Export complete!`
- Three non-zero counts (assuming the DB has data)
- A `Go to Library` link

- [ ] **Step 6: Inspect the on-disk layout**

```bash
ls -R /tmp/fabulis-export-test-1
```

Expected: one subdirectory per category, each containing one subdirectory per story, each containing files named `Version N [ModelName].md`. Categories / stories / versions with no messages should be absent (no empty directories).

- [ ] **Step 7: Inspect a file's contents**

```bash
cat "/tmp/fabulis-export-test-1/<some category>/<some story>/Version 1 [<some model>].md"
```

Expected: the conversation as alternating `**Me:**` and `**StoryTeller:**` blocks, each followed by a blank line, then the content, then a blank line. File should end with a trailing newline.

- [ ] **Step 8: Round-trip via Import**

In the browser, navigate to `/import`. Enter the path to one of the exported category directories, e.g. `/tmp/fabulis-export-test-1/<some category>`. Click **Import**.

Expected: the success panel reports `Categories created: 0`, `Stories created: 0`, `Versions created: 0` (because the DB already has all of this data — Import is idempotent and skips existing rows). No errors.

If instead Import reports zero versions created AND logs warnings about "No conversation turns found" for files you know contain turns, the delimiter labels do not match what Import's regex accepts — stop and investigate before claiming success.

- [ ] **Step 9: Clean up**

Stop the server (Ctrl+C). Delete the test export directory:

```bash
rm -rf /tmp/fabulis-export-test-1
```

- [ ] **Step 10: No commit for this task** — manual verification only.
