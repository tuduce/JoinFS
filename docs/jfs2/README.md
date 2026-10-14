# JFS2 recorder: where we are

Working notes for the recorder v2 project (branch `jfs2-recording`). The format library exists; since stage 2 the app writes and plays a first slice of it (aircraft identity and position).

| File | What |
|---|---|
| `recorder-v2-design.md` | The design: why, the options, the proposal. |
| `format-spec.md` | Byte-exact specification of the `.jfs2` format, edition 1, approved 2026-10-12. Appendix C lists the decisions it makes. |
| `tools/jfsstat.py` | Read-only statistics of legacy `.jfs` files (frame types, sizes, how much of each variable frame changes). |
| `tools/jfs2ref.py`, `tools/example.jfs2` | Reference writer for the spec's Appendix A example (221 bytes). The format library's golden test pins these bytes (it reads `example.jfs2` from here). |

## Status (2026-10-12)

Spec edition 1 and its ten decisions approved; project layout decided (standalone format library).
**Stage 1 is done**: `JoinFS.Recording.Format` (writer, reader, CRC-32, Deflate, repair, dump) with
`JoinFS.Recording.Format.Tests` (54 tests: golden bytes of Appendix A, round trips, writer rules, every
truncation, random corruption). Not wired into the app.
**Prep refactor done** (2026-10-12): the canonical messages, the JFP2 codecs, `NodeId`, `AddressCodec` and
`MessageClasses` are now the standalone library `JoinFS.Messages/` (same namespaces, no call site
changed; 1129 tests pass; all six build configurations compile).
**Stage 2, the tracer bullet, is built** (2026-10-12, in-sim playback confirmed; see "Stage 2 as built").
**Stage 3 is built**: 3a variables and events, 3b object-position, 3c keyframes, identity changes (see
"Stage 3 as built"); 3d, the streaming player, moved to stage 5.
**Stage 4 is partly built** (2026-10-14): `.jfs` to `.jfs2` conversion, in the app and as `JoinFS -convert` (see "Stage 4 as built").

## Open decisions

1. Nothing blocks stage 2.
2. Later, not blocking: working-file location and crash-recovery prompt (stage 5), overdub merge on
   stop (design question 3), default metadata (question 4), what to do with an old `.jfs` that parses
   both ways under the FS2024 and non-FS2024 layouts (stage 4; recommended: ask the user).

## The format library

`JoinFS.Recording.Format/` (net8.0, no references, no build symbols). Records are opaque: (kind id,
track, time, bytes).

- `RecordingWriter`: `Create`, `BindKind`, `Add`, `StartKeyframeChunk` + `AddSnapshot`, `EndChunk`,
  `AddAttachment`, `SetTrackLabel`, `Sync`, `Finish`. Enforces the track lifecycle and time rules of spec
  §5; `Finish` closes tracks still open. The caller owns the chunking policy and builds snapshots,
  because that needs codec knowledge. Not thread-safe; the engine puts it on a writer thread.
- `RecordingReader`: `Open`, `Header`, `IsFinished`, `Chunks`, `Tracks`, `Kinds`, `Attachments`, `Damage`,
  `FindKeyframeChunk`, `TryReadChunk`. Opens finished and unfinished files alike.
- `RecordingRepair.Repair`, `RecordingDump.Write`.
- Index tracks carry the callsign and model the writer was told; a scan of an unfinished file cannot know
  them (it has no codec), so a repaired file lists them empty.
- Finding for the spec: the Index does not list Schema blocks, so opening a finished file walks the
  block heads (type and length only) to find them. Cheap, and written into §8.1. If it ever matters,
  the Index could list schema offsets in a later minor edition.

## Plan

Each stage fits a fresh session (low-consumption workflow).

0. **Spec and decisions.** Done 2026-10-12. Collect the `.jfs` corpus (see below).
1. **Format library.** Done 2026-10-12: new standalone project: writer, reader, CRC-32, repair, `dump`. Opaque
   records. Tests: round trip, truncation and corruption fuzzing, golden bytes from `tools/example.jfs2`.
   Prep refactor (shared messages and codecs library `JoinFS.Messages`): done 2026-10-12.
2. **Tracer bullet.** Built 2026-10-12, waiting for an in-sim test. Codec binding for `track-open`,
   `identity`, `position`; each new recording also writes a `.jfs2`, and a `.jfs2` opens and plays.
   The old recorder stays in place.
3. **Widen.** Remaining kinds; the track-state tracker (needed for keyframes and for Trim);
   keyframes, index, Jump; variable-change capture; a reader prefetch thread so interpolation can look
   ahead without blocking the sim thread.
4. **Importer.** Move the old reader into a frozen module; corpus tests; open by magic; the command-line
   tool; move RecordingXRay onto the new libraries.
5. **Editing and switch-over.** Trim, Append, Overdub, Remove, Skip on the new engine; both UIs
   (WinForms `MainForm`/`RecorderForm`, Avalonia `LiveRecorderSource`/`RecorderViewModel`), dialogs,
   resx strings, hotkeys, `-play`, working file and crash recovery; delete the old write path.
6. **Measured options.** Compression, recording rate, `PositionV2`, new kinds.

## Stage 2 as built

Code in `JoinFS/Recording/`, tests in `JoinFS.Tests/Recording/`. `JoinFS` references the format library.

- `Jfs2Kinds.cs`: kind names and versions, `Jfs2KindIds.Bind`, and the `track-open` body (`TrackOpenV1`). `identity` and
  `position` use the shared `IdentityV1Codec` and `PositionV1Codec` from `JoinFS.Messages`.
- `Jfs2Capture`: takes canonical messages and recording seconds, writes the file. It chunks at about 2 s, clamps a time that
  goes back (per track, and below the previous chunk's TMin, spec 5), writes the track id and time into the position body
  (spec 5), and on any error switches itself off with one log line. It knows nothing of `Sim` or `Main`.
- `Jfs2Loader`: reads a file into `Recorder.Obj`/`Recorder.AircraftPositionFrame`, so the old player, jump, trim and both UIs
  work on it unchanged. It loads everything (the stage 3 player replaces that), ignores snapshots, applies nothing after a lost
  chunk until the next keyframe (spec 8.2), and counts what it skipped by kind name and version. An essential kind of a newer
  version gives an error that says so, not an empty scene.
- `Recorder`: a new recording (not an overdub) also starts `Jfs2Capture` (`<documents>/<date>_<time>_recording.jfs2`, unique
  name). `AddRecordedObject` replaces two copies of the object creation and opens the track; `Record(.., ref AircraftPosition)`
  tees the position; `Stop` finishes the file. `Parse` recognises the magic (it reads as version 250 to the old reader) and
  hands the stream to the loader, so File → Open, Add, `-play` and the Avalonia picker (open filter `jfs;jfs2`) need nothing
  else. `-no-jfs2` switches the capture off.
- `SimMessageMapper.ToPositionUpdate(objectId, netTime, ref position, flags)`: the recorder has no `Sim.Aircraft` to read flags
  from; the old format keeps only on-ground, so only that flag is set.

Limits of this stage, all deliberate: aircraft only (no `object-position`), no variables or events, so a played `.jfs2` has
position but no gear, lights or flaps; one identity per track (later identity changes are not written by the capture, and
ignored by the loader); only the first chunk is a keyframe; the file is written on the sim thread (a chunk of about 8 KB every
2 s) and flushed to the operating system, not to disk, until it is finished; a `.jfs2` is not saved again as `.jfs2`
(Save writes `.jfs`); an overdub is not captured. Not tested in a simulator yet.

## Stage 3 as built

Stage 3 goes in slices, each leaving a working recorder.

- **3a, variables and events** (2026-10-12). `Jfs2Capture.Variables` keeps the last value it wrote
  for each variable of a track and stores only the changes (spec 7.6: the first call of a track is the full set; a call
  with no change writes nothing; more than 255 entries split into records of the same time; floats compare bit for bit).
  `Jfs2Capture.Event` writes `event`. `Recorder.Record(..)` for events and the three variable dictionaries tee into them.
  `Jfs2Loader` turns each `variables` record into up to three old frames (integers, floats, strings) holding only what the
  record changed, and an `event` into a `SimEventFrame`; the old player merges them, which is how an upsert reads. A played
  `.jfs2` now has gear, lights and flaps. The three dictionaries of one sample are three records (the old call sites record
  them one by one); merging them into one is a size saving for later.
- **3b, `object-position` and non-aircraft objects** (2026-10-12; boats and vehicles not yet checked in a simulator). `ObjectPositionV1Codec`
  (`JoinFS/Recording/`, 89 bytes, spec 7.2; private to the recorder, so not in `JoinFS.Messages`). Every recorded object now
  gets a track (`Recorder.OpenJfs2Track` takes a `Sim.Obj`), `Jfs2Capture.ObjectPosition` writes its samples, and
  `Jfs2Loader` turns a track whose identity is not an aircraft into a plain `Recorder.Obj` with `ObjectPositionFrame`s, so
  boats and vehicles play instead of being skipped. `SimMessageMapper.ToObjectPositionUpdate` got an overload that takes the id,
  time and flags (the recorder has no `Sim.Obj` to read them from).
  Confirmed in a simulator 2026-10-14: gear and lights record and play.
- **3c, keyframes** (2026-10-14, unit tests only). `Jfs2Capture` keeps a `TrackState` per track (its track-open and latest
  identity and position bodies, the merged variables, and the time of each) and starts a keyframe chunk after every fifth chunk
  (about 10 s) with the spec 6.1 snapshot: track-open, identity, latest position, merged variables, per track in opening order, each
  with the time of what it summarises. `Jfs2Loader` still skips snapshots on a clean read, but after a lost chunk (CRC failure, or a
  gap in an unfinished file) it waits for the next keyframe and restores from its snapshot (spec 8.2): tracks opened in the lost
  chunk appear, the merged variables come back, and a snapshot position already read is not added twice; frames stay in time order.
- **Identity changes** (2026-10-14, unit tests only). `Jfs2Capture.Identity` writes a new `identity` record when the encoded body
  differs from the latest one (a long ICAO string the codec cuts off changes nothing); a keyframe's snapshot carries the latest identity
  with its own time. `Recorder.RecordIdentity(Sim.Obj)` is called just before each recorded position (`Sim.AircraftUpdate.cs`,
  `Sim.SimConnect.cs`). `Jfs2Loader` still plays each object with the identity it opened with (the old `Recorder.Obj` has one) and says
  how many changes it ignored; the stage 5 player applies them.
- Stage 3 is complete. **3d, the chunk-by-chunk player with a prefetch thread, moves to
  stage 5** (decided 2026-10-14): it would have to plug into the old `Recorder`, which still holds every frame while recording
  and has four open paths (WinForms, Avalonia, `-play`, Add), and stage 5 rebuilds playback on the new engine anyway. The pieces
  it needs exist: `RecordingReader.FindKeyframeChunk`, the snapshots, and the restore code in `Jfs2Loader`. One gap it would
  close: the old `Jump` moves the position but does not restore variables (gear, flaps and lights stay as they were before it).

## Stage 4 as built

Code in `JoinFS/Recording/`, tests in `JoinFS.Tests/Recording/Jfs2ImportTests.cs`.

- `Jfs2Exporter` writes `Recorder.Obj`s as a `.jfs2`. The old reader (`Recorder.Parse`, now usable without a `Main`) produces
  the objects, so it is the frozen importer of design section 8 until stage 5 moves it into its own module. Frames of all objects
  go in time order (a chunk may not go back), tracks are numbered from 1 and opened at their first frame, with owner `recorded`
  and the nickname kept. Objects without a position are left out. A time before 0 is held at 0.
- `Jfs2Convert` and `JoinFS -convert a.jfs [b.jfs ...]` write `a.jfs2` beside each file, never overwriting (`a-2.jfs2`) and never
  touching the original. It runs before the application starts, so it needs no simulator. A windowed build attaches to the
  parent console for its report. A file it cannot read is reported and the rest go on; the exit code is 1 if any failed.
- Open by magic already worked (stage 2: `Recorder.Parse` sniffs the format). Not done yet: `RecordingXRay` still has its own
  reader; the importer as a separate module.
- `JoinFS.Recording.Tool/` (2026-10-14) builds `jfs2`, a command-line tool that uses only the format library: `jfs2 dump [--records] f`,
  `jfs2 verify f` (reads every chunk, exit code 1 if anything is wrong), `jfs2 repair f [-o out]` (works on a copy, never overwrites;
  default `<name>-repaired.jfs2`). Tested in `JoinFS.Recording.Format.Tests/ToolTests.cs`.
- Acceptance test on the 11 real recordings in the owner's `Downloads/tiger recordings` (or the folder in `JOINFS_JFS_CORPUS`;
  skipped when absent): same objects, same number of position frames and events per object, same positions (the five control
  axes to 1/16384, everything else bit for bit), same merged variables at four moments. What it found, all accepted:
  - `IcaoType` and `IcaoAirline` are cut to 8 bytes by `IdentityV1Codec`. FS2024 recordings keep a long model string in
    `IcaoType` (for example `ATCCOM.AC_MODEL TIGER MOTH.0.tex`), which becomes `ATCCOM.A`. Live capture has the same limit.
  - Old files hold some frames out of order and some at negative times (down to -7 s); the new file has them in order and
    holds negative times at 0.
  - The new file is about 60% smaller for a one-aircraft recording (variables are stored on change): 2678 KB became 1035 KB.

## Stage 5 as built

Stage 5 goes in slices too. The editing stays on the old in-memory `Recorder` until the end (it already holds every frame), and the
format switches over first.

- **5a, Save writes `.jfs2`** (2026-10-14, unit tests only). `Recorder.SaveJfs2(path, objects, now, overwrite)` runs the exporter into
  `<path>.tmp` and moves it into place, so a failure leaves nothing behind and does not destroy a file being replaced. Auto-save
  (`BuildAutoSaveFileName` now ends in `.jfs2`), the WinForms Save dialog and the Avalonia `SaveAsync` all use it; the save dialogs offer
  `.jfs2` only. Open still reads both. A recording edited in memory (trim, append, overdub, remove, skip) therefore saves as `.jfs2`
  with the edits. Consequences: the file saved is built from the old objects, so it has the exporter's limits (identity changes made
  during the live capture are not in it, ICAO strings cut to 8 bytes), and the live capture's own working file
  (`<date>_<time>_recording.jfs2`) still stays in the documents folder beside the saved one: deleting it, and offering it after a crash,
  is the working-file decision still open. The old `Recorder.Write` (the `.jfs` writer) is no longer called by the app.
- **5b, a damaged file is announced in the new UI** (2026-10-14). `Jfs2Loader.Result.Warnings` holds the notes the user should see (lost
  chunks, unreadable records, kinds this build does not play); the identity note stays in the log. `Recorder.Parse(reader, warnings)`
  collects them, `IRecorderSource.OpenAsync` returns them as text, and the Recorder tab shows "Opened, but part of the file is damaged: ..."
  in its status and in a message overlay. The recording still plays. The WinForms recorder form does not show it (it goes away).


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
