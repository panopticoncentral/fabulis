# Story Summaries — Design

**Date:** 2026-06-17
**Status:** Approved (pending implementation plan)

## Goal

Give every **story** (not draft, not prompt) a one-paragraph summary,
generated automatically in the background by a configurable model. The
summary is per-story and reflects all of the story's versions over time.
It is editable, can be force-regenerated, and is hidden behind a toolbar
action rather than shown by default.

## Key decisions

- **Model:** a dedicated, configurable `SummaryModel` setting, falling
  back to the existing global `AssistantModel` when unset.
- **Prompt:** a single editable `SummaryPrompt` setting with a built-in
  default. The service composes the user message; the prompt is the
  system prompt.
- **Trigger:** both event-driven (enqueue on save to library) and a
  periodic sweep backstop while the vault is unlocked.
- **Versions / amalgamation:** automatic passes fold incrementally —
  prior summary + the text of versions newer than the one last
  summarized. The first-ever summary is just the single version's text.
- **Manual edits:** not locked. A later version folds the edited text in
  as the base (the edit becomes the "prior summary" input).
- **Manual Regenerate:** a full rebuild from scratch — all versions'
  text, prior summary discarded.
- **Text fed to the model:** response-role messages only (ignoring the
  user-side prompts in each version), same convention as titling.
- **UI:** a toolbar button in the story view opens a summary sheet;
  nothing is shown by default.

## Data model

Summaries are 1:1 with `Story` and small, so they live as columns on the
existing `Stories` table rather than a new entity — this keeps the story
read path join-free. Columns are added through the hand-written SQL
schema in `FabulisDbContext` (the project does not use EF migrations).

New columns on `Stories`:

| Column | Type | Notes |
| --- | --- | --- |
| `SummaryText` | `TEXT NULL` | The paragraph; `null` until first generated. |
| `SummaryStatus` | `INTEGER NOT NULL DEFAULT 0` | `None=0`, `Ready=1`, `Failed=2`. |
| `SummarizedThroughVersion` | `INTEGER NULL` | Highest `VersionNumber` reflected in `SummaryText`. |
| `SummaryError` | `TEXT NULL` | Last failure message, when `SummaryStatus = Failed`. |
| `SummaryUpdatedAt` | `TEXT NULL` | UTC timestamp of last successful write. |

Corresponding properties are added to the `Story` entity
(`src/Fabulis.Server/Data/Story.cs`) and mapped in `FabulisDbContext`.

**Needs-work predicate:** a story needs (re)summarization when
`SummarizedThroughVersion IS NULL OR SummarizedThroughVersion < max(VersionNumber)`.

**"Generating" is not persisted.** It is tracked in-memory by the
background service, so a server restart can never leave a story stuck in
a generating state. The `GET` endpoint reports `"generating"` when the
story id is in the service's in-flight set; otherwise it reports the
persisted `SummaryStatus`.

## Settings

Two new `AppSetting` key/value rows:

- `SummaryModel` — model id for summarization. When empty, the service
  falls back to the `AssistantModel` value.
- `SummaryPrompt` — the editable system prompt. Default lives as a
  constant (e.g. `StorySummary.DefaultPrompt`), along the lines of:

  > "You write concise summaries of stories. Given the full text of a
  > story — and, when provided, an existing summary to update —
  > respond with a single paragraph that captures the main characters,
  > setting, and arc. Output only the summary paragraph: no preamble, no
  > headings, no commentary."

Both are surfaced through `SettingsDto` and `SettingsUpdateRequest`
(`src/Fabulis.Server/Api/Dtos.cs`), read/written in
`SettingsEndpoints.cs`, and edited in the client Settings screen — the
model via the existing `ModelPickerView`, the prompt via a `TextEditor`
(same shape as the storyteller prompt editor). `SummaryPrompt` upsert
should not blank an existing value on an empty submit (match the
existing "leave alone when null/blank" convention for prompts).

## Background summarizer — `SummaryService`

A singleton `BackgroundService` (registered as a hosted service),
mirroring `GenerationManager`'s scope and cancellation discipline:

- **Sweep:** a timer (~30s). On each tick, while `vault.IsUnlocked`,
  query stories that need work and process them **one at a time** — this
  is low-priority background work and should be gentle on the API.
- **On-save enqueue:** `DraftService.SaveToLibraryAsync` signals a
  `Channel<int>` (story id) after a successful save so a new story or
  version is picked up promptly rather than waiting for the next tick.
- **Per job:**
  1. Open a DI scope (`IServiceScopeFactory`) — the `DbContext` is
     configured with the vault password at scope creation, same as
     `GenerationManager`.
  2. Load the story + versions + (needed) messages.
  3. Build the input (see below).
  4. Call `OpenRouterService.ChatAsync(model, SummaryPrompt, userMessage,
     temperature: <low>, disableReasoning: true)` where `model` is
     `SummaryModel` or the `AssistantModel` fallback.
  5. On success: write `SummaryText`, set `SummarizedThroughVersion =
     maxVersion`, `SummaryStatus = Ready`, `SummaryUpdatedAt = now`,
     clear `SummaryError`.
  6. On failure: set `SummaryStatus = Failed`, store `SummaryError`.
- Subscribes to `vault.Locked` to cancel any in-flight work.
- Maintains an in-memory `HashSet<int>` of stories currently generating;
  this drives the `"generating"` status reported by the API and prevents
  double-processing within a run.

The service exposes a small surface for the API layer, e.g.
`bool IsGenerating(int storyId)` and `void Enqueue(int storyId)`
(the latter also used by the regenerate endpoint).

## Input construction — `StorySummary` helper

A pure, unit-tested static class parallel to `TitleGeneration`
(`src/Fabulis.Server/Data/`):

- `BuildVersionBody(version)` — joins the version's response-role
  `StoryMessage`s by `SortOrder` (same as `TitleGeneration.BuildStoryBody`).
- **Automatic fold:** when a prior `SummaryText` exists, the user message
  combines the existing summary with the bodies of versions whose
  `VersionNumber > SummarizedThroughVersion`. The first-ever summary (no
  prior text) is just that single version's body.
- **Full rebuild (manual Regenerate):** the user message is all versions'
  bodies, prior summary discarded.
- A helper composes the user message with clear sections, e.g.:

  ```
  EXISTING SUMMARY:
  <prior summary>

  NEW STORY CONTENT:
  <newer version bodies>
  ```

  and, for first-time / full-rebuild, just the story content with no
  "existing summary" section.
- Output cleanup (trim whitespace; collapse to a single paragraph if the
  model emits extra blank lines) lives here too and is unit-tested.

## API — `/api/v1/stories/{id}/summary`

Added to `StoryEndpoints.cs`, under the existing `/stories` session group.

- `GET /stories/{id}/summary` → `SummaryDto`:

  ```
  SummaryDto(
    string? Text,
    string Status,                 // "none" | "generating" | "ready" | "failed"
    int? SummarizedThroughVersion,
    int LatestVersion,
    bool IsStale,                  // server-computed needs-work predicate
    DateTime? UpdatedAt,
    string? Error)
  ```

  `Status` is `"generating"` when the id is in the service's in-flight
  set, otherwise the persisted status mapped to a string.

- `PUT /stories/{id}/summary` with `{ text }` → saves a manual edit:
  sets `SummaryText`, `SummarizedThroughVersion = LatestVersion`,
  `SummaryStatus = Ready`, `SummaryUpdatedAt = now`. Returns the updated
  `SummaryDto`.

- `POST /stories/{id}/summary/regenerate` → enqueues a full from-scratch
  rebuild (service runs the full-rebuild path). Returns `Accepted` (or
  the current `SummaryDto` with `"generating"`).

DTOs go in `src/Fabulis.Server/Api/Dtos.cs`; client mirrors in
`client/Fabulis/Models/APIDtos.swift`.

## Client UI

- **`StoryView`** (`client/Fabulis/Views/Story/StoryView.swift`): add a
  toolbar button (e.g. `text.quote`) alongside the existing version menu.
  Tapping presents a sheet. Nothing about the summary is shown until the
  sheet is opened.
- **`StorySummarySheet`** (new view): shows
  - the summary text when `ready`;
  - an empty/"No summary yet" state when `none`;
  - a "Generating…" state with a spinner when `generating`;
  - the error with a Retry/Regenerate affordance when `failed`.

  Controls: **Edit** (reveals an inline `TextEditor` + Save, calling
  `PUT`), and **Regenerate** (calls `POST …/regenerate`). While status is
  `generating`, the sheet polls `GET` every few seconds until it settles
  to `ready`/`failed`.
- **`FabulisAPIClient`** (`client/Fabulis/Services/`): add
  `summary(id:)`, `updateSummary(id:text:)`, `regenerateSummary(id:)`.
- **Settings** (`client/Fabulis/Views/Settings/SettingsView.swift`): add
  a Summary model picker (reuse `ModelPickerView`) and a Summary prompt
  editor (`TextEditor`).

## Testing

- **Unit (server):** `StorySummary` helper — `BuildVersionBody`
  (response-only, sort order), incremental-fold input vs. first-time vs.
  full-rebuild composition, output cleanup.
- **Endpoint:** `GET`/`PUT`/`regenerate` happy paths, `GET` staleness
  computation, `PUT` setting `SummarizedThroughVersion` to latest.
- **Service behavior:** needs-work query selects stale/unsummarized
  stories; manual edit is folded as base on the next version; restart
  does not strand a "generating" story (no persisted generating state).

## Out of scope

- Summaries for drafts or prompts.
- Showing summaries in library list views (summary is lazy-loaded when
  the sheet opens).
- Surfacing the summary model/prompt per-storyteller (it is a single
  global app setting).
- Backfilled migration tooling beyond the natural sweep, which will pick
  up all pre-existing stories the first time it runs while unlocked.
