# 战斗服饰收购与售价调整（2026-09-24）

## 范围与原因

截图中作战补给商拒收的 13 件服饰，以及同系列的 `PMC_CaptainHelmet`，改为地面和轨道作战补给商均可收购。所选目标为：

- `Mugirl_KnightMountPlateWhite`、`Mugirl_Combatant_BulletproofVest`、`Mugirl_ReconArmor`、`Mugirl_WarriorCombatArmor`、`PMC_CombatUniform`、`Mugirl_NeuralCombatArmor`、`PMC_Helmet`；
- `Mugirl_RA_Carrying_Equipment`、`Mugirl_MilitaryDress`、`Mugirl_Combatant_Underwear`、`Mugirl_SpeciallyStockings`、`Mugirl_Gambeson`、`Mugirl_Combatant_Clothes`、`PMC_CaptainHelmet`。

这些服饰原本没有与作战补给商收购生成器匹配的交易标签。新增专用 `Mugirl_CombatApparelBuyOnly` 标签，并只向 `Caravan_Outlander_CombatSupplier`、`Orbital_CombatSupplier` 追加 `StockGenerator_BuyTradeTag`。该生成器只处理玩家售出，不生成商人库存。对原本允许双向交易的 4 件目标，明确设为 `Sellable`，防止按物品类别生成商人库存。14 件目标均保留从父类继承的旧交易标签，以维持原有商人的收购行为。

巨企订购与每周现货使用 `CorporateNetwork.IsOrderable` 筛选。专用标签也从这些供货目录中排除；已付款的旧订单仍按保存的订单交付。已生成的商人库存不会被追溯删除，需等待商人离开或库存刷新。第三方模组若直接指定某件物品作为商人库存，仍可绕过普通交易方向规则。PawnKind 的直接装备引用、制作研究门槛与服装属性不变。

另外，`Apparel_Mugirl_Combatant.xml` 的 7 件和 `Apparel_PMC.xml` 的 3 件原先逐件显式设置 `SellPriceFactor=0.2`，导致玩家卖价只有默认乘数的五分之一。这 10 行已删除，回到原版默认乘数 1；市场价值、商人购入报价及其他交易因子不变。

## 验证

XML 结构检查确认 14 件目标拥有专用收购标签，10 处 `SellPriceFactor=0.2` 均已移除，原本允许双向交易的 4 件目标均设为 `Sellable`。Release 构建通过并更新正式 DLL；构建前的原 DLL 已备份到 `DevData/Backups/CombatApparelTrade-20260924-prebuild/MugirlRace.dll`。Phase 6 静态主体验证通过，资源检查因既有的 42 张 FA 烘焙脸红贴图缺失退出 1，与本次交易改动无关。

隔离游戏启动加载了 Harmony、HAR、雪牛娘及原版 DLC；`Invoke-PlayerLogScan.ps1` 对此次独立日志未发现可疑行。未在实际交易界面逐件试卖或抽样检查商人生成库存。隔离环境目录在 `%LOCALAPPDATA%\RimWorldModTests\`，RimSort 目录环路审计为 `affected_candidates=0`、`scan_errors=0`。
