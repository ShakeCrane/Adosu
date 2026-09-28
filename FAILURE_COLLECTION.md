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
- **修复**：保留 `done = 本子任务已交付` 语义，并升级为可执行合同：所有需要 Mika 自动续接的非 Mika 子任务在结束 Run 前必须显式执行并确认 `multica issue status <child-id> done`；非 Mika Agent 禁止主动设置子 Issue 为 `in_review`。Mika 仅在父 Issue 内部工作已收敛、等待最终外部/用户验收时使用 `in_review`；每次 dispatch 必须把 completion action 写入任务合同末尾。
- **验证状态**：NOT VERIFIED — 旧修复已确认复发；新修复需至少在真实 PASS 路径及一次非 PASS（如 `CHANGES REQUIRED / BLOCKED`）路径中验证无需用户人工改状态即可唤醒 Mika 并继续路由。
