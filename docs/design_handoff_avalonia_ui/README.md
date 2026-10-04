# Handoff: JoinFS Desktop UI Modernization

## Overview
A redesign of the JoinFS desktop client (a multiplayer/shared-object add-on for flight simulators). It covers the full app shell: collapsed "mini" view, expanded view with sidebar navigation, 11 main tabs, and all modal overlays. Target implementation is a native **Avalonia (.NET) desktop app using MVVM**.

## About the Design Files
The bundled `.dc.html` files are **design references**, not production code. They are interactive HTML prototypes built to show exact look, states, and behavior. The task is to **recreate these designs in Avalonia/XAML using MVVM** — translate layout, styling, and interaction patterns into AXAML views + view models, Avalonia styles/resources for design tokens, and `ReactiveUI`/`CommunityToolkit.Mvvm` (or the project's preferred MVVM toolkit) for state and commands. Do not attempt to embed or ship the HTML — it is reference only.

If an Avalonia project/environment doesn't already exist, scaffold one (.NET 8, Avalonia latest stable, MVVM toolkit of choice) before implementing these screens.

## Fidelity
**High-fidelity.** Colors, spacing, typography, radii, and copy in the HTML are final — recreate pixel-close using Avalonia's styling system (not necessarily 1:1 pixel values, since DPI/font rendering differ, but match proportions, hierarchy, and spacing scale closely).

## App Shell
- Floating window-style panel, bottom-left anchored (`left:24px; bottom:24px`), white background, 12–14px corner radius, soft drop shadow (`0 24px 60px -12px rgba(blue-black,.35)`).
- Custom title bar (not OS chrome): 18px tiger-moth glyph icon + "JoinFS-FS2024" session label (12.5px, weight 600), chat badge (unread dot), expand/collapse toggle, minimize/maximize/close glyphs. Height ~38px, background `oklch(0.97 0.003 250)`, 1px bottom border.
- Two states, animated between (`width/height/border-radius` transition, 0.38s cubic-bezier(.2,.8,.2,1)):
  - **Collapsed**: compact 2-node flow diagram — Simulator connect button → Hub address picker → Network connect button, each with state labels. "Open full view ⤢" link expands it.
  - **Expanded**: fixed two-pane layout — 216px dark sidebar (nav) + flexible content area for the active tab.

## Sidebar (Expanded view)
- Background `oklch(0.20 0.015 255)`, text `oklch(0.92 0.005 255)`, 1px bottom border under the header in `oklch(0.30 0.015 255)`.
- Header: 60×60 tiger-moth SVG icon (two-tone, colors follow theme) + "JoinFS" wordmark (15px, weight 600).
- Nav list, 11 items, each a row with 18×18 stroke-based icon (`currentColor`, stroke-width ~1.4-1.6) + label, 2px gap between rows, hover state `background: oklch(0.27 0.02 255)`, active state similarly highlighted:
  1. Home
  2. Network Hubs
  3. Session
  4. Aircraft
  5. Objects
  6. Model Matching
  7. Flight Plan
  8. Recorder
  9. Chat (shows unread-dot badge when `hasNewChat`)
  10. Monitor
  11. Settings
- Footer: version string "v26.6.0" (11px, muted), top border divider. **Now clickable** — opens the About overlay (see below). Hover state lightens text color.

## Shared Table/Panel Conventions
- White cards/panels, `border-radius: 10-12px`, border `1px solid oklch(0.92 0.005 250)`, occasional shadow for floating modals.
- Tables: `border-collapse: collapse`, header row background `oklch(0.96 0.003 250)`, header text `oklch(0.5 0.01 250)` 12px, sortable columns show a click-to-sort chevron/arrow indicator appended to the label.
- Primary action buttons: `background: oklch(0.62 0.14 235)` (blue), white text, no border, `border-radius: 7px`, `padding: 8px 18-24px`, `font-weight:600`.
- Secondary/cancel buttons: white background, `1px solid oklch(0.85 0.005 250)` border, same radius/padding.
- Accordion sections (Settings tab): row with label (14px, weight 600) + chevron icon that rotates 180° (`transform` + `transition: transform .15s`) when open; no section is open by default.

## Tabs (11 total)
1. **Home** — landing/dashboard tab.
2. **Network Hubs** — table of hubs (Name/Status/Users/Aircraft/Version, sortable), expandable rows showing hub "about" text, voice-chat link, next scheduled event. Status badges (Online/Password/Offline/Global) have distinct colors. Clicking a password-protected hub opens the Password Required overlay.
3. **Session** — connected users table (Nickname/Callsign/Connected/Latency/Simulator/Version/Protocol), protocol badge highlights "Legacy" in red/orange.
4. **Aircraft** — sortable table (Owner/Model/Count/Bearing/Distance), footer Group-by/List toggle, row actions opening Substitute / Adjust Height / Explain Match modals.
5. **Objects** — same table pattern as Aircraft, scoped to scenery/shared objects.
6. **Model Matching** — matching rules/table, shares Substitute and Explain Match modals with Aircraft.
7. **Flight Plan** — plan details + "Import from SimBrief" action. Uses a stored SimBrief username if present; otherwise opens the SimBrief prompt overlay once and remembers the value (persisted, not re-asked).
8. **Recorder** — transport controls (Open/Add/Save, Play/Stop/Overdub), timecode readout in monospace font.
9. **Chat** — message list + composer (flagged for possible follow-up visual pass).
10. **Monitor** — log/debug table with Export Debug Bundle action (also reachable from Explain Match).
11. **Settings** — accordion list: Simulator, User Interface, Network, Hub Mode (Public), Address Book, X-Plane, Variables. All collapsed by default.

## Overlays / Modals
All modals follow the same shell: centered, `background: oklch(0.2 0.01 250 / 0.45-0.5)` scrim, white card, `12px` radius, header row (title + ✕ close) with bottom border, scrollable body, footer action row with top border.

- **Onboarding** (`isOnboarding`): first-run only. Nickname input (required), optional "Using SimBrief?" toggle revealing a SimBrief-username field, Continue button (disabled until nickname entered).
- **Password Required** (`isPasswordPrompt`): shown when joining a password-protected hub. Hub name + password field, Cancel/Join.
- **SimBrief Import Prompt** (`isSimbriefPrompt`): shown once from Flight Plan if no SimBrief username stored; remembers the value after confirm.
- **Model Scanning** (Simulator settings action): progress/scan overlay.
- **Variables** (shared by Aircraft & Settings): list of mapped simulator variables with Remove/Edit per row, OK to close.
- **Adjust Height** (`isEditingHeight`): per-model Y-offset editor with ±5cm / ±50cm steppers.
- **Explain Match** (`isExplainingMatch`): read-only attribute comparison table (Attribute/Requested/Matched), Copy to clipboard + Export Debug Bundle + Close.
- **About** (`isAboutOpen`) — NEW: opened by clicking the sidebar version number. Shows 56×56 tiger-moth icon, "JoinFS" title, current version, Documentation link (external), MIT license + copyright line, and — only when a newer version is available (`newVersionAvailable`) — a "Download vX.Y.Z →" link to the update URL. Close via ✕ or footer Close button.

## Design Tokens
- **Color space**: all colors defined in `oklch()`. Keep oklch (or convert to an equivalent token system) rather than flattening to arbitrary hex — it's what keeps theme-aware re-coloring (e.g. the tiger-moth icon) consistent.
- **Core palette**:
  - App background: `oklch(0.88 0.006 250)`
  - Panel/card background: `#fff`
  - Sidebar background: `oklch(0.20 0.015 255)`
  - Sidebar hover: `oklch(0.27 0.02 255)`
  - Primary accent (buttons, links): `oklch(0.62 0.14 235)` / link text `oklch(0.55 0.16 235)`
  - Borders (light): `oklch(0.85-0.92 0.005 250)`
  - Muted text: `oklch(0.5-0.6 0.01 250)`
  - Body text: `oklch(0.22 0.01 250)`
  - Destructive/warning accents: red-orange hues around `oklch(0.55-0.6 0.15-0.2 25)`
- **Typography**: `'Segoe UI', system-ui, sans-serif` throughout. Scale: 11px (meta/footer) / 12-12.5px (body small, inputs) / 13-14px (table/body) / 14.5-16px (headings/modal titles).
- **Radius scale**: 6px (inputs, small chips) / 7-8px (buttons, inputs) / 10-12px (cards/modals) / 50% (circular icon buttons).
- **Shadows**: modals/cards use `0 20px 50px -10px oklch(0.2 0.01 250 / 0.4)`; main shell uses `0 24px 60px -12px oklch(0.2 0.01 250 / 0.35)`.
- **Spacing**: section padding typically 14-18px; gaps between stacked elements 6-16px depending on density.

## Assets
- **Tiger Moth icon**: custom two-path SVG (biplane silhouette), sourced from `formation-tigermoth-mono.svg` and inlined directly into the shell at two sizes: 18px (title bar, single-tone `currentColor`) and 60px (sidebar header, two-tone theme colors). The same artwork is reused at 56px inside the About overlay. Recreate as a vector asset (e.g. `.axaml` `Path`/`Geometry` resource or SVG-to-XAML conversion) so it can still take theme-aware fill colors.
- No other custom icons — all other glyphs are simple inline stroke/fill SVG shapes (arrows, chevrons, circles, rects) drawn directly in markup; recreate as vector `Geometry`/`PathIcon` resources or a small icon font.

## State Model (recreate as ViewModel properties/commands)

### App shell state
- `expanded: bool` — collapsed vs expanded view. `toggleExpand` flips it. Drives `panelSize`/`panelRadius` (collapsed: 376×190, r12; expanded: min(1200,vw-48)×min(760,vh-48), r14) and the animated width/height/radius transition.
- `tab: string` — active tab id (`home|network|session|aircraft|models|flightplan|recorder|chat|objects|log|settings`). One `goX()` command per tab sets it and also forces `expanded:true`.
- `onboarded: bool` — persisted; `isOnboarding = !onboarded`. First-run gate.
- `hasNewChat: bool` — unread chat flag; shows sidebar badge + title-bar badge when collapsed/on another tab; cleared by `openChat()` (which also switches to Chat tab and expands).

### Connection state machines (Simulator / Network / Flight Plan)
Three independent 3-state machines, each `disconnected → connecting → connected`, built from one shared helper pattern (`statusProps`):
- `simState`, `netState`, `fpState`, each one of `disconnected|connecting|connected`.
- Each exposes a button label that changes per state (Sim: Connect/Connecting…/Disconnect; Network: Join/Connecting…/Disconnect; Flight Plan: Fetch/Fetching…/Loaded) and a status label (Disconnected/Connecting…/Connected, or for FP: Not loaded/Fetching…/Loaded).
- `toggleNet`: if connecting, no-op; if connected, go straight to disconnected; if disconnected, look up the selected address-book hub — if it `requiresPassword`, open the Password modal with a `connect` continuation; otherwise connect immediately (simulate transition to `connecting` then `connected` after ~900ms).
- Recreate as an enum-backed state machine per connection with a `ICommand Toggle` and simulated/real async transitions.

### Address book
- `addressBook: [{ name, address, requiresPassword? }]`, `selectedAddr: string` (bound to a `<select>` in both collapsed and expanded views).
- `addAddr`: appends `{name, address}` from `newAddrName`/`newAddrHost` draft fields (no-op if name blank), then clears the draft fields.

### Network Hubs tab
- `hubs: [{ name, status, users, aircraft, version, about, voice, nextEvent, isExpanded, ignored }]`. Source rows are `[name, status, users, aircraft, version, about, voice, nextEvent]` tuples. `status` ∈ `Online|Password|Offline|Global` with distinct badge colors (`statusRow(status)` style map).
- Sorting: `hubSortKey`/`hubSortDir`, toggled per-column via `sortHubsBy(key)`; `hubSortIndicators` appends ↑/↓ to the active column header.
- `expandedHub: name|null` — row expansion (accordion-style, one at a time) shows `about`/`voice`/`nextEvent`.
- `ignoredHubs: string[]` — per-row ignore toggle.
- `showOfflineHubs: bool` filter toggle.
- Clicking a `Password` hub opens the Password modal (see below) before connecting.

### Session (connected users) tab
- `users`, generated per address-book-like peer list: `{ nick, callsign, connected, latency, aircraft, simulator, protocol, objectsExported, port, version, connStyle, protoStyle, isIgnored }`.
- `version` is `18.2.4` when `protocol==='Legacy'` else `26.4.0`; Legacy protocol gets a red/orange highlighted badge style.
- `port = 6809 + (nick.length % 40)` (placeholder derivation — replace with real session data).
- `ignoredPeers: string[]` toggle per row.

### Aircraft / Objects tabs (shared shape)
- Rows: `{ owner, model, count, bearing, distance }` + sort indicators; `sortAcBy(key)` / `objSort(key)` cover `callsign|owner|distance|heading|altitude|gs|model|bearing|count` depending on tab.
- Footer toggles: `groupByModel: bool`, `listIgnoredObjects: bool` (both currently display-only placeholders — wire to real grouping/filtering logic).
- Row actions open shared modals: Substitute (`editingModel`), Adjust Height (`editingHeightModel`), Explain Match (`explainingMatchModel`).

### Model Matching / Substitute modal
- `editingModel: { original, substitute } | null`; derived `editModel` adds live filter/type/variation drafts (`editFilter`, `editType`, `editVariation`).
- `modelOverrides: { [originalModelName]: substituteModelName }` — persisted override map.
- `useOriginalModel()` sets override to itself (i.e. "no substitution") and closes; `saveModelEdit()` commits `editModel.preview` into `modelOverrides` and closes.

### Adjust Height modal
- `editingHeightModel: modelName | null`.
- `heightAdjustments: { [modelName]: centimeters }` — stepper commands `±5`/`±50` and an `Off` (reset to 0) action, all immutable-merge updates keyed by the currently-editing model.

### Explain Match modal
- `explainingMatchModel: modelName | null`.
- `explainMatchRows: [{ attribute, requested, matched }]` — static comparison table (Manufacturer row shown; extend with real matching attributes: Model, Livery, ICAO type, etc.).
- Footer actions: Copy to clipboard, Export Debug Bundle (currently no-op placeholders), Close.

### Flight Plan tab
- `fp: { callsign, type, rules, from, to, altitude, route, remarks, simbrief }`, `setFp` field setters, `clearFp()` resets to blank defaults (`rules:'VFR'`).
- `simbriefUsername: string|null` — persisted once provided.
- `importFromSimbrief()`: if a username is already stored, calls `performSimbriefImport()` directly; otherwise opens the SimBrief prompt modal.
- SimBrief prompt modal (`simbriefPrompt: bool`, `simbriefPromptValue`): `confirmSimbriefPrompt()` stores the typed value into `simbriefUsername`, closes the prompt, then runs the import — so the user is asked only once, ever.

### Recorder tab
- Transport is a single-active-mode state machine: `recording`, `playing`, `overdubbing` — only one true at a time; `toggleRecording`/`togglePlay`/`toggleOverdub` each flip their own flag and force the other two off; `stopAll()` clears all three.
- `playheadSec`, `totalSec` (default 92s) drive a seekable timeline: click-to-seek and drag-to-seek (`seekTimeline`, `startSeekDrag` with window-level mousemove/mouseup), plus `trimStart()`/`trimEnd()` which shrink `totalSec` relative to the current playhead.
- `loopEnabled: bool` toggle.
- Time formatted via `fmtTime(seconds)` → `mm:ss`-style monospace readout, shown as `{{recordTime}} / {{totalTime}}`.
- Open/Add use a hidden file-input ref (`recordingFileInputRef`); Save synthesizes a Blob download via an anchor ref (`recordingSaveLinkRef`), filename `recording.jfr`.

### Monitor (Log) tab
- `monitorFilters: { nodeStats, packets, network, variables }` booleans, each rendered as a toggle chip (`monitorFilterChips`) with an active/inactive style pair (active: blue-tinted background + border + dot; inactive: white/grey).
- `logLines` — raw log rows for the table.

### Chat tab
- `chatLines` — message list (visual treatment flagged as a possible follow-up pass; current implementation is a simple list + composer, not yet matched to the rest of the app's density).

### Settings tab (accordion)
- `settingsOpen: string|null` — exactly one of `simulator|ui|networkSettings|hub|addressBook|xplane|variables` open at a time, or none (no default). One `toggleX()` command per section (clicking an open section's header closes it; clicking another opens it exclusively). Chevron rotates 180° when open (`transition: transform .15s`).
- **Hub Mode section** fields: `hubFields = [Domain, Name, About, Voice Server, Next Event]`, each with a placeholder — these are the fields a user fills in to publish their own hub.
- **Variables section**: `variablesList: [{ model, files, edit }]` (seed rows: GC1a Swift Factory → ListBox_Sets; PMDG 777-200ER → Custom_FMC_Vars, ListBox_Sets; CH-1 N-1981 → Rotor_Vars). `edit` opens the shared Variables modal (`editingVariables: { model, files }|null`), also reused from the Aircraft tab.
- **Simulator section** includes the "Scan Models" action opening `isScanningModels` overlay (progress/scan UI — build out the scan progress state when wiring to real model-scanning).
- **Whazzup toggle**: `whazzupEnabled: bool` (defaults true) with a disabled-looking sub-label style when off.

### About overlay (new)
- `isAboutOpen: bool`, opened from the sidebar version label, closed via ✕ or Close button.
- `newVersionAvailable: bool`, `newVersionLabel: string`, `downloadUrl: string` — when `newVersionAvailable` is true, show the "Download vX.Y.Z →" link; wire these three to a real update-check service.

## Files
- `JoinFS Shell.dc.html` — full app shell, all 11 tabs, and all overlays/modals (primary reference).
- `JoinFS Logo.dc.html` — standalone logo/icon component reference.
- `support.js` — runtime helper for the HTML prototypes only; not relevant to the Avalonia implementation.
