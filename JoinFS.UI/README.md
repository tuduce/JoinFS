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
| `IPreferencesStore` (the Settings cards: User Interface, Simulator, Network, Hub Mode, X-Plane) | **Live**: `Settings.Default`, plus the app's own copies (`settingsPort`, `settingsHub*`, `settingsWhazzup*`, `settingsTcas`, …), as the old `SettingsForm`'s OK did. The cards have no OK: every change is saved at once, and `Settings.Default` is written to disk half a second after the last one. A save acts only on what changed: a new port is opened (when the box loses focus, and only if it is a whole port), hub mode starts or stops the hub, Show Nickname removes the injected aircraft, Low bandwidth goes to the network. Hub mode needs a name of 3 characters, as before; until it has one the card shows why and saves nothing. Always on top is applied to the window. Auto refresh and Tool tips are saved, but the new UI does not use them yet |
| `IFlightPlanStore` and `ISimBriefClient` (Flight Plan tab, the strip's Flight Plan button) | **Live**, both in `LiveFlightPlanSource`. The plan is the sim thread's `userFlightPlan`: read from `Sim.View`, changed by a posted command, as `FlightPlanForm`'s OK did (the callsign is then "set by user", the airline follows the callsign, a blank callsign or type is the aircraft's own again). The tab shows the live plan until the user edits it, then keeps their edits until Save or Clear. SimBrief is the old fetch (`Sim.FetchSimBriefAsync`). **The strip's button** fetches, makes the plan the user's and sends it to the network at once, as the old SimBrief button did, and shows `simBriefFetchState` (fetching, loaded; "not loaded" for nothing asked yet and for a failed fetch); clicking "Loaded" only forgets the fetch. **The tab's link** only fills the fields, to be checked and saved, as the old dialog's import did. A registration and an alternate that an import brings, which the tab has no field for, reach the plan on Save. Not yet tried live with a real SimBrief user: only "no plan found" |
| `IModelCatalog` (Model Matching tab, Substitute, the model picker) | **Live**: `main.substitution`. The table is its match table, defaults first and then the others by name, with the substitute shown as the title and, in FS2024, ` [+] livery`. Substitute (from the table, from an object of the network, or from an aircraft) goes through `SetMatch`/`ClearMatch` for what is injected and `SetMasquerade`/`ClearMasquerade` for your own and the simulator's aircraft, the operations the old `EditMatch`/`EditMasquerade` now use too: the file is saved and the aircraft using the model are replaced. "Use Original" clears the match, so a default rule's row goes (it comes back when the defaults are chosen again). The picker is the old dialog's: filter words, type, variation, and the picker starts on what `Match()` gives for the model now. Without models it says to scan first. The tab says how many models are known, or that a scan is running |
| `ITrafficSource`, model parts (Explain Match, Adjust Height, Assign Variables, Copy Flight Plan) | **Live**. **Explain Match** is the old window's: the table, the steps and the other candidates, with its Markdown report for the clipboard and the debug bundle (a zip of the report, the models, matching and masquerading files); for your own aircraft it runs the matcher as a preview. **Adjust Height** keeps the adjustment per substituted model in the sim's `HeightAdjustmentStore`, for aircraft JoinFS creates; OK saves, Cancel does not. **Assign Variables** is the old `VariablesForm`: the model picker, the model's files from the sim thread's `ModelVariableStore`, Add (a file under `Documents\JoinFS\Variables` only, as before), Remove, Edit (not the built-in files), and OK, which connects the simulator again so the lists take effect. **Copy Flight Plan** puts the row's plan on the clipboard; it has no old equivalent |
| `IVariablesCatalog` (Settings → Variables) | **Live**: the models that have a list of their own (those not using the default of their kind), read when the card opens; Edit is the Variables overlay |
| `IModelScanSource`, `IXPlaneScanSource` (Scan For Models) | **Live**: where the last scan looked (`Substitution` keeps it, in the folders file) and the scan itself, which runs in the background as before (`Substitution.ScanFolders`, shared with the old dialog). Microsoft Flight Simulator has no subfolder list (its packages nest `SimObjects`), FS2020 lists its add-ons, FS2024 has "FS2024 models via SimConnect"; the others list the `SimObjects` subfolders. X-Plane scans the aircraft folders ticked. The old confirmation before generating CSL objects is the warning shown under the checkbox. Not tried: a scan itself, which rewrites the models file |
| `IMonitorSource` (Monitor tab) | **Live**: the last 50 lines of `main.monitor`, read once a second while the tab is shown and scrolled to the end, with the old window's note that the rest is in the files. FPS is measured as the old window did (SimConnect builds only; X-Plane shows none). **Network** and **Variables** are the old context menu's switches (`monitor.network`, `monitor.variables`). **Node Statistics** and **Received Packets** were one-shot menu items that write a dump into the log, so they are buttons with the chip look (`Monitor.WriteNodeStatistics`/`WritePacketStatistics`, which the old window now calls too). View Logs opens this run's and the last run's file. The old "save the log" tick is gone: the log has been written to a file always |
| `IChatSource` (Chat tab, the unread dot) | **Live**: the session channel of `main.notes`, as the old session window showed it: the notes of the pilots of the session that are not ignored, oldest first, with the callsign each flew under. Notes expire after ten minutes, so that is as far back as the chat goes. Sending posts a note to the session, at most one a second as before; a line that starts with "." is a command answered to you alone (only `.help`) and kept for an hour. The chat is read every quarter of a second while it is on screen and marks what it shows as read; elsewhere only the dot is kept up to date, from what others said since it was last looked at (your own messages and ignored pilots do not count). Without a session the line is off and says to join a hub. The old "show chat" tick is gone: the tab is the chat. Not tried live: sending to a session and receiving from another pilot |
| `IRecorderSource` (Recorder tab) | **Live**: the sim thread's recorder; its flags and published end time are read, everything else is asked of the sim thread, as the old window did. The mode, the time, the length and both lists are read again every quarter of a second while the tab is shown. **Record** starts a take of the aircraft ticked in the Aircraft tab; it replaces playing and overdubbing. **Play** plays, pauses while playing and goes on when paused. **Overdub** plays and records on top. **Stop**, **Loop**, **Open** (replaces the recording and plays it), **Add** (after its end) and **Save** are the old window's, with the old `.jfs` files and folder. A recording that was made or added to and not saved is asked about before a new one starts, before another file is opened, and when the window closes. The playhead moves, and trims cut, only while a take plays or is paused: the old window would have cut the whole take from a playhead at zero. The "Aircraft to record" list leaves out aircraft that cannot be recorded (the ones the recorder plays). In "Loaded recording", **unticking an aircraft leaves it out of playback** (`Recorder.Skip`; it is still saved in the file); its tick then goes grey until the recording is loaded again, because ticking it back is postponed (see below) |
| The rest | Fake |

Not handled yet: the old login dialog (email and password, `JoinResult.LoginRequired`), and the message that a remembered password
was rejected.

`-newui` exists only in builds with `NEWUI` defined. That is every `-Debug` configuration except CONSOLE; pass `-p:NewUi=true` to turn it on for
a release build. It is off by default there because the reference adds the Avalonia libraries to what the installers ship. The forms are
not opened with `-newui`, so anything that only a form handled (the scheduled plugin install, login and nickname prompts) does not happen.

The onboarding card has no simulator-folder section yet. The old `InitialSetupForm` asked for it when auto-detection failed.

## Left for later

Old things the new UI does not take over. Each is marked in the code with a `TODO(newui-cleanup)` or `TODO(newui-review)` comment, so
`git grep "TODO(newui-"` lists them.

| Marker | What | Where |
|---|---|---|
| cleanup | **Early update.** Early updates no longer exist: remove the setting, the checkbox and the reads | `SettingsForm.cs`, `MainForm.cs` (three reads) |
| cleanup | **Indicator colours.** Users no longer choose them: remove the `Colour*` settings, the buttons in `SettingsForm` and every form that reads them (all the old forms) | `SettingsForm.cs` |
| review | **ATC mode** (airport, level, frequency, Euroscope). Decide where it lives, presumably the Atc view | `SettingsForm.cs` |
| review | **Reset settings.** Does the new UI need one? | `SettingsForm.cs` |
| review | **Tick a skipped aircraft again** in the Recorder's "Loaded recording" list, so it joins playback once more. Postponed: it has to start again at the right point of the take. Until then an unticked aircraft stays out until the recording is loaded again | `RecorderViewModel.cs` (`LoadedAircraftViewModel`) |

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
