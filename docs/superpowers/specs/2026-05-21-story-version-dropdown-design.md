# Story version browsing: collapse landing page into a version dropdown

## Problem

Browsing a saved story currently takes three taps through three screens:

1. **`CategoryView`** — list of story titles.
2. **`StoryView`** — a landing page showing the story title, metadata, and a
   list of versions to pick from.
3. **`StoryVersionView`** — the actual messages of the chosen version.

The middle screen exists only to pick a version. For stories with a single
version (the common case) it is pure friction.

## Goal

Collapse steps 2 and 3 into one screen. Tapping a story title goes straight to
the **latest** version's content, with a compact version dropdown in the
toolbar for switching between versions.

## Design

### Navigation

Unchanged at the call site: `CategoryView` still navigates to
`StoryView(storyId:fallbackTitle:)`. What changes is `StoryView`'s behavior —
it becomes the combined reading screen rather than a version picker.

### `StoryView` (rebuilt)

State:
- `detail: StoryDetail?` — title + version list.
- `selectedVersion: Int?` — the version currently displayed.
- `versionDetail: StoryVersionDetail?` — messages for the selected version.
- separate loading / error flags for the story-level fetch and the
  version-level fetch so the dropdown can stay visible while the body reloads.

Behavior:
- **Nav title:** the story title (falls back to `fallbackTitle` until loaded).
- **Toolbar (top-right):** a `Menu` labeled `Version N ▾` for the current
  selection. Tapping lists every version compactly — `Version 3`, `Version 2`,
  `Version 1`, latest first. Selecting one loads and shows that version.
- **Body:** the message list
  (`ForEach(versionDetail.messages) { StoryMessageView(message:) }`),
  the same loop currently in `StoryVersionView`.
- While switching versions the body shows a `ProgressView`; the dropdown
  stays in place.
- **Empty case** (story with no versions): a
  `ContentUnavailableView("No versions yet", …)` and no dropdown.
- `.refreshable` re-fetches the story and the current version.

### Data flow

1. `loadStory()` → `FabulisAPIClient.shared.story(id:)` → set `detail`; pick
   `selectedVersion = detail.versions.map(\.versionNumber).max()`.
2. `loadVersion(_:)` → `FabulisAPIClient.shared.storyVersion(storyId:version:)`
   → set `versionDetail`.
3. Dropdown selection sets `selectedVersion` and calls `loadVersion`.

### Removed / unchanged

- **`StoryVersionView.swift`** is deleted; its per-version fetch and message
  rendering fold into `StoryView`.
- **`StoryMessageView`** is untouched.
- **Server, API endpoints, and DTOs** are unchanged — both `story(id:)` and
  `storyVersion(storyId:version:)` already exist.

## Trade-off accepted

The old landing page surfaced the version count and each version's model name
and date. With compact dropdown entries, that detail is no longer visible —
only `Version N` and the messages show. This is an intentional simplification.
