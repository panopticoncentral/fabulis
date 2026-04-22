# UI Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the utilitarian Fabulis UI with a literary-aesthetic, sidebar-shell, light-mode design — new palette, new typography, new shared component vocabulary, and structural redesigns for every page (dashboard Home, writing-studio Draft, reading-mode Version, story-card grids).

**Architecture:** Bottom-up — foundation first (tokens, fonts, base CSS), then layout shell and shared components, then page-by-page rewrites. Every step preserves existing `@code` logic; only markup and styling change. No data-model, service, or routing changes.

**Tech Stack:** ASP.NET Core / .NET 10, Blazor Server, EF Core + SQLite/SQLCipher, Markdig for markdown rendering. No test framework is configured for this project — verification is via `dotnet build Fabulis.slnx` plus manual smoke testing in the browser, consistent with prior feature work (see [docs/superpowers/specs/2026-04-21-cancel-generation-design.md](../specs/2026-04-21-cancel-generation-design.md) for precedent).

**Spec:** [docs/superpowers/specs/2026-04-22-ui-refresh-design.md](../specs/2026-04-22-ui-refresh-design.md)

---

## File-structure overview

New files:

```
src/Fabulis.Server/
  wwwroot/
    css/
      tokens.css         (color, font, spacing, radii vars on :root)
      base.css           (reset + typography defaults + link/body styles)
      shell.css          (sidebar shell, centered layout, responsive)
      components.css     (buttons, fields, story card, message bubble, chips, flourish, empty state)
      pages.css          (per-page overrides: draft studio grid, reading mode, dashboard)
    fonts/
      InterVariable.woff2
      Fraunces-Variable.ttf
  Components/
    Layout/
      CenteredLayout.razor   (new — no-sidebar layout)
      MainLayout.razor       (rewritten — sidebar shell)
    Shared/
      Sidebar.razor
      PageHeader.razor
      Flourish.razor
      EmptyState.razor
      StoryCard.razor
      ContinueWritingCard.razor
      StorytellerPanel.razor
```

Deleted: `wwwroot/app.css` (at Task 12).

Every page under `Components/Pages/` has its markup rewritten; `@code` blocks are preserved unchanged unless explicitly noted.

---

### Task 1: Self-host Fraunces and Inter fonts

Download variable font files and place them in `wwwroot/fonts/`. No CSS wiring in this task — just the font files.

**Files:**
- Create: `src/Fabulis.Server/wwwroot/fonts/InterVariable.woff2`
- Create: `src/Fabulis.Server/wwwroot/fonts/Fraunces-Variable.ttf`

- [ ] **Step 1: Create the fonts directory**

Run:

```bash
mkdir -p src/Fabulis.Server/wwwroot/fonts
```

- [ ] **Step 2: Download Inter (variable) from rsms.me (official Inter source)**

Run:

```bash
curl -fL -o src/Fabulis.Server/wwwroot/fonts/InterVariable.woff2 \
  https://rsms.me/inter/font-files/InterVariable.woff2
```

Expected: file size between 300–500 KB. Verify with `ls -la src/Fabulis.Server/wwwroot/fonts/InterVariable.woff2`.

If the URL returns 404 or the file is under 100 KB, fall back to https://gwfh.mranftl.com/fonts/inter (select "latin", "variable", download zip) and extract `inter-latin-wght-normal.woff2` to the same path.

- [ ] **Step 3: Download Fraunces (variable) from the official googlefonts repo**

Run:

```bash
curl -fL -o src/Fabulis.Server/wwwroot/fonts/Fraunces-Variable.ttf \
  'https://github.com/googlefonts/fraunces/raw/main/fonts/variable/Fraunces%5BSOFT%2CWONK%2Copsz%2Cwght%5D.ttf'
```

Expected: file size between 500 KB – 1.2 MB. Verify with `ls -la src/Fabulis.Server/wwwroot/fonts/Fraunces-Variable.ttf`.

If that URL 404s, fall back to https://gwfh.mranftl.com/fonts/fraunces (variable, latin) and extract the resulting WOFF2 file, saving as `Fraunces-Variable.woff2` instead — and adjust the `@font-face` src in Task 2, Step 2 to use `url('/fonts/Fraunces-Variable.woff2') format('woff2-variations')`.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/wwwroot/fonts/
git commit -m "Add self-hosted Fraunces and Inter variable font files"
```

---

### Task 2: Create tokens.css and base.css

Establish the CSS variable layer and body-level typography/reset.

**Files:**
- Create: `src/Fabulis.Server/wwwroot/css/tokens.css`
- Create: `src/Fabulis.Server/wwwroot/css/base.css`

- [ ] **Step 1: Create the css directory**

```bash
mkdir -p src/Fabulis.Server/wwwroot/css
```

- [ ] **Step 2: Write `tokens.css`**

```css
/* Design tokens — single source of truth for palette, type, and layout. */

@font-face {
  font-family: 'Inter';
  src: url('/fonts/InterVariable.woff2') format('woff2-variations');
  font-weight: 100 900;
  font-style: normal;
  font-display: swap;
}

@font-face {
  font-family: 'Fraunces';
  src: url('/fonts/Fraunces-Variable.ttf') format('truetype-variations');
  font-weight: 100 900;
  font-style: normal;
  font-display: swap;
}

:root {
  /* Palette */
  --paper:      #faf6ec;
  --parchment:  #f5efe4;
  --linen:      #f0e7d3;
  --ink:        #2b2417;
  --ink-muted:  #6b5f44;
  --meta:       #8a7a5c;
  --rule:       #e0d6c2;
  --gilt:       #c9a85a;
  --gilt-deep:  #8a6c24;
  --seal:       #9b3535;

  --field-bg:   #fdfaf2;
  --field-border: #d7cab0;
  --gilt-ring:  rgba(201, 168, 90, 0.18);
  --gilt-tint:  rgba(201, 168, 90, 0.14);
  --ink-hover:  rgba(43, 36, 23, 0.04);

  /* Typography */
  --font-serif: 'Fraunces', Georgia, 'Times New Roman', serif;
  --font-sans:  'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  --font-mono:  ui-monospace, 'SF Mono', Menlo, Consolas, monospace;

  /* Spacing (8px grid) */
  --space-1: 0.25rem;
  --space-2: 0.5rem;
  --space-3: 0.75rem;
  --space-4: 1rem;
  --space-5: 1.5rem;
  --space-6: 2rem;
  --space-8: 3rem;

  /* Radii */
  --radius-sm: 4px;
  --radius-md: 6px;
  --radius-lg: 10px;
  --radius-xl: 14px;

  /* Shadows */
  --shadow-sm: 0 1px 2px rgba(43, 36, 23, 0.04);
  --shadow-md: 0 4px 12px rgba(43, 36, 23, 0.06);
  --shadow-lg: 0 10px 30px rgba(43, 36, 23, 0.08);

  /* Sidebar */
  --sidebar-width: 240px;
  --sidebar-width-compact: 64px;
}
```

- [ ] **Step 3: Write `base.css`**

```css
/* Reset + global typography + link defaults. */

*, *::before, *::after {
  box-sizing: border-box;
}

html, body {
  margin: 0;
  padding: 0;
}

body {
  font-family: var(--font-sans);
  font-size: 15px;
  line-height: 1.5;
  color: var(--ink);
  background: var(--paper);
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

h1, h2, h3, h4 {
  font-family: var(--font-serif);
  font-weight: 400;
  color: var(--ink);
  letter-spacing: -0.01em;
  margin: 0 0 var(--space-3);
  font-variation-settings: "SOFT" 50;
}

h1 { font-size: 1.75rem; line-height: 1.2; }
h2 { font-size: 1.35rem; line-height: 1.25; font-weight: 500; }
h3 { font-size: 1.1rem; line-height: 1.3; font-weight: 500; }

p {
  margin: 0 0 var(--space-3);
}

a {
  color: var(--ink);
  text-decoration: underline;
  text-decoration-color: var(--gilt);
  text-decoration-thickness: 1px;
  text-underline-offset: 2px;
}

a:hover {
  text-decoration-thickness: 2px;
}

code, kbd, pre {
  font-family: var(--font-mono);
  font-size: 0.9em;
}

hr {
  border: none;
  border-top: 1px solid var(--rule);
  margin: var(--space-5) 0;
}

/* Utility classes used site-wide */
.eyebrow {
  font-family: var(--font-sans);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--gilt);
}

.label {
  font-family: var(--font-sans);
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: var(--meta);
}

.breadcrumb {
  font-size: 0.85rem;
  color: var(--meta);
  margin-bottom: var(--space-2);
}

.breadcrumb a {
  color: var(--meta);
  text-decoration: none;
}

.breadcrumb a:hover {
  color: var(--ink);
  text-decoration: underline;
  text-decoration-color: var(--gilt);
}

.meta {
  font-size: 0.85rem;
  color: var(--meta);
}

.error-message {
  color: var(--seal);
  font-weight: 500;
  margin: var(--space-2) 0;
}

.success-message {
  padding: var(--space-4);
  background: var(--linen);
  border-left: 3px solid var(--gilt);
  border-radius: 0 var(--radius-md) var(--radius-md) 0;
  margin: var(--space-4) 0;
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/wwwroot/css/
git commit -m "Add design tokens and base CSS for UI refresh"
```

---

### Task 3: Wire tokens + base into App.razor (keep app.css for now)

Add the new stylesheets in cascade order. Leave `app.css` linked so existing pages keep their look until later tasks replace specific rules.

**Files:**
- Modify: `src/Fabulis.Server/Components/App.razor`

- [ ] **Step 1: Update `App.razor` to include the new stylesheets**

Replace `src/Fabulis.Server/Components/App.razor` with:

```razor
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <title>Fabulis</title>
    <link rel="stylesheet" href="css/tokens.css" />
    <link rel="stylesheet" href="css/base.css" />
    <link rel="stylesheet" href="app.css" />
    <HeadOutlet />
</head>

<body>
    <Routes />
    <script src="_framework/blazor.server.js"></script>
</body>

</html>
```

- [ ] **Step 2: Build to verify**

Run:

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds with no errors. Warnings are OK.

- [ ] **Step 3: Manual smoke — run the app and confirm the body background is paper-colored**

Run:

```bash
dotnet run --project src/Fabulis.Server
```

Open `http://localhost:5xxx` in a browser. Expected: the body background is warm cream (`#faf6ec`) and body text uses Inter. Existing page layouts still work (top navbar, forms, etc.). Stop the server with Ctrl+C.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/App.razor
git commit -m "Wire design tokens and base CSS into App.razor"
```

---

### Task 4: Create shell.css

Styles for the sidebar shell and centered-layout variant. Not yet applied — Task 5 creates the components that use these classes.

**Files:**
- Create: `src/Fabulis.Server/wwwroot/css/shell.css`

- [ ] **Step 1: Write `shell.css`**

```css
/* Sidebar shell + centered layout. */

.app-shell {
  display: flex;
  min-height: 100vh;
}

.app-sidebar {
  width: var(--sidebar-width);
  flex-shrink: 0;
  background: var(--parchment);
  border-right: 1px solid var(--rule);
  padding: var(--space-5) var(--space-3);
  display: flex;
  flex-direction: column;
  position: sticky;
  top: 0;
  height: 100vh;
  overflow-y: auto;
}

.app-main {
  flex: 1;
  min-width: 0;
  padding: var(--space-6) var(--space-6);
  max-width: 1100px;
}

.sidebar-brand {
  font-family: var(--font-serif);
  font-weight: 500;
  font-size: 1.35rem;
  letter-spacing: -0.01em;
  color: var(--ink);
  text-decoration: none;
  padding: var(--space-1) var(--space-3) var(--space-5);
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-variation-settings: "SOFT" 50;
}

.sidebar-brand:hover {
  text-decoration: none;
}

.sidebar-brand .flourish {
  color: var(--gilt);
  font-size: 1.1rem;
}

.sidebar-section {
  margin-top: var(--space-4);
}

.sidebar-section-label {
  font-size: 0.65rem;
  font-weight: 700;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--meta);
  padding: 0 var(--space-3);
  margin-bottom: var(--space-2);
}

.sidebar-link {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: 0.45rem var(--space-3);
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  color: var(--ink-muted);
  text-decoration: none;
  margin-bottom: 2px;
}

.sidebar-link:hover {
  background: var(--ink-hover);
  color: var(--ink);
  text-decoration: none;
}

.sidebar-link.active {
  background: var(--ink);
  color: var(--paper);
}

.sidebar-link.active:hover {
  background: var(--ink);
  color: var(--paper);
}

.sidebar-link .icon {
  width: 18px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  opacity: 0.75;
}

.sidebar-link.active .icon {
  color: var(--gilt);
  opacity: 1;
}

.sidebar-link .count {
  margin-left: auto;
  font-size: 0.7rem;
  padding: 0.08rem 0.4rem;
  border-radius: 999px;
  background: rgba(43, 36, 23, 0.06);
  color: var(--meta);
}

.sidebar-link.active .count {
  background: rgba(245, 239, 228, 0.15);
  color: var(--rule);
}

.sidebar-footer {
  margin-top: auto;
  padding: var(--space-3) var(--space-3) 0;
  border-top: 1px solid var(--rule);
}

.lock-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  padding: 0.3rem 0.6rem;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 600;
}

.lock-badge.unlocked {
  background: var(--gilt-tint);
  color: var(--gilt-deep);
}

.lock-badge.locked {
  background: rgba(155, 53, 53, 0.1);
  color: #7a2a2a;
}

/* Centered layout (no sidebar) for Unlock, Home-locked, Version reading mode. */

.centered-layout {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-start;
  padding: var(--space-8) var(--space-4);
  background: var(--paper);
}

.centered-layout > main {
  width: 100%;
  max-width: 640px;
}

/* Page header shared pattern */
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--space-4);
  margin-bottom: var(--space-5);
  flex-wrap: wrap;
}

.page-header .page-title-block { flex: 1; min-width: 0; }
.page-header h1 { margin: 0; }

.page-header .page-actions {
  display: flex;
  gap: var(--space-2);
  flex-shrink: 0;
}

/* Responsive */
@media (max-width: 900px) {
  .app-sidebar {
    width: var(--sidebar-width-compact);
    padding: var(--space-4) var(--space-2);
  }
  .sidebar-brand { font-size: 1rem; padding-left: 0.35rem; }
  .sidebar-brand span:not(.flourish) { display: none; }
  .sidebar-section-label { display: none; }
  .sidebar-link { justify-content: center; padding: 0.5rem; }
  .sidebar-link span:not(.icon):not(.count) { display: none; }
  .sidebar-link .count { display: none; }
  .sidebar-footer .lock-badge { font-size: 0; padding: 0.35rem; }
  .sidebar-footer .lock-badge::before { font-size: 0.85rem; }
  .app-main { padding: var(--space-5) var(--space-4); }
}

@media (max-width: 600px) {
  .app-shell { flex-direction: column; }
  .app-sidebar {
    width: 100%;
    height: auto;
    position: static;
    padding: var(--space-3);
    flex-direction: row;
    align-items: center;
    gap: var(--space-3);
    overflow-x: auto;
    overflow-y: hidden;
  }
  .sidebar-section { margin-top: 0; display: flex; gap: var(--space-1); }
  .sidebar-footer { margin-top: 0; padding: 0; border-top: none; margin-left: auto; }
}
```

- [ ] **Step 2: Wire into `App.razor` after `base.css`**

Modify `src/Fabulis.Server/Components/App.razor` — add a line so the `<head>` stylesheets read:

```razor
    <link rel="stylesheet" href="css/tokens.css" />
    <link rel="stylesheet" href="css/base.css" />
    <link rel="stylesheet" href="css/shell.css" />
    <link rel="stylesheet" href="app.css" />
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/wwwroot/css/shell.css src/Fabulis.Server/Components/App.razor
git commit -m "Add shell.css and wire into App.razor"
```

---

### Task 5: Create the Sidebar shared component

A persistent left rail that queries categories from the DB.

**Files:**
- Create: `src/Fabulis.Server/Components/Shared/Sidebar.razor`

- [ ] **Step 1: Create the Shared directory**

```bash
mkdir -p src/Fabulis.Server/Components/Shared
```

- [ ] **Step 2: Write `Sidebar.razor`**

```razor
@inject VaultService Vault
@inject NavigationManager Nav
@inject IServiceProvider Services
@using Microsoft.EntityFrameworkCore
@implements IDisposable

<aside class="app-sidebar">
    <a href="/" class="sidebar-brand">
        <span class="flourish">&#x2766;</span><span>Fabulis</span>
    </a>

    @if (Vault.IsUnlocked)
    {
        <div class="sidebar-section">
            <a href="/stories/new" class="sidebar-link @ActiveClass("/stories/new")">
                <span class="icon">&#xFF0B;</span><span>New Story</span>
            </a>
        </div>

        <div class="sidebar-section">
            <div class="sidebar-section-label">Library</div>
            <a href="/library" class="sidebar-link @ActiveClass("/library")">
                <span class="icon">&#x2767;</span><span>All Categories</span>
            </a>
            @foreach (var cat in Categories)
            {
                var catPath = "/categories/" + cat.Id;
                <a href="@catPath" class="sidebar-link @ActiveClass(catPath)">
                    <span class="icon">&#x2666;</span>
                    <span>@cat.Name</span>
                    <span class="count">@cat.StoryCount</span>
                </a>
            }
        </div>

        <div class="sidebar-section">
            <div class="sidebar-section-label">Workshop</div>
            <a href="/storytellers" class="sidebar-link @ActiveClass("/storytellers")">
                <span class="icon">&#x2619;</span><span>Storytellers</span>
            </a>
        </div>

        <div class="sidebar-section">
            <div class="sidebar-section-label">Tools</div>
            <a href="/import" class="sidebar-link @ActiveClass("/import")">
                <span class="icon">&#x2193;</span><span>Import</span>
            </a>
            <a href="/export" class="sidebar-link @ActiveClass("/export")">
                <span class="icon">&#x2191;</span><span>Export</span>
            </a>
            <a href="/settings" class="sidebar-link @ActiveClass("/settings")">
                <span class="icon">&#x2699;</span><span>Settings</span>
            </a>
        </div>

        <div class="sidebar-footer">
            <span class="lock-badge unlocked">&#x1F513; Vault unlocked</span>
        </div>
    }
    else
    {
        <div class="sidebar-section">
            <a href="/unlock" class="sidebar-link @ActiveClass("/unlock")">
                <span class="icon">&#x1F512;</span><span>Unlock</span>
            </a>
        </div>
        <div class="sidebar-footer">
            <span class="lock-badge locked">&#x1F512; Vault locked</span>
        </div>
    }
</aside>

@code {
    private record CategoryRow(int Id, string Name, int StoryCount);

    private List<CategoryRow> Categories { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        Nav.LocationChanged += OnLocationChanged;
        await LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Categories = [];
            return;
        }

        try
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
            Categories = await db.Categories
                .OrderBy(c => c.Name)
                .Select(c => new CategoryRow(c.Id, c.Name, c.Stories.Count))
                .ToListAsync();
        }
        catch
        {
            Categories = [];
        }
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        _ = InvokeAsync(async () =>
        {
            await LoadCategoriesAsync();
            StateHasChanged();
        });
    }

    private string ActiveClass(string path)
    {
        var uri = new Uri(Nav.Uri);
        var current = uri.AbsolutePath.TrimEnd('/');
        var target = path.TrimEnd('/');
        if (target.Length == 0) target = "/";
        if (current.Length == 0) current = "/";
        return current == target ? "active" : string.Empty;
    }

    public void Dispose()
    {
        Nav.LocationChanged -= OnLocationChanged;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Shared/Sidebar.razor
git commit -m "Add Sidebar shared component with category list"
```

---

### Task 6: Rewrite MainLayout to use the sidebar shell

**Files:**
- Modify: `src/Fabulis.Server/Components/Layout/MainLayout.razor`

- [ ] **Step 1: Replace `MainLayout.razor`**

```razor
@inherits LayoutComponentBase
@using Fabulis.Server.Components.Shared

<div class="app-shell">
    <Sidebar />
    <main class="app-main">
        @Body
    </main>
</div>
```

- [ ] **Step 2: Add `Fabulis.Server.Components.Shared` to `_Imports.razor`**

Open `src/Fabulis.Server/Components/_Imports.razor` and append:

```razor
@using Fabulis.Server.Components.Shared
```

Full expected file:

```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using Fabulis.Server.Components
@using Microsoft.EntityFrameworkCore
@using Fabulis.Server.Data
@using Fabulis.Server.Components.Shared
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Manual smoke — confirm the sidebar renders**

Run `dotnet run --project src/Fabulis.Server`, open the app, unlock the vault. Expected:
- Left sidebar shows Fabulis brand, `+ New Story`, Library (with any existing categories), Storytellers, Import/Export/Settings, and a "Vault unlocked" badge.
- Clicking Library/Storytellers/etc. navigates and highlights the active link.
- Locked state (before unlock) shows just the Unlock link and a locked badge.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Components/Layout/MainLayout.razor src/Fabulis.Server/Components/_Imports.razor
git commit -m "Rewrite MainLayout to use Sidebar shell"
```

---

### Task 7: Add CenteredLayout and apply it to Unlock

**Files:**
- Create: `src/Fabulis.Server/Components/Layout/CenteredLayout.razor`
- Modify: `src/Fabulis.Server/Components/Pages/Unlock.razor`

- [ ] **Step 1: Write `CenteredLayout.razor`**

```razor
@inherits LayoutComponentBase

<div class="centered-layout">
    <main>
        @Body
    </main>
</div>
```

- [ ] **Step 2: Add `@layout CenteredLayout` to Unlock.razor**

Open `src/Fabulis.Server/Components/Pages/Unlock.razor` and insert, right after the existing `@page` directive at the top, a new line:

```razor
@layout Fabulis.Server.Components.Layout.CenteredLayout
```

The first six lines of the file should now read:

```razor
@page "/unlock"
@layout Fabulis.Server.Components.Layout.CenteredLayout
@using Microsoft.AspNetCore.Components.Routing
@inject VaultService Vault
@inject IServiceProvider Services
@inject NavigationManager Nav
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Manual smoke — Unlock page has no sidebar**

Run the app and navigate to `/unlock` (lock the vault first from Settings if already unlocked). Expected: no sidebar, just the centered unlock form on paper background. `/library` (after unlock) still has the sidebar.

Stop the server.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Components/Layout/CenteredLayout.razor src/Fabulis.Server/Components/Pages/Unlock.razor
git commit -m "Add CenteredLayout and apply to Unlock page"
```

---

### Task 8: Create components.css with buttons, fields, chips, messages, cards

**Files:**
- Create: `src/Fabulis.Server/wwwroot/css/components.css`

- [ ] **Step 1: Write `components.css`**

```css
/* Primitive components: buttons, fields, cards, chips, messages, flourish, empty state. */

/* --- Buttons --- */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  padding: 0.55rem 1.1rem;
  border-radius: var(--radius-md);
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 500;
  line-height: 1.2;
  border: 1px solid transparent;
  cursor: pointer;
  text-decoration: none;
  transition: background 0.12s ease, border-color 0.12s ease, color 0.12s ease;
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--ink);
  color: var(--parchment);
  border-color: var(--ink);
}
.btn-primary:hover:not(:disabled) { background: #1e1910; border-color: #1e1910; color: var(--parchment); text-decoration: none; }

.btn-secondary {
  background: transparent;
  color: var(--ink);
  border-color: #c9b894;
}
.btn-secondary:hover:not(:disabled) { background: var(--linen); color: var(--ink); text-decoration: none; }

.btn-accent {
  background: var(--gilt);
  color: var(--ink);
  border-color: var(--gilt);
  font-weight: 600;
}
.btn-accent:hover:not(:disabled) { background: #d8b867; border-color: #d8b867; color: var(--ink); text-decoration: none; }

.btn-danger {
  background: var(--seal);
  color: #fdf5f0;
  border-color: var(--seal);
}
.btn-danger:hover:not(:disabled) { background: #7a2a2a; border-color: #7a2a2a; color: #fdf5f0; text-decoration: none; }

.btn-ghost {
  background: transparent;
  color: var(--ink-muted);
  border-color: transparent;
  padding: 0.4rem 0.75rem;
  font-size: 0.85rem;
}
.btn-ghost:hover:not(:disabled) { background: var(--ink-hover); color: var(--ink); text-decoration: none; }

.btn-sm {
  padding: 0.35rem 0.7rem;
  font-size: 0.8rem;
}

/* --- Form fields --- */
.field {
  margin-bottom: var(--space-4);
}

.field > label {
  display: block;
  font-family: var(--font-sans);
  font-size: 0.82rem;
  font-weight: 500;
  color: var(--ink-muted);
  margin-bottom: 0.3rem;
}

.field input[type="text"],
.field input[type="password"],
.field input[type="number"],
.field input[type="search"],
.field input[type="email"],
.field textarea,
.field select,
.field .field-input {
  width: 100%;
  padding: 0.55rem 0.75rem;
  font-family: inherit;
  font-size: 0.95rem;
  line-height: 1.4;
  background: var(--field-bg);
  border: 1px solid var(--field-border);
  border-radius: var(--radius-md);
  color: var(--ink);
  box-sizing: border-box;
  transition: border-color 0.12s, box-shadow 0.12s, background 0.12s;
}

.field textarea {
  resize: vertical;
  min-height: 5rem;
}

.field input:focus,
.field textarea:focus,
.field select:focus {
  outline: none;
  border-color: var(--gilt);
  background: #fff;
  box-shadow: 0 0 0 3px var(--gilt-ring);
}

.field-hint {
  font-size: 0.8rem;
  color: var(--meta);
  margin-top: 0.3rem;
}

.field-row {
  display: flex;
  gap: var(--space-2);
  align-items: flex-start;
}

.field-row > .field { flex: 1; margin-bottom: 0; }

/* --- Chips / tags --- */
.chip {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.2rem 0.6rem;
  background: var(--linen);
  color: var(--ink-muted);
  font-size: 0.75rem;
  font-weight: 500;
  border-radius: 999px;
  text-decoration: none;
}

.chip:hover {
  background: var(--rule);
  color: var(--ink);
  text-decoration: none;
}

.chip-gilt {
  background: var(--gilt-tint);
  color: var(--gilt-deep);
  font-family: var(--font-mono);
  font-size: 0.72rem;
}

/* --- Cards --- */
.card {
  background: #fff;
  border: 1px solid var(--rule);
  border-radius: var(--radius-lg);
  padding: var(--space-4) var(--space-5);
  box-shadow: var(--shadow-sm);
}

.card-muted {
  background: var(--parchment);
  border-color: var(--rule);
}

.card-accent {
  position: relative;
  overflow: hidden;
}

.card-accent::before {
  content: "";
  position: absolute;
  top: 0;
  left: 0;
  width: 4px;
  height: 100%;
  background: linear-gradient(180deg, var(--gilt), var(--gilt-deep));
}

/* --- Story card --- */
.story-card {
  display: block;
  background: #fff;
  border: 1px solid var(--rule);
  border-radius: var(--radius-lg);
  padding: var(--space-4) var(--space-5);
  box-shadow: var(--shadow-sm);
  text-decoration: none;
  color: inherit;
  transition: border-color 0.12s, box-shadow 0.12s;
}

.story-card:hover {
  border-color: #c9b894;
  box-shadow: var(--shadow-md);
  text-decoration: none;
}

.story-card .story-eyebrow {
  font-family: var(--font-sans);
  font-size: 0.65rem;
  font-weight: 700;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--gilt);
  margin-bottom: 0.35rem;
}

.story-card .story-title {
  font-family: var(--font-serif);
  font-size: 1.2rem;
  font-weight: 500;
  line-height: 1.25;
  margin: 0 0 0.35rem;
  font-variation-settings: "SOFT" 50;
}

.story-card .story-excerpt {
  font-family: var(--font-serif);
  font-style: italic;
  font-size: 0.92rem;
  color: var(--ink-muted);
  line-height: 1.55;
  margin: 0 0 var(--space-3);
  font-variation-settings: "SOFT" 50;
}

.story-card .story-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.75rem;
  color: var(--meta);
  border-top: 1px solid var(--linen);
  padding-top: 0.5rem;
}

/* --- Card grid --- */
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: var(--space-4);
}

/* --- Message bubbles --- */
.msg {
  padding: var(--space-4);
  border-radius: var(--radius-lg);
  border: 1px solid var(--rule);
  margin-bottom: var(--space-3);
}

.msg.prompt {
  background: var(--parchment);
}

.msg.response {
  background: #fff;
}

.msg .msg-role {
  font-family: var(--font-sans);
  font-size: 0.62rem;
  font-weight: 700;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--meta);
  margin-bottom: 0.35rem;
}

.msg .msg-content {
  font-family: var(--font-serif);
  font-size: 1rem;
  line-height: 1.65;
  color: var(--ink);
  font-variation-settings: "SOFT" 50;
}

.msg.prompt .msg-content {
  font-family: var(--font-sans);
  font-size: 0.95rem;
  line-height: 1.55;
}

.msg .msg-content p:first-child { margin-top: 0; }
.msg .msg-content p:last-child { margin-bottom: 0; }

.msg .msg-content pre {
  background: rgba(0, 0, 0, 0.05);
  padding: var(--space-3);
  border-radius: var(--radius-sm);
  overflow-x: auto;
}

.msg .msg-content :not(pre) > code {
  background: rgba(0, 0, 0, 0.05);
  padding: 0.1em 0.35em;
  border-radius: 3px;
}

.msg .msg-content blockquote {
  border-left: 3px solid var(--gilt);
  margin-left: 0;
  padding-left: var(--space-3);
  color: var(--ink-muted);
  font-style: italic;
}

.msg .msg-actions {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-2);
}

.msg .msg-editing textarea {
  width: 100%;
  padding: var(--space-2);
  border: 1px solid var(--field-border);
  border-radius: var(--radius-md);
  background: var(--field-bg);
  font-family: inherit;
  font-size: 0.95rem;
  resize: vertical;
}

/* --- Flourish divider --- */
.flourish-divider {
  text-align: center;
  color: var(--gilt);
  font-size: 1.25rem;
  letter-spacing: 0.5em;
  margin: var(--space-6) 0;
  padding-left: 0.5em;
  user-select: none;
}

/* --- Empty state --- */
.empty-state {
  text-align: center;
  padding: var(--space-8) var(--space-4);
  color: var(--ink-muted);
}

.empty-state h2 {
  font-family: var(--font-serif);
  font-weight: 400;
  color: var(--ink);
  margin: var(--space-3) 0 var(--space-2);
}

.empty-state p {
  color: var(--meta);
  max-width: 36ch;
  margin: 0 auto var(--space-4);
}

/* --- Confirm bar (destructive confirm) --- */
.confirm-bar {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-3) var(--space-4);
  background: #fbf0ec;
  border: 1px solid #f0c7bd;
  border-left: 3px solid var(--seal);
  border-radius: var(--radius-md);
  margin-bottom: var(--space-4);
  color: #5a2020;
}

/* --- Inline add form (single-line "+ item" pattern) --- */
.inline-add {
  display: flex;
  gap: var(--space-2);
  margin-bottom: var(--space-5);
}

.inline-add input {
  flex: 1;
  padding: 0.55rem 0.75rem;
  background: var(--field-bg);
  border: 1px solid var(--field-border);
  border-radius: var(--radius-md);
  font-size: 0.95rem;
}

.inline-add input:focus {
  outline: none;
  border-color: var(--gilt);
  box-shadow: 0 0 0 3px var(--gilt-ring);
  background: #fff;
}
```

- [ ] **Step 2: Wire into `App.razor`**

Modify `src/Fabulis.Server/Components/App.razor` — the `<head>` stylesheets now read:

```razor
    <link rel="stylesheet" href="css/tokens.css" />
    <link rel="stylesheet" href="css/base.css" />
    <link rel="stylesheet" href="css/shell.css" />
    <link rel="stylesheet" href="css/components.css" />
    <link rel="stylesheet" href="app.css" />
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/wwwroot/css/components.css src/Fabulis.Server/Components/App.razor
git commit -m "Add components.css for buttons, fields, cards, and message bubbles"
```

---

### Task 9: Add Flourish, EmptyState, and PageHeader shared components

**Files:**
- Create: `src/Fabulis.Server/Components/Shared/Flourish.razor`
- Create: `src/Fabulis.Server/Components/Shared/EmptyState.razor`
- Create: `src/Fabulis.Server/Components/Shared/PageHeader.razor`

- [ ] **Step 1: Write `Flourish.razor`**

```razor
<div class="flourish-divider">&#x2766;&#x2009;&#x2766;&#x2009;&#x2766;</div>
```

- [ ] **Step 2: Write `EmptyState.razor`**

```razor
<div class="empty-state">
    <Flourish />
    @if (!string.IsNullOrEmpty(Title))
    {
        <h2>@Title</h2>
    }
    @if (Body is not null)
    {
        <p>@Body</p>
    }
    @if (ChildContent is not null)
    {
        <div>@ChildContent</div>
    }
</div>

@code {
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Body { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

- [ ] **Step 3: Write `PageHeader.razor`**

```razor
<header class="page-header">
    <div class="page-title-block">
        @if (Breadcrumb is not null)
        {
            <p class="breadcrumb">@Breadcrumb</p>
        }
        @if (Eyebrow is not null)
        {
            <p class="eyebrow">@Eyebrow</p>
        }
        @if (TitleContent is not null)
        {
            <h1>@TitleContent</h1>
        }
        else if (!string.IsNullOrEmpty(Title))
        {
            <h1>@Title</h1>
        }
        @if (Subtitle is not null)
        {
            <p class="meta">@Subtitle</p>
        }
    </div>
    @if (Actions is not null)
    {
        <div class="page-actions">@Actions</div>
    }
</header>

@code {
    [Parameter] public RenderFragment? Breadcrumb { get; set; }
    [Parameter] public RenderFragment? Eyebrow { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public RenderFragment? TitleContent { get; set; }
    [Parameter] public RenderFragment? Subtitle { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Components/Shared/Flourish.razor src/Fabulis.Server/Components/Shared/EmptyState.razor src/Fabulis.Server/Components/Shared/PageHeader.razor
git commit -m "Add Flourish, EmptyState, and PageHeader shared components"
```

---

### Task 10: Add StoryCard and ContinueWritingCard shared components

**Files:**
- Create: `src/Fabulis.Server/Components/Shared/StoryCard.razor`
- Create: `src/Fabulis.Server/Components/Shared/ContinueWritingCard.razor`

- [ ] **Step 1: Write `StoryCard.razor`**

```razor
<a href="/stories/@Story.Id" class="story-card">
    <div class="story-eyebrow">@Eyebrow</div>
    <h3 class="story-title">@Story.Title</h3>
    @if (!string.IsNullOrEmpty(Excerpt))
    {
        <p class="story-excerpt">@Excerpt</p>
    }
    <div class="story-meta">
        <span>@VersionsText</span>
        @if (LastEdited is not null)
        {
            <span>@FormatDate(LastEdited.Value)</span>
        }
    </div>
</a>

@code {
    [Parameter, EditorRequired] public Story Story { get; set; } = null!;
    [Parameter] public string? CategoryName { get; set; }

    private string Eyebrow =>
        (CategoryName ?? Story.Category?.Name ?? "Story")
        + " · "
        + VersionsText;

    private string VersionsText =>
        Story.Versions.Count == 1 ? "1 version" : $"{Story.Versions.Count} versions";

    private string? Excerpt => null;

    private DateTime? LastEdited =>
        Story.Versions.Count > 0
            ? Story.Versions.Max(v => v.CreatedAt)
            : Story.CreatedAt;

    private static string FormatDate(DateTime dt)
    {
        var delta = DateTime.UtcNow - dt;
        if (delta.TotalMinutes < 60) return "just now";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return dt.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
```

- [ ] **Step 2: Write `ContinueWritingCard.razor`**

```razor
@if (Draft is null)
{
    <div class="card card-muted card-accent" style="min-height: 180px; display: flex; flex-direction: column; justify-content: center;">
        <p class="eyebrow">Start something new</p>
        <h2 style="margin-bottom: var(--space-3);">Write a new story</h2>
        <p class="meta" style="margin-bottom: var(--space-4);">You have no drafts in progress. Pick a storyteller and begin.</p>
        <div><a href="/stories/new" class="btn btn-accent">&#x2726; New Story</a></div>
    </div>
}
else
{
    <div class="card card-accent">
        <p class="eyebrow">Continue writing &middot; @FormatUpdated(Draft.UpdatedAt)</p>
        <h2 style="margin-bottom: var(--space-2);">@(string.IsNullOrWhiteSpace(Draft.Title) ? "Untitled draft" : Draft.Title)</h2>
        <p class="meta" style="margin-bottom: var(--space-4);">@Draft.Storyteller.Name &middot; @Draft.Messages.Count messages</p>
        <div style="display: flex; gap: var(--space-2);">
            <a href="/stories/draft/@Draft.Id" class="btn btn-accent">Resume draft &rarr;</a>
            <a href="/stories/new" class="btn btn-secondary">New story</a>
        </div>
    </div>
}

@code {
    [Parameter] public Draft? Draft { get; set; }

    private static string FormatUpdated(DateTime dt)
    {
        var delta = DateTime.UtcNow - dt;
        if (delta.TotalMinutes < 60) return "just now";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return dt.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Shared/StoryCard.razor src/Fabulis.Server/Components/Shared/ContinueWritingCard.razor
git commit -m "Add StoryCard and ContinueWritingCard shared components"
```

---

### Task 11: Add StorytellerPanel component

**Files:**
- Create: `src/Fabulis.Server/Components/Shared/StorytellerPanel.razor`

- [ ] **Step 1: Write `StorytellerPanel.razor`**

```razor
<aside class="storyteller-panel">
    <p class="label">Storyteller</p>
    <div class="card" style="margin-bottom: var(--space-4);">
        <h3 style="margin: 0 0 var(--space-1);">@Storyteller.Name</h3>
        <span class="chip chip-gilt">@Storyteller.ModelName</span>
        <div style="margin-top: var(--space-3); border-top: 1px solid var(--linen); padding-top: var(--space-3);">
            <div class="param-row"><span>Temperature</span><strong>@Storyteller.Temperature.ToString("0.00")</strong></div>
            @if (Storyteller.TopP is not null)
            {
                <div class="param-row"><span>Top P</span><strong>@Storyteller.TopP.Value.ToString("0.00")</strong></div>
            }
            @if (Storyteller.MinP is not null)
            {
                <div class="param-row"><span>Min P</span><strong>@Storyteller.MinP.Value.ToString("0.00")</strong></div>
            }
            @if (Storyteller.TopK is not null)
            {
                <div class="param-row"><span>Top K</span><strong>@Storyteller.TopK</strong></div>
            }
            @if (Storyteller.TopA is not null)
            {
                <div class="param-row"><span>Top A</span><strong>@Storyteller.TopA.Value.ToString("0.00")</strong></div>
            }
            @if (Storyteller.MaxTokens is not null)
            {
                <div class="param-row"><span>Max Tokens</span><strong>@Storyteller.MaxTokens</strong></div>
            }
        </div>
    </div>

    <p class="label">Prompt</p>
    <div class="card card-muted" style="font-family: var(--font-serif); font-style: italic; font-size: 0.9rem; color: var(--ink-muted); line-height: 1.55; font-variation-settings: 'SOFT' 50;">
        @(TruncatePrompt(Storyteller.Prompt))
    </div>

    <p style="margin-top: var(--space-3);">
        <a href="/storytellers/@Storyteller.Id" class="btn btn-ghost btn-sm">Edit storyteller</a>
    </p>
</aside>

@code {
    [Parameter, EditorRequired] public Storyteller Storyteller { get; set; } = null!;

    private static string TruncatePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        var trimmed = prompt.Trim();
        return trimmed.Length > 320 ? trimmed[..320].TrimEnd() + "…" : trimmed;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Shared/StorytellerPanel.razor
git commit -m "Add StorytellerPanel component for the Draft page"
```

---

### Task 12: Add pages.css and delete the old app.css

This is the cut-over. Every page now depends on the new CSS files. Subsequent tasks rewrite pages to use the new markup.

**Files:**
- Create: `src/Fabulis.Server/wwwroot/css/pages.css`
- Delete: `src/Fabulis.Server/wwwroot/app.css`
- Modify: `src/Fabulis.Server/Components/App.razor`

- [ ] **Step 1: Write `pages.css`**

```css
/* Page-specific rules that don't warrant a shared component. */

/* --- StorytellerPanel param rows --- */
.param-row {
  display: flex;
  justify-content: space-between;
  padding: 0.2rem 0;
  font-size: 0.82rem;
  color: var(--ink-muted);
}
.param-row strong {
  font-family: var(--font-mono);
  color: var(--ink);
  font-weight: 500;
}

/* --- Draft writing studio --- */
.draft-studio {
  display: grid;
  grid-template-columns: 1fr;
  gap: var(--space-4);
}

@media (min-width: 1024px) {
  .draft-studio {
    grid-template-columns: minmax(0, 1fr) 280px;
  }
}

.draft-conversation {
  min-width: 0;
}

.storyteller-panel .label { margin-bottom: var(--space-2); }

.draft-input {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-4);
  padding: var(--space-3) 0;
  position: sticky;
  bottom: 0;
  background: linear-gradient(to top, var(--paper) 80%, rgba(250, 246, 236, 0));
}

.draft-input textarea {
  flex: 1;
  padding: var(--space-2) var(--space-3);
  font-family: inherit;
  font-size: 0.95rem;
  background: var(--field-bg);
  border: 1px solid var(--field-border);
  border-radius: var(--radius-md);
  resize: vertical;
  min-height: 4rem;
}

.draft-input textarea:focus {
  outline: none;
  border-color: var(--gilt);
  background: #fff;
  box-shadow: 0 0 0 3px var(--gilt-ring);
}

.draft-input button { align-self: flex-end; }

/* --- Dashboard (Home) --- */
.dashboard-hero {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: var(--space-4);
  margin-bottom: var(--space-5);
}
@media (max-width: 720px) {
  .dashboard-hero { grid-template-columns: 1fr; }
}

.stats-card {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.stats-card .stat-row {
  display: flex;
  justify-content: space-between;
  padding: 0.35rem 0;
  border-bottom: 1px dashed var(--rule);
  font-size: 0.9rem;
}
.stats-card .stat-row:last-child { border-bottom: none; }

.stats-card .stat-value {
  font-family: var(--font-serif);
  font-weight: 600;
  color: var(--ink);
  font-variation-settings: "SOFT" 50;
}

.recent-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: var(--space-3);
}

.mini-card {
  display: block;
  background: #fff;
  border: 1px solid var(--rule);
  border-radius: var(--radius-md);
  padding: var(--space-3);
  text-decoration: none;
  color: inherit;
  transition: border-color 0.12s, box-shadow 0.12s;
}
.mini-card:hover {
  border-color: #c9b894;
  box-shadow: var(--shadow-sm);
  text-decoration: none;
}
.mini-card .title {
  font-family: var(--font-serif);
  font-size: 0.95rem;
  font-weight: 500;
  margin: 0 0 0.2rem;
  font-variation-settings: "SOFT" 50;
}
.mini-card .meta { font-size: 0.72rem; color: var(--meta); }

/* --- Version reading mode --- */
.reading-page {
  max-width: 640px;
  margin: 0 auto;
}

.reading-header {
  text-align: center;
  margin-bottom: var(--space-6);
}

.reading-header .breadcrumb {
  text-align: center;
  margin-bottom: var(--space-3);
}

.reading-title {
  font-family: var(--font-serif);
  font-weight: 400;
  font-size: 2.2rem;
  line-height: 1.15;
  letter-spacing: -0.01em;
  margin: 0 0 0.4rem;
  font-variation-settings: "SOFT" 50;
}

.reading-subtitle {
  font-family: var(--font-serif);
  font-style: italic;
  color: var(--meta);
  font-size: 0.95rem;
  margin: 0;
}

.reading-prose {
  font-family: var(--font-serif);
  font-size: 1.1rem;
  line-height: 1.75;
  color: var(--ink);
  font-variation-settings: "SOFT" 50;
}

.reading-prose p { margin: 0 0 var(--space-4); }

.reading-prose > .msg-content:first-child > p:first-child::first-letter,
.reading-prose > p:first-child::first-letter {
  font-family: var(--font-serif);
  font-size: 3.2em;
  line-height: 0.85;
  float: left;
  padding: 0.12em 0.1em 0 0;
  color: var(--gilt);
  font-weight: 500;
}

/* --- Model picker --- */
.model-picker {
  margin-top: var(--space-2);
}
.model-picker-search { display: flex; gap: var(--space-2); margin-bottom: var(--space-2); }
.model-picker-search input {
  flex: 1;
  padding: 0.55rem 0.75rem;
  background: var(--field-bg);
  border: 1px solid var(--field-border);
  border-radius: var(--radius-md);
  font-size: 0.9rem;
}
.model-picker-search input:focus {
  outline: none;
  border-color: var(--gilt);
  box-shadow: 0 0 0 3px var(--gilt-ring);
  background: #fff;
}
.model-picker-list {
  max-height: 320px;
  overflow-y: auto;
  border: 1px solid var(--rule);
  border-radius: var(--radius-md);
  background: #fff;
}
.model-picker-item {
  padding: 0.55rem 0.75rem;
  cursor: pointer;
  border-bottom: 1px solid var(--linen);
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}
.model-picker-item:last-child { border-bottom: none; }
.model-picker-item:hover { background: var(--parchment); }
.model-picker-item.selected { background: var(--gilt-tint); }
.model-picker-id { font-family: var(--font-mono); font-size: 0.82rem; color: var(--ink); }
.model-picker-name { font-size: 0.78rem; color: var(--meta); }

/* --- Storyteller detail form: grouped panels --- */
.detail-panels {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}
.detail-panel h2 {
  font-family: var(--font-serif);
  font-size: 1.15rem;
  font-weight: 500;
  border-bottom: 1px solid var(--rule);
  padding-bottom: var(--space-2);
  margin: 0 0 var(--space-3);
  font-variation-settings: "SOFT" 50;
}

/* --- Settings sections --- */
.settings-section { margin-bottom: var(--space-6); }
.settings-section > h2 {
  font-family: var(--font-serif);
  font-size: 1.2rem;
  font-weight: 500;
  border-bottom: 1px solid var(--rule);
  padding-bottom: var(--space-2);
  margin-bottom: var(--space-3);
  font-variation-settings: "SOFT" 50;
}
.settings-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: var(--space-4);
  align-items: center;
  padding: var(--space-3) 0;
  border-bottom: 1px dashed var(--rule);
}
.settings-item:last-child { border-bottom: none; }
.settings-item-vertical {
  padding: var(--space-3) 0;
  border-bottom: 1px dashed var(--rule);
}
.settings-item-vertical:last-child { border-bottom: none; }
.settings-label { font-weight: 500; }
.settings-description { color: var(--meta); font-size: 0.85rem; margin: 0.2rem 0 0; }

/* --- Versions timeline --- */
.version-timeline {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  position: relative;
  padding-left: var(--space-4);
}
.version-timeline::before {
  content: "";
  position: absolute;
  left: 6px;
  top: 10px;
  bottom: 10px;
  width: 1px;
  background: var(--rule);
}
.version-timeline .version-item {
  position: relative;
  background: #fff;
  border: 1px solid var(--rule);
  border-radius: var(--radius-md);
  padding: var(--space-3) var(--space-4);
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.version-timeline .version-item::before {
  content: "";
  position: absolute;
  left: calc(-1 * var(--space-4) + 2px);
  top: 50%;
  transform: translateY(-50%);
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--gilt);
  border: 2px solid var(--paper);
}
```

- [ ] **Step 2: Delete `app.css`**

```bash
git rm src/Fabulis.Server/wwwroot/app.css
```

- [ ] **Step 3: Update `App.razor` to drop `app.css` and pick up `pages.css`**

Replace `src/Fabulis.Server/Components/App.razor` with:

```razor
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <title>Fabulis</title>
    <link rel="stylesheet" href="css/tokens.css" />
    <link rel="stylesheet" href="css/base.css" />
    <link rel="stylesheet" href="css/shell.css" />
    <link rel="stylesheet" href="css/components.css" />
    <link rel="stylesheet" href="css/pages.css" />
    <HeadOutlet />
</head>

<body>
    <Routes />
    <script src="_framework/blazor.server.js"></script>
</body>

</html>
```

- [ ] **Step 4: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 5: Manual smoke — pages still render (though some look awkward)**

Run the app. Expected: every page loads without errors. Styles may look off on pages that still use old class names like `.item-list`, `.add-form`, `.detail-form`, etc. — those will be fixed in later tasks. No page should throw server errors.

Stop the server.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/wwwroot/css/pages.css src/Fabulis.Server/Components/App.razor
git commit -m "Remove legacy app.css and add pages.css"
```

---

### Task 13: Rework the Library page as a category grid

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Library.razor`

- [ ] **Step 1: Replace `Library.razor` with a category-grid layout**

```razor
@page "/library"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

<PageHeader Title="Library">
    <Subtitle>@($"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}")</Subtitle>
</PageHeader>

<div class="card" style="margin-bottom: var(--space-5);">
    <EditForm Model="this" OnValidSubmit="AddCategory" FormName="addCategory" class="inline-add">
        <InputText @bind-Value="NewCategoryName" placeholder="New category name…" />
        <button type="submit" class="btn btn-primary">Add category</button>
    </EditForm>
</div>

@if (Categories.Count == 0)
{
    <EmptyState Title="Your library is empty" Body="Create a category above to start organizing stories." />
}
else
{
    <div class="card-grid">
        @foreach (var category in Categories)
        {
            <a href="/categories/@category.Id" class="story-card">
                <div class="story-eyebrow">Category</div>
                <h3 class="story-title">@category.Name</h3>
                <p class="story-excerpt">
                    @if (category.Stories.Count == 0)
                    {
                        <em>no stories yet</em>
                    }
                    else
                    {
                        @(category.Stories.OrderByDescending(s => s.CreatedAt).First().Title)
                    }
                </p>
                <div class="story-meta">
                    <span>@category.Stories.Count @(category.Stories.Count == 1 ? "story" : "stories")</span>
                    <span>@FormatDate(category.CreatedAt)</span>
                </div>
            </a>
        }
    </div>
}

@code {
    private List<Category> Categories { get; set; } = [];

    [SupplyParameterFromForm]
    private string? NewCategoryName { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }
        await LoadCategories();
    }

    private async Task LoadCategories()
    {
        Categories = await Db.Categories
            .Include(c => c.Stories)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    private async Task AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
            return;

        Db.Categories.Add(new Category
        {
            Name = NewCategoryName.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        NewCategoryName = null;
        await LoadCategories();
    }

    private static string FormatDate(DateTime dt) => dt.ToLocalTime().ToString("yyyy-MM-dd");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — Library renders as a category grid**

Run the app, unlock, and navigate to `/library`. Expected: a grid of category cards, each showing "Category" eyebrow, name, most-recent story title (or "no stories yet"), and story count. "Add category" input is at the top in a paper card. Adding a category refreshes the grid. Empty state (if no categories) shows the flourish.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Library.razor
git commit -m "Rework Library page as a category grid"
```

---

### Task 14: Rework the Category page as a story-card grid

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/CategoryPage.razor`

- [ ] **Step 1: Replace `CategoryPage.razor`**

```razor
@page "/categories/{CategoryId:int}"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

@if (Category is null)
{
    <PageHeader Title="Category not found" />
}
else
{
    <PageHeader>
        <Breadcrumb><a href="/library">Library</a></Breadcrumb>
        <TitleContent>
            @if (Action == "edit")
            {
                <EditForm Model="this" OnValidSubmit="RenameCategory" FormName="renameCategory" class="inline-add" style="margin: 0;">
                    <InputText @bind-Value="EditName" placeholder="Category name…" />
                    <button type="submit" class="btn btn-primary">Save</button>
                    <a href="/categories/@CategoryId" class="btn btn-secondary">Cancel</a>
                </EditForm>
            }
            else
            {
                @Category.Name
            }
        </TitleContent>
        <Subtitle>@Category.Stories.Count @(Category.Stories.Count == 1 ? "story" : "stories")</Subtitle>
        <Actions>
            @if (Action != "edit")
            {
                <a href="/categories/@CategoryId?action=edit" class="btn btn-secondary">Rename</a>
                <a href="/categories/@CategoryId?action=delete" class="btn btn-danger">Delete</a>
            }
        </Actions>
    </PageHeader>

    @if (Action == "delete")
    {
        <div class="confirm-bar">
            @if (Category.Stories.Count > 0)
            {
                <span>This will also delete @Category.Stories.Count story(ies). Are you sure?</span>
            }
            else
            {
                <span>Delete this category?</span>
            }
            <EditForm Model="this" OnValidSubmit="DeleteCategory" FormName="deleteCategory" style="margin: 0;">
                <button type="submit" class="btn btn-danger">Yes, delete</button>
            </EditForm>
            <a href="/categories/@CategoryId" class="btn btn-secondary">Cancel</a>
        </div>
    }

    <div class="card" style="margin-bottom: var(--space-5);">
        <EditForm Model="this" OnValidSubmit="AddStory" FormName="addStory" class="inline-add">
            <InputText @bind-Value="NewStoryTitle" placeholder="New story title…" />
            <button type="submit" class="btn btn-primary">Add story</button>
        </EditForm>
    </div>

    @if (Category.Stories.Count == 0)
    {
        <EmptyState Title="No stories yet" Body="Add a story above, or start a draft from &ldquo;+ New Story&rdquo; and save it here." />
    }
    else
    {
        <div class="card-grid">
            @foreach (var story in Category.Stories.OrderBy(s => s.Title))
            {
                <StoryCard Story="story" CategoryName="@Category.Name" />
            }
        </div>
    }
}

@code {
    [Parameter]
    public int CategoryId { get; set; }

    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    private Category? Category { get; set; }

    [SupplyParameterFromForm]
    private string? NewStoryTitle { get; set; }

    [SupplyParameterFromForm]
    private string? EditName { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }
        await LoadCategory();

        if (Action == "edit" && Category is not null)
        {
            EditName ??= Category.Name;
        }
    }

    private async Task LoadCategory()
    {
        Category = await Db.Categories
            .Include(c => c.Stories)
                .ThenInclude(s => s.Versions)
            .FirstOrDefaultAsync(c => c.Id == CategoryId);
    }

    private async Task AddStory()
    {
        if (string.IsNullOrWhiteSpace(NewStoryTitle) || Category is null)
            return;

        Db.Stories.Add(new Story
        {
            CategoryId = Category.Id,
            Title = NewStoryTitle.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        NewStoryTitle = null;
        await LoadCategory();
    }

    private async Task RenameCategory()
    {
        if (string.IsNullOrWhiteSpace(EditName) || Category is null)
            return;

        Category.Name = EditName.Trim();
        await Db.SaveChangesAsync();
        Nav.NavigateTo($"/categories/{CategoryId}");
    }

    private async Task DeleteCategory()
    {
        if (Category is null)
            return;

        Db.Categories.Remove(Category);
        await Db.SaveChangesAsync();
        Nav.NavigateTo("/library");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — Category page has story grid + rename/delete actions**

Navigate to `/categories/{id}`. Expected: breadcrumb, Fraunces category name, rename/delete buttons, "+ Add story" input card, then a 2-column grid of story cards (or empty state). `?action=edit` inline renames, `?action=delete` shows the confirm-bar.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/CategoryPage.razor
git commit -m "Rework Category page as a story-card grid"
```

---

### Task 15: Rework Draft page into a writing-studio two-pane layout

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/DraftPage.razor`

- [ ] **Step 1: Replace `DraftPage.razor`**

```razor
@page "/stories/draft/{DraftId:int}"
@rendermode InteractiveServer
@inject DraftService Drafts
@inject OpenRouterService OpenRouter
@inject VaultService Vault
@inject NavigationManager Nav
@using Markdig
@implements IDisposable

@if (CurrentDraft is null)
{
    <PageHeader Title="Draft not found" />
}
else
{
    <PageHeader>
        <Breadcrumb><a href="/stories/new">New Story</a> &rsaquo; Draft</Breadcrumb>
        <Eyebrow>Draft &middot; @CurrentDraft.Messages.Count @(CurrentDraft.Messages.Count == 1 ? "turn" : "turns")</Eyebrow>
        <TitleContent>
            @if (IsEditingTitle)
            {
                <input type="text" class="field-input" @bind="EditTitle" @bind:event="oninput"
                       @onblur="SaveTitle" @onkeydown="OnTitleKeyDown"
                       style="font-family: inherit; font-size: inherit; background: transparent; border: 1px dashed var(--gilt); border-radius: var(--radius-sm); padding: 0.1rem 0.4rem; width: 100%;" />
            }
            else
            {
                <span @onclick="StartEditTitle" style="cursor: pointer;">@(CurrentDraft.Title ?? "Untitled")</span>
            }
        </TitleContent>
        <Actions>
            <a href="/stories/draft/@DraftId/save" class="btn btn-secondary">Save to Library</a>
            @if (!ConfirmingDelete)
            {
                <button class="btn btn-danger" @onclick="() => ConfirmingDelete = true">Delete</button>
            }
            else
            {
                <button class="btn btn-danger" @onclick="DeleteDraft">Confirm delete</button>
                <button class="btn btn-secondary" @onclick="() => ConfirmingDelete = false">Cancel</button>
            }
        </Actions>
    </PageHeader>

    <div class="draft-studio">
        <div class="draft-conversation">
            @foreach (var message in CurrentDraft.Messages)
            {
                var isLast = message == CurrentDraft.Messages.Last();
                <div class="msg @(message.Role == MessageRole.Prompt ? "prompt" : "response")">
                    <div class="msg-role">@message.Role</div>
                    @if (EditingMessageId == message.Id)
                    {
                        <div class="msg-editing">
                            <textarea @bind="EditingContent" @bind:event="oninput" rows="4"></textarea>
                            <div class="msg-actions">
                                @if (message.Role == MessageRole.Prompt)
                                {
                                    <button class="btn btn-accent btn-sm" @onclick="() => SaveEditAndResubmit(message)">Save &amp; Resubmit</button>
                                }
                                else
                                {
                                    <button class="btn btn-primary btn-sm" @onclick="() => SaveEdit(message)">Save</button>
                                }
                                <button class="btn btn-ghost btn-sm" @onclick="CancelEdit">Cancel</button>
                            </div>
                        </div>
                    }
                    else
                    {
                        <div class="msg-content">@(RenderMarkdown(message.Content))</div>
                        @if (!IsStreaming)
                        {
                            <div class="msg-actions">
                                <button class="btn btn-ghost btn-sm" @onclick="() => StartEditMessage(message)">Edit</button>
                                @if (isLast && message.Role == MessageRole.Response)
                                {
                                    <button class="btn btn-ghost btn-sm" @onclick="RegenerateLastResponse">Regenerate</button>
                                }
                                <button class="btn btn-ghost btn-sm" @onclick="() => DeleteMessage(message)">Delete</button>
                            </div>
                        }
                    }
                </div>
            }

            @if (StreamingContent.Length > 0)
            {
                <div class="msg response">
                    <div class="msg-role">Response</div>
                    <div class="msg-content">@(RenderMarkdown(StreamingContent.ToString()))</div>
                </div>
            }

            <div class="draft-input">
                <textarea @bind="UserInput" @bind:event="oninput" placeholder="Continue the story, or ask for a new direction…"
                          rows="3" disabled="@IsStreaming"
                          @onkeydown="OnInputKeyDown"></textarea>
                @if (IsStreaming)
                {
                    <button class="btn btn-danger" @onclick="CancelGeneration">Stop</button>
                }
                else
                {
                    <button class="btn btn-accent" @onclick="SendMessage" disabled="@(string.IsNullOrWhiteSpace(UserInput))">&#x2726; Generate</button>
                }
            </div>

            @if (ErrorMessage is not null)
            {
                <p class="error-message">@ErrorMessage</p>
            }
        </div>

        <StorytellerPanel Storyteller="CurrentDraft.Storyteller" />
    </div>
}

@code {
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private MarkupString RenderMarkdown(string content) =>
        new(Markdown.ToHtml(content, Pipeline));

    [Parameter]
    public int DraftId { get; set; }

    private Draft? CurrentDraft { get; set; }
    private string? UserInput { get; set; }
    private bool IsStreaming { get; set; }
    private System.Text.StringBuilder StreamingContent { get; set; } = new();
    private string? ErrorMessage { get; set; }
    private bool IsEditingTitle { get; set; }
    private string? EditTitle { get; set; }
    private bool ConfirmingDelete { get; set; }
    private int? EditingMessageId { get; set; }
    private string? EditingContent { get; set; }
    private CancellationTokenSource? Cts;

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }
        CurrentDraft = await Drafts.GetDraftAsync(DraftId);
    }

    private void StartEditTitle()
    {
        EditTitle = CurrentDraft?.Title;
        IsEditingTitle = true;
    }

    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || CurrentDraft is null || IsStreaming)
            return;

        var prompt = UserInput.Trim();
        UserInput = null;

        await Drafts.AddMessageAsync(CurrentDraft.Id, MessageRole.Prompt, prompt);
        CurrentDraft = await Drafts.GetDraftAsync(DraftId);

        await SubmitToLLM();
    }

    private async Task OnInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await SendMessage();
        }
    }

    private async Task SaveTitle()
    {
        if (CurrentDraft is not null && !string.IsNullOrWhiteSpace(EditTitle))
        {
            await Drafts.UpdateDraftTitleAsync(CurrentDraft.Id, EditTitle.Trim());
            CurrentDraft.Title = EditTitle.Trim();
        }
        IsEditingTitle = false;
    }

    private async Task OnTitleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await SaveTitle();
        }
        else if (e.Key == "Escape")
        {
            IsEditingTitle = false;
        }
    }

    private async Task DeleteDraft()
    {
        if (CurrentDraft is null) return;
        await Drafts.DeleteDraftAsync(CurrentDraft.Id);
        Nav.NavigateTo("/stories/new");
    }

    private void StartEditMessage(DraftMessage msg)
    {
        EditingMessageId = msg.Id;
        EditingContent = msg.Content;
    }

    private void CancelEdit()
    {
        EditingMessageId = null;
        EditingContent = null;
    }

    private async Task SaveEdit(DraftMessage msg)
    {
        if (CurrentDraft is null || string.IsNullOrWhiteSpace(EditingContent)) return;

        await Drafts.UpdateMessageContentAsync(msg.Id, EditingContent.Trim());
        EditingMessageId = null;
        EditingContent = null;
        CurrentDraft = await Drafts.GetDraftAsync(DraftId);
    }

    private async Task SaveEditAndResubmit(DraftMessage msg)
    {
        if (CurrentDraft is null || string.IsNullOrWhiteSpace(EditingContent)) return;

        await Drafts.UpdateMessageAndDeleteSubsequentAsync(msg.Id, EditingContent.Trim());
        EditingMessageId = null;
        EditingContent = null;
        CurrentDraft = await Drafts.GetDraftAsync(DraftId);

        await SubmitToLLM();
    }

    private async Task RegenerateLastResponse()
    {
        if (CurrentDraft is null || IsStreaming) return;

        var removed = await Drafts.DeleteLastResponseAsync(CurrentDraft.Id);
        if (!removed) return;

        CurrentDraft = await Drafts.GetDraftAsync(DraftId);
        await SubmitToLLM();
    }

    private async Task DeleteMessage(DraftMessage msg)
    {
        if (CurrentDraft is null) return;

        await Drafts.DeleteMessageAndSubsequentAsync(msg.Id);
        CurrentDraft = await Drafts.GetDraftAsync(DraftId);
    }

    private async Task SubmitToLLM()
    {
        if (CurrentDraft is null || !CurrentDraft.Messages.Any()) return;

        var draftId = CurrentDraft.Id;
        IsStreaming = true;
        StreamingContent.Clear();
        ErrorMessage = null;
        Cts = new CancellationTokenSource();
        StateHasChanged();

        try
        {
            var storyteller = CurrentDraft.Storyteller;
            await foreach (var chunk in OpenRouter.ChatStreamAsync(
                storyteller.ModelName,
                storyteller.Prompt,
                CurrentDraft.Messages.ToList(),
                storyteller.Temperature,
                storyteller.TopP,
                storyteller.MaxTokens,
                storyteller.MinP,
                storyteller.TopK,
                storyteller.TopA,
                Cts.Token))
            {
                StreamingContent.Append(chunk);
                StateHasChanged();
            }

            var fullResponse = StreamingContent.ToString();
            await Drafts.AddMessageAsync(draftId, MessageRole.Response, fullResponse);
            CurrentDraft = await Drafts.GetDraftAsync(DraftId);
        }
        catch (OperationCanceledException)
        {
            if (StreamingContent.Length > 0)
            {
                try
                {
                    await Drafts.AddMessageAsync(draftId, MessageRole.Response, StreamingContent.ToString());
                    CurrentDraft = await Drafts.GetDraftAsync(DraftId);
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Error saving partial response: {ex.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            Cts?.Dispose();
            Cts = null;
            StreamingContent.Clear();
            IsStreaming = false;
            StateHasChanged();
        }
    }

    private void CancelGeneration()
    {
        Cts?.Cancel();
    }

    public void Dispose()
    {
        Cts?.Cancel();
        Cts?.Dispose();
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — Draft page shows two-pane writing studio**

Navigate to an existing draft at `/stories/draft/{id}`. Expected (at viewport ≥1024px):
- Page header with breadcrumb, "Draft · N turns" eyebrow, Fraunces title (click to edit), Save/Delete actions
- Left pane: conversation with parchment prompt bubbles and paper response bubbles (Fraunces prose)
- Right pane: StorytellerPanel showing active storyteller, model chip, sampling params, prompt excerpt
- Sticky input with gilt "✦ Generate" button at the bottom of the left pane
- Below 1024px: the two panes stack vertically

Test: type a prompt, press Generate, confirm streaming works and the final message persists. Then: edit a message, regenerate, delete a message — all should continue to work.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/DraftPage.razor
git commit -m "Rework Draft page as a two-pane writing studio with StorytellerPanel"
```

---

### Task 16: Rework Home as a dashboard (unlocked) and centered hero (locked)

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Home.razor`

- [ ] **Step 1: Replace `Home.razor`**

```razor
@page "/"
@inject VaultService Vault
@inject FabulisDbContext Db
@inject DraftService Drafts
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore
@layout Fabulis.Server.Components.Layout.MainLayout

@if (!Vault.IsUnlocked)
{
    <div style="min-height: 60vh; display: flex; align-items: center; justify-content: center;">
        <div style="text-align: center; max-width: 440px;">
            <Flourish />
            <h1 style="font-size: 2.25rem; margin-bottom: var(--space-3);">Fabulis</h1>
            <p class="meta" style="margin-bottom: var(--space-5);">Your vault is locked.</p>
            <a href="/unlock" class="btn btn-accent">&#x1F512; Unlock your vault</a>
        </div>
    </div>
}
else
{
    <PageHeader Title="What shall we write today?">
        <Eyebrow>Welcome back</Eyebrow>
    </PageHeader>

    <div class="dashboard-hero">
        <ContinueWritingCard Draft="LatestDraft" />
        <div class="card stats-card">
            <p class="label" style="margin-bottom: var(--space-2);">Your library</p>
            <div class="stat-row"><span>Stories</span><span class="stat-value">@StoryCount</span></div>
            <div class="stat-row"><span>Drafts</span><span class="stat-value">@DraftCount</span></div>
            <div class="stat-row"><span>Storytellers</span><span class="stat-value">@StorytellerCount</span></div>
        </div>
    </div>

    @if (RecentStories.Count > 0)
    {
        <p class="label" style="margin-bottom: var(--space-3);">Recently written</p>
        <div class="recent-grid">
            @foreach (var story in RecentStories)
            {
                <a href="/stories/@story.Id" class="mini-card">
                    <h3 class="title">@story.Title</h3>
                    <p class="meta">@(story.Category?.Name ?? "Story") &middot; @FormatDate(story.LatestVersionAt ?? story.CreatedAt)</p>
                </a>
            }
        </div>
    }
}

@code {
    private Draft? LatestDraft { get; set; }
    private int StoryCount { get; set; }
    private int DraftCount { get; set; }
    private int StorytellerCount { get; set; }

    private record RecentStory(int Id, string Title, DateTime CreatedAt, Category? Category, DateTime? LatestVersionAt);

    private List<RecentStory> RecentStories { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked) return;

        var drafts = await Drafts.GetDraftsAsync();
        LatestDraft = drafts.OrderByDescending(d => d.UpdatedAt).FirstOrDefault();
        DraftCount = drafts.Count;

        StoryCount = await Db.Stories.CountAsync();
        StorytellerCount = await Db.Storytellers.CountAsync();

        var stories = await Db.Stories
            .Include(s => s.Category)
            .Include(s => s.Versions)
            .ToListAsync();

        RecentStories = stories
            .Select(s => new RecentStory(
                s.Id,
                s.Title,
                s.CreatedAt,
                s.Category,
                s.Versions.Count > 0 ? s.Versions.Max(v => v.CreatedAt) : (DateTime?)null))
            .OrderByDescending(r => r.LatestVersionAt ?? r.CreatedAt)
            .Take(3)
            .ToList();
    }

    private static string FormatDate(DateTime dt)
    {
        var delta = DateTime.UtcNow - dt;
        if (delta.TotalMinutes < 60) return "just now";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return dt.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — dashboard when unlocked, centered hero when locked**

Navigate to `/` while unlocked. Expected: eyebrow + serif greeting, hero `ContinueWritingCard` with gilt left border (showing most recent draft or fallback CTA), stats panel to the right, and a "Recently written" row with up to 3 mini-cards.

Lock the vault (Settings → Lock) and visit `/`. Expected: centered flourish, "Fabulis" serif title, "Your vault is locked" meta, gilt "Unlock your vault" button. Sidebar still visible but only shows the Unlock link.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Home.razor
git commit -m "Rework Home into a dashboard with ContinueWritingCard and stats"
```

---

### Task 17: Rework Version page as reading mode with CenteredLayout

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/VersionPage.razor`

- [ ] **Step 1: Replace `VersionPage.razor`**

```razor
@page "/versions/{VersionId:int}"
@layout Fabulis.Server.Components.Layout.CenteredLayout
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore
@using Markdig

@if (Version is null)
{
    <p>Version not found.</p>
}
else
{
    <div class="reading-page">
        <header class="reading-header">
            <p class="breadcrumb">
                <a href="/categories/@Version.Story.CategoryId">@Version.Story.Category.Name</a>
                &rsaquo;
                <a href="/stories/@Version.Story.Id">@Version.Story.Title</a>
            </p>
            <p class="eyebrow">Version @Version.VersionNumber &middot; @Version.ModelName</p>
            <h1 class="reading-title">@Version.Story.Title</h1>
            <p class="reading-subtitle">@Version.CreatedAt.ToLocalTime().ToString("MMMM d, yyyy")</p>
        </header>

        <Flourish />

        <div class="reading-prose">
            @foreach (var message in Version.Messages.OrderBy(m => m.SortOrder))
            {
                @if (message.Role == MessageRole.Response)
                {
                    <div class="msg-content">@(RenderMarkdown(message.Content))</div>
                }
                else
                {
                    <div class="card card-muted" style="margin: var(--space-4) 0; font-family: var(--font-sans); font-size: 0.9rem;">
                        <p class="label" style="margin-bottom: var(--space-2);">Prompt</p>
                        <div>@(RenderMarkdown(message.Content))</div>
                    </div>
                }
            }
        </div>

        <Flourish />

        <p style="text-align: center;">
            <a href="/stories/@Version.Story.Id" class="btn btn-secondary">&larr; Back to story</a>
        </p>
    </div>
}

@code {
    private static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private MarkupString RenderMarkdown(string content) =>
        new(Markdig.Markdown.ToHtml(content, Pipeline));

    [Parameter]
    public int VersionId { get; set; }

    private StoryVersion? Version { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }
        Version = await Db.StoryVersions
            .Include(v => v.Story)
                .ThenInclude(s => s.Category)
            .Include(v => v.Messages)
            .FirstOrDefaultAsync(v => v.Id == VersionId);
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — Version page is reading mode**

Navigate to a `/versions/{id}` URL. Expected: no sidebar, centered 640px column, centered breadcrumb + eyebrow + Fraunces title + subtitle date, flourish divider, prose in Fraunces 17/1.75 with drop-cap on the first paragraph, flourish divider at end, "Back to story" button.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/VersionPage.razor
git commit -m "Rework Version page as reading mode with CenteredLayout"
```

---

### Task 18: Rework Story page with versions timeline

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/StoryPage.razor`

- [ ] **Step 1: Replace `StoryPage.razor`**

```razor
@page "/stories/{StoryId:int}"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

@if (Story is null)
{
    <PageHeader Title="Story not found" />
}
else
{
    <PageHeader Title="@Story.Title">
        <Breadcrumb><a href="/library">Library</a> &rsaquo; <a href="/categories/@Story.CategoryId">@Story.Category.Name</a></Breadcrumb>
        <Subtitle>
            <a href="/categories/@Story.CategoryId" class="chip">@Story.Category.Name</a>
            <span style="margin-left: var(--space-2);">@(Story.Versions.Count == 1 ? "1 version" : $"{Story.Versions.Count} versions")</span>
        </Subtitle>
        <Actions>
            <a href="/stories/new" class="btn btn-accent">&#x2726; New draft</a>
        </Actions>
    </PageHeader>

    @if (Story.Versions.Count == 0)
    {
        <EmptyState Title="No versions yet" Body="Start a draft and save it here to create the first version." />
    }
    else
    {
        <div class="version-timeline">
            @foreach (var version in Story.Versions.OrderByDescending(v => v.VersionNumber))
            {
                <div class="version-item">
                    <div>
                        <div class="eyebrow" style="color: var(--gilt-deep);">Version @version.VersionNumber</div>
                        <div class="meta">@version.ModelName &middot; @version.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</div>
                    </div>
                    <a href="/versions/@version.Id" class="btn btn-secondary btn-sm">Read</a>
                </div>
            }
        </div>
    }
}

@code {
    [Parameter]
    public int StoryId { get; set; }

    private Story? Story { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }
        Story = await Db.Stories
            .Include(s => s.Category)
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Id == StoryId);
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — Story page has title, chip, timeline**

Navigate to `/stories/{id}`. Expected: breadcrumb + Fraunces title + category chip + version count, "+ New draft" action. Versions list renders as a timeline with gilt dots, version number + model + date, "Read" button. Empty state when no versions.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/StoryPage.razor
git commit -m "Rework Story page with category chip and versions timeline"
```

---

### Task 19: Rework Storytellers list page

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Storytellers.razor`

- [ ] **Step 1: Replace `Storytellers.razor`**

```razor
@page "/storytellers"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

<PageHeader Title="Storytellers">
    <Subtitle>@StorytellerList.Count @(StorytellerList.Count == 1 ? "persona" : "personas")</Subtitle>
    <Actions>
        <a href="/storytellers/new" class="btn btn-primary">+ New storyteller</a>
    </Actions>
</PageHeader>

@if (StorytellerList.Count == 0)
{
    <EmptyState Title="No storytellers yet" Body="Create a persona to define a voice, a prompt, and sampling parameters for story generation." />
}
else
{
    <div class="card-grid">
        @foreach (var s in StorytellerList)
        {
            <a href="/storytellers/@s.Id" class="story-card">
                <div class="story-eyebrow">Storyteller</div>
                <h3 class="story-title">@s.Name</h3>
                <p class="story-excerpt">@TruncatePrompt(s.Prompt)</p>
                <div class="story-meta">
                    <span class="chip chip-gilt">@s.ModelName</span>
                    <span>temp @s.Temperature.ToString("0.00")</span>
                </div>
            </a>
        }
    </div>
}

@code {
    private List<Storyteller> StorytellerList { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        StorytellerList = await Db.Storytellers
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    private static string TruncatePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        var trimmed = prompt.Trim();
        return trimmed.Length > 160 ? trimmed[..160].TrimEnd() + "…" : trimmed;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Storytellers.razor
git commit -m "Rework Storytellers list as profile cards"
```

---

### Task 20: Rework Storyteller detail page with grouped panels

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/StorytellerPage.razor`

- [ ] **Step 1: Replace `StorytellerPage.razor`**

```razor
@page "/storytellers/new"
@page "/storytellers/{Id:int}"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject OpenRouterService OpenRouter
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore
@rendermode InteractiveServer

<PageHeader>
    <Breadcrumb><a href="/storytellers">Storytellers</a></Breadcrumb>
    <TitleContent>@(IsNew ? "New storyteller" : (Name ?? "Storyteller"))</TitleContent>
</PageHeader>

@if (!IsNew && Storyteller is null)
{
    <p>Storyteller not found.</p>
}
else
{
    @if (!IsNew && Action == "delete")
    {
        <div class="confirm-bar">
            <span>Delete this storyteller?</span>
            <EditForm Model="this" OnValidSubmit="Delete" FormName="deleteStoryteller" style="margin: 0;">
                <button type="submit" class="btn btn-danger">Yes, delete</button>
            </EditForm>
            <a href="/storytellers/@Id" class="btn btn-secondary">Cancel</a>
        </div>
    }

    <EditForm Model="this" OnValidSubmit="Save" FormName="saveStoryteller">
        <div class="detail-panels">

            <section class="detail-panel card">
                <h2>Identity</h2>
                <div class="field">
                    <label for="name">Name</label>
                    <InputText id="name" @bind-Value="Name" placeholder="e.g. Adventure Narrator" />
                </div>
            </section>

            <section class="detail-panel card">
                <h2>Prompt</h2>
                <div class="field">
                    <label for="prompt">System prompt</label>
                    <InputTextArea id="prompt" @bind-Value="Prompt" rows="8" placeholder="You are a creative storyteller who..." />
                </div>
                <div class="card card-muted" style="border-left: 3px solid var(--gilt); margin-top: var(--space-3);">
                    <p class="label" style="margin-bottom: var(--space-2);">Prompt assistant</p>
                    <div class="field-row">
                        <div class="field" style="flex: 1;">
                            <input type="text" @bind="PromptDescription" @bind:event="oninput" placeholder="Describe your storyteller, e.g. 'a pirate who tells bedtime stories'" />
                        </div>
                        <button type="button" class="btn btn-accent" @onclick="GeneratePrompt" disabled="@IsGenerating">
                            @(IsGenerating ? "Generating…" : "Generate prompt")
                        </button>
                    </div>
                    @if (GenerateError is not null)
                    {
                        <p class="error-message">@GenerateError</p>
                    }
                </div>
            </section>

            <section class="detail-panel card">
                <h2>Model</h2>
                @if (ModelName is not null)
                {
                    <p class="meta" style="margin-bottom: var(--space-2);">Selected: <strong>@ModelName</strong></p>
                }
                <div class="model-picker">
                    <div class="model-picker-search">
                        <input type="text" @bind="ModelSearch" @bind:event="oninput" placeholder="Search models…" />
                        @if (Models.Count == 0 && !IsLoadingModels)
                        {
                            <button type="button" class="btn btn-secondary btn-sm" @onclick="LoadModels" disabled="@IsLoadingModels">
                                Load models
                            </button>
                        }
                    </div>

                    @if (IsLoadingModels)
                    {
                        <p class="field-hint">Loading models from OpenRouter…</p>
                    }
                    else if (ModelsError is not null)
                    {
                        <p class="error-message">@ModelsError</p>
                    }
                    else if (Models.Count > 0)
                    {
                        <div class="model-picker-list">
                            @foreach (var model in FilteredModels)
                            {
                                <div class="model-picker-item @(model.Id == ModelName ? "selected" : "")"
                                     @onclick="() => ModelName = model.Id">
                                    <span class="model-picker-id">@model.Id</span>
                                    <span class="model-picker-name">@model.Name</span>
                                </div>
                            }
                            @if (FilteredModels.Count == 0)
                            {
                                <p class="meta" style="padding: var(--space-2);">No models match your search.</p>
                            }
                        </div>
                    }
                </div>
            </section>

            <section class="detail-panel card">
                <h2>Sampling</h2>
                <div class="field">
                    <label for="temperature">Temperature</label>
                    <InputNumber id="temperature" @bind-Value="Temperature" step="0.1" min="0" max="2" />
                    <p class="field-hint">0.0 = deterministic, 2.0 = very creative</p>
                </div>
                <div class="field">
                    <label for="topp">Top P</label>
                    <InputNumber id="topp" @bind-Value="TopP" step="0.1" min="0" max="1" />
                    <p class="field-hint">Optional. Leave empty for provider default.</p>
                </div>
                <div class="field">
                    <label for="maxtokens">Max Tokens</label>
                    <InputNumber id="maxtokens" @bind-Value="MaxTokens" min="1" />
                    <p class="field-hint">Optional. Leave empty for provider default.</p>
                </div>
                <div class="field">
                    <label for="minp">Min P</label>
                    <InputNumber id="minp" @bind-Value="MinP" step="0.01" min="0" max="1" />
                    <p class="field-hint">Optional. Leave empty for provider default.</p>
                </div>
                <div class="field">
                    <label for="topk">Top K</label>
                    <InputNumber id="topk" @bind-Value="TopK" min="0" />
                    <p class="field-hint">Optional. Leave empty for provider default.</p>
                </div>
                <div class="field">
                    <label for="topa">Top A</label>
                    <InputNumber id="topa" @bind-Value="TopA" step="0.01" min="0" max="1" />
                    <p class="field-hint">Optional. Leave empty for provider default.</p>
                </div>
            </section>

            <div style="display: flex; gap: var(--space-2);">
                <button type="submit" class="btn btn-primary">@(IsNew ? "Create" : "Save")</button>
                @if (!IsNew)
                {
                    <a href="/storytellers/@Id?action=delete" class="btn btn-danger">Delete</a>
                }
                <a href="/storytellers" class="btn btn-secondary">Cancel</a>
            </div>
        </div>
    </EditForm>
}

@if (ErrorMessage is not null)
{
    <p class="error-message">@ErrorMessage</p>
}

@code {
    [Parameter]
    public int Id { get; set; }

    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    private Storyteller? Storyteller { get; set; }
    private bool IsNew => Id == 0;
    private string? ErrorMessage { get; set; }

    [SupplyParameterFromForm]
    private string? Name { get; set; }

    [SupplyParameterFromForm]
    private string? Prompt { get; set; }

    [SupplyParameterFromForm]
    private string? ModelName { get; set; }

    [SupplyParameterFromForm]
    private double Temperature { get; set; }

    [SupplyParameterFromForm]
    private double? TopP { get; set; }

    [SupplyParameterFromForm]
    private int? MaxTokens { get; set; }

    [SupplyParameterFromForm]
    private double? MinP { get; set; }

    [SupplyParameterFromForm]
    private int? TopK { get; set; }

    [SupplyParameterFromForm]
    private double? TopA { get; set; }

    private string? PromptDescription { get; set; }
    private bool IsGenerating { get; set; }
    private string? GenerateError { get; set; }

    private List<ModelInfo> Models { get; set; } = [];
    private bool IsLoadingModels { get; set; }
    private string? ModelsError { get; set; }
    private string? ModelSearch { get; set; }

    private List<ModelInfo> FilteredModels =>
        string.IsNullOrWhiteSpace(ModelSearch)
            ? Models
            : Models.Where(m =>
                m.Id.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        if (IsNew)
        {
            Temperature = 0.7;
        }
        else
        {
            Storyteller = await Db.Storytellers.FindAsync(Id);
            if (Storyteller is not null)
            {
                Name ??= Storyteller.Name;
                Prompt ??= Storyteller.Prompt;
                ModelName ??= Storyteller.ModelName;
                Temperature = Storyteller.Temperature;
                TopP ??= Storyteller.TopP;
                MaxTokens ??= Storyteller.MaxTokens;
                MinP ??= Storyteller.MinP;
                TopK ??= Storyteller.TopK;
                TopA ??= Storyteller.TopA;
            }
        }
    }

    private async Task GeneratePrompt()
    {
        if (string.IsNullOrWhiteSpace(PromptDescription))
            return;

        IsGenerating = true;
        GenerateError = null;

        try
        {
            var model = await OpenRouter.GetSettingAsync("AssistantModel") ?? "anthropic/claude-sonnet-4";

            var systemPrompt = """
                You are an expert at crafting system prompts for AI storytelling personas.
                Given a description of a storyteller, write a detailed, effective system prompt
                that captures their personality, speaking style, and storytelling approach.
                The prompt should instruct the AI to stay in character and tell engaging stories.
                Output only the system prompt text — no explanations, preamble, or markdown formatting.
                """;

            Prompt = await OpenRouter.ChatAsync(model, systemPrompt, PromptDescription);
        }
        catch (Exception ex)
        {
            GenerateError = ex.Message;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task LoadModels()
    {
        IsLoadingModels = true;
        ModelsError = null;

        try
        {
            Models = await OpenRouter.GetModelsAsync();
        }
        catch (Exception ex)
        {
            ModelsError = ex.Message;
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Prompt) || string.IsNullOrWhiteSpace(ModelName))
        {
            ErrorMessage = "Name, prompt, and model are required.";
            return;
        }

        if (IsNew)
        {
            Db.Storytellers.Add(new Storyteller
            {
                Name = Name.Trim(),
                Prompt = Prompt.Trim(),
                ModelName = ModelName.Trim(),
                Temperature = Temperature,
                TopP = TopP,
                MaxTokens = MaxTokens,
                MinP = MinP,
                TopK = TopK,
                TopA = TopA,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            if (Storyteller is null) return;

            Storyteller.Name = Name.Trim();
            Storyteller.Prompt = Prompt.Trim();
            Storyteller.ModelName = ModelName.Trim();
            Storyteller.Temperature = Temperature;
            Storyteller.TopP = TopP;
            Storyteller.MaxTokens = MaxTokens;
            Storyteller.MinP = MinP;
            Storyteller.TopK = TopK;
            Storyteller.TopA = TopA;
        }

        await Db.SaveChangesAsync();
        Nav.NavigateTo("/storytellers");
    }

    private async Task Delete()
    {
        if (Storyteller is null) return;

        Db.Storytellers.Remove(Storyteller);
        await Db.SaveChangesAsync();
        Nav.NavigateTo("/storytellers");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — storyteller form grouped into panels**

Create a new storyteller via `/storytellers/new`. Expected: breadcrumb + Fraunces title, four panels (Identity, Prompt + assistant sub-card with gilt left border, Model with searchable picker, Sampling with all six fields). Save/Delete/Cancel actions at bottom. Save creates the record and navigates back.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/StorytellerPage.razor
git commit -m "Rework storyteller detail form with grouped panels"
```

---

### Task 21: Rework Settings page

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Settings.razor`

- [ ] **Step 1: Replace `Settings.razor`**

```razor
@page "/settings"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject OpenRouterService OpenRouter
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore
@rendermode InteractiveServer

<PageHeader Title="Settings" />

<section class="settings-section">
    <h2>API</h2>

    <div class="settings-item">
        <div>
            <div class="settings-label">OpenRouter API key</div>
            <p class="settings-description">Required for story generation. Stored encrypted in the database.</p>
        </div>
        <EditForm Model="this" OnValidSubmit="SaveApiKey" FormName="saveApiKey" style="display: flex; gap: var(--space-2); margin: 0;">
            <InputText type="password" @bind-Value="ApiKey" placeholder="@ApiKeyPlaceholder" />
            <button type="submit" class="btn btn-primary">Save</button>
        </EditForm>
    </div>

    @if (ApiKeySaved)
    {
        <p style="color: var(--gilt-deep); font-size: 0.85rem;">API key saved.</p>
    }

    <div class="settings-item-vertical">
        <div>
            <div class="settings-label">Assistant model</div>
            <p class="settings-description">Model used for the prompt-writing assistant on the Storytellers page.</p>
        </div>

        @if (CurrentAssistantModel is not null)
        {
            <p class="meta" style="margin-top: var(--space-2);">Current: <strong>@CurrentAssistantModel</strong></p>
        }

        <div class="model-picker">
            <div class="model-picker-search">
                <input type="text" @bind="ModelSearch" @bind:event="oninput" placeholder="Search models…" />
                @if (Models.Count == 0 && !IsLoadingModels)
                {
                    <button type="button" class="btn btn-secondary btn-sm" @onclick="LoadModels" disabled="@IsLoadingModels">
                        Load models
                    </button>
                }
            </div>

            @if (IsLoadingModels)
            {
                <p class="field-hint">Loading models from OpenRouter…</p>
            }
            else if (ModelsError is not null)
            {
                <p class="error-message">@ModelsError</p>
            }
            else if (Models.Count > 0)
            {
                <div class="model-picker-list">
                    @foreach (var model in FilteredModels)
                    {
                        <div class="model-picker-item @(model.Id == SelectedModelId ? "selected" : "")"
                             @onclick="() => SelectModel(model.Id)">
                            <span class="model-picker-id">@model.Id</span>
                            <span class="model-picker-name">@model.Name</span>
                        </div>
                    }
                    @if (FilteredModels.Count == 0)
                    {
                        <p class="meta" style="padding: var(--space-2);">No models match your search.</p>
                    }
                </div>
            }
        </div>

        @if (AssistantModelSaved)
        {
            <p style="color: var(--gilt-deep); font-size: 0.85rem;">Assistant model saved.</p>
        }
    </div>
</section>

<section class="settings-section">
    <h2>Library</h2>

    @if (ShowClearConfirm)
    {
        <div class="confirm-bar">
            <span>This will permanently delete all categories, stories, and messages. Are you sure?</span>
            <EditForm Model="this" OnValidSubmit="ClearLibrary" FormName="clearLibrary" style="margin: 0;">
                <button type="submit" class="btn btn-danger">Yes, clear everything</button>
            </EditForm>
            <a href="/settings" class="btn btn-secondary">Cancel</a>
        </div>
    }
    else
    {
        <div class="settings-item">
            <div>
                <div class="settings-label">Clear library</div>
                <p class="settings-description">Delete all categories, stories, and messages.</p>
            </div>
            <a href="/settings?action=clear" class="btn btn-danger">Clear library</a>
        </div>
    }
</section>

<section class="settings-section">
    <h2>Security</h2>

    <div class="settings-item">
        <div>
            <div class="settings-label">Lock vault</div>
            <p class="settings-description">Lock the database and require the password to be re-entered.</p>
        </div>
        <EditForm Model="this" OnValidSubmit="LockVault" FormName="lockVault" style="margin: 0;">
            <button type="submit" class="btn btn-secondary">Lock</button>
        </EditForm>
    </div>
</section>

@code {
    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    private bool ShowClearConfirm => Action == "clear";

    [SupplyParameterFromForm]
    private string? ApiKey { get; set; }

    private string ApiKeyPlaceholder { get; set; } = "sk-or-...";
    private bool ApiKeySaved { get; set; }

    private string? CurrentAssistantModel { get; set; }
    private bool AssistantModelSaved { get; set; }

    private List<ModelInfo> Models { get; set; } = [];
    private bool IsLoadingModels { get; set; }
    private string? ModelsError { get; set; }
    private string? ModelSearch { get; set; }
    private string? SelectedModelId { get; set; }

    private List<ModelInfo> FilteredModels =>
        string.IsNullOrWhiteSpace(ModelSearch)
            ? Models
            : Models.Where(m =>
                m.Id.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase) ||
                m.Name.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        var existingKey = await Db.AppSettings.FindAsync("OpenRouterApiKey");
        if (existingKey is not null)
        {
            ApiKeyPlaceholder = "••••••••  (key is set)";
        }

        var existingModel = await Db.AppSettings.FindAsync("AssistantModel");
        if (existingModel is not null)
        {
            CurrentAssistantModel = existingModel.Value;
        }
    }

    private async Task LoadModels()
    {
        IsLoadingModels = true;
        ModelsError = null;

        try
        {
            Models = await OpenRouter.GetModelsAsync();
        }
        catch (Exception ex)
        {
            ModelsError = ex.Message;
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    private async Task SelectModel(string modelId)
    {
        SelectedModelId = modelId;

        var existing = await Db.AppSettings.FindAsync("AssistantModel");
        if (existing is not null)
        {
            existing.Value = modelId;
        }
        else
        {
            Db.AppSettings.Add(new AppSetting { Key = "AssistantModel", Value = modelId });
        }

        await Db.SaveChangesAsync();
        CurrentAssistantModel = modelId;
        AssistantModelSaved = true;
    }

    private async Task ClearLibrary()
    {
        var categories = await Db.Categories.ToListAsync();
        Db.Categories.RemoveRange(categories);
        await Db.SaveChangesAsync();
        Nav.NavigateTo("/settings");
    }

    private async Task SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return;

        var existing = await Db.AppSettings.FindAsync("OpenRouterApiKey");
        if (existing is not null)
        {
            existing.Value = ApiKey.Trim();
        }
        else
        {
            Db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = ApiKey.Trim() });
        }

        await Db.SaveChangesAsync();
        ApiKey = null;
        ApiKeyPlaceholder = "••••••••  (key is set)";
        ApiKeySaved = true;
    }

    private void LockVault()
    {
        Vault.Lock();
        Nav.NavigateTo("/unlock");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Settings.razor
git commit -m "Rework Settings page with grouped sections and typed items"
```

---

### Task 22: Rework NewStory page

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/NewStory.razor`

- [ ] **Step 1: Replace `NewStory.razor`**

```razor
@page "/stories/new"
@rendermode InteractiveServer
@inject DraftService Drafts
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

<PageHeader Title="New Story">
    <Subtitle>Pick a storyteller and begin a draft.</Subtitle>
</PageHeader>

<div class="card" style="max-width: 640px;">
    @if (StorytellerList.Count == 0)
    {
        <EmptyState Title="No storytellers yet">
            <ChildContent>
                <a href="/storytellers/new" class="btn btn-accent">Create your first storyteller</a>
            </ChildContent>
        </EmptyState>
    }
    else
    {
        <div class="field">
            <label for="storyteller">Storyteller</label>
            <select id="storyteller" @bind="SelectedStorytellerId">
                <option value="0">Select a storyteller…</option>
                @foreach (var s in StorytellerList)
                {
                    <option value="@s.Id">@s.Name (@s.ModelName)</option>
                }
            </select>
        </div>
        <button class="btn btn-accent" @onclick="StartNewDraft" disabled="@(SelectedStorytellerId == 0)">
            &#x2726; Start draft
        </button>
    }
</div>

@if (DraftList.Count > 0)
{
    <section style="margin-top: var(--space-6);">
        <h2 style="font-size: 1.1rem; margin-bottom: var(--space-3);">In progress</h2>
        <div class="card-grid">
            @foreach (var draft in DraftList)
            {
                <a href="/stories/draft/@draft.Id" class="story-card">
                    <div class="story-eyebrow">Draft &middot; @draft.Messages.Count @(draft.Messages.Count == 1 ? "turn" : "turns")</div>
                    <h3 class="story-title">@(draft.Title ?? "Untitled")</h3>
                    <div class="story-meta">
                        <span>@draft.Storyteller.Name</span>
                        <span>@draft.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")</span>
                    </div>
                </a>
            }
        </div>
    </section>
}

@code {
    private List<Storyteller> StorytellerList { get; set; } = [];
    private List<Draft> DraftList { get; set; } = [];
    private int SelectedStorytellerId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        StorytellerList = await Db.Storytellers.OrderBy(s => s.Name).ToListAsync();
        DraftList = await Drafts.GetDraftsAsync();
    }

    private async Task StartNewDraft()
    {
        if (SelectedStorytellerId == 0) return;
        var draft = await Drafts.CreateDraftAsync(SelectedStorytellerId);
        Nav.NavigateTo($"/stories/draft/{draft.Id}");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/NewStory.razor
git commit -m "Rework NewStory page with card-form and draft grid"
```

---

### Task 23: Rework Unlock page with flourish and gilt CTA

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/Unlock.razor`

- [ ] **Step 1: Replace `Unlock.razor`**

```razor
@page "/unlock"
@layout Fabulis.Server.Components.Layout.CenteredLayout
@using Microsoft.AspNetCore.Components.Routing
@inject VaultService Vault
@inject IServiceProvider Services
@inject NavigationManager Nav

<div style="text-align: center; margin-top: var(--space-6);">
    <Flourish />
    <h1 style="font-size: 2.25rem; margin-bottom: var(--space-2);">Fabulis</h1>
    <p class="meta" style="margin-bottom: var(--space-5);">Open your story vault.</p>
</div>

@if (ErrorMessage is not null)
{
    <p class="error-message" style="text-align: center;">@ErrorMessage</p>
}

<div class="card" style="max-width: 420px; margin: 0 auto;">
    <EditForm Model="this" OnValidSubmit="TryUnlock" FormName="unlock">
        <div class="field">
            <label for="password">Password</label>
            <InputText id="password" type="password" @bind-Value="Password" placeholder="Enter vault password…" />
        </div>
        <button type="submit" class="btn btn-accent" style="width: 100%;">&#x1F513; Open vault</button>
    </EditForm>
</div>

<p class="meta" style="text-align: center; margin-top: var(--space-4); max-width: 440px; margin-left: auto; margin-right: auto;">
    Enter a password to open your story vault. If this is your first time, the password you enter will be used to create and encrypt the vault.
</p>

@code {
    [SupplyParameterFromForm]
    private string? Password { get; set; }

    private string? ErrorMessage { get; set; }

    protected override void OnInitialized()
    {
        if (Vault.IsUnlocked)
            Nav.NavigateTo("/library");
    }

    private async Task TryUnlock()
    {
        if (string.IsNullOrWhiteSpace(Password))
            return;

        Vault.Unlock(Password);

        try
        {
            await using var scope = Services.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureSchemaUpdatedAsync();
            Nav.NavigateTo("/library");
        }
        catch (NavigationException)
        {
            throw;
        }
        catch
        {
            Vault.Lock();
            ErrorMessage = "Could not open the vault. Is the password correct?";
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Manual smoke — unlock page is centered with flourish**

Lock the vault (via Settings), navigate to `/unlock`. Expected: centered layout (no sidebar), gilt flourish at top, large Fraunces "Fabulis" title, lede, paper card with password field and full-width gilt "🔓 Open vault" button, helper text below. Submitting with correct password navigates to library.

Stop the server.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/Unlock.razor
git commit -m "Rework Unlock page with flourish and gilt CTA"
```

---

### Task 24: Rework SaveDraft, Import, Export pages

Three small form pages that just need to use the new primitives.

**Files:**
- Modify: `src/Fabulis.Server/Components/Pages/SaveDraft.razor`
- Modify: `src/Fabulis.Server/Components/Pages/Import.razor`
- Modify: `src/Fabulis.Server/Components/Pages/Export.razor`

- [ ] **Step 1: Replace `SaveDraft.razor`**

```razor
@page "/stories/draft/{DraftId:int}/save"
@rendermode InteractiveServer
@inject DraftService Drafts
@inject FabulisDbContext Db
@inject VaultService Vault
@inject NavigationManager Nav
@using Microsoft.EntityFrameworkCore

@if (CurrentDraft is null)
{
    <PageHeader Title="Draft not found" />
}
else
{
    <PageHeader Title="Save to Library">
        <Breadcrumb>
            <a href="/stories/new">New Story</a>
            &rsaquo;
            <a href="/stories/draft/@DraftId">@(CurrentDraft.Title ?? "Untitled")</a>
            &rsaquo;
            Save
        </Breadcrumb>
    </PageHeader>

    <div class="card" style="max-width: 640px;">
        <div class="field">
            <label>Category</label>
            <select @bind="SelectedCategoryId">
                <option value="0">-- New Category --</option>
                @foreach (var cat in Categories)
                {
                    <option value="@cat.Id">@cat.Name</option>
                }
            </select>
        </div>

        @if (SelectedCategoryId == 0)
        {
            <div class="field">
                <label>New category name</label>
                <input type="text" @bind="NewCategoryName" placeholder="Category name…" />
            </div>
        }
        else
        {
            <div class="field">
                <label>Story</label>
                <select @bind="SelectedStoryId">
                    <option value="0">-- New Story --</option>
                    @foreach (var story in StoriesInCategory)
                    {
                        <option value="@story.Id">@story.Title (@story.Versions.Count version(s))</option>
                    }
                </select>
            </div>
        }

        @if (SelectedStoryId == 0)
        {
            <div class="field">
                <label>Story title</label>
                <input type="text" @bind="NewStoryTitle" placeholder="Story title…" />
            </div>
        }

        <div style="display: flex; gap: var(--space-2);">
            <button class="btn btn-primary" @onclick="Save" disabled="@IsSaving">
                @(IsSaving ? "Saving…" : "Save")
            </button>
            <a href="/stories/draft/@DraftId" class="btn btn-secondary">Cancel</a>
        </div>

        @if (ErrorMessage is not null)
        {
            <p class="error-message">@ErrorMessage</p>
        }
    </div>
}

@code {
    [Parameter]
    public int DraftId { get; set; }

    private Draft? CurrentDraft { get; set; }
    private List<Category> Categories { get; set; } = [];
    private List<Story> StoriesInCategory { get; set; } = [];
    private int _selectedCategoryId;
    private int SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            _selectedCategoryId = value;
            SelectedStoryId = 0;
            LoadStoriesInCategory();
        }
    }
    private int SelectedStoryId { get; set; }
    private string? NewCategoryName { get; set; }
    private string? NewStoryTitle { get; set; }
    private bool IsSaving { get; set; }
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Vault.IsUnlocked)
        {
            Nav.NavigateTo("/unlock");
            return;
        }

        CurrentDraft = await Drafts.GetDraftAsync(DraftId);
        Categories = await Db.Categories.OrderBy(c => c.Name).ToListAsync();
        NewStoryTitle = CurrentDraft?.Title;
    }

    private void LoadStoriesInCategory()
    {
        if (_selectedCategoryId == 0)
        {
            StoriesInCategory = [];
            return;
        }
        StoriesInCategory = Db.Stories
            .Include(s => s.Versions)
            .Where(s => s.CategoryId == _selectedCategoryId)
            .OrderBy(s => s.Title)
            .ToList();
    }

    private async Task Save()
    {
        if (CurrentDraft is null) return;
        ErrorMessage = null;
        IsSaving = true;

        try
        {
            int categoryId;
            if (SelectedCategoryId == 0)
            {
                if (string.IsNullOrWhiteSpace(NewCategoryName))
                {
                    ErrorMessage = "Please enter a category name.";
                    return;
                }
                var category = new Category
                {
                    Name = NewCategoryName.Trim(),
                    CreatedAt = DateTime.UtcNow
                };
                Db.Categories.Add(category);
                await Db.SaveChangesAsync();
                categoryId = category.Id;
            }
            else
            {
                categoryId = SelectedCategoryId;
            }

            int? storyId = SelectedStoryId > 0 ? SelectedStoryId : null;
            string? storyTitle = storyId.HasValue ? null : NewStoryTitle?.Trim();

            if (!storyId.HasValue && string.IsNullOrWhiteSpace(storyTitle))
            {
                ErrorMessage = "Please enter a story title.";
                return;
            }

            var version = await Drafts.SaveToLibraryAsync(CurrentDraft.Id, categoryId, storyId, storyTitle);
            Nav.NavigateTo($"/versions/{version.Id}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
```

- [ ] **Step 2: Replace `Import.razor`**

```razor
@page "/import"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject CategoryImportService Importer
@inject NavigationManager Nav
@rendermode InteractiveServer

<PageHeader Title="Import Category">
    <Subtitle>Read a folder of markdown files into a category.</Subtitle>
</PageHeader>

@if (ErrorMessage is not null)
{
    <p class="error-message">@ErrorMessage</p>
}

@if (Result is not null)
{
    <div class="success-message">
        <p><strong>Import complete.</strong></p>
        <ul>
            <li>Categories created: @Result.CategoriesCreated</li>
            <li>Stories created: @Result.StoriesCreated</li>
            <li>Versions created: @Result.VersionsCreated</li>
        </ul>
        <a href="/library" class="btn btn-secondary">Go to library</a>
    </div>
}

<div class="card" style="max-width: 640px;">
    <EditForm Model="this" OnValidSubmit="RunImport" FormName="import">
        <div class="field">
            <label for="import-path">Category directory</label>
            <InputText id="import-path" @bind-Value="DirectoryPath" placeholder="/path/to/category" />
            <p class="field-hint">
                Enter the full path to a category directory. The directory name becomes the category name;
                each subdirectory becomes a story; markdown files become versions.
            </p>
        </div>
        <button type="submit" class="btn btn-primary" disabled="@IsImporting">
            @(IsImporting ? "Importing…" : "Import")
        </button>
    </EditForm>
</div>

@code {
    [SupplyParameterFromForm]
    private string? DirectoryPath { get; set; }

    private ImportResult? Result { get; set; }
    private string? ErrorMessage { get; set; }
    private bool IsImporting { get; set; }

    protected override void OnInitialized()
    {
        if (!Vault.IsUnlocked)
            Nav.NavigateTo("/unlock");
    }

    private async Task RunImport()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        ErrorMessage = null;
        Result = null;
        IsImporting = true;

        try
        {
            Result = await Importer.ImportAsync(Db, DirectoryPath.Trim());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }
}
```

- [ ] **Step 3: Replace `Export.razor`**

```razor
@page "/export"
@inject FabulisDbContext Db
@inject VaultService Vault
@inject CategoryExportService Exporter
@inject NavigationManager Nav
@rendermode InteractiveServer

<PageHeader Title="Export All">
    <Subtitle>Write every category, story, version, and draft as markdown files.</Subtitle>
</PageHeader>

@if (ErrorMessage is not null)
{
    <p class="error-message">@ErrorMessage</p>
}

@if (Result is not null)
{
    <div class="success-message">
        <p><strong>Export complete.</strong></p>
        <ul>
            <li>Categories exported: @Result.CategoriesExported</li>
            <li>Stories exported: @Result.StoriesExported</li>
            <li>Versions exported: @Result.VersionsExported</li>
            <li>Drafts exported: @Result.DraftsExported</li>
        </ul>
        <a href="/library" class="btn btn-secondary">Go to library</a>
    </div>
}

<div class="card" style="max-width: 640px;">
    <EditForm Model="this" OnValidSubmit="RunExport" FormName="export">
        <div class="field">
            <label for="export-path">Destination directory</label>
            <InputText id="export-path" @bind-Value="DirectoryPath" placeholder="/path/to/new-directory" />
            <p class="field-hint">
                Enter the full path to a directory that does not yet exist. It will be created
                and every category, story, version, and in-progress draft in the database will
                be written as markdown files.
            </p>
        </div>
        <button type="submit" class="btn btn-primary" disabled="@IsExporting">
            @(IsExporting ? "Exporting…" : "Export")
        </button>
    </EditForm>
</div>

@code {
    [SupplyParameterFromForm]
    private string? DirectoryPath { get; set; }

    private ExportResult? Result { get; set; }
    private string? ErrorMessage { get; set; }
    private bool IsExporting { get; set; }

    protected override void OnInitialized()
    {
        if (!Vault.IsUnlocked)
            Nav.NavigateTo("/unlock");
    }

    private async Task RunExport()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            return;

        ErrorMessage = null;
        Result = null;
        IsExporting = true;

        try
        {
            Result = await Exporter.ExportAsync(Db, DirectoryPath.Trim());
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build Fabulis.slnx
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Components/Pages/SaveDraft.razor src/Fabulis.Server/Components/Pages/Import.razor src/Fabulis.Server/Components/Pages/Export.razor
git commit -m "Rework SaveDraft, Import, Export with new primitives"
```

---

### Task 25: Final verification sweep

End-to-end check that every page works and no legacy class names remain.

**Files:** (read-only verification — no edits expected unless defects surface)

- [ ] **Step 1: Confirm no legacy class selectors leak**

Run:

```bash
grep -rn --include='*.razor' --include='*.css' -E '\.item-list|\.add-form|\.detail-form|\.page-actions|\.draft-header|\.rename-form|\.settings-current|\.settings-item-vertical|\.prompt-assistant|\.conversation|\.chat-input|\.message-role|\.message-content|\.message-actions|\.message-editing|\.message-edit-actions' src/Fabulis.Server || echo 'NONE FOUND'
```

Expected: either `NONE FOUND` or only hits inside commit-safe contexts. `.settings-item-vertical` is an allowed survivor (still used in `Settings.razor`). If any `.item-list`, `.add-form`, `.detail-form`, `.page-actions`, `.draft-header`, `.rename-form`, `.prompt-assistant`, `.conversation`, `.chat-input`, `.message-*` appears, fix the file to use new class names from `components.css`/`pages.css`.

- [ ] **Step 2: Build one more time and run the app**

```bash
dotnet build Fabulis.slnx
dotnet run --project src/Fabulis.Server
```

- [ ] **Step 3: Click through every route in a browser**

Starting from a locked state:

1. `/` → locked hero (flourish + unlock button)
2. `/unlock` → centered unlock form, submit a correct password
3. `/` → dashboard (greeting, ContinueWritingCard, stats, recent)
4. `/library` → category grid + add-category card
5. Add a new category → grid updates
6. Click the category → `/categories/{id}` with story grid
7. Rename category → inline form works
8. Add a story → story card appears
9. Click the story → `/stories/{id}` with timeline or empty state
10. `/stories/new` → storyteller picker + draft list
11. Start a new draft → `/stories/draft/{id}` two-pane writing studio
12. Send a prompt → response streams, renders in Fraunces
13. Edit / regenerate / delete a message — all work
14. Save to library → SaveDraft form → navigates to `/versions/{id}`
15. `/versions/{id}` → reading mode (no sidebar, drop-cap, flourishes)
16. `/storytellers` → card grid
17. `/storytellers/new` → grouped panels, create a storyteller
18. `/settings` → three sections, API key form, assistant model picker, lock vault, clear library
19. `/import`, `/export` → form cards render with hints
20. Resize the browser below 900px: sidebar collapses to icon-rail. Below 600px: sidebar becomes a horizontal strip above the content.

If anything throws a server error, fix it before declaring done.

- [ ] **Step 4: Final commit (if any defects surfaced and were fixed)**

If Step 1 or Step 3 found nothing to fix, skip this step. Otherwise:

```bash
git add -A
git commit -m "Fix UI-refresh defects found during final verification"
```

---

## Success criteria (matches spec)

- ✅ App consistently uses the literary palette, Fraunces display, Inter chrome, gilt accents, and shared primitives.
- ✅ Sidebar is visible on every standard page (except Unlock, locked Home, Version reading mode).
- ✅ Draft page shows active storyteller and sampling params alongside the conversation.
- ✅ No razor or CSS file references the old class names (`.add-form`, `.item-list`, `.detail-form`, …).
- ✅ No functional regression: every route works, every form submits, the vault unlocks, drafts generate.
