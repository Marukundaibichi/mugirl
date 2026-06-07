# ADR-004: 根命名空间保留类型

## Decision

`MooGirl` 根命名空间继续作为历史兼容命名空间保留，但新增类型默认不再使用根命名空间。

## Context

大量 XML 字段直接引用 `MooGirl.*` 类型，包括 `Class`、`thingClass`、`compClass`、`driverClass`、`workerClass`、`incidentWorkerClass` 和 quest root。直接批量迁移命名空间会同时触碰 XML、Harmony metadata、Scribe 类名和旧运行时引用。

## Root Namespace Is Allowed For

- XML 直接引用类型。
- Scribe/存档稳定类型。
- Harmony patch 类型，直到 patch metadata audit 支持完整命名空间类名。
- DefOf、Mod 入口和必须保持 RimWorld 反射路径稳定的 marker 类型。
- 过渡期 legacy 类型，但新增时必须说明原因。

## Preferred New Namespaces

- `MooGirl.Core`
- `MooGirl.Compatibility.<Domain>`
- `MooGirl.Features.<FeatureName>`
- `MooGirl.UI`

## Consequences

- 静态验证输出 namespace boundary report，不立即失败。
- 当前 root namespace 类型数量是治理基线，不是质量目标。
- 后续若要收紧，应先建立允许清单，再禁止新增无理由 root namespace 类型。
