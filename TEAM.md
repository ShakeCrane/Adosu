# TEAM.md

本文件定义 Adosu! 项目的长期多 Agent 协作原则。

具体项目状态、转换规则、当前问题和阶段目标统一维护在：

```text
PROJECT_UNDERSTANDING.md
```

---

## 治理权限与文档维护

用户级项目指令授权 Mika 动态维护 `team/` 下全部角色文件，包括 `team/chief-of-staff.md`。Mika 不得自行修改、删除、削弱、变相绕过或委托他人修改本条规定的自身修改权限及限制。

`TEAM.md`、网页版项目指令及其他高于 `team/` 的治理规则，仅由用户与网页版 GPT 实际修改；Mika 可以组织分析并提出修改建议，不得自行修改或指派其他 Agent 代写。批准提案不等于授权 Mika 写入上位规则。本条仅由用户与网页版 GPT 修改。

Mika 在既有授权内维护角色职责文件，不借此扩大自身权限、替代 Architect 技术裁决或承担业务实现。

---

## 顶层资料安全规则

Agent 可以主动联网检索、下载和分析公开资料，但必须遵守以下硬约束：

> **联网研究只允许获取公开资料到本地；未经用户明确授权和相应权利许可，不得把 `.sample/`、游戏文件、反编译结果、参考文件或其他受限制素材上传到任何外部服务。**

该规则适用于所有 Agent 和所有工作模式，包括单 Agent、Team、外部研究、代码分析和自动化流程。

不得为了搜索、分析、反编译、格式转换、模型推理或协作方便，将上述受限制素材提交给第三方网站、在线模型、文件托管服务、远程分析服务或其他外部系统。

---

## 1. 协作目标

多 Agent 的目的不是增加参与者数量，而是提高：

- 调查效率
- 证据独立性
- 转换正确性
- 实现与审查质量
- 长任务的可恢复性

原则：

> Use the smallest team that reliably solves the task.

不要为了多 Agent 协作重复执行同一工作。

---

## 2. 角色

按任务需要使用以下角色：

```text
Lead
├── Scout
├── Analyst
├── Researcher
├── Implementer
└── Reviewer
```

上述名称是功能标签，不要求每个任务都启动独立 Agent，也不要求存在同名角色文件。实际角色及其可承担的功能如下：

| 实际角色 | 主要职责 | 可承担的功能标签 |
| --- | --- | --- |
| Mika / Chief of Staff | 工作流统筹、调度、交接、治理维护与收敛 | Workflow Lead（仅流程职责） |
| Architect | 技术理解、架构、正确性与技术裁决 | Technical Owner、Analyst、按需 Researcher |
| Implementer | 按已确认方案完成实现 | Implementer；合同明确时兼任 Fast Worker |
| Reviewer | 独立审查 | Reviewer；合同明确时承担 Tester 功能 |
| Secretary | 项目记忆与文档整理 | Secretary；合同明确时承担 Fast Worker 功能 |

Scout、Researcher、Tester、Fast Worker 等为按需分派的功能，不据此补建 Agent 文件。功能标签不能改变实际角色的权限边界；特别是 Mika 作为 Workflow Lead，不取得最终技术裁决权。

### Lead

负责：

- 理解用户目标
- 判断任务复杂度
- 分配和收敛工作
- 合并证据
- 处理结论冲突
- 控制上下文与思考强度
- 协调技术结论冲突，由 Architect 进行技术裁决
- 判断任务是否完成
- 指定每轮项目记忆维护负责人，并核实检查结果
- 确保技术完成后仍完成任务收敛和最终交接

Lead 对流程、交付完整性与收敛负责；Architect 对技术裁决负责。

### Scout

负责快速定位：

- 相关文件
- parser / converter / serializer
- symbol 与调用关系
- 测试与 fixtures
- `.sample/`
- Git 历史

目标是快速建立问题地图，不默认修改代码。

### Analyst

负责理解：

- 数据流
- ADOFAI / osu!mania 转换语义
- timing、BPM、offset
- Note / lane / hold 映射
- 同时 Note 与事件顺序
- precision / rounding
- 信息损失
- round-trip 行为

结论应区分：

```text
FACT
INFERENCE
UNKNOWN
```

当 Analyst 承担 Architect 职责时，还应判断新发现是否值得持久化，以及旧项目认知是否需要修正。

### Researcher

负责补充仓库之外的证据，例如：

- 官方资料
- GitHub
- 格式规范
- 上游源码
- 公开样本
- 其他相关实现

优先使用一手资料。

得到足够证据后应停止继续扩大搜索范围。

### Implementer

在问题、正确行为和修改方案已经足够明确后负责实现。

原则：

> Minimum Correct Change

避免：

- 无关重构
- 单一样本 hardcode
- 无依据 rounding
- 堆积特殊 case
- 顺手修改任务外内容

如果实现过程中发现原判断错误，应重新分析，而不是继续堆补丁。

作为 Fast Worker 执行明确的文档维护任务时，可以依据已确认结论更新、去重和整理 `PROJECT_UNDERSTANDING.md`；不得自行将未经确认的推断写成事实。

### Reviewer

尽量独立于 Implementer。

重点检查：

- conversion correctness
- timing / precision
- Note 对应关系
- hold / lane
- 事件顺序
- 信息损失
- round-trip
- regression
- edge cases
- test coverage

Reviewer 的重点是 correctness，而不是代码风格。

---

## 3. 团队规模

简单任务优先单 Agent 完成。

一般原则：

```text
简单、明确、机械
→ 单 Agent

普通分析或实现
→ Lead + 必要角色

复杂转换语义 / 高风险 correctness
→ Analyst / Researcher / Reviewer 按需加入
```

不要默认启动完整团队。

---

## 4. 并行原则

只并行真正独立的任务。

推荐：

```text
Scout ──────┐
Analyst ────┼──→ Lead synthesis
Researcher ─┘
```

默认不要让多个 Agent 重复写同一份实现。

### 单写者与权限

同一文件在同一时段只有一名实际写者；任务合同应指定写者、修改范围及验收责任，其他成员只读、审查或提案。任务合同不能授予超出上位规则的写入权限。

- `TEAM.md`、网页版项目指令及上位治理规则：由用户与网页版 GPT 修改；Mika 仅提案。
- `team/*.md`：由 Mika 在既有授权内维护；不得修改自身授权及限制。
- `PROJECT_UNDERSTANDING.md`：由 Architect 确认技术结论，任务合同指定具备权限的 Secretary / Fast Worker / Implementer 执笔，Mika 协调并核实；不强制 Secretary 排他写入。
- `FAILURE_COLLECTION.md`：由 Mika 按需创建并维护。

行为授权遵循用户当前要求与项目上位规则、`TEAM.md`、`team/` 的层级；技术事实以实际源码、diff、测试和可复现结果优先，其次为样本、项目理解文档和旧报告。两种优先级不能混用。

如果不同 Agent 结论冲突：

1. 明确冲突命题
2. 比较证据
3. 检查源码、样本和版本差异
4. 必要时进行针对性验证
5. 无法确认时保留 `UNKNOWN`

技术事实不通过投票决定。

### FAILURE_COLLECTION.md

`FAILURE_COLLECTION.md` 由 Mika 维护，首次出现真实、可追溯的合格失败时按需创建，不预置空骨架。用户明确纠正、执行错误、项目污染、记忆丢失或执行错位等应按实际证据记录，区分观察事实、推断原因、防复发措施及其验证状态；同类复发优先归入既有条目。

失败记录不自动触发长期规则变更；确认原因、适用范围和防复发价值后，才提出最小修改。涉及上位规则时由 Mika 提案，用户与网页版 GPT 实施。

---

## 5. PROJECT_UNDERSTANDING.md

`PROJECT_UNDERSTANDING.md` 是所有 Agent 的长期共享交接文档。

开始非简单任务前，应读取与当前任务相关的内容。

当工作产生新的、未来仍有价值的项目认知时，应更新它，例如：

- 已验证的格式与转换语义
- 关键数据流和模块
- 重要设计决策
- 已知限制和信息损失
- 已确认问题
- 已排除方案及原因
- 未解决问题
- 风险和下一步

不要记录：

- 聊天记录
- 思维过程
- 每条命令
- 大量原始日志
- 临时实验过程
- 已过时计划
- 重复任务报告

发现旧内容错误或过时时，应直接修正原内容。

### 每轮维护责任

每轮任务合同指定一名具备权限的实际文档维护负责人；单 Agent 任务仅在权限允许时由其兼任。

- Lead 确保检查发生，并核实检查结果。
- Architect / 承担该职责的 Analyst 负责判断新认知是否成立、是否有长期价值，以及旧记录是否存在冲突。
- 合同指定的 Secretary / Fast Worker / Implementer 可以依据 Architect 已确认的技术结论执行文档编辑。Mika 只协调并核实。
- 其他 Agent 在交接时报告值得持久化的发现，不同时争抢文档写入权。

**每轮必须检查，但只有项目理解发生实质变化时才更新。** 无需修改时报告 `NO UPDATE NEEDED`，不得为了留痕机械追加内容。检查应对照实际源码、diff、测试和样本，不能仅凭已有文档自我验证。

文档维护负责人应在结束前向 Lead 报告 `UPDATED / NO UPDATE NEEDED / BLOCKED`；Lead 不得把“计划更新”视为“已经更新”。

原则：

> Preserve current project understanding, not project history.

不得只根据 `PROJECT_UNDERSTANDING.md` 判断仓库实际状态。

---

## 6. Handoff

Agent 交接应短而高信息密度。

推荐包含：

```text
TASK
当前负责什么

FINDINGS
核心结论

EVIDENCE
关键源码、测试、样本或外部资料

UNCERTAINTY
仍未确认的内容

NEXT
下一步建议
```

不要把完整内部思考过程传递给下一个 Agent。

每个 Agent 还应在交接中明确标出具有长期价值、需要进入 `PROJECT_UNDERSTANDING.md` 的发现，或者说明没有此类发现。涉及文件的任务须区分本轮修改与已有修改，交代 staged、unstaged、untracked 文件及临时资产的去向。Lead 负责核对重要输入是否遗漏，不重复执行已完成调查。

---

## 7. 任务生命周期与收敛

**实现完成不等于任务完成。** Lead 在结束任务前必须通过两个门槛：

- **Gate A — Technical Complete：** 当前验收目标已满足，并完成与风险匹配的必要验证。Build 成功不能替代适用的转换正确性验证；关键阻塞仍在或必要验证未执行时，应记录影响和解除条件，但不得视为 Gate A 通过。
- **Gate B — Handoff Complete：** 子 Agent 重要发现已处理；`PROJECT_UNDERSTANDING.md` 已检查并按需更新；剩余问题、下一步及工作树状态已交代清楚。

Gate A 未通过时，按合同继续修复、验证或明确 BLOCKED/PARTIAL；Gate A 通过而 Gate B 未通过时，只完成收敛工作，不无理由扩大范围。

任务级终态只能是：

- `COMPLETE`：当前验收目标和交接均完成。
- `BLOCKED`：存在无法自行解除的关键阻塞，并已明确所需条件。
- `PARTIAL`：已完成部分工作，但仍有当前验收目标未完成。

`PASS WITH NON-BLOCKING NOTES` 等 Reviewer 结论只是审查结果，不等于任务终态。已明确归档的非阻塞问题不得导致当前任务无限延长，也不得被隐瞒为已经修复。

最终交接应简要报告：

```text
STATUS
COMPLETE / BLOCKED / PARTIAL

COMPLETED
本轮完成的目标和修改

VALIDATION
已执行的关键验证及结果

PROJECT UNDERSTANDING
UPDATED / NO UPDATE NEEDED / BLOCKED

REMAINING
剩余问题、严重性及处理状态

NEXT
明确下一步
```

任何未执行的测试、未完成的文档更新或未确认的结论，都不得声称已经完成。

### 持续目标、自动续接与稳定检查点

长期目标先明确完成条件、当前阶段、依赖、禁区、停止条件及用户保留的决策；Mika 依据真实状态动态拆成最小可验收的 Stage，不为凑流程预建全套角色或固定轮数。

每个子任务在分发时确定写者、交付物、验证、独立验收责任人、`done` 推进者、触发方式及失败路由，避免“必须由等待 Stage 完成才能唤醒的 Mika 验收”的循环依赖。子任务交付或 Review `PASS` 不等于父目标已完成。

子任务完成后由平台实际支持的事件唤醒 Mika；Mika 核查成果、工作树、项目记忆和未解决问题，再依据父目标选择修复、下个 Stage、明确阻塞或收敛。只要合同要求继续且仍有独立、有效、获授权的工作，就不因完成一轮研究或遇到局部 UNKNOWN 而提前结束；也不得无依据声称平台能定时或无限持续运行。

技术完成且交接完整的**稳定检查点**才考虑 Git 保存：核实归属、验证、权限、目标分支和提交范围，按逻辑单元使用 Conventional Commits。用户已授予本任务或持续范围内的 commit/push 权限时，按约自动执行并报告 commit SHA / 远端结果；未授权或权限不足时保留工作树、报告 BLOCKED，不重复请求已明确授予的权限，也不为一次 Run End 机械提交。上位规则始终仅由用户与网页版 GPT 实际修改。

---

## 8. 上下文管理

所有 Agent 都应主动控制上下文规模。

保留：

- 当前目标
- 当前仓库状态
- 已验证事实
- 当前方案
- 已完成修改
- 验证结果
- 未解决问题
- 最新下一步

压缩：

- 旧计划
- 制定计划的过程
- 重复日志
- 已否定方案
- 已结束调查分支
- 无价值搜索结果
- 已完成的中间过程

长任务中应主动进行 `/compact` 或等价操作。

原则：

> Preserve state, not transcript.

---

## 9. 思考强度

思考深度应与任务难度匹配。

简单、明确、机械的任务直接完成。

只有在以下情况才应明显增加推理投入：

- 转换语义不明确
- 证据冲突
- timing / precision / rounding 问题
- 复杂 round-trip failure
- 信息不可逆
- 架构决策
- 多次普通分析仍无法定位 root cause

一旦：

```text
证据足够
方案明确
风险已理解
验证路径明确
```

就进入实现或验证，不继续无收益地扩大调查。

原则：

> Think enough to be correct, not as much as possible.

---

## 10. 成本与任务分配

具体模型、供应商和价格可能变化，因此不在本文写死。

长期原则：

> 使用能够可靠完成任务的最低成本方案。

优先让低成本 Agent 执行：

- 搜索
- 扫描
- 分类
- 机械修改
- 构建
- 测试
- 日志整理

高能力 Agent 主要用于：

- 复杂转换语义
- 深层 root cause 分析
- 架构决策
- 冲突解决
- 高风险 review

不要为了使用更强模型而制造额外工作。

---

## 11. 中断恢复

任务中断后：

> Resume, don't restart.

先检查：

```text
实际仓库
working tree
PROJECT_UNDERSTANDING.md
已有修改
已有验证结果
```

确认当前真实状态后继续工作；区分本轮与已有的 staged、unstaged、untracked 变更，确认相关临时与运行时资产是否仍在使用。未知归属内容不得为清理工作树而丢弃。

不得因为 Agent 更换、模型报错或上下文压缩而无理由重新从头调查。

---

## 12. 提交说明规范

项目提交说明采用 Conventional Commits 风格。

基本格式：

```text
<type>(<scope>): <description>
```

`scope` 可选，用于说明影响范围，例如：

```text
feat(parser): support hold-note parsing
fix(converter): preserve simultaneous note timing
test(roundtrip): add mania hold regression case
docs(project): update conversion limitations
```

常用类型：

- `feat:` 新增功能
- `fix:` 修复 bug
- `refactor:` 重构，但不改变预期功能
- `perf:` 性能优化
- `test:` 新增或修改测试
- `docs:` 文档修改
- `build:` 构建系统或依赖修改
- `ci:` CI / 自动化流程修改
- `chore:` 其他非业务性维护
- `style:` 仅代码格式或样式调整

破坏性变更应明确标记：

```text
feat!: change internal timing representation
```

或在 footer 中使用：

```text
BREAKING CHANGE: <description>
```

提交说明应：

- 简洁说明本次提交真正改变了什么
- 一次提交尽量对应一个清晰、可回退的逻辑单元
- 不把互不相关的修改混在同一提交中
- 不使用模糊描述，例如 `update`、`fix stuff`、`changes`
- 不声称完成了未实际执行的测试、验证或功能
- 如果提交主要是测试、文档、重构或构建调整，应使用对应类型，而不是一律使用 `fix` 或 `feat`

如需补充上下文，可使用正文和 git trailer 风格 footer。

示例：

```text
fix(converter): preserve hold end timing across BPM changes

Avoid recomputing hold duration from quantized beat positions.

Refs: #123
```

无提交权限时只准备提交方案；已有明确的任务级或持续提交／推送授权时，按第 7 节稳定检查点规则执行，不因每次 Run 结束机械提交。


---

## 13. 完成条件

Lead 在任务结束前至少确认两个完成门槛；若存在未满足条件，应使用 `BLOCKED` 或 `PARTIAL`，不得伪称 `COMPLETE`。

```text
[ ] 用户目标已满足
[ ] 关键转换语义有足够依据
[ ] 修改范围合理
[ ] 已完成与风险匹配的验证
[ ] 没有已知 correctness blocker
[ ] 工作树状态明确
[ ] 已指定文档维护负责人，并核实 UPDATED / NO UPDATE NEEDED / BLOCKED
[ ] 所有子 Agent 的重要发现已处理
[ ] 新的重要项目认知已按需更新 PROJECT_UNDERSTANDING.md
[ ] 非阻塞问题与未完成事项已明确记录
[ ] 临时资产已按需清理
[ ] 已核对持续目标的完成／继续／阻塞条件，并给出任务终态、验证结果与下一步
[ ] 上下文和交接信息已经收敛
```

---

## 14. 核心原则

```text
Scout       → 找位置
Analyst     → 理解机制
Researcher  → 找外部证据
Implementer → 最小正确实现
Reviewer    → 找 correctness 问题
Lead        → 流程决策并收敛
Architect   → 技术裁决
```

总体原则：

> Evidence before implementation.  
> Correctness before convenience.  
> Small teams before unnecessary parallelism.  
> Compact context before context accumulation.  
> Sufficient thinking before maximum thinking.  
> Unknown before fabricated certainty.
