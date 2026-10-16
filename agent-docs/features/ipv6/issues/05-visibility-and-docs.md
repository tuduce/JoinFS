# Visibility and docs

Status: resolved
Blocked by: 04

Spec: [../spec.md](../spec.md), build-order slice 5. Mechanism: `docs/jfp2/design-future.md` §4.1.
Read the spec's "Further Notes" (pressure points to verify) and "Testing Decisions" before starting.

Family in the peer snapshot, shown in the WinForms and Avalonia session views next to the routed/direct and protocol labels; `NETWORK:` log lines for dial choice, path upgrade and fallback; docs listed under the spec's Implementation Decisions (`joinfs-architecture.md`, `network-plugin-architecture.md` §4, roadmap I1, README Docker note; protocol edition 2 NOT marked released).

## Answer

Done on branch `ipv6-05-visibility`.

- Snapshot: `PeerSnapshot.Family` ("IPv6"/"IPv4", the family of `RouteEndPoint`, so it follows path moves and names the first hop of a relayed peer), from the pure `PathFamily.Label` (`PathFamilyTests`).
- Session views: an "IP" column in WinForms (`ColFamily`, last column, hidden when the node is not dual-stack, sortable with IPv6 first) and Avalonia (`PeerInfo.Family`, `SessionViewModel.Family`, cell and screen-reader text); empty on an IPv4-only node, so those users see no change. Verified by building only, as the spec says.
- Log: the dial choice, `Dial` timeout, path upgrade, fallback and candidates lines already existed (slices 2-4). Added `NETWORK: Dial order ...` (when more than one address) and `NETWORK: Dial <ep> answered over <family> (n of m)`.
- Settings: IPv6 on/off in both UIs (Avalonia Network section `UseIpv6`, text "Use IPv6 (restart to apply)" translated in the 8 languages; WinForms `Check_Ipv6` made in code next to Low bandwidth, tooltip from the same text). It writes `Settings.Ipv6` and takes effect at the next start (the socket is opened with the value read at startup; `main.settingsIpv6` is this run's value). `--noipv6` is in `-help` and in the WinForms command-line list.
- Docs: `joinfs-architecture.md` new §5.8 and the snapshot bullet, `network-plugin-architecture.md` §4 item 10, `roadmap.md` (I1 built, awaiting field test and edition 2 release; D3 fixed), README Docker/IPv6 note. Protocol edition 2 is not marked released.
- Tests: FS2024-Debug x64 1379 pass, `JoinFS.UI.Tests` 506 pass (including the localization checks); CONSOLE, FS2020 and XPLANE build. No fixture touched.
