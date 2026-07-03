# ADR-002: Harmony 扫描与 Patch 元数据

## Decision

保留 Harmony attribute 扫描作为主要 patch 注册方式，同时使用 `MugirlPatchInfo` / `MugirlPatchCatalog` 记录高风险和手动 patch 元数据。

## Context

本 mod 有大量小型 Harmony patch，也有少量手动反射 patch。完全手动注册能提高显式性，但会制造重复登记、漏注册和维护成本；完全依赖 attribute 扫描又会让 patch 目的、风险和失败行为不可见。

## Rationale

- Attribute 扫描适合普通 patch，能减少样板代码。
- Patch metadata 记录模块、目标类型/方法、patch 类型、是否跳过原方法、风险等级和失败行为。
- 手动反射 patch 继续由 `MugirlPatchRegistry` 管理，失败时用一次性 warning 降级。
- 静态验证会检查 Harmony patch 和手动 patch 是否存在 metadata，避免新增高风险 patch 变成隐形行为。

## Consequences

- 新增 patch 时必须同步更新 metadata。
- Patch metadata 是审计边界，不是运行时功能开关。
- 若 patch class 未来迁移命名空间，metadata audit 需要先支持完整类型名。
