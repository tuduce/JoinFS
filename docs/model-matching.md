# Model matching

How JoinFS decides which installed model stands in for a remote aircraft. Two engines exist side by side; **Classic is the default**, the
**New** engine is selected with the setting `ModelMatchingEngine` (0 = Classic, 1 = New; stored in the user settings).

## Where it lives

| What | Where |
|---|---|
| Entry point (unchanged signature) | `Substitution.Match(...)` builds a `MatchRequest` and calls `Substitution.Resolve` |
| Engine switch | `Substitution.Resolve` -> `MatchClassic` or `MatchWithNewEngine` (`Substitution.engine`) |
| New engine | `JoinFS/Matching/`: `CombinedMatcher`, `SimilarityScorer`, `SpecResolver`, `ModelSpecCache`, `AirlineResolver`, `RelatedTypes`, `ReferenceSpecs`, `IdentityScorer` |
| Identity signals (shared by both engines) | `Substitution.ScoreCandidate` (the new engine passes the typerole as a weak hint: no -240) |
| Reference data | `Resources/aircraft-specs.json` (embedded), `XPMP2_Doc8643.dat`, `XPMP2_related.dat`, `ICAO_Airlines.dat` |
| Measured data | read during the model scan from each model's `aircraft.cfg` / `flight_model.cfg` / `engines.cfg` (`MeasuredSpecsReader`); cached in `specs - <sim>.txt` (`SpecCacheFile`) |
| X-Plane | CSL `ICAO` / `AIRLINE` / `LIVERY` lines now give models an ICAO type and airline (`XsbEntry`); XPMP2 still makes the final CSL choice in the plugin |
| Tests | `JoinFS.Tests`: `SubstitutionMatchCharacterizationTests` (pins Classic), `NewMatchingEngineTests`, `NewMatchingEnginePerformanceTests`, `MeasuredSpecsReaderTests`, `SpecCacheFileTests`, `XsbEntryTests` |

Constraints kept: no change to the network protocol, recordings or plugin interfaces; no new typeroles; the enums are append-only; the
`models - <sim>.txt` and `matching - <sim>.txt` formats are unchanged (measured data goes into the separate `specs - <sim>.txt`).
Livery is used only where `Match` already receives it (FS2024); on every other build matching works on the aircraft title.

## Extending the aircraft data

`Resources/aircraft-specs.json` holds facts per ICAO type (span, length, weight, engines, cruise speed, gear, manufacturer), curated aliases for wrong
type tags (`500E` -> `H500`) and title hints that identify untagged models. To add or correct a type: edit the row, keep every designator valid in Doc8643
(a test checks that), run the tests. Values are compiled from public specifications and still to be verified row by row against type-certificate data
sheets; do not copy text or tables from any source. A measured value from a model's own files always outranks a reference row.

The remainder of this document is the rule set as implemented.

---

**Order of execution (Combined):**

1. Tier 1 - Substitute, on the request exactly as sent. A hit ends the match.
2. Tier 2 - Original, on the request exactly as sent. A hit ends the match.
3. Correct the request: type tag (alias, punctuation), airline, typerole (section 2). Notes are added to the matching steps.
4. Tier 3 - score every installed model: identity signals (including related types), physical similarity, same-manufacturer bonus.
5. Plausibility gate, acceptance, deterministic ordering. The best acceptable model wins.
6. Tier 4/5 - configured default for the typerole, else the last resort (Combined refuses an implausible model).

## Contents

1. [Inputs](#1-inputs)
2. [Correct the request before scoring (Combined only)](#2-correct-the-request-before-scoring-combined-only)
3. [Tier 1 - Substitute](#3-tier-1---substitute)
4. [Tier 2 - Original](#4-tier-2---original)
5. [Tier 3 - scoring](#5-tier-3---scoring)
6. [Physical similarity](#6-physical-similarity-combined-only)
7. [Plausibility gate and acceptance](#7-plausibility-gate-and-acceptance)
8. [Ordering](#8-ordering-combined-only)
9. [Tier 4/5 - Default and last resort](#9-tier-45---default-and-last-resort)
10. [Where physical data comes from](#10-where-physical-data-comes-from)
11. [Integration constraints (no JoinFS protocol change)](#11-integration-constraints-no-joinfs-protocol-change)
12. [What Explain Match shows](#12-what-explain-match-shows)

---

## 1. Inputs

**The request** (the remote aircraft): title, livery, ICAO type, ICAO airline, `atc_airline` (optional), class code, WTC,
typerole, registration. From an Explain Match report, a debug bundle, `--icao/--title/...` options, or a mix (options override).

**The installed models** (`models - <sim>.txt`): title, livery/variation, ICAO type, WTC, ICAO airline, class code, registration
(`atc_id`), typerole, folder. Optionally enriched with measured data from `aircraft.cfg` / `flight_model.cfg` (`--sim-folder`)
or live from the simulator (`--simconnect`).

**Reference data** (`data/`): `Doc8643.dat` (type to class code, WTC, manufacturer), `aircraft-specs.json` (size, weight, speeds,
engines per ICAO type, aliases, title hints), `ICAO_Airlines.dat` (airline code, IATA code, name), `related.dat` (families of
visually similar ICAO types, used by the Combined engine).

**JoinFS settings** (`matching - <sim>.txt`): the user's substitutions (title to model) and the default model per typerole.

If the class code is missing, it and the WTC are looked up from the ICAO type in Doc8643.

For a **manual request** (no report) without `--typerole`, the typerole is derived the way JoinFS derives it for models: `L1P` SingleProp, `L2P` TwinProp, `L4P`/`L4T` FourProp, helicopters Rotorcraft, medium/heavy twin-jets Airliner (plus, where JoinFS has no role: 3 prop engines `L3P`/`L3T` -> TwinProp, 5 or more `L6P`... -> FourProp). Nothing derivable: SingleProp. A report's own typerole is never replaced.

---

## 2. Correct the request before scoring (Combined only)

Wrong tags are common, so the Combined engine repairs them - but **only after Tier 1 (Substitute) and Tier 2 (Original) have run on the request exactly as it was sent**. A user's substitution or an exact installed title is an explicit choice and is never second-guessed; the corrections apply only when matching reaches the scoring tiers (Tier 3 onward). **The report always shows what was asked for**
(`MD11F`, `FEDEX`); the corrections are listed in the matching steps and the "Why this model" section, and the corrected values
are what the scoring tiers below work with. JoinFS today works on the raw values.

The corrections run in the order of the subsections below: type, then airline, then typerole.

### 2.1 ICAO type

If the type is an **alias** in `aircraft-specs.json` (`500E` -> `H500`, `MD11F` -> `MD11`,
`DA20` -> `DV20`), it is replaced by the aliased type. A curated alias wins even over a real Doc8643 designator, because simulators reuse codes (MSFS tags the Stemme S12 `S12`, which Doc8643 lists as the Spencer Air Car). Punctuation and spaces are ignored when looking a tag up (`JU-52` -> `JU52`,
`B 738` -> `B738`). Unknown tags without an alias stay as they are.

### 2.2 Airline

atc_airline (`FDX`) and icao_airline (`FEDEX`) are often mixed up, and the operator is often only in the title (`FedEx N234234`).
`AirlineResolver` settles the airline, **only accepting unambiguous results**:

1. The ICAO airline is a known 3-letter code in `ICAO_Airlines.dat`: keep it. (Not guessed.)
2. Otherwise, for the ICAO-airline value first, then the `atc_airline` value, the first rule that gives **exactly one airline** wins:
   1. it is a known ICAO airline code;
   2. it is a 2-letter IATA code that exactly one airline has (`FX` -> `FDX`);
   3. it equals an airline name or callsign once case, punctuation and generic words (Air, Airlines, Airways, Express,
      International, Cargo, Ltd, ...) are ignored (`FEDEX`, `FedEx` -> "FedEx Express" -> `FDX`);
   4. full-text search: it (4+ letters) appears as a whole word in the name of exactly one airline.
3. Otherwise the title and livery are searched for the longest airline name (4+ letters, up to three words) that identifies exactly one airline.
4. Otherwise nothing is changed.

A **lookup** (rules 2.1-2.3: an ICAO code in the wrong field, a unique IATA code, an exact name/callsign) is trusted and scores the full airline points; a **guess** (rule 2.4, a word found in an airline name, and rule 3, a name found in the title/livery) is marked *guessed* and its airline points are multiplied by 0.2 (see [5](#5-tier-3---scoring)). A value that is ambiguous
("Pacific" matches several airlines) is never used.

When the (corrected) airline is a valid ICAO code but **no installed model carries it**, a step says so (`'SAA' (South African
Airways) ... no installed model carries it`): the airline signal cannot score then, and the choice rests on type, class, WTC and similarity.

### 2.3 Typerole - the class code overrules it

Not every aircraft has a fitting typerole (JoinFS has none for a tri-motor), while the class code (given, or looked up from the ICAO
type) always describes the airframe. When a role can be derived from the class code (see section 1) and differs from the stated typerole,
the derived one is used and a note says so. The report's Requested column shows the role that was used with the stated one for reference (`Airliner (stated: SingleProp)`). In scoring, the typerole stays a **weak hint**: the Combined engine gives +15 for an equal role and **never applies the -240 mismatch penalty** (JoinFS today keeps it). No new typeroles are introduced; the Doc 8643 class code and the physical features carry what the roles cannot express.

---

## 3. Tier 1 - Substitute

If the user has a substitution for the request's **title** (Settings > Model Matching) and the target model is installed (with the
livery, when the request has one), that model is the result: `MatchType.Substitute`. If the target is not installed the step is
noted and matching continues.

## 4. Tier 2 - Original

If an installed model has **exactly the requested title** (and livery, when given): that model is the result: `MatchType.Original`.
When the title is installed but not with that livery, the report lists the installed liveries.

Both tiers are identical in both engines and use the request as sent (uncorrected). The corrections of section 2 come after them.

---

## 5. Tier 3 - scoring

Every candidate gets a score as the sum of independent signals (`Substitution.ScoreCandidate`):

| Signal | Points | Condition |
|---|---:|---|
| ICAO type | **+200** | same ICAO type (case-insensitive) |
| ICAO airline | **+100** | same airline code |
| Class code | **+60** | same 3-character class code (e.g. `L2J`) |
| ... engine count | +20 | additionally, same middle character |
| ... engine type | +20 | additionally, same last character |
| WTC | **+40** | same wake category |
| Typerole | +15 | same typerole |
| Typerole mismatch | **-240** | typerole differs **and** the ICAO type is not an exact match (cancels coincidental airline + class + WTC = 240). Combined: **never applied** - the typerole is only a weak hint (+15 when equal); the class code, the similarity and the plausibility gate decide |
| Related ICAO type (Combined only) | **+120** | the model's type is a *different* member of the same `related.dat` family as the requested one (`A320` for an `A20N`); not added on top of an exact type |
| Registration | +15 | equals the model's `atc_id` (letters/digits only) |
| Registration (weak) | +5 | model has no `atc_id` but title/livery contains the registration |
| Title prefix | +min(n, 25) | shared start of the titles, n = common characters, only when n >= 4 |
| Livery word | +1 | first livery word of 4+ letters that appears in the model's livery |

**Related types** (`related.dat`, e.g. `A318 A319 A320 A321 A19N A20N A21N` are one family) get `RelatedTypePoints` = **120** in the
Combined engine (x0.2 when the model's type is guessed): between an airline match (100) and the exact type (200). So an SAA A320 beats
another airline's exact A20N, while an exact type of the right airline still wins. JoinFS today does not use the families.

**Untagged models (Combined only):** a model with no ICAO type at all whose title names the requested type through a title hint (`S12-G: Passengers` for
a Stemme S12) is the aircraft itself. It earns type, class and WTC points at `TitleInferredTypeFactor` = **75%** (150 / 75 / 30), via the class code and WTC
Doc8643 lists for that type.

**Guessed values** are worth a fifth: when the model's ICAO type was guessed, ICAO type, class code and WTC points are
multiplied by **0.2**; when the airline was guessed (on the model **or** resolved from a wrong field on the request), the
airline points are multiplied by 0.2.

**Candidate pool (JoinFS today):** installed models sharing the ICAO type, the class code, the loose category (first and third
class character), or the typerole; if that pool is empty, all installed models. The best score wins if it is at least **20**
(`MinMatchScore`). The match type is `Icao` (same ICAO type), `Category` (same class code), else `Auto`.

**Candidate pool (Combined):** *all* installed models are scored with the same signals, then the similarity below is added.

---

## 6. Physical similarity (Combined only)

Aim: replace a missing 747-8 with a 747-400 and never with a Cessna. Each candidate and the request get a set of physical
features; **a feature only counts when both sides know it** (no guessing, no penalty for unknown data).

| Feature | Weight | Distance (0 = identical, 1 = completely different) |
|---|---:|---|
| Weight (MTOW) | 3.0 | log ratio, saturating at a factor 4 |
| Wing span (rotor diameter) | 2.0 | log ratio, saturating at a factor 2 |
| Length | 1.5 | log ratio, saturating at a factor 2 |
| Cruise speed | 2.0 | log ratio, saturating at a factor 2 |
| Engine count | 2.0 | same 0, one apart 0.6, more 1.0 |
| Engine type | 2.0 | same 0 (a pure glider is engine type *none*, 0 engines); piston/turboprop 0.5; turboprop/turboshaft 0.4; turboprop/jet 0.6; piston/turboshaft 0.7; piston/electric 0.4; jet/turboshaft 0.9; otherwise 1.0 |
| Wing position | 1.0 | same 0; mid vs high/low 0.5; rotor, biplane or high vs low 1.0 |
| Landing gear | 1.5 | same 0; tricycle/taildragger 0.6; floats vs wheels 0.8; otherwise 1.0 |

- **Score** = sum over compared features of weight x (1 - distance) / sum of compared weights (0 to 1).
- **Confidence** = compared weight / total weight of all features (0 to 1). Little data means low confidence. Exception: when no size feature (weight, span, length) could be compared, confidence is measured against engine count + engine type + gear only (the features that then decide), so a complete engine/gear comparison counts as full confidence.
- **Similarity points** = round(100 x score x confidence) - at most 100, i.e. between an airline match (100) and an ICAO type (200).

**Same-manufacturer bonus (+40):** the candidate's manufacturer equals the request's (curated reference, then Doc8643, normalized),
both are rotorcraft or both fixed wing, **and** the size is similar: weights within a factor **2.5**, or (without weights) spans within **1.6**.
So an R22 beats another make as stand-in for an R44, and a Cessna 172 never borrows a Citation. The bonus is not given to gated candidates.

**Combined score** = identity score (section 5, including related types) + similarity points + same-manufacturer bonus.

---

## 7. Plausibility gate and acceptance

A candidate is **excluded** ("implausible") regardless of score when

- one is a rotorcraft and the other is fixed wing, or
- the weights differ by more than a factor **8**.

Excluded candidates are ranked last and cannot win.

The best candidate is **accepted** when its combined score is at least **20** (`MinMatchScore`) and, if the comparison had any data
(confidence > 0), either its similarity score is at least **0.5** or its ICAO type matched (exactly, or as a related type). If nobody qualifies the
engine goes on to the default tier. The match type is set as in section 5.

---

## 8. Ordering (Combined only)

Candidates are sorted deterministically, so the same inputs always give the same answer:

1. non-excluded before excluded,
2. higher combined score,
3. higher similarity score,
4. title, then variation (ordinal alphabetical).

---

## 9. Tier 4/5 - Default and last resort

1. **Default:** the user's configured default model for the request's typerole, used when it is installed and (Combined) not implausible: `MatchType.Default`.
2. **Last resort**
   - *JoinFS today:* the first installed model in the scan list, whatever it is.
   - *Combined:* **refuses**; no model is better than an implausible one (a light single for a wide-body). The report says so.
   - With no models installed at all, neither can choose.

---

## 10. Where physical data comes from

Best source first, per feature (`SpecResolver`); the report shows the source of every value.

For the **request** (remote aircraft):

1. Public reference for its ICAO type (after alias correction).
2. If the type has no entry: the type inferred from the title - the longest of the title hints / names / ICAO code / aliases of any
   reference entry that appears as a whole word in the title.
3. Coarse values derived from the class code and typerole (engine count, engine type, rotor) - low confidence.

**Aircraft without an ICAO designator** (Wright Flyer, Spirit of St. Louis, Do X, Latecoere 631, Boeing 307, Gee Bee ...) have reference rows with a
pseudo key (`X-...`, flagged `nonIcao`) and are found by step 2, the title. That works for the request and for installed models alike; their
type tag in the simulator is usually blank or `ZZZZ`.

For an **installed model**:

1. Measured values: live from the simulator (SimConnect), else from its `aircraft.cfg` / `flight_model.cfg` (span, weight, speeds, engines).
2. Missing values from the public reference for its ICAO type, or the type inferred from its title and livery.
3. Coarse class-code values; a model whose typerole is Glider and has no class code is a glider: no engine, not a rotorcraft.

**Manufacturer** comes from the curated reference, else the majority maker for the type in Doc8643, else the cfg; Doc8643
outranks the cfg because add-on authors often put the wrong value there (for example the type code).

---

## 11. Integration constraints (no JoinFS protocol change)

Everything above works with what JoinFS already has, so merging it into JoinFS needs **no change to the network protocol, recordings or
plugin interfaces**:

- **No new typeroles.** The typerole IDs are untouched; the Combined engine only *reads* the existing ones and derives an existing role from
  the class code. Its typerole is a weak hint (+15).
- **`MatchRequest` / `Model` carry nothing new on the wire.** `atc_airline` is optional local input (not sent by remote clients),
  `IcaoAirlineGuessed` / `TyperoleDerived` are local flags, and `EngineKind.None` (gliders) exists only inside this matcher.
- **Data is local:** `aircraft-specs.json`, `related.dat` and `ICAO_Airlines.dat` are read from files next to JoinFS; models keep their
  existing fields (title, livery, ICAO type, WTC, airline, class code, registration, typerole).
- **Tiers 1 and 2 (Substitute, Original) and the JoinFS file formats (`models - ...txt`, `matching - ...txt`) are unchanged.**

## 12. What Explain Match shows

Explain Match (right-click a remote aircraft) shows the trace the selected engine produced: the requested-versus-matched attribute grid with the
points each attribute earned, the matching steps (every tier in order, with the request corrections that were applied - type alias, airline
resolution, typerole from the class code - and the "no installed model carries this airline" note), and the best other candidates with the reason for
each score (identity signals, similarity, same-manufacturer bonus). With the New engine the requested type and airline are always shown as received;
corrections appear in the steps. The trace is plain text built in English like the Classic steps. Dedicated dialog sections (feature-by-feature table,
side-by-side with the Classic result) are future work and need localized strings in every language.
