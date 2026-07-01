# Tropes — design

**Date:** 2026-07-01
**Status:** Approved, pending implementation plan

## Summary

Add a fifth kind of library content, **Tropes**, alongside Prompts, One-liners,
Drafts, and Stories. A *trope* is a single untitled text fragment meant to be
slotted into a larger prompt later — e.g. *"a haunted lighthouse"* or *"enemies
who become allies"*. The eventual use is to build a prompt such as:

> Can you give me 5 story ideas that involve `<<trope>>`. Include at least a
> paragraph or two about each story idea.

Tropes are organized under the same `Category` taxonomy as Stories, Prompts,
and One-liners, but each is stored separately and has **no title** — it is just
its fragment of text.

This phase covers **defining tropes and managing them in the UI** only.
Generating prompts (or stories) *from* a trope — the `<<trope>>` substitution
and its surrounding template — is explicitly **out of scope** and deferred. We
do not store the template anywhere in this phase.

## Decisions

- **Shared categories.** Tropes reuse the existing `Category` taxonomy. A single
  category can hold stories, prompts, one-liners, *and* tropes.
- **Single text field, no title.** A trope is just its fragment, so the entity
  is a flat row with a `Text` column — no title, and no child-message table
  (unlike `Prompt`/`PromptMessage`). The list row shows the fragment itself.
- **Structurally identical to One-liners.** A trope and a one-liner have the same
  shape (a categorized `Text` row). They are kept as **separate kinds** because
  they mean different things to the user: a one-liner is a finished evocative
  line that seeds a story directly; a trope is a reusable fragment substituted
  into a prompt template. Modeling them separately keeps each list, count, and
  future generation path clean. We do **not** try to unify them behind one
  entity or one tab.
- **Lightweight inline create/edit.** A compose field at the top of the
  category's list captures fragments quickly; editing happens in a small sheet —
  mirroring the One-liners UX, not a full-screen editor.
- **Create with text supplied directly.** The trope `POST` carries the text from
  the compose field, so a trope is never empty.
- **Newest-first ordering.** The list orders by `CreatedAt` descending, so a
  newly added fragment appears directly under the compose field.
- **No detail-fetch endpoint.** The edit sheet seeds from the summary already in
  the list (the fragment is short and is not truncated server-side), so there is
  no `GET /tropes/{id}`.

## Data model: chosen approach

**New flat `Trope` entity (chosen).** Mirror `OneLiner`: one column of content
under a category. This is the same decision reached for One-liners, for the same
reasons — reusing `Prompt` (title + ordered messages) or storing fragments as
rows on `Category` were both rejected there and remain wrong here.

## Server changes

### Entities

```csharp
public class Trope
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
```

`Category` gains `public List<Trope> Tropes { get; set; } = [];`.

### `TropeService`

New service mirroring `OneLinerService`:

- `CreateTropeAsync(int categoryId, string text)` — trims `text`; sets
  `CreatedAt`/`UpdatedAt`.
- `GetCategoryWithTropesAsync(int categoryId)` — category + its tropes.
- `GetTropeAsync(int id)` — trope including its `Category` (for `ToDto`).
- `UpdateTropeAsync(int id, string text, int categoryId)` — updates text and
  category, bumps `UpdatedAt`.
- `DeleteTropeAsync(int id)`.
- `CategoryExistsAsync(int categoryId)`.

### `FabulisDbContext`

- Add `DbSet<Trope> Tropes`.
- In `EnsureSchemaUpdatedAsync`, add a `CREATE TABLE IF NOT EXISTS` statement
  following the One-liners precedent so **existing vaults** gain the table
  (`EnsureCreated` only covers fresh databases and the in-memory test DB). The
  foreign key cascades: `Tropes.CategoryId → Categories(Id) ON DELETE CASCADE`.

```sql
CREATE TABLE IF NOT EXISTS Tropes (
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
public sealed record TropeSummaryDto(
    int Id, string Text, DateTime CreatedAt);

public sealed record TropeCategoryDto(             // category + its tropes
    int Id, string Name, DateTime CreatedAt,
    IReadOnlyList<TropeSummaryDto> Tropes);

public sealed record TropeDto(
    int Id, int CategoryId, string CategoryName, string Text,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateTropeRequest(int CategoryId, string Text);
public sealed record UpdateTropeRequest(string Text, int CategoryId);
```

Extend the existing `CategorySummaryDto` with two fields (for parity with the
story/prompt/one-liner count+latest pairs; `CategoryRow` uses the count, and the
latest text is available for future use):

```csharp
public sealed record CategorySummaryDto(
    int Id, string Name, DateTime CreatedAt,
    int StoryCount, string? LatestStoryTitle,
    int PromptCount, string? LatestPromptTitle,
    int OneLinerCount, string? LatestOneLinerText,
    int TropeCount, string? LatestTropeText);         // added
```

`POST /categories` constructs a `CategorySummaryDto` inline (currently with
trailing `0, null` for one-liners) — extend that literal with `0, null` for
tropes as well.

### Endpoints

Extend `LibraryEndpoints`:

- `GET /library` — `.Include(c => c.Tropes)`; populate `TropeCount` and
  `LatestTropeText` (text of the most recently created fragment) per category.
- `GET /categories/{id}/tropes` — returns `TropeCategoryDto`, tropes ordered by
  `CreatedAt` **descending**.

New `TropeEndpoints.cs` (`/tropes` group, `.RequireSession()`):

- `POST /tropes` — `CreateTropeRequest`; rejects blank `Text` (400) and a
  non-existent category (400). Returns the full `TropeDto` so the client has the
  new id.
- `PUT /tropes/{id}` — `UpdateTropeRequest`; rejects blank `Text`; updates text +
  `CategoryId`, bumps `UpdatedAt`. Returns `TropeDto`.
- `DELETE /tropes/{id}` — removes the trope.

Wire `MapTropeEndpoints()` into startup (`Program.cs`) alongside the other
endpoint groups, and register `TropeService` in DI next to `OneLinerService`.

## Client changes

### `LibraryKind`

Add `.tropes`, positioned right after `.oneLiners`:

```swift
case tropes             // label "Tropes", hasCategories = true
```

Tabs become **Prompts · One-liners · Tropes · Drafts · Stories**. *Note:* this
is a 5-segment picker; on a narrow iPhone the labels will be tight (the
One-liners spec already flagged 4 segments as snug). Start with the full
"Tropes" label; if it crowds in practice, shortening labels is a follow-up, not
part of this phase.

### DTOs (`APIDtos.swift`)

- `TropeSummary { id, text, createdAt }`
- `TropeCategoryDetail { id, name, createdAt, tropes: [TropeSummary] }`
- `TropeDetail { id, categoryId, categoryName, text, createdAt, updatedAt }`
- Extend `CategorySummary` with `tropeCount: Int` and `latestTropeText: String?`
  (and update its `==` in `LibraryView.swift`).
- Request bodies: `CreateTropeRequest { categoryId, text }`,
  `UpdateTropeRequest { text, categoryId }`.

### `FabulisAPIClient`

Add: `categoryTropes(categoryId:)`, `createTrope(categoryId:text:)`,
`updateTrope(id:text:categoryId:)`, `deleteTrope(id:)`.

### `LibraryView`

- Picker is already driven by `LibraryKind.allCases`, so the new tab appears
  automatically.
- Extend the kind `switch` sites to treat `.tropes` like the other
  category-backed kinds:
  - **toolbar** leading button: **New Category** (categories are shared).
  - **sidebar list**: render `categoriesList`.
  - **detail** builder: when a `.category` is selected under `.tropes`, show
    `TropeCategoryView`; otherwise the existing routing.
- Update the "Delete category?" message copy to "…stories, prompts, one-liners,
  **and tropes**."

### `CategoryRow`

Add the `.tropes` count string: `"N trope" / "N tropes"`.

### `TropeCategoryView` (new)

Cloned from `OneLinerCategoryView`:

- Loads `GET /categories/{id}/tropes`.
- **Compose field** pinned at the top: a multi-line `TextField` + **Add** button
  (disabled when blank). Add → `POST /tropes` for this category, clear the field,
  reload, and notify the sidebar via `onChanged` so its count updates.
- **List** of tropes (newest first): each row shows the fragment
  (`lineLimit(1...3)`). Tap a row → presents the edit sheet. Swipe-to-delete on
  rows.
- Reuses the shared category rename/delete controls. The category-delete
  confirmation copy includes tropes (see `LibraryView`).

### `TropeEditSheet` (new)

Cloned from `OneLinerEditSheet`; a small modal sheet seeded from the tapped
`TropeSummary` (text) plus the current category id:

- A `TextEditor` for the fragment and a category `Picker` (populated from the
  loaded `categories`) so a trope can be moved between categories.
- **Save** sends `PUT /tropes/{id}`; **Delete** (destructive) sends `DELETE`. On
  dismiss, the category view reloads and notifies the sidebar.

## Out of scope (deferred)

- Generating prompts or stories from a trope, including the `<<trope>>`
  substitution and its surrounding template.
- Bulk import/export: the CLI does not handle `Prompt`s or `OneLiner`s today, so
  tropes are likewise excluded. Record any deferral in `BACKLOG.md` during
  implementation if appropriate.

## Testing

- Server: `TropeServiceTests.cs` mirroring `OneLinerServiceTests` — create with
  text, reject/normalize blank text, update text + move category, delete,
  cascade delete when the category is removed; `/library` returns correct trope
  counts; schema bootstrap creates the table on a pre-existing vault.
- Client: builds for an iOS Simulator destination and Mac Catalyst; the Tropes
  tab lists categories, drills into a category, adds a fragment via the compose
  field, edits it (and moves its category) via the sheet, deletes one, and the
  sidebar count stays correct.
