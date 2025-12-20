# JoinFS Release 25.8

## New Features
The JoinFS versioning system changes to a timeline versioning scheme. The version number is now in the format `YEAR.SEQUENCE.0`. In some places the trailing .0 is ignored.

The JoinFS project now targets .NET 8.0 for all variants.

The solution file was reorganized.
- A single project builds all the variants, for their corrsponding architecture.
- The project file was simplified to use SDK-style project format.
- The build process was simplified using MSBuild properties and targets.
- The output folder structure was simplified.
- A single installer project builds all the installers.
- A CI/CD pipeline was introduced to use the new build process.
- An automated workflow checks code quality and security features.
- A testing framework was introduced to automate unit and integration tests.

A new model matching algorithm was introduced to improve the accuracy of model matching across different simulators. The new model matching algorithm uses a combination of model name, livery, and other metadata to identify models more accurately. This new model matching is described in more detail in its [wiki page](https://github.com/tuduce/JoinFS/wiki/Enhanced-Model-Matching-System).

## Bug Fixes
- Replaced the random number generator with a cryptographically secure random number generator.
- Fixed an issue where in MSFS2024 shared cockpit the entered aircraft would bank sharply to the right.

## Limitations
The `FSX` and `P3D` variants are built for the x86 (32bit) architecture. Since the Microsoft.ML package does not currently offer a x86 variant, the AI-enchanced model matching is not included for `FSX` or `P3D`.

## Known Issues
> [!WARNING]
> The FSX and XPLANE variants are built in a similar fashion like the original JoinFS. I do not have access to FSX to test. The XPLANE vaiant was tested briefly, since I only have the demo version of XPLANE. Please report any issues you find with these variants.
> If you want to help with testing or development of these variants, please contact me.

## Installation
Download the appropriate installer for your flight simulator:
- FS2024: JoinFS-FS2024.msi
- FS2020: JoinFS-FS2020.msi
- FSX: JoinFS-FSX.msi
- P3D: JoinFS-P3D.msi
- XPLANE: JoinFS-XPLANE.msi
- CONSOLE: JoinFS-CONSOLE.zip (for headless/console mode)
