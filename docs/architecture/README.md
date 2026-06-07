# Architecture Decision Records

本目录存放长期架构决策记录。这里记录“为什么这样设计”，不替代 `docs/10-architecture-improvement-plan.md` 的执行计划，也不替代 `docs/09-post-refactor-hardening-plan.md` 的整改流水账。

当前 ADR：

- `ADR-001-single-dll.md`：保持单 DLL。
- `ADR-002-harmony-registration.md`：Harmony 扫描 + 高风险 patch metadata。
- `ADR-003-compatibility-scope.md`：Compatibility 层只先迁移反射和探测。
- `ADR-004-root-namespace-markers.md`：根命名空间保留条件。
