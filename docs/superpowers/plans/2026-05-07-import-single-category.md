# Import a Single Category — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the `import` CLI command so it can accept either a full library root (today's behavior) or a single category directory, auto-detecting which shape was supplied.

**Architecture:** Add a private `DetectImportShape` helper to `CategoryImportService` that classifies a `DirectoryInfo` by inspecting its immediate children (and one level of grandchildren) for `Version N [<Model>].md` files. `ImportAsync` dispatches to the existing per-category loop or, in single-category mode, calls `ImportCategoryAsync` directly with the supplied directory.

**Tech Stack:** .NET 10, EF Core, `System.IO` (`DirectoryInfo`), existing `[GeneratedRegex]` pattern.

**Spec:** `docs/superpowers/specs/2026-05-07-import-single-category-design.md`

**Note on testing:** `Fabulis.Cli` has no test project. Each task ends with a `dotnet build` check; the final task is manual end-to-end verification using a real export.

---

## File Structure

One file modified, one documentation file updated.

- **Modify** `src/Fabulis.Cli/CategoryImportService.cs` — add `ImportShape` enum, `DetectImportShape` static helper, and refactor `ImportAsync` to dispatch on shape.
- **Modify** `src/Fabulis.Cli/README.md` — document the dual-mode import behavior under the existing `import` description.

No changes to `Program.cs`, the database schema, DTOs, or any other file.

---

### Task 1: Add shape detection and dispatch to `ImportAsync`

**Files:**
- Modify: `src/Fabulis.Cli/CategoryImportService.cs`

- [ ] **Step 1: Replace the body of `ImportAsync` with the shape-dispatching version**

In `src/Fabulis.Cli/CategoryImportService.cs`, replace the current `ImportAsync` method:

```csharp
public async Task<ImportResult> ImportAsync(FabulisDbContext db, string rootPath)
{
    var result = new ImportResult();
    var root = new DirectoryInfo(rootPath);
    if (!root.Exists)
        throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

    foreach (var subDir in root.GetDirectories().OrderBy(d => d.Name))
    {
        if (string.Equals(subDir.Name, "_drafts", StringComparison.OrdinalIgnoreCase))
            continue;

        await ImportCategoryAsync(db, subDir, result);
    }

    var draftsDir = root.GetDirectories("_drafts").FirstOrDefault();
    if (draftsDir is not null)
        await ImportDraftsAsync(db, draftsDir, result);

    await db.SaveChangesAsync();
    return result;
}
```

with:

```csharp
public async Task<ImportResult> ImportAsync(FabulisDbContext db, string rootPath)
{
    var result = new ImportResult();
    var root = new DirectoryInfo(rootPath);
    if (!root.Exists)
        throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

    var shape = DetectImportShape(root);
    switch (shape)
    {
        case ImportShape.LibraryRoot:
            foreach (var subDir in root.GetDirectories().OrderBy(d => d.Name))
            {
                if (string.Equals(subDir.Name, "_drafts", StringComparison.OrdinalIgnoreCase))
                    continue;

                await ImportCategoryAsync(db, subDir, result);
            }

            var draftsDir = root.GetDirectories("_drafts").FirstOrDefault();
            if (draftsDir is not null)
                await ImportDraftsAsync(db, draftsDir, result);
            break;

        case ImportShape.SingleCategory:
            await ImportCategoryAsync(db, root, result);
            break;

        case ImportShape.Unknown:
        default:
            throw new InvalidOperationException(
                $"Could not determine whether '{rootPath}' is a library root or a single category. " +
                "Expected either category subdirectories or story subdirectories containing " +
                "'Version N [<Model>].md' files.");
    }

    await db.SaveChangesAsync();
    return result;
}
```

- [ ] **Step 2: Add the `DetectImportShape` helper and the `ImportShape` enum**

Inside the `CategoryImportService` class, immediately after the `ImportAsync` method, add:

```csharp
private static ImportShape DetectImportShape(DirectoryInfo root)
{
    // Rule 1: a _drafts/ child is conclusive evidence of a library root.
    if (root.GetDirectories("_drafts").Length > 0)
        return ImportShape.LibraryRoot;

    var versionRegex = VersionFileNamePattern();
    var children = root.GetDirectories();

    // Rule 2: any child directly contains a version file -> single category.
    foreach (var child in children)
    {
        if (child.GetFiles("*.md").Any(f => versionRegex.IsMatch(f.Name)))
            return ImportShape.SingleCategory;
    }

    // Rule 3: any grandchild contains a version file (and no child did) -> library root.
    foreach (var child in children)
    {
        foreach (var grandchild in child.GetDirectories())
        {
            if (grandchild.GetFiles("*.md").Any(f => versionRegex.IsMatch(f.Name)))
                return ImportShape.LibraryRoot;
        }
    }

    return ImportShape.Unknown;
}

private enum ImportShape
{
    LibraryRoot,
    SingleCategory,
    Unknown
}
```

`VersionFileNamePattern()` is the existing `[GeneratedRegex]` method already declared at the top of the class — reuse it directly.

- [ ] **Step 3: Build the solution to verify it compiles**

Run:

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds with no errors. Warnings unchanged from before.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Cli/CategoryImportService.cs
git commit -m "Detect single-category vs library-root import shape"
```

---

### Task 2: Document the new behavior in the CLI README

**Files:**
- Modify: `src/Fabulis.Cli/README.md`

- [ ] **Step 1: Update the `import` description**

In `src/Fabulis.Cli/README.md`, find the existing bullet:

```
- `import` reads a directory tree from `<source>`. Each immediate
  subdirectory is treated as a category. An optional `_drafts/` directory
  alongside the categories is read back into the `Drafts` table.
```

Replace it with:

```
- `import` reads a directory tree from `<source>`. The CLI auto-detects
  whether `<source>` is:
  - a **library root** — each immediate subdirectory is treated as a
    category, and an optional `_drafts/` directory alongside the
    categories is read back into the `Drafts` table; or
  - a **single category** — the immediate subdirectories are treated as
    stories and the category name is the basename of `<source>`. Drafts
    are not read in this mode.

  Detection rule, applied in order: a `_drafts/` child means library
  root; otherwise, if any immediate subdirectory contains
  `Version N [<Model>].md` files directly, the source is treated as a
  single category; otherwise, if any grand-subdirectory contains those
  files, the source is treated as a library root. If none of these
  match, import errors out.
```

- [ ] **Step 2: Commit**

```bash
git add src/Fabulis.Cli/README.md
git commit -m "Document single-category import in CLI README"
```

---

### Task 3: Manual end-to-end verification

**Files:** None modified. This task is a verification checklist; do not commit anything.

- [ ] **Step 1: Make sure the server is not running**

Importing while the server is writing to the same vault produces
inconsistent results. If a `dotnet run --project src/Fabulis.Server`
process is alive, stop it (Ctrl-C in its terminal) before continuing.

- [ ] **Step 2: Export the current vault to a scratch directory**

Run:

```bash
rm -rf /tmp/fabulis-import-test
dotnet run --project src/Fabulis.Cli -- export /tmp/fabulis-import-test
```

When prompted, enter the vault password. Expected output (counts will
vary):

```
Exported: N categories, N stories, N versions, N drafts
```

If `N categories` is 0, the rest of this task can't be verified
meaningfully — create at least one category in the app first.

- [ ] **Step 3: Pick a category subdirectory for the single-category test**

Run:

```bash
ls /tmp/fabulis-import-test
```

Pick the name of any category directory (call it `<Cat>`). Confirm it
contains story subdirectories with `Version N [...].md` files:

```bash
ls "/tmp/fabulis-import-test/<Cat>"
```

- [ ] **Step 4: Single-category import (idempotent re-run)**

Run:

```bash
dotnet run --project src/Fabulis.Cli -- import "/tmp/fabulis-import-test/<Cat>"
```

Expected output:

```
Imported: 0 categories, 0 stories, 0 versions, 0 drafts
```

(All four are 0 because the data already exists in the vault, the
category is reused, and drafts aren't read in single-category mode.)
The command should exit with status 0 and not print any "could not
determine" error.

- [ ] **Step 5: Library-root import still works**

Run:

```bash
dotnet run --project src/Fabulis.Cli -- import /tmp/fabulis-import-test
```

Expected output:

```
Imported: 0 categories, 0 stories, 0 versions, 0 drafts
```

(Idempotent: no new rows.) Confirms the library-root path still
dispatches correctly.

- [ ] **Step 6: Empty / ambiguous directory errors out**

Run:

```bash
mkdir -p /tmp/fabulis-empty-import
dotnet run --project src/Fabulis.Cli -- import /tmp/fabulis-empty-import
```

Expected: command exits non-zero with output that includes:

```
error: Could not determine whether '/tmp/fabulis-empty-import' is a library root or a single category.
```

- [ ] **Step 7: Clean up scratch directories**

Run:

```bash
rm -rf /tmp/fabulis-import-test /tmp/fabulis-empty-import
```

- [ ] **Step 8: Confirm fresh-vault behavior with single-category import**

This is the headline scenario — importing one category into a vault
that does not already have it.

1. Move (or rename) the existing vault out of the way:

   ```bash
   mv src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db \
      src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db.bak
   ```

   (Skip this if you'd prefer to use a separate scratch DB via
   `FABULIS_DB_PATH` — either is fine, as long as the target vault
   does not already contain the category you're about to import.)

2. Start the server once so it creates a fresh vault, sets the
   password, and exits cleanly. Or use the desktop client's onboarding
   flow. Whichever path you take, end with: server stopped, fresh
   vault on disk, vault password known.

3. Re-export from the *backup* vault to a scratch directory if you
   don't already have one with at least one category:

   ```bash
   FABULIS_DB_PATH=$(pwd)/src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db.bak \
   dotnet run --project src/Fabulis.Cli -- export /tmp/fabulis-singlecat-source
   ```

4. Import a single category into the fresh vault:

   ```bash
   dotnet run --project src/Fabulis.Cli -- import "/tmp/fabulis-singlecat-source/<Cat>"
   ```

   Expected:

   ```
   Imported: 1 categories, M stories, V versions, 0 drafts
   ```

   where `M` and `V` match the story / version counts of `<Cat>` in
   the source library, and drafts is `0`.

5. Start the server, open the client, and confirm only the imported
   category is visible (its stories and versions intact).

6. Stop the server and restore your real vault:

   ```bash
   rm -rf /tmp/fabulis-singlecat-source
   mv src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db.bak \
      src/Fabulis.Server/bin/Debug/net10.0/data/fabulis.db
   ```

If every step above produced the expected result, the feature works.
