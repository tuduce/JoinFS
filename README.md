JoinFS is an advanced multiplayer client for flight simulators including Microsoft Flight Simulator 2020, FSX, X-Plane and Prepar3D. Allows players to fly together across different simulators.

## Installation
* Download the specific installer for your simulator. The installer is provided on the [Releases](https://github.com/tuduce/JoinFS/releases) page.
* Run the installer
* Run the JoinFS utility

On first run, JoinFS tries to auto-detect your simulator's install folder from the
simulator's own records (MSFS's `UserCfg.opt`, X-Plane's `x-plane_install_*.txt`, or the
FSX/Prepar3D registry keys), so you don't have to browse for it yourself. If it can't be
found, you'll be asked to pick it once, alongside setting your nickname and (optionally)
your SimBrief username.

How the model set is then obtained differs by simulator:
* **FSX / Prepar3D / X-Plane** - model matching is a folder scan (`aircraft.cfg`/`.acf`
  files), so once the folder is known (detected or picked), the scan runs immediately -
  no need to open the sim first.
* **MSFS 2020** - also a folder scan, but the add-ons list it also reads is only
  available once you've actually connected to the sim, so the scan itself runs at that
  point (automatically, same as today).
* **MSFS 2024** - fetches its community model set by asking the running sim directly,
  so a folder is saved right away but the model list can only be completed once you've
  connected to the sim.

OR
### If you want to run it as unattended Hub on Docker
the docker image is worth a look: https://hub.docker.com/r/joinfs/joinfs-console

## Building from source
To build JoinFS from source, please follow these steps:
* Clone this repository
* Open a command/terminal window (cmd.exe in Windows)
* Change directory into the repository folder
* Build JoinFS with the command
  ```
  dotnet build .\JoinFS\JoinFS.cproj -c CONFIGURATION
  ```
  where CONFIGURATION is one of: FS2024, FS2020, FSX, P3D, XPLANE, CONSOLE

## Changes
The [Releases](https://github.com/tuduce/JoinFS/releases) page will offer a description of the changes each release brings.

## Original README
The original README can be found [here](ORIGINAL_README.md).

## Disclaimer

This SOFTWARE is provided "as is" and without warranties as to performance of merchantability or any other warranties whether expressed or implied. Because of the various hardware and software environments into which the SOFTWARE may be put, no warranty of fitness for a particular purpose is offered.

To the maximum extent permitted by applicable law, in no event shall the author be liable for any damages whatsoever (including without limitation, direct or indirect damages for personal injury, loss of profit, business interruption, loss of information, or any other pecuniary loss) arising out of the use, or inability to use this SOFTWARE, even if the author has been advised of the possibility of such damages.

You are solely responsible for all costs and expenses associated with rectification, repair or damage caused by such errors.

You must assume the entire risk of using the SOFTWARE.

