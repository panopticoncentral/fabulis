# One-liners — design

**Date:** 2026-06-25
**Status:** Approved, pending implementation plan

## Summary

Add a fourth kind of library content, **One-liners**, alongside Prompts,
Drafts, and Stories. A *one-liner* is a single evocative line that can later
seed a story — e.g. *"She smiled as she set fire to the only document proving
his innocence."* One-liners are organized under the same `Category` taxonomy as
Stories and Prompts, but each is stored separately and has **no title** — it is
just its line of text.

This phase covers **defining one-liners and managing them in the UI** only.
Story generation from a one-liner (combining the line with a secondary framing
prompt such as *"Start the story from the beginning. Give a lot of
background…"*) is explicitly **out of scope** and deferred.

## Decisions

- **Shared categories.** One-liners reuse the existing `Category` taxonomy. A
  single category can hold stories, prompts, *and* one-liners.
- **Single text field, no title.** A one-liner is just its line, so the entity
  is a flat row with a `Text` column — no title, and no child-message table
  (unlike `Prompt`/`PromptMessage`). A title would be overkill; the list row
  shows the line itself.
- **Lightweight inline create/edit (UX Approach A below).** A compose field at
  the top of the category's list captures lines quickly; editing happens in a
  small sheet — not a dedicated full-screen editor like `PromptEditorView`.
- **Create with text supplied directly.** Unlike prompts (create-empty, then
  edit), the one-liner `POST` carries the text from the compose field, so a
  one-liner is never empty.
- **Newest-first ordering.** The list orders by `CreatedAt` descending, so a
  newly added line appears directly under the compose field — rather than the
  alphabetical ordering used for titled prompts.
- **No detail-fetch endpoint.** The edit sheet seeds from the summary already
  in the list (the line is short and is not truncated server-side), so there is
  no `GET /one-liners/{id}`.

## Data model: chosen approach

**Approach A — new flat `OneLiner` entity (chosen).** Mirror `Prompt` but
without `Title` and without a child-message table. One column of content.

Rejected alternatives:

- **B — reuse `Prompt` with a "kind" flag / a single message.** A prompt's
  title and ordered message list are meaningless for a one-liner; the shared
  endpoints and the full-screen editor would branch on the flag everywhere.
- **C — store one-liners as rows on `Category` (e.g. a delimited list).** No
  per-item ids, timestamps, or clean moves between categories. Fragile.

## UX: chosen approach

**Approach A — lightweight inline (chosen).** Fast capture of many short lines:
an inline compose field for adds, a tap-to-edit sheet for edits and
category-moves.

Rejected alternatives:

- **B — modal sheet for both add and edit.** Heavier for the common case
  (adding many lines in a row).
- **C — mirror `PromptEditorView` (push a full-screen editor per line).**
  Maximum consistency with prompts, but far too heavyweight for a single line.

## Server changes

### Entities

```csharp
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

`Category` gains `public List<OneLiner> OneLiners { get; set; } = [];`.

### `OneLinerService`

New service mirroring `PromptService`:

- `CreateOneLinerAsync(int categoryId, string text)` — trims `text`; sets
  `CreatedAt`/`UpdatedAt`.
- `GetCategoryWithOneLinersAsync(int categoryId)` — category + its one-liners.
- `UpdateOneLinerAsync(int id, string text, int categoryId)` — updates text and
  category, bumps `UpdatedAt`.
- `DeleteOneLinerAsync(int id)`.
- `CategoryExistsAsync(int categoryId)`.

### `FabulisDbContext`

- Add `DbSet<OneLiner> OneLiners`.
- In `EnsureSchemaUpdatedAsync`, add a `CREATE TABLE IF NOT EXISTS` statement
  following the Prompts precedent so **existing vaults** gain the table
  (`EnsureCreated` only covers fresh databases and the in-memory test DB). The
  foreign key cascades: `OneLiners.CategoryId → Categories(Id) ON DELETE
  CASCADE`.

```sql
CREATE TABLE IF NOT EXISTS OneLiners (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CategoryId INTEGER NOT NULL,
    Text TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
);
```

The required (non-nullable) `CategoryId` relationship makes EF Core cascade
deletes by convention, so the test path (`EnsureCreated`) matches the raw-SQL
production path.

### DTOs

New records in `Dtos.cs`:

```csharp
public sealed record OneLinerSummaryDto(
    int Id, string Text, DateTime CreatedAt);

public sealed record OneLinerCategoryDto(           // category + its one-liners
    int Id, string Name, DateTime CreatedAt,
    IReadOnlyList<OneLinerSummaryDto> OneLiners);

public sealed record OneLinerDto(
    int Id, int CategoryId, string CategoryName, string Text,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateOneLinerRequest(int CategoryId, string Text);
public sealed record UpdateOneLinerRequest(string Text, int CategoryId);
```

Extend the existing `CategorySummaryDto` with two fields (for parity with the
story/prompt count+latest pairs; `CategoryRow` uses the count, and the latest
text is available for future use):

```csharp
public sealed record CategorySummaryDto(
    int Id, string Name, DateTime CreatedAt,
    int StoryCount, string? LatestStoryTitle,
    int PromptCount, string? LatestPromptTitle,
    int OneLinerCount, string? LatestOneLinerText);   // added
```

### Endpoints

Extend `LibraryEndpoints`:

- `GET /library` — `.Include(c => c.OneLiners)`; populate `OneLinerCount` and
  `LatestOneLinerText` (text of the most recently created line) per category.
- `GET /categories/{id}/one-liners` — returns `OneLinerCategoryDto`, one-liners
  ordered by `CreatedAt` **descending**.

New `OneLinerEndpoints.cs` (`/one-liners` group, `.RequireSession()`):

- `POST /one-liners` — `CreateOneLinerRequest`; rejects blank `Text` (400) and
  a non-existent category (400). Returns the full `OneLinerDto` so the client
  has the new id.
- `PUT /one-liners/{id}` — `UpdateOneLinerRequest`; rejects blank `Text`;
  updates text + `CategoryId`, bumps `UpdatedAt`. Returns `OneLinerDto`.
- `DELETE /one-liners/{id}` — removes the one-liner.

Wire `MapOneLinerEndpoints()` into startup alongside the other endpoint groups.

## Client changes

### `LibraryKind`

Add `.oneLiners`, positioned right after `.prompts`:

```swift
case oneLiners          // label "One-liners", hasCategories = true
```

Tabs become **Prompts · One-liners · Drafts · Stories**. *Note:* a 4-segment
picker with "One-liners" is a little tight on a narrow iPhone; if it crowds in
practice, shorten the label to "Lines". Start with "One-liners".

### DTOs (`APIDtos.swift`)

- `OneLinerSummary { id, text, createdAt }`
- `OneLinerCategoryDetail { id, name, createdAt, oneLiners: [OneLinerSummary] }`
- `OneLinerDetail { id, categoryId, categoryName, text, createdAt, updatedAt }`
- Extend `CategorySummary` with `oneLinerCount: Int` and
  `latestOneLinerText: String?` (and update its `==`).
- Request bodies: `CreateOneLinerRequest { categoryId, text }`,
  `UpdateOneLinerRequest { text, categoryId }`.

### `FabulisAPIClient`

Add: `categoryOneLiners(categoryId:)`, `createOneLiner(categoryId:text:)`,
`updateOneLiner(id:text:categoryId:)`, `deleteOneLiner(id:)`.

### `LibraryView`

- Picker is already driven by `LibraryKind.allCases`, so the new tab appears
  automatically.
- Extend the three kind `switch` sites to treat `.oneLiners` like the other
  category-backed kinds:
  - **toolbar** leading button: **New Category** (categories are shared).
  - **sidebar list**: render `categoriesList`.
  - **detail** builder: when a `.category` is selected under `.oneLiners`, show
    `OneLinerCategoryView`; otherwise the existing routing.
- Update the "Delete category?" message copy to "…stories, prompts, **and
  one-liners**."

### `CategoryRow`

Add the `.oneLiners` count string: `"N one-liner" / "N one-liners"`.

### `OneLinerCategoryView` (new)

The lightweight inline flow:

- Loads `GET /categories/{id}/one-liners`.
- **Compose field** pinned at the top: a multi-line `TextField` + **Add** button
  (disabled when blank). Add → `POST /one-liners` for this category, clear the
  field, reload, and notify the sidebar via `onChanged` so its count updates.
- **List** of one-liners (newest first): each row shows the line
  (`lineLimit(1...3)`). Tap a row → presents the edit sheet. Swipe-to-delete on
  rows, mirroring `PromptCategoryView`.
- Reuses the shared category rename/delete controls. The category-delete
  confirmation copy includes one-liners (see `LibraryView`).

### `OneLinerEditSheet` (new)

A small modal sheet seeded from the tapped `OneLinerSummary` (text) plus the
current category id:

- A `TextEditor` for the line and a category `Picker` (populated from the
  loaded `categories`) so a line can be moved between categories.
- **Save** sends `PUT /one-liners/{id}`; **Delete** (destructive) sends
  `DELETE`. On dismiss, the category view reloads and notifies the sidebar.

## Out of scope (deferred)

- Story generation from a one-liner + secondary framing prompt.
- Bulk import/export: the CLI does not handle `Prompt`s today, so one-liners are
  likewise excluded. Record any deferral in `BACKLOG.md` during implementation
  if appropriate.

## Testing

- Server: `OneLinerServiceTests.cs` mirroring `PromptServiceTests` — create with
  text, reject/normalize blank text, update text + move category, delete,
  cascade delete when the category is removed; `/library` returns correct
  one-liner counts; schema bootstrap creates the table on a pre-existing vault.
- Client: builds for an iOS Simulator destination and Mac Catalyst; the
  One-liners tab lists categories, drills into a category, adds a line via the
  compose field, edits it (and moves its category) via the sheet, deletes one,
  and the sidebar count stays correct.
