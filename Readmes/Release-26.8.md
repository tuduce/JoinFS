## New Features

- **Other aircraft sit on the ground in every simulator.** On the ground, JoinFS now places other pilots' aircraft using your own simulator's ground and the height of the model you see on its wheels, so the wheels touch your ground whatever simulator or model the other pilot uses. In the air their altitude counts as before, with a smooth blend after touchdown and take-off. Aircraft are never shown below the ground. Adjust Height still fixes models that hover or sink, now only on the ground. (Thanks @joeherwig for the contribution)
- **The flight plan follows your aircraft.** When you change aircraft, callsign or registration in the simulator, JoinFS updates your flight plan and sends it to the other pilots at once. A new aircraft type starts a new flight: the old route is cleared, and your latest SimBrief plan is loaded only if it was made for that type. A new callsign or registration keeps your route. Your own edits stay as long as nothing changes in the simulator. "Clear" and "File plan" now send the plan straight away. (Thanks @joeherwig for the contribution)
- **Your airline callsign is used.** In MSFS 2024, the callsign you enter (for example BAW388) is now sent with its airline, even on another airline's livery. In X-Plane, enter an airline and flight number, for example in the FMC, and other pilots see you as e.g. KLM511 with the right airline instead of your tail number. Otherwise the airline plus flight number is used, and your registration only when there is neither. (Thanks @joeherwig for the contribution)
- **Air traffic control with EuroScope.** Controllers can now run EuroScope next to JoinFS on the same PC: tick "Enable ATC client connection" in Settings -> ATC and connect EuroScope to JoinFS. Every pilot of the session appears on the radar screen with flight plan, squawk and ident. Controllers see each other, hand aircraft over, share notes, chat and publish an ATIS. Nothing a controller does changes your simulator. This replaces the old manual ATC mode.
- **Follow and enter recorded aircraft from the Recorder tab.** Each aircraft of the loaded recording now has "Follow" and "Enter" links, so you no longer have to find it in the Aircraft tab. They work while the recording plays; "Enter" becomes "Leave" while you are in a cockpit. (Thanks @TaruTaru113 for the idea)
- **Network Hubs, Session, Aircraft and Monitor in windows of their own.** Click the icon at the top right of one of these tabs and it opens in a separate window that you can move and resize as you like, for example onto a second monitor. The list keeps updating even while the main JoinFS window is small. Click the icon in that window, or close it, to put the tab back. JoinFS remembers which tabs you undocked and where their windows were, and opens them there again the next time it starts.

## Bug Fixes

- **No SimBrief plan for the wrong aircraft.** JoinFS sometimes loaded an older SimBrief plan made for another aircraft type, at start-up or after changing aircraft, so other pilots saw your airliner as a helicopter. Automatic loading now only uses a plan made for the aircraft you fly. If you load a plan from SimBrief yourself and it is for another type, JoinFS asks first: press Enter to load it anyway, or Esc to keep your current plan.
- **X-Plane aircraft parked on the ground no longer show as flying.** X-Plane only counted an aircraft as on the ground when its centre was less than a metre above it, which is never true for most airliners. JoinFS now uses X-Plane's own wheel contact, and tells other pilots how high your aircraft stands on its wheels, so X-Plane traffic no longer hovers in MSFS.
- **The flight plan you see stays current.** After you change aircraft, the bar and the Flight Plan tab no longer keep showing the previous route, and a plan imported in the tab but not yet saved is dropped when you switch to another aircraft type. After you file a plan, the old callsign no longer flashes up for a moment before the new one.
- **X-Plane: CSL is no longer generated when you switched it off.** If you use only installed CSL packs such as X-CSL, JoinFS 26.7 generated CSL objects for all your aircraft at every start, even with "Generate CSL objects for installed aircraft" unticked, which can take tens of gigabytes. JoinFS now counts your linked packs as models and never generates CSL at start-up while the option is off. 

## Limitations

The `FSX` and `P3D` variants are built for the x86 (32bit) architecture. Since the Microsoft.ML package does not currently offer a x86 variant, the AI-enchanced model matching is not included for `FSX` or `P3D`.

## Known Issues



## Installation

Please follow the instructions for your simulator.

### MSFS2024 or MSFS2020

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the installer corresponding to your simulator version (`JoinFS-FS2024.msi` or `JoinFS-FS2020.msi`). If upgrading from a `3.2.x` version, please uninstall the previous version before installing the new one.

### FSX or P3D

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the installer corresponding to your simulator version (`JoinFS-FSX.msi` or `JoinFS-P3D.msi`). If upgrading from a `3.2.x` version, please uninstall the previous version before installing the new one.

Please make sure that you have the SimConnect SDK installed for your simulator version.

### XPLANE

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the installer corresponding to your simulator version (`JoinFS-XPLANE.msi`). If upgrading from a `3.2.x` version, please uninstall the previous version before installing the new one.

If you are installing JoinFS for the first time, start JoinFS before starting XPLANE. From JoinFS install the plugin into XPLANE using the "Install XPLANE Plugin" button in the settings dialog.

### CONSOLE

The `CONSOLE` variant is compiled for `x64` architectures.

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the ZIP file (`JoinFS-CONSOLE.zip`) and extract it to a folder of your choice. Follow the instructions in the `Old-Readme.txt` file.
