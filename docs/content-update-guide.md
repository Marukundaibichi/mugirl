# 内容更新指南

本文档面向新增或调整内容：服装、武器、食物、Hediff、Job、事件、场景、种族外观、能力和可选集成。

1.7 的内容在 `1.7/Defs`、`1.7/Patches`、`1.7/Languages`，可选 DLC 与第三方内容在 `1.7/Mods/<名称>`；C# 在 `Source/MugirlRace`。1.6 目录已冻结。本文于 2026-10-16 同步已落地的 R/R2/S 名字和保存边界；完整 V 验证仍在进行。重构期间保持现有玩家文案、数值和行为，不能把下面的内容维护建议当作调整玩法的授权。

## 新增 Def 的基本流程

1. 选择合适目录，不要把新内容混进无关文件。
2. 新 defName 使用 `Mugirl_<PascalName>`，新文件按 Def 类型和主题命名（[ADR-009](architecture/ADR-009-naming-and-layout.md)）。不要仅为了改掉现存日期文件名而改变加载序列；移动/拆分需核对完整 Def 顺序。[冻结的 151 项公开 API](architecture/ADR-008-public-api.md) 不能改。
3. label、description、reportString、message、Gizmo 文本等玩家可见内容进入语言文件。
4. 如果 C# 要引用该 Def，加入定义它的功能的 `DefOf`；DLC 内容加 `[MayRequire]`。
5. 如果内容只在可选 DLC 或第三方 mod 存在时才需要，使用 `MayRequire`、`1.7/Mods/<名称>` 条件目录或兼容层。
6. 改名或删除已发布 Def、类型、Scribe 字段时，在本模块 `SaveUpgrade/Modules` 登记，并证明旧数据和进行中状态的处置（[ADR-007](architecture/ADR-007-save-schema-upgrade.md)）。未知形状不猜测，玩家同形文字不重写；公开 XML 别名和为保持既有行为而保留的内容另按其契约维护。
7. 运行 `pwsh tools/Validate.ps1 -WorkRoot <独立目录>`；涉及生成、交互、战斗、事件时，再做游戏内验证（[验证手册](validation-runbook.md)）。

## 服装与装备

- 固定材料成品若继承了会携带材质的半成品（如 `UnfinishedWeapon`、`UnfinishedApparel`），须在具体装备的 `recipeMaker` 中显式设置 `workAmount`，并与其基础 `WorkToMake` 同步维护。原版制作任务会把半成品的材质传入工作量计算；缺少显式值时，即使成品没有可选材质，也会叠加玻璃钢、铀、黄金的材料倍率。真正可选材质的装备继续使用原版材料修正；不要在通用装备基类锁定工作量。相关排查与修复见[固定材料装备制作工作量](records/equipment-work-amount-2026-09-30.md)。
- 新增服装须显式设置或从父定义继承 `thingCategories`，才能出现在储存区筛选列表中；`apparel.layers` 和 `apparel.tags` 不能替代储存分类。`Mugirl_CasualApparelBase`、`Mugirl_ArmorSmithableBase` 未提供默认分类，其具体服装须自行指定。沿用现有雪牛娘头饰、贴身服装、内衣、丝袜分类；包包归 `ApparelMisc`，骑士甲归 `ApparelArmor`。
- X 乳贴使用 `Mugirl_SkinSticker`（drawOrder -1）；高叉毛衣使用 `OnSkin`（0），乳贴应位于毛衣下方、身体上方。
- 需要取消染色的服装在其 `<apparel>` 内加 `<tags><li>MugirlFixedColor</li></tags>`：`Harmony_Apparel_DrawColor_FixedColor` 让穿着、掉落、图标渲染固定使用 def 颜色（默认白色），材质色与染色不再生效；`Harmony_Dialog_StylingStation_Ctor_FixedColor` 与 `..._Reset_FixedColor` 从造型台染色列表剔除它们；雕像色与尸体腐烂色仍保留。2026-09-26 名单：大姐姐口罩、太空面罩、太空服、OL 套装、兔女郎三件、逆兔女郎两件、X 乳贴、名牌包包、真皮高跟鞋、特化携行具、骑士坐骑甲三件、警察服全套、修女装全套、霜蓝仙子服。
- 确认 `apparel.layers`、`bodyPartGroups`、`wornGraphicPath` 和贴图路径一致。
- 普通身体和自定义饰品层的穿戴贴图须使用 `<wornGraphicPath>_Female_<方向>.png`；允许儿童穿戴的还需 `_Child` 版本。`Overhead`、`EyeCover` 和背包的路径规则不同，不能统一添加体型。`useWornGraphicMask` 只控制染色遮罩，不控制体型后缀；遮罩也必须沿用相同体型前缀。物品图标存在不能证明穿戴贴图可用。
- 生成用标签要和 PawnKind、商人库存、配方解锁配套。
- 束具类装备如果会添加 Hediff，检查穿戴、卸下、破解、销毁、保存读档路径。
- 需要特殊 Gizmo 的装备要确认按钮文本、冷却、佩戴者过期对象检查。
- 贴图源文件不进发布目录；发布目录只放游戏需要加载的资源。

## 墙壁栓点

`Mugirl_WallRopeHitch` 是无动力墙面附件，只保留用于栓系的 `CompUsable_RopeToHitch`，不添加 `CompProperties_Flickable` 或供电组件。原版开关组件即使没有供电组件也会生成电力开关按钮和拨动任务。调整组件时须验证实际栓系菜单、任务、拆除解绳和已有建筑读档。见[多余开关修复记录](records/wall-hitch-switch-2026-10-27.md)。

## Hediff 与效果

- `HediffCompProperties` 中可调参数放 XML，运行状态放 Comp。
- 会自然消失的 Hediff 写清 tick 含义，避免注释和数值不一致。
- 影响 Pawn 能力、心情、饥饿、免疫、意识、疼痛等行为时要记录验证点。
- Tick 类 Comp 要有检查间隔或必要性说明。

## 泌乳与世界人物封存

`Mugirl_Lactation` 在 1.7 中设置 `allowMothballIfLowPriorityWorldPawn=true`，允许原版认定的低优先级世界人物封存，包括 `Free`、`ForSaleBySettlement`、`FactionLeader`、`Kidnapped`。其他 Hediff 的封存阻止项仍有效；商队和旅行中的运输舱仍由原版排除，地图上的产奶逻辑不变。

封存期间有意暂停奶量与挤奶设备库存的后台累积，保留已有数值；回图后恢复更新，不补算封存期间的产量。产后增产期限按游戏 tick 与生育时间差计算，不因封存延长。此设置不移除 Hediff，也不改变世界人物 GC 的保留规则。它是定义修改，重启游戏加载后生效，不需要 DLL 或存档结构迁移。

## Job、WorkGiver 与能力

- 成人右键“喝奶”保留 Job 与收益，但不播放双方交互动画；“喂奶”和孩子“找奶喝”继续使用喂奶动画。开发者模式选中雪牛娘可打开喂奶姿态微调，暂停后在地图中预览附近被喂者，调整双方 X/Z 偏移与角度；配置可导出到 `Config/Mugirl/MilkFeedingLayout.xml`。
- Biotech 雪牛投掷与流星大灌篮分别使用 `Textures/UI/Mugirl_Throw.png`、`Mugirl_Dunk.png`。普通奶渍和旧电击奶渍使用 `Textures/Things/Filth/Mugirl_MilkStain/` 下的三张变体；水渍和喷乳 Fleck 保留原 `Mugirl_PoolSoft`。
- Job 的 `TargetA/B/C` 含义要在注释或代码中能看懂。
- WorkGiver 扫描范围要窄，避免全图宽扫描。
- 强制 Job、打断 Job、等待 Job 都要处理目标死亡、倒地、离图、地图不一致。
- 能力 AI 条件要保守，避免无条件群控或误伤非目标阵营。

### 雪牛投掷与流星大灌篮（Biotech）

- 雪牛投掷要求施术者能使用暴力；禁用暴力时按钮禁用，直接调用或已排队的举起请求也会被拒绝。举起后失去暴力能力会安全放回物体，已经飞出的物体继续完成落地结算。
- 雪牛投掷先举起地图上的实体，再选择落点投出。目标的有效重量不得超过施术者的 `CarryingCapacity`；投掷上限与举起上限一致。重量相等时仍可举起。物品堆按整堆重量判定。能力冷却 5000 tick（2 游戏小时）。
- 落点冲击伤害为 `4 + √有效质量 × 3.5 + 投掷距离 × 0.75`，向下限 6、上限 110 取整（2026-09-26 削减，约为原先的一半；原公式 `8 + √质量 × 7 + 距离 × 1.5`，范围 12..220）。穿甲与爆炸半径按同一伤害值推导，随之同步降低。
- 建筑只因实际结构连接被拒绝举起：在整个占地及四边邻格中，按 `graphicData.linkType/linkFlags` 判定相容的连接，支持围栏等 `asymmetricLink` 的额外类别与门端点；双方任一方向的结构连接都计入。只有 `MapEdge` 标记不构成建筑之间的连接，斜角或隔一空格不计入；Odyssey 下图形结构连接不跨越飞船地基边界。输电线（含隐藏电缆）与同格或四边相邻的输电端连接时也拒绝，断电不解除结构限制。家具靠墙、桌椅并排、墙与电缆重叠或相邻，都不因这些摆放关系被拒绝；普通用电设备接入电网仍可举起。第三方建筑复用上述连接属性时同样适用，未接入这些属性的自定义管网不自动推断。见[连接限制修复记录](records/throw-building-connections-2026-10-26.md)。孤立结构及获准目标仍须满足重量限制。所有未显式定义 `Mass` 的 `Building`，有效重量至少为完整最大耐久度 × 10，并保留原有占地面积、建造材料估算形成的更高下限；不能因为 `Mass` 默认返回 1 或建筑受损而变轻。`Mineable` 山体即使被其他模组赋予较小的显式 `Mass`，也有完整最大耐久度 × 10 的重量下限。原版墙体基础耐久 300 对应 3000 kg，实际重量随建材的耐久修正变化；花岗岩山体为 9000 kg。普通 550 kg 搬运上限无法举起，搬运能力提升到足够高时才能举起。其他显式定义 `Mass` 的物体（如花岗岩岩块 `ChunkGranite`）仍按原有质量规则计算。
- 飞行物的位置与旋转在相邻游戏 tick 间按真实渲染时间补间，暂停时冻结；空中旋转保持每游戏 tick 42°，让物体在飞行途中转多圈，同时通过逐帧补间消除跳角。飞行控制器允许跨屏绘制，避免施术者离开镜头后被误剔除。屋顶穿透、命中和落点仍由整数游戏 tick 决定，不因显示补间提前触发。
- 投掷进行中（举起瞄准与物体在空中的整个阶段）持物者不能开枪：射击菜单与小队齐射指令给出“正举着待投掷的物体”的禁用理由（`Harmony_FloatMenuUtility_GetRangedAttackAction_ThrowLock`），`Verb.TryStartCastOn` 前置补丁兜底拦截绕过菜单的射击（`Harmony_Verb_TryStartCastOn_ThrowLock`，仅限 `Verb_LaunchProjectile`，近战与能力施放不受影响）。落地或放回物体后射击立即恢复。
- 举起载荷的绘制按类型分流：`Pawn` 飞行态走 Pawn 旋转渲染器、持有态走自身 `DynamicDrawPhaseAt`；`Corpse` 的 ThingDef 没有 `graphicData`，直接取 `thing.Graphic` 会画成红叉（`BaseContent.BadGraphic`），必须转交 `InnerPawn` 的渲染树——持有态调 `Corpse.DynamicDrawPhaseAt`（同 `Building_GibbetCage` 的悬挂尸体），飞行态复用 `PawnSpinRenderer` 画 `InnerPawn`；`InnerPawn` 为空的损坏尸体不绘制。其余物品与建筑才走 `Graphic.GetShadowlessGraphic()` 分支。
- 雪牛流星大灌篮是仅开发者模式可见和可用的整蛊能力。目标必须是仍有头的人形 `Pawn`：摘下目标的头作为篮球拍向灌篮落点，身体留在原处。选目标时若篮筐附近没有可跳落点，应拒绝施放，避免施术者原地起落而头在远处爆裂。起跳和上升阶段，头颅应贴近施术者手部，随 `PawnFlyer` 的位置同步移动。雪牛娘从最高点开始下坠时，头颅立即从手边加速下扣，比雪牛娘更快地砸向篮筐；飞行转下扣的相邻画面应连贯，头不能先悬停或回跳。头颅的触地点应位于空中雪牛娘面朝方向的前方、画面下方，并在她落地前触地；撞地当刻才播放冲击音效与碎裂、血液效果，不在地面弹跳。头在空中的位置与旋转按渲染帧在相邻游戏 tick 之间补间；空中旋转保持每游戏 tick 46°，飞行途中转多圈且画面连续。即使头飞离控制器所在视野也要继续绘制。摘头瞬间、拍球触地和灌篮触地碎裂时要有血液飞溅演出，效果遵循目标的 `CanBleed`：不流血的目标不生成血液或血沫。手中篮球的头和头发按目标原本的实际绘制尺寸显示，不能因改用独立物体绘制而缩小。无头、非人形及无效目标应给出明确拒绝提示。
- 灌篮由投掷基因一并授予，不在普通基因界面新增独立灌篮基因。历史 `throwGeneFixed`、`dunkGeneFixed`、`throwSystemGeneFixed` 与异种修复进度已移入 `SaveUpgrade/Modules/Milk`，由 `Milk.XenotypeRepair` 和 `GameComponent_MilkUpgrade` 保留原完成条件、剩余有效 tick 与触发时点。安全范围内补基因的行为不变，自定义异种和植入异种胚芽仍保留；不能在当前领域代码重新读取旧标志。详见[Milk 实施记录](records/refactor-1.7-milk-2026-10-15.md)。
- 这些能力涉及目标离图、销毁、读档恢复与施术者中断时的实体归还；调整流程后按验证手册复查，不能留下消失的目标或控制器。

## 事件、任务和派系

- 生成 Pawn 时确认年龄、性别、派系、招募状态、世界 pawn 注册和清理路径。
- 信件目标和 quest signal 需要能处理多个 pawn 或生成失败。
- 新事件要检查无 DLC、全 DLC、对应可选 mod 缺失时的行为。

### 雪牛娘事件入口与生成规则

民间随机事件集中在 `1.7/Defs/Storyteller/Incidents_Mugirl.xml`，巨企事件保持独立目录与调度。任务脚本目前沿用原有 Def 名称。以后需要改名时，在升级器中登记映射（[ADR-007](architecture/ADR-007-save-schema-upgrade.md)），不为旧档保留名字。

| 事件 | 入口 | 基础权重 | 人物与去留 |
| --- | --- | --- | --- |
| 开局双人坠舱 | `GameComponent_Storyline` / `StorylineService`，开局剧情开关 | 固定剧情 | 两名 18 岁、无派系、倒地雪牛娘；后接运货员 |
| 随机坠舱救援 | `Mugirl_SlaveRefugeePodCrash` | 0.6 | 一名无派系成年雪牛娘，使用与开局相同的两天疲惫状态确保倒地，走原版救援流程 |
| 流浪者申请加入 | `Mugirl_SlaveWandererJoins` | 0.4 | 一名无派系成年雪牛娘；信件接受后入场并加入，拒绝或一天超时结束 |
| 大迁徙 | `Mugirl_Migration` | 1.0 | 保留迁徙开关、人数、服装与离图规则 |
| 牛聚变投资 | `Mugirl_FusionInvestment` | 0.7 | 保留投资开关、金额、回报与后续研究者剧情 |
| 逃跑农场 | `Mugirl_RunawayFarmQuest` | 0.8 | 保留农场开关、场景任务与奖励 |

2026-10-04 将旧野人事件的 1.0 基础权重拆成救援 0.6 与申请加入 0.4；这是候选相对权重，不是固定发生率，仍受人口曲线、故事讲述者与可用性影响。随机救援和申请加入不受开局剧情开关控制。其余事件权重、人数与收益不变。

无派系民间人物共用 `Mugirl.Core.GeneratedPawnUtility.CreateFactionlessAdultRequest`：女性、成年、可招募，不生成怀孕或人物关系；任务各自处理年龄、倒地、服装、世界人物注册和去留。流浪者必须覆写原版的 `GeneratePawn_NewTemp(Map)`，只覆写旧 `GeneratePawn()` 会生成原版人类。

`Mugirl_WildManWandersIn` 的随机玩法已退役。`1.7/Defs/Compatibility/RetiredEvents.xml` 继续保留原隐藏、零权重、不可执行的 Def 和文字，避免改变既有队列及历史读取行为；worker 已移到 `Mugirl.SaveUpgrade.IncidentWorker_RetiredEvent`。旧 PawnKind 和已生成的野人不清理。原计划中的删 Def 未执行，原因是最新行为守恒约束，见[Core/Content 记录](records/refactor-1.7-core-content-2026-10-16.md)。

产奶能力只检查身体、生理年龄及性别条件，不再要求派系。开局、随机坠舱、迁徙及旧档野人在招募前也会积累奶量，有奶时可手动榨乳，达到既有门槛后可喝奶。自动工作扫描范围、奶量消耗与产出不变，儿童仍不能产奶。

## 雪牛娘机械体暂缓发布

- 2026-09-26 起机械体暂不随更新启用。冻结的 1.6 目录中，`Mugirl_Mechanoids.xml`、`Mugirl_MechanoidBodies.xml`、`Mugirl_MechanoidAttacks.xml` 的种族、PawnKind、身体及攻击 Def 和对应中文 DefInjected 翻译均整组注释，源码保留在 `1.6/Source/Features/Mechanoids`。1.7 没有迁移这些 Def 与源码，共用贴图仍在根 `Textures/Things/Pawn/Mechanoid`。
- 恢复时须整组重新实现并复查引用，不能只恢复种族或 PawnKind。当前版本没有机械体制作入口。曾用开发者模式生成的测试机械体依赖这些 Def；按[内容清单](refactor-1.7/inventory/content.md)的建议，旧档中的机械体及其尸体由升级器删除。

## 地图植物进食

- 雪牛娘吃地图植物时只替换原版 `JobDriver_Ingest.PrepareToIngestToils`，使植物留在地上；后续咀嚼及 `FinalizeIngest` 仍由原版 Job 执行。不能因为植物没有立刻销毁，就认定营养没有结算：原版 `Plant.IngestedCalculateAmounts` 在只吃掉部分营养时降低 `Growth`，吃完整株才销毁。
- 进食卡住的核对需同时记录 Job 是否结束、植物 `Growth`、食物需求值和 fresh log；隔离测试入口见[验证手册](validation-runbook.md)，本轮调查见[机械体暂缓、投掷暴力限制与吃草调查](records/mech-throw-graze-2026-09-26.md)。

## 资源路径

- XML 路径使用游戏资源路径，不带文件扩展名。
- 大小写尽量和磁盘一致，减少跨平台或打包时的路径问题。
- 发布包脚本排除源码、bin/obj、`.sai2`、临时文件和备份目录；1.7 的 DLL 与匹配 PDB 一并保留，不能混用另一构建的符号文件。

## Facial Animation 内容

- 新增或修改 `1.7/Mods/FacialAnimation` 的 `Defs`、`Textures` 前，先看 `features/facial-animation-guide.md`。
- 新 FA 动画必须写 `raceName=Mugirl`，否则当前雪牛娘专属动画池不会使用它。
- 新 shape 要同时确认 `*ShapeDef` 和 `{shape}_south` 贴图；缺贴图会按 `altShapeDef`、`normal`、透明 dummy 静默回退。
- 不要删除或限制 `normal_Mugirl`，它是所有动画合成的基础帧。
- 眼睛贴图当前按整张眼睛和高光绘制，不写 `altMaskPath`；只有重新启用左右眼 mask 时，才补 `FA/Eyes/Common` 并把 `altMaskPath` 加回所有相关 Eye TypeDef。
- `blush`、`lovinblush` 必须作为 `headShapeDef` 使用并画在 `Heads_Blank`，这样脸红位于眼白下方；不要在新动画里用 `emotionShapeDef=blush/lovinblush`。
- `sad` 也是严肃嘴，不只代表低心情；静态攻击、`Wait_Combat` 等沉默紧张状态用 `sad`，咬牙或明显用力状态才用 `tight`。`Wait_Combat` 可以用 `angled` 眉和扫视表现警戒，但不能全程半眯。
- 疼痛动画不需要哭泣层；普通 `Wear`、`RemoveApparel` 不使用爱心眼或 `lovinblush`，这些强表情留给 Lovin 或明确特殊互动。
