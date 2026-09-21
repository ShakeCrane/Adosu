# TEAM.md

本文件定义 Adosu! 项目的长期多 Agent 协作原则。

具体项目状态、转换规则、当前问题和阶段目标统一维护在：

```text
PROJECT_UNDERSTANDING.md
```

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

角色表示职责，不要求每个任务都启动独立 Agent。

### Lead

负责：

- 理解用户目标
- 判断任务复杂度
- 分配和收敛工作
- 合并证据
- 处理结论冲突
- 控制上下文与思考强度
- 做最终技术判断
- 判断任务是否完成
- 指定每轮项目记忆维护负责人，并核实检查结果
- 确保技术完成后仍完成任务收敛和最终交接

Lead 对最终结果负责。

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

如果不同 Agent 结论冲突：

1. 明确冲突命题
2. 比较证据
3. 检查源码、样本和版本差异
4. 必要时进行针对性验证
5. 无法确认时保留 `UNKNOWN`

技术事实不通过投票决定。

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

每轮任务必须指定一名实际文档维护负责人；单 Agent 任务由该 Agent 兼任。

- Lead 确保检查发生，并核实检查结果。
- Architect / 承担该职责的 Analyst 负责判断新认知是否成立、是否有长期价值，以及旧记录是否存在冲突。
- Fast Worker / Implementer 可以依据已确认结论执行文档编辑。
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

每个 Agent 还应在交接中明确标出具有长期价值、需要进入 `PROJECT_UNDERSTANDING.md` 的发现，或者说明没有此类发现。Lead 负责确认重要输入没有被遗漏，不必重复执行子 Agent 已完成的调查。

---

## 7. 任务生命周期与收敛

**实现完成不等于任务完成。** Lead 在结束任务前必须通过两个门槛：

- **Gate A — Technical Complete：** 当前验收目标已满足；关键 correctness 验证已执行，或者关键阻塞已明确记录。Build 成功不能替代适用的转换正确性验证。
- **Gate B — Handoff Complete：** 子 Agent 重要发现已处理；`PROJECT_UNDERSTANDING.md` 已检查并按需更新；剩余问题、下一步及工作树状态已交代清楚。

Gate A 未通过时，继续必要的修复或明确阻塞；Gate A 通过而 Gate B 未通过时，只完成收敛工作，不无理由开启新的调查或扩大范围。

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

确认当前真实状态后继续工作。

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

除非用户明确要求，否则 Agent 不默认执行 commit；可以准备或建议符合本规范的提交说明。


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
[ ] 已给出任务终态、验证结果与下一步
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
Lead        → 决策并收敛
```

总体原则：

> Evidence before implementation.  
> Correctness before convenience.  
> Small teams before unnecessary parallelism.  
> Compact context before context accumulation.  
> Sufficient thinking before maximum thinking.  
> Unknown before fabricated certainty.
