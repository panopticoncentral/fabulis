# Auto-generate story title

## Goal

Add a "Generate" button to the **Save Draft** dialog that auto-generates a story
title from the body of the story, using the same model the storyteller uses to
write stories. The prompt that drives titling ("titling prompt") is editable in
the same place as the storyteller prompt and ships with a sensible default.

## Decisions

- **Title source:** all assistant (story) responses in the draft, joined in sort
  order. Not the user prompts, not just the latest response.
- **Model:** the storyteller's configured `ModelName` (the same model used to
  write stories).
- **Sampling:** fixed sensible defaults for titling — `temperature: 0.3`,
  `maxTokens: 32`. No per-titling sampling settings are stored or exposed.
- **Persistence of the generated title:** none. The endpoint returns the title;
  the client populates the editable text field with it. The title is only saved
  when the user saves the draft as it already does.
- **Button visibility:** only when creating a **new** story (the title field is
  already hidden when saving into an existing story).

## Server changes

### Entity — `Storyteller.cs`

Add a field alongside `Prompt`:

```csharp
public required string TitlingPrompt { get; set; }
```

Define the default titling prompt as a shared constant (e.g. on `Storyteller`
or `FabulisDbContext`):

> You write titles for stories. Given the full text of a story, respond with a
> single short, evocative title — 2 to 6 words. Output only the title itself: no
> quotation marks, no trailing punctuation, no commentary.

### Schema — `FabulisDbContext.EnsureSchemaUpdatedAsync`

- Add `TitlingPrompt TEXT NOT NULL DEFAULT '<default prompt>'` to the
  `CREATE TABLE IF NOT EXISTS Storytellers` body (for fresh vaults).
- For existing vaults the `CREATE TABLE IF NOT EXISTS` is a no-op, so add an
  idempotent column add: check `PRAGMA table_info(Storytellers)`; if
  `TitlingPrompt` is absent, run
  `ALTER TABLE Storytellers ADD COLUMN TitlingPrompt TEXT NOT NULL DEFAULT '<default prompt>'`.
- `SeedDefaultStorytellerIfMissingAsync` sets `TitlingPrompt` to the default
  constant when seeding the initial storyteller.

### DTOs — `Api/Dtos.cs`

- `StorytellerDto`: add `string TitlingPrompt`.
- `StorytellerUpdateRequest`: add `string TitlingPrompt`.

### Storyteller endpoints — `Api/StorytellerEndpoints.cs`

- `GET /api/v1/storyteller`: include `TitlingPrompt` in the response.
- `PUT /api/v1/storyteller`: persist `body.TitlingPrompt`.

### New endpoint — `POST /api/v1/drafts/{id}/generate-title`

In `Api/DraftEndpoints.cs` (session-protected like the others):

1. Load the draft with its storyteller and messages.
2. Concatenate all assistant/story-response messages (in `SortOrder`) into one
   body string. If there are none, return a `400` (or equivalent error result).
3. Call `OpenRouterService.ChatAsync(model: storyteller.ModelName,
   systemPrompt: storyteller.TitlingPrompt, userMessage: body,
   temperature: 0.3, maxTokens: 32)`.
4. Trim whitespace and surrounding quotation marks from the returned text.
5. Return `GenerateTitleResponse { string Title }`. Persist nothing.

Add a `GenerateTitleResponse` DTO.

## Client changes

### DTOs — `Models/APIDtos.swift`

- `StorytellerDto`: add `titlingPrompt: String`.
- `StorytellerUpdateRequest`: add `titlingPrompt: String`.
- Add `GenerateTitleResponse { let title: String }`.

### API client — `Services/FabulisAPIClient.swift`

```swift
func generateTitle(draftId: Int) async throws -> String
```

POST `/drafts/{id}/generate-title`, decode `GenerateTitleResponse`, return its
`title`.

### Storyteller editor — `Views/Settings/StorytellerEditorView.swift`

- Add a "Titling prompt" `TextEditor` section, modeled on the existing system
  prompt section.
- Load it from `getStoryteller()` into state, and include it in the
  `StorytellerUpdateRequest` on save.

### Save Draft dialog — `Views/Draft/SaveDraftSheet.swift`

In the existing "Story title" section (rendered only when `selectedStoryId == nil`):

- Add a "Generate" button beneath/next to the title `TextField`.
- Tapping it sets an in-progress state, calls
  `FabulisAPIClient.shared.generateTitle(draftId:)`, and fills `newStoryTitle`
  with the trimmed result (still editable).
- Disable the button while generating. Surface errors via the existing
  `errorMessage` mechanism.

## Out of scope

- No per-titling sampling controls.
- No automatic titling on save; the button is explicit.
- No streaming for the titling call (one-shot completion via `ChatAsync`).
