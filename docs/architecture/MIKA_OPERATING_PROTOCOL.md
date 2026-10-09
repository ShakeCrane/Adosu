# Mika Operating Protocol — 事件与结果合同

> **单一职责：** 本文件定义 Mika 的事件状态机、专业 Agent 交付字段与失败恢复。全体治理和权限在 `TEAM.md`，角色独有要求在 `team/`，实际 mention/activity/唤醒和权限服从 Multica 的 Squad 系统协议。

## 1. 事件闭环

```text
用户目标 → Squad 父 Issue → Mika Run → 派发 → Mika 结束 Run
                      ↑                       ↓
                  事件唤醒 ← 成员在同 Issue 发布结构化结果
                      ↓
                  Mika 评估 → 下一项工作 / 整体收敛 / BLOCKED
```

使用一个 Squad 持有的父 Issue 默认串行接力；确需独立生命周期、并行或跨 Issue 依赖才新建子 Issue。已分配 todo 子 Issue 和父 Issue 的 @mention **不可重复派发同一工作**。不要让一个 Agent Run 占用上下文轮询等待其他角色。

## 2. 四层状态与恢复

| 层级 | 含义 |
| --- | --- |
| Agent Run | 平台调用结束或失败 |
| 本轮交付 | 按合同提交有效产物、结果与证据 |
| Stage / 子任务 | 阶段依赖和实际验收是否满足 |
| 父目标 | 整体业务目标是否完成，Gate A/B 是否均通过 |

Mika 逻辑状态：`RECEIVE → TRIAGE → DISPATCH → WAIT_EVENT → EVALUATE → CONTINUE / WRAP_UP / BLOCKED`。这些不是要在 Multica 中新增的状态名。

每次收到事件，Mika 先核对父目标、**上一次派工**、最新结果是否为该任务的真实新交付、是否重复、证据与依赖是否充分，再选下一责任人：核心语义/根因未知 → Architect，方案明确需改代码 → Implementer，作者交付需独立审查 → Reviewer，长期技术认知变化 → Secretary。成员 `NEXT` 仅供参考，最终由 Mika 决策。无有效下一动作时按平台协议 `no_action`，不得制造空转。

## 3. Agent 统一结构化交付

成员在被派发的 Issue 发布一条中文工作报告，结尾附以下**统一字段**。报告保留事实和证据，字段不能替代实际验证：

```yaml
ROLE: Architect                 # Architect | Implementer | Reviewer | Secretary
DELIVERY: COMPLETE             # COMPLETE | PARTIAL | BLOCKED | FAILED
VERDICT: READY                  # READY | PASS | PASS_WITH_NOTES | CHANGES_REQUIRED | UNKNOWN | N_A
SUMMARY: "实际结论"
EVIDENCE:
  - "可核查源码/行号、diff、测试命令与结果或公开来源"
CHANGES: []                      # 本轮真正修改的文件；无则 []
UNKNOWNS: []                     # 仍不确定的事项；无则 []
MEMORY: NO_UPDATE_NEEDED         # UPDATED | NO_UPDATE_NEEDED | UPDATE_REQUIRED | BLOCKED
NEXT:
  OWNER: Mika                    # Architect | Implementer | Reviewer | Secretary | Mika | User | NONE
  ACTION: "推荐的下一动作和依据"
BLOCKER: NONE                    # 无阻塞为 NONE，有则写明解除条件
```

`DELIVERY` 表示本次任务交付是否完成，`VERDICT` 是技术/审查结论；例如 Reviewer 可以 `DELIVERY: COMPLETE`、`VERDICT: CHANGES_REQUIRED`。Implementer 自测 PASS 不代替独立 Reviewer。未进行测试不得声称通过；尚未同步的记忆应写 `UPDATE_REQUIRED` 而不是 `UPDATED`。成员无需另 @下一角色，由 Multica 评论事件唤醒 Mika。

## 4. 分支与故障

- `CHANGES_REQUIRED`：Mika 据根因交 Implementer 修复或交 Architect 重审方案，再由 Reviewer 视风险复核。
- `UNKNOWN`：关键未知交 Architect 收集证据；其他依赖独立、已经授权的工作可继续。
- `BLOCKED`：明确业务证据、用户决策/权限、工作区冲突或平台事件故障等原因，给出责任人、解除条件和恢复触发，不把 PARTIAL 假装完成。
- 平台评论未触发、Runtime 不在线或旧子 Issue 不能续接：检查 Issue timeline、Run 状态、Roster、安装版本及可用 Wakeup；Resume, don't restart，不能强制改 `done` 制造假信号。
- Gate A/B 全通过且无合法后续工作才报告父目标 COMPLETE；受 Multica Squad 系统协议约束，父 Issue 最终 `in_review` 不能用普通 Prompt 越权改 `done`。这不是要求逐 Stage 人工验收。
- 文档与源码可静态证明预期机制，但不是本机连续运行的验证；用户只负责本地基线，不专门创建空跑 Issue 或 PASS/FAIL 演练。

> Preserve state, not transcript.
