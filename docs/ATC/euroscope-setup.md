# Connecting EuroScope to JoinFS

**Status (stage 2, 2026-11-20): written from the code of `joinfs-euroscope-bridge`, not yet checked against EuroScope.**
The stage's check (EuroScope stays connected for 10 minutes) is the owner's evening test. Section 4 says what to
change if it fails, and section 5 is where to record what was found.

## 1. Turn the listener on

The listener is off by default and nothing listens while it is off (decision D3). Turn it on in **Settings → ATC**
(tick "Enable ATC client connection"; the port is 6809 unless you change it). It starts at once and the card says
"Listening on port 6809", then "Connected as EDDM_TWR 118.700" once EuroScope has logged in. Unticking it closes the port
and disconnects EuroScope. The choice is saved and survives a restart.

Or turn it on for one run only:

```
JoinFS-FS2024.exe -fsd            (port 6809)
JoinFS-FS2024.exe -fsd 6810       (another port)
```

or set `AtcFsd` (and `AtcFsdPort`) in the user settings. On the CONSOLE build, which has no window, use
`--fsd`. The monitor and the log show:

```
Listening for ATC clients on 127.0.0.1 and ::1 port 6809
ATC client connected from 127.0.0.1
ATC station EDDM_TWR logged in
ATC station EDDM_TWR is Tower on 118.700
ATC station EDDM_TWR logged off (connection closed)
ATC client from 127.0.0.1 refused: <why>
```

It listens on the loopback addresses only (D4): a client on another PC cannot connect.

## 2. EuroScope

The bridge's README sets EuroScope up as a connection to a private server, not to VATSIM: in the connection settings
choose the direct connection and leave the VATSIM option off, set the server to `127.0.0.1` (port 6809 is the default),
and log in with a callsign such as `EDDM_TWR`. CID, password and rating are not checked by JoinFS (the rating is taken as
sent), so any values do.

One station per JoinFS instance: a second login is refused with an `$ER`. The callsign is the whole callsign the client
sends: 3 to 12 characters of `A-Z 0-9 _`, so `EDDM_TWR`, `EDDM_N_TWR` and `EDMM_ALB_CTR` all log in.

## 3. What JoinFS says to the client

Found in the bridge (`src/fsdServer.js`); the aim is the same observable behaviour, written independently.

| When | JoinFS sends |
|---|---|
| connect | **nothing.** The bridge sends no `$DI` banner: its source says EuroScope rejects the connection when the server announces itself as "VATSIM FSD". The client speaks first (`$ID`, then `#AA`). |
| after `#AA` | `#TMserver:<cs>:<welcome>`, then `$CQSERVER:<cs>:CAPS`, `$CRSERVER:<cs>:ATC:Y:<cs>` (`N` for an observer), `$CRSERVER:<cs>:CAPS:ATCINFO=1:SECPOS=1` and `$CRSERVER:<cs>:IP:<address>`. The bridge's author guesses EuroScope waits for this whole sequence, the IP line in particular. |
| every 30 s | `#DLSERVER:*:0:0`, as the bridge does |
| `$PI` to `SERVER` | `$PO` with the same timestamp |
| `$CQ ...:SERVER:ATC:<cs>` | `$CR` with `Y` for the logged-in station when it is a controller (facility is not observer), otherwise `N` |
| `$CQ ...:SERVER:IP` | `$CR` with the client's address |
| `$CQ ...:SERVER:FP:<aircraft>` | `$ER` 008 "No flight plan" (aircraft come in stage 4) |

Lines end with `CR LF`; JoinFS accepts `LF` alone. A line over 2,048 bytes, or any verb before the login other than
`$ID` and `#AA`, closes the connection. `%` updates the station (frequency, facility, visibility, position); `#DA`, or
closing the connection, logs it off. No `$ZC` challenge is sent.

## 4. If EuroScope does not stay connected

In the order of likelihood:

1. **The greeting.** The plan (§4.2) first said to send `$DI` on connect. Stage 2 follows the bridge and sends none. If
   EuroScope sits at "connecting" forever, add `$DISERVER:CLIENT:<text>:` as the first line, in
   `AtcGateway.OnConnected` (`FsdServerId` in `JoinFS.Fsd` builds it), with a text that does not say VATSIM.
2. **The post-login sequence** of §3: compare with the bridge's `buildPostLoginCapsExchange`.
3. **The keep-alive**: leave `#DL` out, or send it more often.

Use `JoinFS/util/fake_fsd_client.py` to see every line the server sends without EuroScope, and EuroScope's own message
log (Settings, Message log) for what it received and rejected.

## 5. Found to work

*(To fill in after the first check with EuroScope: the version, the connection settings that worked, and whether the
greeting question of §4 had to change.)*
