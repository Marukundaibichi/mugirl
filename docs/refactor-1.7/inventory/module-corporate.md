# 巨企模块审计：`Source/MugirlRace/Features/Corporate/**`

范围：81 个 .cs 文件，13,852 行（56 个文件在根命名空间 `Mugirl`，25 个在 `Mugirl.Features.Corporate`）。
冻结对照：`1.6/Source/Features/Corporate/**`（工作树，即最后一个 1.6 构建）与 git 中 0.6.0–0.6.7 的各发布提交。
除特别标注 `1.6/…` 外，所有 `文件:行` 均相对 `Source/MugirlRace/Features/Corporate/`。原版行为均在 DecompilerServer 的 `rw17-game`（`C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Assembly-CSharp.dll`）上核对。

---

## 0. 摘要

1. **1.7 树在本模块没有改动任何存档名。** 8 个以 `Class="…"` 写入存档的类型、5 个按成员名保存的枚举，以及 126 处 Scribe 调用的标签和默认值，都与 1.6 冻结源码一致。我逐条比对了两边全部 `Scribe_*.Look(ref x, "label", …)`：唯一差异是 `CorporateRecord` 的局部变量从 `key` 改为 `savedKey`，标签不变。1.6/Defs 与 1.7/Defs 中所有巨企 defName 也完全相同（集合比对）。
2. 巨企系统从 0.6.0（e93677d，2026-09-16）开始出现。之后的存档形状变化都集中在 0.6.0–0.6.7 与 50f7979（2026-10-03）之间；最后一个 1.6 构建（未提交工作树）又新增了 `independentContact`。见 §0.2 指纹表。
3. 遗留代码共 23 处（§A），按性质分为：
   - 5 处读档时每次都执行、**不受版本门控**：A3、A6、A11、A15、A17。其余各项都按字段的哨兵值或缺失与否门控（A16 由 `initialized` 门控）。
   - 2 处只为**从未发布的开发存档**服务（A13、A14）。
   - 1 处只为**未发布的中间构建**服务（A10）。
4. 统一升级器在本模块需要的工作：
   - 8 条类型映射，在类型改名或改命名空间时需要。
   - 40 个 `CorporateNetwork` 顶层扁平标签迁入子节点的结构变换（可选，但推荐，见 §C4.1）。
   - 2 个纯 XML 回填：A1、A7。
   - 1 个地图 Lord 图的结构变换：A12。
   - 5 个需要活对象的读档后步骤：A5、A8、A15、A16、A17，外加可选的 A3、A6。
5. 改名风险因类型种类而异（已在 1.7 原版代码上核实）：
   - `LordJob_CorporateSupport` 改名但不做映射：`Lord.ExposeData_StateGraph` 在 PostLoadInit 解引用 `curJob.LostImportantReferenceDuringLoading`，`curJob` 为 null，触发 **NRE**。
   - `QuestPart_*` 不做映射：`Quest.ExposeData` 删除 null part，并记录 “Some quest parts were null”。
   - `GameComponent` 不做映射：被 `Game.FillComponents` 用空实例替换，巨企全部状态和 Vault 中托管的所有 Thing/Pawn 都会丢失。
   - `JobDriver` 不做映射：原版只发出 “Cleaning up invalid job state” 警告并结束该 Job，后果最轻。

### 0.1 版本时间线（只列巨企存档相关的改动）

| 发布 | 提交 | 日期 | 巨企存档变化 |
|---|---|---|---|
| ≤0.5.x | — | — | 没有巨企组件，但已有运货员任务 `Mugirl_CourierRaid`（自 1113cd8 起）。A16 从这里恢复历史 |
| 0.6.0 | e93677d | 09-16 | 首版：`CorporateNetwork`、`CorporateIntroduction`、两个 QuestPart、两个 JobDriver，以及订单、现货、贷款、人员、委托的全部基础标签 |
| 0.6.1 | 0dbb4e2 | 09-16 | `IsOrderableDef` 排除不可销毁的 Def；`DiscardUnspawnedProduct` 增加“受保护旧样品”分支（A3b） |
| 0.6.1.1 | 8498e42 | 09-17 | 新增 Services 的 6 个标签；新增 `LordJob_CorporateSupport`（无 AddFleeToil 覆盖，会被原版追加逃跑分支）；新增 `CorporateOrder.creditedTurnover` 与 `CorporatePersonOffer.paidAmount` |
| 0.6.2 | bf577ca | 09-18 | 新增 `LordToil_CorporateSupportHunt` 与 JobGiver，不影响存档索引 |
| 0.6.4 | 37545af | 09-20 | 新增 `RepairFusionSiteGenerationLockout`（A15） |
| 0.6.5 | de6753d | 09-21 | 新增 `supportGraphVersion`、`JobDriver_CorporateSupportTend`（`patientWaitJobId`），以及聚变谢礼 `fusionProtectionRewards` |
| 0.6.7 | 131ee89 | 09-24 | 新增 `priceVersion`、`buyAsColonist`、`transferApplied`、`corporateCreditedPersonIds`；人员改穿主题套装 |
| — | 50f7979 | 10-03 | 新增 `CorporateOrder.speed`/`expediteFee`；独特武器样品改用规格种子 |
| 最后 1.6 构建 | 工作树 | — | 新增 `CorporateIntroduction.independentContact`（`1.6/…/CorporateIntroduction.cs:510`） |

五个存档枚举的成员在 10 个发布提交中完全一致（`git grep "enum Corporate"`）。17 个历史记录键（§A20）在所有发布中也完全一致。

### 0.2 版本指纹（存档里没有模组版本号时，升级器据此识别旧形状）

`Scribe_Values.Look` 遇到值等于默认值时不写节点（已核实 1.7 `Verse.Scribe_Values.Look<T>`）。因此只有满足“新版本总写出、旧版本没有”的节点，才能作为指纹：

| 指纹 | 含义 | 可靠性 |
|---|---|---|
| `game/components` 中没有 `li Class="Mugirl.CorporateIntroduction"`（或没有 `<corporateIntroductionInitialized>True`） | 0.6.0 之前的 Mugirl 存档，或从未装过本模组的存档 | 可靠 |
| `corporateOrders/li` 缺少 `<creditedTurnover>` | 0.6.0 时写入的订单（之后的版本值 ≥0，而 Scribe 默认值是 −1，所以总会写出） | 可靠 |
| `corporatePeopleOffers/li` 缺少 `<priceVersion>` | ≤0.6.6（之后的版本总写 1，Scribe 默认值是 0） | 可靠 |
| `paid=True` 但缺少 `<paidAmount>` | 0.6.0 时付款的报价 | 可靠 |
| `lordJob Class="Mugirl.LordJob_CorporateSupport"` 缺少 `<supportGraphVersion>` | 0.6.1.1–0.6.4 | 可靠 |
| 缺少 `<corporateSupportCallsUsed>` | 0.6.0（此时也一定没有 `corporateSupportWeek`） | 可靠 |
| 缺少 `buyAsColonist`、`transferApplied`、`speed`、`independentContact` | 无法判断 | **不可靠**：这些字段的默认值本来就不写出 |

---

## A. 遗留/向后兼容代码

“升级器所需”一列沿用任务定义的代号：(a) 类型改名映射；(b) defName 改名映射；(c) 有类型的所有者内的 Scribe 标签改名；(d) 枚举值改名；(e) 存档 XML 的结构变换；(f) 需要活对象的读档后语义步骤；(g) 不需要，可以直接删除。

| ID | 位置 | 处理的旧形状（引入版本） | 新规范形状 | 升级器所需 | 说明 |
|---|---|---|---|---|---|
| A1 | `Trade/CorporateOrder.cs:31`（Scribe 默认值 −1）、`:46-50`（PostLoadInit 回填）；1.6 对应 `1.6/…/CorporateNetwork.Trade.cs:49,59-62` | 0.6.0 的订单没有 `creditedTurnover`，部分交付的金额早于成交额账本（字段在 0.6.1.1 才加入） | `creditedTurnover` 恒为有效值 ≥0，Scribe 默认值改为 0 | **(e)** 纯 XML：对缺少节点的 `li` 写入 `quantity<=0 ? 0 : floor(paid*min(quantity,deliveredQuantity)/quantity)` | 同一 `li` 内的字段即可算出，不需要活对象。之后删除 `:46-50` |
| A2 | `Trade/CorporateOrder.cs:35-36`（注释“旧合同默认普通备货”）、`:37` | 50f7979 之前的订单没有 `speed`/`expediteFee` | 不变 | **(g)** | `default(CorporateOrderSpeed)=Standard`，`expediteFee` 默认 0，与缺省语义一致。只需删除注释 |
| A3 | `Trade/CorporateNetwork.Trade.cs:37-67` `MigrateLegacyStockSamples`，由 `Network/CorporateNetwork.cs:117-118`（`LoadedGame`）调用；1.6 中位于 `1.6/…/CorporateNetwork.Trade.cs:92-101`（PrepareOrderCatalog） | 50f7979 之前，独特武器（`CompUniqueWeapon`）现货样品的词条是随机的 | 样品由 `MakeOrderProduct` 用规格种子生成（`Pricing/CorporateNetwork.Pricing.cs:208-224`，配合 `Harmony_CorporateWeaponSpecification` 改写种子） | 推荐 **(g)**；也可 (f) 执行一次 | **没有门控**：每次读档都会为每个独特武器样品新造一件 Thing。删除是安全的：成交时货物一律由 `TryMakeGoods→MakeOrderProduct` 重新生成（`Trade.cs:268,214-238`），样品只影响预览；每周刷新（`Trade.cs:497-535`）会整体替换现货，偏差最多持续 7 天 |
| A3b | `Trade/CorporateNetwork.Trade.cs:182-203`，即 `DiscardUnspawnedProduct` 的 `!def.destroyable` 分支（注释“受保护的旧样品”） | 0.6.0 的现货样品或订单货物可能是不可销毁的 Def；0.6.1 起 `IsOrderableDef` 已排除这类 Def（`Trade.cs:75`） | 托管物一律可销毁 | (g)，或 (f)：升级时丢弃不再可订购的样品并立即补一次周刷新 | 可以保留为防御分支，但应删除“旧样品”措辞 |
| A4 | `Trade/CorporateTradeSettings.cs:13-15`（`specialOrderPriceFactor`、`specialOrderMinimumUnitPrice`）、`:26-28`（`rareOrderDefs`、`specialOrderDefs`） | 旧 XML 补丁（含第三方）可能设置这些字段 | 删除 | **(g)** | 全仓库没有读取方（grep），`1.7/Defs/Misc/CorporateTradeSettings.xml` 也不再设置；不是存档可见字段 |
| A5 | `People/CorporatePersonOffer.cs:15-16,28`（`priceVersion`，类内默认 1、Scribe 默认 0）；`People/CorporateNetwork.People.cs:67-77` | ≤0.6.6 的 `price` 存的是 ×2 后的买价 | `price` = 基础价，删除 `priceVersion` | **(f)** `price = PeopleBasePrice(pawn)`（`People.cs:203-207`，需要 `Pawn.MarketValue`）；在此之前先执行 A7 | 指纹为缺少 `<priceVersion>` |
| A6 | `People/CorporateNetwork.People.cs:59-65`；`People/CorporateShowcaseApparel.cs:31-39` `TryEnsureOutfit`（注释 `:5`） | 0.6.7 之前，未付款展示人员穿的是随机混搭衣物 | 由 `TryDress` 生成整套服装（`People.cs:140`） | 推荐 **(g)**；也可 (f) 执行一次 | **没有门控**：每次读档都会检查所有未付款报价，衣装不完整就重新穿衣并重新定价。删除是安全的：未付款报价会在下次周刷新时被丢弃（`People.cs:105-120`）。删除后 `TryEnsureOutfit` 成为死代码 |
| A7 | `People/CorporatePersonOffer.cs:12,25`（sentinel −1）；`People.cs:38`、`People.cs:343`、`Services/CorporateNetwork.Services.cs:113-116` 中的 `paidAmount < 0 ? price : paidAmount` | 0.6.0 时付款的报价没有 `paidAmount`（字段在 0.6.1.1 加入） | 已付款时 `paidAmount` 恒为实付额 | **(e)** 纯 XML：`paid=True` 且缺少 `paidAmount` 时，令 `paidAmount = price`，并且必须在 A5 改写 price **之前**执行 | 必须在 `People.cs:30-43` 的 PostLoadInit 退款逻辑之前完成，所以只能放在 XML 阶段。完成后删除三处回退写法 |
| A8 | `People/CorporateNetwork.People.cs:324-335`（注释 `:326`“旧档可能在付款后已完成 PreTraded”） | ≤0.6.6 付款后空投失败的报价，此时 Pawn 可能已执行过 PreTraded。0.6.6 的购买一律按奴隶处理（`git show 8cd70f9:…People.cs` 约第 190 行），所以缺省 `buyAsColonist=false` 是正确的 | `transferApplied` 准确反映转移状态 | **(f)**：对 `paid && !delivered` 且属于 ≤0.6.6 的报价，令 `transferApplied = pawn.IsSlaveOfColony` | 之后 `:327-328` 的派系二次判断可以简化，或改成断言 |
| A9 | `People/CorporateNetwork.People.cs:30-43`（失效人员的已付款转为待领白银） | 不属于版本兼容：任何存档里 Pawn 引用都可能失效（卸载模组、被销毁） | 保留在领域代码中 | — | 只需去掉其中 A7 的回退写法 |
| A10 | `Services/CorporateNetwork.Services.cs:262`（Scribe 默认 −1）、`:270-274` | 注释称“旧版只记录单次成功呼叫所在的周”。实际上 `corporateSupportWeek` 与 `corporateSupportCallsUsed` **同时**在 0.6.1.1 加入（`git log -S`），0.6.0 两者都没有，结果恒为 0 | Scribe 默认值改为 0 | **(g)** | 任何已发布存档都不会走到 `supportWeek>=0 && callsUsed<0`；这个分支只服务于未发布的中间构建 |
| A11 | `Services/CorporateNetwork.Services.cs:268-269`（`Max(0)`、`Where(id>0).Distinct()`）；`Network/CorporateNetwork.cs:144`（`records.RemoveAll(null)`） | 防御性规范化；没有任何发布能产生负值或重复（`AddTradeTurnover` 只累加正数 `:66-81`；`CreditPersonTurnover` 先判断 `Contains` `:85`） | `creditedPersonIds` 改为 `HashSet<int>` | **(g)** | 每次读档都会分配新列表 |
| A12 | `Services/LordJob_CorporateSupport.cs:10,21-23`（`AddFleeToil` 只在 PostLoadInit 期间对版本 0 返回 true）、`:40-65`（首个 tick 删除逃跑分支）、`:71-72` | 0.6.1.1–0.6.4 创建的支援 Lord 被原版 `Lord.SetJob` 追加了 `LordToil_PanicFlee`。由于 `AddTransition(highPriority:true)` 插入列表头部，旧图结构为：toils `[0 索敌, 1 撤离, 2 PanicFlee]`；transitions `[逃跑(自 1), 逃跑(自 0), leave]`；trigger 0、1 是 `Trigger_FractionPawnsLost`（无 data），trigger 2 是 `Trigger_TicksPassed`（`TriggerData_TicksPassed`） | 新图：toils `[0,1]`，trigger 0 = TicksPassed；删除 `supportGraphVersion` | **(e)+(f)**。(e) 对缺少 `supportGraphVersion` 的 Lord：`triggerData` 键 2→0，删除键 0、1；`lordToilData` 删除键 2；`curLordToilIdx` 为 2 时改为 0 并做标记。(f) 对已标记的 Lord 执行 `GotoToil(toils[0])` 以重新分配 duty；可选结束 `TendPatient` Job | 只做 (g) 的后果：原版 `ExposeData_StateGraph` 报下标越界，离场计时器重置；如果存档时正在逃跑，`curLordToil` 会一直为 null。0.6.1.1 的图使用原版 `LordToil_HuntEnemies`，与 `LordToil_CorporateSupportHunt` 下标兼容，`LordToilData_HuntEnemies` 也相同，因此不需要额外变换。另外，原版只在 `faction.def.autoFlee` 时追加逃跑分支，巨企 FactionDef 未覆盖该值，使用原版默认 |
| A13 | `Missions/CorporateMission.cs:62-63`（`SalesTarget→LegacySalesTarget`）、`:172-175`（PostLoadInit 回填）；`Missions/CorporateNetwork.Quests.cs:493-496`（接受时回填）；`Pricing/CorporateMoney.cs:71-72`；测试 `Source/Tests/MugirlRace.Tests/Corporate/CorporateMoneyTests.cs:82-97` | 没有 `salesGoal` 的分销合同。但 0.6.0 的 `BuildDistributionOffer` 就已设置该值（`git show e93677d:…Quests.cs:152`），已发布存档中**不存在**这种合同 | `salesGoal > 0` 是分销合同的不变量 | **(g)** 删除全部 4 处与测试 | 保守做法：(e) 回填 `max(1, ceil(count*0.8))` |
| A14 | `Missions/CorporateNetwork.Quests.cs:57-60,833,840-848` `RepairMissionQuestRoot`；`Introduction/CorporateIntroduction.cs:407,447-454,799` `RepairIntroductionQuestRoot` | 注释说明是“早期开发存档和夹具”中用 MakeRaw 生成、缺少根的任务。所有发布版本都用 `QuestUtility.GenerateQuestAndMakeAvailable` 生成（`git show e93677d:…CorporateIntroduction.cs:216`、`…Quests.cs:264`） | `Quest.root` 恒有值 | **(g)** | 如果重构会重命名 QuestScriptDef `Mugirl_CorporateMission` 或 `Mugirl_CorporateIntroductionQuest`，则需要 **(b)**，否则 `Quest.root`（`Scribe_Defs "root"`）解析为 null |
| A15 | `Missions/CorporateNetwork.Quests.cs:62,67-79` `RepairFusionSiteGenerationLockout` | ≤0.6.3：`PostMapGenerate` 失败会永久消耗一次性的聚变邀请（0.6.4 加入此修复） | 生成失败时由失败处理本身重新开放邀请（`MissionSite.cs:62-77` 的 catch 分支），读档时不再推断 | **(f)** 执行一次（需要 `Site.HasMap`） | **没有门控**：每次读档都会执行。当前版本生成失败后，它也充当自愈机制；这种语义应移到失败处理的位置 |
| A16 | `Introduction/CorporateIntroduction.cs:15`（`migrated`）、`:62-86`（`LoadedGame` 中 `!initialized` 分支）、`:364-401` `RecoverCourierHistory`（读取 `Mugirl_CourierRaid` 任务与 `QuestPart_SpawnCourier`）、`:569-587`（`migrated` 决定开场白：Long/Late/OldSave）；标签 `:778-779` | 0.6.0 之前、已有运货员但没有开户组件的存档；也包括从未装过模组、中途加装的存档 | 用全局存档版本戳取代 `initialized`；用 `retrofitted`（或 `ContactOrigin` 枚举）取代 `migrated` | **(f)**（需要任务列表、Pawn） | 叙事文本 `Mugirl.CorporateIntro.OpeningOldSave` 等应保留，属于剧情，不是兼容代码 |
| A17 | `Introduction/CorporateIntroduction.cs:32-33,64-66,95-99,108-138` `EnsureCorporateFactionAvailable` | 0.6.0 之前的存档，或中途加装后没有巨企派系（原版只补齐官方 DLC 派系） | 由升级器一次性补齐；调试动作 `DevTriggerRepresentative`（`:337`）可继续调用 | **(f)**（需要 `FactionGenerator` 与世界，失败时按 `retryTicks` 重试） | **没有门控**：每次 `LoadedGame` 都会设置 `factionMigrationPending=true` 并经 `GameComponentUpdate` 轮询 |
| A18 | `Introduction/CorporateIntroduction.cs:18`（注释“旧档缺失时为 false”） | 最后 1.6 构建之前的存档没有 `independentContact` | 不变 | **(g)** | 默认 false 本身就是正确语义，只需删除注释 |
| A19 | `Network/CorporateRecord.cs:13-19`（以 null 作为 Scribe 默认值，“保持与 1.6 相同的存档写法”） | 外观性的形状冻结 | `key` 不可为 null，默认值 `string.Empty` | **(g)** | — |
| A20 | `UI/Terminal/Pages/CorporateHistoryPage.cs:108-110`（“旧存档的历史文本依赖这些 Keyed 键名”）；校验基线 `Source/Tests/MugirlRace.Validation/Baselines/dynamic-lookups.allow.txt:42`，以及 `C.frozen-keys` | `corporateRecords/li/key` 保存的是**翻译键**。全部 17 个：`Mugirl.Corporate.RecordSale/RecordPurchase/RecordRefund`、`Mugirl.CorporateFinance.LoanSigned/Payment/Liquidated/DefaultTitle`、`Mugirl.CorporatePeople.Purchased/Sold`、`Mugirl.CorporateServices.Unlocked/GiftRecord/SupportRecord`、`Mugirl.CQ.AcceptedRecord/ClaimedRecord/FailedRecord/ProtectedRecord`、`Mugirl.CorporateIntro.Title`（10 个发布提交 `git grep` 结果完全一致） | 保存 `CorporateHistoryKind` 枚举，显示时再翻译 | **(c)+(d)** 键到枚举的值映射，见 §C4.5 | 只要重命名任一 Keyed 键（例如 `Mugirl.CQ.*`），就必须执行这项值映射 |
| A21 | `Network/CorporateNetwork.cs:5-7`（“仍以原有的扁平 Scribe 键保存，存档结构与 1.6 相同”） | 设计约束：所有状态以 40 个扁平标签写在组件节点下 | 按领域分子对象 | **(e)**，见 §C4.1 | — |
| A22 | “不能改名”注释：`Introduction/CorporateIntroduction.cs:10`、`Introduction/CorporateIntroductionQuest.cs:7`、`Missions/CorporateMissionQuest.cs:7`、`Missions/CorporateMission.cs:7`、`Services/LordJob_CorporateSupport.cs:6`、`Services/JobDriver_CorporateSupportTend.cs:7`、`UI/Entry/JobDriver_CorporateTalkRepresentative.cs:6`、`UI/Entry/JobDriver_CorporateUseComms.cs:6`、`UI/Terminal/Window_CorporateComms.cs:12`（这一处是测试反射耦合） | 名字冻结 | — | 改名时需要 **(a)/(d)** | — |
| A23 | 仅描述“与 1.6 一致”的注释（不是兼容代码）。Logic：`Finance/CorporateNetwork.Finance.cs:7,448`、`Missions/CorporateNetwork.Quests.cs:302`、`Pricing/CorporateDefCache.cs:31`、`Pricing/Harmony_CorporateWeaponSpecification.cs:11`、`Services/CorporateDiehard.cs:39`、`Trade/CorporateDropSpotOrder.cs:5`、`Trade/CorporateNetwork.Trade.cs:30`、`Trade/CorporateTradeContext.Delivery.cs:176,229,279`。UI/Kit：`CorporateClock.cs:11`、`CorporateEasing.cs:5,9,17,28`、`CorporateLayout.cs:5`、`CorporateTextCache.cs:5,8`、`CorporateUI.Controls.cs:12`、`CorporateUI.cs:16,131`、`CorporateUI.Decor.cs:60`、`CorporateUI.Money.cs:14,20`、`CorporateUI.ScrollView.cs:172`。Catalog：`CorporateBrowserText.cs:5,54`、`CorporateCatalogPage.cs:313-314`、`CorporateOrderCatalog.cs:189`、`CorporatePriceBuckets.cs:10`、`CorporateSearchMatcher.cs:6,16,18`。Pages：`CorporateFinancePage.cs:231,264`、`CorporateHistoryPage.cs:75`、`CorporateMissionsPage.cs:46,272`、`CorporateTradePage.cs:222` | — | 改写为“本代码的意图”，把迁移对照记录移入文档 | (g) | 生产级代码中不应保留以历史实现为参照的说明 |

**其他已确认的事项**

- 本模块没有 `GetNamedSilentFail`。1.6 的 `CorporateQuestDefs` 曾使用它，并在缺失时回退到 Villager（`1.6/…/CorporateQuestContract.cs:221-228`）；1.7 改为 `[DefOf]`（`Missions/CorporateQuestConfigDef.cs:41-66`）。
- 巨企 XML 中的 `MayRequire` 都指向 DLC 内容，不涉及已删除的内容。
- 没有专门为存档保留的空壳类型。
- `Compatibility/Savegames/CorporateValidationSaveMigration.cs:8-52`（不在本目录内，但属于巨企）会在 `Game.ExposeSmallComponents` 的 Prefix（LoadingVars）中删除旧验证构建误注册的 `Mugirl.CorporateRuntimeValidation` 与 `Mugirl.CorporateVisualValidation` 组件。它属于 (e) 类，正好可以作为统一升级器 XML 阶段的挂载点与样板。
- `Missions/CorporateNetwork.Quests.cs:888` 是有意设计，不需要升级：≤0.6.4 已完成且选择“保护”的聚变合同没有谢礼货物（`fusionProtectionRewards` 在 0.6.5 加入），代码有意不补发。
- `Network/CorporateNetwork.cs:89` 中的“旧档”指长时间未读档后的周期追赶，不属于版本兼容。

---

## B. 存档可见面

### B1. 以 `Class="完整类型名"` 写入存档的类型

原版 `Scribe_Deep.Look<T>` 只在 `target.GetType() != typeof(T)` 时写出 `Class` 属性（已核实 1.7 源码）。全部类型在 1.6 冻结版中的完整类型名都相同。

| 完整类型名（1.6 = 1.7） | 原版基类 | 在存档中的位置 | 自身标签 | XML 引用 | 未映射就改名的后果 |
|---|---|---|---|---|---|
| `Mugirl.CorporateNetwork` | GameComponent | `game/components/li` | §B3（40 个） | 无 | `li` 无法解析，`FillComponents` 新建空组件：**巨企全部状态和 Vault 中的物品、展示人员一并丢失** |
| `Mugirl.CorporateIntroduction` | GameComponent | `game/components/li` | §B5（18 个） | 无 | 开户进度丢失，A16 的补装路径会被重新触发 |
| `Mugirl.QuestPart_CorporateMission` | QuestPart | `questManager/quests/li/parts/li` | `missionId`（`Missions/CorporateMissionQuest.cs:39`，无默认值） | 无 | `Quest.ExposeData` 删除 null part 并记录错误；任务失去描述与查看目标 |
| `Mugirl.QuestPart_CorporateIntroduction` | QuestPart | 同上 | 只有基类字段（`Introduction/CorporateIntroductionQuest.cs:18-55`） | 无 | 同上 |
| `Mugirl.LordJob_CorporateSupport` | LordJob | `maps/li/lordManager/lords/li/lordJob` | `fallback`、`supportGraphVersion` | 无 | **NRE**：`Lord.ExposeData_StateGraph` 在 PostLoadInit 读取 `curJob.LostImportantReferenceDuringLoading`，而 `curJob` 为 null |
| `Mugirl.JobDriver_CorporateSupportTend` | JobDriver_TendPatient | `…/pawn/jobs/curDriver` | 基类字段 + `patientWaitJobId` | `1.7/Defs/Corporate/CorporateSupportDuty.xml:5` driverClass | `curDriver` 为 null，原版警告并结束 Job（`Pawn_JobTracker.ExposeData`） |
| `Mugirl.JobDriver_CorporateUseComms` | JobDriver | 同上 | 只有基类字段 | `1.7/Defs/Corporate/CorporateIntroduction.xml:34` | 同上 |
| `Mugirl.JobDriver_CorporateTalkRepresentative` | JobDriver | 同上 | 只有基类字段 | `1.7/Defs/Corporate/CorporateIntroduction.xml:39` | 同上 |

**XML 可见但不写入存档的类型：**

- `QuestNode_Root_CorporateMission`：`1.7/Defs/QuestScriptDefs/CorporateQuests.xml:33`。
- `QuestNode_Root_CorporateIntroduction`：`1.7/Defs/Corporate/CorporateIntroduction.xml:9`。任务只按 defName 保存 root，不保存节点类。
- `SitePartWorker_Corporate`：`CorporateQuests.xml:39,51`。worker 由 SitePartDef 创建。
- `JobGiver_CorporateSupportTend`：`CorporateSupportDuty.xml:23`。
- 5 个 Def 类，以 XML 元素名引用：
  - `Mugirl.CorporateTradeSettingsDef`（`Defs/Misc/CorporateTradeSettings.xml:3`）
  - `Mugirl.CorporateFinanceConfigDef`（`Defs/Misc/CorporateFinance.xml:3`）
  - `Mugirl.CorporatePeopleConfigDef`（`Defs/Misc/CorporatePeople.xml:3`）
  - `Mugirl.CorporateQuestConfigDef`（`Defs/QuestScriptDefs/CorporateQuests.xml:4`）
  - `Mugirl.CorporateIntroductionDef`（`Defs/Corporate/CorporateIntroduction.xml:11`）

**既不写入存档也不被 XML 引用的类型：**

- `LordToil_CorporateSupportHunt`：由 `CreateGraph` 重建。Lord 只保存 toil 下标、`LordToilData` 与 `TriggerData`（见 1.7 `Verse.AI.Group.Lord.ExposeData_StateGraph`）。
  - **对校验器的更正**：`Source/Tests/MugirlRace.Validation/Checks/SaveContractChecks.cs:16-23` 把 `Verse.AI.Group.LordToil` 列为存档可见根，这是过度保守的。LordToil 不写入存档，只有图形状（下标）有影响。
- `FloatMenuProvider_CorporateRepresentative`：原版按反射实例化。
- 全部 Window 与 Harmony 类。
- `CorporatePersonnelTrader`：临时的 ITrader。

### B2. 内联保存的 IExposable（不写 `Class`）

声明类型与运行时类型相同（都是 sealed）。

| 类型 | 所在节点 | 改名是否影响存档 |
|---|---|---|
| `CorporateRecord`（sealed） | `corporateRecords/li` | 不影响 |
| `CorporateStock`（sealed） | `corporateStock/li` | 不影响 |
| `CorporateOrder`（sealed） | `corporateOrders/li` | 不影响 |
| `CorporatePersonOffer`（sealed） | `corporatePeopleOffers/li` | 不影响 |
| `CorporateMission`（sealed） | `corporateMissions/li` | 不影响。但如果改成多态子类，会开始写 `Class`，见 §C4.3 |
| 原版 `ThingOwner<Thing>` | `corporateVault`（ctorArgs `this`）、`gifts` | — |
| 原版 `ThingDefCountClass` | `ingredients/li`：`thingDef`、`stuff`、`count`(1)、`quality`(Awful)、`color`、`chance` | — |

本模块没有 Comp、Hediff 或 Worker 一类的内联存档者。

### B3. `CorporateNetwork` 全部顶层标签（按写入顺序，父节点为 `game/components/li[@Class="Mugirl.CorporateNetwork"]`）

| # | 标签 | Scribe | 字段类型 | 默认值 | 1.7 位置 | 引入版本 |
|---|---|---|---|---|---|---|
| 1 | `corporateVault` | Deep（ctorArgs `this`） | `ThingOwner<Thing>` | — | `Network/CorporateNetwork.cs:131` | 0.6.0 |
| 2 | `corporateNextRefresh` | Values | int | −1 | `:132` | 0.6.0 |
| 3 | `corporateSequence` | Values | int | 0 | `:133` | 0.6.0 |
| 4 | `corporateRecords` | Collections Deep | `List<CorporateRecord>` | — | `:134` | 0.6.0 |
| 5 | `corporateStock` | Collections Deep | `List<CorporateStock>` | — | `Trade/CorporateNetwork.Trade.cs:564` | 0.6.0 |
| 6 | `corporateOrders` | Collections Deep | `List<CorporateOrder>` | — | `:565` | 0.6.0 |
| 7–22 | `corporateLoanPrincipal`(float 0)、`corporateLoanInterest`(float 0)、`corporateLoanOverdueInterestTotal`(float 0)、`corporateLoanOriginalPrincipal`(int 0)、`corporateLoanLastAccrualTick`(0)、`corporateLoanDueTick`(0)、`corporateLoanNextRaidTick`(0)、`corporateLoanDefaulted`(false)、`corporateLoanThreeDayReminder`(false)、`corporateLoanOneDayReminder`(false)、`corporateLoanCollateralValue`(0)、`corporateLoanCollateralLiquidated`(false)、`corporateFinancePendingSilver`(0)、`corporateLoanSignedDailyRate`(0.02f)、`corporateLoanSignedOverdueRate`(0.03f)、`corporateLoanSignedLiquidationRatio`(0.5f) | Values | 见括号 | 见括号 | `Finance/CorporateNetwork.Finance.cs:79-94` | 0.6.0 |
| 23 | `corporateLoanFaction` | References | Faction | — | `:95` | 0.6.0 |
| 24 | `corporateLoanCollateral` | Collections Reference | `List<Thing>` | — | `:96` | 0.6.0 |
| 25 | `corporatePeopleOffers` | Collections Deep | `List<CorporatePersonOffer>` | — | `People/CorporateNetwork.People.cs:20` | 0.6.0 |
| 26 | `corporatePeopleAwaitingWorld` | Collections Reference | `List<Pawn>` | — | `:21` | 0.6.0 |
| 27 | `corporatePeoplePendingSilver` | Values | int | 0 | `:22` | 0.6.0 |
| 28 | `corporateMissions` | Collections Deep | `List<CorporateMission>` | — | `Missions/CorporateNetwork.Quests.cs:46` | 0.6.0 |
| 29 | `nextMissionId` | Values | int | 1 | `:47` | 0.6.0 |
| 30 | `fusionQualificationSeen` | Values | bool | false | `:48` | 0.6.0 |
| 31 | `fusionInvitationCreated` | Values | bool | false | `:49` | 0.6.0 |
| 32 | `fusionInvitationTick` | Values | int | −1 | `:50` | 0.6.0 |
| 33 | `retainedFusionInvestor` | References | Pawn | — | `:51` | 0.6.0 |
| 34 | `retainedFusionInvestorName` | Values | string | null | `:52` | 0.6.0 |
| 35 | `corporateTradeTurnover` | Values | long | 0L | `Services/CorporateNetwork.Services.cs:257` | 0.6.1.1 |
| 36 | `corporateCreditedPersonIds` | Collections Value | `List<int>` | — | `:258` | 0.6.7 |
| 37 | `corporateGiftWeek` | Values | int | −1 | `:259` | 0.6.1.1 |
| 38 | `corporateFreePersonWeek` | Values | int | −1 | `:260` | 0.6.1.1 |
| 39 | `corporateSupportWeek` | Values | int | −1 | `:261` | 0.6.1.1 |
| 40 | `corporateSupportCallsUsed` | Values | int | **−1**（A10 sentinel） | `:262` | 0.6.1.1 |

不保存的字段：`nextServiceTick`、`peopleNextCleanupTick`、`nextMissionCheck`、静态的 `defaultedLoanFaction`。
1.6 对应位置：`1.6/…/CorporateNetwork.cs:75-92`、`…Trade.cs:412-420`、`…Finance.cs:80-105`、`…People.cs:72-104`、`…Quests.cs:26-44`、`…Services.cs:156-172`。标签完全相同。

### B4. 嵌套 IExposable 的标签

**`CorporateRecord`**（`Network/CorporateRecord.cs:15-18`；1.6 对应 `1.6/…/CorporateNetwork.cs:113-119`）

| 标签 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `tick` | int | 0 | — |
| `key` | string | null | 翻译键，见 A20 |
| `detail` | string | null | **写入时已翻译的文本**，见 §C4.5 |
| `amount` | int | 0 | — |

**`CorporateStock`**（`Trade/CorporateStock.cs:14-17`；1.6 对应 `…Trade.cs:12-25`）

| 标签 | Scribe | 类型 / 默认值 |
|---|---|---|
| `id` | Values | int, 0 |
| `sample` | References | Thing，指向 Vault 中的物品 |
| `count` | Values | int, 0 |
| `unitPrice` | Values | int, 0 |

**`CorporateOrder`**（`Trade/CorporateOrder.cs:26-39`；1.6 对应 `…Trade.cs:42-63`）

| 标签 | Scribe | 类型 / 默认值 |
|---|---|---|
| `id` | Values | int, 0 |
| `def` | Defs | ThingDef |
| `savedLabel` | Values | string, null（写入时已翻译的 LabelCap） |
| `quantity` | Values | int, 0 |
| `deliveredQuantity` | Values | int, 0 |
| `creditedTurnover` | Values | int, **−1**（A1） |
| `paid` | Values | int, 0 |
| `createdTick` | Values | int, 0 |
| `readyTick` | Values | int, 0 |
| `speed` | Values | enum `CorporateOrderSpeed`, Standard |
| `expediteFee` | Values | int, 0 |
| `state` | Values | enum `CorporateOrderState`, Preparing |
| `goods` | Collections Reference | `List<Thing?>` |

**`CorporatePersonOffer`**（`People/CorporatePersonOffer.cs:20-28`；1.6 对应 `…People.cs:49-60`）

| 标签 | Scribe | 类型 / 默认值 |
|---|---|---|
| `id` | Values | int, 0 |
| `pawn` | References | Pawn，指向 Vault |
| `price` | Values | int, 0 |
| `paid` | Values | bool, false |
| `delivered` | Values | bool, false |
| `paidAmount` | Values | int, **−1**（A7） |
| `buyAsColonist` | Values | bool, false |
| `transferApplied` | Values | bool, false |
| `priceVersion` | Values | int, **0**（类内初始值为 1，A5） |

**`CorporateMission`**（`Missions/CorporateMission.cs:132-165`；1.6 对应 `1.6/…/CorporateQuestContract.cs:125-158`）

- Values（未显式给默认值者默认为 default(T)）：`id`、`kind`（enum）、`state`（enum）、`offeredTick`、`acceptByTick`、`acceptedTick`、`deadline`、`duration`（7*60000）、`cost`、`reward`、`goodwill`、`count`、`progress`、`salesGoal`（0）、`batches`、`highRisk`、`spawned`、`dialogueShown`、`originalInvestor`、`gratitudeRevoked`、`choice`（enum）、`threatPoints`（float）、`tile`（PlanetTile，默认 Invalid）。
- Defs：`product`（ThingDef）、`stuff`（ThingDef）、`recipe`（RecipeDef）、`nativeRoot`（QuestScriptDef）。
- References：`enemy`（Faction）、`site`（Site）、`quest`（Quest）、`investor`（Pawn）。
- Collections：`targets`（Reference，`List<Pawn?>`）、`ingredients`（Deep，`ThingDefCountClass`）、`pendingGoods`（Reference，`List<Thing>`，指向 Vault）。
- 保存时序：`Saving` 阶段在写出前先执行 `RecordDeadTargets()`（`:127-130`），把已死亡的目标折算进 `progress`。

### B5. `CorporateIntroduction` 的标签

位置：`Introduction/CorporateIntroduction.cs:778-795`；父节点为 `game/components/li[@Class="Mugirl.CorporateIntroduction"]`。1.6 对应 `1.6/…/CorporateIntroduction.cs:504-529`，标签完全相同。

| 标签 | Scribe | 类型 / 默认值 | 说明 |
|---|---|---|---|
| `corporateIntroductionInitialized` | Values | bool, false | 唯一带前缀的标签 |
| `migrated` | Values | bool, false | — |
| `pending` | Values | bool, false | — |
| `completed` | Values | bool, false | — |
| `independentContact` | Values | bool, false | — |
| `giftsPrepared` | Values | bool, false | — |
| `independentAggression` | Values | bool, false | — |
| `courierKilledByPlayer` | Values | bool, false | — |
| `courierDied` | Values | bool, false | — |
| `courierChoice` | Values | int, 0 | 0 未知 / 1 放行 / 2 冲突 |
| `contactTick` | Values | int, −1 | — |
| `nextServiceTick` | Values | int, 0 | **瞬态节流值却被保存** |
| `representativeArrivalTick` | Values | int, −1 | — |
| `courier` | References | Pawn | — |
| `representative` | References | Pawn | — |
| `preferredMap` | References | Map | — |
| `introductionQuest` | References | Quest | — |
| `gifts` | Deep（ctorArgs `this`） | `ThingOwner<Thing>` | — |

### B6. 地图与任务中的其他标签

| 所有者 | 标签 | 位置 | 1.6 对应 |
|---|---|---|---|
| `LordJob_CorporateSupport` | `fallback`（IntVec3，默认值无） | `Services/LordJob_CorporateSupport.cs:70` | 1.6 不调用 base（`…Services.cs:222-227`），基类本来为空，无影响 |
| 同上 | `supportGraphVersion`（int，Scribe 默认 0，类内初始值 1） | `:72` | 同上 |
| `JobDriver_CorporateSupportTend` | `patientWaitJobId`（int，默认 −1） | `Services/JobDriver_CorporateSupportTend.cs:15` | `1.6/…/CorporateSupportTending.cs:167-171` |
| `QuestPart_CorporateMission` | `missionId` | `Missions/CorporateMissionQuest.cs:39` | `1.6/…/CorporateQuestSite.cs:36-40` |

### B7. 枚举、Def 与引用

**按成员名保存的枚举**（成员自 0.6.0 起从未改变）：

| 枚举 | 成员 | 保存处 |
|---|---|---|
| `CorporateOrderState` | Preparing, Ready, Completed, Cancelled, RefundDue, Refunded | `CorporateOrder.state` |
| `CorporateOrderSpeed` | Standard, Express, Priority | `CorporateOrder.speed`（50f7979 起） |
| `CorporateMissionKind` | Processing, Purge, Investment, Distribution, Native, Fusion | `CorporateMission.kind` |
| `CorporateMissionState` | Available, Active, Ready, Completed, Failed, Expired, Abandoned | `CorporateMission.state` |
| `CorporateResearchChoice` | Undecided, Execute, Protect | `CorporateMission.choice` |

另有一个用 int 编码的伪枚举：`courierChoice`（0/1/2）。

**`Scribe_Defs` 保存的 Def 类型：**

- ThingDef：`order.def`、`mission.product`、`mission.stuff`，以及 `ingredients` 中的 `thingDef`/`stuff`。
- RecipeDef：`mission.recipe`。
- QuestScriptDef：`mission.nativeRoot`，取原版 `TradeRequest` 或 `OpportunitySite_ItemStash`。

**被原版间接写入存档的巨企 defName**（1.6 与 1.7 相同）：

| Def 类型 | defName | 写入处 |
|---|---|---|
| QuestScriptDef | `Mugirl_CorporateMission`、`Mugirl_CorporateIntroductionQuest` | `Quest.root` |
| SitePartDef | `Mugirl_CorporatePurgeSite`、`Mugirl_CorporateResearchSite` | Site 的 part |
| JobDef | `Mugirl_CorporateUseComms`、`Mugirl_CorporateTalkRepresentative`、`Mugirl_CorporateSupportTend` | `Job.def` |
| DutyDef | `Mugirl_CorporateSupportHunt` | `PawnDuty` |
| TraitDef | `Mugirl_CorporateDiehard` | 特性 |
| ThoughtDef | `Mugirl_CorporateSoldMugirl` | 记忆 |
| PawnKindDef | `Mugirl_CorporateShowcase`、`Mugirl_CorporateRepresentative`、`Mugirl_CorporateSupport`、`AI_GC_Courier` | Pawn |
| FactionDef | `Mugirl_GiantCorporations_Hostile` | 派系 |

不写入存档的巨企 Def：5 个配置 Def、`TraderKindDef Mugirl_CorporatePersonnelTrader`（临时 ITrader）、`PrefabDef Mugirl_CorporateFusionResearchSite`。

**`Scribe_References` 的目标：**

- Thing：`stock.sample`、`order.goods`、`loanCollateral`、`mission.pendingGoods`，全部由 Vault 深度保存。
- Pawn：`offer.pawn`、`peopleAwaitingWorld`（在 Vault 中）、`retainedFusionInvestor`（世界 Pawn）、`mission.investor`、`mission.targets`（在地图上）、`courier`、`representative`。
- Faction：`loanFaction`、`mission.enemy`。
- 其他：`mission.site`（WorldObject）、`mission.quest` 与 `introductionQuest`（Quest）、`preferredMap`（Map）。

**对升级器的含义：** 移动 `corporateVault` 节点是安全的（引用按 loadID 解析），但 Vault 必须在整个存档中只被深度保存一次。另外，Vault 的 ThingOwner 所有者（ctorArgs）必须仍是一个 `IThingHolder`。

### B8. 1.7 树已引入的差异（相对冻结的 1.6）

**标签、类型名、枚举、defName 都没有变化。** 以下是行为上的差异：

1. **迁移时机改变。** People 与 Trade 的迁移从 Scribe 的 PostLoadInit（`1.6/…/People.cs:77-103`）和 `PrepareOrderCatalog`（`1.6/…/Trade.cs:85-101`）移到 `LoadedGame`（`Network/CorporateNetwork.cs:113-120`），两者仍然是每次读档都执行。
2. **新增 `records.RemoveAll(null)`**（`Network/CorporateNetwork.cs:144`）。
3. **新增 `base.ExposeData()` 调用**（`CorporateNetwork`、`LordJob`）。实际无影响：1.7 的 `GameComponent.ExposeData` 与 `LordJob.ExposeData` 都是空方法。
4. **委托内容改用 `[DefOf]` 强绑定。** `CorporateQuestDefs` 由 `GetNamedSilentFail`（带 Villager 回退）改为 `[DefOf]`，对 Core 中的 `TradeRequest`、`Mercenary_Gunner` 形成硬依赖（`Missions/CorporateQuestConfigDef.cs:41-56`）。
5. **订购目录排序改为进程级缓存**（`Trade.cs:13,31-35`）。不影响存档。

---

## C. 命名与架构问题

### C1. 推断出的约定

- **命名空间。** ADR-004（`docs/architecture/ADR-004-root-namespace-markers.md`）规定新类型使用 `Mugirl.Features.<Feature>`，根命名空间只用于 XML、Scribe 或 Harmony 需要冻结的名字。
  - 本模块中较新的纯计算与 UI 代码已遵循这条规则，例如 `CorporateMoney`、`CorporateDefCache`、`CorporateTerminalPage` 及各页面。
  - 新方向撤销了“冻结”理由，因此整个模块都应进入 `Mugirl.Features.Corporate`。
  - Harmony 元数据基线已经使用完整类型名（`Baselines/patch-targets.txt:4-28`），改命名空间不再受 ADR-002 的限制。
- **原版风格的类型名。** 使用 `JobDriver_X`、`LordJob_X`、`QuestPart_X`、`SitePartWorker_X`、`FloatMenuOptionProvider_X`。
  - 全仓库自建的提供者都叫 `FloatMenuProvider_*`，与原版不同，但仓库内部一致。这里不建议单独修改。
- **Harmony 补丁类名。** 仓库约定是 `Harmony_<Feature><Target>`（91 个），另有 24 个使用 `*_Patch`。
- **配置 Def。** 使用 `<Domain>ConfigDef`，现有 Finance、People、Quest 三个遵循，Trade 与 Introduction 不一致。
- **Scribe 标签。** 应为 lowerCamel 的字段名，不加冗余前缀。原版风格即如此，例如 Lord 的 `ticksInToil`。

### C2. 命名空间问题

1. **56 个文件在根命名空间 `Mugirl`。** 其中真正存档可见或 XML 可见的只有 B1 列出的 17 个类型（8 个存档可见、9 个仅 XML 可见）。其余全部无冻结理由，包括：
   - 数据类型：`CorporateRecord`、`CorporateStock`、`CorporateOrder`、`CorporateOrderState`、`CorporateOrderSpeed`、`CorporatePersonOffer`、`CorporatePersonQuote`、`CorporateMission` 及其 3 个枚举。
   - 工具与内容类型：`CorporatePersonnelTrader`、`CorporateShowcaseApparel`、`CorporateQuestDefs`、`CorporateQuestSiteDebugActions`、`CorporateSupplyAnalysis`、`CorporateDiehardUtility`、`CorporateSupportUtility`、`CorporateUniforms`、`CorporateTradeContext`、`CorporateCommsAccess`。
   - UI 类型：`CorporateMotion`、`CorporateUI`、`CorporateAnimatedWindow`、`CorporateChoiceWindow`、`CorporateConfirmWindow`、`Window_CorporateComms`。
   - 全部 Harmony 类。
2. **同一文件夹内命名空间混用：**
   - `UI/Kit`：`CorporateMotion`、`CorporateUI.*` 在 `Mugirl`；`CorporateClock`、`CorporateEasing`、`CorporateLayout`、`CorporateTextCache` 在 `Mugirl.Features.Corporate`。
   - `Pricing`：`CorporateMoney`、`CorporateDefCache` 在 F.C；其余在 `Mugirl`。
   - `Trade`：只有 `CorporateDropSpotOrder` 在 F.C。
   - `Missions`：只有 `CorporateFusionSiteBuilder` 在 F.C。
3. **必须同步修改的位置：**
   - XML 引用。
   - 校验器基线：`save-types.txt:4-5`、`patch-targets.txt`、`transpiler-hooks.txt:3`、`dynamic-lookups.allow.txt:38-48`。
   - 测试引用：`UiSmokeSuite.cs:66-83`、`CorporateMoneyTests.cs`。

### C3. 改名建议表

“存档可见”指改名后需要升级器处理；“XML 可见”指 Defs/Patches 按类名引用。

| 种类 | 旧名 | 新名 | 理由 | 存档可见 | XML 可见 |
|---|---|---|---|---|---|
| 命名空间 | `Mugirl`（56 个文件） | `Mugirl.Features.Corporate`（UI 可选 `.UI` 子空间） | ADR-004；冻结理由已撤销 | 8 个类型，需 (a) | 9 个类型 |
| 类型 | `Mugirl.CorporateNetwork` | `Mugirl.Features.Corporate.CorporateNetwork` | 只改命名空间 | **是，(a) 必需** | 否 |
| 类型 | `Mugirl.CorporateIntroduction` | 同上方式改命名空间，或并入 `CorporateNetwork`（§C4.2） | — | 是，(a) 或 (e) | 否 |
| 类型 | `Mugirl.QuestPart_CorporateMission`、`Mugirl.QuestPart_CorporateIntroduction` | 只改命名空间 | — | 是，(a) | 否 |
| 类型 | `Mugirl.LordJob_CorporateSupport` | 只改命名空间 | — | **是，(a) 必需，否则 NRE** | 否 |
| 类型 | `Mugirl.JobDriver_CorporateSupportTend`、`JobDriver_CorporateUseComms` | 只改命名空间 | — | 是，(a)，未映射只产生警告 | 是（driverClass） |
| 类型 | `Mugirl.JobDriver_CorporateTalkRepresentative` | 只改命名空间，并改为继承 `JobDriver_MugirlTalkToPawn` | 去重（§C4.13） | 是，(a) | 是 |
| 类型 | `QuestNode_Root_CorporateMission`、`QuestNode_Root_CorporateIntroduction`、`JobGiver_CorporateSupportTend` | 只改命名空间 | — | 否 | 是 |
| 类型 | `SitePartWorker_Corporate` | `SitePartWorker_CorporateMission` | 名字过于笼统，实际服务两种委托站点 | 否 | 是（2 处） |
| 类型 | `CorporateTradeSettingsDef` | `CorporateTradeConfigDef` | 与 Finance、People、Quest 的 `*ConfigDef` 统一 | 否 | 是（Def 元素名） |
| 类型 | `CorporateIntroductionDef` | `CorporateIntroductionConfigDef` | 统一后缀；避免与 QuestScriptDef `Mugirl_CorporateIntroductionQuest`、组件 `CorporateIntroduction` 混淆 | 否 | 是 |
| 类型 | `CorporateQuestConfigDef` | `CorporateMissionConfigDef` | 同一概念只用一个词，见下面的术语统一 | 否 | 是 |
| defName | `Mugirl_CorporateQuestConfig` | `Mugirl_CorporateMissionConfig` | 同上 | 否 | 是 |
| 类型 | `CorporateQuestDefOf`、`CorporateTradeDefOf`、`CorporateFinanceDefOf`、`CorporatePeopleDefOf`、`CorporateIntroductionDefOf` | 合并为一个 `CorporateDefOf` | 碎片化；`CorporateTradeDefOf` 中混入了非巨企的 `Mugirl_Wool` 和原版 `Furniture`（`Trade/CorporateTradeSettings.cs:33-37`） | 否 | 否 |
| 类型 | `CorporateQuestDefs` | `CorporateMissionContent` | 术语统一 | 否 | 否 |
| 类型 | `CorporateQuestSiteDebugActions` | `CorporateMissionDebugActions` | 术语统一 | 否 | 否 |
| 文件与方法 | `Missions/CorporateNetwork.Quests.cs`；`QuestsExposeData`、`QuestsTick`、`QuestsRefreshWeekly` | `CorporateNetwork.Missions.cs`；`Missions*` | 所在文件夹叫 Missions，字段叫 missions，方法却叫 Quests | 否 | 否 |
| 类型 | `CorporateStock` | `CorporateStockOffer` | 列表才叫 stock，单行是报价；与 `CorporatePersonOffer` 对称 | 否（内联） | 否 |
| 类型 | `CorporateRecord` | `CorporateHistoryEntry` | 与 UI 的 History 页和用途一致 | 否（内联） | 否 |
| 类型 | `CorporateDebt_HostileTo_Patch`、`CorporateDebt_RelationKind_Patch`、`CorporateDebt_Goodwill_Patch`（`Finance/Harmony_CorporateDebt.cs:10,29,48`） | `Harmony_CorporateDebtHostileTo`、`Harmony_CorporateDebtRelationKind`、`Harmony_CorporateDebtGoodwill` | 仓库约定 `Harmony_*` | 否 | 否（只涉及基线） |
| 类型 | `CorporateDiehardUtility`（文件为 `CorporateDiehard.cs`） | `CorporateDiehard`，或把文件改名为 `CorporateDiehardUtility.cs` | 文件名与类型名不一致 | 否 | 否 |
| 属性 | `CorporateMission.IsSide` | `IsFusion` | “Side”含义不明，实际就是 `kind == Fusion` | 否 | 否 |
| 枚举值 | `CorporateMissionKind.Native` | `Referral` | 语义是“转介原版任务”，Native 有误导 | 是，**(d)** | 否 |
| 字段与标签 | `courierChoice`（int 0/1/2） | `CourierOutcome courierOutcome { Unknown, Released, Confronted }` | 消除魔数 | 是，(c)+(d) 值映射：0→Unknown、1→Released、2→Confronted | 否 |
| 字段与标签 | `migrated` | `retrofitted`，或 `ContactOrigin` 枚举 | 实际含义是“中途加装或补装”，与迁移无关 | 是，(c) | 否 |
| 标签 | `corporateIntroductionInitialized` | 删除，由全局存档版本戳取代 | A16 | 是，删除 | 否 |
| 标签 | `CorporateIntroduction.nextServiceTick` | 删除（改为不保存） | 瞬态节流值；`CorporateNetwork` 的同名字段本来就不保存 | 是，删除 | 否 |
| 标签 | `priceVersion`、`supportGraphVersion` | 删除 | A5、A12 | 是，删除 | 否 |
| 标签 | 40 个 `corporate*` 前缀标签 | 去前缀并分组（§C4.1） | 所在节点已表明是巨企；前缀使用不一致：Quests 的 7 个标签没有前缀，Introduction 只有 1 个有前缀；`corporateNextRefresh` 缺少 Tick 后缀 | 是，(e) | 否 |
| Keyed 前缀 | `Mugirl.CQ.*` | `Mugirl.CorporateMissions.*` | 缩写与其余 `Mugirl.Corporate*` 不一致 | 其中 4 个键保存在历史记录里（A20） | Keyed |
| Keyed | `Mugirl.CorporateServices.Page` | `Mugirl.CorporateUI.Page.Services` | 其余 8 个页面键都是 `Mugirl.CorporateUI.Page.*`（`UI/Terminal/Window_CorporateComms.cs:33-38`） | 否 | Keyed |
| defName（跨模块） | `Mugirl_GiantCorporations_Hostile`（`1.7/Defs/FactionDefs/Factions_Enemy.xml:5`） | `Mugirl_GiantCorporation` | 该派系可交易，并非永久敌对（没有 `permanentEnemy`）；敌对关系由代码动态判断 | 是，**(b)** | 是 |
| defName（跨模块） | `AI_GC_*` 系列 PawnKindDef（`1.7/Defs/PawnKindDefs/PawnKinds_GiantCorp.xml`，`AI_GC_Courier` 在本模块的 `Services/CorporateUniforms.cs:37`、`Introduction/CorporateIntroduction.cs:291,305` 中使用） | `Mugirl_Corporate*` | 与其余巨企 defName 的前缀不一致 | 是，(b) | 是 |

**术语统一。** 同一个“委托”概念目前混用三个词：

- Quest：`CorporateQuestConfigDef`、`CorporateQuestDefOf`、`Mugirl.CQ.*`、`QuestsTick`。
- Mission：`CorporateMission`、`Missions/`、`corporateMissions`。
- Contract：1.6 的文件名 `CorporateQuestContract.cs`、注释“合同”。

建议统一为 **Mission**。原版类型 `QuestPart_*` 与 `QuestNode_*` 本来就带 Quest，予以保留。

### C4. 其他生产级重构建议（附理由）

**1. 拆分 `CorporateNetwork` 的扁平状态（(e) 映射）。**
目前 40 个顶层标签由 6 个 partial 文件共同写入同一个节点，各领域的不变量（PostLoadInit 修复、null 保护）分散在各处。建议每个领域一个 sealed IExposable 子对象：`Trade`、`Loan`、`People`、`Missions`、`Services`，再加上 `History`。Vault 留在组件上，因为它是 `IThingHolder` 的所有者。

建议的映射：

| 旧标签 | 新路径 |
|---|---|
| `corporateVault` | `vault` |
| `corporateNextRefresh` | `nextWeekTick` |
| `corporateSequence` | `idSequence` |
| `corporateRecords` | `history` |
| `corporateStock`、`corporateOrders` | `trade/stock`、`trade/orders` |
| 16 个 `corporateLoan*` 及 `corporateLoanFaction`、`corporateLoanCollateral` | `loan/{principal, interest, overdueInterestTotal, originalPrincipal, lastAccrualTick, dueTick, nextRaidTick, defaulted, threeDayReminderSent, oneDayReminderSent, collateralValue, collateralLiquidated, dailyRate, overdueDailyRate, liquidationRatio, creditor, collateral}` |
| `corporateFinancePendingSilver` | `loan/pendingSilver` |
| `corporatePeople*` | `people/{offers, awaitingWorld, pendingSilver}` |
| `corporateMissions` | `missions/list` |
| `nextMissionId` | `missions/nextId` |
| 5 个 `fusion*` 与 `retainedFusion*` | `missions/fusion/{qualified, invitationCreated, invitationTick, investor, investorName}` |
| `corporateTradeTurnover` | `services/turnover` |
| `corporateCreditedPersonIds` | `services/creditedPawnIds` |
| `corporateGiftWeek`、`corporateFreePersonWeek` | `services/giftClaimedWeek`、`services/freePersonClaimedWeek` |
| `corporateSupportWeek`、`corporateSupportCallsUsed` | `services/supportWeek`、`services/supportCallsUsed` |

注意：抵押品在没有贷款时也可能存在（结清后待领回，`Finance/CorporateNetwork.Finance.cs:281-291,379-391`）。因此 `Loan` 应建模为始终存在的账户对象，而不是“无贷款时为 null”。

**2. `CorporateIntroduction` 是否并入 `CorporateNetwork`（可选）。**

- 现状：`CorporateNetwork.Unlocked` 依赖另一个组件（`Network/CorporateNetwork.cs:30`）；两个组件各自持有一个 ThingOwner（`vault`、`gifts`）；两者都通过 `Current` 做组件查找。
- 合并的好处：领域只有一个根，见面礼也可以放进 Vault。
- 代价：(a)+(e) 变换，把 `CorporateIntroduction` 的 `li` 内容迁入 `CorporateNetwork/introduction`，并把 gifts 的 innerList 并入 vault。
- 不合并时：至少把命名空间与标签整理一致。

**3. `CorporateMission` 是一个 34 字段的“万能袋”。**
各种类的字段语义互相重叠：`count` 与 `progress` 在加工、清剿、分销中含义不同；`cost` 分别表示押金、投资额、批发价。种类分支散布在以下位置：

- `Description`：`Missions/CorporateMission.cs:86-109`
- 接受：`Quests.cs:441-504`
- 定时检查：`Quests.cs:876-937`
- 领取：`Quests.cs:745-774`
- 放弃：`Quests.cs:790-800`

两种方案：

- **方案 1：** 抽象 `CorporateMission` 加 6 个子类。保存时会写 `Class`；升级器 (e) 按 `kind` 为每个 `li` 添加 `Class` 属性并删除多余字段。
- **方案 2：** 保持单类，把各种类的字段拆进 `payload` 子对象。

无论选哪种，都应把 `Title`、`Status`、`Description`（`CorporateMission.cs:56-109`）移出领域模型，交给 UI 的 presenter。

**4. 金额表示不统一。**
定价使用 decimal（`Pricing/CorporateMoney.cs`），贷款却使用 float（`Finance/CorporateNetwork.Finance.cs:23-25`，注释 `:7` 说明这是为了“沿用 1.6 的 float 存档字段”），并配合 `0.0001f` 的 epsilon 判断（`:47-49`）。

- Verse 的 `ParseHelper` 没有 decimal 专用解析器（反编译中没有 `ParseDecimal` 成员）。
- 建议改为 long 定点数，例如以 1/10000 白银为单位。
- 升级器 (e) 需要做 float → long 的值变换。

**5. 历史记录保存了语言相关的数据。**

- 已翻译的 `detail`：`Introduction/CorporateIntroduction.cs:668` 写入 `"…CompletedStatus".Translate()`；`Services/CorporateNetwork.Services.cs:78` 写入等级字符串；`Trade/CorporateNetwork.Trade.cs:398` 拼接成 `"label ×qty"`。
- 翻译键作为数据：即 A20。

建议的新形状为 `CorporateHistoryEntry { tick, kind: CorporateHistoryKind, subject, quantity, amount }`。升级器 (d) 的值映射：

| 旧 key | 新 kind |
|---|---|
| `Mugirl.Corporate.RecordSale` | `ProductSold` |
| `Mugirl.Corporate.RecordPurchase` | `Purchase` |
| `Mugirl.Corporate.RecordRefund` | `Refund` |
| `Mugirl.CorporateFinance.LoanSigned` | `LoanSigned` |
| `Mugirl.CorporateFinance.Payment` | `LoanPayment` |
| `Mugirl.CorporateFinance.Liquidated` | `CollateralLiquidated` |
| `Mugirl.CorporateFinance.DefaultTitle` | `LoanDefaulted` |
| `Mugirl.CorporatePeople.Purchased` | `PersonPurchased` |
| `Mugirl.CorporatePeople.Sold` | `PersonSold` |
| `Mugirl.CorporateServices.Unlocked` | `ServiceLevelReached` |
| `Mugirl.CorporateServices.GiftRecord` | `WeeklyGiftClaimed` |
| `Mugirl.CorporateServices.SupportRecord` | `SupportCalled` |
| `Mugirl.CQ.AcceptedRecord` | `MissionAccepted` |
| `Mugirl.CQ.ClaimedRecord` | `MissionClaimed` |
| `Mugirl.CQ.FailedRecord` | `MissionFailed` |
| `Mugirl.CQ.ProtectedRecord` | `ResearchersProtected` |
| `Mugirl.CorporateIntro.Title` | `AccountOpened` |

旧记录中已翻译的 `detail` 只能原样保留为 `subject`。

**6. 平衡参数硬编码。** 项目已有 5 个配置 Def，下列常量应移入其中：

- 服务等级门槛、折扣、额外现货、小队规模：`Services/CorporateNetwork.Services.cs:12-22,43-59`。
- 订单数量上限 2500 与品质上限：`Trade.cs:310`。
- 订单数上限 200：`Trade.cs:388`。
- 周刷新的价格与科技阈值、数量区间：`Trade.cs:506-513,523-524`。
- 原料上限 5000：`Quests.cs:234`。
- 清剿点数与奖励公式：`Quests.cs:301-303`。
- 分销数量 700–1800：`Quests.cs:325`。
- 投资倍率：`Quests.cs:500-503`。
- 3 名守卫：`Missions/CorporateNetwork.MissionSite.cs:105`。
- 历史记录与委托历史上限：`Network/CorporateNetwork.cs:13`、`Quests.cs:13`。

此外，`DayTicks = 60000`（`Network/CorporateNetwork.cs:10`）与原版 `GenDate.TicksPerDay` 重复，字面量 `60000` 还出现在 `Missions/CorporateMission.cs:22,139` 和 `UI/Terminal/Pages/CorporateHistoryPage.cs:109`。

**7. 组件上的静态工具方法。**
`MakeProduct`、`MakeOrderProduct`、`DiscardUnspawnedProduct`、`Price`、`PreparationTicks`、`ExpeditedTotal`、`IsEligibleCollateral`、`IsDebtPair`、`ValidSupportCell`、`ProcessingReward` 都是 `CorporateNetwork` 的静态成员，并被其他类型调用（`Services/CorporateUniforms.cs:23`；`People/CorporateShowcaseApparel.cs:80,83,140`）。建议抽出 `CorporateProductFactory` 等独立工具类。

**8. 热路径上的组件查找。**
`CorporateNetwork.Current` 和 `CorporateIntroduction.Current` 最终调用 `Game.GetComponent<T>`，即对组件列表做线性扫描（`Core/MugirlGameUtility.cs:36`）。以下调用点处于热路径：

- `Pawn.PreApplyDamage` 前缀（`UI/Entry/Harmony_CorporateIntroduction.cs:10-13`）：**每次伤害**都扫描一次，开户完成后仍然如此。
- `Thing.TakeDamage` 前缀（`Missions/Harmony_CorporateMissions.cs:52-58`）。

建议沿用债务叠加层的做法（`Finance/CorporateNetwork.Finance.cs:17-21`），在构造时登记静态实例，并在 `Game` 更换时清除。

**9. 死代码。**

- `IsDebtFactionPair`（`Finance/CorporateNetwork.Finance.cs:476-477`），没有任何调用方。
- 删除迁移后会变成死代码的：`TryEnsureOutfit`（A6）、`CorporateMoney.LegacySalesTarget` 及其测试（A13）、`CorporateTradeSettingsDef` 的 4 个字段（A4）。
- `record.key ?? string.Empty`（`UI/Terminal/Pages/CorporateHistoryPage.cs:110`）：`key` 不可能为 null。

**10. 待领款项分散在三处。**

- `corporateFinancePendingSilver`
- `corporatePeoplePendingSilver`
- 免费的 Ready 收据（`SaveRecoveryOrder`，`Trade.cs:166-171`）

可以考虑合并为统一的“待领账户”；如果 UI 需要分开展示，至少在命名上保持对称。

**11. 两套 ID 生成器。** `corporateSequence`（`NewId()`）与 `nextMissionId` 并存。建议合并，或者至少统一命名（`nextId`）与语义：现在一个保存“最后发出的 ID”，另一个保存“下一个 ID”。

**12. 终端页面用 int 常量。**

- 页面编号用 int 常量（`UI/Terminal/Window_CorporateComms.cs:16-24`），作用域字符串表 `PageScopes`（`:40-41`）与之重复。建议改为枚举。
- 测试通过反射按名字访问 `RequestPage`、`selectedPage`、`PageKeys`（`Source/Tests/MugirlRace.GameValidation/Suites/Mod/UiSmokeSuite.cs:81-83`）。建议改为 `InternalsVisibleTo` 加 internal API，去掉这种名字耦合。

**13. 与 Incidents 模块重复的“与 Pawn 交谈”实现。**
`FloatMenuProvider_CorporateRepresentative` 与 `JobDriver_CorporateTalkRepresentative` 重复了 `Features/Incidents/Shared/FloatMenuProvider_MugirlTalkToPawn.cs` 和 `JobDriver_MugirlTalkToPawn.cs` 的逻辑（运货员和聚变投资人已经复用这两个基类）。建议改为继承它们，并把基类移到共享位置。

**14. 文件组织。**

- `UI/Entry/Harmony_CorporateIntroduction.cs` 处理的是开户领域，应移到 `Introduction/`。
- `Services/Harmony_CorporateTradeTurnover.cs` 内含两个数据类型。
- `Missions/CorporateMission.cs` 内含 3 个枚举。
- `Missions/CorporateQuestConfigDef.cs` 同时含 Def、DefOf 和内容名单。
- XML 配置分散在 `1.7/Defs/Misc/Corporate*.xml`、`1.7/Defs/QuestScriptDefs/CorporateQuests.xml`（其中混有配置 Def 与 SitePartDef）、`1.7/Defs/QuestScriptDefs/CorporateFusionResearchSite.xml`（实为 PrefabDef）和 `1.7/Defs/Corporate/`。建议统一放到 `1.7/Defs/Corporate/`。

**15. 不门控的读档逻辑。**
A3、A6、A11、A15、A17 每次读档都会执行。在新架构中，读档路径应只做本 schema 的不变量校验；一次性数据修补全部交给升级器。

**16. 校验器仍在执行“冻结规则”（跨模块）。**
以下检查强制旧规则，需要改为“每个被删除或改名的名字都有升级器映射覆盖”：

- `SaveContractChecks`（`C.save-types`、`C.save-labels`、`C.save-enums`）
- `FrozenContentChecks`（`C.frozen-keys`）
- 基线 `save-types.txt:4-5`、`save-enums.txt`、`dynamic-lookups.allow.txt:42`

同时应从 `SaveVisibleRoots` 中删除 `Verse.AI.Group.LordToil`（见 §B1 的更正）。

### C5. 跨模块注意事项（简述）

- **剧情状态。** `MugirlStoryState`（Incidents）中的 `fusionInvestmentAccepted`、`fusionInvestmentPending`、`openingCrashStarted`、`courierRaidTriggered`、`courierRaidQuestStarted` 由本模块读取（`Missions/CorporateNetwork.Quests.cs:958-962`、`Introduction/CorporateIntroduction.cs:75-78,233-237`）。如果这些标签改名，A16 的补装路径和聚变邀请都依赖新名字。
- **运货员任务。** `QuestPart_SpawnCourier`（Incidents）的 `courier` 与 `map` 字段被 A16 读取（`Introduction/CorporateIntroduction.cs:371-375`）。该类型的注释也写明了这项依赖（`Features/Incidents/Courier/QuestPart_SpawnCourier.cs:6`）。
- **升级器的挂载点。** 可复用 `Compatibility/Savegames/CorporateValidationSaveMigration.cs` 的 `Game.ExposeSmallComponents` Prefix（LoadingVars）。原版在该方法中先读取 `questManager`，再读取 `components`，而地图在其后才加载；同一棵 XML 树中的 world 和 maps 节点此时也都可以改写，因此 A1、A7、A12 和类型映射都能在这一步完成。A5、A8、A15、A16、A17 则需要放到 `LoadedGame` 或 `FinalizeInit` 阶段。
- **需要更新的测试：** `Source/Tests/MugirlRace.Tests/Corporate/CorporateMoneyTests.cs:82-97`（A13）；`Source/Tests/MugirlRace.Tests/Compatibility/CorporateValidationSaveMigrationTests.cs`。
