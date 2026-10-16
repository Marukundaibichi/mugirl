# 模块审计：Core / DefOf / Compatibility / Platform / Mathematics / Rendering / UI / GlobalUsings / csproj

- 范围：`Source/MugirlRace/{Core, DefOf, Compatibility/**, Platform/**, Mathematics, Rendering/**, UI}`（72 个 .cs，约 6.9k 行）、`GlobalUsings.cs`、`MugirlRace.csproj`，以及 `MugirlMod`/`MugirlSettings` 的 Mod 设置文件契约。
- 对照：冻结的 `1.6/Source/{Core,DefOf,UI,Compatibility}` 工作区快照与 git 历史（`git log -- 1.6/Source`）。
- 引擎事实：DecompilerServer，上下文 `rw17-game`（`RimWorld/Assembly-CSharp.dll`，1.7）。注意：这台 DecompilerServer 由多个会话共用，当前上下文会被别的会话切换（审计中途被切到 `vef17`），调用时要显式传 `contextAlias`。
- 行号：除非写出完整路径，都相对于 `Source/MugirlRace/`，并且都是文件内行号。

## 0. 结论速览

1. 本范围内**没有任何类型会以 `Class="…"` 写进存档**，也没有组件、Hediff、JobDriver 等子类。唯一的 `ExposeData` 是 `MugirlSettings`，写进的是 Mod 设置文件，不是存档。
2. 存档相关的旧版兼容只有 3 处：两个 XML 读档迁移补丁（A1、A2），以及一个退役的 IncidentWorker 加退役 IncidentDef（A3）。三处在统一升级层里都属于 (e) 结构性 XML 变换，没有一处需要类型改名映射。
3. DefOf 类绑定了约 150 个 defName。其中大部分是存档可见的 Def 引用（JobDef、HediffDef、ThoughtDef、PawnKindDef 等），defName 改名都要进 (b) 映射表。另有 47 个 DefOf 字段在 C# 中无人使用，1.6 中同样无人使用。
4. 设置文件是独立的一条线：文件名只由 `MugirlMod` 的**短类名**决定，命名空间可以移动；`MugirlSettings` 改全名时，原版会回退到声明类型，值照常读出，只多一条错误日志。改标签会让旧值静默丢失。
5. 第 D 节记录的引擎事实决定了升级层的形状：Mugirl 的类名对象大多在 `game/components` 之前就已反序列化；存档 meta 不含 Mod 版本；原版 BackCompatibility 在“同一 build + 同一 Mod 列表”时整体短路；Scribe 字典是 keys/values 两个并列列表；Scribe_Values 不写等于默认值的节点。

---

## A. 旧版/向后兼容代码

“需要什么”一列沿用任务定义：(a) 类型改名，(b) defName 改名，(c) 有类型归属的 Scribe 标签改名，(d) 枚举值改名，(e) 存档 XML 结构变换，(f) 需要活游戏对象的读档后语义步骤，(g) 不需要，可直接删除。

### A-1 与存档直接相关

| # | 位置 | 处理的旧形状 | 版本来历 | 新规范形状 | 统一升级层需要 |
|---|---|---|---|---|---|
| A1 | `Compatibility/Savegames/CorporateValidationSaveMigration.cs:6-53`（`[HarmonyPatch(typeof(Game),"ExposeSmallComponents")]` :8；Prefix :15-21；`RemoveLegacyComponents` :24-52；类名常量 :12-13） | `game/components/li[@Class="Mugirl.CorporateRuntimeValidation"]` 与 `…"Mugirl.CorporateVisualValidation"`：验证驱动被误注册成 GameComponent 后写进存档 | 驱动源码首见于 0.6.0 `e93677d`（`docs/tools/Corporate*Validation.cs`），只在 `EnableCorporateValidation=true` 时编入，见 `e93677d:1.6/Source/MugirlRace.csproj:307-317`。迁移补丁在 0.6.1 `0dbb4e2` 加入（`git log -S`），1.7 端逐字移植；单测见 `Source/Tests/MugirlRace.Tests/Compatibility/CorporateValidationSaveMigrationTests.cs` | 存档中没有这两个组件 | **(e)**：通用规则“从 `game/components` 删除 Class 属于已移除类型集合的 `li`”。这类节点只出现在用验证构建玩过的存档里；缺类时原版只记错误并丢弃该组件，所以也可以算 (g)。建议放进升级层的 removed-types 表（成本几乎为零），然后删除本类、单测和 `MugirlPatch("SaveCompatibility",…)`。 |
| A2 | `Compatibility/Savegames/NeuralShoulderSaveMigration.cs:6-213`（补丁 :7-8；常量 :11-12；Prefix :14-20；`RemoveLegacyShoulders` :36-100；引用清理 :86-97；`IsThing` :171-174；`IsReference` :179-185） | ThingDef `Mugirl_NeuralCombatShoulders` 与配方 `Make_Mugirl_NeuralCombatShoulders`。处理三类节点：① 带 `id` 的 `li`/`thing` 物品节点（含嵌套）；② `li[recipe=…]` 订单；③ 叶子引用 `<id>`、`Thing_<id>`、`Bill_<recipe>_<loadID>`，以及 `li`/`def`/`thingDef`/`recipe` 中的 defName。处理方式：`li` 删除，其余改写为 `null` | 该 Def 只存在于 0.6.3 `a172c15`（“a测”，`1.6/Defs/Apparel/Apparel_0920.xml:114`、`Mugirl_Race.xml:666`）。0.6.4 `37545af` 同日删除 Def 并加入迁移。1.7 改成单次 DFS，结果与 1.6 的三次 XPath 逐字节相同（:25-34 注释；`NeuralShoulderSaveMigrationTests.cs`） | 没有该物品与配方 | **(e)**：通用的“退役 ThingDef/RecipeDef 清除”规则，包括删实例、删订单、清引用。需要遍历整棵 `game` 树，但只应在未盖章的存档上执行一次。现在的实现每次读档都遍历整棵树（:33-34 注释自述）。**设计缺陷，需在新规则中修正**：③ 对 `li` 叶子一律删除，`li` 若位于 Scribe 字典的 `keys` 或 `values` 下，会让键值错位（D3）。新规则必须成对删除。另外 Ideology 等字段（如 `apparelDef`）不在 `li/def/thingDef/recipe` 白名单内，仍会报 “Could not load reference”，属于低概率缺口。 |
| A3 | `Compatibility/Savegames/IncidentWorker_RetiredMugirlEvent.cs:3-9`；`1.7/Defs/Compatibility/RetiredEvents.xml:3-13`（`Mugirl_WildManWandersIn`，baseChance 0，hidden）；`1.7/Languages/ChineseSimplified/DefInjected/IncidentDef/Incidents_Mugirl.xml:8`；`docs/content-update-guide.md:79` | 存档中对 IncidentDef `Mugirl_WildManWandersIn` 的引用。已核实的位置：每个 StoryState（世界、地图、远行队）的 `recentRandomIncidents`（List，LookMode.Def）和 `lastFireTicks`（`Dictionary<IncidentDef,int>`），见 `RimWorld.StoryState.ExposeData` 反编译。storyteller 事件队列的 `FiringIncident.def` 也会引用 Def | 0.4.7 `324501c` 由 `MooGirl_WildManWandersIn` 改名而来。0.4.7 至 HEAD `50f7979` 是活跃事件，worker 为 `Mugirl.IncidentWorker_Mugirl_WildManWandersIn`（`HEAD:1.6/Defs/Storyteller/Incidents_Mugirl.xml:4-11`）。退役发生在最后的 1.6 工作区，未提交：`git status` 显示 `?? 1.6/Defs/Compatibility/`、`?? 1.6/Source/Compatibility/Savegames/IncidentWorker_RetiredMugirlEvent.cs`。1.7 原样移植 | 没有该 Def | **(e)**：删除 `recentRandomIncidents/li[.='Mugirl_WildManWandersIn']`；`lastFireTicks` 中删除同一索引的 `keys/li` 与 `values/li`；删除 `def` 为该名的事件队列项。不能只对 `BackCompatibility.WasDefRemoved` 加 Postfix：它只压掉 “Could not load reference”，字典仍会报 “Null key while loading dictionary”（D3）。完成后删除 worker 类、`RetiredEvents.xml`、该 DefInjected 行和 content-update-guide 中的对应段落。 |

> 版本覆盖面：仓库中找不到任何 `MooGirl` 名称的处理代码（`grep -rn MooGirl` 在 1.6/1.7 源码与 Defs 中均无结果）。0.4.7 之前的存档从未被支持过，统一升级层的下限可以定为 0.4.7。

### A-2 Mod 设置文件契约（不是存档，按存档标签对待）

| # | 位置 | 内容 | 版本来历 | 建议 |
|---|---|---|---|---|
| A4 | `Core/MugirlMod.cs:5`（注释“类名决定设置文件名（Mod_<文件夹>_MugirlMod.xml），不能改名或改命名空间”）、:16 `GetSettings<MugirlSettings>()` | 设置文件路径 `Config/Mod_<FolderName>_MugirlMod.xml` | 0.4.7 `324501c` 由 `MooGirlMod` 改名，当时没有迁移，玩家设置已经被重置过一次 | 注释只说对一半。`Verse.Mod.GetSettings` 调用 `LoadedModManager.ReadModSettings<T>(intContent.FolderName, GetType().Name)`，`GetSettingsFilename` 为 `"Mod_{modIdentifier}_{modHandleName}.xml"`（均已反编译），只用到短类名，**命名空间可以自由移动**。改短类名会让全部设置回到默认；确要改名，需要在构造函数中、`GetSettings` 之前读入旧文件，这是设置层迁移。 |
| A5 | `Core/MugirlSettings.cs:3`（注释“类名与 Scribe 标签都是设置文件的一部分，不能改名”）、:28-48 | `<SettingsBlock><ModSettings Class="Mugirl.MugirlSettings">` 加 10 个标签（B2） | 0.4.7 起标签只增不删（`git log -S` 逐键核对，见 B2） | **改全名**：`ScribeExtractor.SaveableFromNode` 找不到 Class 时，`GetBestFallbackType<T>` 对非 Thing/Hediff/Ability/Thought 返回 `typeof(T)`，即 `MugirlSettings`。值照常读出，只多一条 “Could not find class Mugirl.MugirlSettings …” 错误；下次写盘就会写入新 Class。**改标签**：Scribe_Values 读不到节点就取默认值，旧值静默丢失，需要读一次旧标签做回退。建议加 `settingsVersion` 字段，与存档升级层分开。 |
| A6 | `Core/HarmonyBootstrap.cs:28-29` `HarmonyId = "MugirlMod.Mod"`（“与 1.6 相同，其他 Mod 的 HarmonyBefore/HarmonyAfter 依赖它”）；`Source/Tests/MugirlRace.GameValidation/Core/MugirlTarget.cs:14-15` | 对外的 Harmony 互操作 id | 0.4.7 起 | **(g)**：与存档无关，升级层不涉及。仓库内无法核实是否有第三方依赖这个 id，建议保留。若改，应改为 packageId `HAR.MugirlRace`。 |

### A-3 不涉及数据的“与旧版行为一致”代码

| # | 位置 | 内容 | 需要 |
|---|---|---|---|
| A7 | `Core/MugirlIdentity.cs:28-33` `IsMugirlPawn = IsMugirlDef ‖ HasMugirlBody`（注释“将历史种族判定集中在一处…兼容路径则可能通过同一个 body 识别目标”） | 1.7 内容中只有 `Mugirl` 种族使用 `MugirlBody`（`1.7/Defs/ThingDefs_Races/Mugirl_Race.xml:901`）；Punisher 使用 `QuadrupedAnimalWithHooves`（`Mugirl_Punisher.xml:39`） | (g)。不涉及存档；应统一成一个语义（C3-6）。 |
| A8 | `Core/MugirlTickUtility.cs:5-7`（`MaxPresentationTickRate=150`，“钳制到 1.6 的上限”）；:53-71（`Add`、`ConsumeReady`，“保持旧组件计数契约”） | 表现层速度上限；旧的累计计数语义，唯一调用方是 `Features/Mounting/Comp/Comp_MugirlMount.Ticking.cs:29-67` | (g)。若这些计数器会存档，归 Mounting 审计，保持语义即可。 |
| A9 | `Mathematics/UnityMathf.cs:120-136`（Mono 的 float→int 规则）；`Compatibility/YayoCombat/YayoCombatCompatibility.cs:105-121`（保留 1.6 的 `RoundToInt`）；`Compatibility/AlienRace/Harmony_StylingStationRefresh.cs:19, 102-104`；`Rendering/PawnEffects/ExtraOutlineGeometry.cs:4,9,18`、`LanceMotionBlurGeometry.cs:4,9,12`（“逐项照搬 1.6”） | 与 1.6 逐位一致的行为复刻 | (g)。与存档无关，是否继续逐位一致属于行为决策。 |
| A10 | `DefOf/Mugirl_DefOf.cs:52`、`DefOf/MugirlMilkDefOf.cs:3-5`、`Core/HarmonyBootstrap.cs:8`、`Core/MugirlPatchAttribute.cs:13`、`Compatibility/ModCompat.cs:7-9`、`Compatibility/CompatAccessors.cs:12,105`、`Compatibility/DualWield/DualWieldCompatibility.cs:133`、`TrainingFacilityCompatibility.cs:11` 等 | “代替 1.6 的…”“与 1.6 相同…”这类历史说明注释 | (g)。改写成描述现状的注释即可。 |
| A11 | `DefOf/MugirlContentDefOf.cs:136`（“不改 defName 以保护数值与掉落配置”） | 内容命名冻结 | (g)；若改名则属 (b)。 |

### A-4 第三方旧版垫片（不属于 Mugirl 自身旧版，与存档无关）

| 位置 | 内容 | 建议 |
|---|---|---|
| `Compatibility/ModCompat.cs:17, 61` `RunAndGunLegacyPackageId = "roolo.RunAndGun.kotobike"` | 同时识别旧分支和续作 | (g)。这是第三方支持范围的决策；若 kotobike 没有 1.7 用户，可以删除。 |
| `Compatibility/RunAndGun/RunAndGunCompatibility.cs:10-14, 70-76` | `isEnabled` 字段与属性的双路径读取 | 同上，旧分支删除后只留属性路径。 |
| `Compatibility/MeleeAnimation/MeleeAnimationCompat.cs:10`、`Compatibility/YayoCombat/YayoCombatCompatibility.cs:10-13`、`Compatibility/TrainingFacility/TrainingFacilityDefOf.cs:6` | 成员按对方的 1.6 DLL 或 1.6 目录核对 | 应按对方的 1.7 构建重新核对；与存档无关。 |
| `Compatibility/CorporatePricing/CorporatePricingCompatibility.cs:86-88` | 1.7 经版本回退加载其他 Mod 的 1.6 程序集时，反射会抛异常 | 运行期健壮性，保留。 |

### A-5 执行冻结规则的基础设施（规则改变后必须一起改）

- `Source/Tests/MugirlRace.Validation/Checks/SaveContractChecks.cs:8-40`：`C.save-types`、`C.save-labels`、`C.save-enums` 与冻结的 1.6 DLL 逐名比较。`SaveVisibleRoots` 在 :16-23。
- `Checks/FrozenContentChecks.cs`。
- `Baselines/save-types.txt`：:12-13 是 `Mugirl.MugirlMod` 与 `Mugirl.MugirlSettings`。另有 `dropped-save-types.txt`、`dropped-scribe-labels.txt`、`dropped-defs.txt`、`save-enums.txt`、`scribe-defaults.txt`。
- `Baselines/architecture-rules.json`：`mugirl-identity` 规则引用 `Mugirl.Mugirl_DefOf::MugirlBody` 与 `Mugirl.Core.MugirlIdentity`；`external-breast-hediffs` 规则引用 `Mugirl.ExternalBreastHediffDefs`。类型改名后这些条目要同步。
- `docs/architecture/ADR-004-root-namespace-markers.md`；根 `README.md` 的“XML 类名、Scribe 字段、根命名空间及补丁 XPath 不能随意改名”。
- 建议：把 `C.save-*` 从“1.6 的名字必须仍然存在”改为“每个冻结名字要么仍然存在，要么出现在升级层的改名表或删除表中”。这样升级映射成为唯一数据源，与冻结 1.6 DLL 的比较依然有用，还能自动发现漏写映射的改名。

---

## B. 存档可见面

### B1 以 `Class=` 写入的类型

- **存档**：本范围内没有。72 个文件中没有任何类继承自 `SaveVisibleRoots`（`SaveContractChecks.cs:16-23`）列出的基类，也没有以基类声明的 Deep IExposable。`IncidentWorker_RetiredMugirlEvent` 由 Def 实例化，不进存档。
- **设置文件**：`Mugirl.MugirlSettings` 会写 Class 属性。`LoadedModManager.WriteModSettings` 以 `ModSettings settings` 声明调用 `Scribe_Deep.Look(ref settings, "ModSettings")`，而 `Scribe_Deep.Look` 在 `target.GetType() != typeof(T)` 时写 `Class`（两者均已反编译）。`Mugirl.MugirlMod` 只以短类名出现在设置文件名中。
- **XML 可见的类名**：只有 `Mugirl.IncidentWorker_RetiredMugirlEvent`（`1.7/Defs/Compatibility/RetiredEvents.xml:9`）。DefOf 类通过 `[DefOf]` 特性发现，不被 XML 引用。
- **内联保存者**：本范围内没有 Comp、Properties 或 Worker 的存档字段。

### B2 ExposeData 清单：只有 `MugirlSettings`（`Core/MugirlSettings.cs:28-48`）

节点路径为 `SettingsBlock/ModSettings[@Class="Mugirl.MugirlSettings"]/<标签>`。`Scribe_Values.Look` 在保存时不写等于默认值的节点（已反编译，见 D5），所以多数设置文件只有少数几个节点。

| 标签 | 方式 | 类型 | 默认值 | 行 | 1.6 位置 | 首次出现 | 实际含义与使用处 |
|---|---|---|---|---|---|---|---|
| `enableStructuralCrashEvent` | Scribe_Values | bool | true | :32 | `1.6/Source/Core/MugirlMod.cs:61` | 0.4.7 之前（`MooGirlMod` 时代） | **名不副实**：实际控制“开局坠舱剧情线”（含随后的快递剧情）。关闭后，巨企代表改为独立来访：`Features/Incidents/Story/MugirlStoryService.cs:52`、`Features/Corporate/Introduction/CorporateIntroduction.cs:233`。界面文字为 “Enable opening crash storyline”（`1.7/Languages/English/Keyed/Misc_Gameplay.xml:63-64`） |
| `enableMugirlMigrationEvent` | 同上 | bool | true | :33 | :62 | 0.4.8 后 `03439c8` | `IncidentWorker_Mugirl_Migration.cs:17` |
| `enableMugirlFusionInvestmentEvent` | 同上 | bool | true | :34 | :63 | `03439c8` | `IncidentWorker_Mugirl_FusionInvestment.cs:8` |
| `enableRunawayMugirlFarmQuest` | 同上 | bool | true | :35 | :64 | `03439c8` | `IncidentWorker_Mugirl_RunawayFarmQuest.cs:8` |
| `enableFastMilking` | 同上 | bool | false | :36 | :65 | 0.4.7 之前 | `JobDriver_GatherMilk.cs:16` |
| `enableVanillaMilkGauge` | 同上 | bool | false | :37 | :66 | `0ffedc2` | `Gizmo_MilkGauge.cs:36` |
| `enableExtraOutline` | 同上 | bool | false | :38 | :67 | 0.5.2 `38b7ec6` | `MugirlExtraOutline.cs:36` |
| `extraOutlineWidth` | 同上 | float | `MugirlExtraOutline.DefaultWidth`（1.0） | :39，读后在 :40-45 做 NaN/∞ 修正与钳制 | :68-71 | 0.5.2 | `MugirlExtraOutline.cs:65` |
| `enableWeaponWheelDevLog` | 同上 | bool | false | :46 | :72 | 0.5.0 `46d2190` | `WeaponWheelDevLog.cs:27` |
| `hideWeaponWheelBackWeapons` | 同上 | bool | false | :47 | :73 | `50f7979` | `PawnRenderNodeWorker_BackWeapon.cs:16` |

1.7 相对 1.6 的变化：标签和默认值完全相同。类从 `MugirlMod.cs` 拆到独立文件。1.7 先读设置再打 Harmony 补丁（`Core/MugirlMod.cs:14-18`），1.6 顺序相反（`1.6/Source/Core/MugirlMod.cs:13-15`）。1.7 新增了 `WriteSettings` 重写（`Core/MugirlMod.cs:43-48`），只做表现层刷新，不影响数据。

### B3 枚举、Def 与引用

- 按名保存的枚举：本范围内没有。`MugirlPatchRisk`（`Core/MugirlPatchAttribute.cs:4-10`）只是元数据。
- `Scribe_Defs` 与 `Scribe_References`：本范围内没有。

### B4 本范围 DefOf 绑定的 defName 及其存档可见性

defName 本身属于内容，但升级层的 (b) 改名表必须覆盖它们。标记说明：`*` 表示 C# 中没有任何使用（1.6 中同样没有）；`?` 表示 `[MayRequire("Ludeon.RimWorld.Biotech")]`；“原版”表示不由本 Mod 定义。

| Def 类型 | 写入存档？ | defName（文件:行） |
|---|---|---|
| ThingDef | 是：Thing 的 `def`、stuff、`ThingFilter` 列表、种族 | `Mugirl_DefOf.cs`：`WallRopeHitch`:8、`Mugirl`:10、`Mugirl_Milk`:12、`Mugirl_AutoloadingSystem`*:55、`Mugirl_NeuralCombatArmor`:59、`Mugirl_NeuralAmplifierHelmet`:61、`Mugirl_LanceChargeFlyer`:69、`Mugirl_ThrowController`?:87、`Mugirl_DunkProp`?:99、`Mugirl_DunkHead`?:102、`Mugirl_DunkFlyer`?:105。`MugirlContentDefOf.cs`：服装 :8-16（`Combatant_*`* :8-11、`Mugirl_SpeciallyStockings`*:12、`MilitaryDress`*:13、`MilitaryUniform`*:14、`Bikini`:15、`Bikini_Stocking`*:16），展览套装 :19-45，`Mugirl_ShockCollar`:48，`PMC_*`* :51-53，奶制食品 :56-64，`Mugirl_CourierDiary`:67，`Mugirl_PunisherRace`:102，钥匙 :137-138。`MugirlMilkDefOf.cs`：`MugirlMilkStain`:19 |
| JobDef | 是：`curJob`、`jobQueue` 中的 `def` | `Mugirl_DefOf.cs`：`Job_GatherMilk`:18、`Job_MugirlMilkingTargetWait`:20、`Job_MugirlMilkInteractionWait`:22、`JobDriver_RopeMoo`:36、`JobDriver_RemoveRopeMoo`:38、`RopeToBuild`:40、`Job_Nuzzle`:42、`Job_FollowRoper`:44、`Job_MountMugirl`:46、`Job_DismountMugirl`:48、`Job_LoadWeaponWheel`:50、`ForceUnlockSlaveApparel`:53、`Job_MugirlLancePointCharge`:63、`Job_MugirlLanceLineCharge`:65、`Job_MugirlDunk`?*:96。`MugirlContentDefOf.cs`：`Mugirl_CorporateSupportTend`:87、`Mugirl_ReadCourierDiary`:108、`Mugirl_TalkCourier`:109、`Mugirl_TalkFusionInvestor`:110。`MugirlMilkDefOf.cs`：`Job_DrinkMilkFromMugirl`:23、`Job_Breastfeed`:25、`Job_FeedMilkToDowned`:27。`TrainingFacilityDefOf.cs`：第三方的 `UseShootingRange*`:11-17 |
| HediffDef | 是 | `Mugirl_LanceChargeSpeed`*:67、`Mugirl_Armbinder`*:71、`Mugirl_Abasia`:73、`Mugirl_LinkedSwordDance`:75、`Mugirl_Stun`?:78、`Mugirl_Charge`?:81（以上 Mugirl_DefOf）；`Mugirl_RunawayPanic`:101、`Mugirl_PunisherKnockout`:104、`Hediff_MugirlAgedCheese/CowCake/MilkPowder/MilkTablet`* :113-116（以上 MugirlContentDefOf）；`Mugirl_Lactation`:9、`Mugirl_MilkHealing`:11（MugirlMilkDefOf） |
| ThoughtDef | 是：记忆 | `Mugirl_Nuzzled`（Mugirl_DefOf:132）；`Consumed_Mugirl{Pudding,Cheese,AgedCheese,RichMilkFlavor,MilkCandy}`* :119-123；`Consumed_MugirlMilk`:13、`Mugirl_DrankMilk`:15、`Mugirl_FedMilk`:17（MugirlMilkDefOf） |
| PawnKindDef | 是：`kindDef` | `Mugirl_Slave`:116、`Mugirl_Beginning_Slave`:118、`Mugirl_EscapeSpaceSlave`:120、`Mugirl_EscapeWanderSlave`:122、`Mugirl_EscapeWildSlave`:124、`Mugirl_PreEscapeWildSlave`:126、`Mugirl_WildMugirl`:128；`AI_GC_Grunts`*:73、`AI_GC_Courier`:74、`AI_GC_Soldiers`*:75、`AI_GC_Elites`*:76、`AI_GC_MugirlRaider`*:77、`Mugirl_Corporate{Representative,Support,Showcase}`:78-80、`Mugirl_Punisher`:103 |
| BackstoryDef / TraitDef / HairDef / XenotypeDef / AbilityDef | 是：Pawn 的 story、traits、genes、abilities | `Mugirl_Newborn`*:136、`Mugirl_Colonist`*:138、`Mugirl_ExperimentalChild`:140；`Mugirl_BrainWashObey`:114；`Mugirl_CorporateDiehard`:83；`Mugirl_LongCascade`:14；`Mugirl_Xenotype`?:143；`Mugirl_Throw`?*:84、`Mugirl_Dunk`?*:93 |
| QuestScriptDef / IncidentDef / SitePartDef / FactionDef / RecipeDef / InteractionDef / DutyDef / ResearchProjectDef | 是：quest.root、StoryState（A3）、Site 部件、派系、订单、PlayLog、PawnDuty、研究进度 | `Mugirl_SlaveOpeningPodCrash`:134；`Mugirl_CourierRaid`:90、`Mugirl_RunawayFarmQuest`* :99；IncidentDef `[DefAlias]` 字段 `Mugirl_Migration`/`Mugirl_FusionInvestment`/`Mugirl_RunawayFarmQuest`* :93-98；`Mugirl_RunawayFarm`:100；`Mugirl_GiantCorporations_Hostile`:70；`Mugirl_Make*`* :126-134；`Mugirl_Nuzzle`:130；`Mugirl_CorporateSupportHunt`:86；`Mugirl_Autoloading`:57 |
| 不写入存档（已确认） | 否 | BodyDef `MugirlBody`:16；FleckDef `MugirlWashMentalEyes`*:112、`Mugirl_DunkBloodSplash`?:108、`ShockwaveFast`（原版）:105、`FoamSpray`（原版）:29、`Mugirl_MilkSpray`*:31、`GroundWaterSplash`*（原版）:33；SoundDef `Pawn_Melee_BigBash_HitPawn`（原版）:110、`Mugirl_Milking_Sound`:21；PawnRenderNodeTagDef `Root`（原版）:34；MaterialDef（`Rendering/MugirlRenderDefOf.cs:11-12`） |
| 未核实 | — | AnimationDef `Mugirl_Milking*/MilkInteraction*Animation` :24-32（Pawn 当前动画是否存档未核实）；DamageDef `Mugirl_ThrownImpact`?:90（只可能出现在战斗日志中，未核实） |

注意：IncidentDef 与 QuestScriptDef 同名 `Mugirl_RunawayFarmQuest`（`1.7/Defs/Storyteller/Incidents_Mugirl.xml:55`、`1.7/Defs/QuestScriptDefs/Script_RunawayFarm.xml:4`），因此需要用 `[DefAlias]`（`MugirlContentDefOf.cs:93-98`）。按类型分库时同名合法，但容易误读。

### B5 本范围类型的 1.7 与 1.6 对照（全部不写入存档）

| 1.7 完整名 | 1.6 完整名（文件） | 差异 |
|---|---|---|
| `Mugirl.MugirlMod` | `Mugirl.MugirlMod`（`1.6/Source/Core/MugirlMod.cs:6-31`） | 相同。1.6 带多余的 `[StaticConstructorOnStartup]`，1.7 已去掉 |
| `Mugirl.MugirlSettings` | 同名（同一文件 :33-75） | 相同（设置文件可见） |
| `Mugirl.Core.HarmonyBootstrap` | `Mugirl.MugirlBootstrap` 与 `Mugirl.MugirlPatchRegistry` | 新类名和命名空间；不可见 |
| `Mugirl.Core.MugirlPatchAttribute`、`MugirlPatchRisk` | `Mugirl.MugirlPatchInfo`、`MugirlPatchCatalog`、`MugirlPatchRiskLevel` | 新类型 |
| `Mugirl.Core.{MugirlGameUtility, MugirlIdentity, MugirlLog, MugirlSelectionUtility, MugirlText, MugirlTickUtility}` | `Mugirl.*` | 只改了命名空间 |
| `Mugirl.MugirlMilkDefOf` | `Mugirl.MugirlRequiredDefs`、`Mugirl.MugirlOptionalDefs` | 从运行期 `GetNamedSilentFail` 改为 `[DefOf]`。1.6 的 `MugirlMilkFilth`（`1.6/Source/Core/MugirlOptionalDefs.cs:13`）不再被 C# 引用，XML 仍在使用 |
| `Mugirl.Mugirl_DefOf`、`Mugirl.MugirlContentDefOf` | 同名 | 字段有增减 |
| `Mugirl.Rendering.*`、`Mugirl.Platform.*`、`Mugirl.Mathematics.*`、`Mugirl.Compatibility.{ModCompat, CompatAccessors}`、`Mugirl.Compatibility.TrainingFacility.TrainingFacilityDefOf` | 无 | 1.7 新增 |
| 兼容类与补丁类（`Mugirl.DualWieldCompatibility`、`Harmony_*` 等） | 同名，但在 1.6 中补丁类与兼容类同处一个文件（`1.6/Source/Compatibility/WeaponWheel/*.cs`、`OtherMods/*.cs`） | 1.7 拆分了文件，全名不变 |
| `Mugirl.{CorporateValidationSaveMigration, NeuralShoulderSaveMigration, IncidentWorker_RetiredMugirlEvent}` | 同名（`1.6/Source/Compatibility/Savegames/`） | 1.7 改为非 LINQ 实现，行为相同 |

---

## C. 命名与架构问题

### C0 推断出的约定

以下约定取自本仓库最新、最规范的代码（`Core`、`Platform/*`、`Rendering/PawnEffects`、`Mathematics`）和原版惯例。

1. 文件作用域命名空间等于 `Mugirl.` 加上目录路径（`Mugirl.Platform.Rendering`、`Mugirl.Rendering.PawnEffects`）。**例外**：当命名空间名会遮蔽 Verse 类型或第三方根命名空间时不能这样做（C3-1）。
2. 一个文件只放一个顶层类型，文件名等于类型名。小的 record、参数 struct 或嵌套类型可以随宿主放在同一文件。
3. 原版子类命名沿用原版：`IncidentWorker_X`、`JobDriver_X`、`QuestNode_X`、`HediffComp_X`、`CompProperties_X`。ThingComp 原版用 **`CompX`**（如 `CompRefuelable`），不是 `Comp_X`。DefOf 用 **`<域>DefOf`**，不带下划线。
4. 名字与 Verse/RimWorld 冲突时才用 `Mugirl` 前缀（`MugirlLog` 对 `Verse.Log`、`MugirlText` 对 `Verse.Text`、`MugirlMod`、`MugirlSettings`）；命名空间已经说明归属时不再重复。
5. Harmony 补丁类：全仓库 `^\[HarmonyPatch` 之后的类中，`Harmony_<领域>_<作用>` 占 74 个，`*_Patch`、`Patch_*` 等占约 24 个（Features）。本范围全部是 `Harmony_` 形式，所以以它为准，把少数派统一过来。
6. 兼容类：多数用 `<Mod>Compatibility`，只有 `MeleeAnimationCompat` 例外。反射缓存放在嵌套的 `ReflectionCache` 中，并写 `StaticCacheLifecycle` 注释。

### C1 命名空间与目录不符（本范围共 27 个文件）

| 目录 | 文件 | 当前命名空间 | 原因 |
|---|---|---|---|
| `Compatibility/**` | 21 个文件（除 `ModCompat.cs`、`CompatAccessors.cs`、`TrainingFacility/TrainingFacilityDefOf.cs` 外全部） | `Mugirl` | ADR-004 过去的理由是“XML/Scribe 可见、补丁元数据审计”。本范围内只有 A3 的 worker 是 XML 可见（且将被删除），Harmony 补丁类早已改由特性发现（`HarmonyBootstrap.cs:169-209`），这个理由已不成立 |
| `Core/` | `MugirlMod.cs`、`MugirlSettings.cs` | `Mugirl` | 设置契约（见 A4、A5）：`MugirlMod` 的命名空间可以移动；`MugirlSettings` 移动后只多一次错误日志 |
| `DefOf/` | 3 个文件 | `Mugirl` | 没有必要留在根命名空间：DefOf 通过特性发现。`architecture-rules.json` 中的 `Mugirl.Mugirl_DefOf::MugirlBody` 需要同步修改 |
| `UI/` | `MugirlSettingsWindow.cs` | `Mugirl` | 理由正确（:4）：`Mugirl.UI` 会让所有 `Mugirl.*` 代码中的 `UI.screenWidth` 解析到该命名空间。但这与 ADR-004 列出的首选命名空间 `Mugirl.UI` 自相矛盾，ADR 需要修正 |

### C2 改名建议

表头说明：“存档可见”包括设置文件可见；“XML 可见”指类名被 Defs 或 Patches 引用。

| 类别 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| 设置标签 | `enableStructuralCrashEvent` | `enableOpeningCrashStoryline` | 名不副实（B2）：实际控制开局坠舱剧情线与巨企独立来访，与 “structural” 无关。翻译键 `Mugirl.Settings.StructuralCrashEvent.*` 一并改名 | 设置文件（需一次性回退读取旧标签） | 否 |
| 设置标签（可选，与上一条同批） | `enableMugirlMigrationEvent`、`enableMugirlFusionInvestmentEvent`、`enableRunawayMugirlFarmQuest` | `enableMigrationEvent`、`enableFusionInvestmentEvent`、`enableRunawayFarmQuest` | 去掉冗余的 `Mugirl`，与 `enableFastMilking` 一致。只有在上一条已经需要设置迁移时才值得做 | 设置文件 | 否 |
| 类型 | `Mugirl.MugirlMod` | `Mugirl.Core.MugirlMod` | 与目录一致；短名必须保留（A4） | 只有短名可见 | 否 |
| 类型 | `Mugirl.MugirlSettings` | `Mugirl.Core.MugirlSettings` | 与目录一致；读旧文件时一次错误后自愈，也可在 Mod 构造函数中预处理（A5） | 设置文件 | 否 |
| 类型、目录 | `Mugirl.MugirlSettingsWindow`（`UI/`） | `Mugirl.Core.MugirlSettingsWindow`，放到 `Core/Settings/`，与设置类同目录 | 删除只有一个文件的 `UI` 目录，避开 `Mugirl.UI` 陷阱 | 否 | 否 |
| 类型 | `Mugirl.Mugirl_DefOf` | 按领域拆分：核心种族留 `MugirlDefOf`（`Mugirl`、`MugirlBody`、Backstory、PawnKind 等），其余放到各 Feature 的 `<Feature>DefOf`（`RopingDefOf`、`MountingDefOf`、`LanceDefOf`、`ThrowDefOf`…） | 下划线违反 C0-3；一个 DefOf 类装了 10 个领域、约 60 个字段 | 否 | 否 |
| 类型 | `Mugirl.MugirlContentDefOf` | 拆为 `CorporateDefOf`、`MilkFoodDefOf`、`ApparelDefOf`、`StoryIncidentDefOf` 等，放进对应 Feature | “Content” 没有信息量；36 个字段没有使用（C3-4） | 否 | 否 |
| 类型 | `Mugirl.MugirlMilkDefOf` | `Mugirl.Features.Milk.MilkDefOf` | 放到使用它的 Feature 中 | 否 | 否 |
| 命名空间 | `Mugirl`（Compatibility 下 21 个文件） | `Mugirl.Compatibility.<Mod>`：`.DualWield`、`.RunAndGun`、`.YayoCombat`、`.YayoAnimation`、`.MeleeAnimation`、`.LactationExpansion`、`.ExternalHediffs`；HAR 用 `.HumanoidAlienRaces`，**不用 `.AlienRace`** | 与目录一致。`AlienRace` 是 HAR 的根命名空间，会在 `Mugirl.Compatibility.*` 范围内遮蔽它（C3-1） | 否 | 否 |
| 类型 | `Mugirl.CorporatePricingCompatibility` | `Mugirl.Features.Corporate.Pricing.ForeignCompBehavior`；若保留在反射集中层，则为 `Mugirl.Compatibility.ForeignContent.ForeignCompBehavior` | 它不是与某个 “CorporatePricing” Mod 的兼容，而是巨企供货对“其他程序集组件”的行为分析；唯一调用方是 `Features/Corporate/Pricing/CorporateSupplyAnalysis.cs:231` | 否 | 否 |
| 类型 | `Mugirl.MeleeAnimationCompat` | `MeleeAnimationCompatibility` | 与其余 `*Compatibility` 统一 | 否 | 否 |
| 补丁类 | `Harmony_WeaponWheel_MeleeAnimationScope`（`MeleeAnimationPatches.cs:37`） | `Harmony_MeleeAnimation_SuppressIdleDuringLinkedStrike` | 在 MeleeAnimation 文件中，由 MeleeAnimation 的 Prepare 控制，却用了 `WeaponWheel_` 领域标签 | 否 | 否 |
| 补丁类 | `Harmony_WeaponWheel_{ReloadableWearer, ReloadableOwner, FindReloadable}`（`YayoCombatPatches.cs:36, 51, 67`） | `Harmony_YayoCombat_{ReloadableWearer, ReloadableOwner, FindReloadable}` | 同上，三者都由 YayoCombat 的 Prepare 控制 | 否 | 否 |
| 补丁类 | `Harmony_LactationExpansion_{CanBreastfeedNow, SuckleFromLactatingPawn}_Mugirl`（`LactationExpansionPatches.cs:13, 31`） | 去掉后缀 `_Mugirl` | 冗余 | 否 | 否 |
| 补丁元数据 | `[MugirlPatch("…")]` 的模块字符串：`"DualWieldCompat"`、`"WeaponWheelCompat"`、`"Compatibility"`、`"SaveCompatibility"`…（全仓库 23 种） | 改为封闭集合（`enum PatchModule` 或常量类），兼容类统一为 `Compatibility.<Mod>` | 自由字符串写错不会被发现；同一个 Mod 现在出现在 3 个模块名下 | 否 | 否 |
| 类型 | `CorporateValidationSaveMigration`、`NeuralShoulderSaveMigration`、`IncidentWorker_RetiredMugirlEvent` | 删除，改为升级层规则（A1-A3） | — | A3 的 Def 在存档中可见 | A3 可见 |
| 类型 | `Mugirl.Platform.Ui.GuiPhase` | `UiPhase` | 同目录其余 8 个类型都用 `Ui` 前缀 | 否 | 否 |
| 命名空间（可选） | `Mugirl.Rendering` | `Mugirl.Vfx`（与 `VfxDraw` 对应） | 与 `Mugirl.Platform.Rendering` 重名层级相同，在 `Mugirl.Platform.*` 内部 `Rendering` 指向后者，阅读时容易误解（`Platform/Rendering/NativeDraw.cs:7` 的注释必须写全名 `Mugirl.Rendering.VfxDraw`） | 否 | 否 |
| defName（存档可见，b） | `JobDriver_RopeMoo`、`JobDriver_RemoveRopeMoo`（`Mugirl_DefOf.cs:36, 38`） | `Mugirl_Rope`、`Mugirl_Unrope` | defName 写成 C# 类名的样子，还带 “Moo” | 是（curJob） | 是：定义在 `1.7/Defs/JobDefs/JobDefs.xml:5` 等 |
| defName（b） | `RopeToBuild`（:40）、`WallRopeHitch`（:8）、`ForceUnlockSlaveApparel`（:53）、`Job_Breastfeed`（`MugirlMilkDefOf.cs:25`） | `Mugirl_RopeToHitch`、`Mugirl_WallRopeHitch`、`Mugirl_ForceUnlockSlaveApparel`、`Mugirl_Breastfeed` | 没有 Mod 前缀，名字通用，有跨 Mod 撞名风险 | 是 | 是 |
| defName（b，可选） | `Job_*` 前缀的 13 个 JobDef；`Hediff_Mugirl*`（`MugirlContentDefOf.cs:113-116`） | 统一为 `Mugirl_<名称>` | 同一 Def 类型混用 `Job_`、`Mugirl_` 与无前缀三种写法 | 是 | 是 |

### C3 其他生产级重构建议

1. **命名空间遮蔽规则**。必须写成验证器规则，并补进迁移文档。C# 查找简单名称时，先查外层命名空间的成员，再看 using 指令。因此：
   - 若存在 `Mugirl.UI`，所有 `Mugirl.*` 代码中的 `UI` 都会解析为它，而不是 `Verse.UI`（`UI/MugirlSettingsWindow.cs:4` 已指出）。
   - 若存在 `Mugirl.Compatibility.AlienRace`，`Mugirl.Compatibility.*` 内部的 `AlienRace.X` 会指向它，而不是 HAR。
   - 建议的禁止名单：`UI`、`Text`、`Log`、`Find`、`Gen`、`Widgets`、`AlienRace`，以及所有已启用第三方的根命名空间。
2. **用 Harmony 补丁自己的代码**。共 10 个补丁类，都以 Mugirl 自己的类型为目标：
   - DualWield 4 个，目标 `Comp_WeaponWheel`（`DualWieldPatches.cs:7-64`）；
   - RunAndGun 1 个（`RunAndGunPatches.cs:7`）；
   - YayoCombat 2 个（`YayoCombatPatches.cs:8, 20`），其中 `CompTick` 的 Postfix 每个 tick 都对每个轮盘 Pawn 执行一次；
   - MeleeAnimation 2 个（`MeleeAnimationPatches.cs:7-33` 目标 `Comp_WeaponWheel`，:64-89 目标 `MountedPawnMeleeSupport`）；
   - YayoAnimation 1 个 Transpiler，改写自家的 `WeaponWheelAnimationRenderer.Draw`（`YayoAnimationPatches.cs:8`）。

   生产级做法是在 `Comp_WeaponWheel` 与 `MountedPawnMeleeSupport` 中提供显式扩展点（兼容钩子接口或静态事件），各兼容模块在 `Prepare` 等价处注册。这样更快，改名时由编译器保证一致，也不再依赖 `object[] __args` 和 `__originalMethod.Name` 判断（`DualWieldPatches.cs:74-86`）。
3. **Core 依赖 Features，层次倒置**。
   - `Core/MugirlSettings.cs:20, 39-45` 依赖 `MugirlExtraOutline`（`Features/Appearance/MugirlExtraOutline.cs:14-16`）的常量。
   - `UI/MugirlSettingsWindow.cs:1, 57, 72` 依赖 `Mugirl.Features.WeaponWheel.WeaponWheelDevLog` 与 `MugirlExtraOutline`。
   - 应当把常量放回设置层，由 Feature 读取设置；或者由各 Feature 向设置窗口注册自己的分区。
4. **DefOf 卫生**。
   - 47 个字段在 C# 中没有任何引用（1.6 中同样没有）：
     - `Mugirl_DefOf.cs`：:55、67、71、84、93、96、112、136、138；
     - `MugirlContentDefOf.cs`：:8-14、16、51-53、73、75-77、94、96、98、113-116、119-123、126-134；
     - `MugirlMilkDefOf.cs`：:31、33。
   - 它们只起到“Def 必须存在”的隐式断言作用。应当删除；确实需要存在性检查的，放进验证器的 `content-contracts.json`。
   - `[DefAlias]` 别名 :93-98 也属于无人使用。
5. **游戏生命周期中枢放错了位置**。`MugirlLog` 的限次日志键由一个剧情 Feature 组件负责清空（`Core/MugirlLog.cs:19`，调用点 `Features/Incidents/Story/MugirlStoryState.cs:74`）。同一个 `MugirlStoryState.FinalizeInit`/`LoadedGame`（:38-54，重置方法 :72-80）还负责全模组的瞬态状态重置和“读档专用的旧存档迁移”（:48-54 的 `MugirlEventUtility.ClearPlayerMigrationPawns`、`MugirlWildSlaveUtility.NormalizeLoadedPlayerPawnKinds`，不在本范围）。建议在 Core 中建立 `MugirlGameLifecycle`（GameComponent）作为唯一的生命周期中枢，由它承载升级层的盖章字段和 (f) 类步骤。
6. **种族判定只保留一个语义**。`IsMugirlPawn`（30 处调用）、`HasMugirlBody`（6 处）与 `IsMugirlDef`（2 处）混用，含义不同（`Core/MugirlIdentity.cs:8-33`）。另外 `IsMugirlPawnDef`（:35-38）没有调用方，`IsMugirlDef(ThingDef)`（:13）和 `HasMugirlBody(ThingDef)`（:23）只被它使用，因此也是死代码。
7. **其他死代码**：
   - `Core/MugirlGameUtility.cs:38-50` `TryRenderPawnPreview`、:370-373 `WorldPawnsContains`：零调用；
   - `Core/MugirlLog.cs:23, 26` 的 `Message`：两个重载零调用；
   - `Core/MugirlTickUtility.cs:53-56` 的 `Add` 只是 `counter += delta`；`ConsumeReady` 只被 Mounting 使用，应移入 Mounting；
   - `Compatibility/LactationExpansion/LactationExpansionCompatibility.cs:1-14` 只是对 `ModCompat` 的两行转发。
8. **可见性**。本范围有 41 个 `public` 顶层类型（`Platform/*`、`Mathematics/*`、`VfxDraw`、`HarmonyBootstrap` 等）。单 DLL 的 Mod 中，`public` 等于向其他 Mod 承诺 API。建议默认 `internal`，只对引擎或反射需要的类型保留 `public`：Mod 子类、XML 实例化的 Worker、`[DefOf]`（需逐个核实）。测试已有 `InternalsVisibleTo`（`MugirlRace.csproj:39-41`）。`HarmonyBootstrap.cs:23` 中“离线验证通过反射调用，形状不能改”的约束，也可以借 `InternalsVisibleTo` 改成直接调用。
9. **外部 Def 查找**。`Compatibility/ExternalHediffs/ExternalBreastHediffDefs.cs:6-11` 自述“来源 Mod 的 packageId 未经核对”，因此仍用 `GetNamedSilentFail`。生产级做法是核对 packageId，改成带 `[MayRequire]` 的 DefOf，或者按 Mod 分组。
10. **Platform 前缀方案没有文档**。`Engine*`（`EngineClock`、`EngineMaterialView`、`EngineTexture`）、`Native*`（`NativeDraw`、`NativeQuery`、`NativeBridge`、`NativeShaderPass`）、`Mod*`（`ModMesh`、`ModPropertyBlock`）三类前缀并存于同一命名空间。语义可以成立（引擎持有的句柄 / 经 Waylith 的操作 / Mod 自有的引擎资源），建议在 ADR-006 写明，不建议改名。
11. **版本信息**。`MugirlRace.csproj:8-11` 固定 `AssemblyVersion 1.0.0.0`，`InformationalVersion 1.7.0`，没有 Mod 版本或存档 schema 版本常量。建议加一个 `MugirlVersion.SaveSchema` 常量，供升级层盖章使用。原因见 D2：存档 meta 不含 Mod 版本。

### C4 跨模块提示（不在本范围，供其他审计者参考）

- ThingComp 命名违反原版 `CompX` 惯例：`Comp_WeaponWheel`、`Comp_MugirlMount`（本范围的补丁类引用它们）、`CompMooMilkable`。全仓库还混用 “Moo” 与 “Milk”：`MooPawn`（58 处）、`CompMooMilkable`（43 处）、`HasMooMilkSource`、`JobDriver_RopeMoo`、`Toils_RopeMoo` 等。
- `Features/Restraints` 下的 `Pawn_ApparelTracker_ExposeData_SlaveApparel_Patch` 会往原版节点里写额外数据。升级层的 (c) 类标签改名也要覆盖“写在原版节点内的 Mugirl 标签”。
- `1.7/Defs/ThingDefs_Misc/Items_Filth.xml:27` 注释称“旧 MugirlMilkFilth 保留给电击路径与旧存档”。该 Def 仍被 `Hediffs_AdvancedRestraints.xml:74, 127` 使用，不是纯粹的遗留物，交给 Defs 审计。

---

## D. 设计统一升级层所需的引擎事实（均已反编译核实）

以下内容迁移文档中还没有记载（第 1、2、3、5 条），或者只有部分记载（第 4 条已在 `兼容迁移指南.md:799-800` 中写明）。建议补入 `1.7迁移文档/兼容迁移指南.md` 的“存档中的类型名”一节。

1. **反序列化顺序**。`Game.LoadGame` 依次执行 `BackCompatibility.PreLoadSavegame` → `ExposeSmallComponents()` → `world` → `maps` → `FinalizeInit` → `GameComponentUtility.LoadedGame()` → `BackCompatibility.PostLoadSavegame`。在 `ExposeSmallComponents` 内部，`components`（全部 GameComponent）**排在最后**；在它之前加载的 `scenario`（ScenPart）、`storyteller`（事件队列）、`history`、`taleManager`、`playLog`、`letterStack`、`questManager` 等都可能含有 Mugirl 的类名或 Def。由此得出三点：
   - 盖章字段即使放在 GameComponent 里，也必须**从原始 XML 读取**，不能等 Scribe 读到；
   - 唯一能在任何 Mugirl 数据反序列化之前拿到整棵树的 Mod 挂点，是 `Game.ExposeSmallComponents` 的前缀，在 `LoadingVars` 模式下执行，此时 `Scribe.loader.curXmlParent` 就是 `game` 节点，`world` 与 `maps` 都是它的子节点。A1 和 A2 现在正是挂在这里；
   - (f) 类语义步骤放在 `GameComponent.LoadedGame`（在 `FinalizeInit` 之后）。
2. **存档 meta 不含 Mod 版本**。`ScribeMetaHeaderUtility.WriteMetaHeader` 只写 `gameVersion`、`modIds`、`modSteamIds`、`modNames`。因此必须自己盖章。版本判断可以这样做：没有章的存档就是“旧 schema”。1.6 各版本与重构前的 1.7 树写出的是**同一套名字**，因为验证器强制冻结（A-5），所以只需一条 v0 升级路径；它要能容忍重构前 1.7 新增的名字。需要区分时可读 meta 的 `gameVersion`（1.6.x 或 1.7.x）。升级步骤必须幂等：玩家读旧档后不存档再读，会再升级一次。
3. **Scribe 字典是两个按索引配对的并列列表**：`<label><keys><li/>…</keys><values><li/>…</values></label>`（`Scribe_Collections.Look<K,V>`）。`BuildDictionary` 遇到空键时报 “Null key while loading dictionary” 并跳过该对；遇到数量不等时报 “Keys count does not match…”，并按 `Math.Min` 配对，之后的键值全部错位。所以凡是删 `li` 的 XML 规则，都必须识别父节点是不是 `keys` 或 `values`，并成对删除。A2 当前的规则不具备这一点。
4. **原版 BackCompatibility 不可依赖**（与迁移指南一致）。`conversionChain` 和 `RemovedDefs` 都是 private；`GetBackCompatibleType`、`BackCompatibleDefName` 在 `CheckSaveIdenticalToCurrentEnvironment()` 为真时短路，即游戏 build 相同且 Mod 列表未变时；`AppliesToLoadedGameVersion` 只看**游戏**版本。因此建议：
   - 类型改名、标签改名、结构变换都在第 1 条的前缀中直接改写 XML，可离线单测，A1、A2 的测试方式可以沿用；
   - defName 改名，要么在同一处按“Mugirl 独有名字的精确文本匹配”改写 XML，要么对 `BackCompatibility.BackCompatibleDefName` 加 Postfix。`ScribeExtractor.DefFromNode` 先调用它，查不到再看 `WasDefRemoved`，最后报 “Could not load reference”。
5. **Scribe_Values 不写等于默认值的节点**（`!forceSave && value.Equals(defaultValue)` 时直接返回）。因此：
   - 改某个标签的**默认值**，就是对旧档的静默 schema 变更：旧档中缺失的节点会按新默认值读入。升级层要把“改默认值”当作改名一样登记；
   - 改标签名时，只有旧值不等于旧默认值的存档才需要迁移，但规则写成无条件迁移最简单。
6. **设置文件的行为**见 A4、A5：文件名只取短类名；读文件时 `GetBestFallbackType<T>` 对 ModSettings 回退到 `typeof(T)`。对 Thing、Hediff、Ability、Thought，同一函数回退到 Def 中登记的类，所以这四类实例改类名时，只要标签不变就能“自愈”，代价是每个实例一条错误。

## E. 给统一升级层的规则清单（本范围产出）

| 规则 | 类别 | 输入 | 动作 |
|---|---|---|---|
| R1 removed-component | e | `game/components/li/@Class` ∈ {`Mugirl.CorporateRuntimeValidation`, `Mugirl.CorporateVisualValidation`} | 删除该 `li` |
| R2 retired-thingdef | e | ThingDef `Mugirl_NeuralCombatShoulders`；RecipeDef `Make_Mugirl_NeuralCombatShoulders` | 删除带 `id` 的物品节点与订单节点；清理引用，字典中成对删除 |
| R3 retired-incidentdef | e | IncidentDef `Mugirl_WildManWandersIn` | 删除 `recentRandomIncidents` 中的项、`lastFireTicks` 中的键值对、事件队列中的项 |
| R4 defName 改名表 | b | C2 中被采纳的 defName（可选） | 改写 XML 文本或加 Postfix |
| S1 设置迁移（独立于存档） | — | 若采纳 `enableOpeningCrashStoryline` 等改名 | 在 `MugirlSettings.ExposeData` 的 LoadingVars 阶段回退读取旧标签一次，并写入 `settingsVersion` |
