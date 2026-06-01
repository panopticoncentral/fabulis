# Library kind-switcher restructure

**Date:** 2026-05-30
**Status:** Approved design
**Scope:** Client (SwiftUI) only — no server or data-model changes

## Motivation

The library is intended to grow new *kinds* of content beyond drafts and
stories (the first planned one being **outlines** — a standalone item that
shares the same category taxonomy as stories). Adding each new kind as another
section in the sidebar tree would make the sidebar grow unboundedly long.

Instead, kinds become **switchable** via a segmented control at the top of the
sidebar. Each kind shows its own level-1 list; kinds that have categories
(stories, future outlines) reuse the *same* category taxonomy. This keeps the
sidebar a fixed height regardless of how many kinds exist.

This spec covers **only** restructuring the existing two kinds (drafts,
stories) into the switcher and leaving a clean seam for future kinds. It does
**not** add outlines, and does **not** touch the server or data model.

## Decisions captured during brainstorming

- **Outlines (and future kinds) are independent sibling items** that share the
  category taxonomy with stories — not a 1:1 facet of a story. (Informs the
  long-term model; not built here.)
- **Layout = "kind switcher inside the sidebar" (option B)**, not a top-level
  tab bar. Rationale: on iPad and Mac Catalyst, platform tab styling wants to
  live in the sidebar and would compete with the category list. A segmented
  control above the category list behaves identically across iPhone, iPad, and
  Mac Catalyst. The user confirmed kind-switching is infrequent, so the loss of
  a thumb-reachable bottom tab bar on iPhone is acceptable.
- **On two-pane (Mac/iPad), the Drafts sidebar = the drafts list itself**
  (selecting a draft opens it in the detail pane), symmetric with the iPhone
  collapsed flow. The sidebar always holds "the level-1 list".

## Design

### 1. The kind model

A small enum is the single extensibility point:

```swift
enum LibraryKind: String, CaseIterable, Identifiable {
    case drafts, stories          // future: case outlines
    var id: String { rawValue }
    var label: String { … }       // "Drafts", "Stories"
    var hasCategories: Bool { self != .drafts }
}
```

Adding a future kind = one `case` here plus its detail view. The segmented
control is driven by `CaseIterable`, so it grows automatically.

### 2. State & structure

`LibraryView` remains a single `NavigationSplitView`. New state:

- `@State selectedKind: LibraryKind = .stories` — default to the library on
  launch.
- The existing `selection: LibrarySelection?` is kept as the unified detail
  selection.

The sidebar gets a `Picker(.segmented)` bound to `selectedKind`, pinned at the
top. The list below switches on the selected kind.

Switching kinds **clears the detail selection** back to the empty state. This
avoids a stale cross-kind selection; per-kind selection memory is explicitly
not implemented (YAGNI).

### 3. Sidebar & detail per kind

| Kind | Sidebar (level 1) | Detail |
|------|-------------------|--------|
| **Drafts** | the drafts list (rows tagged `.draft(id)`), swipe-to-delete | `DraftView` for the selected draft |
| **Stories** | the category list (as today), swipe-to-delete | `CategoryView` → `StoryView` (unchanged) |

Selection stays one unified enum:

```swift
enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
}
```

The drafts loading + delete logic currently in `DraftsView` moves into
`LibraryView`'s sidebar. Draft and category rows are extracted as small
`DraftRow` / `CategoryRow` subviews for clarity.

The Stories side is essentially unchanged from today: the sidebar already lists
categories and the detail already hosts `CategoryView` → `StoryView`.

### 4. Toolbar & actions

The primary "+" action becomes **contextual** to the selected kind:

- **Drafts selected:** "New Draft" — creates a draft, switches `selectedKind`
  to `.drafts` (already there), and selects the new draft so it opens in the
  detail pane.
- **Stories selected:** "New Category" (`folder.badge.plus`).
- **Settings gear:** always present (trailing).

### 5. iPhone behavior

The `NavigationSplitView` collapses to a navigation stack. The sidebar (the
segmented control + the level-1 list) becomes the root screen; everything below
is push navigation:

1. Root: segmented control + level-1 list (drafts list, or category list).
2. Stories: tap a category → that category's stories. Drafts: tap a draft →
   the draft editor.
3. Tap a story → story detail.

No bottom tab bar.

### 6. Empty states

- Drafts, no drafts: existing "No drafts" `ContentUnavailableView`.
- Stories, no categories: existing "No categories" `ContentUnavailableView`.
- No detail selection: existing "Select a draft or category" empty state.

## Files touched

- `LibraryView.swift` — reworked: segmented picker, `selectedKind` state,
  unified sidebar list switching on kind, contextual toolbar, absorbs the
  drafts list/delete logic.
- `LibraryKind.swift` — new enum.
- `DraftRow.swift` / `CategoryRow.swift` — extracted row views.
- `DraftsView.swift` — removed (its detail-pane role disappears).
- `CategoryView.swift` / `StoryView.swift` / `DraftView.swift` — unchanged.

## Out of scope

- Server and data-model changes.
- Any new kind (outlines) — the seam is left for a future task.
- Per-kind selection memory across switches.
