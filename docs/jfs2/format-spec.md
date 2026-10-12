# JFS2: the JoinFS recording format

**Specification, edition 1. Approved 2026-10-12** (the ten decisions of
[Appendix C](#appendix-c-decisions) included). It defines the bytes of a `.jfs2` file and the rules
a reader and a writer must follow. It does not say why the format looks this way
(`recorder-v2-design.md`), nor how the code is organised or delivered. The container (§1 to §6, §8,
§9) is implemented by `JoinFS.Recording.Format`; the record kinds of §7 are not yet.

Key words **MUST**, **SHOULD** and **MAY** have their usual meaning (RFC 2119).

## Contents

1. [Conventions](#1-conventions)
2. [File structure](#2-file-structure)
3. [Blocks](#3-blocks)
4. [Records](#4-records)
5. [Tracks and time](#5-tracks-and-time)
6. [Keyframes and seeking](#6-keyframes-and-seeking)
7. [Record kinds, version 1](#7-record-kinds-version-1)
8. [Reading: errors, damage and repair](#8-reading-errors-damage-and-repair)
9. [Writing: durability and policy](#9-writing-durability-and-policy)
10. [Extending the format](#10-extending-the-format)
- [Appendix A. Example file](#appendix-a-example-file)
- [Appendix B. Constants](#appendix-b-constants)
- [Appendix C. Decisions](#appendix-c-decisions)

---

## 1. Conventions

### 1.1 Numbers

All fixed-size integers and floats are **little-endian**. `u8`, `u32`, `u64` are unsigned; `i64` is
signed two's complement; `f32` and `f64` are IEEE 754.

**varint** is an unsigned LEB128: seven bits per byte, least significant group first, the high bit of
a byte set when another byte follows. A varint is at most 10 bytes. A writer MUST emit the shortest
form. A reader MUST reject a varint of more than 10 bytes or one that overflows 64 bits.

**zigzag** is a signed value stored as a varint of `(n << 1) ^ (n >> 63)`. So 0, -1, 1, -2 are
0, 1, 2, 3.

### 1.2 Text

A **string** is a varint byte count followed by that many bytes of UTF-8, without a terminator. A
reader MUST accept invalid UTF-8 without failing (it is replaced when displayed); a writer MUST NOT
produce it. The count of a metadata string is at most 65,535.

Text inside a record **body** follows the codec of that kind (§7), not this rule. The JFP2 codecs use
a `u16` length prefix.

### 1.3 CRC-32

CRC-32/ISO-HDLC, as in zlib and PNG: polynomial `0x04C11DB7` reflected (`0xEDB88320`), initial value
and final XOR `0xFFFFFFFF`. Check value: the CRC of the ASCII bytes `123456789` is `0xCBF43926`. The
implementation is a small table in the format library; it does not take a package.

### 1.4 Time

Time is an `i64` count of **microseconds** on the recording's own timeline. 0 is the start of the
recording. It is not wall-clock time and not any network clock. Wall-clock time is `StartUtc` in the
header.

---

## 2. File structure

```
File    := Magic  Block*  Footer
Magic   := FA 00 4A 46 53 32 0D 0A          (8 bytes)
Footer  := IndexOffset:u64  Crc32:u32  EndMagic        (20 bytes)
EndMagic:= "JFS2END\n"                      (4A 46 53 32 45 4E 44 0A)
```

- The first two bytes of the magic, read as a little-endian `int16`, are 250. A JoinFS build that
  predates JFS2 reads that as a recording version below 10022 and rejects the file instead of
  misparsing it. `0D 0A` makes text-mode transfer damage visible.
- The first block MUST be a Header. The last block MUST be an Index. Everything between is Schema,
  Chunk or Attach blocks in any order, subject to the rules of each.
- `Footer.Crc32` is the CRC-32 of the eight `IndexOffset` bytes. `IndexOffset` is the file offset of
  the first byte of the Index block.
- A file without a valid footer is **unfinished** (a crash, a power loss, a copy still in progress).
  It is not corrupt. §8.3 says how it is read and repaired.

---

## 3. Blocks

```
Block := Type:u8  Length:u32  Body[Length]  Crc32:u32
```

`Crc32` covers `Type`, `Length` and `Body`. A block is therefore 9 bytes larger than its body. A
reader MUST NOT allocate for a `Length` it has not checked: it MUST reject a block whose `Length`
exceeds 67,108,864 (64 MiB) or runs past the end of the file.

| Type | Name | Occurs |
|---|---|---|
| 1 | Header | exactly once, first |
| 2 | Schema | any number |
| 3 | Chunk | any number |
| 4 | Index | exactly once, last |
| 5 | Attach | any number |

A reader MUST skip a block of any other type by its `Length`. It MUST check the CRC of a block it
skips only when it is repairing (§8.3).

**Extension of the fixed blocks.** A reader MUST ignore bytes at the end of the body of a Header,
Schema, Index or Attach block that it does not understand. A later minor edition adds fields there.
The Chunk block is the exception (§3.3).

### 3.1 Header (type 1)

```
FormatMajor:u8           1
FormatMinor:u8           0
RequiredFeatures:u32     0
OptionalFeatures:u32     0
StartUtc:i64             microseconds since 1970-01-01T00:00:00Z when time 0 happened
MetadataCount:varint
Metadata:                MetadataCount × ( Key:string  Value:string )
```

- A reader MUST refuse a file whose `FormatMajor` it does not know. It MUST refuse a file with a
  `RequiredFeatures` bit it does not know. It MUST ignore unknown `OptionalFeatures` bits and a
  higher `FormatMinor`.
- `Metadata` is UTF-8 text for people and tools, never needed to play the file. Keys are lower-case
  ASCII, unique within the block. Defined keys: `app` (the writer, for example `JoinFS 26.10`),
  `sim` (the simulator the recording was made on), `title`, `note`. Other keys MUST be kept when a file
  is rewritten.
- A Header whose metadata strings run past the end of the block body is rejected.
- No required or optional feature bits are defined in edition 1.

### 3.2 Schema (type 2)

```
KindId:varint     file-local number of the kind
Name:string       the kind's name, for example "position"
Version:u8        the codec version of the body, ≥ 1
```

A Schema block binds a file-local **kind id** to a (name, version) pair (§7). It MUST appear before
the first record that uses the id, in file order. A kind id MUST NOT be bound twice to different
pairs. Two ids MAY carry the same name with different versions: that is how one file holds records
of two codec versions. Kind id 0 is reserved and MUST NOT be bound.

### 3.3 Chunk (type 3)

```
TMin:i64                 no record in the chunk is earlier
TMax:i64                 no record in the chunk is later
Flags:u8                 bit 0: keyframe. Other bits are 0 and reserved
Compression:u8           0 none, 1 Deflate (RFC 1951, no zlib wrapper). Other values are reserved
SnapshotCount:u32        records at the start of the payload that are the snapshot (§6)
RecordCount:u32          records after the snapshot
UncompressedLength:u32   length of the payload once decompressed (equal to the stored length when Compression is 0)
Payload                  SnapshotCount + RecordCount records (§4), compressed as Compression says
```

- The fixed part is 30 bytes. A new chunk field needs a `RequiredFeatures` bit, because a reader
  cannot skip fields in front of the payload.
- A chunk with the keyframe flag clear MUST have `SnapshotCount` 0. The first chunk in a file MUST
  have the keyframe flag set (its snapshot is empty), so a seek to any time has a keyframe to start from.
- A reader that meets an unknown `Compression` value MUST skip that chunk and report it (§8.2). It
  MUST reject a chunk whose `UncompressedLength` exceeds 67,108,864 or whose decompressed size differs
  from `UncompressedLength`.
- `TMin` and `TMax` bound the times of the records after the snapshot. Snapshot records have their
  own, earlier times (§6.2).

### 3.4 Index (type 4)

```
ChunkCount:varint
Chunks:        ChunkCount × ( Offset:u64  TMin:i64  TMax:i64  Flags:u8 )      25 bytes each, in file order
TrackCount:varint
Tracks:        TrackCount × ( Id:varint  FirstTime:i64  LastTime:i64  Callsign:string  Model:string )
```

`Offset` is the file offset of the first byte of the chunk's block. `Callsign` and `Model` come from
the track's last `identity` record and are for display and searching only; they are empty when
unknown. The Index only caches what a scan of the chunks would find. A reader MUST be able to work
without it (§8.3) and MUST NOT trust it over the chunks when they disagree.

### 3.5 Attach (type 5)

```
Name:string        a file name, for example "variable-names.txt"
MediaType:string   for example "text/plain; charset=utf-8"
Bytes              the rest of the body
```

Attachments carry material that explains the file. A reader that does not know an attachment ignores
it. Defined names:

- `variable-names.txt` (`text/plain; charset=utf-8`): one line per variable, `vuid` as eight lower-case
  hexadecimal digits, a tab, the variable's name, LF. It names the `Vuid` values of `variables` records so
  a person can read the file without the application's tables (§7.6). A writer SHOULD write it when
  recording stops. A later attachment of the same name replaces an earlier one.

---

## 4. Records

A chunk's payload, after decompression, is a run of records with no padding:

```
Record := Kind:varint  Track:varint  TimeDelta:zigzag  Length:varint  Body[Length]
```

- `Kind` is a kind id bound by a Schema block (§3.2).
- `Track` is a track id (§5). Track 0 means "no track" and is for records about the whole recording.
- `Time` of a record = the time of the previous record in the same run, plus `TimeDelta`. The base
  for the first record of the snapshot, and again for the first record after it, is the chunk's
  `TMin`. TimeDelta is signed, so records need not be sorted across tracks.
- `Length` is the body length. A reader MUST check that it fits the payload before reading the body
  and MUST NOT assume the body has the size its codec would produce: it hands exactly `Length` bytes
  to the codec.
- A record whose `Kind` the file's schema does not bind is damage (§8.2).

A reader that does not know a kind's name or version skips the record by `Length` (§10.2).

---

## 5. Tracks and time

A **track** is one recorded object for one stretch of its life. Its id is a varint chosen by the
writer, unique in the file, and **never reused**: an object that leaves and returns gets a new track.
Ids are numbers of this file only; they are not network ids. A track id and a kind id fit 32 bits
(the track id is stored in the `u32` `ObjectId` of bodies, below); a reader treats a larger one as damage.

Lifecycle:

1. The first record of a track MUST be `track-open`. The last record of a finished track is
   `track-close`. At most one of each per track. After `track-close` nothing is written for the track.
2. A writer MUST write `track-close` for every open track when it finishes a file normally. A file
   that ends (crash, repair) with tracks still open is valid: such a track ends at its last record.
3. An `identity` record MUST precede the track's first `position` or `object-position` record.

Time:

- Within one track, record times MUST NOT decrease in file order, and a later chunk MUST NOT hold a
  record of the track earlier than one in an earlier chunk.
- Across tracks there is no order. Writers SHOULD keep the skew between tracks below one second.
- `TMin` and `TMax` of a chunk MUST bound the times of its (non-snapshot) records. A chunk's `TMin`
  MUST NOT be less than the `TMin` of the chunk before it.

**Times and ids in bodies are not authoritative.** The record header's `Track` and `Time` are. The
`ObjectId` and `NetTime` fields inside the bodies of JFP2 codecs describe where the message came from
on a network and say nothing true about the file once it has been shifted, trimmed, appended or had its
tracks renumbered. A writer MUST store the track id in `ObjectId` and the record's time in seconds in
`NetTime`, so the body is well-formed; a reader MUST ignore both.

---

## 6. Keyframes and seeking

### 6.1 Snapshot

A chunk with the keyframe flag starts its payload with a **snapshot**: records that state, for every
track open *before* the chunk, what is needed to start playing at the chunk. They are ordinary records
of the same kinds. For each such track the snapshot MUST contain, in this order:

1. its `track-open` record (as written);
2. its latest `identity` record;
3. its latest `position` or `object-position` record, if it has had one;
4. `variables` records that together hold the merged set: every variable the track has ever had, with
   its latest value (§7.6).

`event` records are never in a snapshot: an event happens at a moment and is not state. Tracks opened
inside the chunk are not in its snapshot; their `track-open` is among its records.

### 6.2 Time of snapshot records

A snapshot record keeps the time of the record it summarises (the latest contributing record, for
`variables`), so it is before the chunk and its delta from `TMin` is zero or negative, which zigzag
makes cheap. A reader uses these times only for interpolation (the position record is the sample
before the first one in the chunk).

### 6.3 Seeking to time T

1. In the Index, take the last chunk with the keyframe flag and `TMin ≤ T`.
2. Apply its snapshot: create the tracks, set identities and variables, set the last position.
3. Apply its records, and those of the following chunks, in file order, until a record is later than
   T. Events are skipped during a seek, not applied.

The state after step 2 is **the state after all records written before the chunk**, in file order.
Records of other tracks that lie slightly beyond T in time (cross-track skew, §5) may therefore
already have taken effect. That is accepted.

---

## 7. Record kinds, version 1

A kind has a **name** and a **version**; the Schema block binds them to a file-local id. A name is
never reused for something else and a shipped version is never edited. A new field is a new version.

| Name | Version | Body |
|---|---|---|
| `track-open` | 1 | §7.1 |
| `track-close` | 1 | empty (`Length` 0) |
| `identity` | 1 | `IdentityV1Codec`, `docs/jfp2/protocol.md` §9.2 |
| `position` | 1 | `PositionV1Codec`, §9.1 there |
| `object-position` | 1 | §7.2 |
| `variables` | 1 | `VariableSyncV1Codec`, §9.3 there, with the rules of §7.6 |
| `event` | 1 | `EventV1Codec`, §9.4 there (12 bytes: `ObjectId`, `EventId`, `Data`) |

The JFP2 codecs are shared, not copied: the recorder calls the same classes the network does, so a
change to one is a change to the other (§10.4).

### 7.1 `track-open` version 1

```
OwnerKind:u8     0 local (this application's own object), 1 network (a remote pilot's), 2 recorded (played from a recording earlier)
Nickname:string  the owner's node nickname, "" if none
SourceApp:string the application that recorded the object, for example "JoinFS 26.10"
Simulator:string the simulator it was seen in, for example "MSFS2024"; "" if unknown
```

Strings follow §1.2 (varint length). Trailing bytes after the last field MUST be ignored.

### 7.2 `object-position` version 1

For non-aircraft objects (boats, vehicles, ...). Fixed size, 89 bytes, the same field order and
encodings as `PositionV1Codec` up to `AccelerationZ`, then:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | `ObjectId` (u32; §5) |
| 4 | 8 | `NetTime` (f64, seconds; §5) |
| 12 | 24 | `Latitude`, `Longitude`, `Altitude` (f64) |
| 36 | 12 | `Pitch`, `Bank`, `Heading` (f32) |
| 48 | 12 | `VelocityX`, `VelocityY`, `VelocityZ` (f32) |
| 60 | 12 | `AngularVelocityX`, `AngularVelocityY`, `AngularVelocityZ` (f32) |
| 72 | 12 | `AccelerationX`, `AccelerationY`, `AccelerationZ` (f32) |
| 84 | 4 | `Height` (f32) |
| 88 | 1 | `StateFlags` (u8; the `PositionStateFlags` bits, of which OnGround, ElevationCorrection and Paused are meaningful) |

JFP2 does not carry this kind. If it ever does, it takes this layout or defines a different version.

### 7.3 `position` and the control axes

`PositionV1Codec` stores rudder, elevator, aileron and both brakes as `int16` in steps of 1/16384, by
truncation. A recording therefore holds those five values to a precision of about 0.00006, where the
legacy `.jfs` held `f32`. This is accepted as the cost of one codec (design §4.4). The importer's
acceptance test allows for it (design §8).

### 7.4 `identity` and `StaticCgToGround`

`IdentityV1Codec` carries `Registration`, `FlightNumber`, `ClassCode`, `Wtc` and `ClassCodeConfirmed`,
which legacy `.jfs` did not. `PositionV1Codec` carries `StaticCgToGround`, with NaN meaning "not
provided". A reader MUST treat NaN as "none", not as zero.

### 7.5 Several records at one time

Writers MAY emit several records with the same time and track. A reader applies them in file order.

### 7.6 `variables`: updates, not sets

A `variables` record is an **upsert**: each entry sets the variable `Vuid` on the track to the given
value; variables the record does not mention keep their value. A variable, once set, is never
removed. The state of a track's variables at any point is the merge of every `variables` record of
the track up to that point, in file order.

- A writer MAY write full sets or only the entries that changed since the last record it wrote for
  the track. Real recordings show about 1% of the integer entries changing from one sample to the next
  (design analysis, 2026-10-11), so a writer SHOULD write changes and the first record of a track
  SHOULD be a full set.
- `VariableSyncV1Codec` holds at most 255 entries per message. A writer MUST split a larger update
  into consecutive records with the same time. A reader treats the split as one update.
- An entry's `Kind` (Int32, Float32, String8) is part of the entry. A `Vuid` is a hash of the variable's
  name; the same `Vuid` with a different `Kind` is a different variable.
- A snapshot's `variables` records (§6.1) are full sets of the merge.

---

## 8. Reading: errors, damage and repair

### 8.1 Opening

A reader opens a file in this order:

1. Check the magic. Anything else is not a JFS2 file.
2. Read the first block. It MUST be a valid Header (CRC, type 1). Apply the refusal rules of §3.1.
3. Look for a valid footer (magic at the end, CRC of the offset), then read the Index block at that
   offset. Then walk the block heads (type and length, without reading chunk bodies) from the end of the
   Header to the Index, to find the Schema and Attach blocks, which the Index does not list, and to check
   that the chunks are those the Index lists. If every step is valid, the file is *finished*. If the
   chunks and the Index disagree, the chunks win and the reader says so.
4. Otherwise, or if the Index fails its CRC, the reader scans (§8.3).

### 8.2 Damage and unknown content

| Situation | Reader |
|---|---|
| Unknown block type | skip by `Length` |
| Block CRC fails, `Length` plausible | report; skip the block; continue |
| `Length` implausible (past the end, over 64 MiB) | report; stop reading blocks; what was read before still plays |
| Chunk with unknown `Compression`, bad deflate data or wrong `UncompressedLength` | report; skip the chunk |
| Record with an unbound kind id, or a record that does not fit the payload | report; skip the rest of the chunk |
| Record of a kind the build cannot decode | skip the record; count it by kind name and version |

After a skipped chunk the state of the tracks is unreliable until the next keyframe chunk. A player
SHOULD apply nothing from the following non-keyframe chunks, only keep reading to the next keyframe,
and say so. The reader counts and names what it skipped; the user is told (design §5.6). Skipping
`track-open`, `identity`, `position` or `object-position` records because the build lacks the decoder
is not graceful: the application says so and points to `convert` instead of playing an empty scene.

### 8.3 Unfinished files and repair

A scan reads blocks from offset 8, one after the other, checking each CRC, and stops at the first
block that does not parse or whose CRC fails (§8.2's first two damage rows apply: a block that fails
its CRC but has a plausible `Length` is skipped and the scan goes on; a truncated last block ends the
scan). The scan yields the same chunk list and track list the Index would have held. A file that
has no valid footer is played this way, and repaired only when the user asks.

**Repair** completes the file in place: truncate after the last valid block (dropping a partial last
block); for every track still open, write a final chunk holding a `track-close` at the track's last
time; then write the Index and Footer. A repair never changes a valid block. At most the last chunk
is lost.

---

## 9. Writing: durability and policy

### 9.1 Durability (normative)

- A writer MUST write each block with a single contiguous write, so a crash leaves a whole block or a
  truncated one, never a block with another's bytes in the middle.
- The file MUST be valid, in the sense of §8.3, after each completed chunk is flushed to the operating
  system. A writer SHOULD ask the operating system to flush to disk at least once every few seconds
  and when it stops.
- A writer MUST write the Index and Footer when it stops normally.
- A writer MUST NOT modify a block it has written. The file is append-only until finished.

### 9.2 Policy (informative)

These are the writer's choices. They are not in the format and a reader MUST NOT depend on them.

- A chunk covers about 2 s of records; every fifth chunk (about 10 s) is a keyframe.
- Compression is 0 until a measurement shows a gain, then 1.
- A track's first `variables` record is a full set; later ones are changes.
- The writer's memory is bounded by one chunk buffer. Encoding happens on the thread that produces the
  records, into a pooled buffer; full chunks go to a writer thread, so the disk never blocks it.

### 9.3 Editing

Editing writes a **new** file and renames it over the old one. It does not change a finished file in
place. Shifting a file in time changes only the chunk headers (`TMin`, `TMax`) and the CRCs, because
record times are deltas. Appending one file to another also renumbers tracks and kind ids, which
rewrites the record headers but not the bodies.

---

## 10. Extending the format

1. An unknown **block type** is skipped by its length (§3).
2. A **kind** whose name or version a build does not know is skipped, record by record. The file
   plays without it if the kind is peripheral. A reader tells the user what it skipped (§8.2).
3. A new **field** in a record is a new **version** of that kind and a new Schema binding; a shipped
   codec is never edited.
4. A build **keeps decoders** by default and pins them with golden files. It writes only the newest
   encoder. Retiring a decoder is a decision per kind, taken only once `convert` can re-encode old files
   to the current version.
5. Kind **names**, block **types** and feature **bits** are never reused.
6. A change to the container that old readers cannot skip sets a `RequiredFeatures` bit.
7. `FormatMajor` changes only if the block structure itself changes.
8. The format and the codecs are compiled without simulator symbols, so a conditional compilation
   symbol can never change a byte of the file.

---

## Appendix A. Example file

A file with one track that opens at time 0 and closes at 1 s. 221 bytes. All values are hexadecimal bytes in
file order. The file is also produced by a short reference writer (`jfs2ref.py`), and the golden test
of the format library will pin the bytes.

```
Offset  Bytes
   0    FA 00 4A 46 53 32 0D 0A                                      Magic

   8    Header block (39 bytes)
        01                              Type 1
        1E 00 00 00                     Length 30
        01 00                           FormatMajor 1, FormatMinor 0
        00 00 00 00   00 00 00 00       RequiredFeatures 0, OptionalFeatures 0
        00 E0 25 DA FE 5B 06 00         StartUtc 1 790 000 000 000 000 (September 2026)
        01                              MetadataCount 1
        03 61 70 70   06 4A 6F 69 6E 46 53         "app" = "JoinFS"
        09 5C 38 9C                     Crc32

  47    Schema block (22 bytes): kind 1 = "track-open" v1
        02 0D 00 00 00  01  0A 74 72 61 63 6B 2D 6F 70 65 6E  01  D0 41 50 6E

  69    Schema block (23 bytes): kind 2 = "track-close" v1
        02 0E 00 00 00  02  0B 74 72 61 63 6B 2D 63 6C 6F 73 65  01  AE B8 C9 C0

  92    Chunk block (54 bytes)
        03                              Type 3
        2D 00 00 00                     Length 45
        00 00 00 00 00 00 00 00         TMin 0
        40 42 0F 00 00 00 00 00         TMax 1 000 000
        01                              Flags: keyframe
        00                              Compression: none
        00 00 00 00                     SnapshotCount 0
        02 00 00 00                     RecordCount 2
        0F 00 00 00                     UncompressedLength 15
        01 01 00 05 00 01 41 00 00      Record: kind 1, track 1, delta 0, length 5,
                                        body: owner 0, "A", "", ""
        02 01 80 89 7A 00               Record: kind 2, track 1, delta +1 000 000 (zigzag 2 000 000 =
                                        80 89 7A), length 0
        E3 AA AB 75                     Crc32

 146    Index block (55 bytes)
        04  2E 00 00 00
        01                              ChunkCount 1
        5C 00 00 00 00 00 00 00         Offset 92
        00 00 00 00 00 00 00 00         TMin 0
        40 42 0F 00 00 00 00 00         TMax 1 000 000
        01                              Flags
        01                              TrackCount 1
        01                              Id 1
        00 00 00 00 00 00 00 00         FirstTime 0
        40 42 0F 00 00 00 00 00         LastTime 1 000 000
        00  00                          Callsign "", Model ""
        51 2B F1 72                     Crc32

 201    Footer (20 bytes)
        92 00 00 00 00 00 00 00         IndexOffset 146
        E5 6C 87 08                     Crc32
        4A 46 53 32 45 4E 44 0A         "JFS2END\n"
```

---

## Appendix B. Constants

| Name | Value |
|---|---|
| Magic | `FA 00 4A 46 53 32 0D 0A` |
| End magic | `4A 46 53 32 45 4E 44 0A` ("JFS2END\n") |
| FormatMajor / FormatMinor | 1 / 0 |
| Block types | 1 Header, 2 Schema, 3 Chunk, 4 Index, 5 Attach |
| Chunk flag bit 0 | keyframe |
| Compression | 0 none, 1 Deflate |
| Largest block, largest uncompressed chunk | 67,108,864 (64 MiB) |
| Largest metadata string | 65,535 bytes |
| Largest `variables` record | 255 entries |
| Fixed chunk header | 30 bytes |
| Footer | 20 bytes |
| Extension | `.jfs2` |

---

## Appendix C. Decisions

Beyond or against the design (`recorder-v2-design.md`). All ten were approved on 2026-10-12.

1. **Time is a signed delta from the previous record**, not an unsigned delta from the chunk start
   (design §4.5). One byte less per record, and a snapshot can carry the original times of what it
   summarises. Cost: a reader cannot know a record's time without reading the ones before it in the
   chunk. Chunks are read whole, so this costs nothing.
2. **Times and ids in bodies are not authoritative** (§5). This is what lets Trim, Append and Remove work
   without re-encoding bodies, and it resolves design question 1: the 12 redundant bytes of
   `PositionV1` stay, as a rule rather than a hope for `PositionV2`.
3. **The snapshot holds the latest position too** (§6.1), which the design did not list. Without it
   there is no sample before the first position of the chunk to interpolate from.
4. **`variables` records are upserts** (§7.6), and a writer SHOULD write changes. This turns the
   variable data, the largest part of today's files (45–58% integers, 18–22% floats), from about
   600 bytes per sample into a few. Keyframes keep seeking cheap.
5. **The first chunk is always a keyframe** (§3.3), and the Index is a cache that must be rebuildable
   from the chunks (§3.4), so repair is a scan.
6. **Kind ids are file-local and the track-open, track-close and object-position kinds are private to
   the recorder** (§7). They take no JFP2 message class. Design question 2 (JFP2 adopting
   `object-position`) stays open and independent.
7. **No new feature bits and no `OptionalFeatures` are defined** in edition 1; both exist so that a
   container change need not be a new major version.
8. **A damaged chunk stops non-keyframe playback until the next keyframe** (§8.2), because the state
   would be wrong, rather than playing on with wrong values.
9. **The `variable-names.txt` attachment** is a plain tab-separated text file, written when recording
   stops, and absent after a crash (§3.5). The application's own tables are then the fallback.
10. **Editing writes a new file and renames** (§9.3), as the design says. A shift in time is a
    header-only change, a property of the delta times.
