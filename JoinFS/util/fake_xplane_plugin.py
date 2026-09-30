"""Minimal stand-in for the JoinFS-XP plugin, to smoke-test the X-Plane link over loopback.

Waits for JoinFS's CONNECT on the plugin port, then sends HEARTBEATs (which make JoinFS
connect) and one MODEL message listing a user aircraft. Prints what it receives.

Usage: python fake_xplane_plugin.py [seconds]   (then start an XPLANE/CONSOLE build with -xplane)
Expect "Connected to simulator", "X-Plane 12" and "Listing aircraft 'TEST1'" in the JoinFS log.
"""
import socket
import struct
import sys
import time

PLUGIN_PORT = 7472
DATA_VERSION = 21023
LEGACY_VERSION = 0x520B
HEARTBEAT, MODEL = 2, 3
RUN_SECONDS = float(sys.argv[1]) if len(sys.argv) > 1 else 12.0


def header():
    # version, flags, guaranteed id, segment index, segment count, sender (ip, port, local), recipient
    return struct.pack('<hBHBB', LEGACY_VERSION, 0, 0, 0, 1) + struct.pack('<IHB', 0, PLUGIN_PORT, 1) + bytes(7)


def fixed(text, length):
    data = text.encode('ascii')[:length]
    return data + bytes(length - len(data))


def heartbeat():
    return header() + struct.pack('<hB', DATA_VERSION, HEARTBEAT) + struct.pack('<hBB', 12, 1, 60)


def model():
    body = struct.pack('<BBBBB', 0, 1, 1, 1, 0)  # index, active, user, plane, livery
    body += fixed('Tester', 20) + fixed('TEST1', 40) + fixed('Aircraft/Laminar Research/Cessna 172/c172.acf', 256) + fixed('C172', 40)
    return header() + struct.pack('<hB', DATA_VERSION, MODEL) + body


sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(('127.0.0.1', PLUGIN_PORT))
sock.settimeout(0.2)
joinfs = None
end = time.time() + RUN_SECONDS
next_heartbeat = 0.0
received = {}
while time.time() < end:
    try:
        data, addr = sock.recvfrom(65536)
        if len(data) >= 24:
            msg_id = data[23]
            received[msg_id] = received.get(msg_id, 0) + 1
            if joinfs is None:
                print('first message from JoinFS at', addr, 'id', msg_id, flush=True)
            joinfs = addr
    except (socket.timeout, ConnectionResetError):
        # Windows reports an earlier ICMP port-unreachable as a reset on the next receive
        pass
    if joinfs and time.time() >= next_heartbeat:
        sock.sendto(heartbeat(), joinfs)
        sock.sendto(model(), joinfs)
        next_heartbeat = time.time() + 1.0
print('received message ids (id: count):', dict(sorted(received.items())), flush=True)
