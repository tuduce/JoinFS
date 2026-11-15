## New Features

- **New user interface.** JoinFS has a completely new, modern window. It opens as a compact bar with the essentials (simulator, network and your flight plan) and expands into tabs for the session, hubs, aircraft, chat, recorder, model matching and settings. You can pick a hub by typing its name or address, join one with a double-click, import your flight plan from SimBrief, and see up-to-date pilot and aircraft counts for every online hub. The old window has been retired.
- **JFP2 now works on its own.** JFP2, the faster network language introduced in 26.6, used to rely on the older language to create, join and leave sessions. It now does everything by itself: creating and joining sessions, staying connected, the hub list, and passing traffic through other pilots when two of you can't reach each other directly. There is nothing to set up: JoinFS picks the best language automatically, and pilots on older versions can still fly with you as before.
- **IPv6 support.** Many internet providers now hand out the newer IPv6 addresses, and some give only those. JoinFS can now connect over IPv6 as well as the classic IPv4, and prefers IPv6 when both sides have it. Everything works the same way: creating and joining sessions, the hub list, chat, flight plans, weather and recording. Pilots who have only IPv4 and pilots who have only IPv6 can still fly together, because a JoinFS that has both passes the traffic between them.
- **New recording format (.jfs2).** Recordings are now saved in a new, sturdier format. Each aircraft gets its own track that can start and end at any time, instead of every aircraft starting at the beginning of the recording. If JoinFS or your PC stops unexpectedly while recording, the recording can be recovered afterwards. The format is built so that future versions can add information without breaking existing files. Your old .jfs recordings still open and can be saved in the new format.
- **Airliners in the right colours first.** When JoinFS has to choose a model for another pilot's airliner, it now prefers a very similar aircraft in that pilot's airline colours over the exact type painted for a different airline. For example, a Lufthansa A320 can appear as your Lufthansa A321 rather than as an A320 of another company. Light aircraft, helicopters, balloons and gliders still get the exact type first. Seventeen aircraft types from X-Plane and several helicopters were added to the reference data. (Thanks @joeherwig for the contribution)
- **Better matching of gliders.** The official aircraft codes lump all gliders together as "GLID" and all motor gliders as "GLIM", so JoinFS could not tell a modern 20-metre racing glider from a small wooden trainer. It now reads hints in the aircraft's name, such as K7, LS4 or Cirrus, to show a glider of the right size and kind, and uses a generic glider only as a last resort. Several more aircraft types were added to the reference data as well. (Thanks @joeherwig for the contribution)
- **Easier X-Plane setup.** JoinFS now finds your X-Plane 11 and 12 installations by itself, including Steam ones, and installs the plugin without asking you to browse for folders. Right after installing, it scans your aircraft. Model packs you already have, such as X-CSL, Bluebell or IVAO_CSL, are linked instead of copied, which saves disk space. If you like, JoinFS can also create a model for each livery, so other pilots appear in their real airline colours. (Thanks @joeherwig for the contribution)
- **More data for live-feed displays.** For anyone who connects instrument panels, maps or other tools to the JoinFS live feed (the WebSocket): every aircraft now also reports its pitch and bank, the flaps position is now correct (it always showed zero before), and the heading moves smoothly in tenth-of-a-degree steps. A new `--feedfullrate` option makes pilots send a hub their full-rate positions, so displays fed from a hub move more smoothly. (Thanks @joeherwig for the contribution)
- **Choose how often whazzup.txt is written.** Hub operators who publish a whazzup.txt file, the list of connected pilots that online maps and other tools read, can now choose how often it is updated. Start JoinFS with the new `--whazzupdelay` option followed by a number of seconds, anywhere from a tenth of a second to once an hour. Values that make no sense are ignored and the usual interval is kept. The option is explained in the Options window. (Thanks @virtual-flying for the contribution)

## Bug Fixes

- **Taildraggers no longer snap on parts of the runway.** A taildragger such as a Spitfire taxiing on a runway could keep snapping between tail-down and level, but only on one stretch of the runway. This happened where the ground height in your scenery differed slightly from the other pilot's. JoinFS then mistook the aircraft for standing on a raised platform, such as a ship's deck, and stopped holding it on the ground. It now makes this judgement from the height of the landing gear and gets it right.
- **Lights and switches of other aircraft follow much faster.** When another pilot switched lights, lowered the gear or moved other controls, it could take up to two seconds before the change was even sent. Your simulator then took only about one change every three to five seconds, because JoinFS mistook its own updates for local changes. Changes now arrive in less than half a second, JoinFS no longer trips over its own updates, and a quick flick of a switch is never sent twice.
- **Strobe lights stay off.** On some aircraft, for example the IFE Typhoon, another pilot's strobe lights switched themselves back on about three seconds after being turned off, because two different settings for the same light kept undoing each other. JoinFS now passes on only the light switches an aircraft's settings actually describe. As a side effect, pilots using versions from before 26.4, and recordings made with them, no longer show their strobe lights.
- **JoinFS starts even when it cannot save its variable files.** Windows' Controlled folder access or an antivirus can stop JoinFS from writing its files in your Documents folder. When that happened, JoinFS quietly failed to start: no window appeared, and the reason was only in the log. It now starts anyway, keeps the existing files, and suggests checking Controlled folder access or your antivirus. If start-up fails for any other reason, a message now shows the error and where to find the log.
- **X-Plane lists and settings are no longer lost.** JoinFS keeps your X-Plane models, matching rules and similar lists in files named after the simulator. While X-Plane was not running they were saved under the name "Not Connected" instead, so a scan made before starting X-Plane seemed to disappear. They are now always stored under X-Plane, and existing "Not Connected" files are picked up automatically. On first start, the plugin question is now asked before the window shrinks, and the number of models found appears in the status bar.
- **The CONSOLE download and Docker image run on the right processors.** In 26.6 the x64 download of the CONSOLE version, the window-less version used for hubs and servers, actually contained a program for ARM processors, so it would not run on ordinary PCs and servers. It is now built correctly, and every build checks this automatically. The Docker image is also built for the right processor again and is published only for releases, tagged with the version number.

## Limitations

The `FSX` and `P3D` variants are built for the x86 (32bit) architecture. Since the Microsoft.ML package does not currently offer a x86 variant, the AI-enchanced model matching is not included for `FSX` or `P3D`.

## Known Issues

- Some XPLANE models appear incomplete (when the model has a space in the filenames of the model data).
- When moving the timeline of a recording in XPLANE, the recorded aircraft disappears.
- When in XPLANE an aircraft model is substituted, the new model is displayed in the center of gravity of the original model. If the replacement model is smaller than the original model, it may appear to be floating in the air. If the replacement model is larger than the original model, it may appear to be embedded in the ground.

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
