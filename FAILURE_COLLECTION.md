# FAILURE_COLLECTION

本文件只记录真实、可追溯且具有复用价值的协作失败；记录失败不等于新增长期规则。事实、推断和未知原因分开书写。

## FC-2026-09-21-01 — AS-25 明确交付要求未在首轮交付中落实

- **状态**：RECORDED / OPEN
- **触发任务与日期**：AS-25，2026-09-21
- **观察到的事实**：用户在 AS-25 中明确纠正“遗漏项目目录改名”，并要求过程文件不要提交、写入 `.gitignore`。此前交付将任务按 Discussion Only 收敛，未完成该明确要求；后续交接中补写了 `.gitignore` 规则并处理了 `quick-create-description.md` 的索引状态，但目录改名因共享工作区占用仍未完成。
- **用户纠正原意**：完成目录改名；过程文件不应提交，并应写入 `.gitignore`。
- **Agent 实际错误（已证事实）**：首轮交付未逐项落实用户明确的目录改名与过程文件约束，也未在“Discussion Only”理解与该要求冲突时升级确认。
- **证据位置**：AS-25 用户评论 `01a0c1dd-3f01-775f-81cf-6bedabc5ca20` 及其交接 `01a0c1e5-4ae0-71d7-a41d-5a7ea219866c`；当前 Git 状态仍可核对 `.gitignore` 修改、`quick-create-description.md` 暂存删除和其他未提交过程文件。
- **原因**：INFERENCE — 原任务同时包含 Discussion Only 约束与目录改名要求，首轮执行将前者作为排除依据，未把冲突升级为用户确认；具体责任链仍未完全核实。
- **影响与恢复**：形成用户目标与交付范围不一致；目录改名未完成，过程文件处理产生后续补救和共享工作区阻塞。本轮不继续改名、不清理过程文件。
- **防再发措施及验证状态**：AS-32 要求在执行前拆分用户授权、仓库上位规则和任务范围，并在结束前逐项对照用户明确交付；该措施尚未经过后续独立复发验证，标记为 NOT VERIFIED。
- **长期规则变更**：NONE。本条只记录事实和当前恢复状态，不据此自动扩展 `TEAM.md` 或角色规则。

## FC-2026-09-27-01 — Worker 以 `in_review` 交付导致 Mika 无法被唤醒

- **状态**：RECORDED / RECURRED / REMEDIATION REVISED / NOT VERIFIED
- **触发与复发**：2026-09-27 首次记录；2026-09-28 用户再次确认仍会出现卡在 `in_review` 的情况。当前平台只有子 Issue 进入 `done` 才会自动唤醒 Mika。
- **观察到的事实**：Worker 已形成交付却将自身子 Issue 置为待审核 / `in_review`，随后 Mika 不会因该状态自动恢复；此前新增“完整交付后推进 `done`”规则后问题仍复发。
- **原因**：INFERENCE — 上一版修复仍主要依赖 Agent 正确解释状态语义，没有把**显式状态命令及状态确认**写成结束 Run 的硬完成动作。此次复发本身不能证明是 Worker 主动选择还是平台默认行为。
- **影响**：Stage 失去自动续接信号，需要人工重新唤醒或改状态，破坏持续目标的自主推进。
- **修复**：保留 `done = 本子任务已交付` 语义，并升级为可执行合同：所有需要 Mika 自动续接的非 Mika 子任务在结束 Run 前必须显式执行并确认 `multica issue status <child-id> done --no-start`；`--no-start` 避免 completion status 写入额外启动 child 自身普通 Run，同时保留真实 `done` 状态变化供 Stage/parent 交接。非 Mika Agent 禁止主动设置子 Issue 为 `in_review`；每次 dispatch 必须把 completion action 写入任务合同末尾。
- **验证状态**：NOT VERIFIED — 旧修复已确认复发；新修复需至少在真实 PASS 路径及一次非 PASS（如 `CHANGES REQUIRED / BLOCKED`）路径中验证无需用户人工改状态即可唤醒 Mika 并继续路由。

## FC-2026-09-28-02 — Mika 将无需人工验收的父任务置为 `in_review`

- **状态**：RECORDED / REMEDIATION APPLIED / NOT VERIFIED
- **触发任务与日期**：AS-73，2026-09-28。
- **观察到的事实**：AS-73 已完成拉取合并、评估、验证与建议交付，Mika 随后把父任务置为 `in_review`。用户此前已明确取消常规阶段人工验收，要求 Mika 在无需用户决策或额外授权时自主推进直到当前总体目标完成。
- **实际错误（FACT）**：在没有报告用户决策、额外授权、不可逆/破坏性操作、用户独有验证或上位规则人工审批需求的情况下，父任务仍进入等待审核状态，造成不必要的人类 gate。
- **原因**：FACT — 既有 `TEAM.md` / Mika 规则仍允许“父 Issue 内部工作收敛后等待最终用户验收”；INFERENCE — Multica 的 “ready for review” 默认提示进一步强化了该选择，但不能据此断言平台强制要求 `in_review`。
- **影响**：即使 Worker → Mika 的 `done` 唤醒链路正常，父任务仍可能在 Mika 收敛阶段停住，无法满足“无需逐阶段人工验收、持续推进至目标完成”的工作流目标。
- **修复**：将 `in_review` 明确定义为真实人工 gate。Mika 在父目标仍有可执行工作时继续下一 Stage；总体目标与交接条件满足且没有人工 gate 时显式执行 `multica issue status <parent-id> done --no-start`，确认后报告 `COMPLETE`，避免最终状态写入再启动自身普通 Run。仅用户决策、额外授权、不可逆/破坏性操作、用户独有验证/证据或上位规则要求人工审批时允许等待用户。
- **验证状态**：NOT VERIFIED — 需在后续真实父任务中确认：Stage 完成后仍有工作会自动继续；总体目标完成且无人工 gate 时会直接 `done`，不再停在 `in_review`。
