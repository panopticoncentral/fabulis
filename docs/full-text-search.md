# Full-text search

Use **Search Everything** in the library toolbar, or **Shift–Command–F** on
Mac. Search covers saved library content: category names; story titles and
summaries; every saved version's prompt and response messages; draft titles
and messages; prompt titles and messages; one-liners; tropes; storyteller names,
writing and titling instructions, and model names; and saved summary instructions.
Unsaved editor text and generation chunks that have not been saved are not indexed.

Results are ranked, show highlighted excerpts, and open the matching item.
Version results open the exact version, including older versions. Summary matches
open the summary. Category results offer all four kinds of category content.
The search sheet preserves the library's current browsing position.

Search is case- and accent-insensitive. Each word is a prefix ("light" finds
"lighthouse"); every entered word must match the same item, but words can occur
in different messages of one conversation. Punctuation separates words; FTS
operators and quoted-phrase syntax are not exposed. This is word-prefix search,
not arbitrary substring, fuzzy, or semantic search.

`GET /api/v1/search?q=…&limit=50&offset=0` requires an unlocked, authenticated
session and returns `results` and `hasMore`. Queries are limited to 500 characters;
page size is clamped to 1–100. The client debounces typing, cancels stale requests,
and offers more pages. Concurrent edits can change rankings between pages.

The FTS5 index and derived documents are stored inside the existing SQLCipher
vault. No external search service or plaintext index is used. Credentials and
service URLs are excluded; only the `SummaryPrompt` setting is allowlisted.
Search responses have `Cache-Control: no-store`.

Schema setup creates and backfills the index transactionally on the first unlock. Subsequent unlocks do not reindex. SQLite triggers
keep it current for API writes, direct SQL/CLI imports, changes in message parents,
renames, and cascade deletes. One conversation is one search document; snippets
and ranking use SQLite's [FTS5 functions](https://www.sqlite.org/fts5.html).
Future changes to indexed fields need an explicit index migration.

Deploy the rebuilt server and Apple client together, restart the server, and
unlock the vault to build the initial index. The first unlock can take longer for
a large library. This implements and expands the earlier story-only design in
`superpowers/specs/2026-05-21-story-full-text-search-design.md`.
