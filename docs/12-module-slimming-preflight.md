# Module Slimming Preflight

本文档记录阶段 E 的预备审计和首轮执行结果。首轮执行只做机械拆分，不改 XML 数值、DefName、save key、Scribe 字段名、玩家可见文本、tick 频率或 Harmony 目标。

## 当前体量

| Module | Files | Lines | Max file |
| --- | ---: | ---: | --- |
| Milk | 30 | 4438 | `MooGirlMilkingAnimation.cs` 603 lines |
| Mounting | 12 | 2293 | `MountedPawnCombatTurret.cs` 749 lines |
| Restraints | 31 | 4280 | `HediffCompProperties_PerformanceEffect.cs` 593 lines |

Phase 6 输出同一组统计，作为阶段 E 后续切片的体量基线。

## 首轮执行结果

截至 2026-06-07，Mounting、Milk、Restraints 的首轮机械拆分已完成，Release 编译已通过。

| Module | Files | Lines | Max file |
| --- | ---: | ---: | --- |
| Milk | 32 | 4453 | `MooGirlMilkingAnimation.cs` 393 lines |
| Mounting | 21 | 2367 | `MountedPawnCombatTurret.cs` 339 lines |
| Restraints | 35 | 4309 | `CompProperties_BrainwashHelmet.cs` 392 lines |

本轮拆分落点：

- Mounting：`MountedCombatRendering`、`MountedCombatTargeting`、`MountedCombatWeaponEligibility`、`Comp_MooGirlMount.Gizmos`、`Comp_MooGirlMount.Lifecycle`、`Comp_MooGirlMount.Storage` 等文件拆出；`Comp_MooGirlMount`、`MountedCombatController` 保持原类名和行为入口。
- Milk：`MooGirlMilkingAnimation.State` 和 `Harmony_MooGirlMilkingAnimation` 拆出；动画状态字典、pawn 生命周期清理和 Harmony patch glue 不再混在主动画数学文件中。
- Restraints：`BrainwashPerformancePlayer`、`GameComponent_BrainwashPerformance`、`HediffComp_BrainWashingStar`、`Comp_MagneticShackles.Gizmos` 拆出；`CompProperties_PerformanceEffect` 和 `CompProperties_MagneticShackles` 保持 XML 直接引用类名不变。

所有 C# 文件当前均低于 400 行。最终发布前仍必须完成 Phase 6、package 和完整游戏内 smoke 验证。

## 不变量

阶段 E 的任何拆分都必须保持：

- 不改 XML 数值、DefName、save key、Scribe 字段名和 XML 直接引用类名。
- 不改变玩家可见功能、Gizmo、FloatMenu、Message、Letter、音效、贴图和 tick 频率。
- 不改变 Harmony patch 目标和 patch metadata，除非同一批同步更新 Phase 6 审计。
- 每次只拆一个文件或一个职责簇。
- 每次拆分后运行 Release build、Phase 6、package；本轮按用户要求集中完成代码拆分后统一进入游戏内 smoke 验证。

## 建议切片

### Mounting

优先文件：`MountedPawnCombatTurret.cs`、`Comp_MooGirlMount.cs`。

建议顺序：

1. 只抽纯查询/选择逻辑，例如 target search、verb eligibility、warmup override 查询。
2. 再抽临时修改原版对象状态的 acquire/release 结构。
3. 最后才处理 tick 调度和渲染 patch。

验证重点：骑乘、下马、骑手远程/近战、装备移除、warmup 绘制、骑乘 pawn despawn/destroy。

### Milk

优先文件：`MooGirlMilkingAnimation.cs`、`CompMooHasBodyResource.cs`、`JobDriver_GatherBodyResources.cs`。

建议顺序：

1. 只分离 milking animation state store、render patch glue 和效果播放 helper。
2. 再分离资源采集 job 的目标验证、进度计算和提交。
3. 最后处理 baby feeding / nurture 的跨系统逻辑。

验证重点：挤奶动画、产奶进度、喝奶/喂奶、婴儿喂养、nurture 成长、pawn despawn/destroy 后状态清理。

### Restraints

优先文件：`HediffCompProperties_PerformanceEffect.cs`、`CompProperties_MagneticShackles.cs`、`CompProperties_BrainwashHelmet.cs`。

建议顺序：

1. 只抽 XML props 数据结构和运行期 player/state。
2. 再抽 hediff/apparel 应用规则。
3. 最后处理 Gizmo、manual use、tick state machine。

验证重点：束具锁定/解锁/破解、脑控演出、电击、磁力镣铐、错误钥匙、装备移除、保存读档。

## 当前关口

阶段 C 的 Compatibility 迁移已经触碰运行时反射和内部 required Def 缓存；阶段 E 首轮拆分完成后，需要至少完成：

- `01-minimal.xml` fresh 加载；
- `06-all-integrations.xml` fresh 加载；
- 进入地图、保存、读档；
- 检查 `Player.log` 中 MooGirl/HAR/FA/SearchAndDestroy/VCookE/MeleeAnimation 相关异常。
