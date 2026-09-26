## Compatibility - read before upgrading

The peer-to-peer network protocol and the recording (`.jfs`) file format changed
in 26.6.

- Every peer in a session, **and the hub / console you connect through**, must
  run 26.6 or newer. A hub still on an older build feeds corrupt aircraft
  positions to every client on it. 26.6 detects and discards a wrong-format
  position packet (keeping the last good position) rather than showing garbage,
  but the real fix is to update everything together.
- `.jfs` recordings saved by 26.6 will not load in 26.5 or older; 26.6 still
  loads older recordings.
- Each position record now carries a length prefix, so a future format addition
  loads (the unknown field is skipped) instead of failing to read.

## New Features

- The Flight Plan dialog's "Clear" button re-reads your callsign and aircraft
  type from the simulator instead of leaving a stale manual or SimBrief value.
- Callsign and type refresh automatically when you change aircraft mid-session,
  and the SimBrief auto-import re-runs if it is enabled. This stops JoinFS
  broadcasting a previous leg's callsign, which caused wrong-livery matches for
  everyone else. See the [Flight Plan and SimBrief](https://github.com/tuduce/JoinFS/wiki/Flight-Plan-and-SimBrief)
  wiki page.
- Test-phase command-line switches (no Settings entry, not persisted):
  `-groundaltitudedeltalimit <m>` (on-ground snap-back tolerance, default 1.5),
  `-injectionretryseconds <s>` (retry delay for a refused injection, default 10),
  `-tracediagnostics` (first-chance exception and per-tick ground-placement
  tracing, off by default).

## Bug Fixes

- **Substitute aircraft are grounded on their own real STATIC CG TO GROUND
  clearance, not the sender's**, so a substitute of any size sits correctly on
  the ground instead of nose-gear-up or jittering. On ordinary ground the
  simulator's own gear/contact physics owns the vertical axis (JoinFS commands
  only horizontal position and heading); on a genuine raised structure (helipad,
  ship deck, rig, rooftop) that the receiver's scenery lacks, JoinFS holds the
  sender's reported altitude and attitude. A retractable substitute's gear is
  forced down while the sender is on the ground. Applies to live and recorded
  traffic.
- **FS2020 / FS2024: traffic now appears without toggling the simulator
  connection.** An injection the simulator refused while still loading was marked
  failed permanently; it now retries on a backoff and re-arms on a fresh
  connection or a SimStart event.
- **MSFS2020: network and recorded helicopters are no longer stuck on the
  terrain** sliding around - they are injected as normal aircraft and the
  on-ground decision no longer trusts MSFS2020's unreliable injected-object
  on-ground bit.
- **Crashes to desktop now leave a `crash-<port>.txt` file** with a full stack
  trace. The work thread is guarded, so one internal error is logged and JoinFS
  keeps running; a storm of errors escalates to a clean shutdown. On startup
  JoinFS points you at an unhandled crash file from a previous run.
- **X-Plane: remote aircraft are drawn again.** Every position packet from the
  plugin was failing to read because the shared position record was parsed with
  the plugin link's protocol version. The X-Plane path is pinned to the layout
  the plugin actually sends, and the position record is now self-describing.
- **Console / hub feeds:** a wrong-format or truncated position packet from a
  version-mismatched peer is discarded instead of decoded into garbage and
  re-broadcast. The WebSocket feed can no longer crash the console process,
  serialises a non-finite value as `0` instead of throwing, drops a slow or
  half-open client after a 5-second deadline instead of stalling the others, and
  no longer leaks its change-tracking table.
- **The `ATC FLIGHT NUMBER` callsign builder** no longer misfires on a flight
  number with a trailing letter (e.g. `34U` is now combined into `EWG34U`
  instead of broadcast bare).
- **Title-based model matching:** coined names with internal capitals (XCub,
  NXCub) resolve an ICAO type from the title; a title-guessed designator's class
  code / wake class is no longer double-penalised; and shared words are scored in
  any position, not just a leading prefix.
- **The public hub list, ban list, model-matching data and the update check** are
  fetched through the jsDelivr CDN (with a fork fallback) instead of
  `raw.githubusercontent.com`, which had started returning HTTP 404.
- **Fixed yaw trembling after crossing the 2*PI heading boundary** in a recorded
  plane the user entered the cockpit of.
- **Optimized angular velocity interpolation.** A recorded aircraft's orientation
  is now blended with quaternion SLERP instead of being snapped to `OBJECT_EULER`
  every tick, and playback's internal heading/pitch/bank accumulator is kept
  bounded instead of growing without limit across many turns. Together these fix
  a heading/tail "jitter" that could start during a sustained turn (e.g. a
  glider thermalling in tight circles) and then persist for the rest of the
  replay, only clearing if the recorded plane's cockpit was re-entered. This
  also depends on the recording actually carrying real angular velocity, not
  the zero every `.jfs` writer used to send - see
  [joinfs-gpx-to-jfs-webcomponent](https://github.com/joeherwig/joinfs-gpx-to-jfs-webcomponent)'s
  own changelog for the writer-side half of this fix if you generate recordings
  from a GPX track.

## Limitations

The `FSX` and `P3D` variants are built for x86 (32-bit). The Microsoft.ML package
has no x86 build, so AI-enhanced model matching is not included for `FSX` or
`P3D`.

## Known Issues

- A genuinely crooked platform that exists in neither your nor the sender's
  scenery cannot be reproduced. A very shallow platform may not be recognised as
  elevated and would settle slightly low; the manual height override in the
  Aircraft window remains available.
- Some XPLANE models render incomplete when the model's data files have spaces in
  their names.
- Moving the timeline of a recording in XPLANE makes the recorded aircraft
  disappear.
- In XPLANE a substituted model is drawn at the original model's centre of
  gravity, so a smaller replacement can look airborne and a larger one buried.

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
