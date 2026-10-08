## New Features

- Introduced JFP2, a new network protocol for exchanging aircraft position and state data between JoinFS peers, running alongside the existing protocol. JFP2 messages are significantly smaller than the previous format, especially for the high-frequency position updates that make up most network traffic, reducing bandwidth usage during a session. It also separates rarely-changing aircraft details (livery, registration, aircraft type) from position updates, fixing a class of bug where appearance information could get out of sync mid-session. JFP2 is negotiated automatically between two updated JoinFS instances; sessions involving an older version continue to work exactly as before, unaffected.
- Reorganized the simulator and network internals. The simulator now runs on its own thread, woken directly by SimConnect or the X-Plane plugin instead of a 5 ms polling loop, and all SimConnect calls come from that thread. On MSFS, FSX and P3D, positions of injected, broadcast and recorded aircraft are now read every rendered frame instead of 20 times per second, reducing steering jitter on remote and recorded aircraft. Network traffic and the recording rate are unchanged. The network stack also got its own thread and a plugin design that lets JFP2 run alongside the existing protocol.
- Reduced CPU work on the network and simulator hot path: aircraft are found through an index instead of by scanning the object list, peer lists are built once per tick instead of for every aircraft and peer during position broadcasts, object-creation checks no longer allocate memory every tick, and diagnostic text is only built when the matching Monitor option is enabled. Model matching also looks up installed models by title through an index instead of scanning every model. These changes don't alter behavior; sessions with many aircraft and peers use less CPU.
- Position estimation for remote aircraft is now swappable: clock models, estimators and steering laws sit behind interfaces in Estimation/, chosen by command-line options. It adds the ClassicFixed estimator and the MinOffset clock (both now defaults), and sim-clock stamps on samples. It adds a per-sample estimation log, steering-gain experiments (Gain4, Gain8, Gain16) with an alternate mode, Python analysis scripts and a tester package. Gain4 is now the default steering.
- Reduced the number of SimConnect requests used to read aircraft variables on MSFS2020/2024, FSX and P3D. Variables that were previously requested individually are now bundled into a single combined request per aircraft, lowering SimConnect overhead - most noticeable with many aircraft nearby. X-Plane and network compatibility are unaffected.
- If the download of the seedhubs.txt fails over HTTP, the list is fetched from a TXT DNS-record.
- Added a protocol dissector for Wireshark, allowing users to inspect JoinFS network traffic in detail.

## Bug Fixes

- Fixed yaw trembling after crossing the 2*PI heading boundary in a recorded plane the user entered cockpit.
- Fixed position messages generated with v26.5 could not be interpreted by older versions.
- Fixed guaranteed-message delivery broken for any relayed peer.

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
