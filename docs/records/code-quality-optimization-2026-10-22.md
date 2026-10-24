# 代码质量审查与优化（2026-10-22）

本记录保存 1.7 全部生产代码（`Source/MugirlRace`，约 6.7 万行）的质量审查结论、owner 授权的优化范围、实施结果和验证证据。约束见[决策 §17](../refactor-1.7/decisions.md#17-代码质量优化授权2026-10-22)。

## 背景与授权

- 2026-10-22，owner 要求按三个维度审查 1.7 代码：明明有原版途径却用了更冗余的写法；花大量性能换不值得的功能；正常人类 modder 明显不会这么写的代码。
- 审查后，owner 要求“按照你的想法进行优化，更新文档，并根据文档做优化”。本文件先列出审查结论和本轮范围，随后按范围实施，最后补记结果。
- 开始前的整树归档在 `DevData/Backups/PreQualityOpt-20261022/`（4941 个文件，逐文件 SHA-256 与 zip 读回核对通过），附 git HEAD、`git status` 与未提交改动补丁。owner 已有的未提交修复（巨企供货兼容、ThinkTree 来源保留等）全部保留，本轮在其上修改。
- 基线：`%TEMP%/mg17-qopt-base` 离线验证两个运行时各 45 项通过，单测 1110 项（2 项显式未运行）无失败，ShaderCheck 通过。

## 审查结论

审查由六个只读切片并行完成，Core、Platform、Mathematics、Rendering、Compatibility 与构建工具单独审阅；所有“原版已有”的说法都对照了 `RimWorld/source` 的 1.7 反编译源码。性能数字是读代码的估算，没有 profile。

### 总体判断

- **性能基本健康。** 补在热门原版方法上的补丁（`FactionUtility.HostileTo`、`Pawn.DrawPos`、`Pawn.DynamicDrawPhaseAt`、`Pawn_RopeTracker.RopingTick`、`WildManUtility.IsWildMan` 等）几乎都在入口 O(1) 早退。
- **主要问题是维度 3，其次是维度 1。** 大多同出一源：1.7 重构的“行为零变化、与 1.6 逐位一致、由验证器证明”目标留在了生产代码里，例如保持随机数调用次数、类型元数据顺序、逐位复刻 Unity 数学和 1.6 浮点运算顺序。
- 不少问题来自 ADR 约定，修改前要先改 ADR。

### 顺带发现的缺陷

| 缺陷 | 证据 |
|---|---|
| 骑乘期间，骑手身上逐 tick 的 Hediff、服装、装备和自身组件全部停止推进。`RiderTickDriver` 只照抄了 `Pawn.TickInterval`，没有原版 `Pawn.Tick()` 中的 `health.HealthTick()`、装备/能力/背包/基因推进，也没有 `ThingWithComps` 的组件 tick 与服装、装备实体的 `DoTick`。 | 原版 `Verse/Pawn.cs` `Tick()`；`Verse/Thing.cs` `DoTick()` 会推进持有者子容器 |
| 挤奶释放时同一音效同一帧播放两次。`CompMilkingDevice` 先播 `releaseSounds`，又触发时间轴组件，而 XML 中时间轴只配了第 0 tick 的同一个 `Mugirl_Milking_Sound`。 | `BondageApparel_Spacer.xml` 两处 `CompProperties_MilkingDeviceReleaseEffect` |

### 维度 1：有原版途径却手写

- 全局性：`Mugirl.Mathematics` 约 1400 行逐位复刻 Unity 2022.3 公式，而原版 1.7 有 917 个文件直接用 UnityStubs 的 `Mathf`；`GameUtility` 给 `Find.*` 套了 `Try*` 包装和别名；三处为保持 `readonly` 手写 `Scribe.EnterNode/ExitNode` 与读取标志。
- 局部：背负武器布局（8 组数，约 750 行，可用 `DrawData.RotationalData` 与 `Dialog_DebugRenderTree`）；骑枪冲锋自写冷却与瞄准（可用 `AbilityDef` 与 `Verb_CastAbilityJump`）；剧情计时、迁徙与惩罚者的手写状态机（可用 `IncidentQueue`、`LordJob` 与 `Trigger_*`）；固色服装用 3 个补丁撤销模组自己加的 `CompColorable`；量杯拖动与多选弹窗（可用 `Gizmo_Slider`）；喂奶姿态调参窗口（可用 `[TweakValue]`）；蹭蹭互动类只为加一条记忆（可用 `recipientThought`）；`ThinkNode_ConditionalRoped` 与原版同名类相同；`RopingTick` 整体替换前缀；为 5 个枚举值写的大小写解析器；白银落点扫全图（可用 `DropCellFinder`）；`Dialog_CorporateConfirm` 与 `Dialog_MessageBox.CreateConfirmation` 签名相同；通讯台与据点入口用补丁而非 `FloatMenuOptionProvider` / `WorldObjectComp`。

### 维度 2：性能花费与收益不相称

| 项目 | 频率 | 判断 |
|---|---|---|
| 订购目录为每个 Def 实际生成一件物品读市价，再用每帧 0.75 ms 分时、价格桶和稳定排序消化 | 订购页打开期间每帧 | 代价自找 |
| 全 Def 供货分析（交易商×生成器×物品矩阵、配方不动点、反射扫描模组类型） | 每次读档和新开局无条件执行 | 未解锁巨企的玩家也付出 |
| `CompWeaponWheel.CompTick` 空闲时每 tick 轮询坐骑、持械可见性与背负可见性，同一 tick 重复调用原版判定 | 每 tick × 每个雪牛娘 | 可去重与早退 |
| ThinkTree 键迁移钩子常驻（启动账本重放、每次 `Job.Clear` 移除弱表记录、读档记录全部 Job） | 每局常驻 | 单次很小，只为旧档服务 |
| 启动自检（约 60 条升级表断言、核对 `Job.ExposeData` IL、核对 28 个补丁） | 每次启动 | 毫秒级，应属测试 |

### 维度 3：正常 modder 不会这么写

- 工程层面：用 IL 改写工具（`MetadataOrder`、1088 行 `TypeOrder.txt`、764 行 `CompileOrder.props`）保持重构前的类型枚举顺序；只有 6 个订阅者的反射扫描生命周期注册表；SaveUpgrade 1.16 万行中约 3/4 来自 DOM 引擎、验证器设施与 ThinkTree 历史重放；`ReviewedSaveEngine` 拿 MVID 与它自己比较；没有生产实现的 `PostLoadUpgradeStep`、Lord 重置策略与 `EndInFlight`；领域对象为 SaveUpgrade 挂了 4 个只有一个实现的续接接口；验证设施（测试 5.6 万行、工具 1.27 万行）超过模组本身。
- 代码模式：大量“与 1.6 一致”的随机数次数、排序与浮点顺序保持；防御自家 Harmony 补丁失效的多点兜底；为 1 个音效写的时间轴组件；死代码（`CompGetSlaveApparelGear`、`ChoiceLetter_FusionInvestment`、恒真的节流和标志等）；只增加跳转的门面（`CompWeaponWheel` 拆分、3249 行的 `CorporateNetwork`）；用运行时代码修正自家 XML 笔误；渲染刷新靠定时重试。

### 做得好的部分

热路径门控一贯良好；很多地方老实使用了原版扩展点（`CompRenderNodes`、`AnimationDef` worker、`PawnFlyer`、`DiaNode`、`PrefabUtility`、`QuestUtility`）；Def 改名用 `BackCompatibleDefName` 后缀；当前版本存档读入几乎没有升级开销；Harmony 补丁逐类应用；债务敌对叠加层正确绕开了 `SetRelationDirect` 的限制。

## 本轮原则

1. 玩家可见的文案与数值不变。行为改变只限本次核实的两个缺陷。
2. 不改变存档写出结构；唯一例外是删除重复音效组件的 30 tick 瞬态字段，并在升级表登记丢弃。
3. 不再要求与 1.6 或重构前构建逐位一致、随机数调用次数一致或类型枚举顺序一致；只要求玩家可感知的规则、数值与文案不变（§17 取代 §7 的相应部分）。
4. 优先原版途径和直接写法；不新增抽象层。
5. 每项改动离线验证两个运行时与单测；缺陷修复另做游戏内验证，做不到时如实记录。

## 本轮范围

| 编号 | 内容 | 维度 | 玩家可见影响 | 存档影响 |
|---|---|---|---|---|
| Q1 | 骑乘期间按原版 `Pawn.Tick` 推进骑手的逐 tick 部分（不含 Job，沿用既有刻意省略），并补上组件的 interval/rare 推进 | 缺陷 | 修复：骑乘期间 Hediff、服装与装备组件恢复推进 | 无 |
| Q2 | 删除只播一个音效的释放时间轴组件；强力释放的音效列表补入原来叠加的普通音效 | 缺陷、3 | 修复：普通释放音效不再重复；强力释放保持两种音效 | 丢弃该组件 5 个瞬态标签 |
| Q3 | 巨企供货分析不再在每次读档和开局无条件构建，改为首次需要时构建 | 2 | 无 | 无 |
| Q4 | 订购目录报价改用不生成物品的途径——仅当显示价与成交价对全部可订 Def 逐项不变时实施，否则保持现状并记录原因 | 2、1 | 无 | 无 |
| Q5 | 武器轮盘空闲 tick 去重与早退、绘制路径的小幅调整，结果等价 | 2 | 无 | 无 |
| Q6 | 移除类型元数据排序构建步骤（`MetadataOrder`、`TypeOrder.txt`、`CompileOrder.props`） | 3 | 无（新开局中本模组组件的创建顺序改变） | 无 |
| Q7 | 生命周期注册表改为 `GameLifecycle` 中的显式调用，删除没有订阅者的事件 | 3 | 无 | 无 |
| Q8 | `ReviewedSaveEngine` 的恒真 MVID 比较改为直接条件 | 3 | 无 | 无 |
| Q9 | 小型冗余清理（行为不变）：投掷控制器计数器；蹭蹭互动的记忆改走 XML（可证等价时）；束具对象类型改用枚举解析；修正 XML 颜色笔误并删除运行时钳制；删除只为 XML 不报错而保留的未用字段；迁徙死代码；异种工具未用参数；可证等价时去掉复制原版的 `RunInt` 与通讯台入口补丁 | 1、3 | 无 | 无 |

### 暂缓与保留

| 项目 | 原因 |
|---|---|
| Unity 数学层（ADR-006） | 约 600 个调用点，`SmoothStep`/`Repeat`/NaN 等语义与 UnityStubs 不同，会改变动画曲线；需要单独一轮并做渲染实机核对 |
| ThinkTree 键迁移子系统 | owner 近期在 §14、§16 明确的策略与交付，保留 |
| SaveUpgrade 未用框架能力、启动自检、清单导出 | 验证器、夹具与游戏内框架依赖这些契约，需要连同验证器单独一轮 |
| 手写 Scribe 节点改 `Scribe_Deep` | 触及读档路径与真实 Scribe 黄金测试，单独一轮 |
| 存档字段清理（冗余标志、恒真节流字段、自维护 age 等） | 需要升级表丢弃行与夹具，单独一轮 |
| 剧情计时、迁徙、惩罚者改 `IncidentQueue`/`LordJob`；骑枪冲锋改 Ability | 行为与存档结构改动大 |
| 背负武器布局工具、扣篮、喂奶调参窗口、量杯弹窗、巨企界面套件与 decimal 金额 | 已交付玩法或 owner 在用的开发工具，保留 |
| 固色服装、怀孕贴图补丁、LE 补丁合并、`SkillRecord.Learn`、`RopingTick` 整体替换、牵引待系窗口、穿戴守卫补丁 | 涉及兼容或时序，需逐项实机验证 |
| 白银落点改用 `DropCellFinder` | 历史上修过落点问题，改用原版查找可能回到旧行为 |
| `GameUtility` 包装 | 受 `B.architecture` 的 Find/Current 位置规则约束，须与规则一起调整 |
| “与 1.6 一致”的随机数与顺序写法 | 只有可读性收益；新代码不再要求 |

## 实施结果

各项先在 `%TEMP%` 中的独立整树副本里修改并各自通过离线验证，再按文件归属合入开发目录。所有合入文件逐一确认与开工快照一致后才覆盖，原有 CRLF/LF 与编码保持不变。

| 编号 | 结果 | 主要改动 |
|---|---|---|
| Q1 | 完成 | `RiderTickDriver.Tick` 每 tick 推进骑手：组件 `CompTick`、每 250 tick 的 `TickRare`、`HealthTick`、装备/能力/背包/基因推进、效果器，以及背包、搬运、装备、服装等子容器中物品的 `DoTick`；interval 部分补上组件的 `CompTickInterval`。不调用 `rider.DoTick()`：它会为非世界 Pawn 推进 Job 与常量思考树，也会在骑手未生成时重置连射中的骑乘 verb。骑乘远程 verb 仍然每 tick 恰好推进一次，推进时以载具为施放者（`MountedCombatController.BeginVerbTick`） |
| Q2 | 完成 | 删除 `CompMilkingDeviceReleaseEffect`、其属性类与 `MilkReleaseSchedule`；两件榨乳器的 `powerReleaseSounds` 在强力音效后补入普通音效。升级表把旧组件的 5 个瞬态标签由改名改为丢弃（`DropLabel`，按旧 Def→组件关系选中两件榨乳器）；删除验证器动态查找允许清单中对应的一行 |
| Q3 | 完成 | `CorporateNetwork.LoadedGame` 只在已开户时预建订购目录与供货分析，`StartedNewGame` 不再预建；两者本来就是只依赖 Def 的进程级惰性缓存，首次使用（开户后的每周刷新、终端页面）时构建，结果不变。新增单测确认未开户的读档与开局都不构建 |
| Q4 | 未实施 | 显示价与成交价一致，是因为两条路径都用同一确定性种子实例化物品。不实例化的市价途径会漏掉只对真实物品生效的部分：Odyssey 独特武器词条（13 件 `*_Unique`）、Royalty 灵刃词条、生物编码、装填与耐久等 StatPart，以及模组组件。不改价格 |
| Q5 | 完成 | `CompWeaponWheel` 每 tick 只求一次骑乘判定与原版 `CarryWeaponOpenly`（空闲时 2→1 次，下骑那一 tick 3→1 次）；收枪/备枪且无轮射、无剑舞时跳过已证明为空操作的推进；动画绘制在确认有武器要画后才取身体位置；骑枪落地拖尾加全局收回窗口，窗口外不再逐 Pawn 查弱表。没有把任何工作移到 `CompTickInterval`（延迟在画面上可见）。新增 7 项单测 |
| Q6 | 完成 | 删除 `Source/Build/MugirlRace.MetadataOrder`、`TypeOrder.txt`、`CompileOrder.props` 及工程导入，共 7 个文件、2387 行；发布脚本的新鲜度检查不再列 `TypeOrder.txt`；README、ADR-009、发布清单、巨企设计文档同步 |
| Q7 | 完成 | 删除反射注册表（4 个文件及其单测）与 `RenderRefreshLifecycle`；新的 `Mugirl.Features.GameLifecycle` 按原顺序显式调用清理（读档/新游戏前：牵引待系登记；Storyline.FinalizeInit：日志限次键、挤奶动画、骑乘 verb 登记、延后渲染刷新）。没有订阅者的 FinalizeInit/StartedNewGame/LoadedGame/AddedToExistingGame 事件一并删除。放在 Features 根命名空间，Core 仍不依赖具体功能；验证器负对照工程中两条失效的源码链接同步删除 |
| Q8 | 完成 | `ReviewedSaveEngine.ReadersAvailable` 取代拿 MVID 与自身比较的 `IsReviewed`；`CanonicalReaderToken(MethodBase)` 在内部核对方法所属模块；`UnresolvedThinkTreeKeys.ProtocolVerified` 直接取 `JobSourceWriteProtocol.Supported` |
| Q9 | 部分完成 | 见下 |

Q9 已完成：
- 删除投掷控制器计数器 `ThrowControllerRegistry`。`ListerThings.ThingsOfDef` 本身就是字典查找，没有控制器时返回空列表，结果不变。
- 束具“对象类型”改为由原版 XML 加载器解析的 `RestraintPawnType` 枚举。XML 中仅有的两个值改为成员名 `Colonist`、`Slave`，删除手写解析器及 17 项单测。
- 洗脑表演 3 个 HediffDef 中 39 处 `(239, 70, 274)` 改为 `(239, 70, 255)`。原版按字节解析时结果与原来运行时钳制后的值逐位相同，`ClampTextColors` 随之删除。
- 删除只为 XML 不报错而保留、运行期从不读取的字段：`labelWhenActive/labelWhenInactive`（连同 2 个 DefInjected 键和中英各 2 个孤立 Keyed 键）、`effectCycleInterval/maxCycles`、`powerFilthDefName`。
- 删除迁徙登记中可证恒不生效的 `CompactForRegistration` 与重复检查。它只在没有进行中迁徙时调用，成员表必为空；`refactor-1.7-incidents` 记录中保留它的说明以本记录为准。
- 删除异种出生规则中从未有调用方传入的两个参数。
- 坠舱任务在 `GeneratePawn` 之后的重复校验缩为空值检查。`GeneratePawn` 只返回 null，或已通过同一校验、存活且未销毁的 Pawn。

Q9 未实施：
- **蹭蹭互动改用 XML `recipientThought`**：原版会按发起者的社交影响缩放心情，并记录 `otherPawn`，心情数值、分组和存档都会改变。
- **坠舱任务改用基类 `RunInt`**：原版在缺地图时抛空引用，Pawn 生成失败时会带着空 Pawn 继续。
- **通讯台入口改用 `FloatMenuOptionProvider`**：选项在菜单中会移到工作类选项之后。

游戏内验证框架（`Source/Tests/MugirlRace.GameValidation`）固定了被观察方法的 IL 指纹。逐项重算后，只有 `CorporateNetwork.LoadedGame` 与 `GameComponent_Storyline.FinalizeInit` 两个方法变化（共 3 个常量）。两处投影只描述组件的存档字段：前者只改变进程级目录缓存何时预建，后者只改变瞬态清理的调用方式，都不改写存档字段。核对源码证据后更新了常量，其余 90 项不变。

规模：生产 C# 由 852 个文件、66819 行变为 842 个文件、66440 行；构建期类型排序工具 7 个文件、2387 行删除；单测由 112 个文件变为 113 个。本轮暂缓的大项（见上表）是主要的结构性冗余，代码量的明显下降要等后续轮次。

### 行为变化

- 骑乘中的骑手按原版容器中未生成 Pawn 的方式推进，与商队、运输舱中的 Pawn 一致：
  - 逐 tick 的 Hediff 组件（包括束具的电击、延迟触发、压制增强、洗脑表演）、所穿服装与装备的组件、背包物品（食物会腐烂）、每日服装磨损都会推进。
  - 骑手自身组件也会推进：雪牛娘骑手骑乘期间继续产奶，武器轮盘进入设计中的 `DisabledMounted` 状态。
  - 效果器显示在载具所在格。
- 普通挤奶释放音效由同一帧两次改为一次；强力释放仍然播放强力音效加普通音效。
- 未开户的存档与新开局在加载画面不再构建巨企目录和供货分析。这项工作移到开户那一刻，每个进程一次，在主线程上执行。已开户的存档不变。
- 其余各项不改变玩家可见的规则、数值、文案或存档写出结构。删除的 5 个瞬态标签在 schema 1 存档中只是原版不读取的残留节点，下次保存后消失。

### 新发现，只记录不处理

- **git 中的贴图目录大小写与磁盘不一致。** git 索引记录 `Textures/Things/Item/Equipment/MuGirlWeapon`，磁盘与 XML 用 `MugirlWeapon`。从 git 检出的副本会得到错误大小写，1.7 按区分大小写查找贴图，`C.textures` 会报 8 个贴图缺失。发布包从开发目录生成，现有 ZIP 不受影响；修正需要 owner 授权的 git 大小写改名。
- **`HediffComp_AddMentalState` 的一次性触发在 Pawn 未生成时也会被消耗**（注释说明与 1.6 相同，商队中已是如此）。骑手现在会推进 Hediff，骑乘中到点也会被消耗。若希望等到重新生成后再触发，可让 `TryTrigger` 在未生成时返回 false，与 `AddThought`、`AddTrait` 一致。
- **`BrainwashPerformancePlayer` 在 Pawn 未生成时结束表演**（商队中已是如此），骑乘时同样会结束而不是暂停。
- **`CompProperties_MilkingDevice.powerFilthCountRange` 也从不被读取**，下一轮可删。
- **`tools/Test-Docs.ps1` 有 2 个原有失败**：README 与 ThinkTree 记录链接的 `DevData/Releases/雪牛娘1017-兼容修复.zip` 不存在。不是本轮造成，未改动。

## 验证

| 项目 | 开工基线 | 本轮最终 |
|---|---|---|
| `Validate.ps1` 两个运行时 | 各 45/45 | 各 45/45，结果一致（65 个检查结果及失败/警告键） |
| 单元测试 | 1110 项，0 失败，2 项显式未运行 | 1081 项，0 失败，2 项显式未运行 |
| ShaderCheck | 通过 | 通过 |
| 验证器自检（负对照） | — | 两个运行时 209 条规则全部按预期触发 |
| strict 存档升级（两个运行时） | — | 均通过，45/45 |
| `--save-corpus`（5 个 P 夹具副本） | — | 全部 Upgraded，动作数 322/850/1188/1338/1312，与改动前一致；二次运行零动作 |
| GameValidation OfflineTests | — | 406 项，0 失败，1 项跳过（副本中无夹具）；指定夹具目录后 `FixtureArchiveTests` 5/5 |
| GameValidation 测试模组 Full / Plumbing 构建 | — | 0 警告、0 错误 |

单测净减 29 项，各项来源如下：

| 来源 | 变化 |
|---|---|
| 删除注册表单测 | −10 |
| 删除解析器单测 | −17 |
| 删除释放排程单测 | −10 |
| 释放标签丢弃测试改为双 Def 理论 | +1 |
| 迁徙死代码单测 | −1 |
| 巨企惰性构建单测 | +1 |
| 轮盘与骑枪单测 | +7 |

strict 覆盖：已发布构建中旧释放组件的 5 个标签由改名变为丢弃；仅出现在开发构建中的 5 个同名标签变为未处置，属于提示级，必需行未处置数仍为 0。

开工前整树归档在 `DevData/Backups/PreQualityOpt-20261022/`。验证证据（基线与最终的 Validate 日志及两份报告、strict、存档语料、OfflineTests 在指纹更新前后的输出、契约指纹对照、自检日志、存档名覆盖表）在 `DevData/Evidence/QualityOpt-20261022/`，附 `SHA256SUMS.txt`。

### 尚未做的验证

本轮没有部署到 `1.7/Assemblies`，也没有运行游戏，正式 DLL 未变。部署并实机验证后才能交付，需要 owner 授权。建议检查：

- **Q1 骑乘**：开发者模式、临时存档目录中，雪牛娘载具搭载人类骑手。
  - 骑手带 `CryptosleepSickness`，600 tick 后剩余时间减少 600。
  - 新造护盾腰带随骑手上骑，300 tick 后能量回升。
  - 骑手戴电击项圈或磁力镣铐 2500 tick：无电击、无报错。
  - 骑手装备 `Gun_Minigun` 时，载具连射仍为每 5 tick 一发、每轮 25 发；装备破片手雷时照常投掷。
  - 骑手始终没有 Job，且保持骑乘。
  - 雪牛娘骑手骑乘期间奶量上升，轮盘为 `DisabledMounted`，下骑后恢复。
  - 骑乘中存读档后状态延续。
- **Q2**：普通释放只听到一次音效；强力释放两种音效都在。
- **Q3**：
  - 新开局（开发者模式）加载期间不出现供货诊断。
  - 开户后正常生成每周备货。
  - 已开户存档的目录数量与报价不变，下单价等于显示价；`-Suite corporate-supply` 通过。
- **Q5**：
  - 征召、解除征召时抬枪动画在同一 tick 开始，背负武器隐藏与显示正确。
  - 全火力轮射、剑舞冲刺、骑乘与下骑后状态正确。
  - 骑枪落地拖尾约 18 tick 收回。
  - 投掷举起与飞行期间禁火，落地后恢复。

## 部署（2026-10-25）

按[决策 §18](../refactor-1.7/decisions.md#18-觅食性能修复的部署与-git-同步2026-10-25)，本轮全部改动随觅食性能修复一起正式部署，DLL SHA-256 为 `C3ED6AA47454470F4EC61C2DDCAE114BA6ACEB97A2330C16FE4904003507DF5A`。部署后的自动检查与旧档夹具矩阵见[觅食植物扫描性能调查](performance-food-search-2026-10-25.md#部署与提交2026-10-25)。

- Q3 的 `-Suite corporate-supply` 已在正式 DLL 上通过。
- 上面“尚未做的验证”中 Q1、Q2、Q5 的手动实机项没有逐项核对，仍按未验证披露。
