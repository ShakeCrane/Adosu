# M0-A semantic timing baseline

Status: implemented locally on 2026-09-18. This is a reader and semantic/timing baseline, not a complete converter or serializer.

## Repository and test surface

- The selected worktree is `D:\Project\adosu!`; its remote is `https://github.com/ShakeCrane/Adosu` and the starting commit is `60830e2` (`Initial commit`).
- The starting Git tree contained only `LICENSE`; the existing research documents and `.sample/` are preserved as pre-existing working-tree material.
- The formal implementation is a dependency-free .NET 10 library in `src/Adosu.Core` with a self-contained executable test suite in `tests/Adosu.Tests`.
- `.sample/` remains read-only and is excluded from Git by `.gitignore`.

## Semantic model

`GameplayChart` keeps five separate concerns:

1. `InputTrack`: ordered taps/holds. `Lane` is nullable, so ordinary ADOFAI floors do not acquire a fake mania lane. Exact same-time input objects remain separate and `Chords()` derives groups without replacing events.
2. `TempoTrack`: red osu! timing points and ADOFAI `SetSpeed` events/derived piecewise segments.
3. `ScrollPresentationTrack`: green osu!mania SV events plus presentation-only ADOFAI actions.
4. `GameplayState`: Twirl, Pause, Hold, FreeRoam, MultiPlanet, and Multitap state changes, including raw provenance.
5. `SourceEvent`/`SourceProvenance`: source format, stable source index, floor/object index, original timestamp/token, and raw source record where available.

No `Dictionary<timestamp,event>` is used for source timing points, actions, or hit objects. Decimal source tokens are retained alongside derived `double` values; serialization precision is not silently chosen by the reader.

## Evidence status

### VERIFIED

- The supplied ADOFAI fixture is a BOM-prefixed JSON v2 chart with `pathData`, `settings`, and `actions`, base BPM 50, offset 30, 217 path tokens, 265 actions, 8 `!` glyphs, 41 `SetSpeed` actions, and 8 Twirl actions.
- In the supplied osu!mania SV fixture: `Mode=3`, `CircleSize=4`, 5,982 hit objects, 5,575 taps, 407 holds, 57 red timing points, 3,333 green timing points, maximum exact-time chord size 4, and hit-time range 1,765-417,332 ms (independently recomputed from the file).
- The official osu! file-format reference defines uninherited timing points as beat duration/BPM and inherited timing points as negative inverse SV percentage. It defines mania holds as `endTime:hitSample` and the lane formula as `floor(x * columnCount / 512)` clamped to the key range.
- The implementation keeps green SV in `ScrollPresentationTrack`; the resolver never rewrites `InputTrack` timestamps from SV.
- The executable micro/regression tests originally passed as eighteen, then twenty-five after the P1 fixes, thirty-one after the P2 duration fix and thirty-four after the Pause canonical-tempo follow-up; they include both real supplied fixtures: ordering across floors, malformed-floor retention, non-object action retention, source-order preservation, duration-legality rejection, and the real-fixture statistics.

### P1 correctness fixes (2026-09-20)

- Same-time state order is no longer conflated with presentation order. `GameplayStateChange.ApplicationOrder` and `TempoEvent.ApplicationOrder` carry the semantic application ordinal (floor traversal, then same-floor source order); `GameplayState.ChangesInApplicationOrder` and `TwirlAt(t)` replay by that ordinal. `pathData: "R!!R"` with `Twirl` on floor 1 (source 1) and floor 2 (source 0) now resolves to a final non-twirled state (`TwirlAt(1) == false`) with consistent `TwirlStateAfter` snapshots; the `(TimeSeconds, SourceIndex)` list remains the deterministic display order. The same split covers cross-floor same-time `SetSpeed`.
- `SetSpeed` legacy/illegal inputs are validated before touching canonical tempo/time state. Non-finite, zero, negative, missing and overflow/underflow-producing `beatsPerMinute`/`bpmMultiplier` candidates are rejected with the stable `ADF-SPEED-VALUE` error, keep raw provenance, expose canonical-empty `Bpm`/`Multiplier` (`TempoEvent.Application == Rejected`) and do not alter BPM, segments or later times. Legality is decided by the computed finite/positive duration invariant (worst-case full-floor travel), not by an arbitrary `Math.Max(bpm, 1e-12)` clamp. Non-finite `angleOffset` emits an explicit error-severity `ADF-SPEED-OFFSET` rather than a silent clamp.

### P2 correctness fix: Pause/Hold/FreeRoam duration invariant (2026-09-20)

- The `duration` field of `Pause`, `Hold` and `FreeRoam` is now validated with the same finite/non-negative rule as tempo values. If the field is present it must be `double.IsFinite(x) && x >= 0`; `Pause` allows `0`. A missing or non-numeric duration is treated as illegal, never defaulted.
- An illegal duration is rejected, not repaired: there is no clamp, absolute value, epsilon or implicit default. The action stays in `GameplayState.Changes` with its raw JSON, `SourceIndex`/`FloorIndex`/`ApplicationOrder` provenance, carries `GameplayStateChangeApplication.Rejected`, exposes a canonical-empty `DurationBeats` (`null`) and emits the stable error `ADF-DURATION-VALUE`. A `Hold` on the same floor also leaves `InputEvent.DurationBeats` canonical-empty so a rejected value cannot reappear as applied.
- `Pause` is validated before it is applied. The candidate accumulated beats (`stateDurationBeats + duration`), the derived seconds (`duration * 60 / currentBpm`), the resulting floor start (`floorStart + seconds`) and the floor span must all stay finite and non-negative; a violation rejects only that action while keeping the previously valid tempo/state. A legal `Pause` retains its existing `ADF-PAUSE-INFERRED` warning-level semantic and its provenance, so Pause is still not promoted past `INFERRED`.
- A final canonical time invariant is checked after floor resolution: every floor start, input time, gameplay-state time and tempo segment boundary is finite, adjacent floor starts are non-decreasing, and no segment runs backwards. A violation emits an explicit `ADF-DURATION-VALUE` error instead of silently exposing non-finite times downstream.
- `SetSpeedMode.Unknown` needed no model change: it keeps its existing `Rejected` + canonical-empty + `ADF-SPEED-TYPE` warning path.
- Precision limitation (not a threshold): a finite segment can still underflow to the same instant as its neighbour (for example a maximal legal BPM with `angleOffset: 5e-324`). The invariant deliberately permits equal, non-decreasing times; no minimum spacing, `BitIncrement` or fabricated gap is introduced. Source angles and provenance are retained.

### P2 follow-up: Pause validates against the canonical floor-end tempo (2026-09-20)

- `Pause` seconds are no longer derived from the entry BPM that happens to be current when the action is read. The raw `duration` is checked in the single source-ordered pass; the candidate is then committed after `ResolveFloorTempo`, using the canonical floor-end BPM and the same `beats * 60 / BPM` expression that advances the floor. `SetSpeed` included, a floor's final tempo is what the Pause's candidate duration is measured against.
- Each candidate is a transaction over the Pauses already accepted on that floor: the accumulated raw beats (`acceptedBeats + duration`) must stay finite and non-negative, the seconds derived from the accumulated beats must stay finite and non-negative, and `floorStart + travel + accumulatedSeconds` must stay finite. A violation rejects only the offending Pause.
- This closes two overflows outside the earlier single-Pause check: (a) two individually representable Pauses whose accumulated beats overflow the beat-to-second step, e.g. two `2e306`-beat Pauses at base BPM `1e307` (`4e306 * 60` overflows) — the first is `Applied`, the second `Rejected`; and (b) a legal `SetSpeed Bpm=1e-300@offset 0` followed by a huge but individually finite Pause, where the Pause is unrepresentable in seconds at the new floor-end tempo regardless of source order.
- A rejected Pause keeps its raw record, full `SourceProvenance`, original `TimeSeconds` and application ordinal, with a canonical-empty `DurationBeats` and a stable `ADF-DURATION-VALUE` error carrying the action provenance. The accepted Pauses, `Twirl`, `MultiPlanet`, `SetSpeed` and the application order are untouched; the floor advance reuses the returned accepted seconds verbatim instead of recomputing them.
- `ADF-PAUSE-INFERRED` still fires only when an accepted Pause contributes beats, and Pause remains `INFERRED`, never `VERIFIED`.
- The thirty-four executable tests pass, including the P1, P2 and Pause canonical-tempo follow-up regressions.

### Malformed input strategy

- An action whose `floor` is null, negative, or beyond the resolved floor range is retained as `GameplayStateChangeKind.Unknown` with its raw source record and a stable warning-level `ADF-FLOOR-RANGE` diagnostic. A missing floor keeps a `null` provenance floor index; it is never disguised as `-1`.
- A non-object entry in `actions[]` is retained as an `SourceEvent` plus an unknown gameplay state with its raw JSON text; it is diagnosed with `ADF007` (reader) and `ADF-FLOOR-RANGE` (resolver). It is not discarded.
- Derived event tracks over real time (`GameplayState.Changes`, `PresentationEvents`, `TempoTrack.Events`) are ordered by real time then `Provenance.SourceIndex` over the full list. No `Dictionary<timestamp,event>` is used to group or replace events.
- The `(TimeSeconds, SourceIndex)` order is presentation only. State replay uses the separate semantic application ordinal; presentation order never decides canonical BPM, segments or Twirl state.

### INFERRED / externally corroborated

- The `pathData` table and relative path tokens follow the public `adofaiex/ADOFAI-JS` implementation. `!` is represented as angle `999` there and in the baseline, but the supplied file alone cannot prove the game semantics.
- The floor geometry/timing baseline follows the public `adofaiex/SharpFAI` `FloorTimingCalculator` structure: absolute exit angle, entry/exit travel, full-circle fallback for non-midspin near-zero travel, Twirl rotation state, piecewise SetSpeed, and separate handling for Pause/Hold/MultiPlanet.
- `SetSpeed` `Bpm`, `Multiplier`, and ordered same-offset application are represented. Non-zero `angleOffset` is retained as a piecewise tempo boundary; it is not collapsed into a single floor BPM.

### UNKNOWN

- Whether every path token mapping and the `!`/999 timing behavior exactly matches the current game across all format versions.
- Exact game semantics and timing units for ADOFAI Hold, Pause, FreeRoam, MultiPlanet edge cases, and modern Multitap simultaneity. The P2 fix and its canonical-tempo follow-up constrain only the legality and numeric representability of `duration` (finite, non-negative, and representable in seconds at the floor-end tempo); they do not promote the game-time rule of Pause, the Hold end time or the FreeRoam time rule to VERIFIED. Illegal durations are rejected rather than mapped to a guessed default.
- Same-floor SetSpeed behavior for every combination of multiple same-offset actions in the game. The model preserves source order and applies the reference convention, but this is not presented as official truth.
- ADOFAI `angleData`/`pathData` version compatibility outside the observed v2 fixture.

Unknown ADOFAI mechanisms are preserved as state/source records and produce diagnostics; they are not silently removed or converted to mania chords.

## External evidence

- Official osu! format: https://osu.ppy.sh/wiki/en/Client/File_formats/osu_(file_format)
- osu!mania CircleSize/key count: https://osu.ppy.sh/wiki/en/Beatmap/Circle_size
- ADOFAI-JS path table/reference parser: https://github.com/adofaiex/ADOFAI-JS/blob/main/src/pathdata/index.ts
- SharpFAI timing reference: https://github.com/adofaiex/SharpFAI/blob/main/SharpFAI/Util/FloorTimingCalculator.cs

Retrieved/reviewed 2026-09-18. The external implementations are evidence, not a replacement for game truth.

## M0-B entry boundary

The next phase must add a deliberate writer/converter only after choosing user-facing degradation policies for lane assignment, chords, ADOFAI Hold/Multitap, `999`, and unsupported state. This baseline intentionally stops before a complete bidirectional converter.
