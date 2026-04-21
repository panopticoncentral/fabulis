# Export Categories to Disk

## Goal

Provide an Export feature that is the inverse of the existing Import feature: serialize every category / story / version / message in the database to a directory tree of markdown files that Import can read back.

## User-visible behavior

A new Razor page at `/export`, linked from the main nav next to `Import`, with:

- a textbox for a destination directory path
- an "Export" button (shows "Exporting..." while running, disabled during the run)
- on success: a panel showing counts of categories, stories, and versions exported, plus a link to `/library`
- on failure: an error message showing the exception's message
- vault-locked guard: if the vault is not unlocked, redirect to `/unlock` on page init

The destination path must not exist. If it exists (as file or directory), export fails with a clear error. Export creates the destination directory itself.

## File layout written to disk

Mirror of what Import reads:

```
<destination>/
  <CategoryName>/
    <StoryTitle>/
      Version 1 [<ModelName>].md
      Version 2 [<ModelName>].md
      ...
```

Version file contents:

```
**Me:**

<prompt message content>

**StoryTeller:**

<response message content>

**Me:**

<next prompt>

...
```

- Messages written in `SortOrder` order
- `MessageRole.Prompt` → `**Me:**`, `MessageRole.Response` → `**StoryTeller:**`
- One blank line between the delimiter and content, one blank line between messages
- UTF-8, trailing newline at EOF
- These exact labels are what Import's regex accepts, so files round-trip

## Scope: what is exported

- Every `Category` in the database
- Every `Story` under each category
- Every `StoryVersion` under each story
- Every `StoryMessage` under each version

Skip rules (no empty folders / files on disk):

- Skip a category that has no stories
- Skip a story that has no versions
- Skip a version that has no messages

## Architecture

Two new files, mirroring the Import pair:

- `src/Fabulis.Server/Data/CategoryExportService.cs` — scoped service, exposes `ExportAsync(FabulisDbContext db, string destinationPath) -> Task<ExportResult>`
- `src/Fabulis.Server/Components/Pages/Export.razor` — `/export` page

Wiring:

- `Program.cs`: `builder.Services.AddScoped<CategoryExportService>();`
- `Components/Layout/MainLayout.razor`: add `<a href="/export">Export</a>` next to the Import link in the nav

`ExportResult`:

```csharp
public class ExportResult
{
    public int CategoriesExported { get; set; }
    public int StoriesExported { get; set; }
    public int VersionsExported { get; set; }
}
```

## Service flow

`CategoryExportService.ExportAsync`:

1. If `Directory.Exists(path) || File.Exists(path)`, throw with message `"Destination already exists: <path>"`.
2. Load all categories eagerly with `Include(c => c.Stories).ThenInclude(s => s.Versions).ThenInclude(v => v.Messages)`.
3. `Directory.CreateDirectory(destinationPath)`.
4. For each category (ordered by name) that contains at least one story with at least one version with at least one message:
   1. Create `<destination>/<CategoryName>`.
   2. Increment `CategoriesExported`.
   3. For each story (ordered by title) with at least one version with at least one message:
      1. Create `<destination>/<CategoryName>/<StoryTitle>`.
      2. Increment `StoriesExported`.
      3. For each version (ordered by `VersionNumber`) with at least one message:
         1. Write `Version <N> [<ModelName>].md` with the conversation content described above.
         2. Increment `VersionsExported`.
5. Return the `ExportResult`.

No cleanup on mid-export failure: whatever was written stays on disk so the user can inspect and delete manually. The destination-must-not-exist rule means a retry picks a fresh path.

## Edge cases

- **Empty database (no categories):** `Directory.CreateDirectory` runs, all counts are zero, no error.
- **Category with no stories / story with no versions / version with no messages:** skipped per rules above.
- **Invalid filesystem characters in a name** (`/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|`): let the `System.IO` call throw. The resulting error message names the offending path, which is enough for the user to fix the name in the DB. No sanitization — would break round-tripping.
- **Duplicate `VersionNumber` values within a story:** not expected (no unique constraint enforced in code). Filename collision will throw `IOException` naturally.
- **Vault locked:** page redirects to `/unlock`; service is never called.

## Testing

No automated tests. The Import side has no test project; Export matches. Manual verification: import a known category, export the DB, diff the re-import against the first import.

## Out of scope

- Filtering (export one category / one story): always exports everything.
- Round-trip extensions (accepting `**Prompt:**` / `**Storyteller:**` in Import): explicitly rejected — export writes `**Me:**` / `**StoryTeller:**` to match the existing Import regex exactly.
- CLI entry point: only the Razor page is in scope.
- Exporting `Storyteller`, `Draft`, or settings data: only `Category` / `Story` / `StoryVersion` / `StoryMessage`.
- Atomic / transactional export (write to temp dir then rename): simple path chosen; partial output on failure is acceptable.
