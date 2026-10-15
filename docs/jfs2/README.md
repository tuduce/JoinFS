# JFS2 recorder: where we are

Working notes for the recorder v2 project (branch `jfs2-recording`): what the code does today, what it does not do yet, and what comes
next. The design is `recorder-v2-design.md`, the bytes are `format-spec.md`; this file is the only place that says how far the code has got.

| File | What |
|---|---|
| `recorder-v2-design.md` | The design: why, the options, the proposal, the decisions. |
| `format-spec.md` | Byte-exact specification of the `.jfs2` format, edition 1, approved 2026-10-12. Appendix C lists the decisions it makes. |
| `tools/jfsstat.py` | Read-only statistics of legacy `.jfs` files (frame types, sizes, how much of each variable frame changes). |
| `tools/jfs2ref.py`, `tools/example.jfs2` | Reference writer for the spec's Appendix A example (221 bytes). The format library's golden test pins these bytes. |
| `tools/jfs2bench/` | Stage 6 measurements (not in `JoinFS.sln`): convert old files, build a synthetic long recording with `Jfs2Capture`, time and size `Jfs2Scene.Load`, copy a file with and without Deflate per chunk. Usage at the top of `Program.cs`. |

## Status (2026-10-15, after stage 6: field test fix, then limits 5, 7 and 13)

The app records, saves, opens, plays and recovers `.jfs2` files, and opens old `.jfs` files through the importer. It no longer writes
`.jfs`. The recording in memory is the scene: one `SceneTrack` of canonical messages per object, which the recorder records into, plays,
edits and saves. The working file is the recording: when recording stops the recorder reads it back as the scene, and Save of an unedited
recording moves it into place; an edited one is written by `Jfs2SceneWriter`. An overdub is captured into a working file of its own. The
player work (stage 5, slices 1 to 4) is done. Stage 6 measured load time, memory and Deflate ("Measurements" below); as decided, the
recording stays loaded whole, the working file is read back at Stop on a worker thread, and every chunk is deflated (capture and scene
writer). Step 3 measured recording rate and a quantised position record; as decided, the rate stays and `PositionV2` waits for JFP2.

Checked in a simulator: stage 2 playback (2026-10-12); gear and lights record and play (2026-10-14); a recording with a change of aircraft, saved right after Stop, trimmed at the start with the gear up (2026-10-15: variables and events good, but every track was shown from the start, fixed since, and the flight's loading phase is recorded at 0/0, limit 16). Everything after that is checked by
unit tests only: keyframes, identity changes, boats and vehicles, Save as `.jfs2`, the working file and recovery, the importer in the app,
the time base, the player on the scene (play, Jump with its state, identity changes), the trims, the working file as the recording (the
swap of the read-back, not the Stop that starts it), Deflate chunks, overdub working files and their recovery, track-close, variable names. The Stop that starts the read-back is not unit-tested (it needs a simulator). In a field test the log line "Recorder: wrote the
JFS2 recording" should end in "0 records held back"; worth checking in a simulator: a Jump back past a gear or lights change, a callsign
or model change during a recording, a trim at the start after the gear went up (the aircraft should start with it up), Save right after
a recording (the saved file is the working file, moved, once the read-back is done; Save before that writes the tracks in memory), and an overdub that is recovered
after closing JoinFS without saving.

Tests: `JoinFS.Recording.Format.Tests` 69, `JoinFS.Tests` 1238 (the recording ones are in `Recording/` and `RecorderAutoSaveTests`),
`RecordingXRay.Tests` 208, `JoinFS.UI.Tests` 506 (the recorder tab in `RecorderTests`).

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
  and builds snapshots, because that needs codec knowledge. Not thread-safe; with `WriterThread` it writes the blocks on a thread of
  its own, and a failed write is thrown by the next call.
- The reader opens finished and unfinished files alike. The Index does not list Schema blocks, so opening a finished file walks the block
  heads to find them (spec 8.1).
- A scan of an unfinished file cannot know callsigns and models (it has no codec), so a repaired file's Index lists them empty.

## How a recording flows today

- **Record.** `Recorder.StartRecord` starts `Jfs2Capture` on `<documents>/Working/<date>_<time>_recording.jfs2` (unless `-no-jfs2`).
  Every recorded object gets a `SceneTrack` in memory and a track in the file; its positions, variables, events and identity changes go
  to both as canonical messages (the file keeps variables as changes only, spec 7.6). The identity is what the owner says the object is:
  its own model, not the one it is shown with here. A chunk ends every 2 s and every fifth is a keyframe with the snapshot of every open
  track (spec 6.1); the writer's own thread deflates and writes each chunk, and syncs the file every 5 s. An object that leaves (`Sim.RemoveFromListAndIndex` calls `Recorder.ObjectLeft`) or is unflagged gets its `track-close` and is
  in no later snapshot; one that comes back, or is flagged again, is a new track.
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
4. Closed by decision in stage 6: everything is loaded whole, there is no chunk-by-chunk player (design 5.5). A one-hour recording with
   ten aircraft is about 63 MB of file (Deflate), loads in 0.85 s and holds about 285 MB in memory ("Measurements"). Revisit only if much
   longer recordings turn up.
5. Done after stage 6: the sim thread encodes each chunk and the writer's own thread (`RecordingWriterOptions.WriterThread`) deflates
   and writes it, so the disk never blocks the sim thread (spec 9.2); the file is asked to reach the disk every 5 s
   (`Jfs2Capture.SyncInterval`, spec 9.1: a power cut loses the last few seconds at most) and when it is finished. A write that fails
   is reported when the next chunk ends. Left: Stop waits on the sim thread for the writer thread to write what it holds (a chunk or
   two) and writes the Index and Footer there, as before.
6. **A clock that steps back still holds records.** Since slice 1 an object's records share one time base, so jitter holds nothing. A
   track's time may not go back (spec 5), so when a sender's network clock is corrected backwards (or positions arrive out of order), the
   capture writes the late records at the track's last time; it counts them ("records held back" in the log). Saved files are not affected
   (the tracks in memory are sorted when recording stops).
7. Done in slice 4: an object that leaves gets its `track-close`. Done after stage 6: one that is unflagged for recording during a
   recording gets its `track-close` too (design 5.3, `Recorder.RecordingStep`), and is a new track if it is flagged again.
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
13. Done after stage 6: RecordingXRay shows identity changes as frames of their own (`IdentityFrame`, kind "Identity"), with the
    new callsign, model, livery and ICAO strings in the inspector and the raw view.
14. Done in slice 4: an overdub has a working file of its own (design 11). Left, all by decision or rare: edits are not journalled, so an
    overdub made after a trim at the start is laid over the untrimmed recording when recovered, offset by the trim; recovering the
    recording alone leaves its overdub's file a leftover, which recovered later finds its base gone (if it was saved meanwhile, under another
    name) and plays alone; the `base` key is not in `format-spec.md` (it is private to working files and never in a saved file).
15. Done in stage 6 step 2: the read-back at Stop reads the working file on a worker thread (`Recorder.ReadBackWorkingFile`) and swaps
    the tracks in on the sim thread. Until then Save writes the tracks in memory; a read that ends after the recording was edited, saved,
    recorded again or played is dropped (the tracks in memory stay, Save writes them). Left: while it reads, the tracks recorded in memory
    and the ones read back are both alive (about 570 MB for an hour of ten aircraft, and the read allocates about 840 MB).
16. **A flight that loads during a recording is recorded at latitude/longitude 0/0** (field test 2026-10-15). While MSFS loads a flight
    after a change of aircraft, the user's aircraft is listed for about 25 s at 0/0 before it is listed again where the flight starts;
    the recorder records that as a track of its own (RecordingXRay shows it, the player shows it there). Proposal in "Open decisions".
    Done after the field test: the player showed every track from the start of playing, at its first position, so the aircraft of each
    track appeared together; now an object is in the sim only from its track's first position to its last frame
    (`SceneTrack.ShownAt`), after a Jump too, and a jump back brings back one that had ended.

## Open decisions

Design questions 1 to 4 are decided (design 11). Open:

- **Positions at 0/0 (limit 16), proposed 2026-10-15.** The capture leaves out a position at exactly latitude 0 and longitude 0 (no
  real flight is there; MSFS puts the user's aircraft there while a flight loads), and opens an object's track with its first position
  that is not, so the loading phase leaves no track at all and a track never starts at 0/0. Alternatives: record it and let the player
  and RecordingXRay hide it (the file keeps junk), or leave the user's aircraft out while the simulator reports it is loading (needs a
  SimConnect state the recorder does not have today, and X-Plane has none). Not done until decided.

Stage 6 step 3's proposals (measured in "Measurements", step 3), decided
by the owner on 2026-10-15:

- **Positions stay at the current rate** (about 19 Hz; the proposal of 10 Hz is rejected). The interpolation errors were measured on
  the Tiger Moth, so slow that its errors say nothing about faster aircraft. The capture keeps every position and every variable record.
- **`PositionV2` later, with JFP2, not for the recorder alone.** A quantised 52-byte record (no ObjectId or NetTime, lat/lon 1e-7 deg,
  int16 angles, Half velocities) halves the file (51% at full rate) with rounding under 1 cm and 0.003 deg, but it saves no memory (the
  scene holds decoded `PositionUpdate`s), JFP2 does not specify it yet, and a codec of the recorder's own would undo design decision 1
  (one codec). It is done when JFP2 defines `PositionV2` (`docs/jfp2/roadmap.md` 2.3); the harness layout is a starting point.

Stage 6's earlier proposals, decided by the owner on 2026-10-15 and done in step 2:

- **No chunk-by-chunk player** (limit 4). The worst case measured, an hour of ten aircraft, loads in under a second and holds 285 MB;
  the scene in memory is what makes Jump, the trims, overdub and Save simple. Revisit only if much longer recordings turn up.
- **Read back at Stop off the sim thread** (limit 15): 0.75 s with the sim thread held is a visible stall of every remote aircraft.
- **Deflate per chunk, on in the capture and the scene writer**: files shrink to 55 to 62%, for 0.4 to 0.9 ms per chunk (every 2 s)
  and 16 to 33% more load time. It must be in the capture, since Save of an unedited recording moves the working file.

## Stage 5: a player on the scene (done)

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

## Next: stage 6, the measured options

1. **Load time, memory, Deflate**, measured (no code change; "Measurements"). Proposals in "Open decisions".
2. **The decisions of step 1**, done: no chunk-by-chunk player (limit 4 closed), the read-back at Stop on a worker thread (limit 15),
   Deflate on in the capture and so in the scene writer (`Jfs2Capture.WriterOptions`).
3. **Recording rate and `PositionV2`**: positions are recorded at about 19 Hz and variables change about as often (the busiest tiger
   track); measure what a lower rate or a smaller position record saves against playback quality. Measured (`jfs2bench rate`,
   `quant`; "Measurements", step 3). Decided: the rate stays, `PositionV2` comes with JFP2 ("Open decisions"). No app code changed.

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
| 6 measurements, step 1 | `10614c6` | Load time and memory of long recordings, Deflate per chunk; `tools/jfs2bench`. Proposals: no chunk-by-chunk player, read-back off the sim thread, Deflate on. |
| 6, step 2 | `cab5060` | The decisions: no chunk-by-chunk player; read-back at Stop on a worker thread, swapped in on the sim thread; Deflate per chunk in the capture and the scene writer. |
| 6, step 3 | `f58b67d`, `c2350f6` | Recording rate and a quantised position record measured; decided: the rate stays, `PositionV2` with JFP2. |
| field test fix | | The player shows a track only from its first position to its last frame (`SceneTrack.ShownAt`), also after a Jump; a jump back brings back an object that had ended. |
| limit 5 | | The writer's own thread deflates and writes the chunks the sim thread encodes; the file is synced every 5 s. |
| limit 7 | | An object unflagged during a recording gets its `track-close`; flagged again, it is a new track. |
| limit 13 | | RecordingXRay shows identity changes. |

## Measurements (stage 6, 2026-10-15)

`tools/jfs2bench`, Release build, on the owner's PC. Load is `Jfs2Scene.Load` of a finished file: best of three after a first run (in
brackets when it differs; only the first file of a run pays the JIT); retained is what the scene holds after a full GC, allocated is
everything the load allocated.

| File | Size | Tracks, length | Load (first) | Retained | Allocated |
|---|---|---|---|---|---|
| tigers taxi, converted | 3.6 MB | 5, 4.8 min | 15 ms | 8 MB | 21 MB |
| tigers 3rd, converted (largest) | 9.0 MB | 5, 11.9 min | 46 ms (131) | 21 MB | 52 MB |
| tigers 3rd, Deflate | 5.6 MB | | 61 ms (72) | 21 MB | 64 MB |
| synthetic, 10 aircraft, 1 h | 114.3 MB | 10, 60 min | 733 ms (787) | 285 MB | 715 MB |
| synthetic, Deflate | 63.0 MB | | 852 ms (964) | 285 MB | 841 MB |
| step 2: tigers 3rd, converted (Deflate on) | 5.6 MB | 5, 11.9 min | 63 ms (105) | 21 MB | 64 MB |
| step 2: synthetic, captured (Deflate on) | 63.0 MB | 10, 60 min | 842 ms (924) | 285 MB | 841 MB |

- Converting: the five largest tiger recordings (12 to 29 MB) become 3.6 to 9.0 MB, in under 0.3 s to import and 0.4 s to write each.
  Real data is about 0.15 MB per aircraft-minute, so an hour of ten aircraft is about 90 MB.
- The synthetic file is ten copies of tigers 3rd's busiest track (positions about 19 Hz, variable records about 20 Hz), each started at
  another point and looped to an hour, written by `Jfs2Capture` in 0.7 s: 684k positions, 708k variable records, 359 keyframes,
  1797 chunks (62 KB each). It is an upper end: the tiger tracks average fewer variable records.
- In memory a frame costs about 205 bytes (1.39 M frames, 285 MB), about 2.5 times its bytes in the file.
- Deflate per chunk (`CompressionLevel.Optimal`, kept only when smaller): tigers 3rd to 62%, the synthetic file to 55%. Writing a chunk
  takes 0.38 ms median (p99 0.63) with five aircraft and 0.87 ms (p99 1.20, max 3.05) with ten, against 0.15 ms without.
- Step 2, with Deflate on in the capture and the scene writer: tigers 3rd converts to 5.6 MB in 0.53 s of writing (0.4 s before),
  tigers taxi to 2.1 MB; the synthetic hour is captured in 2.0 s (0.7 s before, so about 0.7 ms more per chunk), 63.0 MB. Load times
  match the Deflate rows of step 1.
- The scene writer's frame count is of frames handed to the capture: an imported old file's unchanged variable frames count but write
  nothing (tigers 3rd: 144,753 counted, 104,854 read back).

Step 3, recording rate and a smaller position record (`jfs2bench rate` and `quant`, three tiger recordings converted). `rate` rewrites
a file through `Jfs2Capture` keeping a position when at least 0.95/rate after the last kept, merging the variables between into one record
at the next tick; each dropped position is interpolated from the kept ones as the player does (linear; angles by shortest delta) and
compared with the recorded one. The baseline is the same rewrite with nothing dropped (it is smaller than the converted file, because
the scene writer writes the importer's variable frames one by one). Sizes with Deflate; error columns p99 (max); altitude in the
record's units; angle the worst of pitch, bank and heading.

| tigers 3rd (5 tracks, 11.9 min) | Size | Positions | Variable records | Retained | Sideways m | Altitude | Angle deg |
|---|---|---|---|---|---|---|---|
| full rate (baseline) | 4.63 MB | 58,620 | 46,233 | 21 MB | | | |
| 19 Hz | 3.83 MB | 46,983 | 32,054 | | 0.006 (0.12) | 0.06 (0.7) | 0.09 (1.1) |
| 10 Hz | 2.52 MB | 30,249 | 24,712 | 11 MB | 0.009 (0.12) | 0.07 (1.1) | 0.13 (1.3) |
| 5 Hz | 1.44 MB | 16,209 | 6,889 | 4 MB | 0.009 (0.15) | 0.09 (1.3) | 0.23 (2.7) |
| 2 Hz | 0.73 MB | 7,075 | 5,867 | | 0.012 (0.26) | 0.26 (1.9) | 0.79 (4.6) |
| candidate PositionV2, full rate | 2.37 MB | | | 21 MB | 0.007 (0.008) | 0.000 | 0.003 (0.003) |
| candidate PositionV2, 5 Hz | 0.82 MB | | | | | | |

- The other two agree: tigers taxi 1.62 MB at full rate, 0.87 at 10 Hz, 0.50 at 5 Hz, angles p99 0.07 and 0.10 deg; tigers s3 (few
  variables) 2.20, 1.07, 0.65 MB, angles p99 0.14 and 0.24 deg, but single samples off by 12 deg at 10 Hz and 9 at 5 Hz (37 at 2 Hz):
  quick rolls between ticks, where the player's slerp cuts the corner.
- The recorded rate is already uneven: "19 Hz" (0.05 s) drops a fifth of the positions, so many arrive closer than 50 ms apart; and
  variables come in bursts, which the merge collapses (46k to 32k records at 0.05 s).
- The candidate PositionV2 (`quant`; layout in the harness, 52 bytes against `PositionV1`'s 103) gives 45 to 51% of the file at full
  rate and 51 to 58% at 5 Hz; rounding under 1 cm sideways, 0.003 deg in angle, 0.03 in velocity. Memory does not change: the scene
  holds decoded positions.
- Memory follows the frame count: about 205 bytes a frame at any rate.

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
