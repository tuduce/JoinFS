### The JoinFS site is online. Please visit [https://joinfs.net](https://joinfs.net) for all the information about the project.

> [!IMPORTANT]
> The X-Plane variant of joinfs is no longer joinfs-ing, and we need help to get it back on track.
>
> We’re looking for a developer with experience using the X-Plane SDK who’s willing to donate a bit of time to investigate, debug, and help restore functionality. Even small contributions (testing, insights, or pointers) would make a big difference.
> 
> If you care about keeping the X-Plane community connected and thriving, your help would be greatly appreciated.
> Please comment on [issue #53](https://github.com/tuduce/JoinFS/issues/53), open a PR, or reach out if you’re interested in collaborating.
> 
> Thank you!

This fork continues the development of the great [JoinFS](https://joinfs.net/) utility.

JoinFS is an advanced multiplayer client for flight simulators including Microsoft Flight Simulator 2020, FSX, X-Plane and Prepar3D. Allows players to fly together across different simulators.

## Installation
* Download the specific installer for your simulator. The installer is provided on the [Releases](https://github.com/tuduce/JoinFS/releases) page.
* Run the installer
* Run the JoinFS utility

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

# run console
```
dotnet .\JoinFS\bin\CONSOLE\net8.0\JoinFS-CONSOLE.dll --create --hub --hubname "FSC e.V. Test" --nosim --nogui --background --whazzup-public --websocket --websocketport 8765 --comswebhookuri http://localhost/tsapi/usertochannel 
```

## WebSocket Server

The CONSOLE build includes an integrated WebSocket server that broadcasts live aircraft state to any connected client whenever something changes.

### CLI parameters

| Parameter | Default | Description |
|---|---|---|
| `--websocket` | *(disabled)* | Enable the WebSocket server |
| `--websocketport <port>` | `8765` | Port to listen on |
| `--websocketlog` | *(disabled)* | Log connection events, sent messages, and webhook calls to the monitor |

Connect to `ws://<host>:<port>/ws/`. The server tries to bind on all interfaces; if that requires elevated permissions on Windows, it falls back to `localhost` only. To allow remote access without running as admin:

```
netsh http add urlacl url=http://+:8765/ws/ user=Everyone
```

### Message format — `aircraft_update`

Sent whenever one or more aircraft change state (position, frequencies, lights, engines, etc.). Also sent once on first appearance. Sources are locally-connected sim aircraft and, when `--whazzup-public` is active, global hub users.

```json
{
  "type": "aircraft_update",
  "aircraft": [
    {
      "callsign": "DAL123",
      "nickname": "PlayerName",
      "guid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "latitude": 51.476852,
      "longitude": -0.461111,
      "altitude": 35000,
      "speed": 450.3,
      "heading": 270,
      "com1": "121.500",
      "com2": "119.100",
      "squawk": "2000",
      "icaoType": "B738",
      "from": "EGLL",
      "to": "KJFK",
      "rules": "IFR",
      "route": "BCN UL9 ...",
      "remarks": "",
      "gear": 0,
      "flaps": 0.000,
      "lights": {
        "nav": 1,
        "beacon": 1,
        "landing": 0,
        "taxi": 0,
        "strobe": 1
      },
      "engines": {
        "eng1Running": true,
        "eng2Running": true,
        "eng3Running": false,
        "eng4Running": false
      },
      "rotorRpm": 0.0
    }
  ]
}
```

#### Field reference

| Field | Type | Unit / notes |
|---|---|---|
| `callsign` | string | ATC callsign from flight plan |
| `nickname` | string | JoinFS display name of the pilot |
| `guid` | string | Stable UUID identifying this pilot session |
| `latitude` | number | Degrees, 6 decimal places |
| `longitude` | number | Degrees, 6 decimal places |
| `altitude` | number | Feet MSL, rounded to the nearest foot |
| `speed` | number | Knots (ground speed), 1 decimal place |
| `heading` | integer | Degrees magnetic, 0–359 |
| `com1` / `com2` | string | Active COM frequency, e.g. `"121.500"` |
| `squawk` | string | Transponder code, e.g. `"2000"` |
| `icaoType` | string | ICAO aircraft type designator, e.g. `"B738"` |
| `from` / `to` | string | Departure / destination ICAO |
| `rules` | string | `"IFR"` or `"VFR"` |
| `route` / `remarks` | string | Flight plan route and remarks |
| `gear` | integer | `1` = down/locked, `0` = up |
| `flaps` | number | Left trailing-edge flaps, 0.0–1.0 |
| `lights.nav` | integer | `1` = on, `0` = off |
| `lights.beacon` | integer | `1` = on, `0` = off |
| `lights.landing` | integer | `1` = on, `0` = off |
| `lights.taxi` | integer | `1` = on, `0` = off |
| `lights.strobe` | integer | `1` = on, `0` = off |
| `engines.eng1Running`–`eng4Running` | boolean | Whether each engine is running |
| `rotorRpm` | number | Rotor RPM (helicopters), 1 decimal place |

---

## COM Frequency Webhook

The CONSOLE build can call an HTTP endpoint whenever a pilot changes their active COM1 or COM2 frequency. This is useful for integrating with TeamSpeak bots, Discord bots, or any other kind of radio-switching automation.

### CLI parameters

| Parameter | Default | Description |
|---|---|---|
| `--comswebhookuri <uri>` | *(disabled)* | URI to call on COM frequency changes (enables the webhook) |
| `--comswebhookmethod <method>` | `PUT` | HTTP method: `POST`, `PUT`, `PATCH`, or `GET` |
| `--websocketlog` | *(disabled)* | Log the outgoing request body and HTTP response code |

### Payload format

The body is sent as `application/json`. Multiple aircraft whose frequencies changed in the same polling cycle are batched into a single request.

```json
{
  "comsupdate": [
    {
      "callsign": "DAL123",
      "nickname": "PlayerName",
      "com1": "121.500",
      "com2": "119.100"
    }
  ]
}
```

#### Field reference

| Field | Type | Description |
|---|---|---|
| `callsign` | string | ATC callsign from flight plan |
| `nickname` | string | JoinFS display name of the pilot |
| `com1` | string | New active COM1 frequency, e.g. `"121.500"` |
| `com2` | string | New active COM2 frequency, e.g. `"119.100"` |

The webhook fires only on actual frequency **changes** — the first-seen state for each aircraft is recorded silently and no initial call is made.
