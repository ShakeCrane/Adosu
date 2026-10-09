# Adosu!

ADOFAI ↔ osu!mania 逐 Note 双向转换，优先准确、稳定、可解释地保持谱面语义，而不仅是生成合法文件。功能是否已经实现、已验证和仍为 UNKNOWN 的信息请以 [PROJECT_UNDERSTANDING.md](PROJECT_UNDERSTANDING.md) 结合**实际源码与测试**为准。

## 文档导航（单一权威）

| 文件 | 独立职责 |
| --- | --- |
| [TEAM.md](TEAM.md) | 全体共同治理：权限、安全、正确性、Git 和完成门槛 |
| [team/](team/) | Mika 与各专业成员的**专属**职责及执行原则 |
| [MIKA_OPERATING_PROTOCOL.md](docs/architecture/MIKA_OPERATING_PROTOCOL.md) | 自治事件状态机、统一交付字段和异常恢复 |
| [MULTICA_SQUAD_INSTRUCTIONS.md](docs/architecture/MULTICA_SQUAD_INSTRUCTIONS.md) | Multica Squad 配置中的简短指引入口 |
| [PROJECT_UNDERSTANDING.md](PROJECT_UNDERSTANDING.md) | 当前技术模型、实现、进度、证据限制和下一步 |
| [docs/research/m0-a-baseline.md](docs/research/m0-a-baseline.md) | M0-A 特定技术基线与原始证据界限 |
| [docs/research/sample-corpus-index.md](docs/research/sample-corpus-index.md) | 本地样本覆盖的派生统计，不是原始谱面 |
| [FAILURE_COLLECTION.md](FAILURE_COLLECTION.md) | 真实历史协作失败与修复/复发记录 |

共性规则只在 `TEAM.md` 修改，角色特殊要求只在对应 `team/*.md` 修改，状态机/机器可读字段只在 `MIKA_OPERATING_PROTOCOL.md` 修改。Squad 指引负责加载和路由，不成为第二份权威规则。研究证据记录不代替当前技术状态；失败记录中的历史修复措施也不能代替当前协议。

旧 Quick Create 任务提示词不再作为仓库常设文件；如需历史溯源，可从 Git 提交记录获取。
