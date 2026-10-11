# JFS2 recorder: where we are

Working notes for the recorder v2 project (branch `jfs2-recording`). Nothing is implemented yet.

| File | What |
|---|---|
| `recorder-v2-design.md` | The design: why, the options, the proposal. |
| `format-spec.md` | Byte-exact draft of the `.jfs2` format, edition 1. **Draft, not yet approved.** Appendix C lists the decisions it makes. |
| `tools/jfsstat.py` | Read-only statistics of legacy `.jfs` files (frame types, sizes, how much of each variable frame changes). |
| `tools/jfs2ref.py`, `tools/example.jfs2` | Reference writer for the spec's Appendix A example (221 bytes). The stage 1 golden test should pin these bytes. |

## Status (2026-10-11)

Design analysed against the code; format spec drafted; delivery planned in stages. Waiting for the
owner's answers (below). Then update the spec and the design's §11 Decisions table, and start stage 1.

## Open decisions

1. A yes or a change on each of the ten decisions in `format-spec.md` Appendix C.
2. **Project layout.** Recommended: a standalone format library (blocks, chunks, CRC, index, repair,
   no codec knowledge) in stage 1; the messages and codecs (`JoinFS/Net/Messages`,
   `JoinFS/Net/Protocols/Jfp2/Codecs`, `WireText`, `MessageClasses`, `NodeId`) extracted into a
   shared library as a separate prep refactor before stage 2, because the dump/convert tools and
   RecordingXRay need them and "no `#if` by construction" only holds for extracted code.
3. Later, not blocking: working-file location and crash-recovery prompt (stage 5), overdub merge on
   stop (design question 3), default metadata (question 4), what to do with an old `.jfs` that parses
   both ways under the FS2024 and non-FS2024 layouts (stage 4; recommended: ask the user).

Pending small spec fix: §3.1 should say that a Header whose metadata strings run past the end of the
block body is rejected.

## Plan

Each stage fits a fresh session (low-consumption workflow).

0. **Spec and decisions.** Approve the spec. Collect the `.jfs` corpus (see below).
1. **Format library.** New standalone project: writer, reader, CRC-32, repair, `dump`. Opaque
   records. Tests: round trip, truncation and corruption fuzzing, golden bytes from `tools/example.jfs2`.
2. **Tracer bullet.** Codec binding for `track-open`, `identity`, `position`; record your own
   aircraft to `.jfs2` and play it back in the simulator. The old recorder stays in place.
3. **Widen.** Remaining kinds; the track-state tracker (needed for keyframes and for Trim);
   keyframes, index, Jump; variable-change capture; a reader prefetch thread so interpolation can look
   ahead without blocking the sim thread.
4. **Importer.** Move the old reader into a frozen module; corpus tests; open by magic; the command-line
   tool; move RecordingXRay onto the new libraries.
5. **Editing and switch-over.** Trim, Append, Overdub, Remove, Skip on the new engine; both UIs
   (WinForms `MainForm`/`RecorderForm`, Avalonia `LiveRecorderSource`/`RecorderViewModel`), dialogs,
   resx strings, hotkeys, `-play`, working file and crash recovery; delete the old write path.
6. **Measured options.** Compression, recording rate, `PositionV2`, new kinds.

## What the code check found (beyond the design)

- Redundant `ObjectId`/`NetTime` in the `PositionV1` body go stale after Append, Trim or track
  renumbering; the spec makes the record header authoritative (§5).
- `PositionV1` quantises rudder, elevator, aileron and both brakes to 1/16384; the importer's
  "same values" acceptance test needs a tolerance on those five.
- Keyframes need a codec-aware track-state tracker above the format library (merged variables, latest
  identity and position); Trim needs it to synthesise a snapshot at the cut.
- `track-open`, `track-close` and `object-position` have no canonical message or JFP2 class; the spec
  keeps them private to the recorder.
- Capture sites today: `Sim.AircraftUpdate.cs:634`, `Sim.SimConnect.cs:502,712`, `Sim.cs:1120-1122`,
  `Session/MainSessionHost.cs:170-235`. They pass `Sim` structs, not canonical messages.
- The design cites `docs/reference/jfp2-protocol.md`; the file is `docs/jfp2/protocol.md`.

## Corpus findings (the tiger recordings, 12 files, 0.2 to 29 MB)

The files are kept outside the repo (`C:\Users\crist\Downloads\tiger recordings`). Measured with
`tools/jfsstat.py`:

- All are FS2024 layout (three trailing strings), file versions 21005 or 21007; none is 21008, so none
  has `StaticCgToGround`. All objects are aircraft. No non-FS2024 variant file is in the corpus, so
  the build-variant ambiguity (recording-protocol.md §7.1) is untested on real data.
- Bytes by frame type: integer variables 45 to 58%, float variables 18 to 22%, string variables about
  2%, aircraft positions 19 to 31%. Positions run at about 16 Hz and variable sets at about 8 Hz per
  aircraft.
- Between consecutive variable frames about 1% of the integer entries change (5% in one file);
  floats change 28 to 36% in the 21005 files and about 1% in the 21007 files; strings almost never.
  Writing only the changes would shrink a file by roughly 3 to 4 times (estimate, before
  compression), which is why `variables` records are upserts.

For the importer's corpus, still needed: recordings from FSX, P3D, FS2020 or X-Plane builds, an
overdubbed one, an appended one, and one with non-aircraft objects.
