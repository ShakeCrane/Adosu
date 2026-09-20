# Sample Corpus Index

> Derived research index. Generated 2026-09-17.  
> Source tree: `.sample/` (read-only). This file must not be used to modify original samples.  
> Does not contain reconstructable chart bodies, audio, or images.

## Provenance of this index

| Field | Value |
| --- | --- |
| Generated | 2026-09-17 |
| Method | Local read-only scan + UTF-8 JSON parse + `.osu` section parse |
| Original samples modified | No |
| Uploaded off-machine | No |
| Git tracked at generation | `.sample/` was untracked |

## Layout

Actual on-disk layout (do not restructure to match the recommended `provided/` / `_external/` split):

```text
.sample/
├── ADOFAI-sample/      # 1 chart + audio + images; treated as USER PROVIDED
└── osu!mania-sample/   # 1 beatmapset (4 difficulties) + audio + images + .osb
```

Origin classification:

| Path | Origin | Evidence |
| --- | --- | --- |
| `.sample/ADOFAI-sample/` | USER PROVIDED | Present in working tree; not Agent-downloaded this round |
| `.sample/osu!mania-sample/` | USER PROVIDED | Present in working tree; not Agent-downloaded this round |
| `.sample/_external/` | absent | Directory does not exist |
| `.sample/provided/` | absent | Directory does not exist |

Totals: 28 files, 20_365_523 bytes.

## Chart files

| Rel path | Bytes | SHA-256 |
| --- | --- | --- |
| `ADOFAI-sample/main.adofai` | 51428 | `5A395E3B6AC2A4AD90580F798795B79709DF54125BAE2B2856524B9A94914C36` |
| `osu!mania-sample/Camellia - NIGHTMARE CITY (AlexDunk) [2022 Remake N.I.G.H.T.M.A.R.E.].osu` | 198122 | `72A18F628E8450743A225F05778D5C809097553D289101524131C293F2CBA329` |
| `osu!mania-sample/Camellia - NIGHTMARE CITY (AlexDunk) [FINAL JUDGMENT].osu` | 178475 | `3CB7A2C7AEDB63DDE770B098C9AA78374E17D2455D6D6AADBEAFB098E7A2FB12` |
| `osu!mania-sample/Camellia - NIGHTMARE CITY (AlexDunk) [SV JUDGMENT].osu` | 279530 | `E7F4E96722B91F80A3229D381161B4CC77DB4C2F01327789043FDEA7D84AC868` |
| `osu!mania-sample/Camellia - NIGHTMARE CITY (AlexDunk) [Tempestuous Purgatory].osu` | 123736 | `34782AD3227EC492F5E55D3B82EF51DA449F8FCB0FC297CF27C0065DC04A9CDE` |
| `osu!mania-sample/Camellia - NIGHTMARE CITY (AlexDunk).osb` | 2584 | `B7794CE37576052169CF543056A2CAB6F1213030F5DC2BC1534D1B1FB38F2ABC` |

Non-chart assets exist (`.ogg`, `.mp3`, `.png`, `.jpg`) and were inventoried by name/size only. They are not parsed.

## ADOFAI coverage — `main.adofai`

Evidence grade for this subsection: **CONFIRMED for this one file only**.

| Field | Value |
| --- | --- |
| Encoding | UTF-8 with BOM (`EF BB BF`) |
| Newlines | Mixed; mostly LF, 8 CRLF |
| JSON | Parses with `ConvertFrom-Json` after BOM strip |
| Trailing comma before `}` / `]` | 0 |
| Double comma | 0 |
| Top-level keys | `pathData`, `settings`, `actions` |
| `angleData` | absent |
| `decorations` | absent |
| `settings.version` | 2 |
| `settings.bpm` | 50 |
| `settings.offset` | 30 |
| `settings.pitch` | 100 |
| `settings.songFilename` | `ゆめのつづき.ogg` |
| `settings` key count | 55 |
| `pathData` length | 217 |
| `pathData` charset (count) | R=93 L=40 T=30 G=26 F=10 B=8 `!`=8 U=2 |
| `pathData` contains `0` | no |
| Numeric `999` | 0 |
| `actions` count | 265 |
| Event `floor` range | 0–206 |
| Floors with any event | 97 |
| Floors with >1 event | 51 |
| Floors with >1 `SetSpeed` | 0 |

### Event types present

| Count | eventType |
| ---: | --- |
| 85 | MoveCamera |
| 41 | SetSpeed |
| 39 | MoveTrack |
| 23 | Flash |
| 19 | MoveDecorations |
| 15 | AddDecoration |
| 9 | SetFilter |
| 8 | Twirl |
| 8 | RecolorTrack |
| 7 | CustomBackground |
| 5 | SetPlanetRotation |
| 3 | Bloom |
| 2 | SetHitsound |
| 1 | PositionTrack |

### Timing-relevant events in this file

| Mechanism | Present | Notes |
| --- | --- | --- |
| `SetSpeed` `speedType=Bpm` | yes (31) | Field pair observed: `beatsPerMinute` + `bpmMultiplier` |
| `SetSpeed` `speedType=Multiplier` | yes (10) | Same field pair; `beatsPerMinute` often 100 when type is Multiplier |
| Same-floor multiple `SetSpeed` | no | Cannot test order-on-same-floor here |
| `Twirl` | yes (8) | Objects are `{ floor, eventType: Twirl }` only |
| `SetPlanetRotation` | yes (5) | Visual/planet ease; timing effect UNKNOWN |
| `Hold` | no | |
| `Pause` | no | |
| `FreeRoam` | no | |
| `AutoPlayTiles` | no | |
| `MultiPlanet` | no | |
| `angleData` | no | Old `pathData` chart |
| midspin as `999` in `angleData` | no | |
| midspin-like `!` in `pathData` | yes (8) | Mapping `! → 999` is **not** confirmed from this file alone |
| U-turn / path char `0` | no | |

`duration` fields in this file belong to visual events (`Flash`, `MoveTrack`, `MoveCamera`, `MoveDecorations`), not `Hold`. Do not treat those numbers as Hold-duration evidence.

Some event fields use the strings `Enabled` / `Disabled` rather than JSON booleans (36 occurrences).

## osu!mania coverage

Evidence grade: **CONFIRMED for these four files**, cross-checked against the public osu! wiki page cited in `PROJECT_UNDERSTANDING.md`.

All four `.osu` files:

| Field | Value |
| --- | --- |
| Header | `osu file format v14` |
| `Mode` | 3 |
| `CircleSize` | 4 |
| Observed columns | 0–3 |
| Audio | `audio.mp3` |
| `PreviewTime` | 137337 |
| BeatmapSetID | 1034509 |
| HP / OD | 8 / 8 |
| Sections | General, Editor, Metadata, Difficulty, Events, TimingPoints, HitObjects |
| `[Colours]` | absent |

| Difficulty | BeatmapID | HitObjects | Circles | LN (type bit 7) | TimingPoints | Uninherited | Inherited | Max chord | Hit time range (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 2022 Remake N.I.G.H.T.M.A.R.E. | 3479767 | 6342 | 4811 | 1531 | 68 | 21 | 47 | 4 | 1896–417332 |
| FINAL JUDGMENT | 3200765 | 5996 | 5589 | 407 | 68 | 21 | 47 | 4 | 1765–417332 |
| SV JUDGMENT | 3200766 | 5982 | 5575 | 407 | 3390 | 57 | 3333 | 4 | 1765–417332 |
| Tempestuous Purgatory | 3200775 | 4152 | 3963 | 189 | 68 | 21 | 47 | 4 | 4635–417332 |

Uninherited BPM range on all four: 164–230 (17 distinct values in the three non-SV diffs; SV JUDGMENT has the same BPM set plus extra uninherited points).

SV JUDGMENT is the only file with large inherited-point (green-line) coverage. Derived SV from `-100 / beatLength` spans approximately 0.079–10 on that file. The other three difficulties have inherited points whose derived SV stays near 1–1.40.

LN syntax observed: `type=128`, extra field `endTime:hitSample`. Circle syntax observed: `type=1`, no endTime.

`.osb` is a short storyboard companion, not a hitobject chart.

## Coverage vs M0 unknowns

| M0 topic | Covered by current `.sample/`? |
| --- | --- |
| `pathData` (legacy) | yes, one v2 chart |
| `angleData` | **gap** |
| `pathData` ↔ `angleData` mapping | **gap** (no paired file) |
| `!` / midspin | glyph present; timing semantics **gap** |
| `0` / U-turn | **gap** |
| `Twirl` presence | yes; timing effect **gap** |
| `Hold.duration` | **gap** |
| `SetSpeed` Bpm + Multiplier | yes |
| Multiple `SetSpeed` on one floor | **gap** |
| `Pause` | **gap** |
| `FreeRoam` | **gap** |
| `AutoPlayTiles` | **gap** |
| Malformed JSON (trailing/double commas) | **gap** (this file is strict JSON after BOM) |
| `settings.version` span | **gap** (only 2) |
| osu!mania 4K + LN + chords | yes |
| osu!mania keycounts other than 4 | **gap** |
| osu!mania SV-heavy timing | yes (one difficulty) |
| osu file format other than v14 | **gap** |
| Paired ADOFAI ↔ mania conversion pair | **gap** |
| Long-chart cumulative timing / 1.44× claim | **gap** (not reproducible from this corpus) |

## Intentionally not in this index

- Full `pathData` string
- Full `actions` / hitobject bodies
- Audio or image hashes beyond existence
- Any upload or redistribution of originals
