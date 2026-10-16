# 兼容维护指南

本文档记录 DLC 和第三方 mod 兼容的维护方式。原则是低侵入、缺失可降级、兼容边界清楚。

## LoadFolders 与 MayRequire

- `LoadFolders.xml` 控制可选目录是否加载。1.7 的可选内容在 `1.7/Mods/<名称>`（Biotech、Odyssey、FacialAnimation、VCookE、SearchAndDestroy），由 `IfModActive` 条件加载。`v1.6` 列表及其目录已冻结，不再修改（[ADR-005](architecture/ADR-005-version-layout.md)）。
- 单个 Def 节点依赖 DLC 时使用 `MayRequire`。
- 可选目录里的补丁仍要自己保护目标存在，不要假设上游结构永远稳定。
- 不同加载目录中不能有同名的 Defs/Patches 文件，否则后者会遮蔽前者（`C.content-shadowing`）。

## Compatibility 层

放在 `Source/MugirlRace/Compatibility` 的内容适合处理：

- 第三方类型反射。
- 第三方 Def 探测。
- 第三方 mod 缺失时的 null-safe 查询。
- 与渲染或动画相关的临时兼容开关。

不适合放进去的内容：

- 本 mod 的主要业务规则。
- 复杂 UI、Job、Hediff 状态机。
- 只因为“和第三方 mod 有关系”就搬过去的大段功能代码。
- 旧存档迁移。旧名字和旧结构只放在 `Source/MugirlRace/SaveUpgrade`（[ADR-007](architecture/ADR-007-save-schema-upgrade.md)）；原 `Compatibility/Savegames` 的生产迁移实现已经移入升级层。

命名空间为 `Mugirl.Compatibility.<OtherMod>`，共用基础设施位于 `Mugirl.Compatibility`。HAR 兼容已位于 `Mugirl.Compatibility.HumanoidAlienRaces`（[ADR-009](architecture/ADR-009-naming-and-layout.md)）。

## Harmony 兼容

- 每个补丁类带 `[MugirlPatch(module, risk, failureBehavior)]`，由 `HarmonyBootstrap` 统一注册。目标的静态构造会加载资源时，设 `Deferred = true`，推迟到加载结束后再打补丁。
- 同一目标上的多个 Mugirl 补丁使用互异的显式 `[HarmonyPriority]`；`[HarmonyBefore]` / `[HarmonyAfter]` 用于其他 Mod 的 Harmony id，不用共同 owner 排本模组内部顺序（`B.patch-order`）。
- Compatibility 代码不 Harmony 补丁 Mugirl 自己的方法。由功能提供显式的扩展点（钩子或注册表），兼容模块注册进去。
- 补丁失败时优先一次性 warning 或静默降级，不要在启动或 tick 热路径刷屏。

战斗适配器现在由 `CombatCompatibilityBootstrap.Register()` 在 Mod 构造、Harmony 安装后接线。DualWield、RunAndGun、MeleeAnimation、YayoCombat、YayoAnimation、TrainingFacility 通过 `WeaponWheelHooks` / `MountingHooks` 使用原调用边界；原版目标补丁保留，10 个自补丁已删除。必须保持 Active 门控、短路、异常恢复和 tick 提前返回后的 Postfix 语义，不能把注册改成首次绘制时才发生。详见[战斗实施记录](records/refactor-1.7-combat-2026-10-15.md)。

## 被其他模组依赖的名字

MRK.WhiteFluff、har.mugirlrace.subspecies、KindSeal 等模组引用了本模组的名字。这些名字是冻结的公开 API，永不改名（[ADR-008](architecture/ADR-008-public-api.md)），包括：

- packageId `HAR.MugirlRace`、Harmony id `MugirlMod.Mod`（WhiteFluff 使用 `[HarmonyAfter("MugirlMod.Mod")]`）；
- 它们使用的 defName 和抽象 `Name`；
- XML 类名及其 XML 字段名（`Mugirl.CompProperties_MooMilkable`、`Mugirl.CompProperties_MugirlMount`、`Mugirl.StockGenerator_Mugirl_Slaves`、`<Mugirl.SlaveApparelDef>`）；
- 可补丁的扩展点 `Mugirl.MountedPawnUtility.IsMugirl(Pawn)`。

完整清单为 `tools/RenameApply/specs/public-api.json` 的 151 项，`C.public-api` 直接读取它，不另维护一份旧的 107 项清单。依赖方只在验证代码中使用的名字不冻结，迁移路径见 `tools/RenameApply/specs/owner-migration-list.md`。保持名称契约不等于替依赖方完成其 1.7 程序集移植。

旧档中的对象身份、在途 Job/Lord 与外部文本分开处理：真实 Scribe 引用才使用身份别名，未知第三方同名文字结构保留；不能遍历全部字符串做替换。需要支持新的第三方保存结构时，在升级模块登记明确 owner/ValueContext 并给正反金样，不在 Compatibility 中重新加入旧档分支。

## XML Patch 兼容

每个 `<Patch>` 文件开头保留治理注释：

```xml
<!-- PatchGovernance:
Targets: 目标 Def 或上游结构。
Scope: 本补丁只做什么，不做什么。
Duplicate guard: 如何避免重复插入。
Failure: 目标缺失或上游变化时的预期行为。
-->
```

可选集成补丁应尽量用 `PatchOperationConditional` 包住目标路径。目标缺失时允许 no-op，这比红字更适合可选兼容。

## 当前重点兼容对象

- HAR：硬依赖。种族、body、渲染节点相关的路径要谨慎。种族的 body-addon 顺序和 `MugirlBody` 的部位顺序会写进存档，不能随意调整。
- Facial Animation：通过条件目录 `1.7/Mods/FacialAnimation` 加载；只在雪牛娘种族 Def 存在时追加 comp 和动画。新增或调整 FA 内容前先看 `features/facial-animation-guide.md`，尤其是：
  - `raceName=Mugirl` 专属池；
  - `normal_Mugirl` 底座动画；
  - 贴图回退；
  - 脸红必须走 head 层；
  - 当前的整张眼睛贴图和高光路径。
- RJW / Rimworld-Animations：FA 条件目录内的动作映射、mod 名称和头部位移删除范围，以用户提供的原包为准，不按本机安装情况扩展或裁剪。只修正社交动画 XPath，并增加逐列表去重和目标缺失保护。详情见 `features/facial-animation-guide.md` 的 2026-09-21 整合记录。
- Search and Destroy：只向雪牛娘行为树插入主动搜索歼灭分支，目标漂移时 no-op。
- VCookE/PipeSystem：只追加雪牛奶转奶酪工序，工序已存在或目标缺失时 no-op。
- Melee Animation：渲染兼容层不能因为对方内部字段变化而打断绘制。
- 外部乳房 Hediff mods：只作为可选 HediffDef 探测，不混入本 mod 内部的哺乳 Hediff。
