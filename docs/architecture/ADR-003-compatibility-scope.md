# ADR-003: Compatibility 层职责边界

## Decision

Compatibility 层只承载第三方类型反射、第三方 Def 探测和可选 mod glue，不整批搬业务逻辑。

## Context

HAR、Facial Animation、Melee Animation、Search and Destroy、VCookE 和外部 body/hediff mods 都会影响运行时兼容。业务模块如果直接写反射字符串或第三方 DefName，会让错误定位和可选依赖降级变困难。

## Rationale

- 反射和 Def 探测是兼容风险最高、最适合集中审计的部分。
- 业务行为仍保留在 Feature 模块，避免 Compatibility 反向依赖具体业务状态。
- 可选依赖缺失时，Compatibility 返回 null/false，调用方只处理业务 fallback。
- 静态验证已禁止第三方 `AccessTools.TypeByName` 和外部乳房 hediff 字符串回流到业务层。

## Consequences

- Compatibility 不是“第三方相关所有代码”的垃圾桶。
- 迁移业务 glue 前必须先确认没有 XML 类名、存档类名或 patch metadata 稳定性风险。
- 可以按 mod/domain 小步扩展，例如 FacialAnimation 或 VCookE 的 C# glue。
