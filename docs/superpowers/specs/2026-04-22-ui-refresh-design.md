# UI refresh — literary aesthetic, sidebar layout, page overhaul

## Problem

The current Fabulis UI is utilitarian: a thin top navbar, flat story lists, system fonts, and a basic chat interface for drafts. It works but feels primitive. Everything lives in a single `wwwroot/app.css` with ad-hoc selectors per page. The interface doesn't reflect that this is a tool for making stories.

## Goal

A full visual and structural overhaul of the Blazor Server UI — warm "literary" aesthetic (paper tones, serif display, quiet sans-serif chrome, gold accent), persistent left sidebar for navigation, and substantive page redesigns that lean into the storytelling identity. Light mode only. No new features, no data-model or routing changes.

## Non-goals

- No new features, no data-model changes, no routing changes, no new pages.
- No dark mode.
- No new external dependencies (JavaScript frameworks, CSS frameworks, icon libraries).
- No backend work. Blazor components may be reorganized but server services are untouched.
- No accessibility audit beyond the baseline contrast of the palette — a dedicated a11y pass is future work.

## Design direction

### Aesthetic — Literary & Editorial

Warm paper background, ink foreground, gold accent, seal red for destructive actions. Serif display/prose (Fraunces variable), sans-serif UI chrome (Inter). System-font fallbacks.

### Palette

| Name       | Hex       | Role                                       |
| ---------- | --------- | ------------------------------------------ |
| Paper      | `#faf6ec` | App background                             |
| Parchment  | `#f5efe4` | Elevated surfaces (sidebar, prompt bubble) |
| Linen      | `#f0e7d3` | Secondary surfaces, chip backgrounds       |
| Ink        | `#2b2417` | Primary text, primary button               |
| Ink-muted  | `#6b5f44` | Secondary text                             |
| Meta       | `#8a7a5c` | Metadata, captions                         |
| Rule       | `#e0d6c2` | Borders, hairlines                         |
| Gilt       | `#c9a85a` | Accent (focus ring, CTA, eyebrow label)    |
| Seal       | `#9b3535` | Destructive actions only                   |

All tokens are exposed as CSS custom properties on `:root`.

### Typography

- **Fraunces** (variable weight/softness) — display titles, page titles, prose/body in reading mode and draft responses, story-card titles.
- **Inter** — navigation, buttons, forms, metadata, eyebrows/labels.
- Scale (approximate): display 36/42, h1 28/34, h2 22/28, prose 17/28, body 15/22, meta 13/18, eyebrow 12 uppercase.
- Both fonts self-hosted under `wwwroot/fonts/` with `@font-face` and `font-display: swap`. No Google Fonts runtime dependency.

### Layout — Sidebar shell

`MainLayout.razor` becomes a two-column shell:

- **Left rail (240px fixed)** on `#f5efe4` parchment:
  - Brand mark `❦ Fabulis` in Fraunces (links to `/`)
  - Primary CTA `+ New Story` (links to `/stories/new`)
  - **Library** section header (links to `/library`) followed by the list of categories queried from the DB, each with its story count, linking to `/categories/{id}`. If there are no categories, the section shows just the `Library` link.
  - **Workshop** section: `Storytellers` (links to `/storytellers`)
  - **Tools** section: `Import`, `Export`, `Settings`
  - Footer: vault lock-status badge (`🔓 Vault unlocked` / `🔒 Locked`)
- **Main area** on `#faf6ec` paper with breadcrumb + serif page title + action slot.

Responsive:

- Under 900px: rail collapses to a 64px icon-only strip.
- Under 600px: rail becomes off-canvas drawer toggled by a hamburger in a slim top bar.

A separate **`CenteredLayout.razor`** variant is used by pages that opt out of the sidebar (Unlock, locked Home, Version reading mode). Same palette, no rail.

### Component vocabulary

Primitives defined in `components.css`:

- Buttons: `.btn-primary` (ink), `.btn-secondary` (outline), `.btn-accent` (gilt, used for `Generate` / `Resume`), `.btn-danger` (seal), `.btn-ghost`.
- Form fields: `.field` wrapper, inputs/textareas with `#fdfaf2` background, `#d7cab0` border, gilt focus ring (`0 0 0 3px rgba(201,168,90,0.18)`).
- Story card: eyebrow (gilt, uppercase — e.g., `Fantasy · 3 versions`) → Fraunces title → optional italic Fraunces excerpt pulled from the latest version's first response (if cheap to compute) → meta row with last-edited timestamp. Entities have no `Tags` field today, so tag chips are not used on story cards.
- Category chip: linen background, ink-muted text, pill. Used on the Story page header to link back to the parent category.
- Message bubbles (draft): `.msg.prompt` on parchment with rule border; `.msg.response` on paper with rule border, body in Fraunces.
- Flourish: a small ornamental divider component (`❦ ❦ ❦` in gilt) for section breaks and empty states.

### Page changes

| Page                  | Change                                                                                                                                                                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Home (unlocked)**   | Replaces two-line welcome with a dashboard: eyebrow + Fraunces greeting; hero "Continue writing" card (most-recently-edited draft, gilt left border, `Resume draft` accent button); stats panel (Stories / Drafts / Storytellers); Recently-written row of up to 3 story mini-cards ordered by last-modified version. If there is no in-progress draft, the hero is replaced with a `+ Start a new story` call-to-action card. All data comes from existing tables; no schema changes. |
| **Home (locked)**     | Uses `CenteredLayout`. Brand flourish, lede, single accent `Unlock your vault` button.                                                                                                                                                       |
| **Library**           | Grid of **category cards** (not stories — `/library` is the category index). Each card shows category name (Fraunces), story count, and the most recent story title if any. "Add category" form becomes a single paper card at the top of the page. Empty state uses `Flourish` + instructional copy. |
| **Category**          | 2-column **story-card grid** for stories inside the category. Header has Fraunces category name + story count + inline rename / delete affordances (preserving current query-string actions `?action=edit` / `?action=delete`). "Add story" form becomes a primary-action field at the top of the grid. |
| **Story**             | Fraunces title + parent-category chip (links back to `/categories/{id}`) + versions list as vertical timeline of version cards (version number, date, `Read` action). `+ New draft` / `Continue draft` CTA at top. Preserves current actions.|
| **Version**           | Reading mode — uses `CenteredLayout` (no sidebar). Narrow column (~640px), Fraunces 17/28, drop-cap on first paragraph, `❦ ❦ ❦` flourish between intro and body. Breadcrumb returns to the story.                                             |
| **New Story**         | Centered form card with paper treatment. Prompt assistant lives in a gilt-bordered sub-card.                                                                                                                                                 |
| **Draft**             | Writing-studio two-pane layout (≥1024px): conversation on left with prompt/response bubbles; `StorytellerPanel` on right showing active storyteller name, model chip, sampling params (Temperature, Top P, Min P, Top K, Top A), and prompt excerpt. Sticky bottom input with `✦ Generate` gilt CTA. Under 1024px the panel collapses into a disclosure above the conversation. |
| **Save Draft**        | Restyle only — form primitives.                                                                                                                                                                                                              |
| **Storytellers**      | List of storyteller cards (name · model chip · italic prompt excerpt · edit link).                                                                                                                                                           |
| **Storyteller**       | Detail page groups fields into three panels: **Model**, **Sampling**, **Prompt**. Model picker is a searchable paper list (restyle of existing).                                                                                             |
| **Settings**          | Fraunces section headers. Each row: label + description + control in a 2-column grid.                                                                                                                                                        |
| **Unlock**            | `CenteredLayout`. Flourish above a single-card form. Gilt CTA `Open vault`.                                                                                                                                                                  |
| **Import / Export**   | Centered card per page. Restyle only.                                                                                                                                                                                                        |

## Architecture

### CSS structure

Replace the single `wwwroot/app.css` with:

```
wwwroot/
  css/
    tokens.css       — :root CSS variables for palette, fonts, spacing, radii, shadows
    base.css         — reset, html/body, typography defaults, link styles
    shell.css        — sidebar layout, main grid, responsive breakpoints
    components.css   — buttons, fields, story card, message bubbles, chips, flourish, empty state
    pages.css        — page-specific rules that don't warrant a component
  fonts/
    Fraunces-Variable.woff2
    Inter-Variable.woff2
```

All five CSS files are linked from `App.razor` in cascade order: tokens → base → shell → components → pages.

### Shared Razor components

New files under `src/Fabulis.Server/Components/Shared/`:

- `Sidebar.razor` — brand, nav sections, category list (queries the DB via `FabulisDbContext` for the Categories + their story counts when the vault is unlocked; empty otherwise), vault status badge reflecting `VaultService.IsUnlocked`.
- `PageHeader.razor` — parameters: `Breadcrumb` (ChildContent), `Title`, `Actions` (RenderFragment).
- `StoryCard.razor` — parameters: `Story` model; renders eyebrow (parent category + version count), Fraunces title, optional italic excerpt, and a meta row with last-edited timestamp.
- `ContinueWritingCard.razor` — hero variant used on dashboard.
- `StorytellerPanel.razor` — right-side panel on draft page; parameters: `Storyteller`.
- `EmptyState.razor` — Flourish + heading + body + optional action slot.
- `Flourish.razor` — `❦ ❦ ❦` divider with spacing.

### Layout components

- `Components/Layout/MainLayout.razor` — restructured into sidebar shell with `<Sidebar />` + `<main>@Body</main>`.
- `Components/Layout/CenteredLayout.razor` — new; single-column centered content for Unlock and locked Home.
- Pages that need the centered variant use `@layout CenteredLayout`.

### Icons

Literary theme is served primarily by Unicode ornaments (`❦ ❧ ☙ ✦ ❀`) for decorative use. Functional icons (`+`, `✎`, `↓`, `↑`, `⚙`, `🔓`, `🔒`) also use Unicode/emoji initially to avoid introducing an icon library. An inline-SVG icon component can be added later if needed.

### Page-specific razor changes

Each page under `Components/Pages/` gets its markup rewritten to use the new shell and shared components. Logic (`@code` blocks, service injection, event handlers) is preserved — only the rendered markup changes. Where pages currently use inline class names (e.g. `.item-list`, `.page-header`, `.detail-form`), those are replaced with calls to shared components or new class names aligned with `components.css`.

## Rollout

Five sequential steps, each independently reviewable:

1. **Foundation** — add `tokens.css`, `base.css`, self-host Fraunces + Inter fonts, wire them into `App.razor`. Remove no existing styles yet. App looks slightly different (new fonts + background) but still works.
2. **Shell** — introduce `MainLayout` sidebar, `CenteredLayout`, `Sidebar` component. `shell.css`. Every page now renders in the new chrome with its original content.
3. **Primitives** — `components.css`, shared components (`PageHeader`, `StoryCard`, `EmptyState`, `Flourish`). Update existing pages' shared chrome (buttons, forms) to use new classes. Old `wwwroot/app.css` is removed.
4. **Page upgrades — pass 1** — high-impact pages: Library grid, Draft writing studio (with `StorytellerPanel`), Home dashboard, Version reading mode.
5. **Page upgrades — pass 2** — Story, Storytellers, Settings, New Story, Unlock, Category, Import/Export, locked Home.

## Risks and trade-offs

- **Single-developer app, no visual regression tests.** Rollout order lets each step be reviewed in the browser before moving on.
- **Unicode ornaments render differently across platforms.** On macOS they look good in system fonts; on Windows/Linux some glyphs may fall back. Accepted for v1; can be replaced with SVG later.
- **Self-hosted fonts add ~200KB to first load.** Acceptable for a local personal app; `font-display: swap` avoids blocking render.
- **Reading mode on Version page opts out of the sidebar.** Getting back to nav requires the breadcrumb link or browser back. Considered acceptable for the reading use case.
- **CSS variables, not a preprocessor.** Fine for this scale; anyone customizing tokens does it in one file.

## Success criteria

- App feels consistent: every page uses the literary palette, serif display, gold focus rings, and shared primitives.
- Navigation is always one click from any screen (via sidebar).
- The Draft page makes the active storyteller + sampling settings visible without leaving the page.
- No page retains the old class names (`.add-form`, `.item-list`, `.detail-form`, etc.) after rollout.
- No functional regressions: every route works, every form submits, the vault still unlocks, drafts still generate.
