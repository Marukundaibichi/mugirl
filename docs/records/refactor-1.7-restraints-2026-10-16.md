# Wave S Restraints：规范状态、精确旧档升级与在途工作

本轮按 `decisions.md` §7 实施，保留当前 1.7 文案、数值、触发时点、RNG 顺序与活内容。接手基线记录在 `%TEMP%/mg17-restraints-s/`，承接 Root 已完成的 `RestraintExtension`、冻结 XML 别名桥接、私有锁状态与只读查询属性。仅修改 restraints 归属代码、模块升级表/测试和本文；没有部署、启动游戏或修改 1.6、玩家配置与存档。所属 `1.7/` 内容文件的基线散列全部保持。

## 结构与当前行为

- 领域通过 `RestraintExtension` 读取配置；`PublicApi/SlaveApparelDef` 保留全部冻结 XML 字段、默认值和 XML 根。自身生成的 extension 在重复 `ResolveReferences` 时同步原字段，保留列表实例；显式提供的 extension 仍优先。修复桥接后的空 Def 查询边界，使之与原先 `def as SlaveApparelDef` 一样返回 null。
- 锁数、锁定位和破解位独立保存，不折叠为单一状态。保留每次 PostLoadInit 的锁数恢复和已破解状态规范化，也保留原 `Pawn_ApparelTracker.ExposeData` 时点的穿戴锁同步。钥匙消耗、脱下失败回滚和消息原文均不变。
- 所有现存头盔、镣铐、项圈和电击 Hediff 计时原值继续保存，只加组件独有前缀。磁力镣铐下一次切换时刻改为显式保存，旧缺省 `-1` 在有旧类/旧字段证据时物化；取值和时序不变。手动电击默认值仍为 `-99999`。
- 删除 `HediffCompScribe` 的旧标签读取；具体 HediffComp 仅访问自己的规范标签。共享旧 `age/triggered` 的处理集中在 SaveUpgrade。
- `MapRopingIndex` 没有持久字段，移除该空 MapComponent。`PendingHitchRegistry` 使用 `ConditionalWeakTable<Map, HashSet<Pawn>>`，保留同一 Pawn 在不同 Map 的独立登记/清理语义；当前地图与登记地图双清理逻辑不变。地图弱引用避免删除地图后被索引持有；快速计数仅是查询提示，偏大不会影响结果。新局/读档前清理。
- 抽象化不直接使用的 `CompAdvancedRestraint`，统一束具查询方法命名；唯一显示名辅助函数移入钥匙组件私有实现，原短名、标签及 `null/noname` 退化文字均保留。跨模块 Courier 调用由 Root 更新。
- `ThinkNode_ConditionalRoped`、`JobGiver_WanderRoped` 的短名和逻辑保持。原版节点的 null 边界并不相同，而且 ThinkTree 保存键受短类型名影响，不实施旧计划中的替换。

## 四个结构升级步骤

| 步骤 | 保全规则 |
| --- | --- |
| `Restraints.LockState` | 独立转移 lockCount/isLocked/isCracked；缺失锁数从 `IDefCatalog.IntegerField(..., needkeynumber)` 读取包含继承默认的 Def 值，原 Def 不提供该字段时保持原转型失败默认 0。已有规范字段优先。 |
| `Restraints.WornLockList` | 只补实际穿戴、有效锁定的束具引用。现有列表顺序、额外条目和已破解条目不擅自删除；支持缺失/空/IsNull 列表与新旧 Thing ID，第二次 DOM 不重复追加。 |
| `Restraints.BrainwashTriggerLabels` | 对真实 comp 复制第一个直接旧 age/triggered 节点，复现 Scribe 的同名读取顺序。已有 mugirl 独有标签不覆盖；兄弟 comp 均读取同一首值，嵌套 performance 的 age 和排程完整保留。 |
| `Restraints.ItemJobContinuations` | 同时考虑已发布库存/地面布局与重构前 1.7 固定布局，只提交语义证据唯一的 cur/next 索引映射。时长、wantBeginNextToil、目标、工作 ID、持有物和数量不改变。 |

旧组件归属使用 Core 集中生成的完整历史表，没有重登模块子集。未再读取且原本无效果的 `tickCounter/currentTickCount/remainingCycles` 仅由升级层处置。UseItemOn、GiveSlaveApparelGear 和所有其他活 Def 均保留。ForceUnlock 和四个 Roping 驱动按逐发布 Toil 审计登记 Preserve。

## 在途工作的支持范围与真实歧义

依据 `DevData/Evidence/Resume-PreR-20261015/InFlightAudit/`。1.6.9676 与 1.7.4979 的 `TryActuallyStartNextToil` 都在每次开始 Toil 时把 `ticksLeftThisToil` 重置为其 `defaultDuration`，再执行初始化；当前 Toil 的 initAction 不会在 PostLoadInit 重放。`JobDriver` 没有保存 completeMode，不能把它当作输入证据。

- 正剩余 tick 只匹配这些布局中有时长的 Wait 阶段；保留原剩余时长。
- 移动阶段要求非正 tick、没有待切换标记、`pather.moving` 和精确的 Thing 目的地。原版 moving 缺省为 true，已成对复读。物品目的地和目标目的地区分旧索引相同的两种 Goto。
- 同时转换显式 next；`-1` 保留。旧末尾 no-op 若是待进入 next，映射到结束位置，不能再次执行效果。尚未进行的库存转手不能冒充当前“跳过取物”。
- 三个当前 Job 显式保存 `itemFlowVersion=1`，仅表示规范布局，不在领域提供旧格式分支。成功 DOM 写入同一标记，使二次升级不把新索引重新猜成旧布局。无 Thing targetB 的 UseItemOn（包括 Cell targetB）保留已核实相同的原版分支。

**并非所有正常旧存档边界都可以唯一恢复。** 忙碌姿态可能让一个已完成的 Instant 或已到期 Wait 停在相同索引，且 ticks≤0、wantBeginNextToil=true；原始库存/地面分支从未保存。某些显式跳转仍需未完成的库存转手，或者两个目标身份相同，亦会留下互不等价的候选。缺少有效寻路目的地的非等待阶段、未知布局版本和非法待进入索引也不猜测。升级会失败并保留原文件及旧版本戳，不结束工作、不重放效果、不盖新版本戳。

遇到上述真实歧义时，可在该存档原本能够运行的构建中让对应工作完成或正常取消，再另存后升级；必须保留原档备份。另一条开发恢复路径是取得足以唯一辨识该保存状态的新证据后补充步骤和黄金测试。本轮不声称已恢复没有写入存档的来源分支或局部计数器。

## 验证与独立审查

- 独立 `%TEMP%/mg17-restraints-s/Source` 构建，dotnet 从该 Source 启动。最终生产与 Tests 构建均 0 警告、0 错误。
- 初始稳定共享副本测试 Total 696；加入本模块后最终 **Total 763、Failed 0、Errors 0、Skipped 0、Not Run 1**。新增 67 项测试覆盖动态默认、重复旧标签、独有标签优先、嵌套年龄、弱地图索引、全部布局的长阶段、显式 next、不确定忙碌边界、非法版本与实际 Scribe 往返。
- 实际 Scribe 保存→读取/解析引用/PostLoadInit→再次保存共 7 项，通过锁数/锁位/破解位组合、设备所有计时、trigger 状态和嵌套演出进度；配置桥接另有 3 项，含重复解析与显式 extension 优先。
- 四个步骤均有包含全部所声明输入类型/字段的黄金夹具，并验证第二次 DOM 幂等。
- 编译后反编译对照覆盖 35 个关键方法：头盔、镣铐、项圈、电击效果的触发和 RNG 调用，延迟/压制计时，表演时间轴，以及三个 MakeNewToils。仅映射明确的字段/辅助方法/配置成员改名和编译器 iterator 编号，全部方法体一致；没有归一化数值、条件、调用顺序或玩家文字。基线 MVID 为 `c217671b7e30471a96a8e60b47d39024`，审查快照为 `902bb83815b343b59b50fd79c651f70c`。
- `engine_audit` 独立只读复核 ItemFlow/ItemJobStep，未发现已审发布布局中的不安全放行。按建议补了已删除身份过滤、-1 起始状态与非法 next、未知版本、Cell targetB 测试。审查不外推到任意第三方重新安排 Toil 的补丁。

离线验证器在 `--assume-active all` 下：151 项冻结公开 API、Def/内容契约、语言和资源、JIT、Harmony 应用及日志检查通过。全树仍为 **9/45 检查失败**，没有重写中央基线使其变绿。其中本模块关联的剩余项为：两条旧 Scribe allow-list 条目已失效；消费验证器把 `mugirlBrainwashPerformance/age` 的合法嵌套值误当成五个 HediffComp 的直接 age 残留。另有共享动态反射允许项、补丁基线及其他模块未完成的结构输入。具体条目和修正请求已交 Root。两个仅 info 层的抽象 DelayedTrigger 泛型记录仍显示 undisposed，真实具体 comp 输入已消费，不能用虚假抽象 owner 登记掩盖。

临时证据：`baseline-status.txt`、`owned-baseline.sha256`、`build-final.log`、`tests-all-final.log`、`tests-scribe.log`、`validator-final-report.json`、`validator-final.log`、`compiled-body-audit.json`、`compiled-body-results.json`、`owned-changes.txt`，均在 `%TEMP%/mg17-restraints-s/`。首次手动 validator 未传 assume-active，FA 可选目录未激活造成的 78 条公开名失败不作为真实 API 回归；最终 all 配置已通过该项。

## 集成要求与发现

Root 已接收新字段/反射播种路径和删除文件清单。A5 应读取规范字段，不能继续向旧 C# 私有字段写值；GameComponent 的 C# `activePerformances` 字段未改名，仅 Scribe 标签统一。中央需要清理 MapRopingIndex、HediffCompScribe、PawnSlaveStatusUtility 的编译顺序旧项及 MapComponent 类型槽，并按完整结构验收更新基线。

本轮没有实机。隐藏、静音的最终 control/P 夹具验证仍由统一执行者负责，尤其应实际覆盖等待阶段的钥匙/解码器数量、锁次数与效果不重放。

迁移中确认的框架问题是：同一旧物理字段可由多个 comp 消费，但只有它们明确指向同一个结构步骤时才允许重叠。Root 已实现同一步允许、不同步骤或混合 Rename/Drop 拒绝的规则。验证器和 A5 还必须按 owner 与直接标签判断消费，不能通过删除嵌套同名字段或把整个新容器排除在比较范围外来获得通过。
