# JFS2 recorder: where we are

Working notes for the recorder v2 project (branch `jfs2-recording`): what the code does today, what it does not do yet, and what comes
next. The design is `recorder-v2-design.md`, the bytes are `format-spec.md`; this file is the only place that says how far the code has got.

| File | What |
|---|---|
| `recorder-v2-design.md` | The design: why, the options, the proposal, the decisions. |
| `format-spec.md` | Byte-exact specification of the `.jfs2` format, edition 1, approved 2026-10-12. Appendix C lists the decisions it makes. |
| `tools/jfsstat.py` | Read-only statistics of legacy `.jfs` files (frame types, sizes, how much of each variable frame changes). |
| `tools/jfs2ref.py`, `tools/example.jfs2` | Reference writer for the spec's Appendix A example (221 bytes). The format library's golden test pins these bytes. |

## Status (2026-10-15)

The app records, saves, opens, plays and recovers `.jfs2` files, and opens old `.jfs` files through the importer. It no longer writes
`.jfs`. Playback and editing still run on the old in-memory model (`Recorder.Obj`, a frame list per object); the live JFS2 capture is a
crash-recovery journal, and Save exports the in-memory objects. Replacing that model is the one piece of design work left ("Next").

Checked in a simulator: stage 2 playback (2026-10-12); gear and lights record and play (2026-10-14). Everything after that is checked by
unit tests only: keyframes, identity changes, boats and vehicles, Save as `.jfs2`, the working file and recovery, the importer in the app.

Tests: `JoinFS.Recording.Format.Tests` 61, `JoinFS.Tests` 1196 (the recording ones are in `Recording/` and `RecorderAutoSaveTests`),
`RecordingXRay.Tests` 206, `JoinFS.UI.Tests` 506 (the recorder tab in `RecorderTests`).

## The code

| Where | What | Knows |
|---|---|---|
| `JoinFS.Recording.Format/` | The container (spec 1 to 6, 8, 9): `RecordingWriter`, `RecordingReader`, `RecordingRepair`, `RecordingDump`, CRC-32, Deflate. Records are opaque (kind id, track, time, bytes). | nothing else; no build symbols |
| `JoinFS.Messages/` | The canonical messages and the JFP2 body codecs, shared by the network and the recorder (design 4.4). | nothing else; no build symbols |
| `JoinFS.Recording/` | The engine pieces that know the record kinds but not the simulator: `Jfs2Kinds` (names, versions, `TrackOpenV1`), `ObjectPositionV1Codec`, `Jfs2Capture` (writer), `Jfs2Scene` (reader into tracks of canonical messages), `Jfs2SceneWriter` (scene to file), `LegacyJfsImporter` (old `.jfs` to scene). | the two libraries above; no build symbols |
| `JoinFS/Recording/` | The app's side: `Jfs2Loader` (scene to `Recorder.Obj`), `Jfs2Exporter` (`Recorder.Obj` to scene to file), `Jfs2Convert` (`JoinFS -convert`). | `Recorder`, `Sim` types |
| `JoinFS/Recorder.cs` | Recording, playback, editing (in memory), the live capture, Save, the working file. | everything |
| `JoinFS.Recording.Tool/` | `jfs2 dump [--records] f`, `jfs2 verify f`, `jfs2 repair f [-o out]`. | the format library only |
| `RecordingXRay/` | Opens `.jfs2` through `Jfs2Scene`; keeps its own `.jfs` reader on purpose (it shows what the old file stores, frame by frame). | `JoinFS.Recording` |

Format library notes:

- The writer enforces the track lifecycle and time rules of spec 5; `Finish` closes tracks still open. The caller owns the chunking policy
  and builds snapshots, because that needs codec knowledge. Not thread-safe.
- The reader opens finished and unfinished files alike. The Index does not list Schema blocks, so opening a finished file walks the block
  heads to find them (spec 8.1).
- A scan of an unfinished file cannot know callsigns and models (it has no codec), so a repaired file's Index lists them empty.

## How a recording flows today

- **Record.** `Recorder.StartRecord` starts `Jfs2Capture` on `<documents>/Working/<date>_<time>_recording.jfs2` (unless `-no-jfs2`) and
  keeps filling the in-memory `Recorder.Obj` frames as before. Every recorded object gets a track; positions, variables (changes only,
  spec 7.6), events and, for our own and simulator objects, identity changes go to the file. A chunk ends every 2 s and every fifth is a
  keyframe with the snapshot of every open track (spec 6.1). An overdub is not captured.
- **Stop.** The capture finishes the file (Index, Footer). A take with no position is deleted at once.
- **Save and auto-save.** `Recorder.SaveJfs2` runs `Jfs2Exporter` over a copy of the in-memory objects into `<path>.tmp` and moves it into
  place, then the working file is deleted. The saved file therefore has what the in-memory model has: one identity per object, but each
  object's owner, source application and simulator (from the track-open it was loaded with, or from the live recording), and the header's
  `sim` when all objects share one.
- **Open, Add, `-play`.** `Recorder.Parse` sniffs the first two bytes (`Jfs2.LegacyVersionSignature`): a `.jfs2` goes through `Jfs2Scene`,
  an old `.jfs` through `LegacyJfsImporter`; both end in `Jfs2Loader.Objects`. Damage is reported to the user in the Avalonia UI.
- **Recovery.** At start-up the Avalonia UI offers each leftover file in `Working/` (newest first): Recover, Delete, or later. A file another
  JoinFS is writing is skipped. A recovered recording is unsaved until saved, which removes the leftover. The WinForms UI neither asks nor
  offers; its leftovers are offered by the Avalonia UI at the next start.
- **Reading damaged files** (`Jfs2Scene`, spec 8.2). After a lost chunk, or a chunk whose records stop parsing, nothing is applied until the
  next keyframe, whose snapshot restores the state. A record that does not decode marks its track unreliable, and the next keyframe's
  snapshot heals that track alone; a track whose track-open or identity was never read is healed the same way.
- **Old files.** The importer parses both layouts of the trailing strings and keeps the one that validates; when both do it takes FS2024 and
  warns. `JoinFS -convert a.jfs ...` writes `a.jfs2` beside each file, never overwriting.

## Known limits and deviations

From the design or the spec, all known, none silent:

1. **Two writers of one recording.** The capture writes the working file, Save exports the in-memory objects again. What only the capture
   has is lost on Save: identity changes during a track. This goes with "Next".
2. **Identity changes have no effect yet.** They are written to the working file only, the scene keeps one identity per track (it logs how
   many it ignored), and changes of network aircraft are not captured (`Recorder.RecordIdentity` is called on the local send paths only).
3. **Jump does not restore variables** (gear, flaps, lights stay as they were before the jump). Design 5.4 promises it; it comes with the
   player.
4. **Everything is loaded whole.** A one-hour recording with ten aircraft is about 80 MB of file. Design 5.5's chunk-by-chunk player is a
   stage 6 measurement.
5. **Chunks are written on the sim thread** (about 8 KB every 2 s) and flushed to the operating system, not to disk, until the file is
   finished. Spec 9.1 asks for a flush to disk every few seconds (SHOULD); design 5.5 wants a writer thread.
6. **Two time bases in the working file.** Positions carry the sample time, variables and events the recorder's time. A per-track time may
   not go back (spec 5), so the capture holds a late position at the time of the record before it; for network aircraft, network jitter can
   shift position times by tens of milliseconds in a recovered file. Saved files are not affected (the exporter sorts by time). Must be
   fixed before the working file becomes the saved file.
7. **No `track-close` when an object leaves**; tracks close when recording stops. Until then every keyframe repeats the snapshot of objects
   that have gone.
8. **`variable-names.txt` is not written** (spec decision 9). RecordingXRay names variables from its own tables.
9. **ICAO type and airline are cut to 8 bytes** by `IdentityV1Codec` (the JFP2 limit, `docs/jfp2/protocol.md` 9.7). Real designators fit.
   Old FS2024 recordings keep SimConnect's raw `ATC MODEL` string there (for example `ATCCOM.AC_MODEL TIGER MOTH.0.tex`), which becomes
   `ATCCOM.A`. Whether model matching ever got a usable hint from the long string is unchecked; if it did, the importer should clean the
   string instead (`Sim.SimConnect.cs` `ResolveObjectInfoType` strips the same junk from live objects).
10. **`convert` lives in the app** (`JoinFS -convert`), not in the `jfs2` tool as design section 8 says: it was built while the importer still
    lived in the app. The importer is standalone now, so it could move.
11. **The WinForms recorder** does not show damage warnings, does not ask about unsaved recordings, and does not offer recovery. It goes away
    with the WinForms UI.

## Open decisions

- Design question 2 (JFP2 adopting `object-position`), 3 (overdub: merge on stop or in the player), 4 (default metadata).
- Whether to drop identity-change capture or apply it in the player (limit 2). The player plan below assumes apply.

## Next: a player on the scene (stage 5, remaining)

Replace `Recorder.Obj` playback and editing with a player that works on `Jfs2Scene` tracks of canonical messages, so the file the capture
writes is the recording. Suggested slices, each leaving a working recorder, one fresh session each:

1. **Time base** (limit 6): stamp variables and events of an object on the same time base as its positions, so the working file needs no
   clamping. Prerequisite for slice 4.
2. **Player on the scene.** `Recorder` holds a scene instead of `List<Obj>`; play, pause, loop, skip and both UIs (through `Recorder`'s
   existing surface) work on it. The scene gains identity frames, and the player applies them. Jump folds the frames up to T (latest
   position before T, merged variables, latest identity), which removes limit 3. `Jfs2Loader` and the `Recorder.Obj` frame classes go.
3. **Editing on the scene.** Trim start and end, remove, skip, append (time offset, renumbered tracks) and overdub (record new tracks while
   playing, merge on stop as design 7 proposes) change the in-memory scene; Save writes it with `Jfs2SceneWriter`. A trim at the start puts
   the state at the cut first, as a keyframe would.
4. **The working file is the recording.** After Stop, the recorder loads its own working file as the scene. Save of a recording that was not
   edited finishes the file and moves it into place (design 7); an edited one is written by the scene writer. `Jfs2Exporter` goes. Add
   `track-close` when an object leaves (limit 7) and `variable-names.txt` on stop (limit 8).

Stage 6 then measures load time and memory of long recordings to decide on the chunk-by-chunk player (limit 4), and the other measured
options (compression, recording rate, `PositionV2`).

## History

Stages as planned in design section 9; dates are 2026-10.

| Stage | Commits | What |
|---|---|---|
| 0 spec | `b66ef34`, `232c41c` | Design, format spec, corpus statistics; spec approved 12th. |
| 1 format library | `232c41c` | `JoinFS.Recording.Format` and its tests. |
| prep | `27736ab`, `3f512f6` | Messages and JFP2 codecs moved into `JoinFS.Messages`. |
| 2 tracer bullet | `4598072` | Capture and loader of aircraft identity and position, in `JoinFS/Recording/`. |
| 3 widen | `1300f8f`, `91d66fa`, `3df6a3a`, `3dfd8e3` | Variables and events, object-position, keyframes and restore, identity changes. The chunk-by-chunk player (3d) moved to stage 5, then to stage 6. |
| 4 importer and tools | `7e915f7`, `5bc464b`, `d3238e1`, `cf415f3`, `2ae203f` | First done with the old in-app reader as the importer (`7e915f7`), then redone: the engine moved into `JoinFS.Recording` (`d3238e1`, the loader became `Jfs2Scene`), the real `LegacyJfsImporter` and `Jfs2SceneWriter` (`2ae203f`, the exporter and convert rewritten on them); the `jfs2` tool; RecordingXRay on the scene. |
| 5 switch-over | `4c92254`, `d71f22d`, `ea24225`, `1594f38` | Save writes `.jfs2`; damage shown in the Avalonia UI; working file and recovery; old `.jfs` read by the importer only, all old file code deleted (the test-side `LegacyJfsWriter` makes old files for tests). |
| review fixes | `a363179`, `f5afe92`, `4721867` | Restore after a chunk whose records stop parsing and healing of unreadable tracks; owner, application and simulator kept on Save; clean-up. |

## Findings that still hold

- `PositionV1` quantises rudder, elevator, aileron and both brakes to 1/16384 (spec 7.3), so the importer's acceptance test allows that.
- The redundant `ObjectId`/`NetTime` in JFP2 bodies are not authoritative (spec 5): the capture writes the track id and time into them, and
  a reader ignores them.
- `track-open`, `track-close` and `object-position` have no canonical message or JFP2 class; they are private to the recorder.
- Capture sites: `Sim.AircraftUpdate.cs` and `Sim.SimConnect.cs` (own and simulator objects, with `RecordIdentity`), `Sim.cs` (their
  variables), `Session/MainSessionHost.cs` (network objects, posted to the sim thread).

## Corpus (the tiger recordings)

The owner's real recordings (12 measured with `tools/jfsstat.py`, 11 in the acceptance test; 0.2 to 29 MB) are kept outside the repository (`C:\Users\crist\Downloads\tiger
recordings`, or the folder in `JOINFS_JFS_CORPUS`). The corpus test (`LegacyJfsImporterTests`) passes silently where they are absent, so CI
does not run it.

- All are FS2024 layout, file versions 21005 or 21007; none is ambiguous between the layouts. All objects are aircraft.
- Bytes by frame type: integer variables 45 to 58%, float variables 18 to 22%, string variables about 2%, aircraft positions 19 to 31%.
  Positions at about 16 Hz, variable sets at about 8 Hz per aircraft. About 1% of integer entries change between frames, which is why
  `variables` records are upserts. Converted, a one-aircraft recording shrinks by about 60% (2678 KB to 1035 KB).
- Old files hold some frames out of order and some at negative times (down to -7 s); the new file has them in order, negative times at 0.
- Still missing from the corpus: recordings from FSX, P3D, FS2020 or X-Plane builds, an overdubbed one, an appended one, and one with
  non-aircraft objects.
