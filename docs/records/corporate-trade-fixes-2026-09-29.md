# 巨企订单系统三项修复（2026-09-29）

用户报告：订单搜索/数量输入在敲入两个键后第三个键被自动删除；低价高价值的常规途径不可购入物品缺乏底价；雪牛娘服装定价分裂（修女头纱 158 对 兔女郎上衣 20,000）；收购付款白银空投常砸穿屋顶。

## 问题 1：输入框丢字

`CorporateUI.Input` 与原版 `QuickSearchWidget`/`Widgets.TextField` 对照后消除三处偏差：引擎级 `maxLength:1000`、按事件类型切换 `GUI.enabled`（非 Repaint 事件才禁用）、`GUI.color/contentColor` 的非纯白染色与一次性克隆自 `GUI.skin.textField` 的静态样式。这些偏差会让 `GUI.TextField` 内部 `TextEditor` 在 Layout/KeyDown/Repaint 事件间状态不一致，典型表现是中文输入法组合串被打断——头两个键直接提交、第三个组合键被回滚，粘贴（单次提交）不受影响。

修改：`CorporateUI.Input` 按当前 `Text.Font` 从 `Text.textFieldStyles` 克隆样式（清空背景、浅色文字、自定 padding，按字号缓存）；字段绘制期间 `GUI.color` 固定纯白；不可交互时对所有事件统一 `GUI.enabled = false`；不再传引擎级 maxLength。控制名与 `SetNextControlName` 用法保持不变。

限制：输入法行为无法离线复现，修复依据是"与原版逐字节同构"原则，需游戏内实测确认；英文直接键入与粘贴原本就正常。

## 问题 2：价格算法

根因：`Mugirl_ApparelBase` 全线 `tradeability=Sellable`，旧 `IsSpecialOrder` 把所有 Sellable 物品判入特供档（×20、每件不低于 20,000）；而头饰基类 `Mugirl_HeadBase` 走原版 `HatMakeableBase`（默认 All）按普通档计价，造成 158 对 20,000 的分裂。

- `IsSpecialOrder`：Sellable 且玩家可自行制作（`def.recipeMaker != null` 或任意 `RecipeDef.products` 包含，按进程缓存）的物品不再算特供。雪牛娘服装全线可织造，回归普通/稀有档；机械液、AI 核心等不可自制的 Sellable 稀有物维持特供。
- 新增 `unsourcedMinimumUnitPrice`（默认 500）：没有任何商人出售生成器供应、且基础市值低于底价一半的目录物品，订购价与每周现货价不低于该底价。判定扫描全部 `TraderKindDef.stockGenerators`（跳过 `StockGenerator_Buy*` 仅收购型），复用生成器自身的 `HandlesThingDef`（`StockGenerator_Category` 使用 `DescendantThingDefs`，羊毛经 Textiles 正确覆盖；钢经 `StockGenerator_SingleDef` 覆盖），候选集限定低市值非 staple 物品避免全量扫描，在 `PrepareOrderCatalog` 预热。
- 各档底价改为在服务折扣之后取 `max`，"每件不低于 N"不再被服务折扣稀释。
- 效果：兔女郎上衣按 max(市场价值×档位倍数, 500) 报价（默认样品约 500/件）；钢、布、食物等常规件不受影响；低价高价值的模组独占物最低 500/件。修女头纱当时仍继承原版 `Clothing` 标签、算常规有供应，维持普通档约 2.2 倍报价——同日下述基类统一后已一并进入 500 底价。

限制：基础市值为 0 的破损定义仍被 `IsOrderable` 排除在目录外；旧存档中已按 20,000 生成的服装现货条目保持原价至每周刷新；`CorporatePricingChecks.RunQuotes` 中机械液等特供断言不受影响，Steel 等"无底价"断言依赖商人供应判定。

## 问题 3：白银空投砸屋顶

`CorporateTradeContext.DeliverSilver` 原用 `DropCellFinder.TradeDropSpot` 且 `canRoofPunch` 走默认 true；山区基地无未加盖信标时落点退化到任意建筑旁，大额白银按堆分组的空投舱半径扩散并打穿屋顶。

修改：落点优先 `RCellFinder.TryFindRandomPawnEntryCell`（地图边缘、未加盖、可通往殖民地，与访客走边一致），`DropThingsNear` 显式 `canRoofPunch: false`；找不到边缘落点时退回 `TradeDropSpot`（仍禁砸屋顶）。订单货物交付（`Deliver`）保持贸易信标语义不变。

## 追加（同日）：头饰基类统一与供应判定加固

复查发现 `Mugirl_HeadBase`（`Apparel_Head.xml`）原挂原版 `HatMakeableBase`，经 `ApparelMakeableBase` 继承了 `Clothing` 交易标签与默认 All 交易方向：修女头纱等头饰可被原版服装商人生成出售，巨企按普通档报价，与身体服装（`Mugirl_ApparelBase` → Sellable + `Mugirl_Apparel`）形成两套定价。经确认 RimWorld XML 继承规则（`<li>` 追加合并、同名节点递归覆盖、`Inherit="False"` 整节点替换）后：

- `Mugirl_HeadBase` 改挂 `Mugirl_ApparelBase`。原由 `HatMakeableBase` 链提供的 `apparel.parentTagDef`（ApparelHead，渲染挂点）与 `Headgear` 物品分类显式保留，渲染与物品树分组不变；`Mugirl_ApparelBase` 的父类本就是原版 `ApparelBase`，品质/样式组件与基础物品属性不损失。头盔（`Mugirl_ArmorHelmetBase`）与战术目镜（`Mugirl_TacticalGoggles`，自设 parentTagDef、Spacer 级）自动继承，PMC 系列本就直接挂原版基类并自带 Sellable，不受影响。
- 效果：头饰全线变为常规途径不可购入，巨企报价走 500 底价，与身体服装一致；原版服装商人不再生成雪牛娘头饰（已生成的商人库存保留至其离开）。
- 顺带加固 `PrepareUnsourcedOrderDefs`：`HandlesThingDef` 不区分买卖方向，移除候选时追加 `tradeability.TraderCanSell()` 条件，防止按类目/标签匹配的出售生成器把 Sellable 物品误判为常规有供应。
- 模组内仍直接挂原版基类、继承 Clothing 标签（常规可购、普通档定价）的散件已于同日第二轮全部统一，见下节。

## 追加（同日第二轮）：全部原版服装基类改挂 Mugirl 基类

用户确认“都改为我们的基类”。先核对原版 `XmlInheritance` 合并规则与 `StatUtility.GetStatValueFromList`（取首条命中）：Def 的 `statBases` 用具名节点（如 `<MaxHitPoints>`），走同名递归覆盖，子类值直接替换父类，不产生重复条目；`<li>` 列表（comps、tradeTags、recipeUsers）才是追加合并。改动清单：

- `Mugirl_Coat`（大衣）、`Mugirl_KnightlyOrnament`（骑士颈饰）、`Mugirl_RA_Carrying_Equipment`（特化携行具）→ `Mugirl_ApparelBase`，三者均以 `Inherit="False"` 覆盖配方研究。
- **`Mugirl_ApparelMakeableNoQualityBase`（机械师背包基类，Lv1–Lv4 四件随之）按用户确认归入无品质级**：改挂 `Mugirl_ApparelNoQualityBase` 并自带缝纫配方副本（与 `Mugirl_ApparelBase` 的配方同内容），品质 Comp 从链路源头即不存在，不再依赖 `comps Inherit="False"` 从带品质父链剥离；同时修正了首轮曾把它挂到 `Mugirl_ApparelBase` 时顺带改变 Flammability/Deterioration/Beauty 与 pathCost 的细微副作用——这些数值回到原版 `ApparelNoQualityBase` 根链的取值，与改挂前一致。Lv1–Lv4 均自设 `Inherit="False"` 研究门槛（BasicMechtech/StandardMechtech/HighMechtech/UltraMechtech）与 costList，不受基类调整影响。
- `Mugirl_ReverseBunnySticker`（逆兔女郎贴纸）、`Mugirl_MilkingDevice` / `Mugirl_MilkingDeviceHidden`（榨乳器）→ `Mugirl_ApparelBase`。榨乳器自有研究列表（`Mugirl_RegulatedMilking` + `Machining`）与两个 Mugirl 基类的配方节点（均无 research 字段）合并不冲突；Spacer 级 → 稀有档 ×6，前后报价不变。
- `PMC_Helmet`、`PMC_CaptainHelmet`、`PMC_CombatUniform` → `Mugirl_ApparelBase`。三者本就显式 `Sellable` + `Mugirl_CombatApparelBuyOnly`，改挂仅去掉已失效的继承 Clothing 标签，行为不变（作战补给商收购走 BuyTradeTag 补丁，不受父类影响）。
- **`Mugirl_AutoloadingSystem`（自驱装弹系统）为特例**：原挂原版 `ApparelNoQualityBase`（刻意无品质、父类无配方），自带完整制造台配方。直接改挂 `Mugirl_ApparelBase` 会凭空继承品质 Comp（`ApparelBase` 注入 `CompQuality`）且其 `recipeUsers`/`unfinishedThingDef` 会与父类缝纫配方发生 li 追加合并。因此新建 `Mugirl_ApparelNoQualityBase`（父类原版 `ApparelNoQualityBase`，仅统一 `Sellable` + `Mugirl_Apparel`，不含配方），装弹系统改挂该基类。

副作用复查结论：C# 仅按 defName/DefOf 引用这些 Def，无 tradeability/Clothing 标签依赖；束具 `SlaveApparelDef` 自定义链未触碰；XML 补丁只针对商人 stockGenerators；PawnKind 装备引用按 defName 不受父类影响；分类（ApparelMisc/ArmorHeadgear/ApparelUtility）均由子类自设或本就来自原版根链，不变。改动后这些物品全部常规途径不可购入：榨乳器、贴纸、大衣、颈饰、背包、装弹系统在巨企目录按各自档位计价（低价者适用 500 底价；Spacer 级榨乳器/装弹系统/背包 Lv2 走稀有档 ×6），PMC 三件因 `Mugirl_CombatApparelBuyOnly` 标签继续排除在巨企目录外。玩家向商人出售（Sellable）不受影响。

## 验证

- `Invoke-Phase6StaticValidation.ps1` 完整构建（Release，288 项编译）与静态检查全部通过，无警告；正式 DLL 已更新。基类统一与 TraderCanSell 加固后再次完整构建，同样全部通过。
- 原版语义核对基于反编译源码：`QuickSearchWidget`、`Widgets.TextField`、`Text.textFieldStyles`、`DropCellFinder`、`RCellFinder.TryFindRandomPawnEntryCell`、`DropPodUtility.DropThingsNear`、`StockGenerator(_Category/_MiscItems/_MarketValue)`、`Caravan_Outlander_BulkGoods`。
- 未做游戏内实测：输入法组合、边缘落点与服装报价需在下一局确认；Player.log 扫描将在实测后执行。
