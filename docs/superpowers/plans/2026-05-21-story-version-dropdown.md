# Story Version Dropdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the story landing page and the version page into one screen — tapping a story title opens the latest version's messages directly, with a compact version dropdown in the toolbar for switching versions.

**Architecture:** Rebuild `StoryView` (the existing navigation destination from `CategoryView`) so it fetches the story's version list, defaults to the latest version, and renders that version's messages. A toolbar `Menu` switches versions. `StoryVersionView` is deleted; its message-rendering folds into `StoryView`.

**Tech Stack:** SwiftUI (iOS 18.5+ / Mac Catalyst), `FabulisAPIClient` (URLSession). No server, DTO, or API changes — `story(id:)` and `storyVersion(storyId:version:)` already exist.

---

## Testing note (read before starting)

The SwiftUI client has **no view-level test infrastructure** — `client/FabulisTests/FabulisTests.swift` is an empty boilerplate stub, and the views are not unit-testable without scaffolding that this change does not justify (YAGNI). The "latest version" selection is a one-liner that relies on the server already ordering versions `VersionNumber` descending (`StoryEndpoints.cs:30`), so `versions.first` is the latest.

Therefore the verification gate for each task is **a clean compile** plus a **manual smoke test**, not a failing-then-passing unit test. This is a deliberate, documented deviation from the usual TDD cycle because there is nothing meaningful to assert in isolation and no existing test pattern to follow.

**Build command used throughout** (adjust the simulator name to one that exists locally — run `xcrun simctl list devices available` to see options):

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected on success: a line ending in `** BUILD SUCCEEDED **`. If the scheme isn't found from the command line (no shared scheme exists), open `client/Fabulis.xcodeproj` in Xcode and build with ⌘B instead.

---

## File Structure

- **Modify/rewrite:** `client/Fabulis/Views/Story/StoryView.swift` — becomes the combined reading screen (story-level fetch + version dropdown + message list).
- **Delete:** `client/Fabulis/Views/Story/StoryVersionView.swift` — its per-version fetch and message loop move into `StoryView`. This file also held `extension StoryVersionSummary: Hashable` only via `StoryView`'s old `navigationDestination` — wait, that extension lives in `StoryView.swift`; see Task 2.
- **Unchanged:** `client/Fabulis/Views/Story/StoryMessageView.swift`, `client/Fabulis/Views/Library/CategoryView.swift` (its `navigationDestination(for: StorySummary.self) { StoryView(...) }` call site is unaffected), `client/Fabulis/Models/APIDtos.swift`, `client/Fabulis/Services/FabulisAPIClient.swift`.

---

## Task 1: Rebuild `StoryView` as the combined reading screen

**Files:**
- Rewrite: `client/Fabulis/Views/Story/StoryView.swift`
- Delete: `client/Fabulis/Views/Story/StoryVersionView.swift`

- [ ] **Step 1: Replace the entire contents of `StoryView.swift`**

Overwrite `client/Fabulis/Views/Story/StoryView.swift` with:

```swift
import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var selectedVersion: Int?
    @State private var versionDetail: StoryVersionDetail?
    @State private var errorMessage: String?
    @State private var isLoadingStory = true
    @State private var isLoadingVersion = false

    var body: some View {
        Group {
            if let detail {
                if detail.versions.isEmpty {
                    ContentUnavailableView("No versions yet", systemImage: "doc.text",
                        description: Text("This story has no saved versions."))
                } else if let versionDetail {
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 12) {
                            ForEach(versionDetail.messages) { message in
                                StoryMessageView(message: message)
                            }
                        }
                        .padding()
                    }
                } else if isLoadingVersion {
                    ProgressView()
                } else if let errorMessage {
                    errorView(errorMessage)
                }
            } else if isLoadingStory {
                ProgressView()
            } else if let errorMessage {
                errorView(errorMessage)
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .toolbar {
            if let detail, !detail.versions.isEmpty, let selectedVersion {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        ForEach(detail.versions) { version in
                            Button {
                                select(version: version.versionNumber)
                            } label: {
                                if version.versionNumber == selectedVersion {
                                    Label("Version \(version.versionNumber)", systemImage: "checkmark")
                                } else {
                                    Text("Version \(version.versionNumber)")
                                }
                            }
                        }
                    } label: {
                        HStack(spacing: 2) {
                            Text("Version \(selectedVersion)")
                            Image(systemName: "chevron.down").font(.caption2)
                        }
                    }
                }
            }
        }
        .task { await loadStory() }
        .refreshable { await loadStory() }
    }

    @ViewBuilder
    private func errorView(_ message: String) -> some View {
        VStack(spacing: 12) {
            Text("Couldn't load story").font(.headline)
            Text(message).font(.caption).foregroundStyle(.secondary)
            Button("Retry") { Task { await loadStory() } }
        }
        .padding()
    }

    private func select(version: Int) {
        guard version != selectedVersion else { return }
        selectedVersion = version
        Task { await loadVersion(version) }
    }

    private func loadStory() async {
        isLoadingStory = true
        do {
            errorMessage = nil
            let storyDetail = try await FabulisAPIClient.shared.story(id: storyId)
            detail = storyDetail
            // Server returns versions ordered VersionNumber descending, so .first is the latest.
            if let latest = storyDetail.versions.first?.versionNumber {
                selectedVersion = latest
                await loadVersion(latest)
            }
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoadingStory = false
    }

    private func loadVersion(_ version: Int) async {
        isLoadingVersion = true
        versionDetail = nil
        do {
            errorMessage = nil
            versionDetail = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoadingVersion = false
    }
}
```

Notes on the logic:
- `loadVersion` sets `versionDetail = nil` before fetching so the body falls through to the `ProgressView` while switching versions (matches the spec: dropdown stays, body shows a spinner).
- The old `extension StoryVersionSummary: Hashable` and the `navigationDestination(for: StoryVersionSummary.self)` are intentionally gone — there is no longer any navigation to a separate version screen. (`StorySummary: Hashable` lives in `CategoryView.swift` and is untouched.)

- [ ] **Step 2: Delete the now-unused version screen**

```bash
git rm client/Fabulis/Views/Story/StoryVersionView.swift
```

This removes the file from the Xcode build. Because the project uses the file-system-synchronized group introduced in recent Xcode (no per-file entries in `project.pbxproj`), no `.pbxproj` edit is needed. Confirm with:

```bash
grep -c "StoryVersionView" client/Fabulis.xcodeproj/project.pbxproj
```

Expected: `0`. If it prints a non-zero number, the file is referenced explicitly in the project and you must remove its references in Xcode (delete the file from the navigator, choosing "Move to Trash") instead of `git rm`.

- [ ] **Step 3: Build**

Run the build command from the Testing note.
Expected: `** BUILD SUCCEEDED **`, with no references to `StoryVersionView` and no "cannot find type" errors.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Views/Story/StoryView.swift
git rm --cached --ignore-unmatch client/Fabulis/Views/Story/StoryVersionView.swift
git commit -m "Collapse story landing page into a version dropdown"
```

(The `git rm` in Step 2 already staged the deletion; the `--cached --ignore-unmatch` line is a harmless no-op if so. If `git status` already shows the deletion staged, just run `git add` on `StoryView.swift` and commit.)

---

## Task 2: Manual smoke test

**Files:** none — verification only.

- [ ] **Step 1: Run the app against a server with data**

Start the server (`dotnet run --project src/Fabulis.Server`), unlock the vault, and run the client on a simulator or Mac Catalyst. You need a category containing at least one story that has **two or more** versions to exercise the dropdown.

- [ ] **Step 2: Verify the navigation and dropdown**

Check each:
- Tapping a story title in a category opens the **messages of the latest version directly** — no intermediate landing page.
- The nav title shows the **story title**.
- The toolbar shows `Version N ▾` where N is the highest version number.
- Tapping the dropdown lists every version, **latest first**, with a checkmark on the current one.
- Selecting an older version shows a brief spinner, then that version's messages; the dropdown label updates to the selected version.
- Pull-to-refresh reloads and returns to the latest version.
- A story with no versions shows "No versions yet" and no dropdown. (If you cannot easily create a zero-version story, this path is low-risk; note it as unverified.)

- [ ] **Step 3: No commit**

This task produces no code changes.

---

## Self-Review

- **Spec coverage:**
  - Navigation collapse (tap title → latest version directly) → Task 1, Step 1 (`loadStory` picks `versions.first`, body renders messages) + Task 2 verification. ✓
  - Nav title = story title → `.navigationTitle(detail?.title ?? fallbackTitle)`. ✓
  - Toolbar top-right compact `Version N ▾` menu, latest-first, with checkmark → toolbar `Menu` over `detail.versions` (already descending). ✓
  - Body = message list reusing `StoryMessageView` → `ForEach(versionDetail.messages)`. ✓
  - Spinner while switching versions, dropdown stays → `versionDetail = nil` + `isLoadingVersion` in `loadVersion`. ✓
  - Empty case → `ContentUnavailableView("No versions yet", …)`, dropdown hidden by `!detail.versions.isEmpty`. ✓
  - `StoryVersionView.swift` deleted; `StoryMessageView` and DTOs untouched → Task 1 Step 2. ✓
  - No server/DTO/API changes → confirmed; only existing client methods called. ✓
- **Placeholder scan:** none — full file contents and exact commands provided.
- **Type consistency:** `StoryDetail.versions: [StoryVersionSummary]` (has `versionNumber`), `StoryVersionDetail.messages: [StoryMessage]`, `StoryMessageView(message:)`, `FabulisAPIClient.shared.story(id:)` / `.storyVersion(storyId:version:)` all match the existing definitions in `APIDtos.swift` and `FabulisAPIClient.swift`. ✓
