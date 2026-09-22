# 研究树重做实施记录（2026-09-21）

## 版本与范围

- 基准：工作区 commit `de6753d`（0.6.5）之上，含用户未提交改动（DLL、`Apparel_0920.xml`、FA 贴图等，均未触碰）。
- 方案：[研究树设计](../features/research-tree-design.md)（含当日评审决议）。本次为纯 XML/文档改动，未动 C#，未重新构建 DLL。
- 新增研究 6 项 + 专属页签 `Mugirl_ResearchTab`；3 个旧研究迁入页签并降价；门控调整覆盖奶业、驯化两线与配套修补；磁力镣铐补制造配方。

## 改动清单

新增文件：

- `1.6/Defs/ResearchProjectDefs/ResearchTabDef_Mugirl.xml`：`ResearchTabDef`（`visibleByDefault=true`）。
- `1.6/Defs/ResearchProjectDefs/ResearchProjects_Mugirl.xml`：乳品加工 400 / 乳能利用 600 / 精制乳品 1000 / 挽具与拴系 300 / 规范采乳 700 / 行为驯化 1200，均 Industrial、带 tab 与坐标。
- `1.6/Languages/ChineseSimplified/DefInjected/ResearchProjectDef/ResearchProjects_Mugirl.xml`、`.../ResearchTabDef/ResearchTabDef_Mugirl.xml`。

修改文件：

- `ResearchProjects_0920.xml`、`ResearchProjects_WeaponWheel.xml`：3 个旧项目补 `<tab>`、新坐标（1.0/3.0/4.8, 3.4），`baseCost` 1600→1200、2500→2000、5000→4000（降价理由与影响见设计文档"原版同调与数值调整"）。
- `Building_MilkPower.xml`：奶发电机前置 → [乳能利用, Electricity]。
- `Building_Rope.xml`：墙挂拴环 → [挽具与拴系, ComplexFurniture]。
- `Recipes_Mugirl_MilkFood.xml`：9 个乳品配方全部补 `researchPrerequisites`（基础 4 种→乳品加工；精制 4 种→[精制乳品, AirConditioning]；奶宝塔糖→[精制乳品, DrugProduction]）。
- `Recipes_Mugirl.xml`：中世纪/工业钥匙→挽具与拴系；Spacer 钥匙/束具破解器→行为驯化。
- `BondageApparel_Spacer.xml`：采奶器 ×2 → [规范采乳, Machining]；电击项圈 → [规范采乳, Electricity]；洗脑头盔补 [行为驯化, MicroelectronicsBasics]；磁力镣铐新增 recipeMaker（FabricationBench、[行为驯化, Fabrication]、锻造 8、`UnfinishedTechArmor`）并将 `WorkToMake` 180→12000。
- `Apparel_Shell.xml`：`Mugirl_RA_PowerArmor` 子级 recipeMaker 覆盖基类 FlakArmor 为 PoweredArmor。
- `RangedWeaponsEnemy.xml`：`OPC_Defense_Rifle` 补 BlowbackOperation；`OPC_Tactical_Shotgun` 补 GasOperation。
- `docs/features/weapon-wheel-design.md`：修正"第 4–6 格仅开发者模式解锁"过时描述为研究解锁现状，研究成本行同步 2000。
- `docs/features/research-tree-design.md`：状态改"已实装"；核销 `Mugirl_ArmorHelmetRecon`（基类已挂 FlakArmor，无需修改）并修正 RA_PowerArmor 现状描述。

## 实施中发现并修正的问题

1. **`Refrigeration` 引用不存在**：原版 1.6 无此研究 defName（正确为 `AirConditioning`），设计稿沿用了错误名。已在 4 个精制乳品配方与设计文档中改为 `AirConditioning`，交叉引用检查确认 0 坏链。该错误若未发现会在加载时产生 cross-reference 报错。
2. **盘点误报核销**：`Mugirl_RA_PowerArmor`、`Mugirl_ArmorHelmetRecon` 并非无门槛，实为经 `Mugirl_ArmorBase`/`Mugirl_ArmorHelmetBase` 挂 FlakArmor；前者按方案升级为 PoweredArmor，后者维持现状。

## 验证结果

- `Invoke-Phase6StaticValidation.ps1 -SkipBuild`（pwsh 运行）：仍被既有基线失败拦在第一步 `git diff --check`（`Mugirl_Shapes.xml` 两处行尾空格，用户未提交改动，README 已记录；另有 42 张 FA 烘焙贴图缺失基线未触达）。本次改动未新增行尾空格。
- 因脚本早退，另以等效自检覆盖其关键步骤：13 个触碰 XML 全部解析通过；全模组 Def 中所有 `researchPrerequisite(s)`/`prerequisites`/`tab` 引用对原版+DLC+模组 defName 索引 0 坏链；新增翻译键无重复且均能对上 Def。
- C# 未改动：轮盘 4–6 槽解锁仍由 `Mugirl_DefOf.Mugirl_Autoloading` 驱动，`baseCost` 下调不影响已完成研究。

## 未验证与限制

- 游戏内验证未做（页签渲染、双门槛红字、配方逐级解锁、旧档兼容、敌对装备不受影响）。按验证手册 fresh `Player.log` 检查后，在此处补记结果。
- 磁力镣铐 `WorkToMake` 12000 为设计估值，未做游戏内手感校验。

## 存档兼容说明（发布说明素材）

- 旧存档已完成的 3 项研究保留；未完成的按新成本继续。
- 未研究对应新项目的旧存档：9 个乳品配方、4 把钥匙配方、洗脑头盔与磁力镣铐配方会不可用（即时判定）；已建成建筑与已穿装备不回溯失效。

## 扩展：全物品入树（2026-09-22）

用户拍板：所有雪牛娘可制造物品进入专属页签解锁面板。实施内容：

- 新增 4 个研究项目：`Mugirl_DailyAttire`（400）、`Mugirl_CombatAttire`（1000）、`Mugirl_AdvancedGear`（1800，Spacer）、`Mugirl_Firearms`（800）；`ResearchProjects_Mugirl.xml` 追加，翻译同步。
- 武备线整体移至 Y=4.6 并前置枪械节点：`Mugirl_PneumaticWeapons` 前置增加 `Mugirl_Firearms`，坐标 (2.4, 4.6)；`Mugirl_Autoloading` (3.8, 4.6)；`Mugirl_NeuralCombatSystems` (5.2, 4.6)。全树 13 节点。
- 门控全面改为双门槛列表（雪牛娘项目 + 原版科技），共触及 18 个 Def 文件：6 个常规服装基类、`Mugirl_0920CasualBase`、`Mugirl_ArmorBase`/`Mugirl_ArmorHelmetBase`、Combatant 7 件、PMC 3 件、SpecializedGear 8 件、Shell 例外 8 件、Head 例外 2 件（`Gunlink` 前置的 `MayRequire` 保留在 `li` 上）、束缚三基类、枪械 11 把。
- **关键技术点**：XML 继承对列表容器按 `li` 追加合并，且祖先单数字段不被子级复数字段清除；因此所有子级写入的 `researchPrerequisites` 一律 `Inherit="False"`（依据 `XmlInheritance.RecursiveNodeCopyOverwriteElements`）。解锁面板可见性依据 `Verse/ResearchProjectDef.cs:307`：列表包含该项目的配方产物与建筑均计入该项目的解锁列表。
- 验证：XML 解析与交叉引用 0 错误；继承模拟（复刻追加/覆盖/Inherit=False 语义）确认 127 个可制造 Def 全部解析出至少一个 `Mugirl_*` 门槛、无祖先门槛泄漏；剩余单数门槛仅骑枪×3、神经系×3、装填×1（有意保留）。本次改动无新增行尾空格（`git diff --check` 对改动目录通过）。
- 行为变化补充：此前仅挂原版科技（ComplexClothing/FlakArmor 等）的服装与枪械，现在还需对应雪牛娘研究；旧存档在补研究前会暂时失去这些配方，已建成/已穿戴不受影响。
- 不可制造物（火焰喷射器、信使日记、冲锋飞行器、奶/毛资源）与研究无关，不入树；回收配方保持无门槛。

## 布局重排：双翼对角瀑布（2026-09-22）

用户反馈四条等距横排不美观，全树 13 节点重新布点：

- 左翼民生（奶业 V 形分叉 + 驯化斜链），右翼军备（服饰斜链 + 武备斜链），每链 ΔX≈1.5、ΔY≈1.0–1.2 向右下级联，NeuralCombatSystems 为右下角终点；X 跨度 0.8–9.8，Y 跨度 0.7–7.4。
- 节点间距自检通过（任意两节点 ΔX≥1.55 或 ΔY≥0.85，无视觉重叠）。布局规则与示意图更新至设计文档"树形结构总览"。
- 仅改 `researchViewX/Y`，不涉及任何前置/数值变动；已核实研究视图不按 `techLevel` 画时代带，布局不受约束。

## 布局二次重排：紧凑阶段网格（2026-09-22）

游戏截图复核发现“双翼对角瀑布”在实际研究界面产生过大的 X/Y 跨度、长斜线和大片空白，因此由本节方案替代：

- 13 个节点改为四条主题行、四个进度列；X 范围从 0.8–9.8 收紧为 0.8–5.45，Y 范围从 0.7–7.4 收紧为 0.55–4.65。奶业保留紧凑 V 形分叉，其余链以水平短线为主。
- `Mugirl_Autoloading` 增加 `Mugirl_PneumaticWeapons` 前置，使武备线成为 Firearms → Pneumatic → Autoloading；`Mugirl_NeuralCombatSystems` 增加 `Mugirl_AdvancedGear` 前置，与 Autoloading 在右侧汇合。
- 兼容影响只涉及未完成研究：已完成项目按 `defName` 保留且不会回退；未完成的 Autoloading/Neural 需补齐新增种族前置。成本、物品解锁归属和原版双门槛均未改变。
- 布局边界与节点间距完成静态检查；游戏内仍需确认 13 节点完整显示、奶业分叉/封顶汇合不穿线，以及 fresh `Player.log` 无研究 Def 报错。

## 科技语义与解锁归属复核（2026-09-22）

按 XML 继承展开后的实际 `researchPrerequisite(s)` 逐项对照 13 个研究项目，修正名称与内容不贴合的问题：

- “挽具与拴系”改名“束具与拴系”，说明覆盖 11 件基础束具、两档钥匙和墙挂拴环；三类基础束具的原版副门槛由 `FlakArmor` 改为 `ComplexClothing`。
- 电击项圈从“规范采乳”移到更贴切的“高阶管制技术”（原“行为驯化”）；规范采乳只保留两种采奶器，高阶管制统一电子束具、精神控制、高科技钥匙/破解器和磁力镣铐。
- “雪牛娘日常服饰”改名“雪牛娘服装工艺”；`Mugirl_Gambeson`、`Mugirl_RA_Unionary`、`Mugirl_RA_Carrying_Equipment` 三件明确战斗用途的服装改挂“雪牛娘战斗装具”。
- 神经战斗甲/头盔清除基类继承的普通战斗装具门槛，只保留 `Mugirl_NeuralCombatSystems`，避免在两个研究项目的解锁面板重复出现。
- “雪牛娘气动武器”收窄为“雪牛娘气动骑枪”；“雪牛娘特化装备”改为“雪牛娘高阶装备”。所有 `defName` 保持不变。
- 继承展开复核结果：127 个可制造/建造 Def 各自只归属 1 个雪牛娘项目（重复归属 0）；关键计数为束具与拴系 14、规范采乳 2、高阶管制 5、服装工艺 46、战斗装具 22、高阶装备 10、枪械 11、气动骑枪 3、自驱装填 1、神经战斗系统 3。全 278 个 1.6 XML 解析通过，13 个项目中英文翻译、种族前置引用与节点间距检查均无缺失。

兼容影响：旧存档的研究完成状态不变；未完成“高阶管制技术”的存档会暂时失去电击项圈配方，未完成“战斗装具”的存档会暂时失去上述三件战斗服装配方。已经制作或穿戴的物品不受影响。
