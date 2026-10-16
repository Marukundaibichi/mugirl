# Wave S · Corporate 结构重构与旧档升级

工作开始于 2026-10-15，收尾于 2026-10-16。遵循 [decisions §7](../refactor-1.7/decisions.md#7-owner-补充约束2026-10-15恢复重构后优先于所有既有计划)：玩家文案、随机调用、计时、对象身份和游戏行为保持不变。原 [Corporate 计划](../refactor-1.7/module-plans/corporate-plan.md) 中冲突的修改不再执行。

## 结构结果

`Features/Corporate/Network/CorporateNetwork` 仍是唯一 Vault 所有者和跨账户业务编排入口；`CorporateIntroduction` 仍是 gifts 的所有者。没有增加代理 IThingHolder，没有移动 Pawn/Thing 所有权或改变父链。

| 聚合／服务 | 负责的状态和操作 |
| --- | --- |
| `CorporateHistory` | 保留原 key、detail 和金额，按原顺序追加及限制 200 条记录 |
| `CorporateTradeDesk` | 现货与订单集合；持有货物引用 |
| `CorporateLoanAccount` | 原浮点贷款字段、计息、还款、余额和预计余额计算 |
| `CorporatePeopleDesk` | 报价、转世界 Pawn、待领款；保持原加载清理顺序 |
| `CorporateMissionBoard` | 完整合同集合、独立编号、查询及活动数量 |
| `CorporateClientAccount` | 成交额、周权益和已记账 Pawn；持久列表保留顺序，瞬态 HashSet 加速重复记账判定 |
| `CorporateContactState` | 开户事实、独立标志、引用、到访时刻与已保存的服务暂停时刻 |
| `CorporateProductFactory` | 产品生成、固定规格随机作用域、失败回收；现有 Network 调用入口保留 |

`CorporateStateScribe` 通过 `EnterNode` / `ExitNode` 同步暴露值聚合，保留原组件内部的 ExposeData、交叉引用刷新和 PostLoadInit 顺序。没有把聚合改成单独登记的深层回调对象。资产容器仍以原组件为构造参数进行深保存。

保存的根结构是 `vault`、`nextRefreshTick`、`sequence`、`history`、`trade`、`loan`、`people`、`missionBoard`、`services`；开户状态在 `contact` 内，gifts 留在组件根。交易编号与委托编号继续独立，不合并成单一计数器。

删除四个已无读取方、没有 XML 使用且不属于冻结公开 API 的交易配置字段：`specialOrderPriceFactor`、`specialOrderMinimumUnitPrice`、`rareOrderDefs`、`specialOrderDefs`。未删除任何活 Def、翻译或现存玩家文本。

## 一次性升级

升级实现全部位于 [SaveUpgrade/Modules/Corporate](../../Source/MugirlRace/SaveUpgrade/Modules/Corporate)。生成的 R 改名表未改动，由 partial `RegisterStructure` 接入四个 DOM 步骤：

| 步骤 | 精确含义 |
| --- | --- |
| `Corporate.ReceiptDefaults` | 补齐订单已结算额、人员原收据、分销目标及支援呼叫哨兵；将价格版本转换为持久完成动作 |
| `Corporate.NetworkStructure` | 40 个平面标签无损重挂到其账户；不解析重写浮点、文本、load ID 或列表内容 |
| `Corporate.ContactStructure` | 17 个开户标签进入 contact；保留独立布尔值、未知整数 outcome 和完整冷却 tick |
| `Corporate.SupportGraph` | 以持久 continuation 表达旧支援图完成动作；不重置 Lord、Toil、trigger 索引或计时 |

使用中央 `EditPlan.AppendElement` / `Move` 原语重挂原节点。不是 clone + Remove，因此搬运中的对象不会被标记为已删除 load ID。新旧结构同时存在时中止升级，避免猜测哪个账本为准。

订单结算额使用原 `(int)((long)paid * min(quantity, deliveredQuantity) / quantity)`，保留有符号整数截断和宽中间值。人员已付款收据在重新定价前固定；免费订单的明确 `0` 不被替换。旧分销目标使用原精确份额算法。支援调用次数缺省或负数时，仍按旧 `supportWeek >= 0 ? 1 : 0` 处理，不能一概置零。

人员改衣与重定价仍按每份报价的原循环次序执行，位于 `LoadedGame` 的样品刷新之后。具体旧价格语义在 `CorporateOfferPriceCompletion` 中，领域只暴露一般的持久完成接口。成功后移除完成动作，失败时保留待完成状态。明确空 continuation 也作为已完成的规范形状，使中途失败后重新规划不会把当前基础价再当成旧价格。

## 支援 Lord 的保存前空窗

原 `supportGraphVersion=0` 并不总意味着存在 PanicFlee：引擎还要满足派系、autoFlee、neverFlee、地图可退出等条件。因此不能根据版本或 triggerData 条目数量推断图，也不能把 PanicFlee 索引 2 改成 Hunt 0 后提前盖完成标记。

`ICorporateSupportContinuation` 是领域中性接口；具体 `CorporateSupportGraphCompletion` 在升级层。它保持原 `PostLoadInit` 时的 AddFleeToil 判断，并在原首次 `LordJobTick` 时执行原算法：先清除待完成标记，再按需要 GotoToil、结束相同 Pawn 的工作，最后删除原 PanicFlee 转换及节点。异常时同样不重复执行已开始的动作。

如果加载后尚未 tick 就保存，`Scribe_Deep` 会写入具体 continuation Class。schema 1 再次加载仍可恢复同一旧图、稀疏 trigger key、ticksPassed、ticksInToil、当前 Toil 和 fallback；完成后保存为空 continuation。具体类有 public 无参构造，接口赋值由真实 `ScribeExtractor.SaveableFromNode` 测试覆盖。

原版证据来自 `DevData/Evidence/Resume-PreR-20261015/InFlightAudit/` 的两份 engine JSON 与 audit-findings。本轮复读 4979 的 `Scribe_Deep`、`SaveableFromNode` 与 `ScribeLoader.EnterNode`。离线测试必须将真实 Mugirl DLL 登记到 RunningMods：GenTypes 只搜索游戏及活动 Mod 程序集，直接放在测试进程中不足以解析多态 Class。测试用独立作用域登记并恢复，未替换引擎的类型解析逻辑。

## 被行为冻结约束取消的原计划项

- 不把贷款 float 改成 long 定点。`315.0001f` 的现有余额边界就是反例；每一步运算和取整都保留。
- 不重译历史备注，不删除未知历史 key、savedLabel、品质或耐久，不把完整合同 union 按 kind 裁剪，不删除未知 kind 的数据。
- 不合并交易和任务 ID，不把计时器改成 hash interval，不重置在途 Job/Lord。
- 不提前修复核聚变邀请失败：仍仅在原读档位置重评估，不改变运行中的失败结果。
- 不把每次读档的样品刷新、未付款展示人员整装、Quest root 完整性检查简单改成只执行一次。这些现有调用可能影响对象编号、展示与其他后续行为。
- 中途启用 Mod 的开户附着和派系存在检查保留为当前业务不变量；只改变其状态组织，不删掉合法的新增 Mod 流程。
- 保留 `ResetDebtOverlay` 的原构造时点及已有结构债务例外。4979 `Game::.ctor`（0x06000DCD）直接调用 `FillComponents`（0x06000DD3）；`InitNewGame`（0x06000DD4）发生在 Game 已构造之后。改成 BeforeNewGame 会推迟清空，未证明等价。协调者已确认保留，不为形式上的无副作用扩大引擎补丁。

## 验证与限制

- 修改前工作区状态存于 `%TEMP%/mg17-corporate-preS-status.txt`；根基线归档为 `DevData/Backups/PostR2-1.7-20261015-231127`。本模块独立读取基线也记录了其他模块施工中的失败，没有把历史绿色结果当成本次结果。
- 独立 Release 构建：0 警告、0 错误；产物在 `%TEMP%/mg17-corporate-build`。未部署 DLL，未启动游戏，未修改 1.6。
- Corporate、Corporate 旧档升级和全模块 golden 目标集：**98 项，0 失败、0 跳过**。四组 Corporate 金样覆盖全部声明消费的旧标签，并通过完整生产流水线与二次运行幂等检查。
- 真实 Scribe 往返覆盖：接口 Class continuation、首次 tick 前再次保存、空 continuation、恰好一次和异常调用次序、实际组件账户数据、Vault 所有者、浮点位与人员记账顺序。2,000 组计息/还款差分逐位对照原运算。
- `%TEMP%/mg17-corporate-v2.log`：标准 CLR 与 Waylith CLR 检查结果一致，Corporate 的 `C.save-upgrade` 无失败。采样时全套 680 项无失败。Milk 对应 owner 补齐真实类型解析环境后，最后独立重新构建并运行全套 **691 项、0 失败、1 项明确不运行**（`mg17-corporate-all-tests.log`）；这仍不是整个 Wave S 的最终验收。
- 反向源码审查：显式还原字段访问路径后，444 个原方法 token 序列相同；五个迁出的产品工厂方法 body token 完全相同。其余 27 个方法分别是序列化、已测试的账户委托、收据升级或 Lord continuation，已逐项复核。全部 UI 源文件未改，中文文本和 Mugirl 玩家键字面量多重集无增删。
- 反向审查与构造时点证据在 `%TEMP%/mg17-corporate-reverse-audit.json`、`mg17-corporate-file-manifest.json`、`mg17-corporate-constructor-timing-evidence.json`；目标日志为 `mg17-corporate-target.log`。
- 实机总验收由集成 owner 串行执行；本模块离线结果不能替代包含 Pawn job、Lord 自动 flee 条件、暂停后立即保存与重载的游戏内验证。

中央集成需刷新 ownership，并删除 `scribe-defaults.allow.txt` 的四个已失效条目：CorporateNetwork 的 supportCallsUsed、CorporateOrder 的 creditedTurnover、CorporatePersonOffer 的 priceVersion、LordJob_CorporateSupport 的 supportGraphVersion。保留构造清空投影的既有例外。旧 CorporateValidation 两类型由 Core 处置，本模块没有重复登记。

## 缺组读档边界复核（2026-10-16）

Mount 的缺组负对照暴露了同步 Scribe helper 的共同漏洞：LoadingVars 未进入的组，不能在 CrossRefs／PostLoadInit 再次执行其引用读取。CorporateStateScribe 及七处调用点现按各组的本次实际读取状态控制后续阶段；保存仍全部写出，缺失组保持原新实例默认值，执行顺序不变。

原组件测试只执行了 LoadingVars，不能据其通过声称已验证所有引用阶段。本轮补齐真实 CrossRefHandler 与 PostLoadIniter，并用 ScribeErrorCapture 断言零原版 Log.Error；覆盖缺组、部分组、contact 默认状态及已有完整往返。中央补齐测试进程真实 HAR 依赖后，Corporate／Incidents／Mount 联合 **112/112** 通过，独立 Release 0 警告、0 错误，证据 `%TEMP%/mg17-group-fix-target.log`。没有放宽保存值、浮点位或 holder 身份断言。
