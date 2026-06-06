# Stage 3 Milk Module Refactor

## Scope

本阶段开始迁移 Milk 模块。当前完成的是第一批行为等价拆分：集中互动效果、产物生成、状态重置和固定数值说明。没有修改产奶数值、喝奶数值、挤奶数值、触发条件或 UI 阈值。

遵守的锁定规则：

- 不改 Def 数值。
- 不改 C# 数值含义。
- 不改变手动挤奶、榨乳器、喝奶、喂奶、儿童找奶喝、婴儿哺乳的玩家可见行为。
- 发现设计缺陷先记录，涉及行为变化时等待确认。

## Completed Changes

### Milk Interaction Utility

新增 `Features/Milk/MooGirlMilkInteractionUtility.cs`，集中以下任务效果：

- 成人右键喝奶：`JobDriver_DrinkMilkFromMooGirl`
- 雪牛娘喂倒地 pawn：`JobDriver_FeedMilkToDowned`
- 儿童找奶喝：`JobDriver_Breastfeed`

数值保持原样：

- 直接喝奶/喂奶饱食度：`0.5f`
- 直接喝奶/喂奶奶量消耗：`0.05f`
- 儿童找奶喝奶量消耗：`0.3f`

保留原 required/optional 语义：

- 成人喝奶、喂倒地 pawn 使用 required Thought/Hediff cache。
- 儿童找奶喝沿用 optional Thought cache。

### Milk Output Utility

新增 `Features/Milk/MooGirlMilkOutputUtility.cs`，集中“按 `stackLimit` 分堆生成并 `ThingPlaceMode.Near` 放置”的产物输出逻辑。

已迁移调用点：

- `CompMooHasBodyResource.Gathered`
- `JobDriver_GatherMilk.CompleteGather`
- `Comp_MilkingDevice.SpawnReleasedMilk`

保持原行为：

- 产出数量由原调用点计算。
- 每堆数量仍使用 `Mathf.Clamp(amount, 1, thingDef.stackLimit)`。
- 仍在原位置附近放置。

### Milk Effect Utility

新增 `Features/Milk/MooGirlMilkEffectUtility.cs`，将喷乳反馈从 `CompMooMilkable` 中拆出。

保持原行为：

- 污物数量仍为 `Rand.RangeInclusive(1, 3)`。
- 污物位置仍沿 pawn 朝向偏移，并保留原随机偏移范围。
- 中文显示仍为 `喷乳！`，文本来源改为 `MooGirl.Milk.SprayText` 翻译键；颜色仍为 `Color.cyan`，持续时间仍为 `3f`。
- 音效仍使用 optional `MooGirl_Milking_Sound`。

### Food Effect Utility

新增 `Features/Milk/MooGirlFoodEffectUtility.cs`，集中奶制品/思想 hediff 的添加、刷新和移除逻辑。

已迁移调用点：

- `IngestionOutcomeDoer_RemoveHediffs`
- `IngestionOutcomeDoer_AddOrRefreshHediff`
- `Thought_Hediff.AddOrRefreshDefHediff`

保持原行为：

- `removeHediffs` 仍按 XML 字符串列表逐项处理。
- 缺失 HediffDef 仍静默跳过。
- 添加或刷新 hediff 后仍重置 `HediffComp_Disappears`。
- 若目标 hediff 带 `HediffComp_CureFoodEffects`，仍调用 `ReapplyCure()`。

性能改进：

- `removeHediffs` 的字符串 Def 查询增加静态缓存，包含缺失 Def 的 null 缓存。Def 在加载后稳定，因此不改变运行结果，只避免 tick 路径重复 `DefDatabase` 查询。

### Thought Hediff Cleanup

`MooGirlMilked 挤奶相关的编程/Thought_Hediff.cs` 完成一轮行级整理：

- 去掉旧式 `flag` 临时变量和低价值逐行注释。
- 去掉 `System.Linq`，改用索引读取第一个匹配身体部位，避免思想计算路径产生 LINQ 枚举。
- 保留 `added` Scribe 字段名和默认值。
- 保留旧行为顺序：扩展 hediff 添加后，仍对 pawn 身上该 def 的第一个 hediff 增加 severity，而不是假设刚添加的实例。

### Localization And Comment Rule Update

已按新增规则完成第一批处理：

- 新增 `docs/06-localization-and-comments.md`，明确 C# 玩家可见字符串使用 Keyed；源 Def XML 使用英文原文、中文注释和 DefInjected；中文翻译文件注释使用英文原文。
- 将近期新增的英文源码注释改为中文。
- 将设置窗口、Milk 量杯、FloatMenu、DevMode 填满奶量消息、喷乳文字、榨乳器 gizmo/消息/检查文本迁移为翻译键。
- 在 `ChineseSimplified/Keyed/Misc_Gameplay.xml` 与 `English/Keyed/Misc_Gameplay.xml` 补齐对应键。
- 将 Milk 相关 Def 的 `label`、`description`、`jobString`、`reportString`、`verb`、`gerund` 迁移到 DefInjected，并补齐中英文 DefInjected 文件。后续需按最新规则把源 Def 占位改为英文原文加中文注释。
- 已迁移范围包括：奶、奶制食物、奶制食物配方、奶制品 Thought/Hediff、喝奶/喂奶 Thought、哺乳 Hediff/Trait、奶发电机、挤奶 WorkGiver/JobDef、榨乳器服装本体。

本批迁移保持原中文显示含义；英文翻译作为语言文件补充，不改变数值或玩法逻辑。`CompMooMilkable.displayString` 改为 `MooGirl.Milk.FullnessDisplay`，并将存档键独立为 `saveKey = milkFullness`；这会放弃旧存档字段兼容，但符合“不保证旧档兼容”的重构前提，且避免翻译文本影响存档结构。

### Milkable Component Cleanup

`CompMooMilkable` 做了行为等价整理：

- 将产奶激活条件集中到 `CanProduceMilk`。
- 将自动添加 `MooGirl_Lactation` 的兜底逻辑集中到 `EnsureLactationHediff`。
- 保留原每 tick 检查时机；改为事件驱动或低频检查仍属于需要确认的行为变化。
- R18 常驻后，榨乳器强力释放 gizmo 不再经过成人内容开关判断，始终按内容存在显示。

### Milking Device Cleanup

榨乳器完成以下结构整理：

- `CompProperties_MilkingDevice.cs` 只保留 XML 配置；运行组件拆分到 `Comp_MilkingDevice.cs`。
- `Comp_MilkingDevice` 对 `releaseThingDefName` 与 `filthDefName` 使用实例级 Def 缓存。缓存按字符串字段自动失效，避免释放路径重复查 `DefDatabase`。
- 保留 `storedCharges`、`storedMilkAmount`、`autoReleaseEnabled`、`powerReleaseEnabled` Scribe 字段名和默认值。
- 保留释放数量、储乳层数、污物数量、音效、强力释放眩晕 tick 等所有数值。
- 删除未编译、重复定义 `MooGirl_DefOf` 的 `MooGirlMilked 挤奶相关的编程/MooMilk_Defof.cs`。

### Nurture Cleanup

哺育逻辑完成以下行为等价整理：

- `MooGirlNurtureUtility` 保留哺育业务入口。
- `HediffComp_MooGirlNurtureProgress`、`Harmony_MooGirlNurtureGrowthPoints`、`Harmony_MooGirlNurturedSkillLearnCap` 拆分为独立文件。
- 完成哺育时提升技能热情的逻辑移除 LINQ 排序，改为单次遍历选择前两个候选技能。
- 保留旧排序语义：等级更高优先，总经验更高次之，同分保持原技能列表顺序。
- 保留所有哺育数值：`0.07f`、`0.10f`、`0.04f`、`27000000`、成长倍率和学习上限处理倍率。

### Release Effect Cleanup

榨乳器释放特效调度移除 LINQ：

- `InitializeSchedules()` 改为显式 for 循环构建声音、fleck、文字调度列表。
- `PostExposeData()` 中存档列表空值恢复逻辑展开为清晰分支。
- 保留 `durationTicks`、`startTick`、`loopInterval`、`endTick`、fleck 速度和缩放等全部数值。

### Resource State Reset

`CompMooHasBodyResource` 新增私有 `ResetManualChangeTracking()`，集中以下字段重置：

- `fullNotified = false`
- `lastFullNotifyTick = -99999`
- `lastResourceUpdateTick = Find.TickManager.TicksGame`

已迁移调用点：

- `Gathered`
- `TryConsumeFullness`
- `DevFillToFull`
- `ConsumePercentage`
- `ConsumeGatheredFullness`

保持原行为：只封装旧语句，不改变 fullness、tick 或满乳通知触发时机。

## Validation

已执行 Debug 重建：

```text
MSBuild 1.6/Source/WRace/MooGirlRace.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU
```

结果：通过，输出 `1.6/Assemblies/MooGirlRace.dll`。

已检索确认：

- 三个直接互动 JobDriver 不再重复写饱食、心情、疗愈和奶量消耗逻辑。
- 分堆生成奶制品逻辑集中到 `MooGirlMilkOutputUtility`。
- 喷乳特效逻辑集中到 `MooGirlMilkEffectUtility`。
- 污物生成仍留在特效相关代码中，未混入产物输出工具。
- `0.5f`、`0.05f`、`0.3f` 互动数值集中到 `MooGirlMilkInteractionUtility` 并标注为 value-lock 常量。
- R18 常驻后，Milk 模块已无 `AdultContentUtility` 运行时门禁依赖。
- 奶制品 hediff 添加/刷新/移除逻辑集中到 `MooGirlFoodEffectUtility`。
- 删除未编译的根目录旧副本 `1.6/Source/WRace/Thought_Hediff.cs`；保留并继续迁移 csproj 中实际编译的 Milk 目录版本。
- 第一批 Milk/UI 硬编码文本已迁移到翻译键或 DefInjected；后续按最新规则清理源 Def 中的翻译键占位。
- Milk 模块 C# 已移除 LINQ 使用。
- 榨乳器组件和哺育组件拆分后 Debug 重建通过。

## Deferred / Requires Confirmation

以下设计问题已发现，但本阶段不擅自修正：

- `CompMooHasBodyResource` 会根据乳房 Hediff 更新 `BreastSize` / `BreastSizeDays`，但当前若干产出路径仍直接按 `fullness * 100f` 计算产量。修正可能改变实际产出，需要确认。
- `CompMooMilkable.CompTick()` 自动添加 `MooGirl_Lactation` hediff。若改为事件驱动，会改变添加时机，需要确认。
- `Comp_MilkingDevice` 暴露了 `powerFilthDefName` 与 `powerFilthCountRange`，但当前释放流程未实际使用强力释放专用污物。启用它会改变地图污物结果，需要确认。
- `CompProperties_MilkingDeviceReleaseEffect.TextWithParams.text` 当前直接作为文字显示。如果后续 XML 使用该字段，源 XML 应写英文原文并配中文注释，语言文件通过 DefInjected 注入实际翻译，C# 侧不得把 XML 文本当作 Keyed 强制翻译。
