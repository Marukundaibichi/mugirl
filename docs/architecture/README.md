# 架构决策记录

本目录保存长期架构决策。这里记录“为什么这样维护”，不记录重构阶段流水账。

当前 ADR：

- `ADR-001-single-dll.md`：保持单 DLL。
- `ADR-002-harmony-registration.md`：Harmony 扫描 + patch metadata。
- `ADR-003-compatibility-scope.md`：Compatibility 层的职责边界。
- `ADR-004-root-namespace-markers.md`：根命名空间保留条件。

维护规则：

- 修改架构约束前，先更新或新增 ADR。
- ADR 只写稳定决策、背景、后果，不写一次性任务列表。
- 与日常操作相关的步骤放回 `docs/maintenance-guide.md`、`docs/compatibility-guide.md` 或 `docs/release-checklist.md`。
