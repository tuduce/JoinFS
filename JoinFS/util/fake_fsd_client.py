#!/usr/bin/env python3
"""A stand-in for EuroScope: connects to the FSD listener of JoinFS (docs/ATC/atc-fsd-plan.md, stage 2),
logs in as a controller station, sends the % position line every 15 s, prints every line it receives and
sends every line typed on standard input.

    python fake_fsd_client.py --callsign EDDM_TWR --freq 118.700 --facility 4
    python fake_fsd_client.py --port 6810 --callsign EDDM_N_TWR --name "Carla"

Lines typed on standard input are sent as they are (a line ending is added), for example
    $PIEDDM_TWR:SERVER:123
    $CQEDDM_TWR:SERVER:ATC:EDDM_TWR
    #TMEDDM_TWR:@49999:hello
Type "quit" (or press Ctrl-C) to log off with #DA and leave. The standard library is all it needs.
"""
import argparse
import socket
import sys
import threading
import time

POSITION_INTERVAL_S = 15.0


def frequency_field(mhz: float) -> str:
    """118.700 MHz is written 18700 on the wire."""
    return str(int(round(mhz * 1000)) - 100000)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=6809)
    parser.add_argument("--callsign", default="EDDM_TWR")
    parser.add_argument("--name", default="Fake Controller")
    parser.add_argument("--rating", type=int, default=4, help="1 is an observer")
    parser.add_argument("--facility", type=int, default=4, help="0 OBS, 1 FSS, 2 DEL, 3 GND, 4 TWR, 5 APP, 6 CTR")
    parser.add_argument("--freq", type=float, default=118.700, help="MHz")
    parser.add_argument("--range", type=int, default=50, help="visibility, nautical miles")
    parser.add_argument("--lat", type=float, default=48.35377)
    parser.add_argument("--lon", type=float, default=11.78609)
    parser.add_argument("--no-id", action="store_true", help="do not send $ID before #AA")
    args = parser.parse_args()

    try:
        sock = socket.create_connection((args.host, args.port), timeout=5)
    except OSError as error:
        print(f"cannot connect to {args.host}:{args.port}: {error}", file=sys.stderr)
        return 1
    sock.settimeout(None)
    send_lock = threading.Lock()
    stop = threading.Event()

    def send(line: str) -> None:
        print(f">> {line}", flush=True)
        with send_lock:
            sock.sendall((line + "\r\n").encode("latin-1"))

    def receive() -> None:
        buffer = b""
        while not stop.is_set():
            try:
                data = sock.recv(4096)
            except OSError:
                break
            if not data:
                break
            buffer += data
            while b"\n" in buffer:
                raw, buffer = buffer.split(b"\n", 1)
                print(f"<< {raw.decode('latin-1').rstrip(chr(13))}", flush=True)
        print("-- connection closed by the server", flush=True)
        stop.set()

    threading.Thread(target=receive, daemon=True).start()

    if not args.no_id:
        send(f"$ID{args.callsign}:SERVER:ffff:fake_fsd_client:1:0:0:0:0")
    send(f"#AA{args.callsign}:SERVER:{args.name}:1234567::{args.rating}:100")

    def position() -> str:
        return (f"%{args.callsign}:{frequency_field(args.freq)}:{args.facility}:{args.range}:{args.rating}:"
                f"{args.lat:.5f}:{args.lon:.5f}:0")

    def positions() -> None:
        while not stop.wait(POSITION_INTERVAL_S):
            try:
                send(position())
            except OSError:
                return

    send(position())
    threading.Thread(target=positions, daemon=True).start()

    try:
        for typed in sys.stdin:
            typed = typed.rstrip("\r\n")
            if typed.lower() == "quit":
                break
            if typed and not stop.is_set():
                send(typed)
        else:
            # standard input ended (no terminal): stay connected until the server or Ctrl-C ends it
            stop.wait()
    except KeyboardInterrupt:
        pass

    if not stop.is_set():
        try:
            send(f"#DA{args.callsign}:1234567")
        except OSError:
            pass
    stop.set()
    sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
