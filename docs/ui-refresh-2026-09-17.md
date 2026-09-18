# Fabulis UI refresh — September 17, 2026

Implementation follow-up to [the UI review](ui-review-2026-09-17.md). This work retains the native SwiftUI/Mac Catalyst foundation and the existing vault architecture. Preexisting workspace changes were preserved.

## Changes

| Area | Implemented behavior |
| --- | --- |
| Navigation | iOS has Drafts, Stories, and Resources tabs. Mac has named sidebar destinations and a three-column draft/story browser, with titles and their filter in the middle column and the selected content on the right. Destination selections and filters are retained; New Draft is available from the library toolbar. |
| Writing | Unsent text and in-progress message edits are retained by draft ID for the active session. The composer aligns with the reading column, uses contextual field labels, and places editing actions below the field on compact screens. Existing drafts no longer immediately open the keyboard. |
| Editing | Visible message menus supplement context menus. Save Changes preserves the continuation; Regenerate from Here confirms deletion of later messages. The editing buffer survives failed saves/regeneration. Shared dirty-state handling protects supported navigation and sheet-dismissal paths. |
| Saving | Save to Library explicitly separates a new story from a new version. Dependent choices have loading/error states and reject stale responses. Generated titles do not overwrite newer typing. Success offers Open Story. |
| Reading | Saved stories default to continuous prose, with an optional Conversation presentation. Reading options include versions and text size; Listen and Share are directly available. Message backgrounds use restrained system colors, and text respects Dynamic Type without applying scaling twice. |
| Narration | One session-owned player continues across library navigation, with title, preparation state, seek, speed, and adaptive metadata. Locking/disconnecting stops playback. |
| Settings | General, Writing, Narration, and Connection & Vault are separate pages. Failed initial loads show Retry instead of editable defaults. Sampling parameters validate malformed/out-of-range values. Advanced sampling is separated from creativity. |
| Platform details | Mac commands use focused content actions, including Save Changes, Save to Library, Refresh, Listen, and Summary. Model information lives in the scrolling content header on both platforms, below story/version metadata or above the draft conversation. Forms/sheets have deliberate sizes; iOS touch controls have minimum targets. |
| Feedback and consistency | Refresh errors are visible, categories/counts refresh after changes, empty states offer useful actions, summary polling observes missing/stale work, and generation distinguishes preparation/reconnection. Authentication recovery screens scroll. |
| Prompt reuse | Start Draft copies a saved prompt's messages in order through a new authenticated server endpoint, without modifying the template. A seeded draft exposes Generate Response. |

## Verification

The Debug-only UI fixture mode uses sample data and isolated reading preferences. Unsupported fixture requests fail locally rather than accessing a real server or Keychain. Release builds exclude the fixture implementation.

- Server suite: **138 tests passed**, including ordered prompt-to-draft copying, independent draft edits, and missing-template behavior.
- iOS screenshots were inspected on an iPhone SE simulator running iOS 18.6 and an iPhone 17 Pro simulator running iOS 26.5. The small-screen and largest-text checks led to corrections to the edit action layout, toolbar sizing, and Markdown text scaling.
- The UI regression suite covers composition preservation across tabs, Read/Conversation switching, visible listening, successful save/open-story feedback, destructive regeneration confirmation, settings-load recovery, and the largest accessibility text size.
- iOS UI regression suite: **6 tests passed** on iPhone SE / iOS 18.6 (`/tmp/fabulis-ui-verified-tests.xcresult`).
- Swift unit suite: **22 tests in 5 suites passed**, including composition isolation, navigation protection, sampling validation, and editing logic (`/tmp/fabulis-unit-verified-tests.xcresult`).
- Final iOS Debug build passed as part of the unit test run. Final Mac Catalyst Release build also passed (`/tmp/fabulis-ui-mac-final-build.log`).

## Remaining validation and limits

Mac Catalyst Debug and Release compilation succeeded during implementation. Interactive Mac UI testing was obstructed by an unrelated macOS test-runner warning; those failed runs do not establish a Mac interaction regression or a passing Mac interaction test. Keyboard-only navigation, toolbar placement at narrow window sizes, and sheet behavior still need an unobstructed Mac pass.

The fixture checks do not exercise live model generation, actual audio streaming, a real vault, VoiceOver traversal, or all iPad/landscape/contrast combinations. Those remain device/integration checks. The player and navigation changes deserve a real listening session before release.

Draft composition is deliberately session-only; it does not survive process termination. Shared dirty-editor protection covers the implemented navigation, Cancel, and interactive sheet-dismissal paths, but native Mac window close/application quit is not yet guarded for every editor. The optional expanded long-form editor, configurable Return behavior, an iPad-specific three-column layout, and broad full-content search remain separate enhancements.

## Mac story selection follow-up

Following a report that clicking a story did not open it, the Mac content column now uses a native selectable list with value-based navigation links. Its selection is bound to the reader, and the extra navigation stack around the middle column has been removed. The iOS category view retains its pushed reader destination.

Mac UI tests now assert that story prose is actually visible and hittable, not just that a Listen control exists. Reader selection and Read/Conversation switching passed at a 1024-point window width; story selection also passed after resizing to 900 points (`/tmp/fabulis-story-nav-verified.xcresult`). The original failure was not reproduced with sample data at the default width. An earlier 682-point attempt could not reach Stories through the collapsed sidebar; that width is not covered by the passing checks.

The iPhone reader navigation and Read/Conversation regression check also passed (`/tmp/fabulis-story-nav-ios.xcresult`).

These targeted Mac interaction results supersede the earlier test-runner warning limitation for these flows only; the broader device and accessibility matrix remains outstanding.

## Sidebar click-area follow-up

The intermittent-looking Mac sidebar behavior was reproduced as a hit-testing problem. Unselected plain-button rows had transparent backgrounds and only exposed their icon/text as clickable; selected rows exposed the full background. A click in the visible padding beside Stories was ignored. The label now declares a rectangular content shape across its padded width, consistently covering all five destinations.

A Mac UI regression test clicks that previously inert space immediately after launch and switches among every destination, repeating the sequence across three fresh launches. Before the fix, it failed on the first Stories click (`/tmp/fabulis-sidebar-padding-before.xcresult`).

After the fix, all 18 destination clicks across the three launches passed (`/tmp/fabulis-sidebar-verified.xcresult`). The Mac test build and whitespace checks also passed.

## Draft/story column consistency

On Mac, Drafts and Stories now share the same three-column container: destinations/categories on the left, titles in the middle, and the selected editor/reader on the right. The draft filter moves with the draft list and keeps its existing inline text-field style. Loading, error, empty, filtering, and deletion behavior remain attached to that list. iOS keeps its existing navigation.

The Mac story reader UI check passed and the draft layout was visually inspected. The Mac unsent-composition test could not acquire keyboard focus in the UIKit composer. The same test failed with the previous two-column draft layout, so this was not introduced by moving the list; Mac automated typing remains an unresolved verification limitation. This follow-up does not claim a passing Mac composition-preservation test.

Final Mac Catalyst and iOS Simulator Debug builds passed after restoring the three-column layout.
