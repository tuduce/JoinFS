# JoinFS.UI

The Avalonia (.NET 8, MVVM with CommunityToolkit.Mvvm) implementation of the desktop redesign in
[`docs/design_handoff_avalonia_ui/`](../docs/design_handoff_avalonia_ui/README.md). The HTML prototype there is the visual and
behavioural reference.

## Status

Every screen of the handoff is built (the collapsed and expanded shell, 11 tabs, the overlays). It is being wired to the live app one
service at a time. Two ways to run it:

```
# on its fake services only, to work on the screens
dotnet run --project JoinFS.UI.Dev                       # first run shows the onboarding card
dotnet run --project JoinFS.UI.Dev -- --skip-onboarding  # start as a returning user
dotnet run --project JoinFS.UI.Dev -- --xplane           # what the XPLANE build shows

# inside the real app (a -Debug build; see "Live wiring"), the wired services real and the rest still fake
dotnet build JoinFS\JoinFS.csproj -c FS2024-Debug -p:Platform=x64
JoinFS\bin\FS2024-Debug\net8.0-windows\JoinFS-FS2024-Debug.exe -newui
# or: dotnet run --project JoinFS\JoinFS.csproj -c FS2024-Debug -p:Platform=x64 --launch-profile "New UI"

dotnet test JoinFS.UI.Tests/JoinFS.UI.Tests.csproj
JOINFS_UI_SCREENSHOTS=<folder> dotnet test ...           # also keeps a PNG of every screen
```

## How to tell which one you are running

The title bar of the fake launcher reads "JoinFS-FS2024 (fake data)". Anything you change there lives in memory and is gone when the window
closes: nothing is saved. The live app's title bar is the build name (for example "JoinFS-FS2024-Debug"). A build started without `-newui`
opens the old forms.

Where the live app keeps what the wired screens change, under `%LOCALAPPDATA%\JoinFS-<build>\`: the address book is `bookmarks2.txt`
(one `name=address` per line), and the app's log (`log-<port>.txt`) says "Saved N entries in the address book" each time.

## Live wiring

The two connection buttons follow the real state. `MainViewModel.Poll()` (a UI timer calls it four times a second) reads each link's
`State`; a click only asks for a change, and the button changes when the app does. A click while "connecting" gives up the attempt.
The flight-plan button is not live: it is local to the UI.

`JoinFS.UI` is a library; `UiHost.Run(services, args)` starts it on the calling thread. `JoinFS/Live/` (in the JoinFS project, so it can
see `Main`) holds the adapters and `NewUiLauncher`, which builds `AppServices` from the fakes and swaps in the real ones. `Main()` calls
the launcher instead of opening the WinForms forms when started with `-newui`.

| Service | State |
|---|---|
| `IAppInfo` (version, title, links, build) | **Live**: `Main.Version`, `Main.Name`, `#if XPLANE` |
| `ISettingsStore` (nickname, SimBrief name, broadcast and scan options) | **Live**: `Settings.Default`, plus the app's own copies (`settingsNickname`, `settingsScan`). Onboarding is shown while the nickname has fewer than 2 characters, as `MainForm` did |
| `IAddressBookStore` | **Live**: `bookmarks2.txt` through `main.addressBook`, with the built-in Global entry in front; the picked entry is `JoinAddress` |
| `IUpdateChecker` | **Live**: the same `version.txt` the forms read |
| `ISimulatorLink` (the Simulator button) | **Live**: state from `sim.View` (the sim thread's snapshot); a click goes to the sim thread through `ToggleSimulator()`, as the old button did |
| `INetworkLink` (the Network button, Join, Create, password) | **Live**: state from the session snapshot, mapped by the old button's own rule (`NetworkButtonStyle`); Join is `Main.Join(name)`, Disconnect leaves the session, Create is leave then create. A protected session is answered as the old window did: the remembered password first, then a prompt |
| `ISessionSource` (Session tab, Home counts) | **Live**: the users, as the old `SessionForm` listed them, read once a second while the tab is open. Allow Cockpit Entry and Allow Multiple Objects are the `log` entries; Hand Over Controls is the flight controls only (`Peers.shareFlightControls`); Save adds or removes the user in the address book; Ignore is the `log` ignore list |
| `ITrafficSource`, aircraft part (Aircraft tab, Home counts) | **Live**: the aircraft as the old `AircraftForm` listed them (yours, the network's, the recorder's, and on request the simulator's own and other hubs'), with its details and flight plan. Record sets the aircraft's `record` flag on the sim thread; Ignore is the `log` ignore list (by user, or by callsign for the simulator's own); Follow, Enter/Leave Cockpit, Track and Stop Tracking are the sim commands the old menu posted; each is enabled by the old menu's own rules. The two "Include …" links are the old list filters (`IncludeGlobalAircraft`, `IncludeSimulatorAircraft`). Substitute, Explain Match, Copy Flight Plan, Assign Variables and Adjust Height are disabled until the Model Matching work |
| `ITrafficSource`, objects part (Objects tab) | **Live**: the objects as the old `ObjectsForm` listed them (grouped by owner and model when the `GroupObjects` setting is on, which "Group by model" now is). Only objects of the network can be ignored (owner or model, through the `log`); only your own can be broadcast: one object (sim thread), all of a model (`log`), the VRS TacPack and Everything (settings). Substitute is disabled until the Model Matching work. Not yet exercised against real objects (none were present when it was tried) |
| `IHubDirectory` (Network Hubs tab) | **Live**: the hubs the old `HubsForm` listed, read from the app's hub list (online or not, ignored or not); a hub is joined by its address as the old double click did. Add to / Remove From Address Book edits `bookmarks2.txt` through `main.addressBook`; Ignore is the `log` ignore list, by guid and by address. The list is read when the tab opens and once a second after |
| The rest | Fake |

Not handled yet: the old login dialog (email and password, `JoinResult.LoginRequired`), and the message that a remembered password
was rejected. Model overrides and height adjustments are not in `Settings` yet; they stay in memory until the substitution wiring.

`-newui` exists only in builds with `NEWUI` defined. That is every `-Debug` configuration except CONSOLE; pass `-p:NewUi=true` to turn it on for
a release build. It is off by default there because the reference adds the Avalonia libraries to what the installers ship. The forms are
not opened with `-newui`, so anything that only a form handled (the scheduled plugin install, login and nickname prompts) does not happen.

The onboarding card has no simulator-folder section yet. The old `InitialSetupForm` asked for it when auto-detection failed.

## Layout

| Folder | What |
|---|---|
| `Styles/` | `Tokens.axaml` (generated, see below), `Icons.axaml`, `Moth.axaml`, `Controls.axaml` |
| `Models/` | Plain records the view models and services exchange |
| `Services/` | One small interface per concern (`Services.cs`), the fakes, and `AvaloniaPlatform` (clipboard, file pickers, browser) |
| `UiHost.cs` | How a program starts the UI |
| `ViewModels/` | `MainViewModel` is the shell; `Tabs/` and `Overlays/` hold the rest |
| `Views/` | AXAML. `App.axaml` maps each view model to its view |

- **Tokens.** Avalonia cannot parse `oklch()`. `util/gen_tokens.py` turns the design's oklch values into sRGB and keeps each
  original in a comment. Edit the table in the script and regenerate; do not edit `Tokens.axaml` by hand.
- **Tabs and overlays reach the shell only through `IShell`** (go to a tab, show an overlay, join a hub), so they do not know each other.
- **The real app stays behind the service interfaces.** The sim, the network and settings are reached through `ISimulatorLink`,
  `INetworkLink`, `ISettingsStore` and the like; this project never references `Main`, a form or `Settings`. The adapters live in
  `JoinFS/Live/`.

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
  Recorder" / "Add To Recorder" toggle it. "Include All Hub Aircraft" and "Include All Simulator Aircraft" are not part of it: they are
  list filters, as in the old window (an earlier version of this README guessed otherwise).
- "Scan at launch" in the X-Plane scan dialog and "Model scan on connect" in Settings are one setting.
- The Home tab has no Map link yet; a map widget is planned.
- The scan itself and the CSL generation are not wired: Scan saves the options and closes the dialog.
