# Stage 2 Core Infrastructure Refactor

## Scope

本阶段开始把高重复、热路径、跨模块共享的基础逻辑收束到 `Core` 与 `Features` 层。目标不是调整玩法，而是建立后续彻底重构可以依赖的稳定基础。

本阶段遵守以下锁定规则：

- 不改数值。
- 不扩大或缩小行为适用范围。
- 缺失 Def 的 required / optional 语义保持不变。
- 不引入旧档兼容逻辑。
- 只迁移能证明等价的公共逻辑。
- 新增工具类必须有足够注释解释设计原因、兼容性或性能取舍。

## Completed Changes

### Identity

新增 `Core/MooGirlIdentity.cs`，集中 MooGirl Pawn 身份判断：

- `IsMooGirlDef`
- `HasMooGirlBody`
- `IsMooGirlPawn`

已迁移的等价调用点：

- `MountedPawnUtility.IsMooGirl`
- `MooGirlJuvenileGraphicUtility.NormalizeBodyType`
- `MooGirlBirthXenotypeUtility`
- `MooGirlWildSlaveUtility` 中的 `ThingDef/body` 部分

`MooGirlWildSlaveUtility` 保留原有 `PawnKindDef` 判断，不改变野生奴隶、预逃亡奴隶的覆盖范围。

### Def Cache

新增并继续扩展：

- `Core/MooGirlOptionalDefs.cs`
- `Core/MooGirlRequiredDefs.cs`

已缓存的固定 Def 名称包括：

- 乳房 HediffDef 兼容列表
- `MooGirl_Lactation`
- `MooGirl_Milk`
- `MooGirlMilkFilth`
- `MooGirl_Milking_Sound`
- 牛奶互动 JobDef
- 挤奶喷溅 FleckDef
- 牛奶互动 ThoughtDef / HediffDef

迁移时保持原语义：

- 原来 `GetNamed` 的仍作为 required Def。
- 原来 `GetNamedSilentFail` 的仍作为 optional Def。
- 调用点仍按原方式处理 `null`。

### Milk Breast Profile

新增 `Features/Milk/MooGirlBreastProfileUtility.cs`，集中乳房 Hediff 对产奶周期和产量倍率的影响。

保持原顺序与原数值：

- 特殊乳房：`3f` 天，`1.5f` 倍
- 普通女性/普通乳房：`1.2f` 天，`1f` 倍
- 小乳房：`1f` 天，`0.75f` 倍
- 大乳房：`1.5f` 天，`1.25f` 倍
- 平胸/男性回退：`0.85f` 天，`0.5f` 倍

### Apparel Tag Utility

新增 `Features/Apparel/MooGirlApparelTagUtility.cs`，集中处理因 Ideology meme `preventApparelRequirements` 导致 PawnKindDef 服装标签未生效时的补穿逻辑。

已迁移重复调用点：

- `IncidentWorker_MooGirl_WildManWandersIn`
- `QuestNode_Root_MooGirl_WandererJoin_WalkIn`
- `QuestNode_Root_MooGirl_RefugeePodCrash`
- `QuestNode_Root_MooGirl_OpeningPodCrash`
- `StockGenerator_MooGirl_Slaves`

保留原行为：

- 仍检查 `pawn.Ideo.memes[*].preventApparelRequirements`。
- 仍按 `pawn.kindDef.apparelTags` 逐 tag 随机挑选。
- 已按用户确认的新设计改为 R18 常驻；服装候选调用点不再执行成人内容过滤。
- 仍按 `MadeFromStuff` 随机材料。
- 仍只锁定 `AdvancedSlaveApparel` / `BrainWashSlaveApparel`。

重要设计说明：R18 常驻后，候选服装列表不再需要因成人内容开关变更而失效。后续若引入缓存，只需要围绕 Def 加载、可选 mod 条件和 PawnKindDef apparel tag 变化设计生命周期。

### Feature Registry

新增 `Core/MooGirlFeatureRegistry.cs`，为后续模块迁移建立明确的模块目录。

当前只记录模块名和责任边界，不参与玩法判断，不改变任何运行行为。后续模块重构时以该 registry 作为文档、诊断和 patch 分组的共同来源。

### Patch Registry

新增 `Core/MooGirlPatchRegistry.cs`，集中管理手动 Harmony patch。

本阶段迁移的手动 patch：

- `PawnGenerator.GeneratePawn` 奴隶服装锁定 postfix
- `AlienRace.AlienPawnRenderNode_Swaddle.GraphicFor` swaddle prefix

保留原行为：

- `[HarmonyPatch]` 标注的 patch 仍由 `Harmony.PatchAll()` 注册。
- 手动 patch 的目标方法、prefix/postfix 方法和逻辑不变。
- HAR swaddle patch 仍使用运行时反射，不硬引用可选类型。
- 手动 patch 统一使用 `MooGirlBootstrap.Harmony`，不再创建独立 Harmony ID。

### Tick Utility

新增 `Core/MooGirlTickUtility.cs`，将“累计 tick，达到阈值后消费累计值”的模式抽成公共工具。

已迁移：

- `Comp_MooGirlMount.CompTickInterval`

保留原行为：

- 生理 tick、安全检查 tick、炮塔 tick、近战辅助 tick 仍先无条件累计。
- `tickPhysiology` 与 `autoDismount` 开关仍只控制是否消费计数器。
- 达到阈值时仍把完整累计 tick 传给对应逻辑，然后清零。

## Validation

已执行 Debug 重建：

```text
MSBuild 1.6/Source/WRace/MooGirlRace.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU
```

结果：通过，输出 `1.6/Assemblies/MooGirlRace.dll`。

已检索确认：

- 乳房 Hediff 字符串不再散落在产奶组件中。
- 牛奶互动 JobDef 查询已集中到 optional Def cache。
- 挤奶 FleckDef 查询已集中到 optional Def cache。
- 重复的服装标签候选扫描只剩 `MooGirlApparelTagUtility` 一处。
- 手动 Harmony patch 调用只剩 `MooGirlPatchRegistry` 一处。
- `Comp_MooGirlMount` tick 计数迁移保持先累计、后消费的旧语义。

## Deferred Items

以下点暂缓到后续阶段，不在本阶段强行处理：

- XML 属性驱动的动态 Def 查询，例如挤奶设备、治愈食品效果、磁力镣铐音效。它们需要实例级缓存设计，不能简单全局缓存。
- `MooGirlNurtureUtility` 已有懒缓存，本阶段不迁移，避免扩大改动面。
- 牵引系统里 body-only 的 MooGirl 判断可能涉及行为范围变化，后续如需统一到 `MooGirlIdentity` 必须先确认。
- 旧文件中的过量逐行注释会在模块级重写时统一整理；新增核心工具先保证解释关键设计原因。
