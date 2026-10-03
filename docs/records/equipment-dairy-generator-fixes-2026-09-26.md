# 装备平衡、穿戴与乳品发电机修正（2026-09-26）

## 范围与工作区基线

按玩家本轮五项反馈修改当前 0926 工作区。修改前保留已有源码、DLL、Def 与贴图改动；状态、完整 tracked 差异、涉及文件副本及 DLL 哈希位于 `TMP/EquipmentFix-20260926/`。静态基线通过（`baseline-validation.log`）。rimsearcher 本地数据库尚未收录 0920/0926 新装备，不能作为本轮当前 Def 加载结果。

## 装备数值

调整主甲，让工业/Spacer 甲略降、高阶神经战甲具有明确升级价值。下表是普通品质的 Def 基值；材料甲仍叠加材料防护，品质仍按原版规则计算。

| 主甲 | 调整前 → 调整后 |
| --- | --- |
| 动力甲 `Mugirl_PowerArmor` | 耐久 500 → 450；材料防护系数 1.25 → 1.15；锐器/钝器基础防护 0.50/0.30 → 0.45/0.27；负重 +50 → +40；近战冷却 -0.10 → -0.08；移速 +0.8 → +0.6 |
| 战士全防护服 `Mugirl_WarriorCombatArmor` | 耐久 400 → 360；材料防护系数 1.15 → 1.05；锐器/钝器/热基础防护 0.20/0.30/0.40 → 0.18/0.27/0.36；负重 +50 → +40；远程冷却 -0.10 → -0.08 |
| 神经接入战斗护甲 `Mugirl_NeuralCombatArmor` | 耐久 500 → 550；锐器/钝器/热防护 1.35/0.65/0.75 → 1.50/0.75/0.85；负重 +35 → +60；移速 +0.6 → +0.8；近战伤害 +0.25 → +0.30；近战冷却 -0.20 → -0.22 |

配套头盔、成本、研究、耐温与偏转概率未调整。小削防护幅度约为一成，神经甲防护提高约 11%～15%。

## 穿戴与角

- HAR 开启 `onlyUseRaceRestrictedApparel`，而 0926 四件新服装未进入 `apparelList`，导致种族限制拒绝穿戴。补齐金色比基尼、金色乳胶皮靴、白雪公主骑士铠和白雪公主王冠；继续使用种族专属名单。
- 王冠增加 `Mugirl_HideHorns`，复用六个牛角附件已有的 `hiddenUnderApparelTag` 条件。卸下王冠时仍走 HAR 既有刷新和显示规则。
- `StylingStartupValidation.cs` 增加四件新装及 HAR `CanWear` 检查，并将隐藏牛角名单更新为三件既有头盔加王冠。

## 精制乳品

陈年奶酪、牛乳蛋糕、奶粉、奶片和奶宝塔糖五个配方仅需 `Mugirl_AdvancedDairy`。去掉配方层 `AirConditioning` / `DrugProduction`，研究项目仍以前置乳品加工解锁，基础乳品链不变。奶宝塔糖仍在制药台制作；此修改取消配方研究联动，不改变工作台本身的建造解锁。中文翻译、英文 Def 说明和[研究树设计](../features/research-tree-design.md)已同步。

## 发电机绘制

贴图为 512×512，alpha 可见边界为 `(107,9)-(395,403)`，可见宽高 288×394。原 `(2,2)` 绘制尺寸仅显示约 1.125×1.539 格。首次改为 `(3.56,2.6)` 虽把可见边界铺成 2×2，但横纵比例不同，导致原图被拉宽压扁；这属于实现错误，前一轮边界对齐验证没有覆盖原图比例。

玩家后续要求保留比例，并允许贴图上方略微超出占地。现改为 `(3,3)` 等比例放大，偏移 `(0.03,0,-0.139)`：横向居中、可见底边贴齐占地底边，可见宽高约 1.688×2.309 格，上缘约超出 0.308 格。PNG、建筑实际占地、发电量和耗奶量不变。

## 本轮验证

- 修改前与修改后 `Invoke-Phase6StaticValidation.ps1 -SkipBuild` 均通过，包含 XML、Def 引用、翻译、补丁元数据、源码登记与资源路径检查。完整 Release 源码和外观验证驱动另行构建到外部隔离目录。
- 冷启动加载当前 Def：**222 PASS / 0 FAIL**。四件新装的成人穿戴图和 HAR `CanWear` 均通过，牛角隐藏名单为原三件头盔加公主王冠。旧测试驱动调用后发层时未传新增参数，已改为从种族 comp 列表选择 LongCascade 条目后传入，不修改正式后发实现。
- 临时 quicktest 地图：**72 PASS / 0 FAIL**。检查实际穿戴和四向图形，戴王冠时可见原生角节点为 0，卸下后恢复为 2；逐帧导出人物近景及地图截图，视觉确认金色比基尼、长靴、公主骑士铠和王冠均正确显示，发电机实体边界与占地框对齐。王冠贴图自身的金色装饰保持原稿；隐藏的是雪牛娘原生牛角。
- 地图中的五种精制乳品配方仅解析到 `Mugirl_AdvancedDairy`；运行时导出的三件甲 `statBases` / `equippedStatOffsets` 与上表一致。
- 初次临时地图驱动误要求所有体型的六个角附件同时创建节点，产生 8 条节点数量失败；其余 **64 PASS**，包括王冠穿脱显示、装备贴图、研究前置和发电机。成人实际只创建两个角节点，修正驱动断言后换全新输出目录复测为 **72 PASS / 0 FAIL**。初次输出保留在 `VisualInitial`，未改写旧结果。
- 冷启动与最终地图的 fresh `Player.log` 扫描均为 **no suspicious lines**。配置为 Harmony、Core、全 DLC、HAR、Mugirl；未覆盖第三方可选外观模组或已有存档的逐个组合。
- RimSort 全实例目录审计：`affected_candidates=0`、`scan_errors=0`、`cycle_components=0`。测试游戏仅放在 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/EquipmentValidation-gear-dairy-20260926-a-Game/`，不挂回模组。
- 正式 `1.6/Assemblies/MugirlRace.dll` SHA256 始终为 `37B2180F8CCACB84D77D11BD12F43DE37DE0F427FEDA0BD782647A98D06F925E`，与工作区基线一致。验证驱动只在隔离 DLL 中；所有验证进程已退出。

证据归档：`DevData/Evidence/EquipmentDairyGeneratorFix-20260926/`。`Startup` 保存冷启动日志与检查，`VisualFinal` 保存最终地图日志、72 项检查、运行时甲数值与 16 张截图，`VisualInitial` 保留初次驱动误判结果；目录审计、构建和静态检查日志位于归档根目录。本轮基线及修改前文件副本仍在 `TMP/EquipmentFix-20260926/`。

### 发电机等比例修正复测

上述 72 项及边界对齐截图是首次实现的历史结果，未证明原图比例正确。收到玩家“压扁了”及“上方可以略微超出占地格”的反馈后，仅修正发电机 XML 的绘制尺寸和偏移，并换独立输出目录复测。

- 当前采用 `(3,3)` 等比例缩放，保留原贴图宽高比例；可见底边位于占地中心相对 Z=-1.0003，上边 Z=1.3083，即底边对齐、上方超出约 0.31 格。
- 新增横纵绘制比例相等的检查；实际地图 **73 PASS / 0 FAIL**，fresh `Player.log` 扫描为 **no suspicious lines**。已目视核对新地图截图，原本较高的机体不再被压扁，上缘略超出选择框。
- 静态验证通过；目录审计 `affected_candidates=0`、`scan_errors=0`、`cycle_components=0`。原 PNG 与正式 DLL 的 SHA256 均与本次修正前一致。
- 修正前基线与文件副本：`TMP/GeneratorAspectFix-20260926/`。新截图、日志、73 项检查与几何边界数据归档至 `DevData/Evidence/GeneratorAspectFix-20260926/`；外部测试输出目录为 `EquipmentValidation-gear-dairy-20260926-a-Game/Mods/Mugirl/TMP/EquipmentAspect-gear-dairy-20260926-c`。
