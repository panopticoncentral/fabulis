# Inline composer editing for draft bubbles

## Problem

Editing a prompt or response bubble in a draft currently opens
`EditMessageSheet` — a full-screen modal (480×360 on Mac) with a `TextEditor`
and a button bar (Cancel / Save / Save & Resubmit). The modal hides the
conversation and feels heavy for what is usually a small text tweak.

We want editing to happen **in place** in the bottom composer: tapping Edit
drops the bubble's text into the composer field, with a context banner and
action buttons, keeping the conversation visible the whole time.

## Goals

- Edit a bubble's text inline in the existing bottom composer instead of a modal.
- Preserve the three distinct actions:
  - **Cancel** — discard edits, change nothing.
  - **Save** — change the bubble's content in place; leave later bubbles alone.
  - **Resubmit** — change the prompt, delete every message after it, and
    regenerate (prompts only).
- Both prompt and response bubbles use the inline flow. Responses get
  Cancel / Save only (regenerating *from* a response is meaningless, so no
  Resubmit).
- Don't lose a half-typed new prompt: stash it while editing and restore it
  on Cancel or Save.

## Non-goals

- No server changes. All three endpoints already exist.
- No change to the "Regenerate" context-menu quick action (re-runs a prompt
  with its existing content unchanged).
- No change to narration, the Save-draft sheet, or streaming/reattach logic.

## Background: what already exists

Server endpoints (no changes needed):

| Action | Endpoint | DraftService method | Truncates after? | Regenerates? |
|---|---|---|---|---|
| Edit in place | `PUT /drafts/{id}/messages/{mid}` | `UpdateMessageContentAsync` | No | No |
| Edit + resubmit | `POST /drafts/{id}/messages/{mid}/edit-and-resubmit` | `UpdateMessageAndDeleteSubsequentAsync` | Yes | Yes |
| Delete + after | `DELETE /drafts/{id}/messages/{mid}` | `DeleteMessageAndSubsequentAsync` | Yes (incl. self) | No |

Client API methods (no changes needed):

- `editDraftMessage(draftId:messageId:content:)` → `PUT` (in-place save).
- `editAndResubmit(draftId:messageId:content:)` → SSE stream (truncate + regenerate).

So this is purely a **client UX change** in the draft view.

## Design

### Composer states

**Normal (unchanged):** text field + paperplane send button (stop button while
streaming).

```
┌─────────────────────────────────────────────┬─────┐
│ Prompt…                                      │  ➤  │
└─────────────────────────────────────────────┴─────┘
```

**Editing a prompt:** banner above the field; trailing buttons become
Cancel / Save / Resubmit.

```
  ✎ Editing prompt — Resubmit will delete N messages after it
┌───────────────────────────────────────┬──────────────────────┐
│ <prompt text, editable>                │ Cancel  Save  Resubmit│
└───────────────────────────────────────┴──────────────────────┘
```

**Editing a response:** banner + Cancel / Save only.

```
  ✎ Editing response
┌───────────────────────────────────────────────┬──────────────┐
│ <response text, editable>                      │ Cancel  Save │
└───────────────────────────────────────────────┴──────────────┘
```

The banner's "delete N messages after it" count reflects the number of
bubbles after the edited prompt; if there are none, the banner reads simply
"Editing prompt".

### Action semantics

| Action | Trigger | Endpoint / method | Effect |
|---|---|---|---|
| Cancel | Esc key or button | none | Discard edits; restore stashed new-prompt text; exit edit mode |
| Save | Return key or button | `editDraftMessage` (`PUT`) | In-place content change, then reload draft; later bubbles untouched; restore stashed prompt; exit edit mode |
| Resubmit | button click only | `editAndResubmit` (SSE) | Truncate everything after the prompt + regenerate; restore stashed prompt; exit edit mode |

Keyboard:

- **Return** = Save (the non-destructive commit).
- **Shift+Return** = newline.
- **Esc** = Cancel.
- **Resubmit is click-only** — it is destructive, so it must not be a single
  keystroke away.

Save and Resubmit are disabled when the trimmed content is empty.

### Conversation-area affordances during editing

- The bubble being edited gets a **highlight outline** (stroke overlay, same
  mechanism as the existing `isCurrentlyPlaying` border).
- When editing a **prompt**, every bubble *after* it is **dimmed**
  (`.opacity(0.4)`) to preview what Resubmit will remove. This dimming does
  **not** apply when editing a response (Save never truncates).

### Stashing in-progress prompt text

The composer's text field binds to the existing `prompt` state, reused for
edit content. On entering edit mode:

1. Stash the current `prompt` value into `stashedPrompt`.
2. Set `prompt = message.content`, focus the field.

On Cancel / Save / Resubmit:

1. Restore `prompt = stashedPrompt ?? ""`.
2. Clear `stashedPrompt` and `editingMessage`.

### Entering / guarding edit mode

- The **Edit** context-menu item is shown only for `msg.id >= 0` (real,
  persisted messages — optimistic placeholders with negative ids can't be
  `PUT`).
- The **Edit** item is disabled while `isStreaming` (the composer is already
  disabled during streaming; entering edit mode mid-stream would be
  inconsistent).

## Component changes

### `client/Fabulis/Views/Draft/DraftView.swift`

- Remove the `.fullScreenCover(item: $editingMessage)` and its
  `EditMessageSheet` usage.
- Keep `editingMessage: DraftMessageDto?` as the "what is being edited" flag,
  but now it drives the composer rather than a sheet. Add
  `@State private var stashedPrompt: String?`.
- Rework `inputBar` from an `HStack` into a `VStack`: an optional banner row
  plus the field/buttons row. The trailing buttons switch on
  `editingMessage?.role` (normal send button when not editing; Cancel/Save for
  responses; Cancel/Save/Resubmit for prompts).
- Add functions:
  - `beginEdit(_ msg: DraftMessageDto)` — stash prompt, load content, focus.
  - `cancelEdit()` — restore stash, clear edit state.
  - `saveEdit()` — `await editDraftMessage(...)`, `reloadDraft`, restore stash,
    clear edit state; surface errors via `errorMessage`.
  - Resubmit reuses the existing `editAndResubmit(messageId:content:)`, then
    restores the stash and clears edit state.
- Update the `Return` key handler so that, in edit mode, Return calls
  `saveEdit()` instead of `submit()`; add an `Esc` handler that calls
  `cancelEdit()` in edit mode.
- In the `ForEach`, compute per-bubble `isEditing` and `isDimmed` and pass them
  to `DraftMessageView`. `isDimmed` is true when a prompt is being edited and
  the bubble sorts after the edited prompt (compare array index of the edited
  message).
- Guard the **Edit** context-menu item with `msg.id >= 0` and `.disabled(isStreaming)`.

### `client/Fabulis/Views/Draft/DraftMessageView.swift`

- Add two optional flags with defaults: `isEditing: Bool = false`
  (accent-colored stroke overlay, reusing the existing overlay mechanism) and
  `isDimmed: Bool = false` (`.opacity(0.4)` on the bubble).
- Thread them through both initializers and the `EmptyView` convenience
  extension.

### `client/Fabulis/Views/Draft/EditMessageSheet.swift`

- **Delete.** No other file references it once `DraftView` stops presenting it.

## Edge cases

- **Edit while streaming:** prevented — Edit item disabled when `isStreaming`.
- **Optimistic (negative-id) bubbles:** Edit item hidden for `msg.id < 0`.
- **Empty content:** Save and Resubmit disabled when trimmed content is empty.
- **Half-typed new prompt:** preserved via `stashedPrompt`, restored on exit.
- **No messages after an edited prompt:** banner omits the delete-count
  clause; dimming affects nothing.
- **Save error:** stay in edit mode, show `errorMessage`, keep the user's text.

## Testing

Manual verification on Mac Catalyst (primary report platform) and an iOS
simulator:

1. Edit a prompt → Save: bubble text changes, later bubbles unchanged.
2. Edit a prompt → Resubmit: later bubbles removed, new response streams.
3. Edit a prompt → Cancel: nothing changes; previously half-typed prompt
   restored.
4. Edit a response → Save: response text changes; no Resubmit button shown.
5. Return commits Save; Shift+Return inserts a newline; Esc cancels.
6. While editing a prompt, bubbles after it are dimmed; the edited bubble is
   highlighted; neither persists after exiting edit mode.
7. Edit item is absent on optimistic bubbles and disabled during streaming.
