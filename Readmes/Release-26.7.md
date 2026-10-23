## New Features

- JFP2, the protocol introduced in 26.6, now stands on its own. A JoinFS node can create a session, join one, stay in it, leave it, use the hub list and be relayed without a single message of the original protocol. Joining tries JFP2 first and falls back to the original protocol after 2 seconds of silence, so joining a hub of an older version costs two seconds once; JoinFS remembers which hubs need the original protocol, also across restarts (`legacyonly.txt` in the JoinFS data folder). Sessions with older versions keep working: a hub of this version relays between the two, so pilots on 26.6 or earlier and pilots on this version see each other.
- IPv6. JoinFS opens one socket for IPv4 and IPv6. Between two instances of this version the connection uses IPv6 whenever both have it, falls back to IPv4 within 15 seconds when IPv6 stops working, and tries IPv6 again later without flipping back and forth. Addresses can be typed as a host name, `a.b.c.d:port`, `[ipv6]:port` or a bare IPv6 address; a host name with both an A and an AAAA record is tried on both. The Protocol column of the session window reads "JFP2 (IPv6)" or "JFP2 (IPv4)". Pilots on older versions are always reached over IPv4.
- Pilots with IPv6 only. An instance with no IPv4 address can fly in a session with everyone, including pilots on older versions: the hub that admits it gives it a name those versions can address, and relays for it.
- The hub list over IPv6: hubs are listed with their IPv6 addresses, preferred when you have IPv6. A hub you cannot reach because it has no address in a family you use is shown as "Unreachable from here" instead of being hidden. The ban list accepts IPv6 addresses (one /64 per entry).
- Each instance has a key pair, kept in the JoinFS data folder (`node-N.key`), and a name derived from it, so an instance keeps its identity across restarts and two instances on one PC have different ones. Nothing is encrypted or signed yet. Docker hubs should keep the data folder in a volume.
- The password of a session is proved over JFP2 without sending its hash.
- Chat history, the remove-object message, the cockpit-sharing and ATC information and the other messages that only the original protocol carried now also travel over JFP2; the chat history of a late joiner can be larger than one datagram and is sent in pieces.
- JFP2-only hubs: a hub started with the `Jfp2Only` setting or `-jfp2only` speaks no original protocol at all. Pilots on older versions cannot join it and do not see it in their hub list. This is how the original protocol will be retired, hub by hub.
- The Wireshark dissector decodes every JFP2 message of this version, including the join, the directory and the chat history.
- The JFP2 specification is now edition 2 (`docs/network/jfp2/protocol.md`).

## Command-line options

- `-noipv6`: do not use IPv6 in this run (the IPv6 setting is stored; there is no control for it in the settings window yet).
- `-noipv4`: behave as a pilot with no IPv4 address, for testing (never stored).
- `-legacyjoin`: join with the original protocol only, as in 26.6 (never stored).
- `-jfp2only`: speak JFP2 only in this run.

## Limitations

- A hub that requires an email and password to join cannot be joined over JFP2: a pilot with IPv4 logs in over the original protocol as before; a pilot with no IPv4 address cannot use such a hub.
- The `NNNNN-NNNNN` form that hides an address in the log and the session window works for IPv4 only; an IPv6 address is shown as text.
- Nothing is encrypted: JFP2 has no more protection than the original protocol.

## Installation

Please follow the instructions for your simulator, as for 26.6.
