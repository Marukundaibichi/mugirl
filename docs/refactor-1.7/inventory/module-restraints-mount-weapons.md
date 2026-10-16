# 模块审计：Restraints / Roping / Mounting / Lances / WeaponWheel / Weapons

只读审计，2026-10-15。范围：`Source/MugirlRace/Features/{Restraints,Roping,Mounting,Lances,WeaponWheel,Weapons}`，共 171 个 .cs 文件。

路径缩写：
- `F/` = `Source/MugirlRace/Features/`
- `S16/` = `1.6/Source/Features/`（冻结的 1.6 源码）
- `D17/` = `1.7/Defs/`
- `L17/` = `1.7/Languages/`
- `rw17` = `RimWorld/Assembly-CSharp.dll`（1.7），通过 DecompilerServer 反编译核实

升级器需求代码沿用任务定义：(a) 类型改名映射，(b) defName 改名映射，(c) 有类型归属的 Scribe 标签改名，(d) 枚举值改名，(e) 存档 XML 结构变换，(f) 需要活游戏对象的读档后语义步骤，(g) 不需要处理，可以直接删除。

---

## 0. 已核实的引擎事实（影响升级器设计）

| # | 事实 | 证据 | 对升级器的影响 |
|---|---|---|---|
| E1 | `Scribe_Values.Look` 在值等于默认值时不写节点（forceSave=false）。读取时用 `Scribe.loader.curXmlParent[label]`，即**第一个**同名直接子节点。 | rw17 `Verse.Scribe_Values.Look<T>` | ① 默认值属于 schema：改默认值等于改变旧档语义。标签改名只能改已有节点；节点缺失时新旧默认值必须相同，否则要先把旧默认值写成显式节点。② 1.6 的重复标签无法按位置还原（见 A1）。 |
| E2 | `HediffWithComps.ExposeData` 在 LoadingVars 时先 `InitializeComps()`，再按 `comps` 的顺序调用每个 `CompExposeData()`。所有 HediffComp 的字段都写在同一个 hediff 节点里。 | rw17 `Verse.HediffWithComps.ExposeData` | 同一 Hediff 上的 comp 标签必须互不相同（A1）。 |
| E3 | `Pawn_JobTracker.ExposeData`：PostLoadInit 时如果 `curDriver == null && curJob != null`，会打警告 “Cleaning up invalid job state” 并执行 `EndCurrentJob(Errored)`。`JobDriver.ExposeData` 用 forceSave 保存 `curToilIndex`，PostLoadInit 时调用 `SetupToils()`。`ReservationManager.Reservation` 用 `Scribe_References` 保存 `job`。 | rw17 三处 ExposeData | 不能在 XML 里删除进行中的 Job，否则预约会留下悬空引用。要取消 Job，应在读档后调用 `EndCurrentJob`，即 (f)（A11）。 |
| E4 | `CompProperties.compClass` 带 `[TranslationHandle]`，DefInjected 列表句柄等于 compClass 的 `Type.Name`。 | rw17 `TranslationHandleUtility.GetBestHandleWithIndexForListElement` | 改 comp 类短名会改变翻译键，改命名空间不会。L17 中本模块实际用到的句柄：`comps.Comp_BrainwashHelmet`（4）、`Comp_MagneticShackles`（20）、`Comp_ShockCollar`（8）、`CompRopeToBuild`（4）、`CompStampedApparelKey`（6）、`HediffComp_BrainWashingStar`（78）。 |
| E5 | HarmonyBootstrap 按 `FullName` 的序数顺序应用补丁类。 | `Source/MugirlRace/Core/HarmonyBootstrap.cs:207` | 补丁目标和优先级相同时，执行顺序由类名决定。本模块依赖这一点的注释：`F/Restraints/Patches/ApparelTrackerPatches.cs:5`、`F/Restraints/Patches/Harmony_PawnGenerator_Patch.cs:5-6`。多个补丁共用目标的情况（见 `Source/Tests/MugirlRace.Validation/Baselines/patch-targets.txt`）：`Pawn.DynamicDrawPhaseAt` 上有 `Mugirl.Features.Lances.Harmony_LanceCharge_BlurRecovery`、`Harmony_MountRendering`、`Harmony_WeaponWheel_AnimationDraw`；`Verb.get_WarmupTime` 上有 `Harmony_MountedCombatController_WarmupTime`、`Harmony_WeaponWheel_WarmupTime`；`Pawn_EquipmentTracker.Notify_EquipmentRemoved` 上有 `Harmony_MountedCombatController_EquipmentRemoved`、`Harmony_WeaponWheel_EquipmentRemoved`；`PawnGenerator.GeneratePawn` 上有 `PawnGenerator_GeneratePawn_Patch`、`Harmony_WeaponWheel_EnemyLoadout` 等。**改类名或命名空间之前，要先补上显式的 `[HarmonyPriority]`/`[HarmonyAfter]`。** |
| E6 | 0.4.7（`324501c`，2026-07-03）把命名空间从 `MooGirl` 改为 `Mugirl`，同时改了 `Job_MountMooGirl→Job_MountMugirl`、`Job_DismountMooGirl→Job_DismountMugirl`，以及全部 `MooGirl_*` defName。冻结的 1.6 源码和 1.7 树里都没有 MooGirl 兼容代码（grep 结果为 0）。 | `git show 324501c` | 0.1.0–0.4.4 的存档现在已经无法读取。若升级器要覆盖“任何 1.6 时代版本”，可以先加通用前缀规则（类型 `MooGirl.`→`Mugirl.`，defName `MooGirl_`→`Mugirl_`），再套用本报告的映射。是否支持由所有者决定。 |
| E7 | `Source/MugirlRace` 整棵树没有纳入 git（`git ls-files` 返回 0，status 为 `??`），但 `1.7/Assemblies/MugirlRace.dll` 已经构建。 | git | “1.7 预重构树存档”只可能是本地测试档。它们只多出 A1 中的那组标签，其余与 1.6 相同。区分来源可以用存档 meta 中的 gameVersion：1.6.x 表示 1.6 构建；1.7.x 且没有新戳记表示预重构 1.7 树。 |
| E8 | 校验器基线按冻结规则锁住了本模块的名字：`Baselines/save-types.txt`（`GameComponent_BrainwashPerformance`、`MapRopingIndex`）、`dropped-scribe-labels.txt`（`currentTickCount`、`remainingCycles`、`tickCounter`）、`scribe-defaults.txt`（`currentTicksToChange`、`currentCycleInterval`、`currentBindDurationTicks`、`nextStateTick`、`lockCount`）、`empty-types.allow.txt`（`BrainWashSlaveApparel`、`CompAdultContentControl`）、`patch-targets.txt`。 | `Source/Tests/MugirlRace.Validation/Baselines/` | 新方向下，这些“与 1.6 逐名比较”的检查要改成“与升级器映射表比较”。 |

---

## A. 遗留 / 向后兼容代码

| ID | 位置 | 处理的旧形态（引入/变化版本） | 新的规范形态 | 升级器需要 | 说明 |
|---|---|---|---|---|---|
| **A1** | `F/Restraints/HediffComps/HediffCompScribe.cs:3-24`（整类）；`HediffComp_DelayedTrigger.cs:5,14-16,41-46`；`HediffCompProperties_AddMentalState.cs:23-25`；`HediffCompProperties_AddThoughts.cs:26-28`；`HediffCompProperties_AddTrait.cs:34-36`；`HediffCompProperties_ReduceWillorEnslave.cs:26-28`；`HediffCompProperties_SuppressionEnhancer.cs:51-55` | **1.6 所有版本**：洗脑 Hediff 上的 4 个延迟触发 comp 都写共用标签 `age`/`triggered`，SuppressionEnhancer 只写 `age`（`S16/Restraints/HediffCompProperties_AddMentalState.cs:59-63`、`…AddThoughts.cs:63-67`、`…AddTrait.cs:108-112`、`…ReduceWillorEnslave.cs:100-104`、`…SuppressionEnhancer.cs:67-70`）。**1.7 树**改写 `mugirlAddMentalStateAge/Triggered`、`mugirlAddThoughtAge/Triggered`、`mugirlAddTraitAge/Triggered`、`mugirlReduceWillAge/Triggered`、`mugirlSuppressionAge`；读不到新标签时回退到旧标签。 | 每个 comp 使用唯一标签，直接调用 `Scribe_Values`，没有回退。 | **(e)+(c)** | comps 顺序：Level1 和 Level3 为 PerformanceEffect → ReduceWillorEnslave(age,triggered) → SuppressionEnhancer(age) → AddThought(age,triggered) → …；Level5 为 PerformanceEffect → AddMentalState → AddTrait → ReduceWillorEnslave（`D17/HediffDefs/Hediffs_AdvancedRestraints.xml:198-362,385-546,569-738`）。这个顺序从 3.2 起没变过（git 核对），1.6 与 1.7 也相同（diff 核对）。**按位置还原不可行**：按 E1，age=0 和 triggered=false 不会写入，所以节点数少于 comp 数时无法判断各节点属于哪个 comp。建议的 XML 变换：对 `<def>` 属于 {Mugirl_BrainWashing_Level1,3,5}、且没有新标签的 hediff 节点（1.6 来源），把**第一个** `<age>` 复制给每个 comp 的 age 标签，把**第一个** `<triggered>` 复制给每个 comp 的 triggered 标签，然后删除旧节点。这与 1.6 构建和当前 1.7 树的实际行为完全相同（“只要有一个 comp 触发过，就当全部触发过”，保守，不会重复触发）。1.7 树存档只在最终标签变化时改名。附带发现：现在的回退逻辑在**新**存档里也会生效（新标签的值等于默认值时不写节点，读档时就去读 `age`）。目前没出问题，只是因为该 hediff 节点下没有别的代码直接写 `age`（`BrainwashPerformancePlayer` 的 `age` 嵌在 `<performancePlayer>` 里，不受影响）。 |
| **A2** | `F/Restraints/Apparel/AdvancedSlaveApparel.cs:45-49` | 1.6 的 `Crack()` 只在束具被穿戴时清除锁定，所以在地面上破解的高级束具会存成 `isCracked=True`，同时 `isLocked` 缺省（=true）、`lockCount` 不为 0。 | 破解即解锁：`isCracked ⇒ isLocked=false, lockCount=0`（`AdvancedSlaveApparel.cs:12-17` 无条件执行 `ClearLockState`）。 | **(e)** | 对 Class ∈ {`Mugirl.AdvancedSlaveApparel`,`Mugirl.BrainWashSlaveApparel`} 且含 `<isCracked>True</isCracked>` 的 thing/li 节点，写入 `<isLocked>False</isLocked>` 和 `<lockCount>0</lockCount>`。lockCount 的默认值随 Def 变化（见 B2），所以必须显式写 0。地面物品不在任何 `lockedApparel` 列表里，不需要 (f)。 |
| **A3** | `F/Restraints/Apparel/SlaveApparel.cs:51-54` | `isLocked && lockCount<=0`。这段修复从 3.3（`d42b58f`，2026-06-07）就存在，对应早期开锁流程留下的状态。 | 现有代码维持不变式 `isLocked ⇒ lockCount≥1`：`UseKey` 回滚、`TryReleaseAndDrop`、`DevUnlock`、`ClearLockState`（`SlaveApparel.cs:143-208`）。 | **(f)**，也可以用 (e)+按 `<def>` 查 Def | `lockCount := def.needkeynumber`，然后从领域代码中删除这段修复。 |
| **A4** | `F/Restraints/Patches/ApparelTrackerPatches.cs:20-31`（`Pawn_ApparelTracker_ExposeData_SlaveApparel_Patch`，每次读档对每个 pawn 运行）；它调用 `SlaveApparelExtensions.cs:89-107` | `b3074b3`（2026-07-12，最早随 0.5.0 发布）之前的存档：锁定中的束具不在原版 `Pawn_ApparelTracker.lockedApparel` 里。 | 原版 lockedApparel 与 `IsEffectivelyLocked` 保持一致：穿戴时由 `SlaveApparel.Notify_Equipped` 加锁，生成时由 `PawnGenerator_GeneratePawn_Patch` 加锁。 | **(f)**，只执行一次 | 遍历所有 pawn（地图、世界 pawn、尸体内、骑手容器内、商队内），调用 `EnsureWornSlaveApparelLocks()`。之后删除补丁类及它在 patch-targets 基线中的条目。`PawnGenerator_GeneratePawn_Patch` 不是遗留代码，保留。 |
| **A5** | `F/Restraints/Comps/CompProperties_MagneticShackles.cs:245`（注释）；`F/Restraints/HediffComps/HediffCompProperties_EleShock.cs:110`（注释） | 1.6 写了从不读取的 `tickCounter`（`S16/Restraints/Comps/CompProperties_MagneticShackles.cs:365`），以及 `currentTickCount`/`remainingCycles`（`S16/Restraints/HediffCompProperties_EleShock.cs:346-347`）。 | 无 | **(g)** | Scribe 会忽略多余节点。只需删除注释和 `dropped-scribe-labels.txt` 中的条目；升级器也可以顺手删掉这些节点。 |
| **A6** | 死 XML 契约字段/类型：`CompProperties_MagneticShackles.cs:24-26`（`labelWhenActive`/`labelWhenInactive`，D17 中写了 1 次）；`HediffCompProperties_EleShock.cs:20-22`（`effectCycleInterval`/`maxCycles`，D17 中各 3 次）；`F/Restraints/Apparel/SlaveApparelDef.cs:4,7-17`（`soul_type`：XML 没写、C# 也不读；`gives_bound_moodlet` 写了 6 次、`gives_gagged_moodlet` 2 次、`blocks_oral` 2 次，`blocks_penis/vagina/anus/breasts` 0 次，C# 都不读，**只有 `blocks_hands` 被读取**）；`F/Restraints/Apparel/AdultContentControl.cs:3-21`（`AdultContentExtension`、`CompProperties_AdultContentControl`、`CompAdultContentControl`，C# 中读取方为 0，XML 用在 `D17/Apparel/Apparel_Bondage/BondageApparel_Base.xml:30,37-39` 和 `BondageApparel_Spacer.xml` 中的 3 处） | 1.6 时期留下的 XML 字段 | 从 C# 和 XML 中同时删除 | **(g)** | 这些都是 Def 数据或没有状态的 comp，不进存档。同时删除 `empty-types.allow.txt` 中的条目。 |
| **A7** | `F/Restraints/Jobs/JobDriver_UseItemOn.cs:6-7`（整类）；`F/Restraints/Comps/CompGetSlaveApparelGear.cs:3-4`（整类，通过 `BondageApparel_Base.xml:31-33` 挂到所有束具上）；`D17/JobDefs/JobDefs.xml:21-22` 中的 JobDef `GiveSlaveApparelGear`；`L17/*/DefInjected/JobDef/JobDefs.xml` | 注释说保留它们是“以防旧档里有进行中的该 Job”。 | 删除 | **(g)** | 在整个 git 历史里，1.6 源码从未下达过这个 JobDef（`git log -S GiveSlaveApparelGear -- 1.6/Source` 为空，1.7 树里也没有下达方），所以任何存档都不会含有它。三个文件连同翻译一起删除。 |
| **A8** | `F/Roping/MapRopingIndex.cs:6`（注释称类名冻结）及整类 | 每张地图都存一个 `<li Class="Mugirl.MapRopingIndex" />`。1.6 和 1.7 都没有 ExposeData（`S16/Roping/MapRopingIndex.cs` 中没有 Scribe 调用）。 | “待系状态”只存在于同一 tick 内的两个瞬时 toil 之间（`JobDriver_RopeToWallRopeHitch.cs:28,47-86`），不需要 MapComponent，改成进程级静态集合即可（开局和读档时清空）。 | **(e)** | 在 Map 组件实例化之前，从每个 `map/components` 中删除这个 `li`（可参考现有的 `Compatibility/Savegames/CorporateValidationSaveMigration.cs` 的做法：在 Scribe 之前删节点）。不删的话，每张地图读档会报两条红字（迁移指南“存档中的类型名”）。如果选择保留 MapComponent、只改名，则改为 (a)。 |
| **A9** | 只因存档而冻结的名字（注释明确写了）：`F/Restraints/Performance/GameComponent_BrainwashPerformance.cs:3,93`；`F/Restraints/Apparel/SlaveApparel.cs:7`（标签）；`F/Mounting/Comp/Comp_MugirlMount.cs:10` 与 `Comp_MugirlMount.Storage.cs:3`（13 个标签）；`F/Restraints/Performance/BrainwashPerformancePlayer.cs:7`（标签）；`F/Mounting/Combat/MountedCombatController.Turret.cs:6`；`F/Roping/MapRopingIndex.cs:6`；`F/Roping/Jobs/JobDriver_FollowRoper.cs:5`、`JobDriver_RemoveRopeMoo.cs:5`、`JobDriver_RopeMoo.cs:5`、`JobDriver_RopeToWallRopeHitch.cs:6`；`F/Mounting/Jobs/JobDriver_MountMugirl.cs:5`、`JobDriver_DismountMugirl.cs:5`；`F/WeaponWheel/AI/JobDriver_LoadWeaponWheel.cs:6`；`F/Lances/Flight/PawnFlyer_LanceCharge.cs:6`；`F/WeaponWheel/AI/WeaponWheelJobs.cs:5-6`（`Job.count` = 槽位+1 的编码） | — | 见 C3/C4 | 按最终改名决定，使用 **(a)/(c)** | `Job.count` 编码本身可以保留；只是“不能改”的约束不再成立。 |
| **A10** | 只因 XML 或翻译而冻结的名字（与存档无关）：`F/Mounting/Comp/Comp_MugirlMount.cs:7`（**说法不实**：XML 只引用 `CompProperties_MugirlMount`，见 `D17/ThingDefs_Races/Mugirl_Race.xml:1054`）；`CompProperties_MugirlMount.cs:3`；`F/WeaponWheel/Core/CompAutoloadingSystem.cs:5`（**说法不实**：L17 中没有 `comps.CompAutoloadingSystem` 句柄）；`CompProperties_AutoloadingSystem.cs:3`；`CompProperties_WeaponWheel.cs:5`；`F/Weapons/MugirlRaceRestrictedExtension.cs:3`；`CompProperties_BrainwashHelmet.cs:44`；`CompProperties_MagneticShackles.cs:86`；`CompProperties_ShockCollar.cs:46`；`CompStampedApparelKey.cs:7`；`HediffComp_BrainWashingStar.cs:3`、`HediffCompProperties_PerformanceEffect.cs:4`；`ApparelTrackerPatches.cs:5` 与 `Harmony_PawnGenerator_Patch.cs:5-6`（补丁顺序依赖类名） | — | — | **(g)**，存档无关 | 改名时要同步 XML、DefInjected 键（E4）和显式补丁优先级（E5）。两处不实的注释应当纠正。 |
| **A11** | toil 布局改变，1.6 存档中进行中的 Job 对不上新布局：`F/Restraints/Jobs/JobDriver_CrackBondageGear.cs:39-41`、`JobDriver_UnlockSlaveApparelGear.cs:43-45`。1.6 的写法见 `S16/Restraints/JobDriver_CrackBondageGear.cs:56-85` 和 `JobDriver_UnlockSlaveApparelGear.cs:83-131`：toil 数随背包和生成状态裁剪，可能是 1 个，也可能是 3 个。 | 1.6 存档中保存的 `curToilIndex` 按旧布局计数 | 布局固定：`RestraintToils.AcquireItem` 总是产出 5 个 toil（`RestraintToils.cs:11-`）。 | **(f)**，只针对 1.6 来源的存档 | 在 `LoadedGame`/`FinalizeInit`（PostLoadInit 之后、第一次 DriverTick 之前），对 `curJob.def` ∈ {`UnlockSlaveApparel`,`CrackBondageGear`} 的 pawn 执行 `EndCurrentJob(InterruptForced)`，并删除 `jobQueue` 中的这两种 Job（E3：不能用删除 XML 的方式）。**已逐一比对，以下 Job 的布局 1.6 与 1.7 相同，可以直接续跑**：ForceUnlockSlaveApparel、RopeToBuild、Job_FollowRoper（1 个 toil，与原版相同）、JobDriver_RopeMoo、JobDriver_RemoveRopeMoo、Mount/Dismount、LoadWeaponWheel、LanceCharge。RopeToBuild 在 1.6 读档时本来就会 `yield break`（`S16/Roping/JobDriver_RopeToWallRopeHitch.cs:29-31`），1.7 修好了这个问题。 |
| **A12** | `F/Lances/Flight/PawnFlyer_LanceCharge.cs:77-81`（`hitPawns ??=`；0.6.3 的存档没有这个节点，它从 0.6.4/`37545af` 才加入）、`:75`（`wallHitPointsRemaining` 默认为满额，0.6.6/`8cd70f9` 加入）、`:178-191`（`lastPassableCell` 无效时从 `startVec` 重建，对应 0.6.4/0.6.5 的存档） | 旧版本存档里正在飞行的飞行器（存活不到 1 秒） | 字段总是存在；`lastPassableCell` 在 `SpawnSetup(!respawningAfterLoad)` 时初始化为起点格。 | **(e)** | 给缺少节点的 `Mugirl.Features.Lances.PawnFlyer_LanceCharge` 补上 `<hitPawns/>`；`lastPassableCell` 缺失时接受“从当前格继续”的近似处理。注意：`:178-191` 也是**新**冲锋的初始化路径（新飞行器的 `lastPassableCell` 初始为 Invalid），不是纯遗留代码。要先把初始化移到生成时，再删除这段。飞行器节点里包着被带飞的 pawn，绝对不能删除该节点。 |
| **A13** | `F/Mounting/Comp/Comp_MugirlMount.Storage.cs:10-14`（`Scribe_Deep` 读不到节点时得到 null，随后重建容器）；`F/WeaponWheel/Core/Comp_WeaponWheel.cs:13,85-93,227-264`（集合缺节点或槽数变化时补齐；另外 `CompTick` 中 `:99` 每 tick 都调用 `EnsureCollections`） | 加入该 comp 之前的旧档：骑乘在 0.4.4 时已经存在，轮盘在 0.5.0 之前不存在。 | — | **(g)**，作为不变式守卫保留 | XML 中 `maxSlots` 变化同样需要 resize，所以不是兼容代码，只是 Scribe 缺节点时的语义处理。建议删除 `Comp_WeaponWheel.cs:99` 的每 tick 调用，只在 Initialize/PostLoadInit 时调用。 |
| **A14** | `F/Roping/Jobs/JobDriver_FollowRoper.cs:12` 的 FailOn | 注释写的是“兼容旧存档及异常中断留下的孤儿跟随 Job” | — | **(g)**，保留 | 它同时防御运行期的异常中断，只需改写注释。 |
| **A15** | 游戏版本的双路径（与存档无关）：`F/Mounting/Combat/VanillaMeleeAccess.cs:8,70,93`（日志文本是 “using the 1.6 fallback”）；`F/Roping/RopeTrackerAccess.cs:13`（退回 BreakAllRopes） | 反射找不到成员时降级运行 | 只支持 1.7 后，成员缺失时在启动自检中报一次错即可。 | **(g)** | 日志文字中的 “1.6 fallback” 容易误导。 |
| **A16** | 为与 1.6 逐位一致而加的约束（与存档无关）：`F/WeaponWheel/AI/WeaponWheelEnemyLoadoutGenerator.cs:4`（Rand 调用顺序）、`SwordDanceDashCurve.cs:81`、`WeaponWheelAnimationCurves.cs:7`、`MountDrawOffsets.cs:3,6,44`、`HediffCompProperties_EleShock.cs:208`、`HediffCompProperties_ReduceWillorEnslave.cs:71`、`HediffCompProperties_SuppressionEnhancer.cs:75` 等 | 与 1.6 的计算结果逐位一致 | 新方向下不再要求 | **(g)** | 本模块共有 **89 行注释、分布在 62 个文件中**提到 “1.6”。重构后应改写为描述当前行为，与 1.6 的差异移到迁移文档。 |
| **A17** | `F/WeaponWheel/Rendering/WeaponWheelBackLayoutData.cs:86`（旧覆盖文件没有镜像字段）；`WeaponWheelBackLayout.cs:6-8,22,75-`（每次启动都读取 `Config/Mugirl/WeaponWheelBackLayout.xml`） | 用户配置文件的旧格式 | 正式数值已经写进默认表（`WeaponWheelBackLayoutData.cs:25`）。 | **(g)** | 建议只在开发者模式下读取覆盖文件。否则旧文件会一直压过新的默认值，该文件自己的注释（`:8`）也承认了这一点。 |
| **A18** | `F/Restraints/Apparel/SlaveApparelExtensions.cs:35-39`（`IsUnlockAdvancedApparel`，“名称沿用 1.6”，实际含义是“已破解”）；`F/Mounting/MountedPawnUtility.cs:10-14`（`IsMugirl`，“历史入口”，有 2 个调用方：`MountEligibilityService.cs`、`UI/FloatMenuProvider_MountMugirl.cs`） | 旧 API 名 | 前者改为 `IsCrackedAdvanced…`；后者让调用方直接用 `MugirlIdentity.IsMugirlPawn` | **(g)** | 纯代码改名。 |

---

## B. 存档可见面

### B1 存档中写 `Class=` 的类型

| 当前全名（1.7 树） | 基类 / 写入位置 | 1.6 冻结全名 | 0.4.4 及更早 | XML 引用位置 |
|---|---|---|---|---|
| `Mugirl.SlaveApparel` | Apparel。写在地图 things、`wornApparel/innerList`、背包、尸体、商队等处的 `<thing Class=…>`/`<li Class=…>` | 相同 | `MooGirl.SlaveApparel` | `D17/Apparel/Apparel_Bondage/BondageApparel_Base.xml:5`（thingClass） |
| `Mugirl.AdvancedSlaveApparel` | 同上 | 相同 | `MooGirl.AdvancedSlaveApparel` | `BondageApparel_Spacer.xml:6` |
| `Mugirl.BrainWashSlaveApparel` | 同上（空标记类，`F/Restraints/Apparel/BrainWashSlaveApparel.cs:4-6`） | 相同 | `MooGirl.BrainWashSlaveApparel` | `BondageApparel_Spacer.xml:443` |
| `Mugirl.GameComponent_BrainwashPerformance` | `game/components/li` | 相同 | `MooGirl.GameComponent_BrainwashPerformance` | — |
| `Mugirl.MapRopingIndex` | `map/components/li`（没有字段） | 相同 | `MooGirl.MapRopingIndex` | — |
| `Mugirl.JobDriver_CrackBondageGear` | curDriver | 相同 | `MooGirl.*` | `D17/JobDefs/JobDefs.xml:43` |
| `Mugirl.JobDriver_ForceUnlockSlaveApparel` | curDriver | 相同（`03439c8`，最早随 0.5.0 发布） | — | `JobDefs.xml:36` |
| `Mugirl.JobDriver_UnlockSlaveApparelGear` | curDriver | 相同 | `MooGirl.*` | `JobDefs.xml:29` |
| `Mugirl.JobDriver_UseItemOn` | curDriver（实际从未下达过） | 相同 | `MooGirl.*` | `JobDefs.xml:22` |
| `Mugirl.JobDriver_FollowRoper` | curDriver。1.7 改为继承 `Verse.AI.JobDriver_FollowRoper`；1.6 继承 JobDriver，并逐行复制了原版（都只有 1 个 toil，没有额外的 Scribe 字段） | 同名 | `MooGirl.*` | `JobDefs.xml:65` |
| `Mugirl.JobDriver_RopeMoo` | curDriver | 相同 | `MooGirl.*` | `JobDefs.xml:6` |
| `Mugirl.JobDriver_RemoveRopeMoo` | curDriver | 相同 | `MooGirl.*` | `JobDefs.xml:14` |
| `Mugirl.JobDriver_RopeToWallRopeHitch` | curDriver | 相同 | `MooGirl.*` | `JobDefs.xml:50` |
| `Mugirl.JobDriver_MountMugirl` | curDriver | 相同 | `MooGirl.JobDriver_MountMooGirl` | `D17/JobDefs/JobDefs_Mounting.xml:6` |
| `Mugirl.JobDriver_DismountMugirl` | curDriver | 相同 | `MooGirl.JobDriver_DismountMooGirl` | `JobDefs_Mounting.xml:13` |
| `Mugirl.Features.Lances.JobDriver_LanceCharge` | curDriver。冲锋期间 Job 挂在飞行器的 `jobQueue` 里 | 相同（0.6.3） | — | `D17/JobDefs/JobDefs_Lances.xml:3-4` |
| `Mugirl.JobDriver_LoadWeaponWheel` | curDriver | 相同（0.5.0） | — | `D17/JobDefs/JobDefs_WeaponWheel.xml:5` |
| `Mugirl.Features.Lances.PawnFlyer_LanceCharge` | 地图 things 中的 `<thing Class=…>`，内含被带飞的 pawn | 相同（0.6.4） | — | `D17/ThingDefs_Misc/LanceCharge.xml:6` |

**不会写 `Class=` 的 IExposable**：`GameComponent_BrainwashPerformance.ActiveBrainwashPerformance`（用 Deep 列表保存，声明类型等于运行时类型）和 `BrainwashPerformancePlayer`（同理）。本模块内**没有** Verb、Hediff、Ability、Thought、LordJob、LordToil、WorldObject、GameCondition、QuestPart、Gene、Letter、MentalState、Need、Precept、Tale、Bill 或 TransportersArrivalAction 的子类。

### B2 内联保存者：Scribe 调用全表

| 所有者 | 文件:行 | 父节点 | label | Scribe | 字段类型 | 默认值 | 1.6 对照 | 1.7 树的差异 |
|---|---|---|---|---|---|---|---|---|
| SlaveApparel（Thing） | `F/Restraints/Apparel/SlaveApparel.cs:46-55` | 自身的 thing/li 节点 | `lockCount` | Values | int | `InitialLockCount` = `def.needkeynumber`（**取决于 Def**） | `S16/Restraints/SlaveApparelDef.cs:84-89` 相同 | 无 |
| | | | `isLocked` | Values | bool | true | 相同 | 无 |
| AdvancedSlaveApparel | `AdvancedSlaveApparel.cs:40-50` | 同上 | `isCracked` | Values | bool | false | `S16/…/SlaveApparelDef.cs:226-229` | 增加了 PostLoadInit 修复（A2） |
| Comp_BrainwashHelmet | `F/Restraints/Comps/CompProperties_BrainwashHelmet.cs:176-186` | 束具的 thing 节点 | `nonBlockedTicks`=0、`lastManualUseTick`=-999999、`currentTicksToChange`=0 | Values | int | 见 label 列 | `S16/Restraints/Comps/CompProperties_BrainwashHelmet.cs:302-308` | 无 |
| Comp_MagneticShackles | `CompProperties_MagneticShackles.cs:241-259` | 同上 | `lastManualUseTick`=-999999、`isActive`=false、`currentCycleInterval`=0、`currentBindDurationTicks`=0、`nextStateTick`=-1 | Values | int/bool | 见 label 列 | `S16/…:362-371`，另有 `tickCounter` | 去掉了 `tickCounter` |
| Comp_ShockCollar | `CompProperties_ShockCollar.cs:98-103` | 同上 | `ticks`=0、`lastManualUseTick`=**-99999** | Values | int | 见 label 列 | `S16/…:135-139` | 无 |
| CompTargetable_SlaveApparel（继承原版 CompTargetable） | 原版 `CompTargetable.PostExposeData` | 解码器的 thing 节点 | `selectedTarget` | References | Thing | — | 相同 | — |
| CompStampedApparelKey、CompRopeToBuild（都继承 CompUsable） | 原版 CompUsable 没有 PostExposeData（rw17 已核实） | — | — | — | — | — | — | — |
| HediffComp_AddMentalState | `HediffComp_DelayedTrigger.cs:41-46` 与 `HediffCompProperties_AddMentalState.cs:23-25` | hediff 节点 | `mugirlAddMentalStateAge`=0 / `mugirlAddMentalStateTriggered`=false（回退读 `age`/`triggered`） | Values | int/bool | 见 label 列 | 1.6 为 `age`/`triggered` | 标签已改 |
| HediffComp_AddThought | 同上，`…AddThoughts.cs:26-28` | 同上 | `mugirlAddThoughtAge` / `mugirlAddThoughtTriggered` | 同上 | | | 1.6 为 `age`/`triggered` | 标签已改 |
| HediffComp_AddTrait | `…AddTrait.cs:34-36` | 同上 | `mugirlAddTraitAge` / `mugirlAddTraitTriggered` | 同上 | | | 同上 | 标签已改 |
| HediffComp_ReduceWillorEnslave | `…ReduceWillorEnslave.cs:26-28` | 同上 | `mugirlReduceWillAge` / `mugirlReduceWillTriggered` | 同上 | | | 同上 | 标签已改 |
| HediffComp_SuppressionEnhancer | `…SuppressionEnhancer.cs:51-55` | 同上 | `mugirlSuppressionAge`=0（回退读 `age`） | Values | int | 0 | 1.6 为 `age` | 标签已改 |
| HediffComp_EleShock | `HediffCompProperties_EleShock.cs:106-117` | 同上 | `jitterTicksLeft`=0；`filthTimers` | Values；Collections(Def,Value) | int；`Dictionary<ThingDef,int>`（keys/values 子节点） | 0 | `S16/…:342-350`，另有 `currentTickCount`、`remainingCycles` | 去掉了 2 个标签 |
| HediffComp_BrainWashingStar | `F/Restraints/Performance/HediffComp_BrainWashingStar.cs:28-37` | 同上 | `performancePlayer` | Deep | BrainwashPerformancePlayer | — | `S16/…:30-35` | 无 |
| HediffComp_DisappearsAndAddHediffs | 继承原版 `HediffComp_Disappears` | 同上 | `ticksToDisappear`、`disappearsAfterTicks`、`seed`（原版） | — | — | — | 相同 | — |
| BrainwashPerformancePlayer | `BrainwashPerformancePlayer.cs:124-144` | `<performancePlayer>` 或 `<player>` 子节点 | `age`=0、`stunTickTarget`=-1、`stunned`=false、`stunEndTick`=-1、`stunMessageSent`=false、`nextTextShowTicks`/`nextSoundTicks`/`nextFleckTicks`（Collections Value，`List<int>`）、`active`=false | | | | `S16/Restraints/BrainwashPerformancePlayer.cs:116-128` | 无 |
| GameComponent_BrainwashPerformance | `GameComponent_BrainwashPerformance.cs:51-60` | `<li Class="Mugirl.GameComponent_BrainwashPerformance">` | `activeBrainwashPerformances` | Collections Deep | `List<ActiveBrainwashPerformance>` | — | `S16/…:50-55` | 无 |
| ActiveBrainwashPerformance | `…:130-141` | `<li>` | `pawn`（References Pawn）、`sourceHediffDef`（Defs HediffDef）、`player`（Deep） | | | | `S16/…:140-146` | 无 |
| Comp_MugirlMount | `F/Mounting/Comp/Comp_MugirlMount.Storage.cs:6-28` | **载具 pawn 的 thing 节点**（与该 pawn 上所有 comp，包括其他 Mod 的 comp 共用） | `innerContainer`（Deep，`ThingOwner<Thing>`，oneStackOnly、LookMode.Deep，骑手存为 `<li Class="Pawn">`）；`fireAtWill`（bool，默认 `Props.turretFireAtWillDefault`，**取决于 Def**）；`physiologicalTickCounter`、`safetyTickCounter`、`turretTickCounter`、`riderMeleeTickCounter`、`turretBurstCooldownTicksLeft`（int 0）；`turretAimTarget`（TargetInfo）；`turretAimTicksLeft`、`turretAimTicksTotal`（int 0）；`turretCastStartTick`（-1）；`turretLastAttackedTarget`（TargetInfo）；`turretLastAttackTargetTick`（0） | | | | `S16/Mounting/Comp_MugirlMount.Storage.cs:8-27`，13 个标签相同 | `MakeContainer` 从 PostLoadInit 提前到所有非 Saving 模式 |
| CompLanceCharge | `F/Lances/Charge/CompLanceCharge.cs:20-25` | 武器的 thing 节点（武器可能在装备栏、轮盘储备、骑手装备、地面） | `pointCooldownUntilTick`=0、`lineCooldownUntilTick`=0 | Values | int | 0 | `S16/Lances/CompLanceCharge.cs:49-50` | 无 |
| PawnFlyer_LanceCharge（Thing） | `F/Lances/Flight/PawnFlyer_LanceCharge.cs:64-82` | 自身的 thing 节点。原版 PawnFlyer 另写 `startVec`、`destCell`、`flightDistance`、`pawnWasDrafted`、`pawnCanFireAtWill`、`ticksFlightTime`、`ticksFlying`、`flightEffecterDef`、`soundLanding`、`triggeringAbility`、`carriedThing`、`innerContainer`、`jobQueue`、`target`（rw17 已核实，与本类标签没有冲突） | `lineCharge`=false、`steamTrail`=false、`pointTarget`（References Pawn）、`pointImpactApplied`=false、`smokeTick`=0、`cameraShakeTick`=8、`hitPawns`（Collections Reference，`List<Pawn>`）、`wallHitPointsRemaining`=1550、`lastPassableCell`=IntVec3.Invalid | | | | `S16/Lances/PawnFlyer_LanceCharge.cs:286-296` | 无 |
| Comp_WeaponWheel | `F/WeaponWheel/Core/Comp_WeaponWheel.cs:77-94` | pawn 的 thing 节点 | `weaponWheelReserveWeapons`（Deep，`ThingOwner<ThingWithComps>`）、`weaponWheelSlots`（Collections Reference，`List<ThingWithComps?>`）、`weaponWheelUnlockedSlots`（Collections Value，`List<bool>`）、`weaponWheelSwordDanceFailureStreak`（int 0，字段在 `SwordDance/Comp_WeaponWheel.SwordDance.cs:20`） | | | | `S16/WeaponWheel/Comp_WeaponWheel.cs:92-95` | 无 |

**没有存档状态的类型**：CompAutoloadingSystem、CompAdultContentControl、Comp_DevUnlockSlaveApparel、CompSlaveApparelKey、CompTargetCrackBondageGear、CompGetSlaveApparelGear、CompUnlockBondageGear、Comp_AdvancedSlaveApparel（基类）、所有 ThoughtWorker、JobGiver、ThinkNode、FloatMenuProvider、PawnRenderNode/Worker、ITab、DefModExtension。

**写在 Job 里的约定**（存在原版 Job 节点中）：`Job_LoadWeaponWheel` 的 `count` = 槽位+1（`F/WeaponWheel/AI/WeaponWheelJobs.cs:9-16`）；Lance Job 的 `count` = 1 表示已经发射（`F/Lances/Charge/JobDriver_LanceCharge.cs:47-49,78`）。

### B3 按名保存的枚举
本模块**没有**。运行期专用的枚举可以随意改名：`WeaponWheelCombatState`（`F/WeaponWheel/Combat/WeaponWheelCombatState.cs:3` 注明不存档）、`WeaponWheelTransferMode`、`KeyUnlockResult`、`ForceUnlockRestraintTier`、`RestraintPawnType`。

### B4 Def 与引用目标
- **Scribe_Defs**：`ActiveBrainwashPerformance.sourceHediffDef`（HediffDef `Mugirl_BrainWashing_Level*`）；`HediffComp_EleShock.filthTimers` 的键（filth 的 ThingDef，来自 XML 的 `filthSpawnEntries`）。
- **Scribe_References**：`ActiveBrainwashPerformance.pawn`、`PawnFlyer_LanceCharge.pointTarget`/`hitPawns`、`Comp_WeaponWheel.slots`（指向储备容器中的武器或主武器）、原版 `CompTargetable.selectedTarget`。
- **Scribe_TargetInfo**：`Comp_MugirlMount.turretAimTarget`、`turretLastAttackedTarget`。
- **Deep 容器**：骑手（`Comp_MugirlMount.innerContainer`）、储备武器（`Comp_WeaponWheel.reserveWeapons`）。

### B5 会出现在存档中的本模块 defName
- **JobDef**（出现在 `curJob/def`、`jobQueue`、飞行器 `jobQueue`）：`JobDriver_RopeMoo`、`JobDriver_RemoveRopeMoo`、`GiveSlaveApparelGear`（从未入档）、`UnlockSlaveApparel`、`ForceUnlockSlaveApparel`、`CrackBondageGear`、`RopeToBuild`、`Job_FollowRoper`、`Job_MountMugirl`、`Job_DismountMugirl`、`Job_MugirlLancePointCharge`、`Job_MugirlLanceLineCharge`、`Job_LoadWeaponWheel`。1.6 中的名字完全相同。
- **ThingDef**：`WallRopeHitch`（**没有前缀**；另有自动生成的 `Blueprint_WallRopeHitch`/`Frame_WallRopeHitch`）、`Mugirl_LanceChargeFlyer`、`Mugirl_AutoloadingSystem`、束具与钥匙 `Mugirl_*`（Blindfold、Gagstick、Earplugs、SlaveApparelKey_Medieval/Industrial/Spacer/Advance、Slavegauntlet、Slavestockings、Boxbinder、Handcuffs、Armbinder、Straitjacket、Slaveboots、Slaveshackles、MagneticShackles、ShockCollar、BrainwashHelmet）。
- **HediffDef**：`Mugirl_BrainWashing_Level1-5`、`Mugirl_EleShock`、`Mugirl_PowerEleShock`、`Mugirl_PowerEleShock_NoMilk`、`Mugirl_MagneticShacklesBondage` 与 bondage 系列。**ThoughtDef**：`Mugirl_Roper`、`Mugirl_Roped`。**TraitDef**：`Mugirl_BrainWashObey`。**ApparelLayerDef**：`Mugirl_*_bondage`。

---

## C. 命名 / 架构问题

### C1 推断出的约定
依据：仓库内质量最好的代码（`F/Lances`、`F/WeaponWheel` 的 Core/Combat/Rendering）、ADR-004 中的“Preferred New Namespaces”，以及原版惯例。
1. 命名空间为 `Mugirl.Features.<Module>`。子文件夹只用来组织文件，不进入命名空间（WeaponWheel 的 Core/Combat/Rendering 下的文件都是 `Mugirl.Features.WeaponWheel`）。
2. ThingComp 用 `CompX` + `CompProperties_X`（原版：`CompRefuelable`/`CompProperties_Refuelable`）。HediffComp 用 `HediffComp_X` + `HediffCompProperties_X`。CompTargetEffect 用 `CompTargetEffect_X` + `CompProperties_TargetEffectX`（原版 `CompTargetEffect_Resurrect`）。CompUseEffect 用 `CompUseEffect_X`。`JobDriver_`、`JobGiver_`、`ThinkNode_`、`ThoughtWorker_`、`PawnFlyer_`、`PawnRenderNode_` 保留原版的下划线前缀。
3. 一个顶层类型一个文件，文件名等于类型名；partial 文件命名为 `Type.Part.cs`。
4. 命名空间已经表达了“Mugirl”，类型名里不再重复，除非指的是雪牛娘这个实体（例如 `JobDriver_MountMugirl` 表示“骑上雪牛娘”）。
5. 类型短名不与原版重名。
6. defName 一律加 `Mugirl_` 前缀（Corporate、Courier 已经这样做，例如 `Mugirl_CorporateUseComms`）。
7. Scribe 标签用 camelCase。写在共享节点（Pawn、Apparel、武器的 thing 节点，以及 hediff 节点）上的 comp 标签，加 comp 级前缀。现有的好例子是 Comp_WeaponWheel 的 `weaponWheel*`。
8. 补丁类：命名空间表达模块，类名表达目标，统一为 `<TargetType>_<Method>_Patch`；同一目标在同一模块有多个补丁类时，再加 `_<Purpose>`。本模块目前有 4 种写法：`Harmony_<Module>_*`、`*_Patch`、`Patch_*`，以及 `Harmony_Patch_*`。执行顺序必须用显式 attribute 表达（E5）。

### C2 命名空间现状（不符合约定 1 的文件数 / 总文件数）
- Restraints：46/56 在根命名空间 `Mugirl`。
- Roping：23/26。
- Mounting：26/32。`MountedCombatController` 在根命名空间，而同一文件夹的 `MountedWarmupOverride`、`VanillaMeleeAccess`、`RiderTickDriver`、`DismountCellFinder`、`MountDrawOffsets`、`MountedRiderGizmos` 在 `Mugirl.Features.Mounting`。
- Lances：1/16（`F/Lances/Charge/Harmony_LanceGizmos.cs:4`）。
- WeaponWheel：9/39。`Comp_WeaponWheel` 在 `Features.WeaponWheel`，而它的 `CompProperties_WeaponWheel`、`CompProperties_AutoloadingSystem`、`CompAutoloadingSystem`、`JobDriver_LoadWeaponWheel`、`WeaponWheelHarmonyUtility` 和 4 个补丁文件在根命名空间。
- Weapons：2/2。

ADR-004（`docs/architecture/ADR-004-root-namespace-markers.md`）和 `README.md:63`（“XML 类名、Scribe 字段、根命名空间及补丁 XPath 不能随意改名”）是冻结规则的出处，需要在决策后被取代。

### C3 改名表（类型）

“存档可见”：类型全名写进存档，需要 (a)。“XML 可见”：Defs、Patches 或 DefInjected 中引用了这个名字。

**Restraints**：建议统一术语为 “Restraint”，与文件夹名、英文 UI（“Rough Restraint Key”“Unlock Restraint”）和中文“束具”一致。现在同一概念有 SlaveApparel、BondageGear、SlaveApparelGear 三套词，而且这些物品也会被殖民者和囚犯穿戴，“Slave” 并不准确。如果所有者不想改术语，最小方案是只移动命名空间，其余行保持不变。

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| Thing | `Mugirl.SlaveApparel` | `Mugirl.Features.Restraints.RestraintApparel` | 统一术语，移出根命名空间 | 是 | 是（thingClass） |
| Thing | `Mugirl.AdvancedSlaveApparel` | `Mugirl.Features.Restraints.AdvancedRestraintApparel` | 同上 | 是 | 是 |
| Thing | `Mugirl.BrainWashSlaveApparel` | 删除，用 `parent.HasComp<CompBrainwashHelmet>()` 判定。若保留则命名为 `BrainwashHelmetApparel` | 空标记类，只被 3 处用于类型判断；“BrainWash” 的大小写也与 “Brainwash” 不一致 | 是：(a) 映射到 AdvancedRestraintApparel | 是 |
| Def | `Mugirl.SlaveApparelDef`（ThingDef 子类） | 改为 `DefModExtension RestraintExtension`，字段用 camelCase：`equippedHediff`、`keyCount`、`keyDef`、`unlockTicks`、`nextStageDefs`、`hediffBodyParts`、`boundBodyPartGroups`、`blocksHands` | 作为 ThingDef 子类，它的 XML 根元素是 `<Mugirl.SlaveApparelDef>`，第三方 `Defs/ThingDef[...]` 的 XPath 补丁（CE 等）匹配不到；现有字段混用 snake_case 与 PascalCase（`F/Restraints/Apparel/SlaveApparelDef.cs:7-26`） | 否，存档只存 defName | 是（21 个 Def 的根元素） |
| comp | `Comp_AdvancedSlaveApparel` | `CompAdvancedRestraint`（abstract） | 约定 2 | 否 | 否 |
| comp | `Comp_BrainwashHelmet` | `CompBrainwashHelmet` | 约定 2 | 否 | DefInjected 4 处 |
| comp | `Comp_MagneticShackles` | `CompMagneticShackles` | 约定 2 | 否 | DefInjected 20 处 |
| comp | `Comp_ShockCollar` | `CompShockCollar` | 约定 2 | 否 | DefInjected 8 处 |
| comp | `Comp_DevUnlockSlaveApparel` | `CompDevUnlockRestraint` | 约定 2 | 否 | 经由 props |
| comp | `CompSlaveApparelKey` + `CompProperties_SlaveApparelKey` | `CompRestraintKey` + `CompProperties_RestraintKey` | 术语 | 否 | 是 |
| comp | `CompStampedApparelKey` | `CompUsable_RestraintKey` | “Stamped” 没有含义；它其实是钥匙的 CompUsable | 否 | compClass + DefInjected 6 处 |
| comp | `CompUnlockBondageGear` | `CompUseEffect_UnlockRestraint` | 原版 `CompUseEffect_X` 约定 | 否 | 是 |
| comp | `CompTargetCrackBondageGear` + `CompProperties_TargetEffectCrackBondageGear` | `CompTargetEffect_CrackRestraint` + `CompProperties_TargetEffectCrackRestraint` | 原版约定；现在 props 与 comp 的名字对不上 | 否 | 是 |
| comp | `CompTargetable_SlaveApparel` | `CompTargetable_CrackableRestraint` | 术语，并说明作用 | 否 | 是 |
| comp | `CompGetSlaveApparelGear`；`CompAdultContentControl` 及相关类型 | 删除（A7、A6） | 死代码 | 否 | 是 |
| HediffComp props | `CompProperties_AddMentalState`、`CompProperties_AddThought`、`CompProperties_AddTrait`、`CompProperties_ReduceWillorEnslave`、`CompProperties_SuppressionEnhancer`、`CompProperties_EleShock`、`CompProperties_DisappearsAndAddHediffs`、`CompProperties_PerformanceEffect` | `HediffCompProperties_AddMentalState`、`_AddThought`、`_AddTrait`、`_ReduceWillOrEnslave`、`_SuppressionEnhancer`、`_ElectricShock`、`_DisappearsAndAddHediff`、`_BrainwashPerformance` | 原版的 `HediffCompProperties_` 前缀；“Willor” 大小写错误；“EleShock” 是缩写；文件名（`HediffCompProperties_*.cs`、复数的 `…AddThoughts.cs`）与类名不一致 | 否 | 是（HediffDefs 中的 `Class=`） |
| HediffComp | `HediffComp_EleShock`、`HediffComp_ReduceWillorEnslave` | `HediffComp_ElectricShock`、`HediffComp_ReduceWillOrEnslave` | 同上 | 否 | 经由 props |
| HediffComp | `HediffComp_BrainWashingStar` | `HediffComp_BrainwashPerformance` | “Star” 没有含义，与 props 名不一致，大小写也不统一 | 否 | **DefInjected 78 处** |
| GameComponent | `Mugirl.GameComponent_BrainwashPerformance` | `Mugirl.Features.Restraints.GameComponent_BrainwashPerformance` | 只移命名空间（短名本身符合原版约定）；同时删除未使用的无参构造（`:8-10`）——`Game.ExposeSmallComponents` 会把 `this` 作为构造参数传入（rw17 已核实） | 是 | 否 |
| JobDriver | `Mugirl.JobDriver_CrackBondageGear` | `Mugirl.Features.Restraints.JobDriver_CrackRestraint` | 术语 | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_UnlockSlaveApparelGear` | `…JobDriver_UnlockRestraint` | 术语 | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_ForceUnlockSlaveApparel` | `…JobDriver_ForceUnlockRestraint` | 术语 | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_UseItemOn` | 删除（A7） | 死代码 | 名义上是，实际从未入档 | 是 |
| 方法 | `AdvancedSlaveApparel.IsCracked()` | 属性 `IsCracked` | 惯例 | 否 | 否 |
| 方法 | `SlaveApparelExtensions.IsUnlockAdvancedApparel` | `IsCrackedAdvancedRestraint` | 名称误导（A18） | 否 | 否 |
| static | `PawnSlaveStatusUtility`（只有一个 `DisplayName`；字面量 `"null"`/`"noname"` 没有翻译） | 合并为 `KeyUnlockFloatMenu` 的私有方法，缺名时使用已翻译的回退文本 | 名不副实 | 否 | 否 |

**Roping**：“Moo” 是 MooGirl 时代的残留，与 Mugirl 混用。

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| JobDriver | `Mugirl.JobDriver_RopeMoo` | `Mugirl.Features.Roping.JobDriver_RopeMugirl` | 去掉 Moo | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_RemoveRopeMoo` | `…JobDriver_UnropeMugirl` | 同上 | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_RopeToWallRopeHitch` | `…JobDriver_RopeToHitch` | 它也用于 CaravanPackingSpot（`1.7/Patches/MugirlPatch.xml:37-50`），现在的名字过窄 | 是 | 是 |
| JobDriver | `Mugirl.JobDriver_FollowRoper` | `…JobDriver_MugirlFollowRoper` | 与 `Verse.AI.JobDriver_FollowRoper` 短名相同，而且是它的子类 | 是 | 是 |
| JobGiver | `Mugirl.JobGiver_FollowRoper` | `…JobGiver_MugirlFollowRoper` | 与 `RimWorld.JobGiver_FollowRoper` 短名冲突（rw17 已核实） | 否 | 是（ThinkTree） |
| JobGiver | `Mugirl.JobGiver_WanderRoped`（`F/Roping/AI/JobGiver_WanderRoped.cs:5-29`） | 删除；XML 改用原版 `JobGiver_WanderRoped`，并写 `<wanderRadius>4</wanderRadius><ticksBetweenWandersRange>500~800</ticksBetweenWandersRange>` | 与原版同名、逻辑相同，只是构造参数不同；它位于 ConditionalRoped 之下，原版缺少的 null 检查在这里不需要 | 否 | 是 |
| ThinkNode | `Mugirl.ThinkNode_ConditionalRoped`（`F/Roping/AI/ThinkNode_ConditionalRoped.cs:5-12`） | 删除；XML 改用 `ThinkNode_ConditionalRoped`（RimWorld） | 与原版逐字相同（rw17 核实，只多了 `pawn?.`） | 否 | 是（`D17/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml:10`） |
| comp | `CompRopeToBuild` | `CompUsable_RopeToHitch` | “Build” 误导，目标其实是绳桩或打包点 | 否 | compClass 2 处 + DefInjected 4 处 |
| MapComponent | `Mugirl.MapRopingIndex` | 删除（A8） | 没有数据的 MapComponent | 是：(e) 删除节点 | 否 |
| static | `Toils_RopeMoo`、`FloatMenuProvider_RopeMoo` | `RopingToils`、`FloatMenuProvider_RopeMugirl` | 去掉 Moo | 否 | 否 |
| 补丁 | `Harmony_Patch_RopingDraw`、`Patch_RopingTick`、`Patch_FoodUtility_IsAcceptablePreyFor_Mugirl` 等 | 按约定 8 统一 | 现有 3 种写法 | 否 | 否（改 patch-targets 基线） |

**Mounting**

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| comp | `Mugirl.Comp_MugirlMount` | `Mugirl.Features.Mounting.CompMount` | 约定 2 与 4 | 否 | 否（XML 只写 props） |
| props | `Mugirl.CompProperties_MugirlMount` | `Mugirl.Features.Mounting.CompProperties_Mount` | 同上 | 否 | 是（`Mugirl_Race.xml:1054`） |
| JobDriver | `Mugirl.JobDriver_MountMugirl`、`Mugirl.JobDriver_DismountMugirl` | `Mugirl.Features.Mounting.*`，短名不变 | 只移命名空间 | 是 | 是 |
| static | `Mugirl.MountedCombatController`（partial，横跨 Combat/ 与 Rendering/） | `Mugirl.Features.Mounting.MountedCombatController` | 命名空间 | 否 | 否（Compatibility 有引用） |
| static | `Mugirl.MountedPawnMeleeSupport` | `Mugirl.Features.Mounting.MountedMeleeSupport` | 命名空间 | 否 | 否，但 MeleeAnimation 兼容层按方法名打补丁（`Source/MugirlRace/Compatibility/MeleeAnimation/MeleeAnimationPatches.cs:64`） |
| 成员 | `Comp_MugirlMount.MooPawn`、`MountedPawn`、`HasMountedPawn`；JobDriver 中的 `MooInd` | `Carrier`、`Rider`、`HasRider`、`CarrierInd` | 去掉 Moo；局部变量已经叫 carrier/rider（例如 `Comp_MugirlMount.cs:75`） | 否 | 否 |
| static | `MountedPawnUtility.IsMugirl`、`IsHumanlike`、`HasCapacity`、`IsAwake` | 删除别名；通用谓词移到 Core | 职责混杂 | 否 | 否 |
| 补丁 | `Harmony_MountedCombatController_*`、`Harmony_MountedPawnGizmos`、`Harmony_MountRendering` | 移命名空间，按约定 8 命名 | — | 否 | 否 |

**Lances**：基本合规。`Mugirl.Harmony_LanceGizmos` 改为 `Mugirl.Features.Lances.Pawn_EquipmentTracker_GetGizmos_Patch`（否/否）。

**WeaponWheel**

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| comp | `Mugirl.Features.WeaponWheel.Comp_WeaponWheel` | `CompWeaponWheel` | 约定 2 | 否 | 否 |
| props | `Mugirl.CompProperties_WeaponWheel` | `Mugirl.Features.WeaponWheel.CompProperties_WeaponWheel` | 与 comp 的命名空间不一致 | 否 | 是（`Mugirl_Race.xml:1040`） |
| props/comp | `Mugirl.CompProperties_AutoloadingSystem`、`Mugirl.CompAutoloadingSystem` | `Mugirl.Features.WeaponWheel.*` | 命名空间 | 否 | 是（`D17/Apparel/Apparel_WeaponWheel.xml:55`） |
| JobDriver | `Mugirl.JobDriver_LoadWeaponWheel` | `Mugirl.Features.WeaponWheel.JobDriver_LoadWeaponWheel` | 命名空间 | 是 | 是 |
| 补丁 | `Harmony_WeaponWheel_TryGetAttackVerb_Diagnostics` | `Pawn_TryGetAttackVerb_Patch` | **名称误导**：它会改写 `__result`（`F/WeaponWheel/Patches/Harmony_WeaponWheel_Verbs.cs:95-127`），不只是诊断 | 否 | 否 |
| static | `Mugirl.WeaponWheelHarmonyUtility` | `Mugirl.Features.WeaponWheel.WeaponWheelLookup` | 不是 Harmony 工具，而是组件查找 | 否 | 否 |

**Weapons**

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| 扩展 | `Mugirl.MugirlRaceRestrictedExtension`，带 `enabled` 字段 | `Mugirl.Features.Weapons.MugirlOnlyEquipmentExtension`，删除 `enabled` | 扩展本身存在就是标记，`enabled` 字段多余；同时移命名空间 | 否 | 是（`D17/ThingDefs_Weapon/MeleeWeapons_0920.xml` 中 2 处） |
| 补丁 | `Mugirl.Harmony_MugirlRaceRestrictedWeapons` | `Mugirl.Features.Weapons.EquipmentUtility_CanEquip_Patch` | 约定 8 | 否 | 否 |

### C4 Scribe 标签建议（升级器 (c)/(e)）
- **Comp_MugirlMount**：它写在 Pawn 节点上，与几十个 comp 共用，而 `innerContainer`、`fireAtWill` 这类标签太通用。
  - `innerContainer` 改为 `mountRiders`，`fireAtWill` 改为 `mountFireAtWill`。
  - 4 个 tick 计数器删除，改用 `IsHashIntervalTick`，升级器 (g) 删除对应节点。
  - 8 个 `turret*` 字段封装进 `MountedTurretState : IExposable`，存为 `<mountTurret>` 子节点。升级器需要 (e) 把节点移入子节点，并 (c) 去掉 `turret` 前缀。
- **Comp_ShockCollar**：`ticks` 删除，改为 IsHashIntervalTick，或保存绝对 tick `nextShockTick`（需要 (e) 把剩余 tick 换算成绝对 tick，或者直接丢弃）。`lastManualUseTick` 的默认值与其他两个 comp 统一为 -999999。按 E1，只有存档值恰好等于 -99999 的节点会受影响，两个值都表示“很久以前”，语义无害。
- **延迟触发系列**：建议把 1.7 树已经使用的 `mugirl*` 标签定为最终标签，这样只有 1.6 来源的存档需要 A1 的变换。
- **默认值取决于 Def 的标签（E1 隐患）**：`SlaveApparel.lockCount` 的默认值是 `def.needkeynumber`（`SlaveApparel.cs:49`），`Comp_MugirlMount.fireAtWill` 的默认值是 `Props.turretFireAtWillDefault`（`Comp_MugirlMount.Storage.cs:16`）。一旦 XML 改了这些默认值，旧存档中没写节点的实例就会悄悄改变状态。建议用 `forceSave:true`。升级器 (e) 需要按存档时的语义把缺失节点物化：缺失表示“等于当时的 Def 值”；若 XML 从那以后没改过，用当前 Def 值物化就是精确的。

### C5 defName 建议（升级器 (b)）

| 种类 | 旧 | 新 | 理由 |
|---|---|---|---|
| JobDef | `JobDriver_RopeMoo` | `Mugirl_RopeMugirl` | defName 写成了驱动类名 |
| JobDef | `JobDriver_RemoveRopeMoo` | `Mugirl_UnropeMugirl` | 同上 |
| JobDef | `GiveSlaveApparelGear` | 删除 | A7 |
| JobDef | `UnlockSlaveApparel` | `Mugirl_UnlockRestraint` | 没有前缀，可能与其他 Mod 冲突 |
| JobDef | `ForceUnlockSlaveApparel` | `Mugirl_ForceUnlockRestraint` | 同上 |
| JobDef | `CrackBondageGear` | `Mugirl_CrackRestraint` | 同上 |
| JobDef | `RopeToBuild` | `Mugirl_RopeToHitch` | 同上，并且名称不准确 |
| JobDef | `Job_FollowRoper` | `Mugirl_FollowRoper` | `Job_` 前缀与模组其他 JobDef 不一致 |
| JobDef | `Job_MountMugirl`、`Job_DismountMugirl` | `Mugirl_Mount`、`Mugirl_Dismount` | 同上 |
| JobDef | `Job_MugirlLancePointCharge`、`Job_MugirlLanceLineCharge` | `Mugirl_LancePointCharge`、`Mugirl_LanceLineCharge` | 同上 |
| JobDef | `Job_LoadWeaponWheel` | `Mugirl_LoadWeaponWheel` | 同上 |
| ThingDef | `WallRopeHitch` | `Mugirl_WallRopeHitch` | 没有前缀。映射要同时包含 `Blueprint_`/`Frame_` 前缀的自动生成 Def |
| ThingDef | `Mugirl_SlaveApparelKey_Advance`、`Mugirl_Slavegauntlet/Slavestockings/Slaveboots/Slaveshackles` | `…_Advanced`、`Mugirl_SlaveGauntlet` 等（可选，价值低） | 拼写和大小写 |

每个 JobDef 改名都要同步以下位置：
- `Source/MugirlRace/DefOf/Mugirl_DefOf.cs` 中的字段（`:8`、`:36-65`）
- XML 里的 `useJob`（`BondageApparel_Key.xml:29,137`、`Building_Rope.xml:60`、`Patches/MugirlPatch.xml:44`）
- `L17/*/DefInjected/JobDef/*.xml`（两种语言）

`WallRopeHitch` 改名还要同步：
- `D17/QuestScriptDefs/CorporateFusionResearchSite.xml:483`
- `L17/ChineseSimplified/DefInjected/ThingDef/Building_Rope.xml`

顺带发现一处翻译错误：中文 `RopeToBuild.reportString` 写成了“解锁到建筑”。

### C6 死代码清单（已核实）
- A6 和 A7 中的全部条目。
- `Mugirl.ThinkNode_ConditionalRoped`、`Mugirl.JobGiver_WanderRoped`：与原版重复。
- `MountedPawnUtility.IsMugirl`：别名。
- `GameComponent_BrainwashPerformance()` 无参构造（`:8-10`）。
- `CompProperties_MagneticShackles.activateSoundDefName` 是字符串，运行时手动解析（`:35-75`），应直接声明为 `SoundDef? activateSound`，交给 XML 加载器解析和校验。
- `CompProperties_AddTrait.PawnTypeTraitEntry.pawnType` 是字符串，运行时解析（`HediffCompProperties_AddTrait.cs:22-23`、`RestraintPawnType.cs`），应改为 XML 中直接写的枚举字段。

### C7 生产级结构建议
1. **用显式扩展点代替“补丁自己”**：Compatibility 用 Harmony 给本模块的方法打补丁（`Compatibility/DualWield/DualWieldPatches.cs:7`、`Compatibility/MeleeAnimation/MeleeAnimationPatches.cs:16-17,64` 等）。结果是这些方法的签名和参数名被冻结，还要加 `[MethodImpl(NoInlining)]`（`F/Mounting/Combat/MountedPawnMeleeSupport.cs:136-139`），代码里到处是“改签名前同步兼容层”的注释（`Comp_WeaponWheel.Combat.cs:38,78`、`Comp_WeaponWheel.Storage.cs:112,248,300,381`、`WeaponWheelAnimationRenderer.cs:9`）。建议改为模块公开的钩子（委托或注册表，例如 `WeaponWheelHooks.FullFirepowerVetoes`），兼容层只负责注册。
2. **补丁顺序显式化**（E5）：类名排序不能作为执行顺序的依据。
3. **瞬时静态状态集中管理生命周期**：`MountedCombatController.ResetTransientState` 由无关的 `MugirlStoryState` 调用（`Features/Incidents/Story/MugirlStoryState.cs:77-79`）。`MapRopingIndex.totalPending` 换局时不清零（`F/Roping/MapRopingIndex.cs:9-11`）。应改为在 Core 中设一个统一的 reset 注册表，在开局和读档时调用。
4. **骑乘炮塔状态封装**：静态控制器直接写 comp 的 `internal` 字段（`Comp_MugirlMount.cs:17-23`、`MountedCombatController.Lifecycle.cs:18-20`）。应抽出 `MountedTurretState`，配合 C4 的子节点存档。
5. **拆分 Comp_WeaponWheel**：Core 444 + Storage 619 + Combat 879 + SwordDance 539 + Rendering 97，共 2,578 行，靠 partial 合成一个类。建议拆成由 comp 持有的储存、战斗状态机、剑舞、渲染缓存四个协作对象，comp 只作门面。
6. 把 `SlaveApparelDef` 改为 DefModExtension（见 C3）。
7. **减少存档面**：用于计时的 tick 计数器（Comp_MugirlMount 的 4 个、Comp_ShockCollar 的 `ticks`）改为 hash interval；`lockCount`、`fireAtWill` 用 forceSave。
8. **文件组织**：`CompProperties_*.cs` 文件里实际装的是近 300 行的运行时 comp（例如 `CompProperties_BrainwashHelmet.cs` 里的 `Comp_BrainwashHelmet`）；`HediffComps/HediffCompProperties_*.cs` 文件名与类名 `CompProperties_*` 不一致；`AdultContentControl.cs` 含 3 个类型；`ApparelTrackerPatches.cs` 含 5 个补丁类。至少应让文件名等于主运行时类型名。
9. 写在 CompProperties 字符串字段里的 Keyed 键（`CompProperties_BrainwashHelmet.cs:10-12`、`CompProperties_MagneticShackles.cs:16-31`、`CompProperties_ShockCollar.cs:19-24`）可以工作，但与原版“XML 写已本地化文本、用 DefInjected 翻译”的惯例不同。若要统一可以一起改，优先级低。
10. 仅供开发者使用的覆盖文件在正式版中也会读取（A17）。Dev 工具（`F/WeaponWheel/Dev/`，749 行）编译进正式 DLL，目前有 DevMode 或设置开关控制，可以接受。

### C8 跨模块影响（改名时会波及）
- **Incidents**：信使任务读取 `SlaveApparel.lockCount`/`isLocked`（`SlaveApparel.cs:6-7`）；`Features/Incidents/RunawayFarm/QuestPart_RunawayFarmCompletion.cs:253` 调用 `RopingService.ClearPendingSpotRope`。
- **Story**：`MugirlStoryState.cs:78` 调用 `MountedCombatController.ResetTransientState`。
- **Compatibility**：DualWield、MeleeAnimation、RunAndGun、YayoAnimation、YayoCombat 的补丁直接引用本模块的类型和方法（见 C7.1）。
- **DefOf**：`Mugirl_DefOf.cs:8,36-69,114`。
- **Corporate XML**：`CorporateFusionResearchSite.xml:483`（`WallRopeHitch`）。
- **文档与校验器**：E8 列出的校验器基线、`README.md:63`、ADR-004。

---

## D. 本模块对统一升级器的需求汇总

**来源判定**（E7）：存档 meta 中 gameVersion 为 1.6.x 时是 1.6 构建；为 1.7.x 且没有新戳记时是预重构 1.7 树。若决定支持 0.4.4 及更早的存档，先套用 E6 的 MooGirl 前缀规则。

**(a) 类型映射**：旧全名在 1.6 与 1.7 树中相同。
- 按 C3 取舍后的 JobDriver 共 11 个（`JobDriver_UseItemOn` 已删除）
- 3 个 Apparel thingClass
- `GameComponent_BrainwashPerformance`
- `PawnFlyer_LanceCharge` 与 `JobDriver_LanceCharge`：只有改名时才需要
- 实现方式：对 `BackCompatibility.GetBackCompatibleType` 加 Postfix（迁移指南“存档中的类型名”）

**(b) defName 映射**：C5 中的 JobDef，以及 `WallRopeHitch` 及其 Blueprint_/Frame_ 变体。

**(c) 标签改名**：
- 1.6 来源：A1 中的 9 个标签（通过 (e) 复制首值实现）
- 两种来源都需要：C4 中 Comp_MugirlMount 和 Comp_ShockCollar 的标签

**(e) XML 变换**，在 Scribe 实例化之前对原始读档树执行：
1. A1：洗脑 Hediff 的首值复制。
2. A2：已破解的高级束具写入解锁状态。
3. A8：删除 `Mugirl.MapRopingIndex` 组件节点。
4. A12：给飞行器补 `<hitPawns/>`。
5. C4：turret 字段移入子节点；把默认值取决于 Def 的缺失节点物化（`lockCount`、`fireAtWill`）。
6. 可选：删除 A5 中的废弃节点。

**(f) 读档后步骤**，在 `LoadedGame` 时执行（PostLoadInit 之后、第一次 tick 之前）：
1. A3：修复 `lockCount`。
2. A4：一次性补全原版 `lockedApparel`。
3. A11：只针对 1.6 来源，取消进行中的 `UnlockSlaveApparel`/`CrackBondageGear` Job。

**(g) 直接删除**：A5、A6、A7、A13（保留为不变式守卫）、A14–A18。

**(d) 枚举**：不需要（B3）。
