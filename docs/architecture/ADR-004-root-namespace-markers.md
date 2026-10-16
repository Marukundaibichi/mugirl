# ADR-004: 根命名空间保留类型

状态：Superseded by [ADR-009](ADR-009-naming-and-layout.md)（2026-10-15）。下文保留原文，仅作历史记录。新规则是：根命名空间 `Mugirl` 只允许 [ADR-008](ADR-008-public-api.md) 的公开 API 类型，并且只放在 `Source/MugirlRace/PublicApi/`；其余类型由 Wave R 移入 `Mugirl.<Area>[.<Feature>]`。原来保留根命名空间的理由（XML 类名、Scribe 类名、补丁元数据）已由统一存档升级层（[ADR-007](ADR-007-save-schema-upgrade.md)）、PublicApi 别名与 `[MugirlPatch]` 特性解决。

## Decision

`Mugirl` 根命名空间继续作为历史兼容命名空间保留，但新增类型默认不再使用根命名空间。

## Context

大量 XML 字段直接引用 `Mugirl.*` 类型，包括 `Class`、`thingClass`、`compClass`、`driverClass`、`workerClass`、`incidentWorkerClass` 和 quest root。直接批量迁移命名空间会同时触碰 XML、Harmony metadata、Scribe 类名和旧运行时引用。

## Root Namespace Is Allowed For

- XML 直接引用类型。
- Scribe/存档稳定类型。
- Harmony patch 类型，直到 patch metadata audit 支持完整命名空间类名。
- DefOf、Mod 入口和必须保持 RimWorld 反射路径稳定的 marker 类型。
- 过渡期 legacy 类型，但新增时必须说明原因。

## Preferred New Namespaces

- `Mugirl.Core`
- `Mugirl.Compatibility.<Domain>`
- `Mugirl.Features.<FeatureName>`
- `Mugirl.UI`

## Consequences

- 静态验证输出 namespace boundary report，不立即失败。
- 当前 root namespace 类型数量是治理基线，不是质量目标。
- 后续若要收紧，应先建立允许清单，再禁止新增无理由 root namespace 类型。
