# Library kind-switcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Library sidebar's "Drafts row + category tree" with a segmented *kind switcher* (`Drafts | Stories`), leaving a clean seam for future kinds (e.g. outlines).

**Architecture:** `LibraryView` stays a single `NavigationSplitView`. A `Picker(.segmented)` bound to a new `LibraryKind` enum sits at the top of the sidebar; the list below switches on the selected kind — drafts list for `.drafts`, category list for `.stories`. Selecting a sidebar row drives a unified `LibrarySelection` that the detail pane renders (`DraftView` or `CategoryView` → `StoryView`). The drafts list/delete logic moves out of `DraftsView` (which is removed) and into `LibraryView`.

**Tech Stack:** SwiftUI, Swift Testing (`import Testing`, `@Test`, `#expect`), Xcode project with filesystem-synchronized groups (no `project.pbxproj` edits needed to add/remove files).

**Spec:** `docs/superpowers/specs/2026-05-30-library-kind-switcher-design.md`

---

## Build & test commands (used throughout)

Pick an available simulator name first (run once):

```bash
xcrun simctl list devices available | grep iPhone
```

Use a name from that list in the `-destination` flag below (the plan assumes `iPhone 16` — substitute if needed).

Run the test target:

```bash
xcodebuild test -project /Users/paulv/Projects/fabulis/client/Fabulis.xcodeproj \
  -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' 2>&1 | tail -40
```

Build only (faster, for view-only changes):

```bash
xcodebuild build -project /Users/paulv/Projects/fabulis/client/Fabulis.xcodeproj \
  -scheme Fabulis -destination 'platform=iOS Simulator,name=iPhone 16' 2>&1 | tail -40
```

---

## File structure

- `client/Fabulis/Views/Library/LibraryKind.swift` — **new.** The kind enum (extensibility point).
- `client/FabulisTests/LibraryKindTests.swift` — **new.** Unit tests for the enum.
- `client/Fabulis/Views/Library/DraftRow.swift` — **new.** Presentational row for a draft.
- `client/Fabulis/Views/Library/CategoryRow.swift` — **new.** Presentational row for a category.
- `client/Fabulis/Views/Library/LibraryView.swift` — **modified.** Reworked sidebar, detail, toolbar, state; absorbs drafts list/delete.
- `client/Fabulis/Views/Library/DraftsView.swift` — **removed.** Its detail-pane role disappears.

---

## Task 1: `LibraryKind` enum (TDD)

**Files:**
- Create: `client/Fabulis/Views/Library/LibraryKind.swift`
- Test: `client/FabulisTests/LibraryKindTests.swift`

- [ ] **Step 1: Write the failing test**

Create `client/FabulisTests/LibraryKindTests.swift`:

```swift
import Testing
@testable import Fabulis

struct LibraryKindTests {
    @Test func labelsAreHumanReadable() {
        #expect(LibraryKind.drafts.label == "Drafts")
        #expect(LibraryKind.stories.label == "Stories")
    }

    @Test func draftsHaveNoCategories() {
        #expect(LibraryKind.drafts.hasCategories == false)
    }

    @Test func storiesHaveCategories() {
        #expect(LibraryKind.stories.hasCategories == true)
    }

    @Test func allCasesOrderedDraftsThenStories() {
        #expect(LibraryKind.allCases == [.drafts, .stories])
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run the test command above.
Expected: FAIL — compile error, `cannot find 'LibraryKind' in scope`.

- [ ] **Step 3: Write minimal implementation**

Create `client/Fabulis/Views/Library/LibraryKind.swift`:

```swift
import Foundation

/// A switchable category of library content. The single extensibility point
/// for the library kind-switcher: add a `case` (and its detail view) to grow.
enum LibraryKind: String, CaseIterable, Identifiable {
    case drafts
    case stories
    // future: case outlines

    var id: String { rawValue }

    var label: String {
        switch self {
        case .drafts: "Drafts"
        case .stories: "Stories"
        }
    }

    /// Whether this kind organizes its items under the shared category
    /// taxonomy. Drafts are a flat list; stories (and future kinds) are
    /// grouped by category.
    var hasCategories: Bool {
        switch self {
        case .drafts: false
        case .stories: true
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run the test command above.
Expected: PASS — all four `LibraryKindTests` pass.

- [ ] **Step 5: Commit**

```bash
cd /Users/paulv/Projects/fabulis
git add client/Fabulis/Views/Library/LibraryKind.swift client/FabulisTests/LibraryKindTests.swift
git commit -m "Add LibraryKind enum for the library kind-switcher"
```

---

## Task 2: Extract `DraftRow` and `CategoryRow` view components

**Files:**
- Create: `client/Fabulis/Views/Library/DraftRow.swift`
- Create: `client/Fabulis/Views/Library/CategoryRow.swift`

These are presentational rows pulled from the existing inline markup (`DraftsView` lines 25–32 for drafts; `LibraryView` lines 107–111 for categories). No unit test — verified by building.

- [ ] **Step 1: Create `DraftRow.swift`**

Create `client/Fabulis/Views/Library/DraftRow.swift`:

```swift
import SwiftUI

/// One row in the drafts list: title plus message count and last-updated time.
struct DraftRow: View {
    let draft: DraftSummary

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(draft.title ?? "Untitled draft").font(.body)
            Text("\(draft.messageCount) message\(draft.messageCount == 1 ? "" : "s") · \(draft.updatedAt.formatted(date: .abbreviated, time: .shortened))")
                .font(.caption2).foregroundStyle(.secondary)
        }
    }
}
```

- [ ] **Step 2: Create `CategoryRow.swift`**

Create `client/Fabulis/Views/Library/CategoryRow.swift`:

```swift
import SwiftUI

/// One row in the category list: name plus a story count.
struct CategoryRow: View {
    let category: CategorySummary

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(category.name).font(.body)
            Text("\(category.storyCount) \(category.storyCount == 1 ? "story" : "stories")")
                .font(.caption2).foregroundStyle(.secondary)
        }
    }
}
```

- [ ] **Step 3: Build to verify both compile**

Run the build command above.
Expected: BUILD SUCCEEDED. (The new views are unused for now — that is fine.)

- [ ] **Step 4: Commit**

```bash
cd /Users/paulv/Projects/fabulis
git add client/Fabulis/Views/Library/DraftRow.swift client/Fabulis/Views/Library/CategoryRow.swift
git commit -m "Extract DraftRow and CategoryRow presentational views"
```

---

## Task 3: Rework `LibraryView` to the kind switcher

**Files:**
- Modify: `client/Fabulis/Views/Library/LibraryView.swift` (full rewrite)

This replaces the `draftsRoot`/`category` selection model and the "Drafts section + Library section" sidebar with: a segmented `LibraryKind` picker, a kind-driven sidebar list, a contextual toolbar, and a unified detail switch. It also absorbs the drafts loading + delete logic that currently lives in `DraftsView`.

- [ ] **Step 1: Replace the entire file contents**

Overwrite `client/Fabulis/Views/Library/LibraryView.swift` with:

```swift
import SwiftUI

enum LibrarySelection: Hashable {
    case draft(id: Int)
    case category(id: Int, name: String)
}

struct LibraryView: View {
    @State private var selectedKind: LibraryKind = .stories
    @State private var categories: [CategorySummary] = []
    @State private var drafts: [DraftSummary] = []
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var creatingDraft = false
    @State private var selection: LibrarySelection?
    @State private var showingNewCategorySheet = false
    @State private var showingSettingsSheet = false
    @State private var categoryPendingDeletion: CategorySummary?
    @State private var draftPendingDeletion: DraftSummary?

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationTitle("Library")
                .toolbar { toolbarContent }
                .onChange(of: selectedKind) { _, _ in selection = nil }
                .sheet(isPresented: $showingNewCategorySheet) {
                    EditCategorySheet(mode: .create, initialName: "", onSaved: {
                        Task { await load() }
                    })
                }
                .sheet(isPresented: $showingSettingsSheet) {
                    NavigationStack { SettingsView() }
                }
                .alert("Delete category?",
                       isPresented: Binding(
                            get: { categoryPendingDeletion != nil },
                            set: { if !$0 { categoryPendingDeletion = nil } }),
                       presenting: categoryPendingDeletion,
                       actions: { category in
                            Button("Cancel", role: .cancel) {}
                            Button("Delete", role: .destructive) {
                                Task { await deleteCategory(category) }
                            }
                       },
                       message: { _ in
                            Text("This deletes the category and all its stories. This cannot be undone.")
                       })
                .alert("Delete draft?",
                       isPresented: Binding(
                            get: { draftPendingDeletion != nil },
                            set: { if !$0 { draftPendingDeletion = nil } }),
                       presenting: draftPendingDeletion,
                       actions: { draft in
                            Button("Cancel", role: .cancel) {}
                            Button("Delete", role: .destructive) {
                                Task { await deleteDraft(draft) }
                            }
                       },
                       message: { _ in
                            Text("This deletes the draft and its messages. This cannot be undone.")
                       })
                .task { await load() }
                .refreshable { await load() }
        } detail: {
            detail
        }
    }

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem(placement: .topBarLeading) {
            switch selectedKind {
            case .drafts:
                Button {
                    Task { await createDraft() }
                } label: {
                    HStack(spacing: 4) {
                        if creatingDraft { ProgressView().controlSize(.mini) }
                        else { Image(systemName: "plus") }
                        Text("New Draft")
                    }
                }
                .disabled(creatingDraft)
            case .stories:
                Button { showingNewCategorySheet = true } label: {
                    Label("New Category", systemImage: "folder.badge.plus")
                }
            }
        }
        ToolbarItem(placement: .topBarTrailing) {
            Button { showingSettingsSheet = true } label: {
                Image(systemName: "gear")
            }
        }
    }

    @ViewBuilder
    private var sidebar: some View {
        VStack(spacing: 0) {
            Picker("Kind", selection: $selectedKind) {
                ForEach(LibraryKind.allCases) { kind in
                    Text(kind.label).tag(kind)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            .padding(.vertical, 8)

            sidebarList
        }
    }

    @ViewBuilder
    private var sidebarList: some View {
        if isLoading && categories.isEmpty && drafts.isEmpty {
            ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load library").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary).multilineTextAlignment(.center)
                Button("Retry") { Task { await load() } }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
        } else {
            switch selectedKind {
            case .drafts: draftsList
            case .stories: categoriesList
            }
        }
    }

    @ViewBuilder
    private var draftsList: some View {
        if drafts.isEmpty {
            ContentUnavailableView("No drafts", systemImage: "doc.text",
                description: Text("Tap “New Draft” to start a story."))
        } else {
            List(selection: $selection) {
                ForEach(drafts) { draft in
                    DraftRow(draft: draft)
                        .tag(LibrarySelection.draft(id: draft.id))
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                draftPendingDeletion = draft
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                        .contextMenu {
                            Button(role: .destructive) {
                                draftPendingDeletion = draft
                            } label: {
                                Label("Delete Draft", systemImage: "trash")
                            }
                        }
                }
            }
        }
    }

    @ViewBuilder
    private var categoriesList: some View {
        if categories.isEmpty {
            ContentUnavailableView("No categories",
                systemImage: "books.vertical",
                description: Text("Save a draft to a category to see it here."))
        } else {
            List(selection: $selection) {
                ForEach(categories) { category in
                    CategoryRow(category: category)
                        .tag(LibrarySelection.category(id: category.id, name: category.name))
                        .swipeActions(edge: .trailing) {
                            Button(role: .destructive) {
                                categoryPendingDeletion = category
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                        .contextMenu {
                            Button(role: .destructive) {
                                categoryPendingDeletion = category
                            } label: {
                                Label("Delete Category", systemImage: "trash")
                            }
                        }
                }
            }
        }
    }

    @ViewBuilder
    private var detail: some View {
        switch selection {
        case .draft(let id):
            NavigationStack {
                DraftView(draftId: id).id(id)
            }
        case .category(let id, let name):
            NavigationStack {
                CategoryView(categoryId: id, categoryName: name, onDeleted: {
                    selection = nil
                    Task { await load() }
                })
                .id(id)
            }
        case .none:
            ContentUnavailableView("Select a draft or category",
                systemImage: "books.vertical",
                description: Text("Pick a category to read its stories, or open Drafts to keep working."))
        }
    }

    private func load() async {
        do {
            errorMessage = nil
            async let lib = FabulisAPIClient.shared.library()
            async let draftList = FabulisAPIClient.shared.listDrafts()
            categories = try await lib.categories
            drafts = try await draftList
        } catch APIError.unauthorized {
            errorMessage = "Session expired."
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func deleteCategory(_ category: CategorySummary) async {
        if case .category(let id, _) = selection, id == category.id {
            selection = nil
        }
        categories.removeAll { $0.id == category.id }
        do {
            try await FabulisAPIClient.shared.deleteCategory(id: category.id)
        } catch {
            errorMessage = error.localizedDescription
            await load()
        }
    }

    private func deleteDraft(_ draft: DraftSummary) async {
        if case .draft(let id) = selection, id == draft.id {
            selection = nil
        }
        drafts.removeAll { $0.id == draft.id }
        do {
            try await FabulisAPIClient.shared.deleteDraft(id: draft.id)
        } catch {
            errorMessage = error.localizedDescription
            await load()
        }
    }

    private func createDraft() async {
        creatingDraft = true
        defer { creatingDraft = false }
        do {
            let draft = try await FabulisAPIClient.shared.createDraft()
            await load()
            selectedKind = .drafts
            selection = .draft(id: draft.id)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

extension CategorySummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: CategorySummary, rhs: CategorySummary) -> Bool { lhs.id == rhs.id }
}

extension DraftSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: DraftSummary, rhs: DraftSummary) -> Bool { lhs.id == rhs.id }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run the build command above.
Expected: BUILD SUCCEEDED. (`DraftsView.swift` still exists and still compiles standalone; it is just no longer referenced. It is removed in Task 4.)

- [ ] **Step 3: Commit**

```bash
cd /Users/paulv/Projects/fabulis
git add client/Fabulis/Views/Library/LibraryView.swift
git commit -m "Rework LibraryView into a segmented kind-switcher"
```

---

## Task 4: Remove `DraftsView.swift`

**Files:**
- Remove: `client/Fabulis/Views/Library/DraftsView.swift`

Its detail-pane role (a standalone drafts list with its own nav stack) no longer exists — the drafts list now lives in `LibraryView`'s sidebar.

- [ ] **Step 1: Confirm nothing references `DraftsView`**

```bash
cd /Users/paulv/Projects/fabulis
grep -rn "DraftsView" client/Fabulis
```

Expected: only matches inside `client/Fabulis/Views/Library/DraftsView.swift` itself (the `struct DraftsView` declaration). No references from other files.

- [ ] **Step 2: Delete the file**

```bash
cd /Users/paulv/Projects/fabulis
git rm client/Fabulis/Views/Library/DraftsView.swift
```

- [ ] **Step 3: Build and run the full test suite**

Run the test command above.
Expected: BUILD SUCCEEDED and all tests pass (including `LibraryKindTests` and the existing `DraftEditLogicTests`).

- [ ] **Step 4: Manual smoke test**

Open the app (simulator or Mac Catalyst) and confirm:
- Sidebar shows a `Drafts | Stories` segmented control, defaulting to **Stories**.
- **Stories**: categories listed; tapping one shows its stories; tapping a story opens it. Toolbar shows **New Category** (`folder.badge.plus`); swipe-to-delete on a category works.
- **Drafts**: drafts listed directly in the sidebar; tapping one opens the editor in the detail pane. Toolbar shows **New Draft**; tapping it creates a draft, switches to the Drafts segment, and opens it. Swipe-to-delete on a draft works.
- Switching the segment clears the detail back to the empty state.
- On an iPhone simulator: the sidebar collapses to a root screen (segmented control + list), and navigation pushes as expected.

- [ ] **Step 5: Commit**

```bash
cd /Users/paulv/Projects/fabulis
git commit -m "Remove DraftsView; its role moved into the LibraryView sidebar"
```

---

## Self-review notes

- **Spec coverage:** kind enum (Task 1) ✓; segmented switcher + state + clearing selection on switch (Task 3) ✓; drafts sidebar=list / detail=DraftView and stories sidebar=categories / detail=CategoryView→StoryView (Task 3) ✓; unified `LibrarySelection` (Task 3) ✓; contextual toolbar New Draft/New Category + always-on Settings (Task 3) ✓; default to Stories (Task 3, `selectedKind = .stories`) ✓; drafts load/delete absorbed + `DraftsView` removed (Tasks 3–4) ✓; iPhone collapse (Task 4 smoke test) ✓; empty states preserved (Task 3) ✓.
- **Out of scope (not in any task, by design):** server/data-model changes, outlines, per-kind selection memory.
- **Type consistency:** `LibrarySelection` cases `.draft(id:)` / `.category(id:name:)` used consistently across sidebar tags, detail switch, and delete guards; `LibraryKind` cases `.drafts` / `.stories` consistent across enum, picker, toolbar, and sidebar switches.
