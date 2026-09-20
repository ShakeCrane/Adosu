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
- The eighteen executable micro/regression tests pass, including both real supplied fixtures: ordering across floors, malformed-floor retention, non-object action retention, source-order preservation, and the real-fixture statistics.

### Malformed input strategy

- An action whose `floor` is null, negative, or beyond the resolved floor range is retained as `GameplayStateChangeKind.Unknown` with its raw source record and a stable warning-level `ADF-FLOOR-RANGE` diagnostic. A missing floor keeps a `null` provenance floor index; it is never disguised as `-1`.
- A non-object entry in `actions[]` is retained as an `SourceEvent` plus an unknown gameplay state with its raw JSON text; it is diagnosed with `ADF007` (reader) and `ADF-FLOOR-RANGE` (resolver). It is not discarded.
- Derived event tracks over real time (`GameplayState.Changes`, `PresentationEvents`, `TempoTrack.Events`) are ordered by real time then `Provenance.SourceIndex` over the full list. No `Dictionary<timestamp,event>` is used to group or replace events.

### INFERRED / externally corroborated

- The `pathData` table and relative path tokens follow the public `adofaiex/ADOFAI-JS` implementation. `!` is represented as angle `999` there and in the baseline, but the supplied file alone cannot prove the game semantics.
- The floor geometry/timing baseline follows the public `adofaiex/SharpFAI` `FloorTimingCalculator` structure: absolute exit angle, entry/exit travel, full-circle fallback for non-midspin near-zero travel, Twirl rotation state, piecewise SetSpeed, and separate handling for Pause/Hold/MultiPlanet.
- `SetSpeed` `Bpm`, `Multiplier`, and ordered same-offset application are represented. Non-zero `angleOffset` is retained as a piecewise tempo boundary; it is not collapsed into a single floor BPM.

### UNKNOWN

- Whether every path token mapping and the `!`/999 timing behavior exactly matches the current game across all format versions.
- Exact game semantics and timing units for ADOFAI Hold, Pause, FreeRoam, MultiPlanet edge cases, and modern Multitap simultaneity.
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
