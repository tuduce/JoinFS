# JFS2 recorder: where we are

Working notes for the recorder v2 project (branch `jfs2-recording`): what the code does today, what it does not do yet, and what comes
next. The design is `recorder-v2-design.md`, the bytes are `format-spec.md`; this file is the only place that says how far the code has got.

| File | What |
|---|---|
| `recorder-v2-design.md` | The design: why, the options, the proposal, the decisions. |
| `format-spec.md` | Byte-exact specification of the `.jfs2` format, edition 1, approved 2026-10-12. Appendix C lists the decisions it makes. |
| `tools/jfsstat.py` | Read-only statistics of legacy `.jfs` files (frame types, sizes, how much of each variable frame changes). |
| `tools/jfs2ref.py`, `tools/example.jfs2` | Reference writer for the spec's Appendix A example (221 bytes). The format library's golden test pins these bytes. |

## Status (2026-10-15, after player slice 4)

The app records, saves, opens, plays and recovers `.jfs2` files, and opens old `.jfs` files through the importer. It no longer writes
`.jfs`. The recording in memory is the scene: one `SceneTrack` of canonical messages per object, which the recorder records into, plays,
edits and saves. The working file is the recording: when recording stops the recorder reads it back as the scene, and Save of an unedited
recording moves it into place; an edited one is written by `Jfs2SceneWriter`. An overdub is captured into a working file of its own. The
player work ("Next", slices 1 to 4) is done; stage 6 is next.

Checked in a simulator: stage 2 playback (2026-10-12); gear and lights record and play (2026-10-14). Everything after that is checked by
unit tests only: keyframes, identity changes, boats and vehicles, Save as `.jfs2`, the working file and recovery, the importer in the app,
the time base, the player on the scene (play, Jump with its state, identity changes), the trims, the working file as the recording,
overdub working files and their recovery, track-close, variable names. The read-back at Stop is not unit-tested (it needs a simulator). In a field test the log line "Recorder: wrote the
JFS2 recording" should end in "0 records held back"; worth checking in a simulator: a Jump back past a gear or lights change, a callsign
or model change during a recording, a trim at the start after the gear went up (the aircraft should start with it up), Save right after
a recording (the log says the working file was read back; the saved file is the working file, moved), and an overdub that is recovered
after closing JoinFS without saving.

Tests: `JoinFS.Recording.Format.Tests` 61, `JoinFS.Tests` 1223 (the recording ones are in `Recording/` and `RecorderAutoSaveTests`),
`RecordingXRay.Tests` 206, `JoinFS.UI.Tests` 506 (the recorder tab in `RecorderTests`).

## The code

| Where | What | Knows |
|---|---|---|
| `JoinFS.Recording.Format/` | The container (spec 1 to 6, 8, 9): `RecordingWriter`, `RecordingReader`, `RecordingRepair`, `RecordingDump`, CRC-32, Deflate. Records are opaque (kind id, track, time, bytes). | nothing else; no build symbols |
| `JoinFS.Messages/` | The canonical messages and the JFP2 body codecs, shared by the network and the recorder (design 4.4). | nothing else; no build symbols |
| `JoinFS.Recording/` | The engine pieces that know the record kinds but not the simulator: `Jfs2Kinds` (names, versions, `TrackOpenV1`), `ObjectPositionV1Codec`, `Jfs2Capture` (writer), `Jfs2Scene` (reader into tracks of canonical messages; `SceneTrack.StateAt` is what a jump restores), `SceneTrack.TrimStart`/`TrimEnd`/`Shift` are the edits, `Jfs2SceneWriter` (scene to file), `LegacyJfsImporter` (old `.jfs` to scene),
`TrackTimeBase` (the recording times of one object's records). The capture writes `track-close` (`CloseTrack`) and `variable-names.txt` (`Finish` with a namer). | the two libraries above; no build symbols |
| `JoinFS/Recording/` | `Jfs2Convert` (`JoinFS -convert`). | the importer, the scene writer |
| `JoinFS/Recorder.cs` | Recording into tracks, playback of tracks (`Recorder.Obj` is a track plus its play state), editing (in memory), the live capture, the working files (the recording's own and its overdubs'), Save (`SaveCopy`: the unedited working file or the tracks). | everything |
| `JoinFS.Recording.Tool/` | `jfs2 dump [--records] f`, `jfs2 verify f`, `jfs2 repair f [-o out]`. | the format library only |
| `RecordingXRay/` | Opens `.jfs2` through `Jfs2Scene`; keeps its own `.jfs` reader on purpose (it shows what the old file stores, frame by frame). | `JoinFS.Recording` |

Format library notes:

- The writer enforces the track lifecycle and time rules of spec 5; `Finish` closes tracks still open. The caller owns the chunking policy
  and builds snapshots, because that needs codec knowledge. Not thread-safe.
- The reader opens finished and unfinished files alike. The Index does not list Schema blocks, so opening a finished file walks the block
  heads to find them (spec 8.1).
- A scan of an unfinished file cannot know callsigns and models (it has no codec), so a repaired file's Index lists them empty.

## How a recording flows today

- **Record.** `Recorder.StartRecord` starts `Jfs2Capture` on `<documents>/Working/<date>_<time>_recording.jfs2` (unless `-no-jfs2`).
  Every recorded object gets a `SceneTrack` in memory and a track in the file; its positions, variables, events and identity changes go
  to both as canonical messages (the file keeps variables as changes only, spec 7.6). The identity is what the owner says the object is:
  its own model, not the one it is shown with here. A chunk ends every 2 s and every fifth is a keyframe with the snapshot of every open
  track (spec 6.1). An object that leaves (`Sim.RemoveFromListAndIndex` calls `Recorder.ObjectLeft`) gets its `track-close` and is in no
  later snapshot; one that comes back is a new track.
- **Time base.** Each recorded object has a `TrackTimeBase`. Positions keep their sample spacing (for a network object, its sender's
  clock), offset so the first is at the recorder's time; variables, events and identities take the time of the object's latest position,
  and move on only when positions are more than 0.5 s overdue. The in-memory frames and the file get the same times.
- **Stop.** The capture finishes the file: `variable-names.txt` (named from `VariableMgr.definitions`: the text each vuid was hashed
  from), Index, Footer. A take with no position is deleted at once. The recorder then reads the working file back (`ReloadWorkingFile`) and
  its tracks replace the ones recorded in memory, so what plays, is edited and is saved is what the file holds; if it cannot be read, the
  tracks in memory stay (sorted, limit 6) and Save writes them.
- **Play.** Each `Recorder.Obj` plays its track: a frame plays once its time has passed (variables, events; an identity change makes the
  next position show the object as that, so the simulator swaps the model or callsign), and the position is interpolated between the
  frames around the play time. Jump folds the frames before the time (`SceneTrack.StateAt`): the latest position, identity and merged
  variables go to the object, so gear, flaps and lights are right wherever playing starts. Events are not replayed by a jump.
- **Edit.** All in memory, on the tracks; Save writes the result. Trim at the start (`SceneTrack.TrimStart`) cuts the frames before the
  play time and starts each track there with the state they left (opening identity at the cut, variables merged and latest position at 0,
  as a keyframe would); trim at the end cuts the frames from the play time on; an object with nothing left is removed. Remove takes an
  object out, Skip leaves it out of playback but in the recording. Add shifts the new tracks to the end (`SceneTrack.Shift`); the writer
  numbers tracks from 1, so ids never clash. An overdub records new tracks into the scene while it plays; Save's writer puts all tracks
  in one time order, which is the merge design 7 asks for. Every edit (trim, remove, add, overdub, loading a file) marks the recording
  edited, so Save writes the tracks. Edits do not reach the working files (design 11), so a recovery brings back the recording as captured,
  with its overdubs.
- **Overdub.** Captured into a working file of its own (`..._overdub.jfs2`), on the recording's time line, whose header key `base` names the
  file it adds to: the recording's latest working file (its own or the overdub before), else the file it was opened from or last saved to.
- **Save and auto-save.** `CopyForSave` (sim thread) gives a `SaveCopy`: copies of the tracks, and the finished working file when the
  recording is unedited. `Recorder.SaveJfs2` moves that file into place (a rename on the same drive; across drives a copy to `<path>.tmp`
  first), or runs `Jfs2SceneWriter` over the tracks into `<path>.tmp` and moves that; then `Recorder.Saved` deletes the working files and
  remembers the path as the base of a later overdub. A written file has what the tracks hold: identity changes, each object's owner, source
  application and simulator, the header's `sim` when all objects share one, and `variable-names.txt`.
- **Open, Add, `-play`.** `Recorder.Parse` sniffs the first two bytes (`Jfs2.LegacyVersionSignature`): a `.jfs2` goes through `Jfs2Scene`,
  an old `.jfs` through `LegacyJfsImporter`; both end in tracks that `Recorder.Load` plays (Add shifts them to the end). Damage is reported
  to the user in the Avalonia UI.
- **Recovery.** At start-up the Avalonia UI offers each leftover file in `Working/` (newest first): Recover, Delete, or later. A file another
  JoinFS is writing is skipped. Reading an overdub's file reads the file its `base` names first, then lays the overdub's tracks over it at
  their own times; a base that is gone or unreadable is reported and the overdub plays alone. A recovered recording owns its file and the
  working files under it (`Recorder.WorkingFileChain`), is unsaved until saved, and Save removes them all. The WinForms UI neither asks nor
  offers; its leftovers are offered by the Avalonia UI at the next start.
- **Reading damaged files** (`Jfs2Scene`, spec 8.2). After a lost chunk, or a chunk whose records stop parsing, nothing is applied until the
  next keyframe, whose snapshot restores the state. A record that does not decode marks its track unreliable, and the next keyframe's
  snapshot heals that track alone; a track whose track-open or identity was never read is healed the same way.
- **Old files.** The importer parses both layouts of the trailing strings and keeps the one that validates; when both do it takes FS2024 and
  warns. `JoinFS -convert a.jfs ...` writes `a.jfs2` beside each file, never overwriting.

## Known limits and deviations

From the design or the spec, all known, none silent:

1. Done in slice 4: Save of an unedited recording moves its working file; only an edited one is written again.
2. Done in slice 2: identity changes are frames of the scene, captured for network objects too, played and saved.
3. Done in slice 2: Jump restores position, identity and variables (design 5.4).
4. **Everything is loaded whole.** A one-hour recording with ten aircraft is about 80 MB of file. Design 5.5's chunk-by-chunk player is a
   stage 6 measurement.
5. **Chunks are written on the sim thread** (about 8 KB every 2 s) and flushed to the operating system, not to disk, until the file is
   finished. Spec 9.1 asks for a flush to disk every few seconds (SHOULD); design 5.5 wants a writer thread.
6. **A clock that steps back still holds records.** Since slice 1 an object's records share one time base, so jitter holds nothing. A
   track's time may not go back (spec 5), so when a sender's network clock is corrected backwards (or positions arrive out of order), the
   capture writes the late records at the track's last time; it counts them ("records held back" in the log). Saved files are not affected
   (the tracks in memory are sorted when recording stops).
7. Done in slice 4: an object that leaves gets its `track-close`. Left: an object that is unflagged for recording during a recording is still
   recorded until it leaves (design 5.3 says unflagging closes its track too).
8. Done in slice 4: the capture and the scene writer write `variable-names.txt`. RecordingXRay still names variables from its own tables.
9. **ICAO type and airline are cut to 8 bytes** by `IdentityV1Codec` (the JFP2 limit, `docs/jfp2/protocol.md` 9.7). Real designators fit.
   Old FS2024 recordings keep SimConnect's raw `ATC MODEL` string there (for example `ATCCOM.AC_MODEL TIGER MOTH.0.tex`), which becomes
   `ATCCOM.A`. Whether model matching ever got a usable hint from the long string is unchecked; if it did, the importer should clean the
   string instead (`Sim.SimConnect.cs` `ResolveObjectInfoType` strips the same junk from live objects).
10. **`convert` lives in the app** (`JoinFS -convert`), not in the `jfs2` tool as design section 8 says: it was built while the importer still
    lived in the app. The importer is standalone now, so it could move.
11. **The WinForms recorder** does not show damage warnings, does not ask about unsaved recordings, and does not offer recovery. It goes away
    with the WinForms UI.
12. Done in slice 3: a trim at the start puts the state at the cut first. Left: the position at the cut is the latest one before it, moved
    to 0 (at most one sample early), not one interpolated at the cut.
13. **RecordingXRay does not show identity changes**; it lists positions, variables and events.
14. Done in slice 4: an overdub has a working file of its own (design 11). Left, all by decision or rare: edits are not journalled, so an
    overdub made after a trim at the start is laid over the untrimmed recording when recovered, offset by the trim; recovering the
    recording alone leaves its overdub's file a leftover, which recovered later finds its base gone (if it was saved meanwhile, under another
    name) and plays alone; the `base` key is not in `format-spec.md` (it is private to working files and never in a saved file).
15. **The read-back at Stop runs on the sim thread**: reading a long recording holds the sim thread up (about the load time of the same file
    opened). Stage 6 measures load time with the chunk-by-chunk player (limit 4); reading it on another thread is the fix if it matters.

## Open decisions

- None. Design questions 1 to 4 are decided (design 11).

## Next: a player on the scene (stage 5, remaining)

Replace the old frame model of playback and editing with a player that works on `Jfs2Scene` tracks of canonical messages, so the file the capture
writes is the recording. Suggested slices, each leaving a working recorder, one fresh session each:

1. **Time base** (limit 6), done (`30c3dbf`): variables, events and identities of an object are stamped on the time base of its positions
   (`TrackTimeBase`), so jitter holds nothing back; the capture counts what it still holds.
2. **Player on the scene**, done (`38f7d81`): `Recorder.Obj` is a `SceneTrack` plus its play state; play, pause, loop, skip and both UIs
   work on it through `Recorder`'s existing surface (`Parse`, `Load`, `CopyForSave` and Save take tracks). The scene has identity frames,
   the player applies them; Jump folds the frames up to T (`SceneTrack.StateAt`). `Jfs2Loader`, `Jfs2Exporter` and the old frame classes
   are gone (Save already writes with `Jfs2SceneWriter`).
3. **Editing on the scene**, done (`37f052a`): trims are `SceneTrack.TrimStart`/`TrimEnd`, a trim at the start puts the state at the cut
   first (limit 12), an object with nothing left is removed; remove, skip, append and overdub already worked on the tracks since slice 2.
4. **The working file is the recording**, done: after Stop the recorder reads its working file back as the scene; Save of an unedited
   recording moves it into place, an edited one is written by the scene writer; an overdub has its own working file whose `base` names
   the file under it (limit 14); `track-close` when an object leaves (limit 7); `variable-names.txt` (limit 8).

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
| 5 player, slice 1 | `30c3dbf` | One time base per recorded object (`TrackTimeBase`); the capture counts held records. |
| 5 player, slice 2 | `38f7d81` | The recorder records into, plays and saves `SceneTrack`s; identity frames (network objects captured too); Jump restores state; loader and exporter removed. |
| 5 player, slice 3 | `37f052a` | Editing on the scene: trims on `SceneTrack` with the state at the cut first; objects left empty by a trim removed. |
| 5 player, slice 4 | `375b29f`, `f1226e8` | Design questions 1 to 4 decided. The working file is the recording (read back at Stop, moved on Save unless edited); overdub working files with `base`, recovered over their base; `track-close`; `variable-names.txt`. |

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
