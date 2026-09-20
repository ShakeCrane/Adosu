## User request

Resume the previously blocked work from the current checkpoint. Do not restart the whole project, redesign the goal, or discard the investigation conclusions already established. Continue for as long as there are meaningful, executable steps: investigate, implement, test, review, and converge. Do not stop early for a local UNKNOWN, a non-critical issue, incomplete third-party documentation, a first failed test, or an initial search with no result; investigate root causes, correct the model or implementation, and re-verify. Stop only when the baseline is substantively complete or when a genuine correctness blocker remains.

### 1. Locate the real Adosu! worktree first

The local Adosu! working directory does contain files. The earlier conclusion that the current task directory was not a Git repository and contained only `AGENTS.md` means only that the shell was in the wrong directory or the wrong workspace was mounted; it does not mean the Adosu! project, `TEAM.md`, `PROJECT_UNDERSTANDING.md`, or fixtures are absent.

Search the accessible workspaces, mounted directories, and known project directories within a reasonable scope. Find a candidate containing all of:

```text
.git/
TEAM.md
PROJECT_UNDERSTANDING.md
```

Also inspect `.sample/`, source, tests, and project/build files. For each candidate, run and use:

```bash
git status --short
git branch --show-current
git log -1 --oneline
git remote -v
```

Confirm from the remote, `TEAM.md`, `PROJECT_UNDERSTANDING.md`, and the project source that the selected tree is really Adosu!. Once found, read the project instructions, `TEAM.md`, and `PROJECT_UNDERSTANDING.md`, then inspect `.sample/`, source, tests, build configuration, `.gitignore`, and existing working-tree changes. Do not overwrite user changes; distinguish changes that predate this run from changes made during it.

### 2. Fixtures

Find these two real, unrelated fixtures, preferably under `.sample/` or in a read-only external-materials directory:

```text
main.adofai
Camellia - NIGHTMARE CITY (AlexDunk) [SV JUDGMENT].osu
```

They are different songs, are not conversion outputs of one another, and are not a note-by-note ground-truth pair. Use them to test the two source semantics separately. Treat `.sample/` as read-only: do not modify or delete the fixtures, commit them without a reason, or upload them to any external service.

If the real fixtures are temporarily unavailable, continue all work that does not depend on them: source and parser review, semantic-model review, public-format research, SharpFAI/ADOFAI-JS cross-checks, osu! official-format confirmation, test-architecture preparation, and minimal synthetic fixtures. Do not end the entire task merely because a fixture is missing. Report a fixture blocker only if its absence is the sole remaining obstacle to correctness validation.

### 3. Working and delegation

Follow `TEAM.md`. Parallel work is allowed when it avoids duplicated investigation, for example:

- ADOFAI timing semantics and SharpFAI/ADOFAI-JS
- osu!mania timing, SV, LN, chord, and file format
- repository architecture, parser, and tests
- independent correctness review

Unify facts, resolve conflicts, and decide the result rather than blindly concatenating reports. The core work must be cross-checked in multiple rounds. For each central rule, seek official material, reliable upstream/reference source code, and actual fixture or automated-test evidence, adding an independent implementation where practical.

Do not start M0-B or implement a complete converter merely because this baseline looks usable. Spend remaining effort on tests, edge cases, external cross-validation, fixture invariants, precision and ordering audits, independent review, documentation convergence, and cleanup of temporary assets. Do not automatically commit or push.

### 4. osu!mania research and constraints

Use the official osu! wiki and osu!lazer/osu! source where applicable. Confirm:

- `.osu` file format and `Mode = 3`
- `CircleSize` as the key count
- lane derivation from HitObject `x`
- tap objects, LN objects, HitObject `time`, and LN `endTime`
- red/uninherited and green/inherited timing points
- SV semantics
- same-timestamp ordering
- decimal precision

Keep the hard distinction:

```text
mania SV != ADOFAI SetSpeed
```

An inherited timing point/green line must not alter HitObject absolute timestamps. At minimum, keep separate internal tracks:

```text
TempoTrack
Scroll / Presentation Track
```

Do not collapse them into one generic `SpeedChange`.

Preserve osu!mania chords exactly as multiple lanes at one timestamp. Do not turn a chord into `t`, `t + ε`, `t + 2ε` in the reader or IR, and do not decide a chord-degradation policy prematurely. Preserve tap/LN semantics, including LN start and end, through parsing and the semantic model.

For ordering, do not use a lossy `Dictionary<timestamp, event>` for same-timestamp timing points or HitObjects. Preserve `sourceIndex` or equivalent stable order. Keep source numeric values, decimal timing, original event order, and source index without premature `Math.Round`, casts, or integer milliseconds. Distinguish source precision, internal precision, and target serialization precision.

### 5. ADOFAI research and constraints

Investigate:

- `pathData`
- `angleData`
- raw direction and computed rotation angle
- `999`/midspin
- Twirl
- SetSpeed with `Bpm`, `Multiplier`, and `angleOffset`
- same-floor and same-angleOffset ordering
- Pause
- Hold
- FreeRoam
- MultiPlanet
- Multitap

Cross-check especially `adofaiex/SharpFAI` and `adofaiex/ADOFAI-JS`, including `FloorTimingCalculator` and `GetNoteTimesNew`. Treat third-party implementations as evidence, not automatically as game truth. Classify conclusions as `VERIFIED`, `INFERRED / externally corroborated`, or `UNKNOWN`.

Do not use this as a general timing model:

```text
time += angleData[i] / 180 * beatDuration
```

First distinguish raw direction, floor-entry/exit geometry, rotation direction/state, actual travelled angle, speed state, and entry time. Explicitly prove that repeated `R` or the same direction cannot be misread as zero time or cause a zero-time note pileup.

Treat Twirl as gameplay/timing state. It must not be removed during a `Remove VFX` stage before the timing resolver can observe it. Add targeted tests.

For SetSpeed, cover at least `Bpm` and `Multiplier`, and investigate `angleOffset = 0`, non-zero `angleOffset`, multiple SetSpeed events on one floor, and multiple events sharing an offset. If a non-zero angle offset means an intra-tile speed change, do not force it into `floor.Bpm` and claim losslessness; retain a piecewise timing state when required.

Investigate modern ADOFAI Multitap semantics rather than presuming it is an osu!mania chord or presuming ADOFAI can never express simultaneity. If evidence is insufficient, record `UNKNOWN` and formulate the smallest follow-up validation question.

### 6. Minimal semantic model

Do not build a future-driven framework. Establish only the smallest correct model needed for this baseline, with at least these semantic distinctions:

```text
GameplayChart
InputTrack
TempoTrack
Scroll / Presentation Track
GameplayState
```

An `InputEvent` must not inherently require `Lane: int`: osu!mania has lanes, while an ordinary ADOFAI input has no equivalent lane. Use an optional/source-specific lane or an equivalently correct representation. Preserve source provenance on each important semantic object at the smallest useful level, such as source format, source index, source floor/object index, and original timestamp, so future round-trip diffs can answer where a target note came from. Avoid a complex provenance framework.

### 7. Real-fixture regression statistics

When both real fixtures are accessible, parse them and recompute all statistics; do not trust old numbers in the task description.

For ADOFAI, recompute at least path/angle counts, midspin count, Twirl count, SetSpeed count, BPM, offset, and actions.

For osu!mania, recompute at least Mode, keys, HitObject count, tap count, LN count, red timing-point count, green timing-point count, and chord distribution.

Report the actual measured values.

### 8. Synthetic micro-fixtures

Even with the large real fixtures, create minimal tests that isolate:

- ADOFAI repeated direction
- Twirl
- SetSpeed `Bpm`
- SetSpeed `Multiplier`
- midspin
- mania 4K lane boundaries
- tap
- LN
- a two-note chord
- SV
- red plus green timing points

If evidence supports it, add `angleOffset`, Pause, Hold, MultiPlanet, and Multitap. Keep each sample small enough to localize a failure quickly.

Prefer invariant tests over giant snapshots. At minimum verify that:

- SV changes scroll semantics but not input timestamps.
- Twirl affects gameplay state and cannot disappear before timing resolution.
- Repeated ADOFAI direction does not create a zero-time note pileup.
- Chord simultaneity survives parse to semantic model.
- LN start/end survives parse to semantic model.
- Removing VFX does not alter the resolved gameplay timeline.

If the first implementation fails, find the root cause and decide whether the model assumption is wrong. Make the smallest correct fix and add a regression test. If the model is wrong, change the model instead of piling on special cases or fixture-specific hardcoding such as `if (sampleName == ...)`.

### 9. Verification and review

After changes, run every applicable existing project workflow before inventing a new one:

```text
build
unit tests
parser tests
synthetic-fixture tests
real-fixture tests
timing invariants
ordering tests
precision tests
diff review
```

Perform an independent correctness review after the core tests pass. Look specifically for wrong timing assumptions, silent information loss, precision loss, ordering loss, source-specific semantics incorrectly made common, a mania-centric or ADOFAI-centric IR, and happy-path-only coverage. Resolve every concrete correctness bug found, then rerun the relevant tests and final verification.

An UNKNOWN is not itself a blocker. A true blocker must be a missing fact that leaves two or more implementation choices indistinguishable, where choosing incorrectly would cause an actual gameplay-correctness bug, and where the repository, fixtures, public sources, and a minimal experiment cannot resolve it. If blocked, state exactly what fact is missing, why those routes cannot resolve it, what bug a wrong choice would cause, and the smallest validation the user must provide.

### 10. Documentation and temporary assets

Update `PROJECT_UNDERSTANDING.md` when this run produces durable project knowledge. Correct outdated statements in place instead of appending conflicting history. Record the current truth about:

- the ADOFAI timing pipeline
- Twirl semantics
- SetSpeed semantics
- mania Tempo versus SV
- IR boundaries
- precision rules
- ordering rules
- verified fixture facts
- known unsupported cases
- information-loss boundaries
- UNKNOWN items
- the next step

Keep downloaded research, PoCs, probes, and analysis output in temporary space. Move only durable conclusions into `PROJECT_UNDERSTANDING.md` and remove assets without long-term value. Keep the formal repository clean.

### 11. Completion criteria and final report

Conclude only when either the baseline is substantively complete or a genuine, unavoidable blocker has been demonstrated. Completion means a credible ADOFAI reader, a credible osu!mania reader, an ADOFAI gameplay-timing baseline, the minimal Input/Tempo/Scroll-Presentation/GameplayState separation, synthetic and real-fixture regression tests, precision and ordering invariants, a `VERIFIED`/`INFERRED`/`UNKNOWN` list, green build/tests, a reviewed diff, and synchronized `PROJECT_UNDERSTANDING.md`.

The final report should cover:

```text
目标
仓库起始状态
关键发现
VERIFIED
INFERRED
UNKNOWN
实际实现
ADOFAI timing model
osu!mania timing / SV model
semantic model
真实 fixture 实测统计
新增 synthetic fixtures
测试结果
独立 review 发现及修复
工作树最终状态
PROJECT_UNDERSTANDING 更新
仍存在的风险
建议的 M0-B 入口条件
```

Do not repeat this entire request in the final report. During context compaction, retain the complete latest plan, current repository state, verified facts, semantic model, implementation changes, real-fixture statistics, synthetic fixtures, test results, review fixes, UNKNOWN items, remaining risks, and next steps; discard obsolete plans, search logs, repetitive logs, and completed investigation branches.
