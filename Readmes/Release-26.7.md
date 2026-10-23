## New Features

- JFP2, the protocol introduced in 26.6, now stands on its own. A JoinFS node can create a session, join one, stay in it, leave it, use the hub list and be relayed without a single message of the original protocol.
- New UI
- IPv6 support
- JFS2 - New recording format. The new format sts clear boundries for data fields, so that evolutions of the recording format do not break the reading of recording files. JFS2 allows for multiple aicraft tracks that start at different times (as opposed to the old format where all recorded planes started at time 0).

## Bug Fixes



## Limitations

Old .jfs recordings are converted to the new format .jfs2. The new format is not backwards compatible with the old format. Joinfs versions before 26.7 will not be able to play the new recording format.

Smart model matching has been tested live on MSFS 2024 only. MSFS 2020, FSX, P3D and X-Plane use the same rules with less measured aircraft data (on X-Plane the plugin still makes the final choice of the CSL model). About 75 of the 399 bundled reference aircraft are verified against manufacturer data; the others are compiled values and still to be checked.

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

The `CONSOLE` variant is compiled for both `x64` and `ARM64` architectures.

Please make sure that you have the .NET 8.0 runtime installed. You can download it from the [.NET download page](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

Download the ZIP file (`JoinFS-CONSOLE-ARCH-VER.zip`) and extract it to a folder of your choice. Follow the instructions in the `Old-Readme.txt` file.
