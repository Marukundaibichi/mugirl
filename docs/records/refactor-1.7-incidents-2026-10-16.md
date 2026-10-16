# Wave S · Incidents 状态重构与旧档升级

日期：2026-10-16。以 [decisions §7](../refactor-1.7/decisions.md#7-owner-补充约束2026-10-15恢复重构后优先于所有既有计划) 的玩家文案与行为冻结要求约束实施。[旧 Incidents 计划](../refactor-1.7/module-plans/incidents-plan.md) 中的绝对 tick、hash interval、删除内容和修复事件结果不再自动有效。

## 结构与保持的语义

剧情组件改为持有 `OpeningCrashState`、`CourierContactState` 和 `FusionInvestmentState` 三个值聚合，以及独立的瞬态 `StorylineClock`。`EventStateScribe` 在组件原回调中同步暴露聚合，保持引用解析与加载维护的顺序。外部业务通过 `OpeningCrashStarted`、`CourierTriggered`、`CourierQuestStarted`、`InvestmentAccepted`、`InvestmentPending` 只读接口查询；Corporate 两个调用文件由协调者同步。

所有剧情倒计时继续保存剩余量，未转换为绝对时间。时钟仍在首次有效 tick 按 elapsed=1 结算，未到唤醒时不扣倒计时；仍按 Courier → Opening → Fusion 执行。已触发与已发起 Quest 两个 Courier 标志、投资已接受／等待结果／投资人活动标志继续独立存在。没有根据当前 Quest 或空引用擅自推导状态。`FinalizeInit` 中原位置的 `TransientStateReset` 分派和之后的幼年体型维护顺序保留。

投资逻辑拆成状态编排、`FusionInvestmentDialog`、`FusionInvestmentRewards`、`EventSilverLedger`。白银仍正序计数、逆序扣堆叠；奖励仍先食品、后 Pawn，再空投、发信、清除待处理状态。八个迁出的方法在恢复状态字段访问及调用限定名后，方法体 token 与修改前完全相同。

迁徙成员改为 `MigrationMember(pawn, grazeAtTick)`，批次事实进入 `MigrationState`。保留剩余停留时间、击杀窗口、原始人数、计数、独立离开标志及完整的玩家击杀 ID 列表。不会提前移除空 Pawn 引用，也不会把未匹配到当前成员的历史 ID 丢掉。

惩罚者改为 `TrackedPunisher(pawn, spawnTick, leaving)`。离开标志仍是保存的独立事实，不按时间反推。`MapComponent_Punisher` 继续持有原 `ThingOwner`，父链不变；战利品飞散数据、引用及其加载、tick、draw 次序不变。

`MugirlEventMarkers` 的常量和解析并入 `EventPawnTags`，保留三个标签值与序数比较。现有解析测试随入口更新。没有删除未知 questTag。

## 一次性升级与原执行时点

实现位于 [SaveUpgrade/Modules/Incidents](../../Source/MugirlRace/SaveUpgrade/Modules/Incidents)。生成的 R 类型／Def 映射不变，partial `RegisterStructure` 新增三个步骤及四条明确的在途 Job 保留策略。

| 步骤 | 处理 |
| --- | --- |
| `Incidents.StorylineState` | 13 个旧平面标签进入三块状态；所有值和引用逐字搬移 |
| `Incidents.MigrationRecords` | 两个平行列表按原索引配对；8 个批次字段搬移；把 dataVersion 与缺少日程的状态转为持久初始化 |
| `Incidents.PunisherRecords` | Pawn／spawn tick／leaving 按原索引配对；原样重挂 heldLoot 和 lootBursts |

步骤使用中央 `AppendElement`／`Move` 重挂原节点，不 clone + Remove 资产，不改写 Pawn 引用或物品 load ID。只丢弃原加载对齐过程必定截掉的过长平行列表尾部。新旧格式同时出现时中止，避免覆盖账本。

迁徙旧 `nextGrazeTicks` 缺项不能直接补存档 tick：原代码在 MapComponent 的 PostLoadInit 用 `当前 tick + Rand.RangeInclusive(30, 300)` 补齐，而且空 Pawn 槽同样消费抽取。升级保存 `MigrationInitialization.firstUnscheduledIndex`，在相同组件回调点、相同顺序补齐。具体旧版本解锁规则也在该升级层类型内，严格先补日程、再对每张需要处理的地图执行原全 Pawn 解锁遍历，最后设置 outcomeFailed。`dataVersion < 2`（含缺省 0）的谓词保持不变。

初始化有 public 无参构造和明确 Class，保存后可通过真实 Scribe 恢复。每次成功写入一个日程就推进持久游标；后续解锁或初始化发生异常后再次保存，不会重复抽取已完成的随机数。只有整个完成动作成功才清除 marker。

惩罚者缺少 spawn tick 时也在原 PostLoadInit 位置读取 `TickUtility.CurrentGameTickOrFallback(0)` 补齐。没有把它提前替换成 DOM 中猜测的时间。离开标志缺省仍为 false。

保留四个历史 JobDriver 的 Toil 与在途状态：`JobDriver_Nuzzle`（三步）、`JobDriver_ReadCourierDiary`、`JobDriver_TalkCourier`、`JobDriver_TalkFusionInvestor`（后三者两步）。没有结束当前 Job、删除队列或重置 Toil 索引。QuestPart 的顺序和引用、Runaway 剩余检查 tick、可选投资信件及其地图均不变。

## 对旧计划的约束与发现

- 不删除 Courier Incident、FusionInvestment ChoiceLetter、旧野生 PawnKind、有效翻译或活 Def。未决投资信仍有投资、暂缓和拒绝功能。
- 不把 non-player PreEscape 种类合并为野生种类；派系转换的当前判定保持原样。
- 每次加载的草政策补建、正面怀孕心情维护、玩家事件标签与 PawnKind 规范化保留为现有领域不变量。不能因为原注释称为“迁移”就改成只执行一次。两个全 Pawn 扫描仍先全部清标签，再全部改 kind，不合并成改变回调顺序的单次循环。
- Courier 的目标列表为空时，仍按原任务与 QuestPart 顺序查询开局目标；不提前缓存推导出的 Pawn。日记标题、原文案和原信号不替换。
- Runaway 不改 hash interval，不丢失 ticksUntilNextCheck，非检查 tick 的动作不动。惩罚者 TryAdd 失败的处置也不顺手修复。
- 迁徙登记会先清理无效 Pawn，再按剩余 Pawn 顺序使用旧日程列表的前缀。即便这让一个成员接到前一槽的日程，也属于现有行为；`CompactForRegistration` 特意保留，差分测试覆盖 `[A, null, B]` 与 `[100, 200, 300]` 得到 `[A, B]` 和 `[100, 200]`。
- 退休 WildMan Def、文字和空执行语义保留；worker 已由 Core 移至 SaveUpgrade，本模块未重复处理。

### ThinkTree key 不能删除来“修复”改名

直接复读 4979 `ThinkTreeKeyAssigner` 后确认，旧计划 X3 的说明不完整。节点初始 key 来自自身及 parent 链的 **类型短名**，逐级 `Gen.HashCombineInt` 与 `GenText.StableStringHash`。只有碰撞全局 assignedKeys 集合时才 xor `Rand.Int`；defName hash 用于给这段随机序列设 seed。1.6 MVID `61e4173561894da49d210260257b5097` 的该类型反编译源码与 4979 相同。

4979 MVID `425a593954d7432a8f44e3076b25fce9` 的关键 token：Reset `0x06000D17`、AssignKeys `0x06000D18`、AssignSingleKey `0x06000D19`、NextUnusedKeyFor `0x06000D1A`。`ThinkTreeDef.ResolveReferences`（`0x06000D1C`）先调用 AssignKeys，再设置 parent 链，因此读档后根据最终树重算不能证明正确。

AssignSingleKey 在当前游戏程序集只有 `ThinkNode.DeepCopy(bool)`（`0x06004605`）一个直接调用方：递归复制子节点、可选 ResolveSubnodesAndRecur 后，以常量 seed **0** 调用；不能按碰巧相等的整数把该 seed 当成 defName 改写。

`Job.ExposeData`（`0x060042BF`）只在 **PostLoadInit** 调用 `ThinkTreeDef.TryGetThinkNodeWithSaveKey`（`0x06000D1E`）；保存时再从实际 jobGiver.UniqueSaveKey 写 lastJobGiverKey。正确边界是原分配过程中的独立 shadow key 表与本次 legacy 会话的解析 alias，成功绑定后自然写 canonical key。中央实现由协调者负责，本模块没有清空或删除 key。

本模块可直接证明的冻结 1.6 与当前 Punisher XML 拓扑均为 Priority → RegisterPunisher、Subtree(LordDuty)、IdleError，注册节点短名与树 defName 改变。没有完整实证覆盖任意旧依赖的 LordDuty 展开、第三方插入、全局碰撞占用顺序和所有历史发布拓扑，不能声称任意旧 key 都已可映射。

## 验证与交接

- 修改前状态：`%TEMP%/mg17-incidents-preS-status.txt`；所属源码副本：`%TEMP%/mg17-incidents-source-preS`。首次工作区基线撞到其他 owner 移动兼容文件的构建窗口，已记录为采样失败，随后改用不可变的独立 TEMP Source 快照构建。
- 独立 Release：0 警告、0 错误。当前 Incidents 目标集 **73 项全部通过，无跳过**，日志 `%TEMP%/mg17-incidents-target.log`。没有部署、启动游戏或改动 1.6。
- 三组 golden 覆盖 29 个消费标签、完整生产 DOM 流水线及重复运行。真实 Scribe／CrossRefHandler 测试覆盖同一个 Pawn 对象的引用、空槽、地图父链和资产 holder、初始化前保存重载、完成后 canonical 保存以及异常进度。
- 10,000 组时钟差分覆盖首次 tick、重复 tick、唤醒前等待、stale 检查和 int 边界。随机补日程测试比较剩余 Rand 状态，证明抽取次数与次序一致。
- `%TEMP%/mg17-incidents-v2.log`：标准 CLR 与 Waylith CLR 的结果一致；Incidents 的 C.save-upgrade 没有新增失败。全仓当时仍有其他模块施工和中央基线未更新的失败，不冒充最终绿色验收。
- `%TEMP%/mg17-incidents-reverse-audit.json`：在显式还原状态访问与 helper 限定名后，231 个原方法 token 相同；8 个迁出的投资业务方法体相同；其余 28 个差异为状态序列化、已审查的记录索引适配、初始化隔离或时钟提取。中文文本与 Mugirl 玩家键字面量多重集无增删。
- 引擎完整取证文件：`mg17-incidents-thinktree-engine-evidence.json`、`mg17-incidents-thinktree-singlekey-caller.json`、`mg17-incidents-job-exposedata-evidence.json`，均位于 TEMP，已交协调者归档并补入迁移知识库。

中央还需刷新 ownership／根 README，并删除 scribe-defaults.allow.txt 的 `MapComponent_Migration::dataVersion` 陈旧例外。ThinkTree alias 的中央测试与实机旧档验收仍属集成闸门；本模块离线结果不替代完整游戏中的剧情、迁徙、惩罚者及各类旧任务验证。

## 缺组读档边界复核（2026-10-16）

EventStateScribe 与 Storyline／Migration 调用点现记录每个状态组在本次 LoadingVars 是否实际存在。后续 CrossRefs／PostLoadInit 只执行读过的组；保存仍写全部规范状态。这样避免缺失组在引用阶段领取不存在的登记，同时保留缺省计时器、首次执行时点与当前每次加载的不变量。

原测试只断言结果值，不能排除被原版记录后继续运行的错误。本轮将真实 CrossRefs／PostLoadIniter 和零 Log.Error 断言纳入往返，补充全缺组／部分组／空组负控。Corporate／Incidents／Mount 联合 **112/112** 通过，独立构建 0 警告、0 错误，日志 `%TEMP%/mg17-group-fix-target.log`。测试 HAR 依赖由中央补齐，未把实际类型解析错误列为可忽略项。
