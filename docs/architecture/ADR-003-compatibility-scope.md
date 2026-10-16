# ADR-003: Compatibility 层职责边界

状态：Accepted（2026-10-15 复核并补充“1.7 规则”一节；其中的扩展点与迁移尚未实施，计划在 Wave S）。

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

## 1.7 规则（2026-10-15 补充）

### 新增规则

- **Compatibility 不对 Mugirl 自己的方法打 Harmony 补丁。** 功能模块提供显式扩展点（钩子与注册表），兼容模块在启动时向它们注册（[design.md](../refactor-1.7/design.md) §3.5）。
  - 兼容模块对原版或第三方方法的补丁可以保留。
  - 扩展点的接口以 combat 模块计划为准（decisions C07）：`WeaponWheelHooks` 与 `MountingHooks`，见 [combat-plan.md](../refactor-1.7/module-plans/combat-plan.md) S2-1。
  - 兼容一侧由 core 负责，见 [core-plan.md](../refactor-1.7/module-plans/core-plan.md) S2-3：每个 `<Mod>Compatibility` 在 `[StaticConstructorOnStartup]` 静态构造中，于对方 Mod 启用时注册。
- **功能代码不直接调用 Compatibility。** 例如“训练设施绕过武器轮盘战斗”应改为功能模块的扩展点，由兼容模块注册。
- **命名空间：**
  - 每个第三方 Mod 一个 `Mugirl.Compatibility.<OtherMod>`；
  - HAR 用 `Mugirl.Compatibility.HumanoidAlienRaces`；
  - 无后缀的 `Mugirl.Compatibility` 只放共享基础设施，例如 `ModCompat`、`CompatAccessors`（C29）。

  完整约定见 [ADR-009](ADR-009-naming-and-layout.md)。
- **存档迁移不属于 Compatibility。** 旧档迁移只在统一升级层中（[ADR-007](ADR-007-save-schema-upgrade.md)）。

### 现状与待办（Wave S）

| 债务 | 位置 | 处理 |
|---|---|---|
| 10 个兼容补丁类直接补丁 Mugirl 方法（`Comp_WeaponWheel`、`WeaponWheelAnimationRenderer`、`MountedPawnMeleeSupport`） | `Compatibility/{DualWield,RunAndGun,YayoCombat,MeleeAnimation,YayoAnimation}/*Patches.cs` | 扩展点落地后删除，改为注册 |
| 功能直接调用 `TrainingFacilityCompatibility` | `Features/WeaponWheel/Combat/Comp_WeaponWheel.Combat.cs` 3 处；`Features/WeaponWheel/Dev/WeaponWheelDevLog.cs` 读取兼容层的 `Active` 标志 | 改为功能模块的扩展点 |
| 本 Mod 自己的企业行为放在了兼容目录 | `Compatibility/CorporatePricing/CorporatePricingCompatibility.cs` | 改为 `Mugirl.Features.Corporate.CorporateForeignBehavior`（C01） |
| 旧档迁移放在兼容目录 | `Compatibility/Savegames/` 中的两个 `Game.ExposeSmallComponents` 前缀 | 移入 `SaveUpgrade/`；它们记在 `structure-debt.allow.txt` 与 `architecture-rules.json` 的 `save-internals` 例外中 |

### 计划中的检查

core-plan 请求增加两项检查（S4）：

- 验证器规则：没有 Harmony 补丁以 `MugirlRace` 中声明的方法为目标；
- 游戏内 StartupSuite 检查：`MugirlMod.Mod` 拥有的补丁不以 Mugirl 方法为目标。

### 已有检查

`B.architecture`（`Baselines/architecture-rules.json`）已有两条规则：

- `third-party-type-lookup`：`AccessTools.TypeByName` 等只能在 `Compatibility/` 中使用；
- `external-breast-hediffs`：外部乳房 hediff 名只能在 `ExternalBreastHediffDefs` 中出现。
