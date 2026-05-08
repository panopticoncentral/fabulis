# Import a Single Category from an On-Disk Library

## Goal

Extend the `import` CLI command so it can accept a path to a single
category directory in addition to a full library root. Today it only
understands a library root.

## User-visible behavior

The command surface stays the same:

```
dotnet run --project src/Fabulis.Cli -- import <path>
```

The CLI inspects `<path>` to decide what shape it has:

- **Library root** (current behavior): every immediate subdirectory is
  a category, with an optional `_drafts/` sibling.
- **Single category** (new): immediate subdirectories are story
  directories whose `*.md` files match the version filename pattern.
  The category name is the basename of `<path>`.

If `<path>` is empty or its structure does not match either shape, the
import fails with a clear error that names the path and the reason.
The CLI does not silently fall through to a no-op.

In single-category mode:

- Drafts are not imported. A `_drafts/` sibling is irrelevant in this
  mode (there is no library root to look at), and a `_drafts/` *child*
  is structurally invalid for a category and is skipped with a warning
  the same way an unrecognized story directory would be.
- All other behavior — idempotent dedupe by `(CategoryName, StoryTitle,
  VersionNumber)`, the unrecognized-filename warning, the conversation
  parser — is identical to library-root mode.

## Detection rule

Goal: distinguish "library root" from "category" by structure alone.
Applied to a directory `D`, in order:

1. **`_drafts/` child present** → library root. A `_drafts/` directory
   only ever appears at the top of a library export, so its presence
   is conclusive. (This also preserves today's behavior of allowing a
   library that contains drafts but no categories.)
2. **At least one child contains a `Version N [<Model>].md` file
   directly** → single category. Children of a category are story
   directories holding version files.
3. **At least one *grandchild* contains a version file directly, but
   no child does** → library root. Children of a library root are
   categories; grandchildren are story directories.
4. **Otherwise** → unknown. Error out.

The check stops at the first match in each direction — it does not
need to walk the whole tree.

## Architecture

All changes are in `src/Fabulis.Cli/CategoryImportService.cs`. No new
files. No changes to `Program.cs`, the database, or DTOs.

`ImportAsync(FabulisDbContext db, string rootPath)` gets a small
dispatch at the top:

1. Build a `DirectoryInfo` for `rootPath`; verify it exists.
2. Classify it via a new `DetectImportShape(DirectoryInfo)` helper that
   returns one of `LibraryRoot`, `SingleCategory`, or `Unknown`.
3. Switch on the result:
   - `LibraryRoot`: existing loop (call `ImportCategoryAsync` per
     subdirectory, then `ImportDraftsAsync` if `_drafts/` is present).
   - `SingleCategory`: call `ImportCategoryAsync(db, root, result)`
     directly. Skip drafts entirely.
   - `Unknown`: throw with a message like
     `"Could not determine whether <path> is a library root or a single
     category. Expected either category subdirectories or story
     subdirectories with Version N [<Model>].md files."`
4. `await db.SaveChangesAsync();` and return as today.

`DetectImportShape` is a private static method on
`CategoryImportService`. It uses the existing `VersionFileNamePattern`
regex so the detection criterion stays aligned with what the importer
will actually accept.

`ImportCategoryAsync` already takes a `DirectoryInfo` for the category
and uses `categoryDir.Name` as the category name. It works as-is for
the new mode — passing the user-supplied root in directly produces a
category named after that directory's basename, which is what we want.

## CLI output

The single line summary printed by `Program.cs` is unchanged:

```
Imported: N categories, N stories, N versions, N drafts
```

In single-category mode, drafts is always 0 and categories is 0 or 1
depending on whether the category already existed.

## Edge cases

- **Empty directory:** classified as `Unknown` → error.
- **Directory with only `_drafts/` and no categories:** classified as
  library root by rule 1. Behaves exactly like today: drafts are
  imported, no categories are touched.
- **Category whose name collides with an existing category in the
  vault:** existing dedupe wins — the existing category is reused, new
  stories / versions are appended. Same as library-root mode today.
- **Symlinked or junction directories:** treated as plain directories
  by `DirectoryInfo.GetDirectories()`. No special handling.
- **`<path>` ends with a trailing slash:** `DirectoryInfo.Name`
  ignores it, so the category name is correct either way.
- **`<path>` is the literal name `_drafts`:** structurally invalid
  (a `_drafts` directory does not contain version files inside story
  subdirectories) → classified as `Unknown` → error.

## README update

`src/Fabulis.Cli/README.md` gets a short addition under the `import`
section explaining that the path can be either a library root or a
single category directory, and that drafts are only read in
library-root mode.

## Testing

Manual, matching the project's existing CLI testing conventions (no
test project for `Fabulis.Cli`):

1. Export a vault with `dotnet run --project src/Fabulis.Cli --
   export /tmp/lib`.
2. Import a single category: `dotnet run --project src/Fabulis.Cli --
   import /tmp/lib/<SomeCategory>`. Confirm only that category's
   stories / versions land, drafts count is 0, and a re-run is a no-op.
3. Import the full library: `dotnet run --project src/Fabulis.Cli --
   import /tmp/lib`. Confirm behavior is unchanged.
4. Point at an empty directory and at a directory containing only
   loose `.md` files; confirm both error with a clear message.

## Out of scope

- An explicit `--category <name>` flag. The path-based detection
  covers the use case without adding a new argument.
- Importing multiple specific categories in one run (e.g.
  `--category Fantasy --category Romance`). Run the command twice.
- Renaming a category on import. The category name comes from the
  directory's basename, matching library-root behavior.
- Importing `_drafts/` independently from any category data.
- Round-trip changes to the on-disk format itself.
