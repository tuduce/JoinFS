## Model Matching Redesign

- **Unified scoring-based matcher.** Replaces the old tiered ICAO/Category/Auto fall-through with a single weighted scorer (ICAO type, Doc8643 class code, wake turbulence category, engine count/type, registration, ICAO airline, typerole, livery, title-prefix). A candidate no longer needs an exact ICAO type match to win a substitution - sharing class code + WTC can now outscore a same-class-but-wrong-WTC candidate, fixing cases like a Beechcraft Baron remote preferring an installed Diamond DA62 over an installed Douglas DC-3.
- **Real aircraft.cfg/livery.cfg data for FS2024.** SimConnect's `LIVERY FOLDER` is now read the moment a model is actually instantiated locally, parsing its real configuration file for `icao_type_designator`/`icao_WTC`/`icao_airline`/`atc_id` - the same reliability tier non-FS2024 builds already had from their upfront folder scan - ahead of the live category/engine-derived classification and title-guessing, both kept as fallbacks. Handles both same-package and cross-package `base_container` references.
- **Hardened title-guessing.** The fallback guesser that infers an ICAO type from a model's title text now requires a longer, less ambiguous match, fixing false positives like a Cessna Caravan livery for an operator named "Baron Aviation" being mistagged as a Beechcraft Baron.
- **Non-flyable models filtered out.** Scenery props, wrecked vehicles, and static display liveries (e.g. tents, cranes, parking-lot cars bundled with some add-ons) are now excluded from the installed-model list entirely via an expanded ban list, so they can no longer appear in the Substitute dialog or become an accidental default fallback.
- **Fine-grained matching defaults.** The "Model Matching" defaults now go beyond one fallback per coarse typerole (Rotorcraft/Airliner/etc.) to also auto-configure defaults per class code + WTC combination, wherever more than one installed candidate exists to choose between.
- **Network protocol extended** (`Sim.VERSION` 21006→21007): peers now also broadcast their own resolved class code/WTC directly, so receiving clients use the sender's best-available classification instead of each independently re-deriving it from ICAO type - which previously failed whenever ICAO type was a bogus/non-standard string. Purely additive; older peers are unaffected.
- **Explain Match dialog extended.** Shows per-attribute score contributions inline, an "other candidates considered" panel (what almost won and why), and a ban-list exclusion count - both in the dialog and the exported Markdown report.
- Fixed the Explain Match table always showing a blank matched registration, even when a registration match was the decisive signal.
- Fixed the Flight Plan dialog's ICAO type/airline fields (and what gets broadcast for your own aircraft) never being auto-filled from the simulator, unless a SimBrief import had already set them.
- Fixed `LIVERY FOLDER`-based config reading (see above) silently finding nothing for the many FS2024 add-ons that ship a single `aircraft.cfg` with no separate `livery.cfg` - these now resolve via a title-based lookup against your installed packages instead.
- Fixed a `base_container` resolution bug where a variation package's real config data could be discarded if that package had no `livery.cfg` of its own.
- Fixed the installed-package index used by the two fixes above missing every package when your configured simulator folder is the sim's base install directory rather than a `Community`-style folder directly - it now looks one level deeper to find the actual package folders.
- **ICAO type designator is now validated, not trusted blindly.** A config-confirmed `icao_type_designator` is checked against the official ICAO Doc8643 reference list; if it's missing or isn't a real designator (a real, confirmed add-on authoring mistake: `icao_type_designator="500E"`, not a real code, with the actual designator sitting in the separate `icao_model` field instead), JoinFS now tries the `icao_model` field, then a title-text guess corroborated by a class code/WTC derived from that same file's engine/category fields, before finally falling back to the raw value. Explain Match shows exactly what was declared and what was used instead, and why.

## Localization

- All new UI text above, plus existing untranslated text in the Explain Match/Model Matching dialogs and the SimBrief tooltips/status messages, is now localized into all 8 supported languages (German, Spanish, French, Italian, Korean, Dutch, Portuguese, Russian).

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
