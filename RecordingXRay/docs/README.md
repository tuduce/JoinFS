# RecordingXRay redesign: Avalonia implementation brief

Port `RecordingXRay` from WinForms to Avalonia and replace its tree + text-dump UI with the layout below:
frame browser, map, inspector and a multi-track timeline.

- **Visual reference (source of truth for look):** the Claude Design canvas "RecordingXRay Redesign"
  <https://claude.ai/artifact/58sHBw1s33GNdaHaq2HoyR> (private to the owner). Artboards: *Recording loaded*,
  *No recording open*, *Map* (start / mid-flight), *Prototype: selection, cursor and follow*.
- **Design source files:** [`design/`](design/). They are Claude Design `.dc.html` sources. They do not render on their
  own (the runtime is not included), but every colour, size, spacing value and behaviour rule in them is authoritative.
  `Proto.dc.html` and `Map.dc.html` contain the working selection / cursor / follow / drag logic as JavaScript.
- Design size is 1440 x 900. Set the minimum window size to about 1100 x 700.

## Status

- **Milestone 1 done:** Avalonia 11.3 project, token / icon / control styles (`RecordingXRay/Styles/`), shell layout,
  Open (button, Ctrl+O, drag and drop), summary strip, status bar, empty / loading / error states. The frame browser,
  map, inspector and timeline are placeholders for milestones 2 to 4. Recent files (section 5.5) are not done yet.
- **Milestone 2 done:** virtualised frame browser (aircraft picker, aircraft details popover, type chips, "go to time" / type
  filter, Delta column), inspector for every frame type (Fields cards with degrees, attitude indicator, heading dial,
  control bars, variables table with names, sorting and filter; Raw text; Copy). The aircraft picker is a combo box until the
  map and timeline take over selection in milestones 3 and 4. Selecting a frame does not move a time cursor yet (milestone 3).
- **Milestone 3 done:** the cursor and selection model (`MainViewModel.Cursor.cs`, section 6 rules, with tests) and the
  timeline dock: toolbar (previous / next frame, timecode, frame counter, legend, zoom), one track per aircraft with a
  has-data dot, ruler, lanes drawn between each lane's first and last frame with per-pixel frame columns counted from the
  frame times, playhead, click / drag to scrub, Ctrl + wheel zoom, Shift + wheel pan. Keys, while the timeline has focus:
  Left / Right step, Home / End first / last frame, Up / Down change aircraft. Frame list, inspector and status bar follow
  the cursor; picking a frame in the list moves the cursor to it. Decision taken: scrubbing shows the last *position* frame
  at or before the cursor (frames of other types that share a timestamp are reached by stepping or from the list).
- Tests: `dotnet test RecordingXRay.Tests/RecordingXRay.Tests.csproj` (not in `JoinFS.sln`, so CI does not run it yet).
  Set `XRAY_SCREENSHOT_DIR` (and optionally `XRAY_SAMPLE=<a .jfs file>`) to also write headless PNG screenshots of the
  empty and loaded window, for checking the UI against the design.
- Avalonia 11.3.22 names differ from older docs: `ExtendClientAreaToDecorationsHint`, and drag and drop uses
  `DragEventArgs.DataTransfer` / `DataFormat.File`.

## 1. What exists today (before the port)

`RecordingXRay/` is a `net8.0-windows` WinForms app (`Form1`, ~1250 lines in total).

- `RecordingReader.cs` is the parser and **should be kept as is** (see section 9 for one gap). Model: `RecordingFile` ->
  `RecordedAircraft` / `RecordedObject` -> `List<RecordedFrame>`. Frame types handled: `ObjectPosition`,
  `AircraftPosition`, `SimEvent`, `IntegerVariables`, `FloatVariables`, `String8Variables`.
- `VariableLookup.cs` maps variable ids (uint) to names.
- `Form1.cs` has `FormatFrame` (the existing text dump, reuse it for the "Raw" view) and `TypeRoleToText`.
- The format is documented in `docs/recording-protocol.md`.

## 2. Project setup

- Convert `RecordingXRay.csproj` to Avalonia 11.x: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
  `Avalonia.Fonts.Inter` is **not** wanted. Use `CommunityToolkit.Mvvm` for MVVM. Remove `Form1*`, `UseWindowsForms`.
- Target `net8.0` (Avalonia is cross-platform). Keep the project in `JoinFS.sln` under the same name.
- Theme: `FluentTheme`, `RequestedThemeVariant="Dark"`, dark only for v1.
- The map is a **custom-drawn control** (no tiles), matching the mock-up: dark background, lat/lon graticule, trails,
  markers. Do not add Mapsui or tile downloads in v1.
- Keep logic testable: view models and the pure helpers in section 6 must not touch Avalonia types. Add unit tests
  (there is a `JoinFS.Tests` project on xunit) for the selection rule, cursor/frame mapping, interpolation and number formatting.

## 3. Design tokens

| Token | Value | Use |
|---|---|---|
| window / title bar | `#0B0E16` | window background |
| strip, dock | `#0E121B` | summary strip, timeline dock |
| panel | `#11151F` | frame browser, map, inspector |
| card | `#161B27` | inspector cards, instrument cards |
| raised | `#232A3B` | border of panels, pressed segment, track bar, icon-button border |
| divider | `#1B2130` | row dividers in timeline and strip |
| input bg | `#0B0E16` | filter box, segmented control well |
| text | `#E8ECF6` | primary text, `#FFFFFF` for values and selected |
| text 2 | `#C3CBDD` | secondary text |
| muted | `#8791AD` | captions, indexes, deltas |
| key label | `#A3ADC5` | field names in cards |
| dim | `#5E6985` | disabled / "no data" |
| accent | `#FF8A3D` | selection, playhead, primary button (text on accent `#1A0F05`), row tint = accent at 14% |
| type: Position | `#5BA2FF` | `AircraftPosition`, `ObjectPosition` |
| type: Integer | `#B58CFF` | `IntegerVariables` |
| type: Float | `#4FD1B5` | `FloatVariables` |
| type: String8 | `#F28BB0` | `String8Variables` |
| type: SimEvent | pick a neutral, e.g. `#F2C14B` | `SimEvent` (not in the mock-up) |
| ok / chip | `#4FD1B5`, chip text `#6FE3C8`, chip bg `#4FD1B5` at 14% | "has data" dot, Ground chips |
| map bg / grid / labels | `#0C121C` / `#1A2332` / `#6C7792` | map |
| map trails | selected: accent; others past `#E8ECF6`, future `#9AA6C4` | see section 5.3 |
| attitude | sky `#2A5FA6`, ground `#6A4A2E` | instrument |

Type: UI `Segoe UI Variable Text`, falling back to `Segoe UI`; numbers and times `Cascadia Mono`, falling back to
`Consolas`. Body 13 px; captions 10.5 px uppercase with 0.08 em letter spacing; mono values 12 to 12.5 px;
timecode 20 px semibold; panel title 15 px semibold. Panel corner radius 10, controls 8, chips fully rounded.
Gaps between panels 12 px. Tabular (monospace) digits for every changing number.

## 4. Layout

Top to bottom: title bar 40, summary strip 44, workspace (flex), timeline dock 248 (user-resizable with a splitter),
status bar 28. Workspace, left to right, with `GridSplitter`s between:
frame browser 400 | map (fills, min 400) | inspector 440 (min 360).

- **Title bar:** `ExtendClientAreaToDecorations`, keep the native caption buttons. App icon + "RecordingXRay"; centred
  address pill (520 wide, shows the folder in muted text and the file name in primary text; "No recording open" when empty);
  accent **Open** button.
- **Summary strip:** Version, Aircraft, Objects, Frames (thousands separators), Duration (`711.165 s` plus `11:51.165`).
  Show an em dash for each value when no file is loaded.
- **Status bar:** left status dot + text ("Ready", "Loading...", "Recording loaded", or the error);
  right `{callsign} > [{frameIndex}] {FrameType} @ {time:0.000} s`.

## 5. Components

### 5.1 Frame browser (left)
- Header: callsign (accent plane icon), frame count, and the aircraft's `Model` string. The sample shows the model string as
  `Tiger Moth TIGER-4 01001011110`; display it as-is in muted text. The mock-up splits it for looks only.
- Filter box: accepts a time (`123.4` or `m:ss.fff`) to jump the list to that frame, otherwise filters by type name.
  Type chips (Position / Integer / Float / String8 / others present) toggle visibility of that frame type in the list.
- Columns: Frame `[i]` (right aligned, muted), Time `0.026 s`, Type (colour dot + full type name), Delta (`+75 ms`
  versus the previous frame in the same lane, `—` for the first). Row height 28.
- **Virtualise.** A lane can hold tens of thousands of frames. Bind the `ListBox` to an index list over
  `RecordedObject.Frames`, never create one view model per frame.
- Selecting a row selects the frame: it feeds the inspector and moves the time cursor to the frame's time.
- Clicking the header opens an "Aircraft" popover with `Callsign`, `Nickname`, `Plane`, `Model`, `TypeRole` (use
  `TypeRoleToText`), `Livery`, `IcaoType`, `IcaoAirline`, `Frames`.

### 5.2 Inspector (right)
- Header: type colour dot, frame type name (17 px semibold), subtitle `{callsign} · frame [i] · {t} s`, chips
  **Ground** and **Elevation correction** (green with a check when true, dim with a dash when false),
  segmented control **Fields | Raw**, **Copy** icon button (copies the Raw text).
- **Fields** for `AircraftPosition`: an instruments row (attitude indicator, heading dial), then single-column cards:
  Position (Latitude, Longitude, Altitude, Elevation), Attitude (Pitch, Bank, Heading), Velocity (X Y Z),
  Angular velocity, Acceleration, Controls (raw).
  - Each row: key (left), degrees (muted, only for angular values: latitude, longitude, pitch, bank, heading), raw value
    (right, mono, full precision, `CultureInfo.InvariantCulture`).
  - Latitude, longitude, pitch, bank and heading are **radians** in the recording (the sim definitions use radians);
    show degrees beside the raw value: latitude/longitude 4 decimals, pitch/bank 2, heading 2.
  - Controls: Rudder, Elevator, Aileron are centred bars, Brake left / right fill from the left. The recording stores
    fixed-point `int16` where `value = raw / 16384` (see `docs/recording-protocol.md` 4.2), so scale bars with
    `raw / 16384`, ±1. (The mock-up assumed 16383; use 16384.) Show the raw integer at the right.
- `ObjectPosition`: same as above without Controls; show `Height` instead of `Elevation`.
- `SimEvent`: two rows, `EventId` and `Data`.
- `Integer/Float/String8Variables`: a table `Id | Name | Value` (name from `VariableLookup.Resolve`, "unknown" when
  missing), sortable, with a filter box. No instruments.
- **Raw:** the existing `FormatFrame` output in a read-only selectable mono text box.
- Attitude indicator and heading dial are small custom controls (`Render` override). Pitch and bank sign convention
  must match the sim: SimConnect reports positive pitch as nose down. Verify against a recording where the aircraft
  is parked: a tail-dragger on the ground should show the nose up, which is a **negative** pitch.

### 5.3 Map (centre)
- Header: "Map", `{n} of {total} aircraft at {m:ss.fff}` (just `{total} aircraft` when all have data), toggle chips
  **Trails**, **Labels**, **Follow**. Overlays: Past / Future legend (top-left), zoom in / zoom out / fit-all buttons
  (top-right), scale bar (bottom-left, adaptive), readout pill (bottom-right): accent dot, selected callsign,
  `55.4293°N 13.2377°E`, plus ` · no data` when the selected lane has no data at the cursor.
- Projection: local Web Mercator (or equirectangular scaled by `cos(lat)`), pan and zoom with pointer drag and wheel.
  Graticule lines and labels adapt their step to the zoom (the mock-up uses 0.1° lon / 0.05° lat at its default scale).
  On load, fit the bounds of all trails.
- **The entire flight is always drawn** for every aircraft. Build each trail once per lane from the position frames
  (thin it: drop points closer than about 1 px at the fit zoom, or Douglas-Peucker), then split it at the cursor:
  - Past (before the cursor): solid and bright. Selected: accent, 3.2 px. Others: `#E8ECF6`, 2 px, 85% opacity.
  - Future (after the cursor): dim and dashed. Selected: accent, 2.2 px, 45% opacity, dash `6 5`. Others: `#9AA6C4`,
    1.6 px, 32% opacity, dash `4 5`.
- **Markers** sit at the interpolated position at the cursor time (linear between the neighbouring position frames;
  heading by shortest angle). Only aircraft whose lane has data at the cursor are shown, plus the selected one
  (drawn at 50% opacity at the nearest end of its lane when it has no data). Selected: accent dot r7, soft halo,
  heading arrow, name in an accent chip with dark text. Others: `#C3CBDD` dot r5, name in 11 px semibold (hidden
  when Labels is off). Draw the selected aircraft last.
- **Click selects.** Clicking a marker (hit radius about 16 px), or pressing Enter / Space on a focused marker, calls
  `SelectAircraft` (section 6). Markers are keyboard focusable. A drag that ends over a marker must not count as a click.
- **Follow:** keeps the selected aircraft centred, also as the cursor moves and when another aircraft is selected.
  **Any user pan (pointer drag) turns Follow off**, starting from the current view so nothing jumps.
  Clicking Follow on re-centres; clicking it off keeps the current view. **Fit all** resets to the all-trails bounds
  and turns Follow off. Selecting an aircraft with Follow off does not move the view.
- Aircraft with no position frames get no marker or trail (they still have a timeline lane).
- `RecordedObject`s (non-aircraft) are not in the mock-up, and the sample file has none. Default: add them as extra
  lanes below the aircraft in the timeline and as smaller grey markers; hide the section entirely when there are none.

### 5.4 Timeline dock (bottom)
- Toolbar: previous / next frame buttons (move the cursor to the previous / next frame of the selected lane,
  honouring the type filter), timecode `mm:ss.fff` (accent, 20 px mono semibold), `/ 11:51.165` total, `frame [i] of N`,
  type legend, zoom slider. **No playback**: this app only inspects data.
- Left column (236 wide): "Aircraft (N)" header; one button row per lane: a **has-data dot** (filled green when the
  cursor is inside the lane's extent, hollow dim otherwise, with a tooltip), name, frame count. Selected row: accent
  tint and white name. Clicking a row calls `SelectAircraft`.
- Right area: ruler (ticks adapt to zoom; every minute at full zoom) and one lane per aircraft, 34 px each. A lane is drawn only
  between its first and last frame time. Draw a density histogram from precomputed bins (for example 2048 bins per lane):
  upper bar 10 px blue for position frames, lower bar 6 px with the variable types in violet / teal / pink. Selected lane
  at full opacity, others at 50%. Do not draw one rectangle per frame.
- Playhead: 2 px accent line with a flag on the ruler. Click or drag anywhere on the ruler / lanes to scrub
  (capture the pointer). Ctrl + wheel zooms, Shift + wheel pans.
- Keyboard: Left / Right previous / next frame, Home / End first / last frame of the selected lane, Up / Down change
  the selected aircraft (through `SelectAircraft`).

### 5.5 Empty, loading, error
- **Empty:** dashed drop zone filling the workspace: icon, "Open a joinfs recording", one line of help,
  **Choose file** button, **Recent** list (up to 5, new feature, persisted in `%AppData%/RecordingXRay/settings.json`;
  the mock-up shows one entry). Strip values are dashes, the dock shows "Aircraft tracks appear here once a recording is
  open", status "Ready". The whole window accepts a dropped `.jfs` file.
- **Loading:** reading can take a moment on big files; show an indeterminate bar in the status bar and keep the UI responsive
  (read on a background task).
- **Error:** keep the previously loaded recording, show the exception message in an inline banner above the workspace
  (replaces the old `MessageBox`).

## 6. Selection and cursor model (core behaviour)

State: `Recording`, `SelectedLane`, `Cursor` (seconds), `SelectedFrame`, `FollowMap`.

```csharp
// Lane extent = [first frame Time, last frame Time] of that RecordedObject.
bool HasData(Lane lane, double t) => t >= lane.FirstTime && t <= lane.LastTime;

void SelectAircraft(Lane lane)        // map marker, timeline track, keyboard Up/Down
{
    if (Cursor < lane.FirstTime)      Cursor = lane.FirstTime;   // cursor before the lane: jump to its first frame
    else if (Cursor > lane.LastTime)  Cursor = lane.LastTime;    // cursor after the lane: jump to its last frame
    // otherwise the cursor stays where it is
    SelectedLane = lane;
}
```

- The rule is **extent-based** (inside first..last counts as data, even across gaps).
- Moving the cursor (scrub, step, keyboard) never changes the selected aircraft; the selected aircraft can end up with
  "no data" (dim dot, "no data" in the map readout, marker at 50%).
- Cursor to selected frame: the last position frame of the selected lane with `Time <= Cursor`, falling back to the first
  frame. Several frames share one timestamp (for example Integer, Float and String8 at 0.154 s), so do not select by
  time alone: when a frame is chosen in the list it keeps the cursor at its time and stays selected until the cursor moves.
- Selecting a frame in the browser sets `Cursor = frame.Time`.
- Interaction details (drag vs click, Follow off on pan) are in section 5.3.

## 7. Suggested structure

- `MainViewModel`: `Recording`, `Lanes`, `SelectedLane`, `Cursor`, `SelectedFrame`, `FollowMap`, `OpenCommand`,
  `StepFrameCommand`, `SelectAircraftCommand`, loading / error state.
- `LaneViewModel`: wraps a `RecordedObject`: `Name`, `Model`, `Frames` (as an `IReadOnlyList`), `FirstTime`, `LastTime`,
  position-frame index (sorted by time, binary search), trail points, density bins.
- `FrameInspectorViewModel`: builds the card sections for the selected frame type.
- Controls: `MapView`, `TimelineView`, `AttitudeIndicator`, `HeadingDial`. Everything else is plain XAML with styles
  built from the tokens in section 3 (put them in a `ResourceDictionary`; no hard-coded colours in views).
- Accessibility: real buttons for markers, tracks and chips; `AutomationProperties.Name` on icon buttons; visible
  focus ring; text contrast 4.5:1 on the dark surfaces (the token table already satisfies this).

## 8. Milestones

1. Convert the project, theme and resource dictionaries, shell layout, open file, summary strip, status bar.
2. Frame browser (virtualised) + inspector for all frame types + Raw / Copy.
3. Cursor and selection model with tests, timeline dock (lanes, ruler, scrubbing, keyboard).
4. Map (projection, graticule, trails with past / future split, markers, click select, follow, drag-off).
5. Empty / loading / error states, recent files, drag and drop, polish.

## 9. Placeholders in the mock-up, and open points

- Only YR-SCD's first frame (0.026 s) and its position come from the sample recording `tigers 3rd.jfs`. The other four
  aircraft positions, all trails, the lane extents of other aircraft and the mid-flight coordinates in the mock-up are
  illustrative. Implement from the real frames.
- The lane texture in the mock-up is illustrative; the real one is the density histogram described in 5.4.
- Check `RecordingReader` against files from build 21008 or newer: `docs/recording-protocol.md` 4.2 says
  `AircraftPositionFrame` gains a trailing `StaticCgToGround` float at version 21008+, and the current reader does not
  read it. The sample file is version 21005, so it parses fine, but newer files may mis-parse. Fix separately from this
  UI work, and expose `StaticCgToGround` in the inspector once the reader reads it.
- Confirm: objects handling in 5.3, SimEvent colour, and whether a light theme is wanted later.
