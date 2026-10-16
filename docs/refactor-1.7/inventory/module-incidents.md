# 模块审计：Incidents / Newborn / Misc（存档升级层输入）

范围：`Source/MugirlRace/Features/Incidents/**`（Courier、FusionInvestment、Migration、OpeningCrash、Punisher、Raids、RefugeePod、RunawayFarm、Shared、Story、Trade、VanillaFixes、Wanderer、WildSlaves）、`Features/Newborn`、`Features/Misc/**`（FixedColorApparel、Grazing、MilkRecipe、Nuzzle、Pregnancy、RenderRefresh、Thoughts）。共 90 个 .cs 文件，7649 行。

下文路径无前缀时都相对于 `Source/MugirlRace/Features/`。冻结的 1.6 源码记为 `1.6/Source/...`。原版行为用 DecompilerServer 的 `rw17-game`（1.7 的 Assembly-CSharp）核对。历史版本用 `git log -S` 核对。所有行号都在当前工作区上逐行读过。

## 0. 先说结论

- **存档里能看到的类型名（14 个）**：`Mugirl.MugirlStoryState`、`Mugirl.GameComponent_GhoulRenderingRefresh`（GameComponent）；`Mugirl.MapComponent_MugirlMigration`、`Mugirl.MapComponent_MugirlPunisher`（MapComponent）；`Mugirl.QuestPart_SpawnCourier`、`Mugirl.QuestPart_MugirlRescueJoin`、`Mugirl.QuestPart_SpawnRunawayFarm`、`Mugirl.QuestPart_RunawayFarmCompletion`（QuestPart）；`Mugirl.ChoiceLetter_MugirlFusionInvestment`（Letter）；`Mugirl.JobDriver_ReadCourierDiary`、`Mugirl.JobDriver_TalkCourier`、`Mugirl.JobDriver_TalkFusionInvestor`、`Mugirl.JobDriver_Nuzzle`（JobDriver）；`Mugirl.Thought_MugirlOnly`（Thought）。这些类名与 1.6 完全相同。1.7 只新增了两个 Scribe 标签 `punisherHeldLoot`、`punisherLootBursts`，以及两个不写类名的 IExposable（`MugirlPunisherLootBurst`、它的嵌套类 `DebrisArc`）。
- **只为旧档或旧版本存在的代码：22 处**，见 A 节。最重的几处：
  - 每次读档都执行三遍全 Pawn 扫描：`ClearPlayerMigrationPawns`、`NormalizeLoadedPlayerPawnKinds`、`NormalizeLoadedPawns`。
  - 迁徙组件自带 `dataVersion` 升级。
  - 两个只供旧角色解析的 PawnKind，加上一套退役事件 Def 与空 Worker。
  - 一个从未被创建过的 `ChoiceLetter`。
  - 每次读档都重新补“草”食物方案；每个怀孕心情 Comp 读档时都重新转换。
- **升级层下限**：0.4.7（commit 324501c，2026-07-03）之前命名空间是 `MooGirl`、defName 是 `MooGirl_*`。当前 1.6 与 1.7 源码中没有任何 `MooGirl` 处理，`grep -i moogirl` 为空，所以这些存档现在就已经读不了。建议把升级层下限明确定为 0.4.7 及以后。
- **存档来源有两条 1.6 线要同时覆盖**：
  - HEAD（50f7979，0.6.7 已提交）。野人闯入事件 `Mugirl_WildManWandersIn` 还在运行，会生成 `Mugirl_EscapeWildSlave`。
  - 冻结的 1.6 工作区构建（2026-10-04，未提交）。这个事件已退役（`docs/records/event-normalization-2026-10-04.md:20`）。
  两条线都会写 `Mugirl_RunawayReward` 标签（`1.6/Source/Features/Incidents/QuestPart_RunawayFarm.cs:752`）。

---

## A. 旧档与旧版本兼容代码

升级层分类：
- (a) 类型改名映射
- (b) defName 改名映射
- (c) 有类型归属的 Scribe 标签改名
- (d) 枚举值改名
- (e) 存档 XML 结构变换
- (f) 需要活的游戏对象的读档后语义步骤
- (g) 什么都不需要，可直接删除

与升级层设计相关的原版事实，都已在 1.7 反编译中核对：

- 找不到类型时，`ScribeExtractor.SaveableFromNode` 调用 `GetBestFallbackType`：
  - Thing、Hediff、Ability、Thought 按 Def 中的类构造。
  - 其他类型回落到声明类型。QuestPart、JobDriver、GameComponent、MapComponent 和 `IArchivable` 都是抽象类型或接口，会抛异常，然后元素变成 null 被丢弃。
- **`Quest.ExposeData` 在 LoadingVars 阶段 `parts.RemoveAll(null)`**。QuestPart 的引用 ID 是 `"QuestPart_" + quest.id + "_" + Index`（`RimWorld.QuestPart.GetUniqueLoadID`）。所以只要有一个 QuestPart 类名解析失败，后面所有 Part 的索引都会错位。例如 `QuestPart_RunawayFarmCompletion.spawnPart` 的引用会解析错或失败。**QuestPart 改名必须走 (a)。**
- `ScribeExtractor.DefFromNode`：找不到 Def 时，除非 `BackCompatibility.WasDefRemoved`（public static，读私有列表 `RemovedDefs`）返回 true，否则记红字，返回 null。Dictionary 的 Def 键为 null 时，`BuildDictionary(logNullErrors: true)` 还会再报一次。
- `Pawn.ExposeData` 用 `Scribe_Defs.Look(ref kindDef, "kindDef")` 读 kindDef，Def 缺失时得到 null 加红字。删除 PawnKindDef 前必须先有 (b)。
- `Job.ExposeData` 保存 `def`、`interaction`、`jobGiverThinkTree`（Scribe_Defs），以及按 ThinkTree 结构算出的 `lastJobGiverKey`。所以 **ThinkTreeDef 的 defName 也会写进存档**。
- `Pawn_JobTracker.ExposeData` 在 `curDriver` 为 null 时只警告，然后结束当前工作。JobDriver 改名不做 (a) 也只损失当前工作。
- 结构变换 (e) 的挂点：`ScribeLoader.InitLoading(string)` 在 `xmlDocument.Load` 后把 DocumentElement 放进 `curXmlParent`，此时 Scribe 还没读任何字段。在这里加 Postfix 可以先改 XML。它也用于加载其他文件，所以要先判断根元素是 `savegame`。这一点已从源码确认，**未实测**。

### A 表

| ID | 位置 | 处理的旧形态（引入或变化的版本） | 新形态 | 升级层需要 |
|---|---|---|---|---|
| A01 | `Compatibility/Savegames/IncidentWorker_RetiredMugirlEvent.cs:5-10`（不在本范围的目录，但属于本领域）；`1.7/Defs/Compatibility/RetiredEvents.xml:4-13`；`1.7/Languages/ChineseSimplified/DefInjected/IncidentDef/Incidents_Mugirl.xml` 中保留的 `Mugirl_WildManWandersIn` 文本 | IncidentDef `Mugirl_WildManWandersIn`。0.6.7（HEAD）中它仍运行，worker 为 `Mugirl.IncidentWorker_Mugirl_WildManWandersIn`，见 `git show HEAD:1.6/Defs/Storyteller/Incidents_Mugirl.xml:4,11`。冻结的 1.6 构建把它改成零权重的空 Worker。存档中出现在 `StoryState.lastFireTicks`（Def 键字典）、`recentRandomIncidents`（Def 列表）和事件队列里 | 删除 Def、空 Worker 类和翻译 | **(e)**：读档前从 `lastFireTicks/keys+values`、`recentRandomIncidents`、事件队列中删掉 `Mugirl_WildManWandersIn` 对应的条目。不建议只 Postfix `WasDefRemoved`：字典 null 键仍会报错 |
| A02 | `Incidents/WildSlaves/MugirlWildSlaveUtility.cs:11-14`（`IsEscapeWildSlave`）、`:16-19`（`IsWildMugirl` 含 EscapeWildSlave）、`:21-27`（`HasLegacyPlayerMugirlKind`）、`:35-46`（`IsMugirlPawn` 中的 kind 列表）；`DefOf/Mugirl_DefOf.cs:124,126`；`1.7/Defs/PawnKindDefs/PawnKinds_Slave.xml:93-124`（defName 在 `:94`、`:110`；`:111` 注释“仅保留给旧存档”） | **`Mugirl_EscapeWildSlave`**：只有 A01 的野人闯入事件生成它（≤0.6.7）。1.7 中没有任何 XML 或 C# 再生成它，非玩家的此类角色仍走野人行为（`Harmony_WildSlavePatches.cs:15,36`、`ThinkNode_ConditionalNonPlayerWildMugirl.cs:10`）。**`Mugirl_PreEscapeWildSlave`**：0a6c435（2026-07-15）之前，招募的逃亡野生奴隶和它们的新生儿会被 `ChangeKind` 成这个 kind，见 `git show 0a6c435` 中删掉的 `pawn.ChangeKind(Mugirl_DefOf.Mugirl_PreEscapeWildSlave)` | 只保留 `Mugirl_WildMugirl` 一个“野生” kind。`IsMugirlPawn` 的 kind 判断多余：这三个 kind 的 race 都是 `Mugirl`（`PawnKinds_Slave.xml:5`），`MugirlIdentity.IsMugirlPawn` 已经覆盖 | **(b)** PawnKindDef `Mugirl_EscapeWildSlave`→`Mugirl_WildMugirl`，`Mugirl_PreEscapeWildSlave`→`Mugirl_WildMugirl`，然后执行 A03 的 (f)。只换 kind 不会重新生成服装；KindLabel 会从 “Escaped Wild Mugirl” 变成 “Wild Mugirl”。需要同步删除 Def、DefOf 字段和中文 DefInjected `PawnKindDef/PawnKinds_Slave.xml` 中的对应条目 |
| A03 | `Incidents/Story/MugirlStoryState.cs:48-54`（`LoadedGame`）→ `MugirlWildSlaveUtility.cs:146-160`（`NormalizeLoadedPlayerPawnKinds`）、`:162-180` | 0a6c435 之前加入玩家、但 kind 仍是野生的 Pawn。每次读档都扫一遍 `PawnsFinder.All_AliveOrDead` | 加入时的实时清理保留：`CleanupAfterJoiningPlayer` → `NormalizePlayerKindToFactionDefault`（`:86`），判定改为 `kindDef == Mugirl_WildMugirl` | **(f)** 只执行一次：玩家派系、人形、非任务寄宿者、kind 为 `Mugirl_WildMugirl`（已含 A02 映射过来的）时，`ChangeKind(Faction.def.basicMemberKind)`。之后删掉读档路径 |
| A04 | `Incidents/Shared/MugirlEventUtility.cs:61-74`（`ClearPlayerMigrationPawns`），调用点 `MugirlStoryState.cs:52` | 03439c8（07-05）到 9e8ae83（07-08）之间，加入玩家的迁徙雪牛娘没有清掉 `"Mugirl_Migration"` 任务标签。每次读档全量扫描 | 加入时由 `CleanupAfterJoiningPlayer` 清理（`MugirlWildSlaveUtility.cs:91`） | **(f)** 只执行一次：玩家派系 Pawn 去掉 `Mugirl_Migration` 标签。存档 XML 中派系是 `Faction_N` 引用，用 (e) 不方便 |
| A05 | `Incidents/Migration/MapComponent_MugirlMigration.cs:10`（`CurrentDataVersion = 2`）、`:27`、`:337-368`（`ReleaseLegacyBikiniLocks`/`UnlockLegacyBikini`）、`:478`（标签 `dataVersion`，默认 0）、`:486-495` | `dataVersion` 0：03439c8 到 b3074b3（07-05 至 07-12），迁徙事件会 `apparel.Lock` 比基尼。`dataVersion` 1：b3074b3 到 0a6c435（07-12 至 07-15），没有惩罚者结算，读档时强制 `migrationOutcomeFailed = true`，避免误召唤。注意：`ReleaseLegacyBikiniLocks` 由每张地图的组件各执行一次，但每次都扫全局 Pawn | 删除 `dataVersion` 字段、标签和两个方法 | **(e)**：该 MapComponent 节点中 `dataVersion` 缺失或小于 2 时，把 `migrationOutcomeFailed` 写成 True，然后删除 `dataVersion` 节点。**(f)** 只执行一次：带 `Mugirl_Migration` 且不带 `Mugirl_RunawayFarm` 标签的 Pawn，解锁其穿着的 `Mugirl_Bikini` |
| A06 | `Migration/MapComponent_MugirlMigration.cs:7`（注释）、`:320-335`（`EnsureGrazeTicksAligned`，调用点 `:38`、`:485`）；`Punisher/MapComponent_MugirlPunisher.cs:7`、`:239-266`（`EnsureStateListsAligned`，调用点 `:102`、`:286`） | 平行列表在旧档或缺节点时长度不一致：`migrationPawns`/`nextGrazeTicks`、`punishers`/`punisherSpawnedAtTicks`/`punisherLeavingFlags`。`playerKilledMigrationPawnIds` 是第三份以 thingIDNumber 为键的平行状态 | 每个对象一条记录的 IExposable 列表：`MigrantEntry{pawn, nextGrazeTick, killedByPlayer}`、`PunisherEntry{pawn, spawnTick, leaving}` | **(e)** 把平行列表按索引合并成记录列表，缺的值用默认值补齐：graze 用当前 tick 加随机值，可在 (f) 中补；spawnTick 用当前 tick；leaving 用 false。`playerKilledMigrationPawnIds` 中的 ID 通过 `migrationPawns` 的引用 ID（`Thing_<def><id>`）匹配到记录的 `killedByPlayer`。标签改名见 B、C 节 |
| A07 | `Punisher/MapComponent_MugirlPunisher.cs:16`（注释）、`:287-288` | 1.6 存档没有 `punisherHeldLoot`/`punisherLootBursts`。1.6 不保存飞散动画，见 `Punisher/MugirlPunisherLootBurst.cs:6-7` | 两者都是必有字段 | **(g)**：缺节点就等于空。`?? new` 属于通用防御，不是兼容逻辑，可以保留，删掉“旧存档”字样即可 |
| A08 | `Story/MugirlStoryState.cs:152-156`（`openingCrashPawns ??=`，注释“早期存档”）；`Courier/QuestPart_SpawnCourier.cs:138-164`（`FindOpeningCrashPawns` 回退，注释在 `:143`） | 0.6.6（8cd70f9，09-23）之前的存档没有 `openingCrashPawns`，运货员改从开局任务的 `QuestPart_MugirlRescueJoin.pawns` 找目标 | `openingCrashPawns` 始终有值 | **(f)** 只执行一次：列表为空，且存在 root 为 `Mugirl_SlaveOpeningPodCrash` 的任务时，复制其 RescueJoin 部件的 `pawns`。之后删除回退循环，读取改为直接用状态 |
| A09 | `Story/MugirlStoryService.cs:122-125` | `courierRaidTimer` 的初始延迟在 8cd70f9 从 `5 * 60000` 改为 `60000`（`git show 8cd70f9` 中 `CourierRaidInitialDelayTicks` 的改动）。≤0.6.5 的存档可能存着最多 300000 的计时，每次 tick 都会被截断 | 计时由新逻辑写入，不需要截断 | **(e)**（纯数值）：未触发时取 `timer = min(timer, 60000)`。之后删除截断 |
| A10 | `Story/MugirlStoryState.cs:14-16,107-111`；`MugirlStoryService.cs:117,132-136,164`；`MugirlStoryDebugActions.cs:46`；`Courier/IncidentWorker_Mugirl_CourierRaid.cs:44-49`；跨模块：`Corporate/Introduction/CorporateIntroduction.cs:78,236` | `courierRaidTriggered` 与 `courierRaidQuestStarted` 两个标志并存。1.6 的手动事件只发任务、不标记剧情状态（`1.6/Source/Features/Incidents/IncidentWorker_Mugirl_CourierRaid.cs:17-27`），所以存在“任务已开始但未触发”的状态，要等计时到期再转换。1.7 中两者总是同时置位 | 一个标志 `courierContact.started` | **(e)** `started = triggered OR questStarted`。**(f)** 若存档中已有 root 为 `Mugirl_CourierRaid` 的任务，也视为已开始（修补 1.6 的事件路径） |
| A11 | `FusionInvestment/ChoiceLetter_MugirlFusionInvestment.cs:5-80` | 开发期以选择信件发起投资。该类在 03439c8 加入，注释是 “Save compatibility for older games that already had the investment choice letter open”。**git 全部历史中没有任何代码创建过它**，只有早于 git 的开发构建可能写入过 | 删除 | **(a)** `Mugirl.ChoiceLetter_MugirlFusionInvestment` → `Verse.StandardLetter`。它是 `ChoiceLetter` 子类，标题、正文和目标都保留，多出来的 `<map>` 被忽略。或者 **(e)** 从 `archive/archivables` 与 `letterStack/letters` 中删除。删除后 `MugirlFusionInvestmentUtility.TryAcceptInvestment(..., investor: null)` 这条调用路径消失 |
| A12 | `Shared/MugirlEventMarkers.cs:16-17`（注释）；`RunawayFarm/QuestPart_RunawayFarmCompletion.cs:249` | 1.6（含冻结构建）给奖励 Pawn 写入只写不读的任务标签 `"Mugirl_RunawayReward"`（`1.6/Source/Features/Incidents/MugirlEventUtility.cs:12,50`、`QuestPart_RunawayFarm.cs:752`）。每条 questTag 都会参与原版的任务信号广播 | 不写 | **(e)**：从所有 `questTags` 中删除 `<li>Mugirl_RunawayReward</li>` |
| A13 | `Shared/MugirlEventMarkers.cs:16-22`；`Shared/MugirlEventUtility.cs:6` | 事件标记字符串（`Mugirl_Migration`、`Mugirl_RunawayFarm`、`Mugirl_FusionInvestor`）写在 `Thing.questTags` 中，因为“1.6 与 1.7 互读”而冻结 | 值本身没有问题，热路径需要逐 Pawn O(1) 判断，**建议保持字符串不变** | 不改则 **(g)**，只删掉“不能改名”的理由。改名则 **(e)** 改写所有 questTags 中的字符串 |
| A14 | `Misc/Grazing/Harmony_MugirlFoodPolicyGrass.cs:16-30`（`FoodRestrictionDatabase.ExposeData` 的 PostLoadInit 后缀）；`Misc/Grazing/MugirlFoodPolicyUtility.cs:10-12,42-54` | 0.6.2（bf577ca，09-18）之前的存档，或中途加装的存档，没有“草”方案。**每次读档都执行**，按中文或英文标签去重。玩家删除或改名后会再次生成（`:11` 注释自认） | 新开局时生成（`:6-14` 的 NewGame 后缀）。升级时生成一次，并记录方案 id | **(f)** 只执行一次，同时覆盖“中途加装”的情况。之后删除读档后缀和标签匹配 |
| A15 | `Misc/Pregnancy/Harmony_MugirlPregnancyMood.cs:15-27` | 0.6.6（8cd70f9）之前，雪牛娘的 `PregnancyMood` 可能已抽到负面 `selectedThought`。现在每个 `HediffComp_GiveRandomSituationalThought` 读档时都做一次检查 | 只保留新建时的后缀（`:5-13`） | **(f)** 只执行一次：遍历所有 Pawn 的 PregnancyMood Hediff，调用 `MugirlPregnancyMoodUtility.EnsurePositiveThought` |
| A16 | `Story/MugirlStoryState.cs:40-46,62-69` → `Newborn/LifeStageVisualService.cs:46-59`（`NormalizeLoadedPawns`） | 自 0.2.1（ed1eb97）起，每次新建游戏和读档都扫描所有存活 Pawn，校正身形。实时补丁 `Harmony_PawnAgeTracker_RecalculateLifeStageIndex_MugirlBodyType`、`Harmony_PawnGenerator_NewbornVisuals` 已经维持这个不变量 | 只靠实时补丁 | **(f)** 只执行一次。若要保留为“其他 Mod 改了身形时的不变量修复”，必须写明理由，并移出剧情组件 |
| A17 | `Courier/QuestPart_SpawnCourier.cs:189-218`（注释在 `:205`） | ≤0.6.5 生成的运货员背包里有写了假 `stackCount` 的钥匙 | 交出物品前按当前锁数重建钥匙，这本身就是领域逻辑 | **(g)**：逻辑保留，删掉旧档注释。运货员最多停留 2 天（`ConversationWaitTicks`，`:9`） |
| A18 | “类名或标签写进存档，不能改名”的冻结注释：`Courier/JobDriver_ReadCourierDiary.cs:5`、`Courier/JobDriver_TalkCourier.cs:5`、`FusionInvestment/JobDriver_TalkFusionInvestor.cs:5`、`Shared/JobDriver_MugirlTalkToPawn.cs:5-6`、`Misc/Nuzzle/JobDriver_Nuzzle.cs:6`、`Misc/Thoughts/Thought_MugirlOnly.cs:3`、`Courier/QuestPart_SpawnCourier.cs:5-6`、`OpeningCrash/QuestPart_MugirlRescueJoin.cs:3`、`RunawayFarm/QuestPart_SpawnRunawayFarm.cs:7`、`RunawayFarm/QuestPart_RunawayFarmCompletion.cs:7`、`Migration/MapComponent_MugirlMigration.cs:6-7`、`Punisher/MapComponent_MugirlPunisher.cs:6-7`、`Story/MugirlStoryState.cs:3`、`Misc/RenderRefresh/GameComponent_GhoulRenderingRefresh.cs:6`、`RunawayFarm/QuestNode_Root_Mugirl_RunawayFarmQuest.cs:8`、`Courier/CompReadableBook.cs:6`、`WildSlaves/MugirlWildSlaveUtility.cs:6` | 冻结名称规则 | 按 C 节改名 | 对应 B、C 节的 (a)、(b)、(c)。**注意 `QuestNode_Root_Mugirl_RunawayFarmQuest.cs:8` 的说法过严**：信号名在生成时已存进各 QuestPart 字段（`inSignal`、`outSignalsCompleted`、`failSignal`、`protectedTargetTag`），每个旧任务自洽，改字面量只影响新任务，属于 (g) |
| A19 | 只描述“与 1.6 行为一致或不同”的注释，没有兼容代码：`MugirlFusionInvestmentUtility.cs:26,134`；`OpeningCrash/MugirlPawnDowningUtility.cs:55,74`；`OpeningCrash/QuestNode_Root_Mugirl_OpeningPodCrash.cs:8`；`Shared/Harmony_PassiveEventPawns.cs:25`；`Punisher/MugirlPunisherDeathEffectUtility.cs:30`；`Punisher/MugirlPunisherLootBurst.cs:7`；`Punisher/PunisherArcMath.cs:3`；`RunawayFarm/QuestPart_RunawayFarmCompletion.cs:12,249`；`RunawayFarm/QuestPart_SpawnRunawayFarm.cs:75`；`Shared/RunawayWanderUtility.cs:9-10`；`Story/MugirlStoryService.cs:145`；`Story/MugirlStoryState.cs:39`；`Trade/StockGenerator_Mugirl_Slaves.cs:84`；`Wanderer/QuestNode_Root_Mugirl_WandererJoin_WalkIn.cs:15`；`WildSlaves/Harmony_WildSlavePatches.cs:6-7,14`；`Migration/IncidentWorker_Mugirl_Migration.cs:102`；`Misc/Thoughts/Thought_MugirlOnly.cs:6`；`Misc/Nuzzle/JobDriver_Nuzzle.cs:23`；`Misc/Pregnancy/MugirlPregnancyMoodUtility.cs:23`；`Misc/Grazing/Harmony_MugirlGrazeIngest.cs:68`（“旧版”） | 无 | 无 | **(g)**：1.6 不再维护后，这些注释改写为说明当前行为的理由，或者删除 |
| A20 | 设置键 `enableStructuralCrashEvent`（跨模块：`Core/MugirlSettings.cs:6,32`；使用处 `Story/MugirlStoryService.cs:52`、`Corporate/Introduction/CorporateIntroduction.cs:234`） | 名称来自 1.6 中早已无用的 `IncidentWorker_MugirlStructuralCrashMission`（`1.6/Source/Features/Incidents/MugirlStoryState.cs:384-395`，1.7 已删除）。现在控制开局坠舱剧情线和巨企独立接触路线 | 例如 `enableOpeningStoryline` | 属于设置文件迁移，不属于存档：在 `Mod_<folder>_MugirlMod.xml` 中把旧键读成新键。由核心模块审计负责 |
| A21 | 1.6 已死、1.7 已删除：`HediffComp_JoinWhenRescued`（标签 `ticksUntilNextCheck`/`joinedAlready`）、`IncidentWorker_MugirlStructuralCrashMission`（`1.6/Source/Features/Incidents/MugirlStoryState.cs:328-395`） | 1.6 内容中没有 Def 引用它们，`grep 1.6/Defs` 为空 | — | **(g)**：已登记在 `Tests/MugirlRace.Validation/Baselines/dropped-scribe-labels.txt`（joinedAlready） |
| A22 | 内部调用：`Story/MugirlStoryState.cs:75-76` 调用 `MugirlFoodEffectUtility.ResetDefCache()`、`MugirlNurtureUtility.ResetDefCache()` | 两个方法体都是空的，只为“保留调用点”而存在（`Milk/FoodEffects/MugirlFoodEffectUtility.cs:6-9`、`Milk/Nurture/MugirlNurtureUtility.cs:14-17`） | 删除 | **(g)** |

### 升级层合并清单（本模块）

- **(a) 类型映射**：见 B 节和 C 表。必做的是 4 个 QuestPart、2 个 MapComponent、1 个仍保留的 GameComponent，以及 Thought_MugirlOnly（不做 (a) 时每个实例都报红字，但数据保留）。JobDriver 不做 (a) 只丢当前工作，建议仍然映射。ChoiceLetter 映射到 `Verse.StandardLetter`。`GameComponent_GhoulRenderingRefresh` 若删除，改用 (e) 删除节点。
- **(b) defName 映射**：
  - PawnKindDef：`Mugirl_EscapeWildSlave`、`Mugirl_PreEscapeWildSlave` → `Mugirl_WildMugirl`
  - IncidentDef 与 QuestScriptDef：`Mugirl_CourierRaid` → `Mugirl_CourierContact`（可选，见 C 节）
  - JobDef：`Job_Nuzzle` → `Mugirl_Nuzzle`
  - ThinkTreeDef：`MugirlPunisher` → `Mugirl_Punisher`
- **(c) 标签改名**：见 B 节的“建议新标签”列。
- **(d) 枚举**：本范围没有按名字保存的枚举。
- **(e) 结构变换**：A01、A05（dataVersion）、A06（平行列表）、A09、A10、A12；剧情状态拆成嵌套子状态（C 节）；删除空的 `GameComponent_GhoulRenderingRefresh` 节点。
- **(f) 读档后步骤**（只执行一次，之后写入版本戳）：A03、A04、A05（比基尼解锁）、A08、A10（检查任务）、A14、A15、A16。
- **(g) 直接删除**：A07、A13（不改名时）、A17、A19、A21、A22。

---

## B. 存档中可见的接口

### B1. 写 `Class="完整类型名"` 的类型

| 1.7 类型（文件:行） | 原版基类 | 在存档中的位置 | 1.6 对应（冻结） | 差异 |
|---|---|---|---|---|
| `Mugirl.MugirlStoryState`（`Story/MugirlStoryState.cs:5`） | GameComponent | `<game><components><li Class="Mugirl.MugirlStoryState">` | 同名，`1.6/Source/Features/Incidents/MugirlStoryState.cs:8` | 标签与默认值相同 |
| `Mugirl.GameComponent_GhoulRenderingRefresh`（`Misc/RenderRefresh/GameComponent_GhoulRenderingRefresh.cs:9`） | GameComponent | 同上，空元素，没有字段 | 同名，`1.6/Source/Features/Misc/Harmony_GhoulRenderingRefresh.cs:21` | 无 |
| `Mugirl.MapComponent_MugirlMigration`（`Migration/MapComponent_MugirlMigration.cs:8`） | MapComponent | `<map><components><li Class=...>` | 同名，`1.6/.../IncidentWorker_Mugirl_Migration.cs:163` | 无 |
| `Mugirl.MapComponent_MugirlPunisher`（`Punisher/MapComponent_MugirlPunisher.cs:8`，实现了 `IThingHolder`） | MapComponent | 同上 | 同名，`1.6/.../MugirlPunisher.cs:9` | **1.7 新增** `punisherHeldLoot` 与 `punisherLootBursts` |
| `Mugirl.QuestPart_SpawnCourier`（`Courier/QuestPart_SpawnCourier.cs:7`） | QuestPart | `<questManager><quests><li><parts><li Class=...>` | 同名，`1.6/.../QuestPart_CourierRaid.cs:10` | 无 |
| `Mugirl.QuestPart_MugirlRescueJoin`（`OpeningCrash/QuestPart_MugirlRescueJoin.cs:4`） | QuestPart | 同上 | 同名，`1.6/.../QuestPart_MugirlRescueJoin.cs:7` | 无 |
| `Mugirl.QuestPart_SpawnRunawayFarm`（`RunawayFarm/QuestPart_SpawnRunawayFarm.cs:9`，partial，Layout 在另一个文件） | QuestPart | 同上 | 同名，`1.6/.../QuestPart_RunawayFarm.cs:11` | 无 |
| `Mugirl.QuestPart_RunawayFarmCompletion`（`RunawayFarm/QuestPart_RunawayFarmCompletion.cs:9`） | QuestPartActivable | 同上 | 同名，`1.6/.../QuestPart_RunawayFarm.cs:518` | 无 |
| `Mugirl.ChoiceLetter_MugirlFusionInvestment`（`FusionInvestment/ChoiceLetter_MugirlFusionInvestment.cs:6`） | ChoiceLetter | `<archive><archivables><li Class=...>`，信件栏 `<letterStack><letters>` 中按引用出现 | 同名，`1.6/.../IncidentWorker_Mugirl_FusionInvestment.cs:61` | 无。两版都没有创建它的代码 |
| `Mugirl.JobDriver_ReadCourierDiary`（`Courier/JobDriver_ReadCourierDiary.cs:6`） | JobDriver | `<jobs><curDriver Class=...>`，curJob 中有 `<def>Mugirl_ReadCourierDiary</def>` | 同名，`1.6/.../CompReadableBook.cs:69` | 无 |
| `Mugirl.JobDriver_TalkCourier`（`Courier/JobDriver_TalkCourier.cs:6`） | JobDriver（中间插入了 `Mugirl.Features.Incidents.JobDriver_MugirlTalkToPawn`，抽象类，没有字段） | 同上 | 同名，`1.6/.../QuestPart_CourierRaid.cs:423`，直接继承 JobDriver | 1.7 插入了抽象基类，对存档没有影响 |
| `Mugirl.JobDriver_TalkFusionInvestor`（`FusionInvestment/JobDriver_TalkFusionInvestor.cs:6`） | 同上 | 同上 | 同名，`1.6/.../IncidentWorker_Mugirl_FusionInvestment.cs:726` | 同上 |
| `Mugirl.JobDriver_Nuzzle`（`Misc/Nuzzle/JobDriver_Nuzzle.cs:7`） | JobDriver | 同上，JobDef 为 `Job_Nuzzle` | 同名，`1.6/Source/Features/Misc/JobDriver_Nuzzle.cs:8` | 无 |
| `Mugirl.Thought_MugirlOnly`（`Misc/Thoughts/Thought_MugirlOnly.cs:4`） | Thought_Memory | Pawn 的记忆列表（`MemoryThoughtHandler`，LookMode.Deep）中 `<li Class=...>`。用于 ThoughtDef `Mugirl_AteHay`、`Mugirl_AteNutrient`（`1.7/Defs/ThoughtDefs/Thoughts_Traits.xml:22-23,39-40`） | 同名，`1.6/Source/Features/Misc/Thought_MugirlOnly.cs:6` | 1.7 改为重写 `ShouldDiscard`，对存档没有影响 |

没有写 `Class=` 的 IExposable（声明类型等于运行时类型，改名不影响存档）：`MugirlPunisherLootBurst`（`Punisher/MugirlPunisherLootBurst.cs:8`，internal sealed，1.7 新增）和它的嵌套类 `DebrisArc`（`:189`，private sealed，1.7 新增）。

**会存数据的 Comp、Properties、Worker**：本范围没有。`CompReadableBook` 没有 `PostExposeData`。其他由 Def 创建、不写类名的类型：`CompProperties_ReadableBook`、`CompReadableBook`、全部 `IncidentWorker_*`、`QuestNode_Root_*`、`DamageWorker_MugirlPunisherBlow`、`DeathActionWorker_MugirlPunisher`、`JobGiver_MugirlPunisher`、`JobGiver_Nuzzle`、`ThinkNode_ConditionalNonPlayerWildMugirl`、`StockGenerator_Mugirl_Slaves`、`InteractionWorker_Nuzzle`、`ThoughtWorker_SingleshotWeaponAversion`、`FloatMenuProvider_*`（由原版反射实例化）。`StockGenerator_Mugirl_Slaves` 的类名还出现在 Patch 的 XPath 谓词里（`1.7/Patches/MugirlPatch.xml:54,68,82`）。本模块的补丁会改动原版 `HediffComp_GiveRandomSituationalThought.selectedThought`（原版标签）的值，见 A15。

### B2. Scribe 调用明细

“建议新标签”列只在需要 (c) 或 (e) 时填写。

**`MugirlStoryState.ExposeData`**（`Story/MugirlStoryState.cs:135-157`；1.6 在 `1.6/.../MugirlStoryState.cs:110-122`，相同）

| 行 | 标签 | Scribe 方式 | 类型 | 默认值 | 建议新标签（嵌套子状态） |
|---|---|---|---|---|---|
| 138 | `openingCrashStarted` | Values | bool | false | `openingCrash/started` |
| 139 | `openingCrashCheckTimer` | Values | int | 360（`OpeningCrashInitialCheckTicks`） | `openingCrash/checkTimer` |
| 140 | `openingCrashPawns` | Collections，Reference | List&lt;Pawn&gt; | —（PostLoadInit 中 `??=`，`:153-156`） | `openingCrash/pawns` |
| 141 | `courierRaidTriggered` | Values | bool | false | 与下面的 `courierRaidQuestStarted` 合并为 `courierContact/started`（A10） |
| 142 | `courierRaidTimer` | Values | int | 60000 | `courierContact/timer`（A09 截断） |
| 143 | `courierRaidQuestStarted` | Values | bool | false | 合并（A10） |
| 144 | `fusionInvestmentAccepted` | Values | bool | false | `fusionInvestment/accepted` |
| 145 | `fusionInvestmentPending` | Values | bool | false | `fusionInvestment/pending` |
| 146 | `fusionInvestmentAmount` | Values | int | 0 | `fusionInvestment/amount` |
| 147 | `fusionInvestmentTimer` | Values | int | 0 | `fusionInvestment/timer` |
| 148 | `fusionInvestmentNextOfferTick` | Values | int | 0 | `fusionInvestment/nextOfferTick` |
| 149 | `fusionInvestmentInvestorActive` | Values | bool | false | 删除。与 investor 引用重复：`SetFusionInvestor` 和 `ClearFusionInvestor` 总是同时写两者（`:113-124`），“active 但引用为 null”会被 `ClearStaleInvestor` 清掉（`FusionInvestment/MugirlFusionInvestmentUtility.cs:333-351`） |
| 150 | `fusionInvestmentInvestor` | References | Pawn | — | `fusionInvestment/investor` |

不保存的瞬态字段：`storyServiceLastTick`、`storyServiceNextTick`、`fusionInvestmentNextStaleCheckTick`（`:26-28`，在 `FinalizeInit` 中重置）。

**`MapComponent_MugirlMigration.ExposeData`**（`Migration/MapComponent_MugirlMigration.cs:465-497`；1.6 在 `1.6/.../IncidentWorker_Mugirl_Migration.cs:627-637`，相同）

| 行 | 标签 | Scribe 方式 | 类型 | 默认值 | 建议 |
|---|---|---|---|---|---|
| 468 | `migrationPawns` | Collections，Reference | List&lt;Pawn&gt; | — | 与下面两项合并为 `migrants`（`MigrantEntry`，A06） |
| 469 | `nextGrazeTicks` | Collections，Value | List&lt;int&gt; | — | 合并 |
| 470 | `playerKilledMigrationPawnIds` | Collections，Value | List&lt;int&gt;（thingIDNumber） | — | 合并为 `killedByPlayer` |
| 471 | `exitCell` | Values | IntVec3 | Invalid | 保留 |
| 472 | `ticksUntilDeparture` | Values | int | 0 | 保留 |
| 473 | `killWindowTicksRemaining` | Values | int | 0 | 保留 |
| 474 | `originalMigrationCount` | Values | int | 0 | 保留 |
| 475 | `killedMigrationPawns` | Values | int | 0 | 建议改为 `playerKillCount`，可选 |
| 476 | `migrationOutcomeFailed` | Values | bool | false | 保留，或改为 `outcomeFailed` |
| 477 | `departureStarted` | Values | bool | false | 保留 |
| 478 | `dataVersion` | Values | int | 0（字段初始值为 2） | **删除**（A05） |

**`MapComponent_MugirlPunisher.ExposeData`**（`Punisher/MapComponent_MugirlPunisher.cs:275-296`；1.6 在 `1.6/.../MugirlPunisher.cs:244-246`）

| 行 | 标签 | Scribe 方式 | 类型 | 1.6 是否有 | 建议 |
|---|---|---|---|---|---|
| 278 | `punishers` | Collections，Reference | List&lt;Pawn&gt; | 有 | 合并为 `punishers`（`PunisherEntry`） |
| 279 | `punisherSpawnedAtTicks` | Collections，Value | List&lt;int&gt; | 有 | 合并为 `spawnTick` |
| 280 | `punisherLeavingFlags` | Collections，Value | List&lt;bool&gt; | 有 | 合并为 `leaving` |
| 281 | `punisherHeldLoot` | Deep（ctorArgs: this） | ThingOwner&lt;Thing&gt; | **1.7 新增** | 改为 `heldLoot`（标签中的类型前缀多余） |
| 282 | `punisherLootBursts` | Collections，Deep | List&lt;MugirlPunisherLootBurst&gt; | **1.7 新增** | 改为 `lootBursts` |

`MugirlPunisherLootBurst.ExposeData`（`:182-187`，1.7 新增）：`originCell`（Values IntVec3，没有显式默认值）、`startTick`（Values int，0）、`arcs`（Collections Deep `List<DebrisArc>`）。`DebrisArc.ExposeData`（`:211-218`）：`thing`（References Thing，指向 heldLoot 中的物品）、`targetCell`（IntVec3）、`launchHeight`、`sideSwing`、`spin`（float，都是 0f）。

**`QuestPart_SpawnCourier.ExposeData`**（`Courier/QuestPart_SpawnCourier.cs:220-228`；1.6 在 `QuestPart_CourierRaid.cs:201-205`，相同）：`map`（References Map）、`faction`（References Faction）、`spawnCell`（Values IntVec3，默认 Invalid，已登记在 `Baselines/scribe-defaults.txt`）、`courier`（References Pawn）、`inSignal`（Values string）。跨模块读取方：`Corporate/Introduction/CorporateIntroduction.cs:364-376`（`RecoverCourierHistory` 读取 `part.courier`、`part.map`）。

**`QuestPart_MugirlRescueJoin.ExposeData`**（`OpeningCrash/QuestPart_MugirlRescueJoin.cs:110-121`；1.6 在 `:117-120`，相同）：`inSignalRescued`、`inSignalRecruited`（Values string）、`pawns`（Collections Reference List&lt;Pawn&gt;）。PostLoadInit 中 `??=` 并删除 null。

**`QuestPart_SpawnRunawayFarm.ExposeData`**（`RunawayFarm/QuestPart_SpawnRunawayFarm.cs:232-251`；1.6 在 `:488-498`，相同）：`inSignal`（string）、`site`（References Site）、`protectedTargetTag`（string）、`runawayPawns`（Reference List&lt;Pawn&gt;）、`hitches`、`farmThings`（Reference List&lt;Thing&gt;）、`rancher`（References Pawn）、`rancherFaction`（References Faction）、`spawned`（bool，false）。

**`QuestPart_RunawayFarmCompletion.ExposeData`**（`RunawayFarm/QuestPart_RunawayFarmCompletion.cs:292-300`；1.6 在 `:795-799`，相同；基类 QuestPartActivable 的标签是原版的）：`site`（References Site）、`spawnPart`（**References QuestPart**，引用 ID 依赖 Part 索引，见 A 节）、`failSignal`（string）、`ticksUntilNextCheck`（int，0）、`resolved`（bool，false）。

**`ChoiceLetter_MugirlFusionInvestment.ExposeData`**（`:63-67`）：`map`（References Map）。

**JobDriver（4 个）、`Thought_MugirlOnly`、`GameComponent_GhoulRenderingRefresh`**：没有自己的 Scribe 调用。

### B3. 其他会写进存档的名字与字符串

- **按名字保存的枚举**：本范围没有。`MugirlEventFlags`（`Shared/MugirlEventMarkers.cs:5`）只在内存中使用。
- **Scribe_Defs**：本范围的代码没有直接调用。下列 defName 由原版写进存档，属于本领域：
  - QuestScriptDef（`Quest.root`、`recentRandomQuests`）：`Mugirl_SlaveOpeningPodCrash`、`Mugirl_SlaveRefugeePodCrash`、`Mugirl_SlaveWandererJoins`、`Mugirl_CourierRaid`、`Mugirl_RunawayFarmQuest`。
  - IncidentDef（`lastFireTicks`、`recentRandomIncidents`、事件队列）：`Mugirl_SlaveRefugeePodCrash`、`Mugirl_SlaveWandererJoins`、`Mugirl_Migration`、`Mugirl_FusionInvestment`、`Mugirl_RunawayFarmQuest`、`Mugirl_CourierRaid`、已退役的 `Mugirl_WildManWandersIn`。
  - SitePartDef：`Mugirl_RunawayFarm`。
  - JobDef（`curJob/def`）：`Mugirl_ReadCourierDiary`、`Mugirl_TalkCourier`、`Mugirl_TalkFusionInvestor`、`Job_Nuzzle`。
  - InteractionDef（`Job.interaction` 与对话记录）：`Mugirl_Nuzzle`。
  - ThinkTreeDef（`Job.jobGiverThinkTree`）：`MugirlPunisher`。
  - HediffDef：`Mugirl_RunawayPanic`、`Mugirl_PunisherKnockout`、`Mugirl_Abasia`。
  - ThoughtDef：`Mugirl_Nuzzled`、`Mugirl_AteHay`、`Mugirl_AteNutrient`。
  - PawnKindDef：`Mugirl_WildMugirl`、`Mugirl_EscapeWildSlave`、`Mugirl_PreEscapeWildSlave`、`Mugirl_Beginning_Slave`、`Mugirl_EscapeSpaceSlave`、`Mugirl_EscapeWanderSlave`、`Mugirl_Punisher`、`AI_GC_Courier`。
  - ThingDef：`Mugirl_CourierDiary`、`Mugirl_PunisherRace`。
  - DamageDef：`Mugirl_PunisherBlow`。
  - 生成的 RecipeDef：`Administer_Mugirl_Milk`（账单）。它按原版 `RecipeDefGenerator` 的命名规则生成（`Misc/MilkRecipe/Patch_DrugAdministerDefs.cs:28`），**不是被迫冻结的名字，保持即可**。
  - 1.6 与 1.7 中本领域的 defName 完全相同。对两版全部 `<defName>` 做差集，只有机械体 Bulldog 的若干 Def 是 1.6 独有，不属于本范围。
- **Scribe_References 目标**：Map、Faction、Pawn、Site、Thing、QuestPart（`spawnPart`）。
- **字符串数据**：
  - 事件标记 questTags（A13）。旧档还有 `Mugirl_RunawayReward`（A12）。
  - 受保护目标标签 `Quest<id>.RunawayFarm_Protected`（`RunawayFarm/QuestNode_Root_Mugirl_RunawayFarmQuest.cs:48`），会写进牧场物品与 Pawn 的 questTags。
  - 任务信号字面量：`CourierRaid_Spawn`（`Courier/QuestNode_Root_Mugirl_CourierRaid.cs:35`），`RunawayFarm_Success`、`RunawayFarm_Fail`、`RunawayFarm_Expired`（`RunawayFarm/QuestNode_Root_Mugirl_RunawayFarmQuest.cs:45-47`），`pawns.*`（`OpeningCrash/QuestNode_Root_Mugirl_OpeningPodCrash.cs:177-180`）。
  - 这些字符串都在生成时存进 QuestPart 字段，旧任务自洽，改名属于 (g)。
- **按名字查 Def**：`GetNamedSilentFail` 只有 `Misc/MilkRecipe/Patch_DrugAdministerDefs.cs:51`，用于热重载时复用生成的 Def，不是兼容逻辑。本范围没有 `MayRequire` 指向已删除的内容。`Misc/RenderRefresh/MugirlMiscDefOf.cs:7` 的 `[MayRequireAnomaly] Ghoul` 是正常的 DLC 可选引用。

---

## C. 命名与架构问题

### C1. 从仓库较好的代码和原版推断出的约定

1. **命名空间**：`Mugirl.Features.<Feature>`。依据：`docs/architecture/ADR-004-root-namespace-markers.md` 的 Preferred New Namespaces；`Features/WeaponWheel`、`Features/Corporate` 的新代码；本范围的 `IncidentsDefOf` 和 `MugirlStorySchedule`。子文件夹只用于组织，命名空间停在 Feature 一级，这是仓库现行做法，例如 WeaponWheel/AI、Combat、Core 都在 `Mugirl.Features.WeaponWheel`。核心工具在 `Mugirl.Core`（`Baselines/architecture-rules.json` 要求 `Mugirl.Core.MugirlLog` 等）。

   本范围现状：90 个文件中 **74 个在根命名空间 `Mugirl`**，12 个在 `Mugirl.Features.Incidents`，3 个在 `Mugirl.Features.Misc`，1 个在 `Mugirl.Features.Newborn`。根命名空间是冻结规则造成的（ADR-004 的 “Root Namespace Is Allowed For”）。新方向下应全部迁走，ADR-004 和 README 第 63 行“不能随意改名”的约束需要同步改写。

2. **类型名**：采用原版的角色前缀，例如 `IncidentWorker_X`、`QuestNode_Root_X`、`QuestPart_X`、`JobDriver_X`、`JobGiver_X`、`ThinkNode_ConditionalX`、`CompProperties_X`/`CompX`、`GameComponent_X`（原版 `GameComponent_Bossgroup` 等）、`DamageWorker_X`、`DeathActionWorker_X`、`StockGenerator_X`、`InteractionWorker_X`、`ThoughtWorker_X`、`Thought_X`、`Dialog_X`、**`FloatMenuOptionProvider_X`**（原版是 `FloatMenuOptionProvider_Ingest`，仓库全部写成 `FloatMenuProvider_`）。
   - 命名空间已经带 Mugirl，类型名里**不再加 `Mugirl`/`Mugirl_`**。
   - **例外**：类型短名会和原版同角色、同名的类型撞名时，保留 `Mugirl` 作为区分，不加下划线（如 `JobDriver_MugirlNuzzle`）。这些短名也会出现在 DefInjected 句柄和日志里。已核对撞名的原版类型：`RimWorld.JobDriver_Nuzzle`、`RimWorld.JobGiver_Nuzzle`、`RimWorld.InteractionWorker_Nuzzle`、`RimWorld.StockGenerator_Slaves`、`RimWorld.QuestGen.QuestNode_Root_RefugeePodCrash`、`QuestNode_Root_WandererJoin_WalkIn`。
   - MapComponent：原版写法不统一（`FleshmassMapComponent`、`BreakdownManager`）。为了和 `GameComponent_` 对称、并与生态里常见写法一致，用 `MapComponent_X`。
3. **工具类**：`XUtility`，一个关注点一个类。`MugirlEventUtility` 与 `MugirlEventMarkers` 管的是同一件事。
4. **Harmony 补丁类**：仓库中 `Harmony_*` 有 91 个，`*_Patch` 有 25 个，`Patch_*` 有 6 个。这些名字不进存档也不进 XML，只出现在 `Baselines/patch-targets.txt`（可重新生成）。建议统一用多数派 `Harmony_<TargetType>_<Member>`，补丁类统一为 `internal static`。本范围的 `Harmony_Apparel_DrawColor`、`Harmony_StylingStationFixedColor_*`、`Harmony_MugirlGrazeChain`、`Harmony_MugirlGrazeFloatMenu`、`Harmony_MugirlGrazeIngest`、`Harmony_MugirlPregnantApparel_*`、`Harmony_PawnGenerator_NewbornVisuals`、`Patch_DrugAdministerDefs` 是 public。
5. **Scribe 标签**：camelCase，与字段同名，不重复所属类型的前缀（反例：`punisherSpawnedAtTicks`）。相关的一组状态放进嵌套 IExposable，不用平行列表。
6. **defName**：`Mugirl_` 前缀加 PascalCase 名词（Def 名是全局的）。反例：`Job_Nuzzle`、ThinkTreeDef `MugirlPunisher`。同一个 defName 用在不同 Def 类型上有原版先例（原版的 JobDef 和 InteractionDef 都叫 `Nuzzle`）。

### C2. 改名建议表

| 种类 | 旧名 | 新名 | 理由 | 进存档 | 在 XML 中 |
|---|---|---|---|---|---|
| GameComponent | `Mugirl.MugirlStoryState` | `Mugirl.Features.Incidents.GameComponent_Story`，内含 `OpeningCrashState`、`CourierContactState`、`FusionInvestmentState` 三个嵌套 IExposable | 命名空间；原版前缀；13 个扁平公开字段拆成三个子状态（B2） | 是，(a)+(e) | 否（只在 `1.7/Defs/Storyteller/Incidents_Mugirl.xml:3` 的注释里出现） |
| GameComponent | `Mugirl.GameComponent_GhoulRenderingRefresh` | **删除**。调度表改为由每局生命周期服务持有的非存档成员（C3-R2）。若保留，改为 `Mugirl.Rendering.GameComponent_PawnRenderRefresh` | 没有字段，却把类名写进每个存档；“Ghoul”有误导，实际还服务怀孕变体和生命阶段刷新（`Misc/RenderRefresh/Harmony_Hediff_PostAdd_RenderRefresh.cs:19-27`、`Newborn/LifeStageVisualService.cs:33-37`） | 是，(e) 删节点或 (a) | 否 |
| MapComponent | `Mugirl.MapComponent_MugirlMigration` | `Mugirl.Features.Incidents.MapComponent_Migration` | 命名空间里已有 Mugirl | 是，(a)+(e) | 否 |
| MapComponent | `Mugirl.MapComponent_MugirlPunisher` | `Mugirl.Features.Incidents.MapComponent_Punisher` | 同上 | 是，(a)+(e) | 否 |
| QuestPart | `Mugirl.QuestPart_SpawnCourier` | `Mugirl.Features.Incidents.QuestPart_SpawnCourier` | 只改命名空间 | 是，(a) | 否 |
| QuestPart | `Mugirl.QuestPart_MugirlRescueJoin` | `...Incidents.QuestPart_JoinOnRescue` | 去掉 Mugirl；动词短语更清楚 | 是，(a) | 否 |
| QuestPart | `Mugirl.QuestPart_SpawnRunawayFarm`、`Mugirl.QuestPart_RunawayFarmCompletion` | `...Incidents.` 加同名 | 命名空间 | 是，(a) | 否 |
| Letter | `Mugirl.ChoiceLetter_MugirlFusionInvestment` | **删除** | A11 | 是，(a)→`Verse.StandardLetter` | 否 |
| JobDriver | `Mugirl.JobDriver_ReadCourierDiary` | `...Incidents.JobDriver_ReadCourierDiary` | 命名空间 | 是，(a) | 是（`1.7/Defs/JobDefs/Jobs_CourierDiary.xml:6`） |
| JobDriver | `Mugirl.JobDriver_TalkCourier`、`Mugirl.JobDriver_TalkFusionInvestor` | `...JobDriver_TalkToCourier`、`...JobDriver_TalkToFusionInvestor` | 与基类 `TalkToPawn` 的动词一致 | 是，(a) | 是（`Jobs_CourierDiary.xml:13,20`） |
| JobDriver 基类 | `Mugirl.Features.Incidents.JobDriver_MugirlTalkToPawn` | `JobDriver_TalkToEventPawn` | 去掉 Mugirl | 否（抽象类） | 否 |
| JobDriver | `Mugirl.JobDriver_Nuzzle` | `Mugirl.Features.Social.JobDriver_MugirlNuzzle`（或放在 Misc 下） | 与 `RimWorld.JobDriver_Nuzzle` 撞名 | 是，(a) | 是（`1.7/Defs/JobDefs/JobDefs.xml:57`） |
| JobGiver | `Mugirl.JobGiver_Nuzzle` | `JobGiver_MugirlNuzzle` | 与 `RimWorld.JobGiver_Nuzzle` 撞名 | 否 | 是（`1.7/Defs/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml:436`） |
| InteractionWorker | `Mugirl.InteractionWorker_Nuzzle` | `InteractionWorker_MugirlNuzzle` | 与原版撞名 | 否 | 是（`1.7/Defs/InteractionDefs/Interactions_Mugirl.xml:9`） |
| Thought | `Mugirl.Thought_MugirlOnly` | `Mugirl.Features.<Thoughts>.Thought_MugirlOnly` | 只改命名空间；“Mugirl” 在这里是语义（只对雪牛娘有效），保留 | 是，(a) | 是（`Thoughts_Traits.xml:23,40`） |
| ThoughtWorker | `Mugirl.ThoughtWorker_SingleshotWeaponAversion` | 同名，改命名空间 | — | 否 | 是 |
| Comp | `Mugirl.CompProperties_ReadableBook`、`Mugirl.CompReadableBook` | `CompProperties_CourierDiary`、`CompCourierDiary` | 名字是“通用书本”，但页面固定为运货员日记的 5 个 key（`Courier/Dialog_ReadBook.cs:6,39-46`） | 否 | 是（`1.7/Defs/ThingDefs_Misc/Mugirl_Diary.xml:33`）；**DefInjected 句柄** `Mugirl_CourierDiary.comps.CompReadableBook.bookTitle`（`1.7/Languages/ChineseSimplified/DefInjected/ThingDef/Mugirl_Diary.xml:7`）用的是 compClass 短名，要同步改 |
| Window | `Mugirl.Dialog_ReadBook` | `Dialog_CourierDiary` | 同上 | 否 | 否 |
| IncidentWorker | `IncidentWorker_Mugirl_CourierRaid`、`IncidentWorker_Mugirl_FusionInvestment`、`IncidentWorker_Mugirl_Migration`、`IncidentWorker_Mugirl_RunawayFarmQuest` | `IncidentWorker_CourierContact`、`IncidentWorker_FusionInvestment`、`IncidentWorker_Migration`、`IncidentWorker_RunawayFarm` | 去掉 `Mugirl_`；“Raid”有误导：这是一次和平接触（label 为 “courier contact”，`1.7/Defs/Storyteller/Incidents_CourierRaid.xml:7`） | 否 | 是（`Incidents_CourierRaid.xml:11`、`Incidents_Mugirl.xml:36,48,59`） |
| QuestNode | `QuestNode_Root_Mugirl_CourierRaid`、`QuestNode_Root_Mugirl_OpeningPodCrash`、`QuestNode_Root_Mugirl_RunawayFarmQuest` | `QuestNode_Root_CourierContact`、`QuestNode_Root_OpeningPodCrash`、`QuestNode_Root_RunawayFarm` | 同上 | 否（任务存的是 QuestScriptDef） | 是（`Script_CourierRaid.xml:19`、`Script_Mugirl.xml:22`、`Script_RunawayFarm.xml:23`） |
| QuestNode | `QuestNode_Root_Mugirl_RefugeePodCrash`、`QuestNode_Root_Mugirl_WandererJoin_WalkIn` | `QuestNode_Root_MugirlRefugeePodCrash`、`QuestNode_Root_MugirlWandererJoin` | 继承同名原版类型，保留 Mugirl 作区分 | 否 | 是（`Script_Mugirl.xml:43,62`） |
| Worker | `DamageWorker_MugirlPunisherBlow`、`DeathActionWorker_MugirlPunisher` | `DamageWorker_PunisherBlow`、`DeathActionWorker_Punisher` | 去掉 Mugirl | 否 | 是（`DamageDefs/Mugirl_Punisher.xml:27`、`ThingDefs_Races/Mugirl_Punisher.xml:44`） |
| JobGiver | `JobGiver_MugirlPunisher` | `JobGiver_RegisterPunisher` | 它从不给出工作，只做登记（`Punisher/JobGiver_MugirlPunisher.cs:5-12`），名字应该说明副作用 | 否 | 是（`ThinkTreeDefs/Mugirl_Punisher.xml:7`） |
| StockGenerator | `StockGenerator_Mugirl_Slaves` | `StockGenerator_MugirlSlaves` | 原版有 `StockGenerator_Slaves`；去掉多余的下划线 | 否 | 是，**包括 XPath 谓词**：`1.7/Patches/MugirlPatch.xml:54,58,68,72,82,86` 中的 `li[@Class="Mugirl.StockGenerator_Mugirl_Slaves"]` |
| ThinkNode | `ThinkNode_ConditionalNonPlayerWildMugirl` | 同名，改命名空间 | — | 否 | 是 |
| FloatMenu | `FloatMenuProvider_TalkCourier`、`FloatMenuProvider_TalkFusionInvestor`、基类 `FloatMenuProvider_MugirlTalkToPawn` | `FloatMenuOptionProvider_TalkToCourier`、`FloatMenuOptionProvider_TalkToFusionInvestor`、`FloatMenuOptionProvider_TalkToEventPawn` | 原版前缀（仓库内另有 5 个同类，建议一并改） | 否 | 否 |
| 静态类 | `MugirlEventUtility`（根）+ `MugirlEventMarkers`（Features） | 合并为 `EventPawnTags` | 一个关注点拆成了两个类、两个命名空间 | 否 | 否 |
| 静态类 | `MugirlWildSlaveUtility` | 拆成 `Mugirl.Core.PlayerFactionUtility`（`PlayerFaction`、`IsPlayerFaction`、`IsHostileToPlayer` ×2、`TrySetPlayerFaction`）和 `...Incidents.WildMugirlUtility` | `IsPlayerFaction` 有 31 处调用，其中 9 个文件在 Incidents 之外；`PlayerFaction` 有 5 个外部文件；`IsHostileToPlayer(Pawn)` 被 Restraints 使用（`Restraints/HediffComps/HediffCompProperties_AddTrait.cs:84`、`HediffCompProperties_EleShock.cs:226`）。按“野生奴隶”命名会误导 | 否 | 否 |
| 静态类 | `Mugirl_IdeoUtility` | `Mugirl.Core.PlayerIdeoUtility` | 下划线前缀不规范；Restraints 也在用（`Restraints/Comps/CompProperties_BrainwashHelmet.cs:232`） | 否 | 否 |
| 静态类 | `GhoulRenderingRefreshUtility`、`PawnRenderingRefreshUtility`、`MugirlMiscDefOf` | 合并为 `Mugirl.Rendering.PawnRenderRefresh`，DefOf 并入渲染模块 | 一个只有一行的包装类；“Misc”DefOf 放在 RenderRefresh 文件夹里 | 否 | 否 |
| 静态类 | `MugirlStoryService`、`MugirlStorySchedule`、`MugirlStoryDebugActions`、`MugirlFusionInvestmentUtility`、`MugirlPawnDowningUtility`、`MugirlRescueJoinUtility`、`MugirlGeneratedPawnUtility`、`MugirlQuestLauncher`、`MugirlLordUtility`、`MugirlJoinerLetterUtility` | 去掉 `Mugirl` 前缀，放入 `Mugirl.Features.Incidents`。`MugirlGeneratedPawnUtility` 被 Corporate 3 个文件使用，应放到 Core | 统一 | 否 | 否 |
| 方法 | `QuestNode_Root_Mugirl_OpeningPodCrash.SendLetter_NewTemp(Quest, Pawn?[]?, Map)`（`:223`）、`protected TryAddSpawnPawnsInOnePod`（`:87`） | `SendArrivalLetter`；改为 private | `_NewTemp` 是原版过渡期的后缀，自有方法不应照抄；这个类没有子类 | 否 | 否 |
| 文件夹或命名空间 | `Features/Newborn` | `Features/LifeStages`（或 Appearance） | 实际负责所有生命阶段的身形与婴儿头发，不止新生儿 | 否 | 否 |
| defName | IncidentDef 和 QuestScriptDef `Mugirl_CourierRaid` | `Mugirl_CourierContact` | “Raid”误导（同上）。同步改 `courierRaid*` 字段和 `Mugirl.CourierRaid.*` 翻译键 | 是，(b)（`Quest.root`、`lastFireTicks`） | 是 |
| defName | JobDef `Job_Nuzzle` | `Mugirl_Nuzzle` | 没有 Mod 前缀，有跨 Mod 撞名风险 | 是，(b)（`curJob/def`） | 是（`JobDefs.xml:56`、`DefOf/Mugirl_DefOf.cs:42`） |
| defName | ThinkTreeDef `MugirlPunisher` | `Mugirl_Punisher` | 前缀风格不一致 | 是，(b)（`Job.jobGiverThinkTree`） | 是（`ThinkTreeDefs/Mugirl_Punisher.xml:4`、`ThingDefs_Races/Mugirl_Punisher.xml:36` 的 `thinkTreeMain`） |
| defName | PawnKindDef `Mugirl_EscapeWildSlave`、`Mugirl_PreEscapeWildSlave` | 删除 | A02 | 是，(b) | 是 |
| defName | IncidentDef `Mugirl_WildManWandersIn` | 删除 | A01 | 是，(e) | 是 |
| Scribe 标签 | 见 B2 的“建议”列 | — | — | 是，(c)/(e) | 否 |
| 设置键 | `enableStructuralCrashEvent` | 例如 `enableOpeningStoryline` | A20 | 设置文件 | 否 |

不建议改的（改了只是折腾）：`Mugirl_SlaveOpeningPodCrash`、`Mugirl_SlaveRefugeePodCrash`、`Mugirl_SlaveWandererJoins`、`Mugirl_RunawayFarmQuest`、`Mugirl_RunawayPanic` 等 defName；事件标记字符串（A13）；`Administer_Mugirl_Milk`；`Mugirl_TalkCourier`/`Mugirl_TalkFusionInvestor` 两个 JobDef（只为对齐 TalkTo 不值得增加一条 (b)）。

### C3. 其他应当重构的地方（生产质量）

- **R1 Feature 边界**。Incidents 现在什么都装：
  - `Trade/StockGenerator_Mugirl_Slaves` 是商人库存，不是事件，应移到 `Features/Trade` 或 Slaves。
  - `Raids/` 是巨企敌对派系编组的点数分档，应移到 `Features/Corporate` 或 Factions。
  - `VanillaFixes/Harmony_NullRacePawnKindGuard` 修的是原版通用缺陷，与雪牛娘事件无关，应移到 `Core/VanillaFixes`。
  - 玩家派系工具应移到 Core（见 C2）。
  - Misc 同样混杂：`FixedColorApparel` 和怀孕变体服装的补丁元数据本来就标为 `"Apparel"`（`Misc/FixedColorApparel/Harmony_Apparel_DrawColor.cs:9`、`Harmony_StylingStationFixedColor.cs:10,20`），应移到 `Features/Apparel`；`MilkRecipe` 移到 `Features/Milk`；`Grazing` 独立成 Feature；`RenderRefresh` 移到 `Rendering`；`Nuzzle` 和 `Thoughts` 移到 Social 或 Mood。
- **R2 每局生命周期**。`MugirlStoryState.FinalizeInit` 顺带重置 6 个无关模块的瞬态状态（`Story/MugirlStoryState.cs:71-80`），其中 2 个是空方法（A22）。其他模块的注释都依赖它：`Core/MugirlLog.cs:19`、`Milk/Animation/MugirlMilkingAnimation.State.cs:14,43`、`Mounting/Combat/MountedCombatController.VerbState.cs:10`、`Misc/RenderRefresh/GhoulRenderingRefreshUtility.cs:3`。
  - 建议新建一个 Mod 级的 `GameComponent_Mugirl`（或 `MugirlSession`），提供“每局重置”的注册入口，并持有**存档版本戳**和升级层入口。
  - 剧情组件只管剧情。
  - 渲染刷新调度表作为这个组件的非存档成员，从而删掉 `GameComponent_GhoulRenderingRefresh`。
- **R3 状态封装**。`MugirlStoryState` 有 13 个 public 可写字段（`:10-24`），被直接改写的位置：`FusionInvestment/MugirlFusionInvestmentUtility.cs:123,178,275,292-293,311`、`Story/MugirlStoryDebugActions.cs:59`、`Story/MugirlStoryService.cs:60-71,89,112,124,134,141,148,179`；跨模块只读：`Corporate/Introduction/CorporateIntroduction.cs:75-78,233-237`、`Corporate/Missions/CorporateNetwork.Quests.cs:958-959`。应改为子状态对象加有意图的方法，例如 `CourierContact.IsResolvedOrStarted`。
- **R4 平行列表改为记录列表**（A06）。迁徙和惩罚者都按索引成对增删（`MapComponent_MugirlMigration.cs:311-318`、`MapComponent_MugirlPunisher.cs:268-273`）。改成记录后，读档对齐逻辑和 `playerKilledMigrationPawnIds` 都可以删掉。
- **R5 `MugirlFusionInvestmentUtility` 455 行**，混合了 5 件事：投资人生成与 Lord（`:18-56`）、对话 UI（`:216-267`）、白银清点与扣除（`:63-66,127-159,417-454`）、奖励投放（`:269-294,353-415`）、调度（`:58-61,306-313,333-351`）。建议拆成 `FusionInvestmentService`（状态转换，由剧情调度驱动）、`FusionInvestmentDialog`，以及通用的“扣殖民地白银”工具。白银工具可能与 Corporate 的付款逻辑重复，需要跨模块确认。
- **R6 跨模块字符串接口**。`MugirlStoryDebugActions.ParseCorporateOutcome`（`Story/MugirlStoryDebugActions.cs:124-134`）解析 `CorporateIntroduction.DevTriggerRepresentative` 返回的字符串错误码。应改为枚举或结果类型。
- **R7 “草”方案按标签识别**（`Misc/Grazing/MugirlFoodPolicyUtility.cs:10-12,22-28`）。玩家改名或切换到第三种语言后会重复生成。应在升级或新建时记录方案 id（见 A14），以 id 为准。
- **R8 事件 Pawn 的被动处理**。`Shared/Harmony_PassiveEventPawns.cs:26-50` 对被动事件 Pawn 整个跳过 `Notify_DamageTaken`，原版的 `lastHarmTick` 等记录也一起被跳过；注释（`:25`）承认这一点，只补丁几率函数的方案需要游戏内验证。这是技术债，与存档无关。
- **R9 冗余入口**。`IncidentDef Mugirl_CourierRaid` 的 baseChance 为 0（`Incidents_CourierRaid.xml:14`），注释说“只供开发者或手动触发”（`Courier/IncidentWorker_Mugirl_CourierRaid.cs:5`），而调试动作 `Story/MugirlStoryDebugActions.cs:38-62` 已经覆盖。可以考虑删掉这个 IncidentDef 和 Worker；它若曾被触发过，会出现在 `lastFireTicks` 中，删掉时需要 (e)。
- **R10 DefOf 分散**。本范围用到 5 个 DefOf：根命名空间的 `Mugirl_DefOf` 和 `MugirlContentDefOf`，Feature 内的 `IncidentsDefOf`、`MugirlNewbornDefOf`、`MugirlMiscDefOf`。`MugirlContentDefOf.cs:93-99` 中的 `Mugirl_MigrationIncident`、`Mugirl_FusionInvestmentIncident`、`Mugirl_RunawayFarmQuestIncident`（DefAlias）和 `Mugirl_RunawayFarmQuest` 在代码中**没有任何使用**。建议采用 `IncidentsDefOf` 的做法：每个 Feature 一个 internal DefOf。
- **R11 可见性**。补丁类和本模块内部工具中有不少是 public：`GhoulRenderingRefreshUtility`、`PawnRenderingRefreshUtility`、`MugirlMiscDefOf`、`MugirlNewbornDefOf`、`CourierConversationUtility`、`MugirlWildSlaveUtility` 的方法。应收紧为 internal。只有 XML 或反射需要的类型才保持 public。
- **R12 次要正确性风险**（只读代码得出，未验证）：
  - `MugirlPunisherLootBurst.MoveLootInto` 放不进 `heldLoot` 的物品留在弧线中（`Punisher/MugirlPunisherLootBurst.cs:50-60`）。它们既没有被深保存，又只以引用保存（`:213`），动画期间存档会丢失。正常情况下 `TryAdd` 很少失败。
  - `QuestNode_Root_Mugirl_WandererJoin_WalkIn` 在 Def 持有的单例节点上保存每次生成的状态（`Wanderer/QuestNode_Root_Mugirl_WandererJoin_WalkIn.cs:12-13,34-35,66-67`）。这是照搬原版的写法，QuestGen 是单线程的，所以可以接受，但应写明理由。
- **R13 注释清理**。A18、A19 的冻结理由和“与 1.6 一致”注释，在重构后统一删除或改写。

### C4. 跨模块注意事项

- **验证基线需要重做**（`Source/Tests/MugirlRace.Validation/Baselines`）：
  - `save-types.txt:7,8,9,14` 登记了本模块 4 个 GameComponent/MapComponent。“必须与冻结 1.6 同名”的 C.save-types 检查，以及从冻结 DLL 自动找出存档可见类型的逻辑，在新方向下应改为“旧名→新名映射表完整性”检查。
  - `scribe-defaults.txt` 的 `spawnCell` 条目，`dropped-scribe-labels.txt` 的 `joinedAlready` 条目，`patch-targets.txt` 中本模块的约 30 行，都需要更新。
  - 单元测试 `MugirlRace.Tests/Incidents/*`（5 个）和 `Misc/PendingRenderRefreshScheduleTests.cs` 引用了纯计算类型，改名时要同步。
- **Corporate 读取本模块的存档对象**：
  - `CorporateIntroduction.RecoverCourierHistory`（`Corporate/Introduction/CorporateIntroduction.cs:364-376`，在 `LoadedGame` 中调用，`:74`）读取 `QuestPart_SpawnCourier.courier/map`，以及 `Mugirl_CourierRaid` 任务的结局。它本身也是旧档恢复逻辑，应并入统一升级层，并在本模块 A10 之后执行。
  - `CorporateIntroduction.cs:75-78,233-237` 与 `CorporateNetwork.Quests.cs:958-959` 读取剧情状态的字段。
- **`IncidentWorker_RetiredMugirlEvent`** 位于 `Compatibility/Savegames`，由兼容模块审计。本领域的结论见 A01。
- **设置键 `enableStructuralCrashEvent`**（A20）由 Core 和设置模块负责迁移。
- **升级层设计提示**：
  - 判断是否旧档：存在 `Class="Mugirl.MugirlStoryState"` 的 GameComponent 节点，且没有新组件的版本戳。
  - 中途加装本 Mod 的存档没有任何 Mugirl 节点，只需要执行“新加装”步骤（A14 的草方案）。
  - RimWorld 的存档头只有 Mod 列表，没有 Mod 版本，所以版本戳必须放在本 Mod 自己的数据里。
  - (e) 只能在 `ScribeLoader.InitLoading` 之后、Game 的 ExposeData 之前对原始 XML 做；此时版本戳只能从 XML 中读取。
