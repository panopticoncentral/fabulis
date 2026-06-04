# Prompts category — design

**Date:** 2026-06-03
**Status:** Approved, pending implementation plan

## Summary

Add a third category of library content, **Prompts**, alongside Drafts and
Stories. A *prompt* is conceptually a story that contains only *your* side of
the conversation — either a single initial story prompt or a whole series of
prompts. Prompts are organized under the same category taxonomy as Stories, so
the Prompts tab looks and behaves like the Stories tab.

This phase covers **defining prompts and seeing them in the UI** only. How a
prompt later becomes a draft is explicitly **out of scope** and deferred.

## Decisions

- **Shared categories.** Prompts reuse the existing `Category` taxonomy. A
  single category can hold both stories and prompts; the Stories tab shows its
  stories and the Prompts tab shows its prompts.
- **Dedicated prompt editor.** A prompt is edited in place — title, category,
  and an ordered list of your message blocks (add / edit / reorder / delete) —
  rather than through the chat-style Draft flow.
- **New `Prompt` + `PromptMessage` entities** (data-model Approach A below).
- **Prompts are flat — no versions.** Unlike stories (which gain versions from
  regeneration), a prompt is just an ordered list of your messages.
- **No role column.** Every prompt message is implicitly *your* side, so
  `PromptMessage` needs no `Role`.
- **"New Prompt" lives inside a category**, mirroring how stories live under
  categories — not as a top-level button.

## Data model: chosen approach

**Approach A — new `Prompt` + `PromptMessage` entities (chosen).**
Mirror `Story`/`StoryMessage` but flat. Clean separation keeps both subsystems
easy to reason about.

Rejected alternatives:

- **B — reuse `Story` with an `IsPrompt` flag.** Versions and model-names are
  meaningless for prompts, and the shared story endpoints + UI would branch on
  the flag everywhere. Fragile.
- **C — reuse `Draft`.** Wrong shape: drafts are a flat, uncategorized,
  storyteller-bound chat flow.

## Server changes

### Entities

```csharp
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

public class PromptMessage
{
    public int Id { get; set; }
    public int PromptId { get; set; }
    public required string Content { get; set; }
    public int SortOrder { get; set; }

    public Prompt Prompt { get; set; } = null!;
}
```

`Category` gains `public List<Prompt> Prompts { get; set; } = [];`.

### `FabulisDbContext`

- Add `DbSet<Prompt> Prompts` and `DbSet<PromptMessage> PromptMessages`.
- In `EnsureSchemaUpdatedAsync`, add `CREATE TABLE IF NOT EXISTS` statements for
  both tables, following the existing Drafts/DraftMessages precedent so that
  **existing vaults** gain the tables (`EnsureCreatedAsync` only covers fresh
  databases). Foreign keys cascade: `Prompts.CategoryId → Categories(Id)
  ON DELETE CASCADE` and `PromptMessages.PromptId → Prompts(Id) ON DELETE
  CASCADE`.

```sql
CREATE TABLE IF NOT EXISTS Prompts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CategoryId INTEGER NOT NULL,
    Title TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS PromptMessages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PromptId INTEGER NOT NULL,
    Content TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (PromptId) REFERENCES Prompts(Id) ON DELETE CASCADE
);
```

### DTOs

New records in `Dtos.cs`:

```csharp
public sealed record PromptSummaryDto(
    int Id, string Title, DateTime CreatedAt, int MessageCount);

public sealed record PromptCategoryDto(            // category + its prompts
    int Id, string Name, DateTime CreatedAt,
    IReadOnlyList<PromptSummaryDto> Prompts);

public sealed record PromptDto(
    int Id, int CategoryId, string CategoryName, string Title,
    DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<PromptMessageDto> Messages);

public sealed record PromptMessageDto(int Id, string Content, int SortOrder);

public sealed record CreatePromptRequest(int CategoryId, string? Title);
public sealed record UpdatePromptRequest(
    string Title, int CategoryId, IReadOnlyList<string> Messages);
```

Extend the existing `CategorySummaryDto` with two fields:

```csharp
public sealed record CategorySummaryDto(
    int Id, string Name, DateTime CreatedAt,
    int StoryCount, string? LatestStoryTitle,
    int PromptCount, string? LatestPromptTitle);   // added
```

### Endpoints

Extend `LibraryEndpoints`:

- `GET /library` — populate the new `PromptCount` / `LatestPromptTitle` per
  category (include `c.Prompts`). One library call feeds both tabs.
- `GET /categories/{id}/prompts` — returns `PromptCategoryDto` (category name +
  its prompts, ordered by title). Mirrors `GET /categories/{id}`.

New `PromptEndpoints.cs` (`/prompts` group, `.RequireSession()`):

- `GET /prompts/{id}` — `PromptDto` with messages ordered by `SortOrder`.
- `POST /prompts` — `CreatePromptRequest`; creates an empty prompt in the given
  category (default title "Untitled Prompt" when null). Returns the full
  `PromptDto` (empty `Messages`) so the editor can open it directly.
- `PUT /prompts/{id}` — `UpdatePromptRequest`; **replace-on-save**: updates
  title + `CategoryId`, deletes existing `PromptMessage`s and re-inserts the
  provided list with `SortOrder` = array index. Bumps `UpdatedAt`.
- `DELETE /prompts/{id}` — removes the prompt (messages cascade).

Wire `MapPromptEndpoints()` into startup alongside the other endpoint groups.

## Client changes

### `LibraryKind`

Add `.prompts`:

```swift
case prompts            // label "Prompts", hasCategories = true
```

### DTOs (`APIDtos.swift`)

- `PromptSummary { id, title, createdAt, messageCount }`
- `PromptCategoryDetail { id, name, createdAt, prompts: [PromptSummary] }`
- `PromptDetail { id, categoryId, categoryName, title, createdAt, updatedAt, messages: [PromptMessage] }`
- `PromptMessage { id, content, sortOrder }` (no role)
- Extend `CategorySummary` with `promptCount: Int` and `latestPromptTitle: String?`.
- Request bodies: `CreatePromptRequest { categoryId, title }`,
  `UpdatePromptRequest { title, categoryId, messages: [String] }`.

### `FabulisAPIClient`

Add: `prompts(categoryId:)`, `prompt(id:)`, `createPrompt(categoryId:title:)`,
`updatePrompt(id:title:categoryId:messages:)`, `deletePrompt(id:)`.

### `LibraryView`

- Picker becomes 3-way (Drafts / Stories / Prompts) — already driven by
  `LibraryKind.allCases`.
- The Prompts tab shows the shared category list (same `categoriesList` /
  `CategoryRow`), reusing the loaded `categories`.
- Toolbar leading button for `.prompts`: **New Category** (categories are
  shared — same control as Stories). Prompt creation happens inside a category.
- `detail` builder: when a `.category` is selected under the `.prompts` kind,
  show `PromptCategoryView`; otherwise `CategoryView`.

### `CategoryRow`

Show the count for the active kind: `"N stories"` for Stories, `"N prompts"`
for Prompts. Pass the kind (or the count + noun) into the row.

### `PromptCategoryView` (new, mirrors `CategoryView`)

- Loads `GET /categories/{id}/prompts`.
- Lists prompts; empty state: "No prompts — tap New Prompt to add one."
- Toolbar **New Prompt** button: `POST /prompts` for this category, then
  navigates into `PromptEditorView` for the new prompt.
- Reuses the shared category rename/delete controls. The category-delete
  confirmation text changes to "This deletes the category and all its stories
  **and prompts**. This cannot be undone." (Update the same copy in
  `CategoryView` and `LibraryView`.)

### `PromptEditorView` (new)

- Fields: title (`TextField`), category picker (populated from `categories`),
  and an editable list of message blocks supporting add / edit / reorder /
  delete.
- **Save** sends `PUT /prompts/{id}` with the full title + categoryId + message
  list (replace-on-save). On save, refresh the library so counts update.

## Out of scope (deferred)

- Converting a prompt into a draft.
- Any generation/LLM interaction from a prompt.

Record the deferral in `BACKLOG.md` if appropriate during implementation.

## Testing

- Server: prompt CRUD round-trip (create → update with messages → fetch →
  delete); cascade delete when a category is removed; `/library` returns
  correct prompt counts; schema bootstrap creates tables on a pre-existing
  vault.
- Client: builds for an iOS Simulator destination and Mac Catalyst; the Prompts
  tab lists categories, drills into a category, creates/edits/saves a prompt,
  and the new prompt appears with the right count.
