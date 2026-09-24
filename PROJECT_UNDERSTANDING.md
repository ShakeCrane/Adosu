# PROJECT_UNDERSTANDING.md

## Current M0-A implementation checkpoint (2026-09-21)

The repository now has a minimal, dependency-free .NET 10 semantic baseline under `src/Adosu.Core` and an executable regression suite under `tests/Adosu.Tests`. It reads ADOFAI `pathData`/`angleData` plus ordered actions and reads osu!mania timing points and hit objects, preserving source tokens, source indices, decimal timestamps, taps, holds, chords, red tempo points, and green SV points in separate tracks. Thirty-eight regression cases are registered (34 in the preceding local run plus four follow-up cases); the two real-fixture cases use the local ignored `.sample/` corpus. The local Release build and 38/38 test run pass for this revision; no CI result is claimed.

The baseline retains its earlier semantic fixes. Four follow-up cases have been added and pass in the local .NET regression run:

- `GameplayState.Changes` is built in a single source-ordered pass, so mixed action types on the same floor (Twirl, MultiPlanet, Pause, Hold, FreeRoam, Multitap, unknown) never get reordered relative to each other. Every derived event track over real time (`GameplayState.Changes`, `PresentationEvents`, and ADOFAI `TempoTrack.Events`) is ordered by real time then `Provenance.SourceIndex`; no lossy timestamp dictionary is used. This holds across floors too, including a midspin floor crossed by `pathData: "R!"`.
- **P1 (same-time state order):** presentation order and semantic application order are now separate tracks. `GameplayStateChange.ApplicationOrder` and `TempoEvent.ApplicationOrder` are reserved in floor traversal order, then same-floor source order, including rejected and deferred Pause events, and `GameplayState.TwirlAt(t)` / `ChangesInApplicationOrder` replay state in that ordinal, never in the `(TimeSeconds, SourceIndex)` display order. `pathData: "R!!R"` with a `Twirl` on floor 1 (source 1) and floor 2 (source 0) now ends `TwirlAt(1) == false` with `TwirlStateAfter` snapshots consistent with floor traversal, while the display list stays deterministic.
- **P1 (SetSpeed illegal input):** `SetSpeed` candidates are validated before touching canonical state. Non-finite, zero, negative and missing `beatsPerMinute`/`bpmMultiplier`, and multiplier products that overflow or underflow the usable BPM range are rejected with the stable `ADF-SPEED-VALUE` error, keep their raw record/provenance, expose canonical-empty `Bpm`/`Multiplier` (`TempoEvent.Application = Rejected`) and never perturb BPM, segments or later event times. The base BPM guard `ADF009` now also rejects a base BPM whose full-floor duration overflows. Non-finite `angleOffset` is reported as an explicit `ADF-SPEED-OFFSET` error rather than a silent out-of-span clamp. The unbacked `Math.Max(bpm, 1e-12)` epsilon clamp is gone; legality is decided by the computed finite/positive duration invariant.
- **P2 (Pause/Hold/FreeRoam duration invariant):** a present `duration` must be finite and non-negative (`Pause` allows `0`); missing, non-numeric, `1e309`/Infinity and negative values are rejected, never defaulted, clamped or abs-ed. A rejected event keeps its raw JSON and `ApplicationOrder` provenance, carries `GameplayStateChangeApplication.Rejected`, has a canonical-empty `DurationBeats`, and emits the stable `ADF-DURATION-VALUE` error; a rejected `Hold` also leaves `InputEvent.DurationBeats` empty. A legal `Pause` keeps `ADF-PAUSE-INFERRED` and the `INFERRED` (not `VERIFIED`) semantic level. A final canonical time invariant rejects any non-finite floor start or backwards time axis. Underflowed-but-finite segment times remain equal (non-decreasing); no minimum spacing or fabricated gap is introduced.
- **P2 follow-up (Pause is validated against the canonical floor-end tempo):** `Pause` no longer validates its seconds against the entry BPM that is current when the action is read. Its raw `duration` is checked in the source-ordered pass, then the candidate is committed after `ResolveFloorTempo` with the same `beats * 60 / floor-end BPM` expression that advances the floor. Each candidate is checked as a transaction over the Pauses already accepted on that floor: the accumulated raw beats must stay finite and non-negative, the seconds derived from them must stay finite and non-negative, and the resulting floor end must stay finite. This closes two overflows that the single-Pause entry-BPM check missed: two individually representable Pauses whose accumulated beats overflow the beat-to-second step (for example two `2e306` Pauses at base BPM `1e307`), and a legal `SetSpeed` that lowers the floor-end tempo far enough that a huge-but-individually-finite Pause becomes unrepresentable in seconds. Only the offending Pause is rejected, with its own raw record, provenance, original `TimeSeconds` and application ordinal; the accepted Pauses, `Twirl`, `MultiPlanet`, `SetSpeed` and the application order are preserved. The returned accepted seconds are reused verbatim for the floor advance, so no second expression can reintroduce the overflow.
- ADOFAI actions are split into an explicit presentation VFX whitelist (`MoveCamera`, `MoveTrack`, `MoveDecorations`, `AddDecoration`, `SetFilter`, `RecolorTrack`, `CustomBackground`, `SetPlanetRotation`, `Bloom`, `SetHitsound`, `PositionTrack`, `Flash`) versus modeled gameplay/timing state. Any action that is neither is retained as `GameplayStateChangeKind.Unknown` with its raw provenance and emits a stable `ADF-ACTION-UNCLASSIFIED` warning; it is never silently treated as removable VFX.
- An action whose `floor` is null, negative, or beyond the resolved floor range, and any non-object entry in `actions[]`, is never silently dropped: it stays a source record and becomes an explicit unknown gameplay state with a stable `ADF-FLOOR-RANGE` warning. A missing floor keeps a `null` provenance floor index and is never disguised as `-1`.

- **Follow-up (accumulated travel):** individually finite floor spans may overflow when accumulated. The resolver fails closed with a floor-identified `ADF-DURATION-VALUE` `FormatException`, including an overflowing final floor; it neither clamps nor returns an Infinity-filled chart. Exceptional input cannot preserve a partially valid canonical chart.
- **Follow-up (Pause provenance):** `ADF-PAUSE-INFERRED` identifies the first accepted Pause contributing positive duration. `ApplicationOrder` retains source ordering; nonzero-angle-offset SetSpeed remains governed by its resolved timing boundary.

This is still M0-A: there is no writer or complete converter. ADOFAI Hold/Pause/FreeRoam/MultiPlanet/Multitap edge semantics, full pathData version coverage, and exact `!`/999 game timing remain explicitly `UNKNOWN` or externally inferred. Do not treat the current reader as a claim that those mechanisms are losslessly convertible.

> adosu! 项目共享理解
> 更新时间：2026-09-21
> 状态：M0-A correctness hardening follow-up 已交付；M0-B 尚未正式启动。
> 本文件反映当前可验证事实；规划中的模块与转换语义仍多为 TARGET / UNKNOWN。

---

## 1. 项目定位

adosu! 的目标是在 A Dance of Fire and Ice（ADOFAI）谱面与 osu!mania 谱面之间进行尽可能准确、稳定、可解释的逐 Note 双向转换：

```text
ADOFAI ↔ osu!mania
```

重点不是文件格式层面的“能读能写”，而是在两个差异明显的谱面模型之间尽量保存实际谱面语义，包括：

- Note 对应关系
- timing / BPM / offset
- lane / key
- hold / long note
- 同时击打与事件顺序
- rounding / precision
- 特殊机制
- 信息损失
- round-trip 行为

长期原则：

```text
correctness
> 可解释性
> 自动化验证
> 成功生成文件
```

不能因为输出文件可被游戏打开，就认为转换正确。

---

## 2. 当前仓库基线（2026-09-21 核实）

本节为 **CONFIRMED** 仓库事实。

### Git

```text
branch: main
remote: https://github.com/ShakeCrane/Adosu
reviewed parent: 0f0c9c0 (2026-09-21 follow-up)
```

HEAD and local working-tree status are dynamic: check them at task start. `.sample/` remains ignored read-only user material. The tracked tree now includes the solution, Core, tests and research docs; no writer, full converter or CI has been committed.

### 已存在的实现资产

当前 `main` 已跟踪一个最小、无依赖的 .NET 10 语义基线与可执行回归套件：

```text
Adosu.sln
src/Adosu.Core/Adosu.Core.csproj       # 依赖为零
src/Adosu.Core/Model/Primitives.cs
src/Adosu.Core/Parsing/AdofaiReader.cs
src/Adosu.Core/Parsing/OsuManiaReader.cs
src/Adosu.Core/Timing/AdofaiTimingResolver.cs
src/Adosu.Core/Timing/ManiaTimingResolver.cs
tests/Adosu.Tests/Adosu.Tests.csproj
tests/Adosu.Tests/Program.cs
```

因此：

- 存在 ADOFAI / osu!mania Reader 与语义/时间解析的最小实现
- 注册 38 个可执行回归测试（其中 2 个依赖本地 `.sample/`）
- 不存在 Writer / 完整 Converter / CLI / UI / CI
- 规划中的完整 `Adosu.Core` 模块树（`IO` / `Convert` / `Packaging` 等）仍是 TARGET

### 文档

- `TEAM.md`：已跟踪，定义多 Agent 协作原则。
- `PROJECT_UNDERSTANDING.md`：本文件，记录当前实现与剩余 UNKNOWN。
- `docs/research/sample-corpus-index.md`：派生索引，不是 `.sample/` 原文件。
- `docs/research/m0-a-baseline.md`：M0-A 语义/时间基线记录，含 VERIFIED / INFERRED / UNKNOWN。

### 对旧表述的直接修正

已删除或降级的过时说法：

- “本文件尚未与实际仓库核对” → 已核对。
- “基于当前 PLAN.md 整理” → `PLAN.md` 不在仓库中，相关内容为 **PLAN-DERIVED / 无法复核**。
- “不存在 src/ tests/ test/、Adosu.sln、Adosu.Core、Adosu.Tests” → 已过时；这些现在存在。
- “不存在 Reader / Writer / IR / Timing 实现、不存在测试” → 已过时；Reader / 语义时间解析 / 测试已存在。
- “settings.version 在已有预扫描语料中跨度较大” → 当前 `.sample/` 只有 `version: 2`，跨度 **未证实**。
- “应用 SetSpeed 后预测时长仍存在约 1.44× 系统性偏长” → 当前语料无法复现，降为 **无法复核的继承声明**，不是 CONFIRMED。
- “不要在该问题确认前实现 TimeResolver” → 已被 M0-A 基线实现取代；当前策略是“实现 + 明确 UNKNOWN + 显式 diagnostic”，而不是阻塞实现。

---

## 3. 当前阶段

### 当前主阶段

当前应优先推进：

```text
M0：格式 / 时间语义 / ground truth 调研
```

M0 完成到足以支撑主链路后，再进入：

```text
M1：Core / Reader / Writer / IR / Timing
M2：ADOFAI → osu!mania
```

首版方向明确为：

```text
ADOFAI → osu!mania
```

反向 `osu!mania → ADOFAI` 已纳入整体架构，但不应在当前阶段抢跑。

当前双侧 Reader 与语义/时间解析最小基线注册 38 个回归案例（2 个依赖本地 fixture）；Writer / 转换器 / UI 仍未开始。

---

## 4. 当前技术路线

### 4.1 IR-first（TARGET，非已实现代码）

两个方向共用统一 IR 和时间轴能力：

```text
ADOFAI
   ↓
AdofaiReader
   ↓
                 ┌───────────────┐
                 │   IR / Chart  │
                 └───────────────┘
                         ↓
                    TimingGrid
                         ↓
          ┌──────────────┴──────────────┐
          ↓                             ↓
   ColumnAssigner                ChordSerializer
          ↓                             ↓
     OsuWriter                    AdofaiWriter
          ↓                             ↓
      osu!mania                       ADOFAI
```

核心转换逻辑不能依赖 UI。

### 4.2 预计解决方案边界（TARGET，部分已实现）

规划中的主要模块：

```text
Adosu.Core
├── Model        # 已实现最小版：Primitives.cs
├── Timing       # 已实现最小版：AdofaiTimingResolver / ManiaTimingResolver
├── Parsing      # 已实现最小版：AdofaiReader / OsuManiaReader
├── IO
│   ├── Osu      # TARGET
│   └── Adofai   # TARGET
├── Convert
│   ├── AdofaiToMania   # TARGET
│   └── ManiaToAdofai   # TARGET
├── Diagnostics  # 已具基础结构：Diagnostic / SourceProvenance
└── Packaging    # TARGET

Adosu.Cli     # TARGET
Adosu.App     # TARGET
Adosu.Tests   # 已实现最小可执行套件
web/          # TARGET
```

当前允许在 M0 期间并行建立：

- solution / project skeleton
- IR 类型
- test infrastructure
- sample indexing
- diagnostics 基础结构
- 与待验证 timing semantics 无关的基础设施

不得提前把尚未确认的 ADOFAI 时间语义固化成最终转换实现。

---

## 5. 已知格式事实与当前证据状态

长期标记：

```text
CONFIRMED
SUPPORTED_HYPOTHESIS
HYPOTHESIS
UNKNOWN
CONFLICTED
DISPROVEN
PLAN-DERIVED     # 来自已缺失的 PLAN.md / 旧规划，当前无法复核
TARGET           # 设计方向，不是格式事实
```

### 5.1 ADOFAI

`CONFIRMED`（当前唯一 `.adofai` 样本 `main.adofai`）：

- 文件是 UTF-8 文本，带 BOM（`EF BB BF`）。
- 去 BOM 后可被严格 JSON 解析（`ConvertFrom-Json` 成功）。
- 顶层键为 `pathData` / `settings` / `actions`。
- 无 `angleData`，无 `decorations`。
- `settings.version` = 2。
- `settings.bpm` = 50，`settings.offset` = 30。
- `pathData` 为长度 217 的方向字符串；含 `R L T G F B U !`。
- `actions` 265 条；`eventType` 见 corpus index。
- `SetSpeed` 同时出现 `speedType: Bpm`（31）与 `speedType: Multiplier`（10）。
- 该文件中 `SetSpeed` 字段为 `beatsPerMinute` 与 `bpmMultiplier`，不是单独的 `bpm` / `multiplier`。
- 该文件无同一 floor 上的多个 `SetSpeed`。
- 该文件无 `Hold` / `Pause` / `FreeRoam` / `AutoPlayTiles` / `MultiPlanet`。
- 该文件无尾逗号、双逗号。
- 部分事件字段使用字符串 `"Enabled"` / `"Disabled"`，不是 JSON boolean。
- `Twirl` 在该文件中只有 `floor` + `eventType`。
- `duration` 出现在视觉事件上，不能当作 Hold 单位证据。

`CONFIRMED`（公开社区文档，**非正式官方源码**；只确认“存在这类编码说法”，不确认映射表正确）：

- 社区资料将 `.adofai` 描述为 JSON 类关卡文件，并声称 `pathData` 与 `angleData` 可互转。
  来源：<https://learn.modrift.org/libs/level-format>（获取日期 2026-09-17）。

`SUPPORTED_HYPOTHESIS`：

- 更广语料中 Reader 需要容忍非严格 JSON（BOM、尾逗号、双逗号、字符串内特殊内容）。当前样本不足以证明这一点，只证明至少存在“带 BOM 的严格 JSON”。
- `pathData` 字符 `!` 对应 midspin / angle `999`。社区表如此记载；本样本有 8 个 `!`，但无配对 `angleData`，不能升级为 CONFIRMED。
- `SetSpeed` 的 Bpm / Multiplier 两类行为会改变后续 tile 的时间。文件证实字段存在，不证实生效边界。

`PLAN-DERIVED` / `UNKNOWN`：

- `settings.version` 跨度大、需要广泛版本兼容。
- 真实文件普遍含尾逗号 / 双逗号。
- 应用 SetSpeed 后预测时长约 1.44× 系统性偏长。

`TARGET`：

Reader 应 tolerant：

- 未知字段尽量保留
- 未知事件记录而不是静默删除
- malformed JSON 的修复必须字符串感知
- 不能在字符串内部做粗暴替换

### 5.2 osu!mania

`CONFIRMED`（官方 wiki 2026-09-17：<https://osu.ppy.sh/wiki/en/Client/File_formats/osu_(file_format)>，并与当前 4 个 `.osu` 一致的部分）：

- `.osu` 为分段文本；当前样本均为 `osu file format v14`。
- `Mode: 3` 表示 osu!mania。
- Timing point 语法：`time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects`。
- `uninherited=1`：`beatLength` 为每拍毫秒（红线 / BPM）。
- `uninherited=0`：`beatLength` 为负的 inverse slider velocity 百分比（绿线 / SV）。
- Hit object 语法：`x,y,time,type,hitSound,objectParams,hitSample`。
- `type` bit 0 = hit circle，bit 7 = mania hold。
- mania hold：`endTime:hitSample`；列索引 `floor(x * columnCount / 512)`。
- mania 不使用 slider / spinner。

`CONFIRMED`（当前 4 个 `.osu`）：

- 全部 `Mode=3`，`CircleSize=4`，命中列 0–3。
- 全部含普通 note 与 LN。
- 全部含同时击打（max chord = 4）。
- 三个难度 timing 点较少（68）；`SV JUDGMENT` 有 3390 条 timing 点（57 红 / 3333 绿）。
- BPM 范围约 164–230。
- 无 `[Colours]` 段。

`SUPPORTED_HYPOTHESIS`：

- mania 中 `CircleSize` 等于键数 / `columnCount`。官方文件格式页把 Difficulty.`CircleSize` 写成通用 “CS setting”，列计算使用 `columnCount`；当前样本 CS=4 且只有 4 列，支持该对应，但尚未用 1K/5K/7K/8K 样本或官方实现交叉证明。

`PLAN-DERIVED`：仍应用官方文档 + 多样本复核的旧提醒，现已部分完成；缺非 4K、非 v14、无 LN、非 mania 对照样本。

---

## 6. `.sample/` 研究基线

默认只读。本轮未修改、未删除、未格式化、未上传任何原始样本。

实际布局（遵循现状，不重构为 `provided/` / `_external/`）：

```text
.sample/
├── ADOFAI-sample/       # USER PROVIDED
└── osu!mania-sample/    # USER PROVIDED
```

派生索引：`docs/research/sample-corpus-index.md`。

规模：28 个文件，约 20.4 MB。图表文件 1 个 `.adofai` + 4 个 `.osu` + 1 个 `.osb`。

这不是转换配对语料：ADOFAI 与 mania 样本互不对应。

详细哈希、事件直方图、osu 难度统计见 corpus index。

---

## 7. 当前最重要的未知与风险

以下问题会直接影响 ADOFAI 时间轴和转换正确性，不能靠模型直觉决定。

### 高优先级 UNKNOWN

1. `pathData → angleData` 的权威映射（含全部字母、偏移字符、`!`）。
2. `999` / `!` midspin 的准确语义及是否推进时间。
3. `0` / U-turn 的准确 timing 语义。
4. `Twirl` 是否影响 timing，以及如何影响几何解释。
5. `Hold.duration` 的真实单位和版本差异。
6. `SetSpeed` 的准确生效边界（对本 tile 还是下一 tile；与 angleOffset 关系）。
7. 同一 floor 多个 `SetSpeed` 的顺序语义。
8. `Pause` 对时间线的影响。
9. `FreeRoam` 对普通 tile timeline 的影响。
10. `AutoPlayTiles` 等特殊段的时间 / Note 语义。
11. 长谱累计精度与可接受误差。
12. 继承的 “SetSpeed 后 1.44× 偏长” 声明的根因——**当前仓库无法复核该现象是否真实存在**。

当前 `.sample/` 对上述覆盖极窄：只能直接观察 (1) 的 `pathData` 字形、(4) 的 Twirl 存在、(6) 的 SetSpeed 字段形状。其余为 **evidence gap**。

其中任何未确认项，都不得因为“社区实现普遍这么写”而自动升级为事实。

### 下一步最优先的 M0 UNKNOWN

```text
pathData 字符 → 绝对角度 / midspin（!）映射
```

理由：

- 它是时间轴的几何输入；没有它无法对当前唯一 ADOFAI 样本建立 tile 拍数。
- 当前样本是 `version 2` + 纯 `pathData` + 8 个 `!`，正好卡在旧格式路径上。
- 社区对照表存在，但不是官方源码，证据等级最高只能到 SUPPORTED_HYPOTHESIS。
- 需要：官方或反编译许可之外的一手资料、多个 `pathData`/`angleData` 对照样本、以及 `!` 是否消耗时间的独立证据。

当前基线已经实现该映射的候选版本，并把它标记为 INFERRED / SUPPORTED_HYPOTHESIS，配合显式 diagnostic 与 targeted regression；在拿到一手证据前，不得升级为 CONFIRMED，也不得据此固化最终转换语义。

---

## 8. 证据与研究模型

### 8.1 不要求实机运行

当前项目不要求 Agent 自动启动 ADOFAI 或 osu! 客户端做实机测试。

技术事实主要通过：

```text
用户提供的双侧真实文件
+
官方 / 一手资料
+
公开源码与 reference implementation
+
Agent 主动获取的新样本
+
自动解析 / 统计 / 交叉比较
+
regression tests
```

建立证据链。

如果某个 runtime behavior 最终无法通过这些手段确定，允许保持 UNKNOWN / SUPPORTED_HYPOTHESIS，而不是伪造 certainty。

### 8.2 `.sample/`

`.sample/` 默认是只读研究语料。

允许：

- 读取
- 解析
- 索引
- 比较
- 统计
- 提取 minimal reproducer
- 用于研究和测试设计

默认禁止：

- 原地修改
- 删除
- 自动格式化
- 直接覆盖
- 无理由纳入正式源码

用户提供的素材和 Agent 联网获取的素材必须能区分来源。

推荐（若将来新增 Agent 下载内容时再采用；**不**为满足此建议重构现有目录）：

```text
.sample/
├── provided/      # 用户提供
└── _external/     # Agent 获取
```

### 8.3 Agent 可主动联网

普通技术研究无需用户逐次批准。

Agent 可以主动：

- web search
- web fetch
- GitHub / 官方文档调查
- clone 公开仓库
- 下载公开样本
- 获取 release / tag / commit
- 比较多个 parser / converter

联网研究必须保留 provenance。

至少记录：

```text
source URL
retrieval date
version / tag / commit（如适用）
purpose
local path（如下载）
```

下载不等于执行。

未知 `.exe` / `.dll` / `.bat` / `.ps1` 等默认不得自动执行。

**禁止**把 `.sample/`、游戏文件、用户参考文件、反编译结果上传到外部服务。本轮未上传。

---

## 9. Evidence Status

长期事实统一尽量使用：

```text
UNKNOWN
HYPOTHESIS
SUPPORTED_HYPOTHESIS
CONFIRMED
CONFLICTED
DISPROVEN
```

禁止把推断描述成 CONFIRMED。

---

## 10. Source of Truth

事实冲突时，优先级首先遵守项目长期指令：

```text
实际源码 / diff / 测试 / 可复现分析结果
> 实际样本
> PROJECT_UNDERSTANDING.md
> 旧报告或聊天
```

针对格式研究内部，再优先参考：

```text
官方源码 / 明确官方实现
> 官方格式规范 / 技术文档
> 用户提供的真实样本
> 多个真实样本的一致统计
> 高质量 reference implementation
> 独立社区实现
> issue / discussion
> Agent inference
```

不能机械套用优先级。

如果旧官方文档与新版本样本冲突，应调查版本差异，而不是强迫样本符合旧文档。

缺失的 `PLAN.md` 不是 source of truth。

---

## 11. ADOFAI → osu!mania 当前设计方向

### 11.1 Timing

目标是先得到统一、精确的绝对时间线。

候选基础模型仍为 **HYPOTHESIS / TARGET**：

```text
segment beats = angle / 180
segment duration = beats × 60000 / BPM
```

涉及 `0`、`999`/`!`、`SetSpeed`、`Twirl`、`Pause`、Hold 时必须以 M0 证据为准。当前样本不能验证该公式。

### 11.2 TimingPoints

当前 TARGET：

正常情况下优先使用 mania BPM 红线保持音乐 timing。

对于极端 BPM：

- 不静默 clamp
- 产生 diagnostics
- 允许后续由用户选择策略

具体阈值当前不是最终事实。

### 11.3 ColumnAssigner

ADOFAI 没有 mania lane 信息，因此 lane 分配不是无损转换。

应设计成可插拔策略。

当前规划候选：

- TurnDirection
- RandomConstrained
- Stair
- SingleLane

这些属于创作 / 产品策略，而不是格式事实。

必须明确区分：

```text
“转换正确性”
和
“生成谱面的手感”
```

### 11.4 Hold / LN

转换必须保持：

- 起点时间
- 终点时间
- lane 一致性

但 ADOFAI Hold 的具体时间换算规则尚需 M0 确认。当前 `.sample/` 无 Hold 事件。

---

## 12. osu!mania → ADOFAI 当前状态

当前只做架构预留。

主要已知困难：

- 同时和弦必须映射到 ADOFAI 的串行 / 特殊结构
- lane 信息需要转为转角或其他几何表达
- SV / BPM 语义需要重新映射
- 某些信息天然不可逆

Chord serialization 是后续 M4 的主要 correctness 问题。

当前不得为了“完成双向”提前堆特殊 case。

---

## 13. Diagnostics 与信息损失

无法直接对应的机制不得静默处理。

转换结果应能区分：

```text
无损保持
规范化
有控制的近似
明确不可逆
unsupported
correctness bug
```

建议长期使用稳定 issue code，例如：

```text
ADFxxx
OSUxxx
CVTxxx
```

但具体编码方案尚未定稿。

---

## 14. Round-trip 原则

两个方向都需要有意识考虑：

```text
ADOFAI
→ IR
→ osu!mania
→ IR
→ ADOFAI
```

以及：

```text
osu!mania
→ IR
→ ADOFAI
→ IR
→ osu!mania
```

不要求字节级恢复。

测试应该检查语义 invariant，例如：

- Note 数量
- 时间位置
- BPM 曲线
- lane / key
- LN 起止
- simultaneous notes
- event ordering
- precision
- diagnostics

需要明确记录哪些信息：

- 可无损恢复
- 会规范化
- 必然丢失
- 当前尚未支持

---

## 15. 当前验证策略

优先：

```text
parser test
serializer test
fixture conversion
targeted regression
round-trip test
invariant check
batch sample analysis
diff review
build / unit test
```

当前注册 **38 个可执行回归测试**（`tests/Adosu.Tests`，其中 2 个依赖本地 `.sample/`）。在 M0 证据不足处，实现以“保留 raw/source provenance + 稳定 diagnostic + 明确 UNKNOWN”处理，而不是静默丢弃或用特殊分支掩盖。

对于通过 `.sample` 暴露的问题：

```text
真实样本
→ isolate
→ minimal reproducer
→ regression fixture
→ test
```

不要把巨大真实谱面直接复制成长期测试资产，除非确有必要。当前 mania 样本单难度 4k–6k hitobjects，尤其不适合整图入库。

---

## 16. 当前里程碑

### M0 — Research / Ground Truth

- 建立 `.sample` corpus index ← 本轮已完成派生索引
- 获取必要外部证据
- 解决主链路 timing UNKNOWN
- 固化 `docs/adofai-format.md`
- 建立 regression fixtures

### M1 — Core

- Reader / Writer
- IR
- Timing engine
- diagnostics foundation
- unit / round-trip tests

### M2 — ADOFAI → mania

- 主转换链
- TimingPoints
- ColumnAssigner
- LN
- `.osu`
- `.osz`
- diagnostics
- CLI 验证

### M3 — Desktop

- Windows desktop shell
- drag/drop
- parameters
- preview
- diagnostics UI

### M4 — mania → ADOFAI

- chord serialization
- lane → geometry
- reverse timing mapping

### M5 — Polish

- batch conversion
- config persistence
- i18n
- broader workflow improvements

---

## 17. 用户待决策项

以下属于产品策略，Agent 不得自行把某个选项变成最终默认：

- `999` 最终如何映射到 mania
- `0` / U-turn 的转换呈现策略
- 多行星如何映射
- FreeRoam / AutoPlayTiles 的降级策略
- 极端 BPM 超限行为
- 默认键数
- HP / OD 默认策略
- 极短相邻音符 / 伪双押策略
- mania → ADOFAI 的 chord serialization 默认策略
- ADOFAI 特效的长期默认处理方式

在技术事实尚未确认时，应先研究技术事实，再让用户决定产品策略。

---

## 18. 当前多 Agent 工作模型

`TEAM.md` 定义角色。模型供应商分配是规划，不是仓库事实，价格不写入技术结论。

Agent 执行和本地验证以各次实际运行记录为准；本文件不保留过期的小队启动状态。

原则不变：

- 高 token、机械化扫描可交给低成本执行者
- 技术方案与 acceptance criteria 与实现分离
- Review 独立于实现自我评价
- 面向用户汇报不重复实现

---

## 19. 当前联网工具方向

Agent 应优先：

```text
web_search
→ web_fetch
→ 必要时 git clone / curl / Invoke-WebRequest
→ 保存 provenance
→ 本地分析
```

搜索摘要本身不算 ground truth。

本轮已获取、影响基线表述的公开资料：

| URL | Date | Purpose | Local copy |
| --- | --- | --- | --- |
| https://osu.ppy.sh/wiki/en/Client/File_formats/osu_(file_format) | 2026-09-17 | osu! 官方文件格式 | 无；仅笔记 |
| https://learn.modrift.org/libs/level-format | 2026-09-17 | 社区 ADOFAI 关卡结构说明 | 无；仅笔记 |

社区 pathData 角度表 **不得**当作 CONFIRMED 官方映射。

---

## 20. 已排除 / 暂不采用

当前暂不要求：

- 自动操纵 ADOFAI / osu! 客户端
- Computer Use 实机测试闭环
- ADOFAI 特效全量映射
- 过早实现 M4
- 为单一样本 hardcode
- 通过增加大量特殊分支掩盖时间模型错误
- 为了研究方便修改 `.sample` 原文件
- 把 `.sample/` 提交进 Git（未要求；当前仍 untracked）
- 本轮实现 M1/M2

---

## 21. 下一步

基线已建立。下一轮正式 M0 调研应：

1. 调查 `pathData → angle` / `!` midspin 的一手证据（官方或可验证对照样本）。
2. 在不修改 `.sample/` 的前提下，如用户授权，补充 `angleData`、Hold、Pause、非 v2、非 4K 样本。
3. 将候选 timing 语义与 `docs/research/m0-a-baseline.md` 中的 VERIFIED / INFERRED / UNKNOWN 对齐，形成 research note + minimal fixture；新增实现必须带 targeted regression 与显式 diagnostic。
4. Reviewer 独立检查证据强度。
5. 只有证据足够后，才把它在 `docs/research` 中升级为 CONFIRMED，并据此实现对应 Core 转换逻辑。

---

## 22. 维护规则

本文件记录：

- 当前阶段
- 当前真实实现基线
- 已确认转换语义
- 关键数据流
- 设计决策
- 已知限制
- 已确认 bug
- 已排除方案及原因
- 未解决问题
- 风险
- 下一步

本文件不记录：

- 聊天流水账
- Agent 思维过程
- 每条命令
- 大量原始日志
- 已过时计划
- 重复工作报告

当新证据推翻旧结论时：

**直接修正旧内容，不追加相互冲突的历史版本。**

目标始终是：

> 让 `PROJECT_UNDERSTANDING.md` 反映当前项目真实状态，而不是项目历史。
