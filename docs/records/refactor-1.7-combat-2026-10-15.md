# Wave S 战斗模块实施与行为守恒记录

日期：2026-10-15 开始，2026-10-16 离线收尾。适用范围为 WeaponWheel、Lances、Weapons，以及六个战斗兼容适配器；Mounting 状态拆分由同波次独立负责人完成，登记在本模块中。

约束以[决策 §7](../refactor-1.7/decisions.md#7-owner-补充约束2026-10-15恢复重构后优先于所有既有计划)为准。[原模块计划](../refactor-1.7/module-plans/combat-plan.md)中的行为改变不再自动执行。本记录描述实际落地结果，不把计划等同于实现。

## 组件与兼容边界

`CompWeaponWheel` 保留 ThingComp 生命周期、每 tick 调度、公开与内部调用门面及 IThingHolder 身份。四个内部对象分别负责储备和槽位、战斗状态、剑舞、渲染缓存与可见性查询。储备 `ThingOwner<ThingWithComps>` 的 owner 仍是组件本身，武器的 holder 链仍为武器 → 组件 → Pawn；没有把 holder 改成协作者。

每 tick 顺序保持：原版基类 → 集合不变量 → 无 Pawn/已销毁/死亡提前返回 → 读档后的主槽整理 → 原 `nextMaintenanceTick` 维护 → 装弹缓存 → 储备 Verb → 骑乘状态 → 武器公开持有状态 → 剑舞位移 → 待执行剑舞 → 战斗状态 → 背负可见性。兼容 tick 回调在该流程正常返回后执行，包含提前返回；异常时不执行。没有改成 hash 调度，没有删除每 tick 的集合检查。

`WeaponWheelHooks` 和 `MountingHooks` 替代 10 个 Compatibility 对本模组方法的 Harmony 自补丁。`CombatCompatibilityBootstrap.Register()` 在 Mod 构造中、原 Harmony 安装紧后幂等登记，仍先读取各适配器的 Active 开关，外部程序集成员仍在首次使用时解析。外部原版目标上的兼容补丁保留。

| 边界 | 保留的行为 |
|---|---|
| 火力满载资格 | 内建结果为 true 时才按 DualWield、RunAndGun 顺序否决；命中即短路 |
| 施放与外部装备 | 副手绕过在原方法体之前；TrainingFacility 检查留在原三个入口；外部副手通知先过滤 |
| 主手切换 | 副手暂存 → 捕获原主手 → 原操作 → 归还副手 → 成功且实际换手才通知；异常仍归还一次 |
| 生成武器 | Yayo 初始弹药回调仍先于参数校验 |
| 备用弹药 | Yayo 回调中的原 60 tick 检查、枚举顺序、条件和失败闩不变 |
| 骑手近战绘制 | MeleeAnimation 观察入口仍在原领域 guard 之前，结束或异常后恢复原设置实例；每帧不装箱状态 |
| 身体坐标 | 在原 DrawPos 读取点调用 Yayo，跨边界只逐分量复制到 Numerics |

实际 Harmony 3 对照测试发现：DualWield Prefix 返回 false 后，MeleeAnimation 的只观察 Prefix（带 out state）仍会运行。实现保留此细节，不能误解为所有后续 Prefix 均被跳过。原优先级 400/399 对应的恢复先于通知顺序亦保留。诊断中的四个兼容开关文字没有改为新文案。

轮盘绘制补丁原来直接检查 `PawnFlyer_LanceCharge`。现在在同一 Prefix、同一位置检查内部能力接口 `ICustomEquippedWeaponDrawOwner`，唯一实现仍是该 sealed flyer；没有增加一个新的 Harmony Prefix 改变全局排序。此接口无状态和方法，是明确的绘制所有权标记。测试直接读取本 DLL 的 InterfaceImpl 表证明唯一实现，避免加载不相关的可选 HAR 类型。中央空类型清单需以这一生产用途登记理由。

## 保存结构和一次性升级

| 保存范围 | 新标签 / 升级方式 |
|---|---|
| 轮盘储备容器 | `mugirlWheelReserveWeapons`；旧节点原内容搬移 |
| 轮盘槽位 | `mugirlWheelSlotRecords/li/{weapon,unlocked}`；`Combat.WheelSlotRecords` 按原索引配对武器引用和解锁值 |
| 剑舞失败数 | `mugirlWheelSwordDanceFailureStreak`；保持原数值和原 PostLoadInit clamp |
| 骑枪冷却 | `mugirlLancePointCooldownUntilTick`、`mugirlLanceLineCooldownUntilTick`；保持绝对截止 tick |
| 骑乘 | `Combat.MountState`；骑手容器、开火标志、四个累计计时器、全部七个炮塔字段迁入明确状态组，不丢计时债务或 `aimTicksTotal` |

旧槽位两列表长度不同时按较长列表保留每个已有值，缺失部分由新 reader 的原默认值处理；不预先猜测研究解锁。旧记录的引用 XML 节点原样搬入，不按全局字符串替换玩家文字。旧、新形状混杂或不能证明为平面列表时在计划阶段拒绝，避免先改一半。不存在旧标签的 Pawn 不凭空新增升级节点。组件自身的缺数据守卫仍用于 Def 后加组件和运行期不变量。

轮盘纯算法使用受约束的结构体视图访问记录，不构造投影列表或每次调用委托。原双列表纯计算入口只作为通用算法调用形式保留；领域状态已经只有一份槽记录列表。存档旧字段名只在 SaveUpgrade 中消费。

游戏验证夹具需要改用协作者路径：`comp.storage.reserveWeapons`、`comp.storage.slots`（每项为 `WeaponWheelSlot.Weapon/Unlocked`）、`comp.swordDance.swordDanceFailureStreak`；战斗字段位于 `comp.combat`，渲染缓存位于 `comp.renderCache`。中央 SaveOwnerCatalog 需识别新 wrapper 的范围，不能用空比较代替数据守恒。

## 骑枪在途任务

证据位于 `DevData/Evidence/Resume-PreR-20261015/InFlightAudit/`：47 个发布记录、40 个不同 DLL，配套源快照、完整反编译、`policy.json`、`index-maps.json`、`audit-findings.txt`。0920/a172c15 为 Goto → 瞬时冲击两段流程；0920.1 的发布 DLL 已改为单段起飞流程，早于旧计划用来划分版本的源码提交。不能按游戏版本或提交日期猜模组布局。

`Combat.LanceContinuation` 只处理确切的两个 charge JobDef 和当前 Lance driver。规则如下：

- `count=1` 是发布 writer 的已起飞标记；保留当前单段驱动和所有保存状态。未知其它正值不冒充已核实标记。
- `count=-1`、当前索引 0/1、Thing 目标符合旧步行布局时，唯一改动是 curDriver 的 Class，指向 SaveUpgrade 内的 `JobDriver_ResumeLanceCharge`。保持当前/下一 Toil、wantBeginNextToil、ticksLeft、目标、job loadID、冷却和已经发生的效果；不把索引改成 -1，也不重放当前 initAction。
- 未开始的 `cur=-1/count=-1/Thing` 状态同时可能来自旧步行或现代起飞，无法证明下一步相同，因此明确失败并保留原档。只有现代直线冲锋的有效纯地格目标，或精确的已起飞标记，才能证明未开始状态使用现代驱动。错误 JobDef、无明确当前索引、未知边界状态同样拒绝。

续接驱动的 12 个业务方法和属性与发布源 a172c15 按语法 token 对比一致；仅规范化已完成的类型/Def 名字迁移、nullable 标注及 `Mathf.Min` 的等价数值入口。RNG、伤害、耐久和追加骑手攻击调用顺序没有修改。该发布版的 hitThings 与 smokeTick 从未保存，无法恢复未写入文件的数据；首次续接按原 reader 的空集合/0 开始。升级后这两个剩余动作状态显式保存，保证再次保存、读回时不重新丢失。

真实 Scribe + CrossRefs + PostLoadIniter 测试覆盖旧当前索引 0 和 1：重建两个 Toil，保持 cur/next/want/ticks，不调用 Goto.initAction，引用解析为同一目标；连续两次保存一致。新任务仍使用正常 `JobDriver_LanceCharge`，没有修改 JobDef 指向续接类。Mount、Dismount、LoadWeaponWheel 三类在已发布版本中保持相同 Toil 序列，登记精确 Preserve。

独立发布 DLL 审查再次核实：p.zip.0920 为 Pawn 目标、两 Toil、不写 count；p.zip.0920.1 在 MakeFlyer 前写精确 count=1，失败写 0。Line 的有效 Cell-only 目标与旧 Pawn-only writer 互斥。续接中的 DamageInfo、StunFor 省略参数与 4979 默认实参一致，半径、烟计数、Rand、伤害、眩晕、耐久及骑手追加攻击次序逐项相同；未发现其它需要改代码的项。

引擎核验：1.7.4979 rev109 的 Assembly-CSharp SHA256 为 `A97A01C4319414414C0CA39F3565215E83300DC608DC94B69A27720095B4E751`，MVID 为 `425a5939-54d7-432a-8f44-e3076b25fce9`。`JobDriver.ExposeData` 在已核实的 1.6.9438、1.6.9676、1.7.4979 中相同：强制保存当前索引，PostLoadInit 只 SetupToils；`Pawn_JobTracker.ExposeData` 保留存档指定 driver 类并恢复 pawn/job 关联。详细成员证据保存在上述 InFlightAudit 中。

## 未执行的旧计划条目

以下并非遗漏，而是与行为守恒冲突或证据不足：不删除四个骑乘累计计时器或炮塔 aim total；不改维护/烟雾/镜头计时相位；不把 flyer 的 lastPassableCell 扫描提前到 SpawnSetup 或近似为起点；不删除仍用于新飞行与每次读档不变量的 null guard；不新增剑舞静态计数清零时点；不限制布局 override 只在 DevMode 读取；不扩大骑乘资格；不删除 `MugirlOnlyEquipmentExtension.enabled`、现存 Def/标签/翻译或改写占位说明。25 个本模块内容文件与开工基线 SHA256 全部相同。冻结 `MountedPawnUtility.IsMugirl` 仍是真实判定入口并保持 NoInlining。

## 验证与集成事项

独立源码副本：`%TEMP%/mg17-combat-s/Source`，Release、.NET SDK 10.0.401，输出均在该临时目录。未部署，未启动游戏，未修改 1.6 或 owner 配置/存档。

- 构建：0 警告、0 错误。
- Combat、WeaponWheel、Mounting 综合定向回归：129/0，包含缺状态组的真实读档负控、四计数器差分与十万步推进 0 托管分配。
- 当前隔离快照的完整 GoldenFixtureTests：2/0，验证全部已登记结构步骤都有金样、完整流水线结果与金样一致且重复运行不变。
- Hook 专项 10/0，包含真实 Harmony 对照、实际 CompTick 提前返回/异常边界、唯一绘制接口实现。
- Lance 专项 26/0，包含真实 Scribe 往返、歧义失败及原 XML 不变。
- 轮盘组件真实保存专项 2/0；槽位结构、纯算法、holder 与引用测试另在综合定向套件中运行。
- Lance、轮盘组件、槽记录三个真实 Scribe 测试均捕获并断言 0 原版 `Log.Error`。离线场景显式登记测试/模组程序集，并使用不初始化图形资源的最小 ThingDef；不得依赖原版回退创建对象后“结果仍可用”的假通过。测试工程的 HAR 运行依赖由主代理补齐，不进入生产部署。
- 源码审计：旧 Lance 12/12 业务体在明确名字/nullable 规范化后相同；轮盘拆分前 120 个方法体中 90 个经字段归属/记录访问规范化后直接相同，其余差异逐项属于协作者调用门面、单列表存储和新 Scribe 形状。此检查辅助复核，不替代运行测试。
- 临时证据：`%TEMP%/mg17-combat-s/` 的 build/test XML 与日志、`content-conservation.json`；`%TEMP%/mg17-combat-source-audit/` 的源码比较与正规化程序。

中央集成需同步：删除的 10 个自补丁及其旧目标/顺序行；新增绘制能力接口的空类型理由；新增协作者/记录/续接类型元数据；三个 wheel、两个 lance 共享标签及 Mount 状态组的保存所有权；三个结构 step 的 golden；夹具字段路径。既有业务 Harmony 相对顺序、公开 API 151 项、Def 枚举顺序均不因本次调整而豁免。最终整合、实际游戏表现与可选模组联机路径仍由主代理串行验证，本记录不宣称已完成这些实机检查。
