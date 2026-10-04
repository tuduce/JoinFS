# JoinFS.UI

The Avalonia (.NET 8, MVVM with CommunityToolkit.Mvvm) implementation of the desktop redesign in
[`docs/design_handoff_avalonia_ui/`](../docs/design_handoff_avalonia_ui/README.md). The HTML prototype there is the visual and
behavioural reference.

## Status

Every screen of the handoff is built: the collapsed and expanded shell, the 11 tabs, and all 9 overlays. It runs on **fake services**
(`Services/Fake/`), not on the live JoinFS app. Wiring comes next, one service at a time.

```
dotnet run --project JoinFS.UI                       # first run shows the onboarding card
dotnet run --project JoinFS.UI -- --skip-onboarding  # start as a returning user
dotnet test JoinFS.UI.Tests/JoinFS.UI.Tests.csproj
JOINFS_UI_SCREENSHOTS=<folder> dotnet test ...       # also keeps a PNG of every screen
```

## Layout

| Folder | What |
|---|---|
| `Styles/` | `Tokens.axaml` (generated, see below), `Icons.axaml`, `Moth.axaml`, `Controls.axaml` |
| `Models/` | Plain records the view models and services exchange |
| `Services/` | One small interface per concern (`Services.cs`), the fakes, and `AvaloniaPlatform` (clipboard, file pickers, browser) |
| `ViewModels/` | `MainViewModel` is the shell; `Tabs/` and `Overlays/` hold the rest |
| `Views/` | AXAML. `App.axaml` maps each view model to its view |

- **Tokens.** Avalonia cannot parse `oklch()`. `util/gen_tokens.py` turns the design's oklch values into sRGB and keeps each
  original in a comment. Edit the table in the script and regenerate; do not edit `Tokens.axaml` by hand.
- **Tabs and overlays reach the shell only through `IShell`** (go to a tab, show an overlay, join a hub), so they do not know each other.
- **The real app stays behind the service interfaces.** The sim, the network and settings are reached through `ISimulatorLink`,
  `INetworkLink`, `ISettingsStore` and the like; none of this project references `Main`, a form or `Settings`.

## Not wired yet (placeholders)

These do nothing, or only fake data, until the real service exists: the Aircraft row actions other than Substitute, Explain Match, Assign
Variables, Adjust Height and Copy Flight Plan; Explain Match's "Open known-models list" and "Export Debug Bundle"; Monitor's "View Logs"
and its filter chips; X-Plane "Install Plugin / Install C++"; the model scan itself (Scan only closes the dialog); the Recorder's file
contents (the dialogs only pick a name); Objects "Group by model".

## Where the build departs from the handoff

- **HTML wins over README** where they differ: the Aircraft and Objects columns, the Variables modal's Add/Remove/Edit buttons.
- Recordings are `.jfs` (as `RecorderForm` writes them), not the README's `.jfr`.
- Cancel in **Adjust Height** discards the edits (the prototype applies them immediately).
- **Objects → Substitute…** acts on the selected row (the prototype hard-codes one model); it is disabled until a row is picked.
- **Substitute**'s "Filter words" narrows the type list; the prototype only shows the box.
- **Import** in the SimBrief prompt is disabled while the username is blank.
- **Home** words its subtitle by what is connected; the prototype has one sentence.
- Protocol shows `JFP2` where the prototype leaves it blank.
- The strip's Flight Plan button shows "Loaded" after any successful SimBrief import, including one started from the Flight Plan tab.
- The window is a real frameless window (custom title bar, square corners), not a floating rounded panel on a fake desktop.
- "Label text colour → Choose…" is a swatch row plus a hex box (no colour-picker package is available offline).

## Where each WinForms form went

| Form | In the new UI |
|---|---|
| `MainForm` | The title bar and the strip (Simulator, hub picker, Network, Flight Plan). Join is the picked hub, Global is the built-in address-book entry, Create is the mesh card on Network Hubs |
| `HubsForm` | Network Hubs tab |
| `SessionForm` (list) | Session tab |
| `SessionForm` (lower part) | Chat tab |
| `PermissionsForm` | The Settings block in an expanded Session row |
| `AircraftForm`, `ObjectsForm`, `MatchingForm`, `RecorderForm`, `MonitorForm`, `FlightPlanForm`, `SettingsForm` | The tab of the same name |
| `SubstitutionForm`, `HeightForm`, `MatchExplainForm`, `VariablesForm`, `ScanForm`, `AboutForm`, `PasswordForm` | Overlays |
| `BroadcastForm` | Links in an expanded Objects row: This object, All of the model, VRS TacPack, Everything |
| `LoginForm` | Gone: joining a password hub asks for the password |
| `InitialSetupForm` | The onboarding overlay |
| `AddressBookForm`, `AddressForm` | Settings → Address Book |
| `XPlaneForm` | The "Install X-Plane Plugin" overlay, from Settings → X-Plane (XPLANE build only) |
| `ScanForm_XPLANE` | The Scan For Models overlay in the XPLANE build (`ScanXPlaneModelsViewModel`): X-Plane folder, derived CSL folder, Generate / Skip CSL, the Aircraft Folders list, Scan at launch. Other builds get the other scan overlay |
| `AtcForm` | Later, as a special view of Network Hubs |
| `ShortcutsForm`, `OptionsForm` | Not taken over. Maybe later in Settings |

Per build: Hub Mode is in every build; the X-Plane card and the "X-Plane label" options show only in the XPLANE build
(`IAppInfo.IsXPlaneBuild`; run with `--xplane` to see them). Hub Mode's fields are always editable.

## Notes

- The Aircraft tab's Record column and the Recorder's "Aircraft to record" list are one selection (`RecordSelection`). "Remove From
  Recorder" / "Add To Recorder" toggle it. "Include All Hub Aircraft" ticks the aircraft with an owner, "Include All Simulator Aircraft"
  those without (my reading of the names: confirm when the real data is wired).
- "Scan at launch" in the X-Plane scan dialog and "Model scan on connect" in Settings are one setting.
- The Home tab has no Map link yet; a map widget is planned.
- The scan itself and the CSL generation are not wired: Scan saves the options and closes the dialog.
