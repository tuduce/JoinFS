**Pre-release for testing** of two changes: the flight plan following your aircraft (SimBrief only for a matching aircraft), and the new height placement of other pilots' aircraft on the ground. Based on JoinFS v26.7 (`tuduce/JoinFS` main), branch `prerelease/simbrief-ground-settle`. The program still reports itself as version 26.7.0.

## New Features

- **Other aircraft sit on the ground in every simulator.** On ordinary ground, JoinFS now places another pilot's aircraft only by what your own simulator knows: your ground under the aircraft, and how high the model you see stands on its wheels or skids. The other pilot's altitude no longer counts there. It doesn't matter whether that pilot flies X-Plane or MSFS, or which model JoinFS picked to show them: the wheels touch your ground. In the air the other pilot's altitude is used as before. Between the two, JoinFS blends smoothly over 2 seconds after touchdown and 10 seconds after take-off, so there is no jump. An aircraft is never shown below your ground. (Thanks @joeherwig for the contribution)
  - In MSFS, FSX and P3D, JoinFS uses the ground and wheel height the simulator reports for the aircraft it shows, and lets the simulator settle it on its wheels.
  - In X-Plane, the plugin places each aircraft itself: on the ground under it, plus the model's own offset to its wheels.
  - **Adjust Height** still works for a model that hovers or sinks by its own data. It now acts only on the ground, so a model fixed on the ground is not also shifted on approach. In MSFS, a model with a height adjustment is held at that height instead of being pushed back up onto its wheels.
- **The flight plan follows your aircraft.** When you change your aircraft, callsign or registration in the simulator, JoinFS updates your flight plan and sends it to the other pilots straight away. (Thanks @joeherwig for the contribution)
  - A different aircraft type is a new flight: the route of the previous one is cleared, and your latest SimBrief plan is loaded only if it was made for that type. Add-ons that report a type of the same family count as a match (the FlyByWire A380X reports A380 for SimBrief's A388).
  - A new callsign or registration keeps your route. If your latest SimBrief plan was filed under the new callsign, the whole plan is loaded.
  - As long as nothing changes in the simulator, your own edits and imports stay as they are.
  - "Clear" fills in your aircraft's callsign and type from the simulator and files the plan at once. "File plan" sends your plan to the other pilots at once.
- **Your MSFS 2024 callsign is used.** The callsign you enter in MSFS 2024 (for example BAW388) is now sent with its airline, also on a livery of another airline. Airline plus flight number come next, and your tail number only when there is neither. (Thanks @joeherwig for the contribution)
- **X-Plane uses your airline callsign.** Enter an airline and flight number in X-Plane, for example in the FMC, and other pilots see you as e.g. KLM511 with the right airline, instead of your tail number. Without an airline, your registration is used as before. (Thanks @joeherwig for the contribution)

## Bug Fixes

- **No SimBrief plan for the wrong aircraft.** An older SimBrief plan for another aircraft type was loaded automatically, at start-up or after changing aircraft, so other pilots saw your airliner as a helicopter. Automatic loading now only uses a plan made for the aircraft you fly. If you load from SimBrief yourself and the plan is for another type, JoinFS asks first: press Enter to load it anyway, or Esc to keep your plan.
- **X-Plane aircraft parked on the ground showed as flying.** X-Plane decided "on the ground" by the aircraft's centre being less than a metre above the ground, which is never the case for most airliners. JoinFS now uses X-Plane's own wheel contact, and also tells other pilots how high your aircraft stands on its wheels. X-Plane traffic no longer hovers in MSFS.
- **The flight plan shown in the bar stays current.** After an aircraft change, the bar and the Flight Plan tab no longer keep showing the previous route. A plan imported in the tab but not yet saved is dropped when you switch to another aircraft type.
- **No flicker after filing.** After filing a plan, the old callsign no longer shows for a moment before the new one.

## For testers

- **Uninstall JoinFS 26.7 first.** These installers carry the same version number, and an installed 26.7 also takes the connection to the simulator.
- **X-Plane: update the plugin.** Install it from the Settings with X-Plane closed. An older plugin does not connect to this version.
- **Hubs and console servers 26.7 work.** The network protocol is unchanged, so you can fly with pilots on 26.7.
- In the Monitor (network), every other aircraft writes a `GroundPlacement` line when its placement changes: on the ground, blending or in the air, with the ground height, wheel height and adjustment used.

## Installation

Please follow the instructions for your simulator.

### MSFS2024 or MSFS2020

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the installer corresponding to your simulator version (`JoinFS-FS2024-x64-26.8-SimbriefGroundSettlebeta.msi` or `JoinFS-FS2020-x64-26.8-SimbriefGroundSettlebeta.msi`).

### FSX or P3D

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the installer corresponding to your simulator version (`JoinFS-FSX-x86-26.8-SimbriefGroundSettlebeta.msi` or `JoinFS-P3D-x86-26.8-SimbriefGroundSettlebeta.msi`).

Please make sure that you have the SimConnect SDK installed for your simulator version.

### XPLANE

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download `JoinFS-XPLANE-x64-26.8-SimbriefGroundSettlebeta.msi`. Start JoinFS with X-Plane closed and install the plugin using the "Install X-Plane Plugin" button in the settings.

### CONSOLE

`JoinFS-CONSOLE-x64-26.8-SimbriefGroundSettlebeta.zip`, compiled for `x64`.
