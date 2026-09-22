# `DevData/Assets/7.15.3` 服装接入设计

状态：已实现；静态验证通过，待游戏内视觉验收。

日期：2026-07-15

## 1. 结论

`DevData/Assets/7.15.3` 可拆成 9 个接入目标：3 件成人新衣、6 件既有服装的儿童体型适配。

- 9 项素材均已接入；其中 6 项是既有物品的儿童体型贴图，没有新增重复 ThingDef。
- `ChangKu` 与 `XiongYi` 原本缺少地面/物品栏图标，现已从各自南向原画确定性裁边、等比放大并重新居中生成正式 512×512 图标，没有做生成式重绘。
- 成人方向贴图统一使用 `_Female_east/north/south`，儿童方向贴图统一使用 `_Child_east/north/south`。西向继续由 east 镜像，不需要额外贴图。
- 3 件新衣的数值均逐项复制现有相近服装；6 件既有服装只扩展 `Child` 穿戴与贴图，不改数值、文案、配方或物品身份。
- 不需要 C#。预计只涉及 XML Def、本地化、种族服装白名单和贴图目录。

## 2. 素材审查

| 目标 | 定位 | 审查结果 | 接入前处理 |
| --- | --- | --- | --- |
| `XianZi2` | 成人霜蓝仙子服 | 1 张图标、3 张方向图，均为 512×512，可用 | 改为正式 Def/路径名；方向图补 `_Female` |
| `ChangKu` | 成人牛仔长裤 | 3 张方向图，均为 512×512，未见裁边 | 已生成正式图标；正式命名并补 `_Female` |
| `XiongYi` | 成人白色胸衣 | 3 张方向图，均为 512×512，未见裁边 | 已生成正式图标；正式命名并补 `_Female` |
| 儿童仙女装素材 | `Mugirl_ImmortalFairy` 儿童适配 | 图标和 `_Child` 三方向齐全，512×512 | 忽略独立图标；方向图改名后归入现有仙女服目录 |
| 儿童衬衫素材 | `Mugirl_Shirt` 儿童适配 | 图标和 `_Child` 三方向齐全，512×512 | 忽略独立图标；方向图改名后归入现有衬衫目录 |
| `Mugirl_Bikini_Stocking` | 既有服装儿童适配 | 三方向已缩放到儿童体型 | 文件前缀 `MooGirl` 改为 `Mugirl`；`_Female` 改为 `_Child`；沿用现有图标 |
| `Mugirl_RA_Carrying_Equipment` | 既有携行具儿童适配 | 三方向已缩放到儿童体型 | 同上；沿用现有图标 |
| `Mugirl_RA_PowerArmor` | 既有侦察装甲儿童适配 | 三方向已缩放到儿童体型 | 同上；沿用现有图标 |
| `Mugirl_Uniform` | 既有制服儿童适配 | 三方向已缩放到儿童体型 | 同上；TMP 内 555×555 图标不接入，沿用现有 512×512 图标 |

所有方向贴图的非透明像素都处在画布内。`ChangKu` 南/北/东向的最低像素距离底边约 11–12 px，较近但尚未裁切，游戏内应重点检查走动、倒地和尸体旋转时是否出现贴边观感。

## 3. 接入原则

### 3.1 儿童适配不复制物品

RimWorld 1.6 的服装默认只允许 `Adult`。渲染身体服装时，游戏会在 `wornGraphicPath` 后追加身体类型；儿童身体类型会查找 `_Child` 贴图。因此 6 件既有服装采用以下做法：

1. 在原 ThingDef 的 `apparel` 中将 `developmentalStageFilter` 改为 `Child, Adult`。
2. 在原贴图目录补充同一路径下的 `_Child_east/north/south.png`。
3. 不新增儿童版 ThingDef，不复制配方，不新增物品栏条目，也不复制图标。

这样成人继续读取 `_Female`，儿童读取 `_Child`，存档中的物品身份和全部数值保持不变。

### 3.2 儿童仙女装与儿童衬衫复用现有物品

儿童仙女装素材归入 `Mugirl_ImmortalFairy`，儿童衬衫素材归入 `Mugirl_Shirt`。两者与其他儿童适配一致：原物品开放 `Child, Adult`，成人读取 `_Female`，儿童读取 `_Child`；不新增 ThingDef、图标、配方、名称或描述。

### 3.3 贴图与跨平台命名

- 正式文件名统一使用现有前缀 `Mugirl_`，不保留 `MooGirl_`、中文目录名、空格或 `(1)`。
- 贴图路径和磁盘大小写完全一致，避免 Linux/Steam Deck 上因大小写敏感而丢图。
- 新素材没有 mask，相关新 Def 使用 `useWornGraphicMask=false`。

## 4. 新物品设计

### 4.1 数值与穿戴定位

下表列的是接入后的有效关键数值。未列字段继续继承所写参考物，不另做平衡调整。

| 新 Def | 显示定位 | 数值/规则母版 | 层与覆盖 | 有效关键数值 |
| --- | --- | --- | --- | --- |
| `Mugirl_ImmortalFairyAzure` | 成人霜蓝仙子服 | `Mugirl_ImmortalFairy` | `OnSkin` + `Mugirl_FairyOverlay`；Torso、Shoulders；Adult | 布料 50；HP 140；工时 7500；质量 0.6；锐/钝/热 0.08/0.02/0.10；材料护甲 0.18；冷/热隔温 0.40/0.25；社交 +0.35；心灵敏感 +0.15；固定原色 |
| `Mugirl_Jeans` | 成人牛仔长裤 | `Mugirl_Hipwrappingskirt` | `OnSkin`；Legs；Adult | 布料 50；HP 130；工时 6000；质量 0.4；材料护甲 0.2；冷/热隔温 0.00/0.50；社交 +0.1；穿戴延迟 2.0 |
| `Mugirl_WhiteBustier` | 成人白色胸衣 | `Mugirl_Bar` | `Mugirl_Underwear`；Shoulders；Adult | 布料 50；HP 150；工时 2000；质量 0.2；材料护甲 0.09；冷/热隔温 0.40/0.40；社交 +0.2；穿戴延迟 0.7 |
| `Mugirl_ImmortalFairy` 儿童适配 | 复用现有仙女服 | `Mugirl_ImmortalFairy` | `OnSkin`；Torso、Shoulders；Child, Adult | 原物品全部数值与文案不变，只补 `_Child` 贴图 |
| `Mugirl_Shirt` 儿童适配 | 复用现有衬衫 | `Mugirl_Shirt` | `Middle`；Torso、Shoulders；Child, Adult | 原物品全部数值与文案不变，只补 `_Child` 贴图 |

设计说明：

- 霜蓝仙子服按现有仙女服做同数值换款，避免“2 代服装”无理由强于旧款；成品固定使用贴图原色，不接受材料着色或染料改色。
- 霜蓝仙子服保留 `OnSkin` 冲突规则，并追加绘制顺序为 `1` 的 `Mugirl_FairyOverlay`；与绘制顺序为 `0` 的牛仔长裤同穿时，仙子服稳定显示在裤子上层。
- 长裤按现有包臀裙做腿部日常服装换款，二者可直接比较，不引入新的保温档位。
- 白色胸衣按现有文胸做换款，保留内衣层和相同社交加成。候选贴图没有 mask，因此只在渲染设置上使用 `false`。
- 儿童仙女服和儿童衬衫不再复制数值，而是直接使用现有成人物品；不会出现两套配方、两份库存或两种价格。

### 4.2 暂定文案

文案只参考现有服装对材质、剪裁和使用场景的写法，不引入新的厂牌、公司或设计师设定。儿童文案保持日常化，不沿用成人服装中强调身体曲线的句子。

#### `Mugirl_ImmortalFairyAzure`

- 中文名：`雪牛娘 霜蓝仙子服 「流霜垂云」`
- 中文描述：`霜蓝轻纱羽衣，层叠裙摆如云气垂落，银铃与束带随步伐轻响。冷色绸面映出柔和光泽，轻盈剪裁让日常行走与仪式场合都从容自如。`
- 英文名：`Mugirl Azure Fairy Raiment "Frostfall Cloudveil"`
- 英文描述：`A frost-blue fairy raiment of layered gauze, its cloudlike skirt falling around a light sash while a silver bell chimes with each step. The cool-toned fabric carries a soft sheen, giving the garment an effortless presence in everyday wear and ceremonial settings alike.`

#### `Mugirl_Jeans`

- 中文名：`雪牛娘 牛仔长裤 「蓝境远行」`
- 中文描述：`修身牛仔长裤，耐磨斜纹布沿腿部收束，腰带与金属扣件兼顾固定和装饰。利落剪裁适合劳动、远行或日常穿着，在实用与时尚之间保持平衡。`
- 英文名：`Mugirl Jeans "Bluebound Journey"`
- 英文描述：`Fitted Mugirl jeans cut from durable twill, with a sturdy belt and metal fittings that balance utility and ornament. Their clean tailoring suits work, travel and everyday wear without giving up a polished silhouette.`

#### `Mugirl_WhiteBustier`

- 中文名：`雪牛娘 白色胸衣 「初雪轻拥」`
- 中文描述：`白色轻质胸衣，柔软布面以简洁弧线贴合身形，细窄肩带与加固缝线兼顾支撑和舒适。可单独搭配长裤，也可作为外衣之下的轻便内着。`
- 英文名：`Mugirl White Bustier "First Snow Embrace"`
- 英文描述：`A light white bustier with soft fabric, clean shaping and reinforced seams for comfortable support. It can be paired with trousers or worn as a simple underlayer beneath outer clothing.`

### 4.3 新物品公共字段

以下字段由现有服装父类继承，3 个新 ThingDef 均不另起一套规则。表中的“有效值”是游戏加载继承后的结果，不只是子 Def 中直接出现的字段。

| 字段 | 3 件新衣的有效值 | 来源/说明 |
| --- | --- | --- |
| `thingClass` | `Apparel` | `Mugirl_ApparelBase` |
| `category` | `Item` | `Mugirl_ApparelBase` |
| `techLevel` | `Industrial` | 没有单独覆盖，沿用基础服装 |
| `selectable` / `useHitPoints` | `true` / `true` | 可选择并具有耐久 |
| `pathCost` | `10` | 地图寻路代价字段，沿用雪牛娘服装基础值 |
| `tradeability` | `Sellable` | 可交易 |
| `tradeTags` | `Mugirl_Apparel` | 雪牛娘服装交易标签 |
| `Flammability` | `0.4` | 子 Def 未覆盖时继承 |
| `DeteriorationRate` | `1` | 子 Def 未覆盖时继承 |
| `Beauty` | `1` | 子 Def 未覆盖时继承 |
| `onGroundRandomRotateAngle` | `35` | 地面图随机旋转角度 |
| `CompColorable` | 牛仔长裤、白色胸衣有；霜蓝仙子服无 | 仙子服固定原画颜色，不进入染色流程 |
| `CompProperties_Forbiddable` | 有 | 支持禁用/允许 |
| `CompQuality` / `CompProperties_Styleable` | 有 | 由原版 `ApparelBase` 继承，支持品质与样式 |
| `researchPrerequisite` | `ComplexClothing` | 复杂衣物研究 |
| `workSkill` | `Crafting` | 制作技能 |
| `workSpeedStat` | `GeneralLaborSpeed` | 制作速度引用现有父类 |
| `recipeUsers` | `HandTailoringBench`、`ElectricTailoringBench` | 手工/电动裁缝台 |
| `unfinishedThingDef` | `UnfinishedApparel` | 未完成衣物 |
| 固定市场价值 | 不设置 | 继续由材料、工时、品质和游戏公式计算 |
| 默认年龄 | `Adult` | 6 件既有服装在原 Def 中显式扩展为 `Child, Adult` |

`StuffEffectMultiplierArmor` 和两个 `StuffEffectMultiplierInsulation_*` 是材料属性倍率，不是最终面板值。最终护甲、保温和市场价值仍会随选材与品质变化；本文记录的是 Def 中实际采用的倍率。

### 4.4 `Mugirl_ImmortalFairyAzure` 完整规格

| 项目 | 定稿候选 |
| --- | --- |
| `defName` | `Mugirl_ImmortalFairyAzure` |
| 父类 | `Mugirl_OnSkinBase` |
| 中文名 | 雪牛娘 霜蓝仙子服 「流霜垂云」 |
| 英文名 | Mugirl Azure Fairy Raiment "Frostfall Cloudveil" |
| 中文描述 | 霜蓝轻纱羽衣，层叠裙摆如云气垂落，银铃与束带随步伐轻响。冷色绸面映出柔和光泽，轻盈剪裁让日常行走与仪式场合都从容自如。 |
| 英文描述 | A frost-blue fairy raiment of layered gauze, its cloudlike skirt falling around a light sash while a silver bell chimes with each step. The cool-toned fabric carries a soft sheen, giving the garment an effortless presence in everyday wear and ceremonial settings alike. |
| 数值/文案母版 | `Mugirl_ImmortalFairy` |
| 年龄 | `Adult` |
| 穿戴层 | `OnSkin`、`Mugirl_FairyOverlay`；后者 `drawOrder=1`，保证高于牛仔长裤的 `OnSkin=0` |
| 覆盖部位 | `Torso`、`Shoulders` |
| 服装标签/分类 | `Mugirl_OnSkin` / `Mugirl_OnSkin` |
| 材料类别 | `Fabric`、`Leathery` |
| 材料数量 | `costStuffCount=50` |
| 制作研究/工作台 | `ComplexClothing`；手工或电动裁缝台 |
| 最低制作技能 | 不额外设置 |
| `WorkToMake` | `7500` |
| `MaxHitPoints` | `140` |
| `Mass` | `0.6` |
| `EquipDelay` | `2.0`，继承 `Mugirl_OnSkinBase` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | `0.08` / `0.02` / `0.10` |
| 材料护甲倍率 | `0.18` |
| 材料寒冷/炎热隔温倍率 | `0.40` / `0.25` |
| 装备属性偏移 | `SocialImpact +0.35`；`PsychicSensitivity +0.15` |
| 染色 | 禁用 `CompColorable`；地面图与穿戴图均强制使用贴图原色，材料仍正常参与数值与价值计算 |
| mask | `useWornGraphicMask=false` |
| 地面图路径 | `Things/Apparel/Mugirl_ImmortalFairyAzure/Mugirl_ImmortalFairyAzure` |
| 穿戴图路径 | 同上；读取 `_Female_east/north/south` |

需要落地的 4 张文件：

- `Mugirl_ImmortalFairyAzure.png`
- `Mugirl_ImmortalFairyAzure_Female_east.png`
- `Mugirl_ImmortalFairyAzure_Female_north.png`
- `Mugirl_ImmortalFairyAzure_Female_south.png`

### 4.5 `Mugirl_Jeans` 完整规格

| 项目 | 定稿候选 |
| --- | --- |
| `defName` | `Mugirl_Jeans` |
| 父类 | `Mugirl_OnSkinBase` |
| 中文名 | 雪牛娘 牛仔长裤 「蓝境远行」 |
| 英文名 | Mugirl Jeans "Bluebound Journey" |
| 中文描述 | 修身牛仔长裤，耐磨斜纹布沿腿部收束，腰带与金属扣件兼顾固定和装饰。利落剪裁适合劳动、远行或日常穿着，在实用与时尚之间保持平衡。 |
| 英文描述 | Fitted Mugirl jeans cut from durable twill, with a sturdy belt and metal fittings that balance utility and ornament. Their clean tailoring suits work, travel and everyday wear without giving up a polished silhouette. |
| 数值/文案母版 | `Mugirl_Hipwrappingskirt` |
| 年龄 | `Adult` |
| 穿戴层 | `OnSkin` |
| 覆盖部位 | `Legs` |
| 服装标签/分类 | `Mugirl_OnSkin` / `Mugirl_OnSkin` |
| 材料类别 | `Fabric`、`Leathery` |
| 材料数量 | `costStuffCount=50` |
| 制作研究/工作台 | `ComplexClothing`；手工或电动裁缝台 |
| 最低制作技能 | 不额外设置 |
| `WorkToMake` | `6000` |
| `MaxHitPoints` | `130` |
| `Mass` | `0.4`，继承 `Mugirl_OnSkinBase` |
| `EquipDelay` | `2.0`，继承 `Mugirl_OnSkinBase` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | 不设置 |
| 材料护甲倍率 | `0.2` |
| 材料寒冷/炎热隔温倍率 | `0.00` / `0.50` |
| 装备属性偏移 | `SocialImpact +0.1` |
| mask | `useWornGraphicMask=false` |
| 地面图路径 | `Things/Apparel/Mugirl_Jeans/Mugirl_Jeans` |
| 穿戴图路径 | 同上；读取 `_Female_east/north/south` |

需要落地的 4 张文件：

- `Mugirl_Jeans.png`，由南向原画裁边、等比放大并居中生成
- `Mugirl_Jeans_Female_east.png`
- `Mugirl_Jeans_Female_north.png`
- `Mugirl_Jeans_Female_south.png`

### 4.6 `Mugirl_WhiteBustier` 完整规格

| 项目 | 定稿候选 |
| --- | --- |
| `defName` | `Mugirl_WhiteBustier` |
| 父类 | `Mugirl_UnderwearBase` |
| 中文名 | 雪牛娘 白色胸衣 「初雪轻拥」 |
| 英文名 | Mugirl White Bustier "First Snow Embrace" |
| 中文描述 | 白色轻质胸衣，柔软布面以简洁弧线贴合身形，细窄肩带与加固缝线兼顾支撑和舒适。可单独搭配长裤，也可作为外衣之下的轻便内着。 |
| 英文描述 | A light white bustier with soft fabric, clean shaping and reinforced seams for comfortable support. It can be paired with trousers or worn as a simple underlayer beneath outer clothing. |
| 数值/文案母版 | `Mugirl_Bar` |
| 年龄 | `Adult` |
| 穿戴层 | `Mugirl_Underwear` |
| 覆盖部位 | `Shoulders`，与现有 `Mugirl_Bar` 保持一致 |
| 服装标签/分类 | `Mugirl_Underwear` / `Mugirl_Underwear` |
| 材料类别 | `Fabric` |
| 材料数量 | `costStuffCount=50` |
| 制作研究/工作台 | `ComplexClothing`；手工或电动裁缝台 |
| 最低制作技能 | 不额外设置 |
| `WorkToMake` | `2000` |
| `MaxHitPoints` | `150` |
| `Mass` | `0.2` |
| `EquipDelay` | `0.7` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | 不设置 |
| 材料护甲倍率 | `0.09` |
| 材料寒冷/炎热隔温倍率 | `0.40` / `0.40` |
| 装备属性偏移 | `SocialImpact +0.2` |
| mask | `useWornGraphicMask=false`；候选素材没有 mask，不复制 `Mugirl_Bar` 的 `CutoutComplex` 设置 |
| 地面图路径 | `Things/Apparel/Mugirl_WhiteBustier/Mugirl_WhiteBustier` |
| 穿戴图路径 | 同上；读取 `_Female_east/north/south` |

需要落地的 4 张文件：

- `Mugirl_WhiteBustier.png`，由南向原画裁边、等比放大并居中生成
- `Mugirl_WhiteBustier_Female_east.png`
- `Mugirl_WhiteBustier_Female_north.png`
- `Mugirl_WhiteBustier_Female_south.png`

## 5. 既有服装的儿童适配

| 原 Def | Def 改动 | 新增贴图 | 数值与文案 |
| --- | --- | --- | --- |
| `Mugirl_Bikini_Stocking` | `Adult` → `Child, Adult` | `Mugirl_Bikini_Stocking_Child_east/north/south.png` | 不变 |
| `Mugirl_ImmortalFairy` | `Adult` → `Child, Adult` | `Mugirl_ImmortalFairy_Child_east/north/south.png` | 不变 |
| `Mugirl_RA_Carrying_Equipment` | `Adult` → `Child, Adult` | `Mugirl_RA_Carrying_Equipment_Child_east/north/south.png` | 不变 |
| `Mugirl_RA_PowerArmor` | `Adult` → `Child, Adult` | `Mugirl_RA_PowerArmor_Child_east/north/south.png` | 不变 |
| `Mugirl_Shirt` | `Adult` → `Child, Adult` | `Mugirl_Shirt_Child_east/north/south.png` | 不变 |
| `Mugirl_Uniform` | `Adult` → `Child, Adult` | `Mugirl_Uniform_Child_east/north/south.png` | 不变 |

这些 Def 已经全部位于雪牛娘 `apparelList` 中，无需重复加入白名单。

### 5.1 `Mugirl_Bikini_Stocking` 保留规格

- 中文名：`雪牛娘 比基尼（带长筒袜） 「斑驳热焰」`
- 英文名：`Mugirl Bikini (With Stockings) "Flamme Marbrée"`
- 方案沿用的中文描述：`奶牛纹比基尼搭配过膝袜，整体设计突出动感与趣味性，适合活动或舞蹈场合。`
- 方案沿用的英文描述：`A cow-print strappy bikini paired with over-knee socks. Its neck bell, contrasting pattern and flexible cut give the outfit a lively appearance suited to activities or dance.`

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `Mugirl_UnderwearBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Industrial` |
| 穿戴层 | `Mugirl_Underwear` |
| 覆盖部位 | `Torso`、`Shoulders`、`Legs` |
| 标签/分类 | 有效标签 `Mugirl_Underwear`、`Mugirl_Slave_Underwear`；分类 `Mugirl_Underwear` |
| 材料类别/数量 | `Fabric` / `50` |
| 研究与工作台 | `ComplexClothing`；手工或电动裁缝台 |
| `WorkToMake` | `2000` |
| `MaxHitPoints` | `150` |
| `Mass` / `EquipDelay` | `0.2` / `0.7` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定护甲 | 不设置 |
| 材料护甲倍率 | `0.09` |
| 材料寒冷/炎热隔温倍率 | `0.40` / `0.40` |
| 装备属性偏移 | `SocialImpact +0.2` |
| mask | `false`，不变 |
| 地面图 | 沿用 `Mugirl_Bikini_Stocking.png` |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

### 5.2 `Mugirl_RA_Carrying_Equipment` 保留规格

- 中文名：`雪牛娘 特化携行具`
- 英文名：`Mugirl Specialised Field Gear`
- 方案沿用的中文描述：`迈瑞克革命战争期间，雪牛娘革命者普遍装备的携行具。承重挎包战术挂件可以为穿戴者提供战术补给减少武器远程冷却时间。`
- 方案沿用的英文描述：`Field gear widely issued to Mugirl revolutionaries during the Myrick Revolutionary War. Its load-bearing satchels and tactical fittings provide battlefield supplies that help reduce ranged weapon cooldown time.`

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `ApparelMakeableBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Industrial` |
| 穿戴层 | `Mugirl_Ornament`，绘制顺序 201 |
| 覆盖部位 | `Torso` |
| 标签/分类 | `Mugirl_Shell` / `ApparelMisc` |
| 材料类别/数量 | `Fabric`、`Leathery` / `25` |
| 研究 | `ComplexClothing` |
| 制作技能要求 | `Crafting 3` |
| 配方优先级 | `250` |
| `WorkToMake` | `1000` |
| `MaxHitPoints` | `250` |
| `Mass` / `EquipDelay` | `0.5` / `1.5` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `1.0` / `2` / `-3`；来自原版 `ApparelMakeableBase` 继承链 |
| 材料护甲倍率 | `0.1` |
| 材料寒冷/炎热隔温倍率 | `0.1` / `0.1` |
| 装备属性偏移 | `RangedCooldownFactor -0.25`；基础为 1 时结果为 0.75 |
| 是否满足遮羞 | `countsAsClothingForNudity=false` |
| Ideology 穿戴欲望 | `canBeDesiredForIdeo=false` |
| 地面图 | 沿用 `Mugirl_RA_Carrying_Equipment.png` |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

### 5.3 `Mugirl_RA_PowerArmor` 保留规格

- 中文名：`雪牛娘 侦察装甲`
- 英文名：`Mugirl Recon Armour`
- 方案沿用的中文描述：`轻量化侦察装甲，防护覆盖躯干关键部位。防御与挑逗的完美平衡，让敌人分神即是战术。`
- 方案沿用的英文描述：`Lightweight recon armour that protects the torso's vital areas. A perfect balance of defence and provocation, where distracting the enemy becomes part of the tactic.`

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `Mugirl_ArmorBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Ultra` |
| 穿戴层 | `Shell`、`Middle` |
| 覆盖部位 | `Torso`、`Neck`、`Shoulders`、`Arms`、`Legs` |
| 标签/分类 | `Mugirl_Armor` / `ApparelArmor` |
| 固定材料 | `Plasteel 120`、`WoodLog 30`、`ComponentSpacer 6` |
| 研究 | `FlakArmor` |
| 制作工作台 | `TableMachining` |
| 制作技能要求 | `Crafting 6` |
| 配方优先级 | `105` |
| `WorkToMake` | `45000` |
| `MaxHitPoints` | `280` |
| `Mass` / `EquipDelay` | `9` / `11` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | `0.92` / `0.40` / `0.46` |
| 固定寒冷/炎热隔温 | `64` / `9` |
| 装备属性偏移 | `MoveSpeed +0.25`；启用 Ideology 时另继承 `SlaveSuppressionOffset -0.3` |
| 装备行为 | `CompProperties_Biocodable`、金属弹开效果、动力甲穿脱音效 |
| 北向绘制层 | `60` |
| 地面图 | 沿用 `Mugirl_RA_PowerArmor.png` |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

### 5.4 `Mugirl_Uniform` 保留规格

- 中文名：`雪牛娘 制服 「权力禁区」`
- 英文名：`Mugirl Uniform "Zone Interdite du Pouvoir"`
- 方案沿用的中文描述：`雪牛娘标准制服，收腰西装搭配短裙，整体造型干练利落，适合办公或正式场合，展现专业与时尚的结合。`
- 方案沿用的英文描述：`A standard Mugirl uniform combining a fitted blazer with a short skirt. Its clean tailoring suits office work and formal occasions while balancing a professional appearance with practical everyday wear.`

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `Mugirl_OnSkinBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Industrial` |
| 穿戴层 | `OnSkin` |
| 覆盖部位 | `Torso`、`Shoulders` |
| 标签/分类 | `Mugirl_OnSkin` / `Mugirl_OnSkin` |
| 材料类别/数量 | `Fabric`、`Leathery` / `70` |
| 研究与工作台 | `ComplexClothing`；手工或电动裁缝台 |
| `WorkToMake` | `16000` |
| `MaxHitPoints` | `300` |
| `Mass` / `EquipDelay` | `0.4` / `2.0` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | `0.35` / `0.25` / `0.10` |
| 材料护甲倍率 | `0.3` |
| 材料寒冷/炎热隔温倍率 | `0.40` / `0.10` |
| 装备属性偏移 | `SocialImpact +0.1` |
| mask | `false`，不变 |
| 地面图 | 沿用现有 512×512 `Mugirl_Uniform.png`；TMP 的 555×555 图标不接入 |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

### 5.5 `Mugirl_ImmortalFairy` 保留规格

- 中文名：`雪牛娘 仙女服装`
- 英文名：`Mugirl Fairy Raiment`
- 名称、描述、配方及物品身份全部沿用现有内容，本次不新增儿童专用文案。

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `Mugirl_OnSkinBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Industrial` |
| 穿戴层 | `OnSkin` |
| 覆盖部位 | `Torso`、`Shoulders` |
| 标签/分类 | `Mugirl_OnSkin` / `Mugirl_OnSkin` |
| 材料类别/数量 | `Fabric`、`Leathery` / `50` |
| 研究与工作台 | `ComplexClothing`；手工或电动裁缝台 |
| `WorkToMake` | `7500` |
| `MaxHitPoints` | `140` |
| `Mass` / `EquipDelay` | `0.6` / `2.0` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定锐器/钝器/热能护甲 | `0.08` / `0.02` / `0.10` |
| 材料护甲倍率 | `0.18` |
| 材料寒冷/炎热隔温倍率 | `0.40` / `0.25` |
| 装备属性偏移 | `SocialImpact +0.35`；`PsychicSensitivity +0.15` |
| mask | `false`，不变 |
| 地面图 | 沿用 `Mugirl_ImmortalFairy.png` |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

### 5.6 `Mugirl_Shirt` 保留规格

- 中文名：`雪牛娘 衬衫 「惑光半透」`
- 英文名：`Mugirl Shirt "Lumière Trompeuse Translucide"`
- 名称、描述、配方及物品身份全部沿用现有内容，本次不新增儿童专用文案。

| 字段 | 接入后的有效值 |
| --- | --- |
| 父类 | `Mugirl_MiddleBase` |
| 年龄 | `Child, Adult`；本次唯一规则变化 |
| 技术等级 | `Industrial` |
| 穿戴层 | `Middle` |
| 覆盖部位 | `Torso`、`Shoulders` |
| 标签/分类 | `Mugirl_Middle` / `Mugirl_Middle` |
| 材料类别/数量 | `Fabric`、`Leathery` / `40` |
| 研究与工作台 | `ComplexClothing`；手工或电动裁缝台 |
| `WorkToMake` | `4000` |
| `MaxHitPoints` | `100` |
| `Mass` / `EquipDelay` | `0.4` / `2.0` |
| `Flammability` / `DeteriorationRate` / `Beauty` | `0.4` / `1` / `1` |
| 固定护甲 | 不设置 |
| 材料护甲倍率 | `0.2` |
| 材料寒冷/炎热隔温倍率 | `0.30` / `0.10` |
| 装备属性偏移 | 不设置 |
| mask | `false`，不变 |
| 地面图 | 沿用 `Mugirl_Shirt.png` |
| 成人方向图 | 沿用 `_Female_east/north/south` |
| 儿童方向图 | 新增 `_Child_east/north/south` |

## 6. 预计文件改动

### XML

- `1.6/Defs/Apparel/Apparel_OnSkin.xml`：新增霜蓝仙子服、牛仔长裤；制服与既有仙女服开放 Child。
- `1.6/Defs/Apparel/Apparel_Layer.xml`：加入仅用于排序的 `Mugirl_FairyOverlay`，绘制顺序为 `1`。
- `1.6/Defs/Apparel/Apparel_Middle.xml`：既有衬衫开放 Child。
- `1.6/Defs/Apparel/Apparel_Underwear.xml`：新增白色胸衣；长筒袜比基尼开放 Child。
- `1.6/Defs/Apparel/Apparel_Shell.xml`：侦察装甲、携行具开放 Child。
- `1.6/Defs/ThingDefs_Races/Mugirl_Race.xml`：将 3 个成人新 Def 加入 `apparelList`。
- `1.6/Languages/ChineseSimplified/DefInjected/ThingDef/Apparel_*.xml`：加入 3 个成人新物品的中文 label/description。
- `1.6/Source/Features/Misc/Harmony_Apparel_DrawColor.cs`：让霜蓝仙子服忽略材料色与染料色，保持贴图原色。

### 贴图

- 新建 3 个成人新衣的正式贴图目录，使用第 3.3 节的命名规则。
- 为 6 个既有贴图目录各补 3 张 `_Child` 方向图。
- 不把 `TMP` 目录、中文中间目录、重复图标或 555×555 的制服图标带入发布目录。

## 7. 验收标准

### 静态

- 所有 DefName 唯一，XML 可加载，3 个新 Def 都在雪牛娘 `apparelList`。
- `texPath`、`wornGraphicPath` 与磁盘大小写完全一致。
- 每件新衣有地面图标和 east/north/south 三方向；儿童适配有完整 `_Child` 三方向。
- 新文案同时具有英文基准与简中注入条目。
- 运行 `docs/tools/Invoke-Phase6StaticValidation.ps1` 无新增错误。

### 游戏内

- 成人三方向、儿童三方向、穿脱、倒地、尸体、睡眠和草稿状态均无红叉、错位或旧体型回退。
- 6 件共用物品能由成人和儿童穿戴，且成人仍用 `_Female`、儿童使用 `_Child`。
- 3 件成人新衣不能由儿童穿戴。
- 牛仔长裤与白色胸衣可以同时穿戴；白色胸衣与其他同覆盖内衣的冲突符合现有规则。
- 霜蓝仙子服与牛仔长裤同穿时显示在裤子上层，同时仍按 `OnSkin` 规则与其他躯干贴身衣物冲突。
- 霜蓝仙子服不能染色且始终显示原画颜色；其他新衣的材料染色、品质、耐久、裁缝配方、物品栏图标和地面显示正常。

## 8. 已采用的实施决策

1. 正式名采用：`霜蓝仙子服「流霜垂云」`、`牛仔长裤「蓝境远行」`、`白色胸衣「初雪轻拥」`。
2. 6 件既有服装直接开放给儿童，不制作重复儿童物品。
3. `ChangKu` 与 `XiongYi` 的地面图标从原南向绘制确定性生成，只做裁边、缩放和居中，不改变原画内容。
4. 霜蓝仙子服固定原色，并通过保留 `OnSkin`、追加 `Mugirl_FairyOverlay` 的方式仅调整渲染先后，不放宽穿戴冲突。
