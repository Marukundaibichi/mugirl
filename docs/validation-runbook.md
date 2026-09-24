# 验证运行手册

静态脚本只能证明结构、引用和常见风险没有明显问题，不能替代 RimWorld 实际加载和游玩验证。涉及交互、渲染、事件、读档或第三方 mod 的改动，都应按本文档做 fresh log 验证。

## 测试游戏目录必须位于模组扫描范围之外

本机曾因测试游戏通过 Junction 指回模组而触发 RimSort #2450。不要在任何 Mods、Workshop 或 Data 扫描目录下创建带联接的测试游戏；`.gitignore` 和 `LoadFolders.xml` 无法排除 RimSort 的目录大小统计。详情见 [事故与应急处理记录](rimsort-directory-cycles-2026-09-18.md)。

`Start-CorporateValidation.ps1` 的测试游戏现在输出到 `%LOCALAPPDATA%\RimWorldModTests\Mugirl\CorporateValidation-<RunName>-Game`。普通配置和日志可以留在仓库 `TMP`，但不要把外部测试环境再用联接挂回仓库。改动测试脚本后须运行 `Audit-RimSortDirectoryCycles.py`，确认无环路且扫描无错误。

专项脚本与资源工具统一列在本文后半部分。本轮输出写入 `TMP`，需追溯的已完成测试归档到 `DevData/Evidence`，原图和 DLL 回退备份放在 `DevData/Backups`。历史记录中的证据路径已按 2026-09-21 归档位置更新；日志正文内的旧绝对路径可查 `DevData/Evidence/MaintenanceCleanup-20260921/moves.json`。清理前遵守[目录与清理约定](../README.md)。

## 静态验证

完整验证：

```powershell
.\docs\tools\Invoke-Phase6StaticValidation.ps1
```

已有 Release DLL 且只想快速复查：

```powershell
.\docs\tools\Invoke-Phase6StaticValidation.ps1 -SkipBuild
```

日志扫描：

```powershell
.\docs\tools\Invoke-PlayerLogScan.ps1
```

上一轮日志：

```powershell
.\docs\tools\Invoke-PlayerLogScan.ps1 -Previous
```

## 游戏配置模板

生成验证用 `ModsConfig.xml` 模板：

```powershell
.\docs\tools\New-GameValidationConfigs.ps1
```

默认输出：

```text
TMP\GameValidationConfigs
```

使用方式：先备份当前 `ModsConfig.xml`，再把对应模板复制为当前配置。验证完成后恢复用户原配置。`TMP` 用于普通临时输出；清理前确认待删内容不含目录联接，并保留仍需追溯的验证日志。

## 日志和配置路径

```text
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player-prev.log
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml
```

## Fresh Log 记录模板

每次手动验证后追加一条记录，避免只留下“已跑完”但缺少可复核证据。

```text
日期时间：
验证人：
配置模板：
RimWorld 版本：
Mugirl DLL 时间戳：
Player.log 路径：
Player.log LastWriteTime：
日志扫描命令：
日志扫描结果：
进入主菜单：是/否
新档进入地图：是/否
保存读档：是/否
红字：无/有，摘要：
黄字：无/有，摘要：
Mugirl 相关异常：无/有，摘要：
外部 mod 噪音：无/有，摘要：
本轮功能点：
结论：通过/不通过/需复查
后续处理：
```

## 建议验证组合

- 最小必需：Harmony、Core、HAR、Mugirl。
- 全 DLC：Royalty、Ideology、Biotech、Anomaly、Odyssey 全开。
- Facial Animation：按 `features/facial-animation-guide.md` 检查头部 comp、表情动画、贴图回退和渲染日志。重点看 `normal_Mugirl`、眨眼、疼痛不哭泣、倒地、穿脱衣不用爱心眼、Lovin、`blush/lovinblush` 位于眼白下方、`AttackStatic`/`Wait_Combat` 使用 `sad` 严肃嘴、`Wait_Combat` 不全程半眯、整张眼睛贴图与高光；只有重新启用左右眼 mask 时才检查 `FA/Eyes/Common`。
- Search and Destroy：检查雪牛娘闲置战斗行为树不红字。
- VCookE：检查奶酪压制机工序是否追加且不重复。
- 全集成：所有上面组合一起加载。

## 功能抽查点

- 新建地图、保存、读档。
- 生成雪牛娘、成长阶段、外观、头发、角、尾巴。
- 产奶、挤奶、喝奶、奶制食物效果。
- 束具穿戴、卸下、破解、Hediff 清理、Gizmo 点击。
- 牵引、被牵引、牵到建筑、混合牵引普通 pawn。
- 骑乘、下骑、骑乘近战、装备绘制。
- 雪牛投掷（Biotech）：确认等于与超过 `CarryingCapacity` 的目标分别可举起与被拒绝；整堆物品按整堆重量判定。分别尝试孤立墙体/山体，以及占用格或四边邻格连接墙、家具、墙灯或地下电缆的 `Building` 目标；所有未显式定义 `Mass` 的 `Building` 至少按完整最大耐久度 × 10 计算，同时保留占地面积、建造材料造成的更高重量。`Mineable` 即使有较小的显式 `Mass`，也不能低于完整最大耐久度 × 10。原版墙体基础耐久 300 对应 3000 kg，具体材质按实际最大耐久计算；花岗岩山体 9000 kg。550 kg 搬运上限均应拒绝，提升到足够高后才允许举起；打残目标不能降低门槛。显式有 `Mass` 的花岗岩岩块 `ChunkGranite` 仍按原规则判定。确认举起、落点瞄准、命中、放下、中断及存读档后目标不丢失、不复制。
- 投掷飞行视觉：在正常、快速和暂停状态观察位置与旋转是否连续，确认空中旋转保持每游戏 tick 42°，飞行途中能转多圈；尤其检查物体有多个渲染帧但无新游戏 tick 时仍平滑前进和旋转。镜头移到落点、施术者离开视野时飞物不应消失；读档后继续飞行不应跳到终点。显示补间不能提前穿透屋顶或触发命中，落地仍在预定游戏 tick 发生。
- 雪牛流星大灌篮（Biotech）：普通模式不显示能力，也不能从残留命令触发；开发者模式下只接受仍有头的人形 `Pawn`。确认摘头后身体留在原处；对照目标摘头前的头部和头发，检查手中篮球的实际绘制尺寸一致，拍球、起跳、灌篮和落点效果正常。篮筐附近没有可跳落点时，应在选目标阶段拒绝，不能出现原地起落后头在远处爆裂。起跳和上升阶段检查头颅贴近施术者手部，与 `PawnFlyer` 同步移动，不应提前脱手或消失。在正常、快速和暂停状态观察空中头部的位置与旋转：空中旋转保持每游戏 tick 46°，飞行途中能转多圈，同一个游戏 tick 内的多个渲染帧应平滑补间；镜头移到头部、控制器离开视野时，头部仍应显示。施术者越过最高点开始下坠时，完整头颅应连续地从手边转为加速下扣，边界处不能先悬停或回跳；下扣明显快于雪牛娘，头的触地点应位于空中雪牛娘面朝方向的前方、画面下方，并在她落地前砸地。下扣期间没有提前爆血或碎头，触地时音效、血液与碎裂在同一瞬间出现，头应立刻消失，不弹跳。分别观察摘头瞬间、拍球触地和灌篮触地碎裂时的血液飞溅，`CanBleed` 为 false 的目标不应产生血液或血沫。对无头、非人形、目标死亡/离图及施术者中断分别检查拒绝提示和清理，保存读档后无残留控制器或异常。
- 事件和任务：开局坠舱、逃奴加入、野人事件、快递事件。

## 专项验证

| 入口 | 范围 |
| --- | --- |
| `Test-BootstrapStartup.ps1` | 启动期间日志、翻译与补丁失败处理 |
| `Test-MilkCache.ps1`、`Test-MilkingFacing.ps1` | 产奶缓存与挤奶朝向 |
| `Test-RopingIndex.ps1` | 牵引索引 |
| `Test-RenderingRefresh.ps1`、`Test-StylingStationRefresh.ps1` | 渲染缓存与造型台刷新 |
| `Test-BodyAccessoryLifecycle.ps1` | 身体附件生命周期 |
| `Test-MeleeAnimationScope.ps1` | 近战动画兼容边界 |
| `Start-CorporateValidation.ps1` | 巨企隔离游戏测试，参数见[巨企验证](features/giant-corporation-v1-validation.md) |
| `Start-LanceRuntimeValidation.ps1` | 骑枪破墙、连续运动模糊及落地回收；自动构建外部隔离 DLL，见[骑枪专题](features/lance-charge.md) |
| `Start-ThrowRuntimeValidation.ps1 -RunName <名称>` | 投掷重量/连接判定、墙体飞行落地与开发者灌篮；自动构建外部隔离 DLL，结果写入隔离测试目录的 `throw-checks.txt` 和 `throw-complete.txt` |
| `Start-StylingStartupValidation.ps1` | 外观及造型台冷启动验证 |
| `Start-PregnancyMoodValidation.ps1` | 怀孕心情隔离测试；`-WithoutBiotech` 检查无 Biotech 配置 |

`docs/tools` 中的 `.cs` 文件是相应验证驱动或检查实现，包括 `Corporate*`、`LanceRuntimeValidation`、`ThrowRuntimeValidation`、`StylingStartupValidation`、`FaceAccessoryRuntimeValidation`、`LongCascadeRuntimeValidation`、`NeuralArmorRuntimeChecks` 和 `PregnancyMoodProbe`。它们不是清理对象，也不能当作正式模组源码自动加入发布 DLL。

启动真实游戏的测试会生成配置、日志或测试存档，部分脚本使用外部含联接测试游戏。运行前阅读脚本参数与相关功能文档；测试后检查 fresh log，涉及测试目录或脚本改动后运行目录环路审计。不要在模组内重建测试游戏联接。

## 资源处理

| 工具 | 行为 |
| --- | --- |
| `Invoke-TextureAssetValidation.ps1` | 只读检查资源路径、PNG 与 FA 纹理约定 |
| `Build-OutlineAssets.ps1` | 在 `TMP` 建立 Unity 工程，构建正式描边资源；`-ValidateOnly` 执行离屏验证 |
| `Build-LanceMotionBlurAssets.ps1` | 在 `TMP` 建立无联接 Unity 工程，构建骑枪连续运动模糊资源 |
| `Optimize-TextureAssets.py` | 555→512 纹理转换的准备、应用和验证；`--verify` 只读复查 |
| `Normalize-TransparentPixels.py` | 默认只读扫描；`--apply` 备份后修正透明像素，保留受保护遮罩 |
| `Repair-AppearanceTextureArtifacts.py` | 默认只读验证；`--apply` 备份后执行指定身体纹理修复 |

Python 资源工具需要 Pillow、numpy。原图和处理清单存入 `DevData/Backups`；不要因目录未被 Git 跟踪而删除它们。描边源码和详细验收边界见[描边维护](features/outline-guide.md)。
