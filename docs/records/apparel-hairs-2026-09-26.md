# 9.26 贴图实装：金色比基尼套装、白雪公主套装与 Hairs10 发型（2026-09-26）

## 版本与范围

- 版本：0926 发布版（基于 0.6.7 之后的更新工作区，包 `DevData/Releases/雪牛娘0926.zip`）。
- 素材来源：`TMP/9.26贴图`（23 张 PNG 与文案；`_thumb` 缩略图与 `_hair_compare.jpg` 为画师参考件，游戏不引用，保留在 TMP）。
- 内容：
  1. 实装 4 件新装备：金色比基尼、金色乳胶皮靴、白雪公主骑士铠、白雪公主王冠；全部取消染色（`MugirlFixedColor`），并补齐护甲数值。
  2. 实装新发型 Hairs10，区分前发与后发（南向后发层在头部之后绘制）。
  3. 新增比基尼怀孕变体贴图切换机制（画师提供了 `HuaiYuHY` 怀孕三向贴图）。

## 贴图安装映射

| 源（TMP/9.26贴图） | 目标（相对 `Textures/`） |
| --- | --- |
| `XN_JingSeBiJiNi.png` + `_east/_north/_south` | `Things/Apparel/Mugirl_GoldBikini/Mugirl_GoldBikini.png` + `_Female_*`（OnSkin 层补 `_Female` 段） |
| `XN_JingSeBiJiNi_HuaiYuHY_*` | `Things/Apparel/Mugirl_GoldBikini/Mugirl_GoldBikini_Pregnant_Female_*`（怀孕变体） |
| `XN_JingSeBiJiNi_ChangTongXue.png` + `_*` | `Things/Apparel/Mugirl_GoldLatexBoots/…` + `_Female_*` |
| `XNN_BaiXueGongZhuZJ.png` + `_Female_*` | `Things/Apparel/Mugirl_SnowPrincessArmor/…` + `_Female_*`（源图已带 `Female` 段） |
| `XNN_BaiXueGongZhuZJTK.png` + `_*` | `Things/Apparel/Mugirl_SnowPrincessCrown/…`（Overhead 层不带身体类型段） |
| `XNN_Hairs10_east/_north` | `Mugirl/Hair/Mugirl_Hairs10_*`（前发） |
| `XNN_Hairs10_south.png`（555×555） | `Mugirl/Hair/Mugirl_Hairs10_south.png`（LANCZOS 缩放到 512×512；缩放后内容上缘 y≈143 与东西向 142/145 对齐，确认画师按比例绘制） |
| `XNN_Hairs10F_south.png` | `Mugirl/Hair/Mugirl_Hairs10Back_south.png`（后发层，命名对齐 `Mugirl_LongCascadeBack_south`） |

内容对齐核对：比基尼南向 alpha 边界 (185,201,329,378) 与高叉毛衣 (180,185,333,380)、警靴与长靴南向边界几乎重合，画布规范一致。

## 新装备 Def（`1.6/Defs/Apparel/Apparel_0926.xml`）

| Def | 继承 | 层/部位 | 护甲数值 |
| --- | --- | --- | --- |
| `Mugirl_GoldBikini` 金色比基尼 | `Mugirl_0920CasualBase` | OnSkin，Torso+Legs | `StuffEffectMultiplierArmor 0.08`（布料极少的泳装，介于便服 0.12 与轻纱 0.05 之间）；HP 60 |
| `Mugirl_GoldLatexBoots` 金色乳胶皮靴 | `Mugirl_0920CasualBase` | Mugirl_Stocking，Legs | `StuffEffectMultiplierArmor 0.18`（时尚皮靴档，介于警靴 0.25 与便服 0.12 之间）；HP 110 |
| `Mugirl_SnowPrincessArmor` 白雪公主骑士铠 | `Mugirl_ArmorSmithableBase`，中世纪，钢 120+金 20 | Shell，躯干/颈/肩/臂/腿 | 固定三防 Sharp 0.72 / Blunt 0.38 / Heat 0.28（仪式板甲档：低于白色骑士坐骑甲 0.95/0.5/0.25，布衬使热防略高）；Insulation_Cold 18；MoveSpeed -0.25；研究 `Mugirl_CombatAttire`+`PlateArmor`，Crafting 8 |
| `Mugirl_SnowPrincessCrown` 白雪公主王冠 | `Mugirl_HeadBase`，金 40（无材料） | Overhead，HeadAttackTool | 固定小值 Sharp 0.08 / Blunt 0.04 / Heat 0.02（金属头饰，介于墨镜 0.04 与警帽材料档 0.12 之间）；SocialImpact +0.15 |

- 护甲参照延续 2026-09-26《头饰护甲补齐》的档位说明；品质系数照常作用于三防。
- 4 件全部打 `MugirlFixedColor`，造型台不可染色、材料色不影响外观；骑士铠沿用战斗服饰收购名单惯例追加 `Mugirl_CombatApparelBuyOnly`（作战补给商可收购）并带 `Mugirl_Armor`。
- 比基尼额外带新标签 `MugirlPregnantSwap`（见下）。
- 中文文案按画师《文案.txt》原文（白雪公主骑士铠、独特的王冠两段），英文描述同步意译；翻译在 `Languages/ChineseSimplified/DefInjected/ThingDef/Apparel_0926.xml`。

## 怀孕变体贴图切换（新 C#：`Features/Misc/Harmony_PregnantApparelSwap.cs`）

- 原版 `ApparelGraphicRecordGetter.TryGetGraphicApparel` 按 `路径_身体类型_朝向` 解析穿着贴图，没有怀孕概念；因此沿用固色标签模式：`MugirlPregnantSwap` 标签 + `Apparel.WornGraphicPath` getter 的 Postfix，在穿戴者怀孕时（`HediffDefOf.PregnantHuman`）给路径追加 `_Pregnant`。该 getter 在原版中仅被 `TryGetGraphicApparel` 消费（1.6 反编译核实），追加后缀自然组合出 `..._Pregnant_Female_east` 等文件名。
- 刷新时机：图形重建时才重新读取路径，故用两个 Postfix 保证状态切换即时可见——`Hediff.PostAdd`（受孕/移植胚胎）与 `Pawn_HealthTracker.RemoveHediff`（分娩、流产、终止妊娠均经 `PregnancyUtility` 走此路径，1.6 反编译核实）。两者都立即 `SetAllGraphicsDirty()`；雪牛娘额外走既有 `PawnRenderingRefreshUtility.NotifyPawnChanged` 延后兜底，非雪牛娘穿戴者（如普通人类女性）只依赖立即脏化。
- 三个补丁均已登记 `MugirlPatchInfo`（Misc 模块，Low 风险）。雕像使用同一 getter，怀孕雕像会显示变体贴图，视为可接受。
- 贴图缓存无跨档残留：两种变体都走 `GraphicDatabase` 按路径缓存，进程内共享。

## Hairs10 前后发

- `HairDef.Mugirl_Hairs10`（雪牛娘 头巾长发，category `Mugirl_Hair`）：主图三向为前发；北向原图已含完整后发，与 LongCascade 同一约定。
- 后发层泛化：`CompProperties_LongCascadeBackHair` 新增 `hairDef`（XML 直接交叉引用）、`southPath`、`sidePath` 字段；`ResolveReferences` 中未配置时回退 LongCascade 默认值（行为不变），显式留空 `sidePath` 表示仅南向绘制。渲染节点属性改用新 `PawnRenderNodeProperties_LongCascadeBackHair` 携带路径与目标发型，Worker 按节点属性比较 `pawn.story.hairDef`，不再硬编码。
- 后续层级修复：按用户确认的效果，后发继续使用 `-1`，保持身体与胸前服装遮挡；修女头饰的南向白色垂纱从 `-1` 降为 `-5`，留出网格抬升与深度缓冲余量。修女前方帽边、脸部与刘海保持原来的层级。没有改动贴图、画布大小或前后发偏移。
- `Mugirl_Race.xml` comps 新增一条 Hairs10 条目（`southPath Mugirl/Hair/Mugirl_Hairs10Back_south`，`sidePath` 留空——画师未提供东西向后发，侧向马尾由前发贴图承担）；原有 LongCascade 空条目保持原样。
- 中文发型名追加在 `DefInjected/HairDef/Hairs_0920.xml`（现有发型翻译文件）。

## 验证

- 修改 C# 后执行完整 Release 构建并通过静态验证（含资源检查）：退出码 0，日志 `TMP/phase6-static-0926-apparel-hairs.log`；贴图 1620 张（新增 23 张）、穿着贴图路径检查 76 条通过；新源文件曾漏登记 csproj，由静态验证"csproj source references"拦截后补上。
- 去白边（透明像素归一）：`Normalize-TransparentPixels.py` 全量扫描发现 `Textures/` 31 个文件、约 547 万个透明像素 RGB 非黑（含本次新装 23 张与 8 张早前漏跑的武器、UI、建造、头纱背层等贴图），以及 FA 组 393 个文件。为脚本新增 `--only <前缀>` 过滤参数（保持单入口）后，仅对 `Textures/` 执行 `--apply`：31 个文件归一完成，alpha 与可见像素逐文件校验不变，复扫为 0；备份在 `DevData/Backups/TransparentBlack-20260926-155408-863708/OriginalTextures`。归一后重跑静态验证通过，证据日志已刷新。
- 未做游戏内视觉验收（fixed Player.log 缺失）：需在游戏内确认 4 件装备外观与固色、比基尼怀孕变体随孕程切换/分娩复原、Hairs10 前后发在南/东/北向的层次与发色联动。

### 后续：修女头饰与后发层级确认

- 最终按用户确认图保留胸前服装遮挡：后发 `-1`、修女白色垂纱 `-5`、头部 `50`。原图和前发均未修改。
- 隔离游戏使用 Harmony、全部 DLC、HAR、Mugirl；Hairs10 与 LongCascade 的四朝向、修女头饰/眼罩组合、换发型及头盔隐藏后发检查共 **280 PASS / 0 FAIL**。实际渲染矩阵确认后发在垂纱上方，层间深度差大于 `0.001`。
- `portrait-28-2.png` 与用户最终确认的图片逐像素一致；降低垂纱不改变身体对后发的遮挡。该图为 Hairs10 南向、修女服/头饰/眼罩组合。
- 完整 Release 构建、Phase 6 静态验证及 fresh `Player.log` 扫描通过。后发生产源码保持工作区基线；正式 DLL SHA256 为 `37B2180F8CCACB84D77D11BD12F43DE37DE0F427FEDA0BD782647A98D06F925E`，与本轮基线一致，试验中的整体提前层级已撤销。
- RimSort 目录环路审计：`affected_candidates=0`、`scan_errors=0`；证据归档于 `DevData/Evidence/Hairs10VeilLayer-20260926`。本轮没有启用 FA，也没有操作用户存档；上方初次发布的其他待验项目不由本轮替代。

## 发布打包（0926）

- `New-WorkshopPackage.ps1`（含 Release 构建，MSBuild 增量复用 15:44 的当前 DLL）生成 `TMP/WorkshopPackage/MugirlRace`：1961 个文件，顶层 `1.6/About/Bio_1.6/Odyssey_1.6/Versions/Sounds/Textures/LoadFolders.xml`，违禁文件（Source/csproj/pdb/docs/TMP 等）0。
- ZIP 按日期命名惯例输出 `DevData/Releases/雪牛娘0926.zip`（顶层目录 `雪牛娘0926`，35 MB），与展开副本逐文件 SHA256 比对 1961/1961 一致；`git diff --check` 干净（仅 LF/CRLF 提示）。
- 发布限制：清单第 3 条 fresh `Player.log` 游戏内验证仍未完成（与上文视觉验收同批待做）；发布后如发现需修内容，重打时沿用同名脚本与命名。

## 已知限制与后续观察项

- 非 Female 身体类型没有穿着贴图（与兔女郎套、警察套之外的既有单品一致）；比基尼/长靴/骑士铠仅提供 `_Female_*`。
- 初次实装漏登记四件新装的 HAR 种族服装名单，且王冠未加 `Mugirl_HideHorns`。本日后续修复已补齐四件名单，并让王冠隐藏牛角，详见[装备、乳品与发电机修正](equipment-dairy-generator-fixes-2026-09-26.md)。
- 比基尼怀孕变体由 `PregnantHuman` Hediff 驱动，仅覆盖人形怀孕；动物与特殊怀孕类型不会触发。
- `TMP/9.26贴图` 中的 `*_thumb.png` 与 `_hair_compare.jpg`、`_overview.jpg` 为参考件，未实装也不打包。
- FA 透明像素归一**刻意未执行**：`1.6/FacialAnimation/Textures` 有 393 个文件含非黑透明像素，归一会作废 README 记录的"与 `TMP/FacialAnimation_0921_Talos修改版` 逐字节一致"基线；其透明 RGB 属于 Talos 原包内容，且脸红 cover 的 RGB 本就是受保护的着色器数据。如需归一 FA，须连同基线文档一起更新并重做 FA 对照。
