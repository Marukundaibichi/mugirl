# Facial Animation 维护指南

本文记录雪牛娘接入 `[NL] Facial Animation - WIP` 的实际运作逻辑和后续更新规则。结论基于当前本 mod 的 `1.6/FacialAnimation`、`Textures/FA`，以及对 FA `1.6/Assemblies/FacialAnimation.dll` 的反编译核对。

## 接入入口

- `LoadFolders.xml` 只在 `Nals.FacialAnimation` 启用时加载 `1.6/FacialAnimation`。
- `About/About.xml` 使用 `loadAfter` 排在 `Nals.FacialAnimation` 之后，并将旧独立补丁 `Luca.MuGirlFacialAnimation` 标为不兼容。
- `1.6/FacialAnimation/Patches/MooGirl_FacialAnimation.xml` 给 `MooGirl` 追加 FA comp。当前使用这些层：
  - `DrawFaceGraphicsComp`
  - `HeadControllerComp`
  - `EyeballControllerComp`
  - `LidControllerComp`
  - `BrowControllerComp`
  - `MouthControllerComp`
  - `LidOptionControllerComp`
  - `EmotionControllerComp`
  - `FacialAnimationControllerComp`
- FA 还支持 `SkinControllerComp`，但本 mod 当前没有追加它，也没有维护 `Textures/FA/Skins`。不要因为看到 FA 支持 Skin 层就顺手补贴图；除非明确决定启用 Skin 层，否则保持不用。

## FA 数据模型

FA 把脸拆成两类 Def：

- `*TypeDef`：定义某类部件使用哪套贴图、shader、颜色模式、适用种族、性别和基因条件。
- `*ShapeDef`：定义动画可以切换到的形状名。贴图文件名必须和 shape 名匹配，或通过 `altShapeDef` 回退。

MooGirl 当前 TypeDef 全部写了 `raceName=MooGirl`。每层都有 `Normal` 到 `Normal7` 七套贴图目录，其中 `Normal` 对应原 1 号脸，`Normal2` 到 `Normal7` 对应 `TMP/FA/2` 到 `TMP/FA/7`：

| 层 | TypeDef | 贴图根路径 |
| --- | --- | --- |
| 头 | `MooGirl_HeadNormal`、`MooGirl_HeadNormal2`...`7` | `FA/Heads_Blank/Normal`、`Normal2`...`7` |
| 眼睛 | `MooGirl_EyeNormal`、`MooGirl_EyeNormal2`...`7` | `FA/Eyes/Normal`、`Normal2`...`7` |
| 眼皮 | `MooGirl_LidNormal`、`MooGirl_LidNormal2`...`7` | `FA/Lids/Normal`、`Normal2`...`7` |
| 眉毛 | `MooGirl_BrowNormal`、`MooGirl_BrowNormal2`...`7` | `FA/Brows/Normal`、`Normal2`...`7` |
| 嘴 | `MooGirl_MouthNormal`、`MooGirl_MouthNormal2`...`7` | `FA/Mouth/Normal`、`Normal2`...`7` |
| 眼皮附加层 | `MooGirl_LidOptionNormal`、`MooGirl_LidOptionNormal2`...`7` | `FA/LidOptions/Normal`、`Normal2`...`7` |
| 情绪叠层 | `MooGirl_EmotionNormal`、`MooGirl_EmotionNormal2`...`7` | `FA/Emotions/Normal`、`Normal2`...`7` |

`raceName` 的行为很重要：只要某个种族存在任意专属 TypeDef，FA 就用该种族的专属 TypeDef 池替代通用池。MooGirl 因此不会随机到 FA 自带的人类脸型。

FA 的控制器是按层独立随机 TypeDef，不会自动保证 `Normal4` 的头、眼、嘴成套出现。当前各套目录都补齐完整 shape 词表，因此即使混搭也不会透明或回退缺图。若未来需要严格锁定“同一脸号成套随机”，需要额外 C# glue 或改造 FA 选择逻辑。

性别目录来自 `Pawn.gender` 的枚举名，例如 `Female`、`Male`、`None`。雪牛娘种族 `maleGenderProbability=0`，当前只维护 `Female` 贴图是有意选择。若调试或其他 mod 生成非女性 MooGirl，FA 会找不到对应性别贴图并显示透明 dummy 或回退。

## 参考案例：MoeLotl / Axolotl FA 补丁

参考包路径：`C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3292354961`。该包是一个独立 FA 子 mod，`About/About.xml` 声明依赖 `[NL] Facial Animation - WIP` 和本体种族 `HenTaiLoliTeam.Axolotl`，并 `loadAfter` 两者。`LoadFolders.xml` 在 1.5/1.6 都加载根目录和 `Common`，只有 Anomaly 启用时才额外加载 ghoul 头补丁。

它的接入入口是 `Common/Patches/AxolotlFA.xml`：

- 先用 `PatchOperationTest` 确认 `AlienRace.ThingDef_AlienRace[defName="Axolotl"]` 存在。
- 若没有 `comps`，直接创建 `comps` 并放入整套 FA comp。
- 若已有 `comps`，一次性追加整套 FA comp：`DrawFaceGraphicsComp`、头/眼/眼皮/眉/嘴/皮肤/眼皮附加/情绪控制器，以及 `FacialAnimationControllerComp`。
- 它启用了 `SkinControllerComp`，这是和 MooGirl 当前补丁最明显的功能差异。

这个补丁可作为入口结构参考，但不要照抄它的追加方式：它只检查 `comps` 是否存在，没有像 MooGirl 现在这样逐个 `compClass` 做 duplicate guard。若目标种族或其他兼容补丁已提前追加过部分 FA comp，这种批量追加可能制造重复 comp。MooGirl 现有 `MooGirl_FacialAnimation.xml` 的逐项条件追加更稳，应继续保留。

`Common/Patches/AxolotlFA_compatibility.xml` 展示了另一个关键原则：一旦种族写了自己的 `raceName=Axolotl` 动画池，FA 就不会自然混用人类默认动画，所以兼容 mod 新增的 Job 也要补到对应 `FaceAnimationDef.targetJobs`。该补丁在 RJW 存在时把 RJW Job 追加到自定义 Lovin、Combat、Wear 动画；在 `Rimworld Animations 2.0` 存在时移除部分 Lovin 的 `headOffset`，避免和外部动画姿态冲突。MooGirl 未来若给挤奶、束具、骑乘、RJW 或其他 Job 写专属动画，也应采用“补 targetJobs 或新增同 raceName 动画”的思路。

### Axolotl 的 Type / Shape 组织

Axolotl 把长相定义拆在 `Common/Defs/FaceTypeDefs/Axolotl` 和 `Common/Defs/FaceShapeDefs`：

- 所有 TypeDef 都写 `raceName=Axolotl`，因此它完全进入种族专属脸池。
- 大多数 TypeDef 写了 `enableUnisexTexPath=True`，所以贴图末级目录是 `Unisex`，不是 `Female` / `Male`。MooGirl 当前没有启用该选项，所以仍应使用 `Female`。
- 它为同一层维护多个 TypeDef 变体：眼睛 `Normal1`-`Normal4`，眼皮 `Normal1`-`Normal4`，嘴 `Normal1`-`Normal3`，皮肤装饰 `RightEye`、`LeftChin`、`Heart`、`Reddot` 等。TypeDef 决定 Pawn 抽到哪套贴图，ShapeDef 决定动画帧切换到哪个形状。
- 多 TypeDef 变体会放大素材维护量：每个会被动画引用的 shape，都要在每个可能抽到的 TypeDef 目录下有对应贴图，否则该 Pawn 会回退或透明。
- ShapeDef 里大量使用 `altShapeDef` 作为素材缺失时的软回退，例如 `surprise -> normal`、`hot -> blush`、`cold -> normal`。这适合“有些变体暂时没画全”的阶段，但不能代替最终素材检查。
- `LidShapeDef close`、`sleep1`、`sleep2`、`cry` 写了 `disableEyeball=true`，用于闭眼或哭泣时隐藏眼球。MooGirl 新增完全闭眼形状时也可以采用这个规则。
- `AxolotlRaceFaceAdjustment.xml` 额外定义 `FaceAdjustmentDef`，把脸整体缩放到 `(1.38,1.38)`，并指定左右眼锚点 label。注释里保留了按年龄调整 size/offset 的写法。这个 Def 适合修正“整张 FA 脸和种族头型比例/位置不匹配”的问题，不是每个种族都必须有。

### Axolotl 的贴图安排

Axolotl 的实际素材集中在 `Common/Textures/Things/Pawn/Axolotl`，顶层按 FA 层拆分：`Heads_Blank`、`Eyes`、`Lids`、`Brows`、`Mouth`、`LidOptions`、`Emotions`、`Skins`。

关键布局结论：

- `Heads_Blank/AxolotlHead/Unisex` 有 `normal_south/east/north`，也有 `surprise_*` 基础头图；`blush_cover_*`、`hot_cover_*`、`cold_cover_*` 则是 head cover 叠层，不是完整头底图。
- `Eyes/Normal1..4/Unisex` 维护 `normal`、`heart` 眼睛及对应 `*_highlight_*`；`Eyes/Common/Unisex` 维护 `normal_L_*`、`normal_R_*` 左右眼 mask。它的 `altMaskPath` 和实际 mask 目录是闭合的，可作为 MooGirl 未来重新启用左右眼 mask 时的参考。
- `Lids/Normal1..4/Unisex` 使用 `*_bottom_*` 与 `*_cover_*` 拆分眼皮。它有些 shape 只提供 cover 或只提供 bottom，靠回退/透明承担缺口；MooGirl 若维护单一高质量脸型，仍建议每个眼皮 shape 成对补齐，减少方向或层级异常。
- `Mouth/Normal1..3/Unisex` 每套都维护同一组 mouth shape，例如 `normal`、`open`、`smile`、`down`、`sleep1/2`、`cry1/2`、`lovin1/2/3`、`drowsiness` 等。这说明多嘴型 TypeDef 的前提是每套目录都要覆盖完整动画词表。
- `Skins/*/Unisex` 是 SkinControllerComp 的装饰叠层，提供 `normal_south/east/north/west`，不少目录同时有 PNG 和 DDS。它和 `Heads_Blank` 不是同一层；如果 MooGirl 不启用 `SkinControllerComp`，不需要维护 `Textures/FA/Skins`。
- 参考包大量使用 `Unisex` 目录，是因为 TypeDef 明确开启了 `enableUnisexTexPath`。不要把这一点误读成 FA 默认会找 `Unisex`。

对 MooGirl 的可执行结论：

- 当前 comp 补丁比 Axolotl 的批量追加更安全，继续保持逐 comp duplicate guard。
- 不启用 Skin 层时，不要新增空的 `SkinControllerComp`、`SkinTypeDef` 或 `Textures/FA/Skins`；启用 Skin 时必须同时补 TypeDef、shape/动画引用逻辑和完整贴图。
- 当前 MooGirl 眼睛 TypeDef 不使用 `altMaskPath`，按整张眼睛贴图和高光绘制；只有明确要做左右眼 mask 时，才新增 `FA/Eyes/Common/Female/*_L_*`、`*_R_*` 并把 `altMaskPath` 加回来。
- 多眼型/多嘴型随机变体必须保持素材矩阵完整；当前 `Normal` 到 `Normal7` 每套都覆盖现有动画会引用的 shape。
- 只有当空白头和各部件整体比例明显不贴合时，再考虑新增 `FaceAdjustmentDef`；常规新增表情不需要它。

## 贴图命名和回退

FA 的基础贴图路径格式是：

```text
{TypeDef.texPath}/{Gender}/{Shape}_{direction}.png
```

例如：

```text
Textures/FA/Mouth/Normal/Female/smile_south.png
Textures/FA/Mouth/Normal/Female/smile_east.png
```

FA 判断某个 shape 是否存在时只检查 `{Shape}_south`。如果 `_south` 存在，实际方向交给 RimWorld 的 `Graphic_Multi` 处理；通常至少应提供 `south` 和 `east`，需要正面/背面特化时再补 `north`、`west`。

单个 shape 的回退顺序是：

1. 当前 `ShapeDef.defName`
2. 当前 `ShapeDef.altShapeDef`
3. 该类别的全局 `normal` shape
4. 三者都没有贴图时，该层使用透明 dummy

这意味着缺贴图通常不会红字，但会静默变成 normal 或透明。新增动画前必须同时检查 ShapeDef 和贴图，不能只看 XML 是否能加载。

## 特殊层规则

`HeadControllerComp` 会加载基础头图，还会尝试加载 `{shape}_cover` 和 `{shape}_highlight`。MooGirl 的 blush 类表情必须走 head 基础层：`blush`、`lovinblush` 由 `headShapeDef` 引用，并放在 `Heads_Blank/Normal*/Female/{shape}_*`。不要把脸红作为 `emotionShapeDef` 接到动画里；Emotion 层在眼白之上，会把脸红盖到眼白上方。

`LidControllerComp` 有两种模式：

- 如果存在 `normal_cover_south`，FA 使用拆分模式：`*_bottom` 画眼皮底层，`*_cover` 作为头部皮肤遮罩。
- 否则走普通基础贴图模式。

MooGirl 当前眼皮使用拆分模式，因此每个眼皮 shape 至少应成对维护：

```text
normal_bottom_south/east.png
normal_cover_south/east.png
half_bottom_south/east.png
half_cover_south/east.png
close_bottom_south/east.png
close_cover_south/east.png
```

`MouthControllerComp` 会额外尝试 `{shape}_cover`。MooGirl 当前没有 mouth cover，普通 mouth 贴图即可。

`EyeballControllerComp` 如果 `altMaskPath` 为空，会画整张眼睛基础层和高光层：

```text
{shape}_south/east.png
{shape}_highlight_south/east.png
```

如果 `altMaskPath` 不为空，会改为左右眼分层，尝试读取：

```text
{altMaskPath}/{Gender}/{shape}_L_south.png
{altMaskPath}/{Gender}/{shape}_R_south.png
```

当前 `MooGirl_EyeNormal` 到 `MooGirl_EyeNormal7` 都不写 `altMaskPath`，因此不需要 `Textures/FA/Eyes/Common`。若未来重新启用左右眼 mask，必须一次性补齐所有会被引用的眼睛 shape 的 `*_L_*` 与 `*_R_*` mask，并重点验证普通眼、异色眼、`heart` / `MooGirl_heart` 眼和高光层。

爱心眼的语义是“爱心替代眼睛高光”，不是“整张瞳孔换成爱心”。因此 `Eyes/Normal*/Female/heart_*` 和 `MooGirl_heart_*` 基础层必须与同目录 `normal_*` 保持一致；真正的爱心图放在 `heart_highlight_*` 和 `MooGirl_heart_highlight_*`。动画需要显示爱心眼时引用 `eyeballShapeDef=heart`，FA 会用 normal 基础眼加 heart highlight 叠出效果。

## 动画调度

`FaceAnimationDef` 默认目标 Job 是 `ConstantJob`。若写了 `<targetJobs>`，就只在对应 Job 命中时进入该 Job 的动画池；若写空 `<targetJobs />`，通常表示该 Def 只作为子动画，被其他动画通过 `<faceAnimationDef>` 调用。

FA 为 Pawn 建立动画池时有一个种族过滤规则：只要当前种族存在任何专属 `FaceAnimationDef.raceName`，该 Pawn 的动画池就只使用该种族的专属动画，不再混用 FA 自带通用动画。MooGirl 已有完整 `raceName=MooGirl` 动画集，因此新增常驻或 Job 动画也必须写 `raceName=MooGirl`。

动画每帧合成规则：

- 动画按 `priority` 升序排列。
- 多个动画同 tick 命中时，后遍历的高 priority 动画会覆盖低 priority 动画的 shape。
- offset 不是简单覆盖，而是聚合后做平均；`Vector3.y` 在 FA 里作为权重修正参与计算，本 mod 现有动画基本保持 `y=0`。
- `targetMoodMin/Max`、`targetPainMin/Max`、`targetThoughtDefs` 每 60 tick 刷新一次过滤。
- 当前 Job 每 tick 检查；Job 改变时会重置该 Job 动画池。
- `applyWhenStandingOnly=true` 时，Pawn 正在移动就不会应用该动画。
- `roopIntervalMin/Max` 是每轮动画结束后到下次开始前的随机等待 tick。

`normal_MooGirl` 是整个动画系统的底座：它常驻、priority 为 0，并提供完整基础 shape。不要删除它，也不要给它加 mood、pain、thought 或 Job 限制。否则合成帧可能缺少基础 shape，部分控制器会出现空引用风险。

## 渲染流程

FA 通过 `DrawFaceGraphicsComp.CompRenderNodes()` 给 Pawn 头部追加一组 render node。`NLFacialAnimationMasterNode` 本身不画贴图，只在 `PreDraw` 中驱动 `FacialAnimationControllerComp.UpdateStatus()` 和 `UpdateAnimation()`；各部件 node 再按控制器当前 shape 取贴图。

FA 会在渲染前移除原版 head draw request，用 FA 的空白 head 和各部件重新拼脸。MooGirl 当前图层顺序大致是：

| 图层 | 用途 |
| --- | --- |
| 50 | 空白头底图；`blush`、`lovinblush` 也在这里作为 head 基础图绘制 |
| 50.25-50.875 | 眼皮底层、眼睛、眼睛高光、眼皮遮罩 |
| 55 | Skin 层，MooGirl 当前未启用 |
| 56-56.5 | 嘴与嘴 cover |
| 57-57.5 | 头 cover/highlight |
| 58 | 情绪叠层 |
| 59 | 眼皮附加层，例如眼泪 |
| 60 或 100 | 眉毛；由 FA 设置 `DrawBrowsAboveHat` 决定是否盖过帽子 |

多数部件在北向不绘制，头部底图仍可用 north 贴图。MooGirl 当前至少要保证 south/east 表现正确；north 主要影响空白头和情绪叠层。

## 当前 MooGirl 内容状态

已有贴图目录。除特别说明外，`Normal` 到 `Normal7` 每套都有同一组 shape：

| 目录 | 当前 shape |
| --- | --- |
| `Brows/Normal*/Female` | `normal`、`flat`、`angled`、`s-shaped` |
| `Eyes/Normal*/Female` | `normal`、`MooGirl_heart`、`heart`，并有 `normal_highlight`、`heart_highlight`、`MooGirl_heart_highlight` |
| `Lids/Normal*/Female` | `normal`、`half`、`close`，且都有 `bottom`/`cover` |
| `Mouth/Normal*/Female` | `normal`、`open`、`sad`、`smile`、`surprise`、`tight`、`puzzle`、`MooGirl_lovin`、`hot1/2`、`cold_tight1/2`、`pain1/2/3`、`hungry1/2`、`milking1..4` |
| `LidOptions/Normal*/Female` | `tear`、`MooGirl_cry` |
| `Emotions/Normal*/Female` | `blush`、`gloomy`、`lovinblush`、`heart`；其中 `blush/lovinblush` 只保留为旧素材来源，不应由动画直接引用 |
| `Heads_Blank/Normal*/Female` | `normal`、`blush`、`lovinblush`、`cold_cover`、`sweat_cover`、`heavy_sweat_cover`、`hot_cover`、`hot_highlight`、`hot_heavy_sweat_cover`、`hot_heavy_sweat_highlight` |

需要注意的未闭合项：

- `EmotionShapeDef heart` 有 shape 和贴图，但当前没有动画引用。
- `HeadShapeDef gloomy` 已声明，但没有对应 head 贴图，也没有动画引用；若将来引用，会回退到 `normal`。
- `EmotionShapeDef normal`、`LidOptionShapeDef normal` 来自 FA 基础 Def，本 mod 没有对应 normal 贴图；当前作为“透明无叠层”使用，这是可接受状态。
- `hot1/2` 嘴型与热脸叠层全套复用 1 号素材；`pain1/2/3` 嘴型按 1-4 号脸复用 1 号痛嘴、5-7 号脸复用 2 号痛嘴。
- `sad` 嘴型不只是低心情嘴，也表示严肃、紧绷但没有咬牙的表情。`AttackStatic_MooGirl` 和 `WaitCombat_MooGirl` 使用 `sad`；`tight` 是咬牙/憋力嘴，应保留给近战、劳动、挖矿、吃饭咀嚼等更用力的场景。
- 疼痛动画不需要哭泣层。`painHigh_MooGirl`、`painExtreme_MooGirl` 用痛嘴、眉毛、汗等表现即可；`tear`、`MooGirl_cry` 主要留给低心情、倒地或 Lovin 后段。

## 贴图引入矩阵

下表按当前 `Textures/FA` 实物贴图复检。`已接入` 表示已有 ShapeDef 且至少一个动画会引用；`待接线` 表示贴图或 ShapeDef 已存在，但缺少动画入口；`基础 Def` 表示由 FA 原 mod 提供同名 ShapeDef，本 mod 不需要重复声明。

| 类别 | shape | 当前状态 | 引入方式 |
| --- | --- | --- | --- |
| `Heads_Blank` | `normal` | 已接入 | `normal_MooGirl` 的 `headShapeDef=normal` 使用；这是空白头底图。 |
| `Heads_Blank` | `blush`、`lovinblush` | 已接入 | 高心情、社交、穿脱衣、Lovin 等动画通过 `headShapeDef` 引用；脸红画在 head 基础层，位于眼白下方。 |
| `Heads_Blank` | `cold`、`sweat`、`heavy_sweat`、`hot`、`hot_heavy_sweat` | 已接入 | 环境冷热、疼痛和挤奶相关动画会引用；head 层以 cover/highlight 叠层表现。 |
| `Heads_Blank` | `gloomy` | 仅声明 | 已有 MooGirl `HeadShapeDef`，但无贴图、无动画引用；补贴图后还要在动画帧里引用。 |
| `Brows` | `normal`、`flat`、`angled`、`s-shaped` | 已接入 | `normal` 来自 FA 基础 Def；其余来自 MooGirl ShapeDef。常驻、心情、战斗、工作、Lovin 等动画已引用。 |
| `Eyes` | `normal` | 已接入 | `normal_MooGirl` 和多个动画引用；当前使用整张眼睛贴图与高光，不启用左右眼 mask。 |
| `Eyes` | `heart` | 已接入 | MooGirl `EyeballShapeDef` 已声明，Lovin 动画已引用。基础层等同 `normal`，爱心绘制在 `heart_highlight`。普通穿衣、脱衣不使用爱心眼。 |
| `Eyes` | `MooGirl_heart` | 兼容保留 | MooGirl `EyeballShapeDef` 已声明；当前动画统一引用 `heart`。基础层同样等同 `normal`，爱心绘制在 `MooGirl_heart_highlight`。 |
| `Lids` | `normal`、`close` | 已接入 | FA 基础 Def 提供；常驻、眨眼、Lovin、工作等动画引用。 |
| `Lids` | `half` | 已接入 | MooGirl ShapeDef 提供；眨眼、战斗、心情、工作等动画引用。 |
| `Mouth` | `normal`、`open` | 已接入 | FA 基础 Def 提供；常驻、吃饭、Lovin、倒地等动画引用。 |
| `Mouth` | `sad`、`smile`、`surprise`、`tight`、`MooGirl_lovin` | 已接入 | MooGirl ShapeDef 提供；心情、疼痛、工作、Lovin、穿脱衣等动画引用。`sad` 也用于严肃嘴，例如 `AttackStatic` 和 `Wait_Combat`；`tight` 表示咬牙切齿或明显用力。 |
| `Mouth` | `hot1/2`、`cold_tight1/2`、`pain1/2/3`、`hungry1/2`、`milking1..4` | 已接入 | 环境冷热、疼痛、饥饿和挤奶动画引用。 |
| `Mouth` | `puzzle` | 待接线 | ShapeDef 和贴图都存在，但当前没有动画引用；适合接到困惑、科研失败、被脑洗等场景。 |
| `LidOptions` | `tear`、`MooGirl_cry` | 已接入 | 低心情、倒地和 Lovin 后段会引用；疼痛动画不使用哭泣层。`MooGirl_cry` 回退到 `normal`，缺贴图时会透明。 |
| `Emotions` | `gloomy` | 已接入 | 倒地、极低心情等动画引用。 |
| `Emotions` | `blush`、`lovinblush` | 保留不用 | 旧脸红素材仍保留，但动画不得直接引用；需要脸红时用 `Heads_Blank` 的同名 head shape。 |
| `Emotions` | `heart` | 待接线 | ShapeDef 和贴图都存在，但当前没有动画引用；要作为爱心叠层使用，需要新增或调整动画帧。 |

因此，当前所有新增/已有贴图都已被分类考虑：

- 已接入贴图会被现有动画自然加载，不需要额外 XML。
- `Mouth/puzzle` 与 `Emotions/heart` 已有 ShapeDef 和贴图，只缺动画帧引用。
- `Head/gloomy` 已有 ShapeDef 但没有贴图；要么继续作为预留，要么补贴图并新增动画引用。
- 眼睛类贴图若重新启用 `altMaskPath`，必须同时补 `FA/Eyes/Common` 左右眼 mask。

## 推荐更新决策

当前眼睛路径已经采用整张眼睛贴图模式，不写 `altMaskPath`。不要长期保持“声明 mask 路径但目录不存在”的状态，因为它会进入左右眼分层逻辑，可能导致异色眼、高光和整张眼睛重复叠画不符合预期。

`Emotions/Normal*/Female/heart_*` 已经有资源，但当前没有动画使用。若要启用爱心情绪，优先新增明确场景，例如高心情、社交、穿脱束具、Lovin 或被喂奶。

`HeadShapeDef gloomy` 目前只是预留。若预计新增头部表情贴图，需要补 `Heads_Blank/Normal*/Female/gloomy_*`，再在 mood、pain 或特定 Job 动画中引用。若短期不做头部变形，可以保留声明但不要在动画中引用它。

脸红类表情必须保持在眼白下方。新增高兴、害羞、Lovin、穿脱衣等动画时，使用 `headShapeDef=blush` 或 `headShapeDef=lovinblush`，不要使用 `emotionShapeDef=blush/lovinblush`。如果要调整脸红图案，先改 `Heads_Blank/Normal*/Female/{shape}_*`；`Emotions` 下的同名图只作为旧素材来源保留。

嘴型语义不要按文件名过度收窄：`sad` 同时代表低心情和严肃嘴，适合静态攻击、警戒、沉默紧张等“不开口、不咬牙”的状态；`tight` 才是咬牙切齿或明显用力，适合近战、挖矿、劳动、咀嚼等场景。`Wait_Combat` 需要警戒感但不能咬牙切齿，也不能全程 `lidShapeDef=half`；保留 `angled` 眉毛和扫视，眼皮以 `normal` 为主，只允许短暂半眯作为凝视变化。

普通工作和思考类 Job 不要长期使用 `tight`。`DoBill`、`Research` 这类状态优先用 `normal`、`puzzle`、低视线和轻微眉形变化；`tight` 保留给近战、挖矿、倒地挣扎、进食咀嚼等更用力的场景。普通 `Wear`、`RemoveApparel` 只使用轻度 `blush`，不要触发爱心眼或 `lovinblush`；这些更强表情留给 Lovin 或明确的特殊互动。

本 mod 自有 Job 目前没有专属 FA 动画，例如挤奶、喝奶、喂奶、牵引、骑乘、脑洗/束具相关交互。后续若补这些动画，应按玩家可见频率排序：产奶/喂奶与束具互动优先于较少见的任务事件动画。

## 新增 FA 内容流程

1. 先决定新增的是贴图 shape、动画 Def，还是二者都要。
2. 新 shape 必须确认对应 `*ShapeDef` 是否已经存在。FA 基础 Def 已提供 `normal`、`open`、`close`、`blush` 等通用 shape，不要重复定义同名 Def。
3. 贴图放在 `Textures/FA/{Category}/{Normal变体}/Female/`，文件名和 shape 精确一致；多 TypeDef 变体要覆盖每个会被动画引用的 shape。
4. 新动画放在 `1.6/FacialAnimation/Defs/AnimationDefs/MooGirl/` 下，写 `raceName=MooGirl`。
5. 常驻情绪、疼痛、思想动画留在 `Constant`；Job 触发动画放在 `ForJobs`。
6. 新常驻动画要检查 priority，不要意外盖掉疼痛、倒地、穿脱衣等高优先级状态。
7. 如果新增的是子动画，写空 `<targetJobs />`，并只通过 `<faceAnimationDef>` 引用。
8. 如果新增眼睛 shape，当前默认补整张眼睛贴图和高光；只有重新启用 `altMaskPath` 时才补 `FA/Eyes/Common` 左右眼 mask。
9. 新增脸红时用 head shape，不用 emotion shape；验证眼白应覆盖在脸红上方。
10. 游戏内验证至少生成一名普通成年女性 MooGirl，检查南/东朝向、眨眼、心情低落、疼痛、倒地、穿衣/脱衣、Lovin、吃饭和战斗待机。

新增贴图是否已经“引入”，按这个顺序判断：

1. 是否有对应 `*TypeDef.texPath` 指向该目录；没有就新增或调整 TypeDef。
2. 是否有对应 `*ShapeDef`；没有就新增 ShapeDef，或确认使用 FA 基础 Def。
3. 是否至少有 `{shape}_south`；没有就不会通过 FA 存在性检查。
4. 是否需要特殊附属贴图，例如眼皮 `*_bottom`/`*_cover`、眼睛 `*_highlight`、眼睛左右 mask、嘴/头 cover。
5. 是否有 `FaceAnimationDef` 帧引用该 shape；没有引用时贴图只会被预加载/回退逻辑看到，不会在游戏中主动出现。
6. 是否被更高 priority 动画长期覆盖；若被覆盖，需要调整 priority、触发条件或动画时长。

## 验证重点

FA 改动后的 fresh log 不应出现 MooGirl 或 FacialAnimation 相关红字。若游戏内看不到动画，先排除这些设置因素：

- FA 设置中女性绘制被关闭。
- FA 设置中该 race/xenotype 被加入忽略列表。
- FA 设置中未启用非殖民者动画，而测试对象不是殖民者。
- Pawn 当前死亡、腐烂、隐形或渲染处于不绘制头部的模式。
- 测试 Pawn 不是 `Female`，但当前只有 `Female` 贴图目录。

截图或肉眼验证时至少看：

- `normal_MooGirl` 基础脸是否正常。
- 眨眼时 `half`、`close` 眼皮是否遮盖眼睛。
- `blush`、`lovinblush` 是否位于眼白下方，没有盖住眼白。
- `heart` 眼睛是否保持普通瞳孔，只把爱心画在高光层。
- `MooGirl_lovin` 嘴型是否从 `open` 回退正确。
- `tear` 与 `MooGirl_cry` 是否只在低心情、倒地、Lovin 后段等状态出现。
- 疼痛动画是否没有哭泣层；普通穿衣/脱衣是否没有爱心眼。
- `altMaskPath` 决策前后，异色眼和高光是否符合预期。
