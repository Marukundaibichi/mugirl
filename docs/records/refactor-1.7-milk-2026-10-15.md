# Wave S Milk：状态保全与结构重整

本轮以 `DevData/Backups/PostR2-1.7-20261015-231127` 为基线，按 `decisions.md` §7 保持玩家文案和游戏行为。实施开始于 2026-10-15，验证延续到 10-16。修改范围由 `ownership-postR2.json` 的 milk 归属限定；没有修改 1.6、玩家存档、配置、已部署 DLL 或共享内容文件，也没有启动游戏。

## 已实施的结构

- `CompMilkProductionBase` 合并到 `sealed partial CompMilkProduction`；产量状态与结算放在 `.Resource.cs`，条件、设备与 Gizmo 仍由主分部负责。删除无 XML 使用且不在冻结 API 清单内的 `saveKey`。实际 `delta`、未结算余量、满值通知窗口和逐步浮点表达式均保留。`MilkThreshold` 改为同名属性，直接读写原值，不增加 clamp。
- 采集 JobDriver 和 WorkGiver 的单一用途基类合并。Job 仍是 0 接近、1 采集，保留原来的失败条件、预约、开始/每 tick/结束回调及两处幂等清理。工作扫描器仍先执行原子类的快速拒绝，再执行原基类检查，未顺便调整短路顺序。
- 灌篮的 12 个运动字段归入 `DunkMotionState`，状态名 `Bounce` 转为 `DunkPhase.Shattering`，仍只完成碎裂且不再爆炸。保留独立飞行、跟随飞行、直接撞击、动画下扣、非头部载荷等现有语义；不把三个独立布尔状态从阶段枚举推算。容器、Pawn 和 Ability 引用保留原实例。
- 延迟异种初始化由 `SaveUpgrade.GameComponent_MilkUpgrade` 调度，当前基因业务规则保留在 `XenotypeUtility`。四个旧完成标志只用于计算 `pending = !allFixed`；旧计时原值保留。Biotech 关闭不推进计时，300 次有效 tick 递减后在第 301 次执行，缺少目标 Def 时完成标志的变化也与原代码相同。
- Appearance 通过 `AppearanceSettingsBinding` 订阅设置变更，描边常量读取 Core 的 1/.25/3；挤奶动画清理注册原时点的 `TransientStateReset`，顺序 400。删除 Food/Nurture 两个空重置入口。
- 共享组件字段改为独有标签；释放特效的剩余时间、年龄和三组下一次播放 tick、哺乳期 float 出生时刻、思想检查计数器、首次治疗状态、养育完成状态和临时外观状态全部继续保存。

## 升级步骤与边界

| 步骤 | 输入与结果 | 保全边界 |
| --- | --- | --- |
| `Milk.ResourceAccrual` | 旧绝对结算时刻移入可持久的 `LegacyMilkAccrualContinuation` | 不按加载时刻提前换算；在原来首次 Active、delta > 0 且游戏时钟可读的位置执行，再清空。暂停后立即再次保存也不丢时刻。`-99999` 保留现有 pending，其他负值按原规则得到 0。 |
| `Milk.XenotypeRepair` | 四个完成位和 `checkTimer` 转为 pending/remaining | 不提前授基因、不丢 DLC 关闭时的待办、不 clamp 负计时。新旧字段混杂时失败而不猜测。 |
| `Milk.RelinkGatherContinuation` | 保留旧采集当前 Toil 与进度；补充可证明的等待归属 | 专用等待 Def、反向 targetA 和有效整数 Job.loadID 三者同时成立才认领；早期原版 ForceWait 不猜归属。自采集不创建等待归属；任何新字段已存在时不覆盖。重复相关 Pawn ID 失败。 |
| `Milk.DunkMotion` | 旧平面运动字段无损重挂到 `motion` | 不取整、不重算坐标、不重置计时、不丢布尔状态；不 clone/remove 持有对象。 |

旧 comp 归属使用 Core 集中生成的完整历史关系表，没有在 Milk 登记会遮断其他模块的子集。普通改名使用按组件/类型限定的 LR 行。ChildDrink、Drink、Feed、Charge、Dunk 的已发布 Toil 布局由 `DevData/Evidence/Resume-PreR-20261015/InFlightAudit` 支持 Preserve；Gather 使用独立 Claim 步骤。

## 对旧计划的约束性修订

以下原计划目标不再实施：hash 定额 60 tick 产奶、丢弃 pending、满值通知无条件重试、阈值新增 clamp、float 出生时刻转 int、思想计数改 hash、释放效果不存档、加载时立即修复异种、禁用 DLC 时永久放弃修复、Bounce 直接转 Finished、派生/删除运动布尔位、统一为只装头部的容器，以及任何文案修正或活内容删除。

头发从两个节点合成一个节点、字符串 Def 改交叉引用、未知 shader 改迟绑定等方案会改变可扩展输入面或需要渲染等价证据，本轮未据推测实施。现有缓存重置、空列表修复和缺失 ShaderDef 时的 Cutout 退化属于当前读取/渲染不变量，继续保留。不能从“Dunk Job 首次在 0924 发布”推出控制器的其他保存状态从未存在。

## 验证与审查

- 独立 `%TEMP%/mg17-milk-s/Source` 构建，dotnet 从该 Source 目录启动；生产项目与测试项目均 0 警告、0 错误。
- 最后稳定共享源码快照加最新 Milk 文件：完整离线测试报告 **Total 700、Failed 0、Errors 0、Skipped 0、Not Run 1**。包括四个生产步骤的黄金夹具及重复升级检查；Not Run 为原设计的显式测试。
- 新增实际 Scribe 保存→读取→再次保存测试 8 项通过：接口型产奶续接、四种灌篮阶段/独立标志组合、三种异种待办/计时状态。保留 `315.0001`、小数坐标、大坐标的 float 位模式。
- 采集负对照涵盖错采集者、原版等待、接近阶段、自采集、已存在的新字段、嵌套持有的 Pawn、重复 ID；出生 tick、检查余量与释放排程有精确原值断言。
- 用实际基线 DLL（MVID `c217671b7e30471a96a8e60b47d39024`）和本轮 DLL（审查快照 MVID `ee05936fd2dd44ed8422bfea0c70730b`）进行反编译对照。Dunk 的 16 个关键方法只做明确的字段路径和枚举名映射后逐字一致；没有归一化数值、条件、调用或文本。Gather 的 `ExposeData` 完全一致；`MakeNewToils`、`MakeGatherToil`、`CleanupGatherEffects` 仅类型归属、生成器编号和已证明返回 1f 的默认虚调用内联有差异。反射查得旧 Gather 基类仅一个实际子类。
- `engine_audit` 独立只读复核了 Gather 的双向关联条件、未覆盖新字段和保留未知等待的策略，未发现新的复位或阶段丢失；本报告不把静态审查当作实机重载覆盖。
- 仓库全量 `Validate.ps1 -WorkRoot` 尝试在生产构建阶段遇到 Core 同期拆分中六个 `RegisterHooks` 尚未就绪的错误。没有修改他人文件绕过；最终全树验证由集成负责人完成。

临时证据位于 `%TEMP%/mg17-milk-s/`：`build-final.log`、`tests-all-final.log`、`tests-scribe.log`、`validation-final.log`、`gather-compiled-audit.json`、`dunk-compiled-audit.json`、`compiled-equivalence-report.txt`、`owned-changes.txt`。发布前应由集成负责人归档所需证据。

## 集成与实机待验

中央编译/反射顺序清单需维持原 Xenotype GameComponent 槽的位置；Root 已接收确切替换类型和路径。A5 的反射播种读取的是 C# 字段而非 Scribe 标签，Milk 新字段与 `motion.*` 路径清单已交 Root；当前层面不能仅凭 suite 编译成功声称深度播种覆盖仍然存在。

后续由唯一游戏执行者运行隐藏、静音的 default/control 与历史 P 夹具，重点核对首次活跃产奶、当前采集等待、灌篮中途保存、释放排程及 DLC 关闭后的异种待办。此次模块离线交付没有代替这些实机验收，也没有部署中间 DLL。

## 有价值的迁移发现

1. 绝对旧时刻的转换不是纯 DOM 加载时运算。若原实现只在首次活跃 tick 消费，升级成可持久的中性续接对象才能同时保留暂停、停用及立即再保存的语义。
2. Scribe 的多态类型查找只遍历当前 Mod 程序集；离线测试仅加载 AppDomain 程序集不够。测试应登记实际 Mod DLL 并在结束时恢复，而不能 mock 类型解析使失败变绿。
3. XML 空文本节点即使内容长度为 0，与子元素并列仍是 mixed content。用 `AppendElement` 加 `Move` 形成容器，避免先 `SetText("")` 再追加子元素；同时保持对象身份和加载 ID。
4. 采集目标等待归属不能从等待工作名称单独推断，尤其原版 `ForceWait` 会根据床和姿态创建不同工作。专用 Def 的双向目标引用才构成可验证的来源关系。
