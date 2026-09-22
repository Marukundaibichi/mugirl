# 雪牛娘科技树重做设计方案

## 文档状态

- 状态：已实装（2026-09-21 首轮；2026-09-22 扩展"全物品入树"），静态自检通过（既有基线失败除外），游戏内验证待做。实施记录见[研究树实施记录](../records/research-tree-2026-09-21.md)。
- 适用版本：RimWorld 1.6。
- 范围：模组自有 `ResearchProjectDef` 的页签、结构与全部内容的科技门控；不改变原版科技与第三方模组科技。
- 已决议：乳品配方全部入树；磁力镣铐补制造配方；不加仪式性封顶项目；商人乳品库存维持现状（理由见"开放问题的决议"）；**全部雪牛娘可制造物品入树**——专属页签解锁面板可见模组全部物品（2026-09-22 拍板）。

## 背景与现状问题

模组自有科技目前只有 3 项，全部挂在原版 Main 页签，且大量种族内容没有任何研究门槛（盘点基准 2026-09-21）：

| 现有科技 | 位置 | 解锁物 |
| --- | --- | --- |
| `Mugirl_PneumaticWeapons` | Main (12.4, 4.2) | 三把骑枪 |
| `Mugirl_Autoloading` | Main (19.0, 2.4) | 自驱装弹系统 + 轮盘第 4–6 槽（C# 检查见 `Comp_WeaponWheel.cs` 对 `Mugirl_DefOf.Mugirl_Autoloading` 的引用） |
| `Mugirl_NeuralCombatSystems` | Main (20.2, 3.2) | AT-23 机炮、神经战斗甲/盔 |

主要问题：

1. **没有专属页签**，三个节点散落在原版树里，玩家难以感知种族科技线的存在。
2. **奶业经济链零门槛**：9 个乳品配方（`Recipes_Mugirl_MilkFood.xml`）全部无 `researchPrerequisite`；奶发电机只挂原版 `Electricity`。种族核心身份没有进入科技系统。
3. **驯化/管制线借用原版门槛**：束缚装备挂 `FlakArmor`，电击项圈挂 `Electricity`，洗脑头盔 `Mugirl_BrainwashHelmet` 完全无门槛，4 把束缚钥匙配方无门槛。
4. **高阶物品无门槛混入**：`Mugirl_BrainwashHelmet`、敌对枪械 `OPC_Defense_Rifle`、`OPC_Tactical_Shotgun`（可仿制、无前置）。实施核实时修正两项误报：`Mugirl_RA_PowerArmor` 与 `Mugirl_ArmorHelmetRecon` 实际经各自基类挂有 `FlakArmor`，并非无门槛（但前者对 Ultra 级偏弱，见配套修补）。
5. 科技树没有表达模组的进度幻想：从牧场奶业起步，经工业化管制，最终复原雪牛娘母星的 Spacer 武备遗产。

## 设计目标与原则

1. **单开一页**：新增 `ResearchTabDef`，全部雪牛娘科技进入专属研究页签。
2. **三支线**对应模组三大支柱：奶业经济（经济）、驯化管制（殖民地管理）、母星武备（战斗）。每支线 3 节点，全树 9 节点。
3. **原版同调（评审拍板）**：物品解锁普遍采用"雪牛娘项目 + 原版科技"双门槛，把一部分门控深度转移到玩家正常进度必经的原版科技上；雪牛娘项目本身因此只保留"种族增量"定价，`baseCost` 全线下调（明细见"原版同调与数值调整"）。
4. **存档兼容**：3 个现有项目 `defName` 不变，轮盘槽位解锁的 C# 引用零改动；`baseCost` 与前置按本方案调整，只影响未完成项目的成本与可研究时机，不回退已完成进度。
5. **巨企系统不挂科技**：巨企为任务/事件驱动（快递日记 → 来访解锁），保持解耦。基因/异能（Biotech 条件目录）同样维持现状。
6. 新项目纯 XML，不进 `Mugirl_DefOf`，不改 C#。

## 页签机制（技术依据）

- 原版 `ResearchTabDef` 字段：`generalTitle`、`generalDescription`、`visibleByDefault`、`minMonolithLevelVisible`。研究窗口按 `ResearchProjectDef.tab` 分页，未指定时默认 Main（`ResearchProjectDef.cs:388`）。
- `visibleByDefault=true` 使页签始终可见（`ResearchManager.TabInfoVisible`），无需玩家先发现任何节点；`minMonolithLevelVisible` 保持默认 -1，与 Anomaly 巨石无关。
- 自有页签拥有独立坐标系，`researchViewX/Y` 从 1.0 起重新排布，不会与原版节点冲突。
- 跨页签前置（如 `Electricity`）不画连线，但节点信息面板会以红/绿列出前置名称并统计缺失项，为常见模组科技树做法，不阻塞。
- **双门槛字段依据**：`Verse/RecipeDef.cs:133` 定义单数 `researchPrerequisite`，`:137` 定义复数 `researchPrerequisites`（列表），两者都参与锁定判定（任一未完成即不可用），且可同时使用；建筑等 `ThingDef` 用复数 `researchPrerequisites`。因此配方和建筑都能直接表达"雪牛娘项目 + 原版科技"双门槛。

### 页签定义

```xml
<ResearchTabDef>
    <defName>Mugirl_ResearchTab</defName>
    <label>Mugirl</label>
    <generalTitle>Mugirl technology</generalTitle>
    <generalDescription>Research into Mugirl dairy industry, husbandry and recovered homeworld weaponry.</generalDescription>
    <visibleByDefault>true</visibleByDefault>
</ResearchTabDef>
```

中文（DefInjected）：页签按钮「雪牛娘」，总标题「雪牛娘科技」。

## 树形结构总览

布局采用**紧凑阶段网格**（2026-09-22 二次重排，替代横向跨度过大的"双翼对角瀑布"）：四条主题链从左到右推进，同一阶段落在同一列；奶业入口在顶部作短分叉，服饰与武备两条高阶线在右侧汇入神经战斗系统。

```
民生
Dairy ─┬─ MilkPow
       └─ AdvDairy
Rope ─── RegMilk ─── Behav

装备
Daily ── Combat ──── AdvGear ─┐
                              ├─ Neural
Firearm ─ Pneum ───── Auto ───┘
```

布局规则（调整坐标时遵守）：

- 水平方向表达进度深度：入口列 X=0.80、中段列 X=2.35、高阶列 X=3.90、封顶节点 X=5.45；固定步长 1.55，避免长线和无效空白。
- 四条主题行中心分别位于 Y≈0.90、2.15、3.40、4.65，相邻主题行间距 1.25；奶业两个子节点在 Y=0.55/1.25 对称分叉。
- 同列任意节点的 ΔY 至少 0.70，同链相邻节点的 ΔX=1.55；按研究节点约 0.95×0.55 估算，节点不会重叠且保留可辨识空隙。
- `Mugirl_NeuralCombatSystems` 位于服饰/武备两行之间的 Y=4.00，同时承接 `Mugirl_AdvancedGear` 与 `Mugirl_Autoloading`，用两条短斜线形成明确收束。
- 研究视图不按 `techLevel` 绘制时代横带（已核实 1.6 源码），布局不受时代带约束；Spacer 项目在信息面板显示"高于派系科技等级研究变慢"为原版固有行为。

结构语义：

- **奶业线与驯化线的项目前置只走种族链**；原版要求一律下沉到物品层双门槛（见明细表），研究可以提前开始，物品按原版进度逐件解锁。
- **服饰线**承接全部服装/护甲（服装工艺 → 战斗装具 → 高阶装备）；**武备线**为枪械 → 气动骑枪 → 自驱装填。`Mugirl_Autoloading` 增加 `Mugirl_PneumaticWeapons` 前置，使图上连线与实际进度一致。
- `Mugirl_NeuralCombatSystems` 除原版前置和 `Mugirl_Autoloading` 外，增加 `Mugirl_AdvancedGear` 前置；封顶项目因此明确汇合雪牛娘高阶护甲与自动武器两套技术。全树仍为 13 节点。

### 全物品入树的可见性机制（2026-09-22）

- 选中研究项目时的"解锁"面板由 `ResearchProjectDef.UnlockedDefs` 驱动（`Verse/ResearchProjectDef.cs:307`）：配方只要在 `researchPrerequisites` 列表中包含该项目，其产物即出现在该项目的解锁列表；建筑等 `ThingDef` 同理。带多个前置的物品按 `ResearchPrerequisitesUtility.UnlockedDefsGroupedByPrerequisites` 分组，显示"另需 X"表头（缺失红字）。
- 因此"雪牛娘项目 + 原版科技"双门槛的物品**同时出现在两侧页签的解锁面板**：专属页签可见全部物品，原版科技页也保留提示。
- **XML 继承要点**：基类链上同名列表容器的 `li` 是追加合并（`XmlInheritance.RecursiveNodeCopyOverwriteElements`），且祖先的单数字段不会被子级复数字段清除。因此子级/例外件写入的 `researchPrerequisites` 一律带 `Inherit="False"`，确保完全替换祖先门槛，不产生意外叠加。Royalty 的 `Gunlink` 前置保留 `MayRequire` 属性（写在 `li` 上）。

## 项目明细

### 新增项目（10 个）

| defName | 中文标签 | techLevel | baseCost | 前置 | 解锁内容（物品层另有原版双门槛） | 坐标 (X, Y) |
| --- | --- | --- | --- | --- | --- | --- |
| `Mugirl_DairyProcessing` | 雪牛娘乳品加工 | Industrial | 400 | 无 | 基础乳品 4 配方：布丁、奶酪、奶糖、奶油苹果 | (0.8, 0.9) |
| `Mugirl_MilkPowerUtilization` | 乳能利用 | Industrial | 600 | 乳品加工 | 奶发电机（另需 `Electricity`） | (2.35, 0.55) |
| `Mugirl_AdvancedDairy` | 精制乳品 | Industrial | 1000 | 乳品加工 | 精制乳品 5 配方（另需 `AirConditioning`；奶糖另需 `DrugProduction`） | (2.35, 1.25) |
| `Mugirl_RopeHandling` | 束具与拴系 | Industrial | 300 | 无 | 墙挂拴环（另需 `ComplexFurniture`）、中世纪/工业钥匙、基础束缚件（另需 `ComplexClothing`） | (0.8, 2.15) |
| `Mugirl_RegulatedMilking` | 规范采乳 | Industrial | 700 | 束具与拴系 | 穿戴式/隐藏式采奶器（另需 `Machining`） | (2.35, 2.15) |
| `Mugirl_BehavioralConditioning` | 高阶管制技术 | Industrial | 1200 | 规范采乳 | 电击项圈（另需 `Electricity`）、洗脑头盔（另需 `MicroelectronicsBasics`）、Spacer/进阶钥匙、磁力镣铐（另需 `Fabrication`） | (3.9, 2.15) |
| `Mugirl_DailyAttire` | 雪牛娘服装工艺 | Industrial | 400 | 无 | 便服、礼服、职业制服、丝袜、内衣与装饰件（另需 `ComplexClothing`） | (0.8, 3.4) |
| `Mugirl_CombatAttire` | 雪牛娘战斗装具 | Industrial | 1000 | 服装工艺 | 战斗服、甲胄内衬、携行具、防弹/PMC/侦察/骑士板甲（另需 `FlakArmor`/`PlateArmor`/`ComplexClothing`） | (2.35, 3.4) |
| `Mugirl_AdvancedGear` | 雪牛娘高阶装备 | Spacer | 1800 | 战斗装具 | 动力甲、太空服、机械师背包、战术目镜（另需 `PoweredArmor`/`OrbitalTech`/机械科技/`Gunlink`） | (3.9, 3.4) |
| `Mugirl_Firearms` | 雪牛娘枪械 | Industrial | 800 | 无 | 雪牛娘式枪 3 把 + PMC/OPC 枪 8 把（另需 `BlowbackOperation`/`GasOperation`/`PrecisionRifling`） | (0.8, 4.65) |

### 保留项目（3 个，defName 不变，成本下调）

| defName | 调整内容 |
| --- | --- |
| `Mugirl_PneumaticWeapons` | `baseCost` 1600 → 1200；前置增加 `Mugirl_Firearms`；坐标 (2.35, 4.65) |
| `Mugirl_Autoloading` | `baseCost` 2500 → 2000；前置增加 `Mugirl_PneumaticWeapons`；坐标 (3.9, 4.65) |
| `Mugirl_NeuralCombatSystems` | `baseCost` 5000 → 4000；前置增加 `Mugirl_AdvancedGear`；坐标 (5.45, 4.0) |

## 原版同调与数值调整

降价理由（按 README 数值变更记录要求）：原版前置与物品层双门槛已吸收通用工业/Spacer 门控深度，玩家在正常进度中必然研究这些原版科技；雪牛娘项目重复计价这部分深度，故只保留种族增量成本，总价与同深度原版项目对齐。

| 项目 | 原成本 | 新成本 | 吸收其深度的原版科技 |
| --- | --- | --- | --- |
| `Mugirl_DairyProcessing` | （新增） | 400 | 无；开局入口，对标原版早期工业项目 |
| `Mugirl_MilkPowerUtilization` | （新增） | 600 | 物品层 `Electricity`（原设计为项目前置） |
| `Mugirl_AdvancedDairy` | （新增） | 1000 | 物品层 `AirConditioning`、`DrugProduction` |
| `Mugirl_RopeHandling` | （新增） | 300 | 物品层 `ComplexFurniture` |
| `Mugirl_RegulatedMilking` | （新增） | 700 | 物品层 `Machining` |
| `Mugirl_BehavioralConditioning` | （新增） | 1200 | 物品层 `Electricity`、`MicroelectronicsBasics`、`Fabrication` |
| `Mugirl_PneumaticWeapons` | 1600 | 1200 | 项目前置 `LongBlades`+`BiofuelRefining`（不变） |
| `Mugirl_Autoloading` | 2500 | 2000 | 原版前置 `Fabrication`+`PrecisionRifling`；种族前置 `Mugirl_PneumaticWeapons`（2026-09-22 布局二次重排增加） |
| `Mugirl_NeuralCombatSystems` | 5000 | 4000 | 原版前置 `PoweredArmor`+`Fabrication`+`PrecisionRifling`；种族前置 `Mugirl_Autoloading`+`Mugirl_AdvancedGear`；多重分析仪隐含 `MicroelectronicsBasics` |
| `Mugirl_DailyAttire` | （新增） | 400 | 物品层 `ComplexClothing`（2026-09-22） |
| `Mugirl_CombatAttire` | （新增） | 1000 | 物品层 `FlakArmor`/`PlateArmor`/`ComplexClothing`（2026-09-22） |
| `Mugirl_AdvancedGear` | （新增） | 1800 | 物品层 `PoweredArmor`/`OrbitalTech`/机械科技/`Gunlink`（2026-09-22） |
| `Mugirl_Firearms` | （新增） | 800 | 物品层 `BlowbackOperation`/`GasOperation`/`PrecisionRifling`（2026-09-22） |

## 现有内容门控调整清单

### 移入新项目（双门槛 = 雪牛娘项目 + 原版科技）

| Def | 位置 | 当前门槛 | 调整后 `researchPrerequisites` |
| --- | --- | --- | --- |
| `Mugirl_MilkPoweredGenerator` | `Building_MilkPower.xml` | `Electricity` | [`Mugirl_MilkPowerUtilization`, `Electricity`] |
| `Mugirl_MakeMilkPudding` / `Mugirl_MakeCheese` / `Mugirl_MakeMilkCandy` / `Mugirl_MakeMilkCreamApple` | `Recipes_Mugirl_MilkFood.xml` | 无 | [`Mugirl_DairyProcessing`] |
| `Mugirl_MakeAgedCheese` / `Mugirl_MakeCowCake` / `Mugirl_MakeMilkPowder` / `Mugirl_MakeMilkTablet` | 同上 | 无 | [`Mugirl_AdvancedDairy`, `AirConditioning`] |
| `Mugirl_MakeBaotaSugar` | 同上 | 无 | [`Mugirl_AdvancedDairy`, `DrugProduction`]（制药台门槛显式化） |
| `WallRopeHitch` | `Building_Rope.xml` | `ComplexFurniture` | [`Mugirl_RopeHandling`, `ComplexFurniture`] |
| `Mugirl_MakeKey_Medieval` / `Mugirl_MakeKey_Industrial` | `Recipes_Mugirl.xml` | 无 | [`Mugirl_RopeHandling`] |
| `Mugirl_MakeKey_Spacer` / `Mugirl_MakeKey_Advance` | 同上 | 无 | [`Mugirl_BehavioralConditioning`] |
| `Mugirl_MilkingDevice` / `Mugirl_MilkingDeviceHidden` | `BondageApparel_Spacer.xml` | `ComplexClothing` | [`Mugirl_RegulatedMilking`, `Machining`] |
| `Mugirl_ShockCollar` | 同上 | `Electricity` | [`Mugirl_BehavioralConditioning`, `Electricity`] |
| `Mugirl_BrainwashHelmet` | 同上 | **无** | [`Mugirl_BehavioralConditioning`, `MicroelectronicsBasics`] |

### 新增配方：磁力镣铐（评审拍板）

`Mugirl_MagneticShackles`（`BondageApparel_Spacer.xml`）目前无配方不可制造。为其 `recipeMaker` 补：

- `recipeUsers`：`FabricationBench`（材料含 `ComponentSpacer`，与Fabrication 台定位一致）；
- `researchPrerequisites`：[`Mugirl_BehavioralConditioning`, `Fabrication`]；
- `skillRequirements`：Crafting 8；`unfinishedThingDef`：`UnfinishedTechArmor`；
- `costList` 沿用现有（`ComponentSpacer` 6 / `Plasteel` 150 / `Uranium` 50）；
- `WorkToMake` 现值 180 为占位值，建议上调至 12000（同类束具手工量级），实施时验证手感。

### 服饰与枪械入树（2026-09-22，全部改写为带 `Inherit="False"` 的双门槛列表）

按基类批量生效，例外件单独覆盖：

| 范围 | 位置 | 调整后 `researchPrerequisites` |
| --- | --- | --- |
| `Mugirl_0920CasualBase`（休闲 0920 系列 + 警察/修女系列） | `Apparel_0920.xml` | [`Mugirl_DailyAttire`, `ComplexClothing`] |
| 常规服装基类：`Mugirl_HeadBase`/`OnSkinBase`/`MiddleBase`/`Stocking`/`UnderwearBase`/`ChildBase` | 各 `Apparel_*.xml` | [`Mugirl_DailyAttire`, `ComplexClothing`]；`WhitePanties`、`ReverseBunnySuit` 等原先无门槛件经基类继承获得同样门槛 |
| 战斗用途的服装例外：`Mugirl_Gambeson`、`Mugirl_RA_Unionary`、`Mugirl_RA_Carrying_Equipment` | `Apparel_Middle.xml`、`Apparel_OnSkin.xml`、`Apparel_Shell.xml` | [`Mugirl_CombatAttire`, `ComplexClothing`]；从服装工艺线移入战斗装具线 |
| `Mugirl_ArmorHelmetBase`、`Mugirl_ArmorBase`（侦察甲、防弹盔等） | `Apparel_Head.xml`、`Apparel_Shell.xml` | [`Mugirl_CombatAttire`, `FlakArmor`] |
| 战斗/军服/PMC 例外件（Combatant 全套、MilitaryDress/Uniform、PMC 三件、WarriorCombat 两件） | `Apparel_Mugirl_Combatant.xml`、`Apparel_PMC.xml`、`Apparel_SpecializedGear.xml` | [`Mugirl_CombatAttire`, `FlakArmor`] 或 [`Mugirl_CombatAttire`, `ComplexClothing`] |
| 板甲件（KnightMount 三件、PlateArmor、KnightHelmet） | `Apparel_0920.xml`、`Apparel_Shell.xml`、`Apparel_SpecializedGear.xml` | [`Mugirl_CombatAttire`, `PlateArmor`] |
| 特化件：PowerArmor 两件、RA_PowerArmor | `Apparel_SpecializedGear.xml`、`Apparel_Shell.xml` | [`Mugirl_AdvancedGear`, `PoweredArmor`] |
| 太空服两件（Odyssey 条件） | `Apparel_SpecializedGear.xml` | [`Mugirl_AdvancedGear`, `OrbitalTech`] |
| 机械师背包 Lv1–4（Biotech 条件） | `Apparel_Shell.xml` | [`Mugirl_AdvancedGear`, 对应等级 Mechtech] |
| 战术目镜 `Mugirl_RA_MechanicRecon` | `Apparel_Head.xml` | [`Mugirl_AdvancedGear`, `Gunlink`（li 带 Royalty `MayRequire`）] |
| 基础束缚件三基类（OnSkin/Shell/Head 束具） | `Apparel_Bondage/*.xml` | [`Mugirl_RopeHandling`, `ComplexClothing`]；从防弹衣门槛改为与裁制服装一致的复杂服装门槛 |
| 雪牛娘式枪 3 把（支援步枪/机枪手枪/防卫步枪） | `RangedWeapons.xml` | [`Mugirl_Firearms`, 对应 `PrecisionRifling`/`BlowbackOperation`] |
| PMC/OPC 枪 8 把 | `RangedWeaponsEnemy.xml` | [`Mugirl_Firearms`, 对应 `GasOperation`/`PrecisionRifling`/`BlowbackOperation`] |
| 大衣/骑士饰物、兔女郎贴纸 | `Apparel_Shell.xml`、`Apparel_SpecializedGear.xml` | [`Mugirl_DailyAttire`, `ComplexClothing`] |

保留单数种族门槛（无需原版同调）：骑枪 ×3（`Mugirl_PneumaticWeapons`）、AT-23/神经甲/神经盔（`Mugirl_NeuralCombatSystems`）、自驱装弹系统（`Mugirl_Autoloading`）。

### 配套修补（挂原版既有科技，不新增节点）

| Def | 问题 | 调整 |
| --- | --- | --- |
| `Mugirl_RA_PowerArmor` | 实施核实：经基类 `Mugirl_ArmorBase` 已挂 `FlakArmor`，对 Ultra 级护甲偏弱 | 子级 recipeMaker 覆盖为 `PoweredArmor`（与其余动力甲一致） |
| `OPC_Defense_Rifle` | 敌对枪可无前置仿制 | 补 `researchPrerequisite` = `BlowbackOperation` |
| `OPC_Tactical_Shotgun` | 同上 | 补 `GasOperation`（与 PMC 枪械一致） |

实施时核销一项：`Mugirl_ArmorHelmetRecon` 经基类 `Mugirl_ArmorHelmetBase` 已挂 `FlakArmor`，与设计预期一致，无需修改。

### 科技语义复核（2026-09-22）

- `Mugirl_RopeHandling` 的显示名改为“束具与拴系”，与其 11 件基础束具、两档钥匙和墙挂拴环的完整解锁面一致；基础束具的原版副门槛由 `FlakArmor` 改为 `ComplexClothing`。
- 电击项圈从“规范采乳”移入“高阶管制技术”；前者只负责两种采奶器，后者统一承接电子束具、洗脑头盔、高科技钥匙、破解器和磁力镣铐。
- `Mugirl_DailyAttire` 显示名改为“雪牛娘服装工艺”，覆盖广泛服装品类；明确用于战斗的甲胄内衬、革命军作战服和特化携行具改挂“雪牛娘战斗装具”。
- 神经战斗甲和神经放大头盔清除护甲基类继承的 `Mugirl_CombatAttire`/`FlakArmor` 列表，仅保留 `Mugirl_NeuralCombatSystems` 单数门槛，避免同时出现在普通战斗装具与神经战斗系统两个项目的解锁列表中。神经战斗系统项目本身已汇合 `PoweredArmor`、高阶装备与自驱装填，不降低实际科技深度。
- `Mugirl_PneumaticWeapons` 显示名收窄为“雪牛娘气动骑枪”，准确对应其三把骑枪；`Mugirl_AdvancedGear` 中文显示名改为“雪牛娘高阶装备”，对应其动力甲、太空服、机械师背包和战术目镜的混合内容。

### 明确不动

- `Mugirl_SmeltBondageApparel` 回收配方：保持无门槛，回收行为不设卡。
- 商人乳品库存（`Patches/MilkFood_TraderStock_Patch.xml`）：维持现状（决议理由见下节）。
- 巨企系统、Biotech 基因/异能、事件链：不挂科技。
- `Mugirl_Gun_Flame_Thrower`：维持不可制造。

## 开放问题的决议（2026-09-21 评审；2026-09-22 增补第 5 条）

1. **基础乳品是否保留零门槛** → 否。9 个乳品配方全部入树：基础 4 种挂 `Mugirl_DairyProcessing`（成本仅 400），精制 5 种挂 `Mugirl_AdvancedDairy` 并叠加原版双门槛。
2. **商人乳品库存** → 维持现状。该补丁向 18 类商人/商队（外侨商队、访客、海盗商、轨道商船、帝国、部落商队等）追加 `Mugirl_MilkFood` 交易标签库存，使 NPC 出售雪牛娘乳制品。原版 `StockGenerator` 没有"按研究进度开关库存"的机制，强行耦合需 C# 介入；购买只能获得成品，自给自足仍需研究，作为软绕过保留。评审确认：与原版行为一致（原版商人在玩家未研究 `BeerBrewing` 等科技时同样出售成品酒），可接受。
3. **磁力镣铐制造配方** → 增加，见上节。
4. **仪式性封顶项目** → 不加，避免无解锁物的空节点。
5. **全物品入树**（2026-09-22）→ 所有雪牛娘可制造物品（服装、护甲、束缚件、枪械、建筑、配方）至少挂一个 `Mugirl_*` 研究，专属页签解锁面板可见全部物品。不可制造物（`Mugirl_Gun_Flame_Thrower`、`Mugirl_CourierDiary`、`Mugirl_LanceChargeFlyer`、奶/毛资源）与研究无关，不入树；`Mugirl_SmeltBondageApparel` 回收配方保持无门槛。

## 存档与兼容

- 已完成研究按 `defName` 存档：3 个旧项目原名保留，旧存档进度与轮盘 4–6 槽解锁状态不受影响；`Comp_WeaponWheel` 对 `Mugirl_DefOf.Mugirl_Autoloading` 的引用无需改动。
- **数值与前置调整的影响**：`baseCost` 下调只让未完成项目更便宜；未完成的 `Mugirl_Autoloading` 现在要求先完成气动骑枪，未完成的 `Mugirl_NeuralCombatSystems` 现在还要求高阶装备。已完成项目不回退。
- **预期行为变化**：未完成对应新项目的旧存档，乳品 9 配方、4 把钥匙、洗脑头盔等会从可用列表消失（配方研究前置是即时判定的）。已建成的建筑与已穿装备不回溯失效。发布说明须写明。
- 敌对派系 `Mugirl_GiantCorporations_Hostile`（Industrial）的袭击装备生成不依赖玩家配方，无影响。
- 页签对所有殖民地可见（与现有 3 项目一致）；非雪牛娘殖民地研究后多数解锁物受 `MugirlRaceRestrictedExtension` 种族限制，无平衡问题。
- 不新增 XPath patch，全部为 Def 直改，无 `PatchGovernance` 义务。

## 本地化

- Def 内英文 `label`/`description` 作为缺省文本（与现状一致）。
- `Languages/ChineseSimplified/DefInjected/ResearchProjectDef/` 新增 6 个项目的 label/description 条目；新增 `ResearchTabDef` 目录文件（页签标题与描述）。
- 中英 key 与占位符保持一致；研究描述须写明解锁内容与原版同调要求（沿用 `Mugirl_Autoloading` 描述中列明"解锁轮盘剩余三槽"的先例）。

## 实施步骤

1. 新增 `1.6/Defs/ResearchProjectDefs/ResearchTabDef_Mugirl.xml`（页签定义）。
2. 新增 `1.6/Defs/ResearchProjectDefs/ResearchProjects_Mugirl.xml`（6 个新项目，含 `<tab>` 与坐标）。既有 `ResearchProjects_0920.xml`、`ResearchProjects_WeaponWheel.xml` 只为 3 个旧项目补 `<tab>`、改坐标、下调 `baseCost`，不搬文件。
3. 按"门控调整清单"修改：`Building_MilkPower.xml`、`Building_Rope.xml`、`Recipes_Mugirl_MilkFood.xml`、`Recipes_Mugirl.xml`、`BondageApparel_Spacer.xml`（含磁力镣铐新配方）、`Apparel_SpecializedGear.xml`、`RangedWeaponsEnemy.xml`。
4. 补中文翻译（ResearchProjectDef 新条目 + ResearchTabDef 目录）。
5. 同步过时文档：[武器轮盘设计](weapon-wheel-design.md) 中"第 4–6 格仅开发者模式解锁"一节与自驱装填研究解锁的现状不符，实施时一并修正并注明日期。
6. 验证（见下），再在 `docs/records/` 记录实施结果。

## 验证清单

- 静态：`.\docs\tools\Invoke-Phase6StaticValidation.ps1 -SkipBuild`（纯 XML 改动；已知的 `Mugirl_Shapes.xml` 行尾空格与 42 张 FA 贴图缺失为既有基线失败，不因本任务恶化）。注意该脚本为无 BOM UTF-8，需用 `pwsh`（PowerShell 7）运行，Windows PowerShell 5.1 会按 GBK 误读导致解析失败。
- 游戏内（fresh 存档，按[验证手册](../validation-runbook.md)查 fresh `Player.log`）：
  1. 研究页签出现「雪牛娘」，无 XML/翻译报错；13 节点紧凑四行布局正确，奶业分叉与 Neural 双线汇合无穿线，跨页前置在信息面板正常显示。
  2. 双门槛验证：只完成雪牛娘项目而未研究对应原版科技时，配方/建筑不可用且显示缺失前置；补齐原版科技后立即可用。
  3. 逐项完成 10 个新项目，验证对应配方/建筑/装备逐级解锁；磁力镣铐可在 FabricationBench 制作且工作量合理。
  4. 解锁面板覆盖抽查：DailyAttire/CombatAttire/AdvancedGear/Firearms 四节点的解锁列表应列出全部对应服装/护甲/枪械（含经基类继承门槛的件），带"另需 X"分组。
  5. 完成旧存档（已研究 `Mugirl_PneumaticWeapons`）加载：研究保留、骑枪可造、轮盘已解锁槽位不回退。
  6. 未完成新项目的旧存档：乳品/钥匙/服装/枪械配方按预期消失（此前仅挂原版科技的服装与枪械也会暂时锁定），已建成建筑与已穿装备不受影响，发布说明措辞核对。
  7. 敌对 PMC/OPC 袭击装备不受影响。
