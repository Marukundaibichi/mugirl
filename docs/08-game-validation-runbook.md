# 游戏内验证 Runbook

本文档用于补齐第二轮重构后的游戏内验证。`docs/tools/Invoke-Phase6StaticValidation.ps1` 只能证明静态一致性，不能替代 RimWorld 实际加载、生成、交互、读档和长时间运行验证。

执行边界：游戏内验证由用户手动执行；本轮自动化工作只交付静态验证脚本、静态验证结果和手动验证清单，不自动启动 RimWorld，也不修改用户当前游戏配置。

执行原则：

- 不验证旧档兼容；用户已确认不用兼容旧档。
- 游戏内验证由用户手动执行，不作为本轮静态重构执行的自动化阻塞项。
- 每一组验证前先备份当前 `ModsConfig.xml`，验证后恢复用户原配置。
- 每一组验证都必须记录 `Player.log` 路径、启动时间、mod 组合、是否有红字、是否有重复刷日志。
- 只要出现红字、加载失败、功能缺失、玩家可见文本缺 key、贴图/音效缺失、非 MooGirl 内容被影响，就不能记为通过。
- 静态脚本必须在游戏内验证前后各跑一次，确保验证期间没有引入新的文件或构建问题。

## 准备

当前本机关键 packageId：

- Harmony：`brrainz.harmony`
- Core：`ludeon.rimworld`
- Royalty：`ludeon.rimworld.royalty`
- Ideology：`ludeon.rimworld.ideology`
- Biotech：`ludeon.rimworld.biotech`
- Anomaly：`ludeon.rimworld.anomaly`
- Odyssey：`ludeon.rimworld.odyssey`
- Humanoid Alien Races：`erdelf.HumanoidAlienRaces`
- MooGirl Race：`HAR.MuGirlRace`
- Facial Animation：`Nals.FacialAnimation`
- Search and Destroy Continued：`MemeGoddess.SearchAndDestroy`
- Vanilla Cooking Expanded：`VanillaExpanded.VCookE`

日志路径：

```text
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player-prev.log
```

配置路径：

```text
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml
```

生成验证用配置模板：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\New-GameValidationConfigs.ps1
```

输出目录：

```text
TMP\GameValidationConfigs
```

使用方式：先备份当前 `ModsConfig.xml`，再将对应模板复制为当前 `ModsConfig.xml`；验证完成后恢复原配置。脚本只生成模板，不会修改用户当前游戏配置。

验证前命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-Phase6StaticValidation.ps1
```

日志扫描命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-PlayerLogScan.ps1
```

该脚本发现疑似红字、异常、缺失翻译、XML 或 patch 错误时会返回失败码；需要检查上一轮日志时使用 `-Previous`。

## Fresh Log 记录模板

每次手动验证后追加一条记录，避免只留下“已跑完”但缺少可复核证据。

```text
日期时间：
验证人：
配置模板：
RimWorld 版本：
MooGirl DLL 时间戳：
Player.log 路径：
Player.log LastWriteTime：
日志扫描命令：
日志扫描结果：
进入主菜单：是/否
新档进入地图：是/否
保存读档：是/否
红字：无/有，摘要：
黄字：无/有，摘要：
MooGirl 相关异常：无/有，摘要：
外部 mod 噪音：无/有，摘要：
本轮功能点：
结论：通过/不通过/需复查
后续处理：
```

## 加载组合

### 1. 最小必需组合

目的：证明无 DLC 和无可选集成时，核心 mod 加载不红字。

`activeMods`：可使用 `TMP\GameValidationConfigs\01-minimal.xml`。

```xml
<li>brrainz.harmony</li>
<li>ludeon.rimworld</li>
<li>erdelf.HumanoidAlienRaces</li>
<li>HAR.MuGirlRace</li>
```

验收：

- 主菜单加载完成。
- 无红字。
- `Bio_1.6`、`1.6/FacialAnimation`、`Versions/1.6/Integrations/*` 不应加载。
- MooGirl race、基础 apparel、milk、rope、mounting Def 不缺失。

### 2. 全 DLC 组合

目的：证明 Royalty、Ideology、Biotech、Anomaly、Odyssey 全开时 patch 和 MayRequire 正常。

`activeMods`：可使用 `TMP\GameValidationConfigs\02-all-dlc.xml`。

```xml
<li>brrainz.harmony</li>
<li>ludeon.rimworld</li>
<li>ludeon.rimworld.royalty</li>
<li>ludeon.rimworld.ideology</li>
<li>ludeon.rimworld.biotech</li>
<li>ludeon.rimworld.anomaly</li>
<li>ludeon.rimworld.odyssey</li>
<li>erdelf.HumanoidAlienRaces</li>
<li>HAR.MuGirlRace</li>
```

验收：

- 主菜单加载完成。
- 无红字。
- `Bio_1.6` 正常加载，`MooGirl_Xenotype`、MooGirl gene、ability 可见。
- Odyssey 专用服装、Anomaly 手术/holding platform 条目无缺 Def。
- `HumanlikeConstant` 使用原版 Def，本 mod 不再覆盖 vanilla/DLC Def。

### 3. Facial Animation 组合

目的：证明 `1.6/FacialAnimation` 仅在 FA 启用时加载，且补丁不重复添加 comp。

`activeMods`：可使用 `TMP\GameValidationConfigs\03-facial-animation.xml`；等价于在全 DLC 组合后追加：

```xml
<li>Nals.FacialAnimation</li>
```

验收：

- 主菜单加载完成。
- 无红字。
- MooGirl pawn 生成后脸部、眼、嘴、眉、情绪贴图显示，不缺贴图。
- 重进游戏后不出现重复 comp、重复 patch 或重复 body addon 报错。

### 4. Search and Destroy 组合

目的：证明 Search and Destroy 集成只在目标 mod 存在时加载，并正确插入 `MooGirlLike`。

`activeMods`：可使用 `TMP\GameValidationConfigs\04-search-and-destroy.xml`；等价于在全 DLC 组合后追加：

```xml
<li>MemeGoddess.SearchAndDestroy</li>
```

验收：

- 主菜单加载完成。
- 无红字。
- draft/搜索歼灭相关行为节点不会破坏 MooGirl idle、牵引、主思维树。
- 未启用 Search and Destroy 时不会有 SearchAndDestroy 类型缺失红字。

### 5. VCookE 组合

目的：证明 VCookE 集成只在目标 mod 存在时加载，且 cheese press 目标缺失或已添加时静默。

`activeMods`：可使用 `TMP\GameValidationConfigs\05-vcooke.xml`；等价于在全 DLC 组合后追加：

```xml
<li>OskarPotocki.VanillaFactionsExpanded.Core</li>
<li>VanillaExpanded.VCookE</li>
```

验收：

- 主菜单加载完成。
- 无红字。
- `MooGirl_MooGirlMilkIntoCheese` 可用于 VCookE cheese press 流程。
- 未启用 VCookE 时不会有 `ProcessDef` 或 `PipeSystem` 类型缺失红字。

### 6. 全集成组合

目的：证明全 DLC、HAR、FA、Search and Destroy、VCookE 同时存在时没有 patch 顺序冲突。

`activeMods`：可使用 `TMP\GameValidationConfigs\06-all-integrations.xml`；等价于全 DLC 组合 + FA + Search and Destroy + VCookE。

验收：

- 主菜单加载完成。
- 无红字。
- 进新档后完成下方所有功能验证。

## 功能验证

每项都在新档中验证，不使用旧档。

### 1. 新游戏与生成

- 开新殖民地，检查起始 pawn 可生成 MooGirl。
- Dev mode 生成 `MooGirl_Colony`、`MooGirl_Slave`、`MooGirl_GiantCorporationCombatant`。
- 检查姓名、backstory、body type、life stage、hair category、xenotype、faction 显示。
- 儿童、成人、Biotech xenotype 相关内容无红字。

### 2. 产奶与奶制品

- 选中 MooGirl，检查 milk gizmo、inspect string、产奶进度。
- Dev fill 或快进使 milk 满，执行挤奶。
- 验证喝奶、喂倒地 pawn、母乳/婴儿喂养相关 float menu。
- 制作或生成奶制食品，检查食用 hediff/thought/效果。
- 验证 `milking_V1` 音效能播放，奶制品堆叠贴图显示。

### 3. 束具与高级束具

- 穿戴基础束具，检查 locked/unlocked/cracked 状态文本。
- 使用 medieval、industrial、spacer、advance key 解锁或破解。
- 验证错误钥匙、手部阻塞、已预约、不可达、目标非法时的 FloatMenu 文本。
- 验证 magnetic shackles、shock collar、brainwash helmet、advanced apparel 的 gizmo、冷却、mote、message、hediff。
- 强制触发洗脑 MentalState，检查 beginLetter、recoveryMessage、thought 和恢复。

### 4. 牵引与拴墙

- 对 MooGirl 执行 rope、unrope、rope to hitch。
- 验证 roped inspect string、draft 禁用、door open、escape prevention。
- 验证 roper downed/dead/despawn、rope target 不可达、断绳时不红字。
- 验证非 MooGirl pawn 不被本 mod 牵引逻辑意外影响。

### 5. 骑乘与骑手战斗

- 让人形 pawn mount MooGirl，再 dismount。
- 验证 rider gizmo、carrier gizmo、inspect string、reserved/no path/bad state reason。
- 让骑手持远程武器，验证 fire at will、hold fire、warmup、caster 恢复。
- 验证换武器、武器销毁、下马、读档后不会保留错误 caster/warmup 静态状态。
- 销毁或 despawn 载体，验证 rider 正常掉落；无可用格时验证 world pawn 兜底不丢 pawn、不复制 pawn、不红字。

### 6. 事件与任务

- 触发开局坠机、野生逃亡、wanderer join、快递事件、巨企袭击。
- 验证事件信件、儿童分支、factionless/hostile/non-hostile 分支文本。
- 与 courier 对话，验证 fight、leave items、demand pay/refuse。
- 验证 courier 生成、离开、战斗、物品掉落、已处理状态读档。

### 7. 非 MooGirl 副作用

- 对普通人类 pawn 检查 forbidden、pollution、plant、HumanlikeConstant 行为无异常。
- 普通人类 constant think tree 不应被 MooGirl 自定义 Def 覆盖。
- 普通 trader、caravan、orbital trader 的 stock generator 不应重复添加或刷日志。
- 非 MooGirl apparel drop tooltip 不应被束具逻辑改变。

## 结果记录模板

```text
日期：
RimWorld 版本：
验证人：
mod 组合：
ModsConfig 备份路径：
Player.log 路径：
静态脚本结果：
主菜单加载：
红字/异常：
重复日志：
功能项通过：
失败项：
截图/存档：
结论：
```

## 当前状态

截至 2026-06-07：

- 静态验证已通过：`docs/tools/Invoke-Phase6StaticValidation.ps1`。
- 最小必需组合已完成一次 fresh 游戏内验证。
- 最小组合 `Player.log` 未再出现旧 `VacuumResistance` 缺失、贴图缺失或 MooGirl `rulesStrings` 注入错误。
- 最小组合仍有 Simplified Chinese 翻译报告 load errors，但已判定为原版/DLC/HAR 外部语言噪音，不是 MooGirl DefInjected 条目。
- 2026-06-07 追加修复 MooGirl 自身翻译报告缺失项：`MooGirl_Colonist.description`、`MooGirl_GiantCorporations_Hostile.leaderTitle`、`messageDefendersAttacking`、MooGirl/PMC/OPC 武器 `verbs.Verb_Shoot.label`。修复后 Phase 6 静态验证通过。
- 全 DLC 组合已完成一次 fresh 游戏内验证；`Player.log` 经扫描后无 MooGirl 可疑行。原版 `Ideo.PostLoadInit` 自动补齐隐藏 ritual precept 的 warning 已归类为读档修复噪音，并加入 `Invoke-PlayerLogScan.ps1` 精确白名单。
- Facial Animation 组合已完成一次 fresh 游戏内验证；`Player.log` 扫描无可疑行，`1.6/FacialAnimation` 加载未出现 MooGirl 缺 Def、缺贴图或 patch 错误。
- Search and Destroy 组合已完成一次 fresh 游戏内验证；`Player.log` 扫描无可疑行，SearchAndDestroy think tree patch 未出现 target 或类型错误。
- VCookE 组合已完成 fresh 游戏内验证；首次发现 MooGirl `ProcessDef` 语言目录错误，已修复为 `PipeSystem.ProcessDef` 并重跑确认 General load errors 为 0。剩余 3 条翻译 load errors 均为外部 Blackboard/SchoolDesk 条目。
- 全集成组合已完成一次 fresh 游戏内验证；MooGirl、Facial Animation、Search and Destroy、VCookE 集成均无加载或 patch 错误。日志仍含已归类外部语言、外部元数据、外部按键和原版 AncientSoldier 存档关系噪音。
- 阶段 A 的 6 组加载组合已覆盖完成；后续仍需按“功能验证”清单做具体交互长跑。

## 验证记录

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：01-minimal.xml；Harmony + Core + Humanoid Alien Races + MooGirl Race
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：%USERPROFILE%\Desktop\TranslationReport.txt
静态脚本结果：修复后 `docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过
主菜单加载：已完成
红字/异常：未发现 MooGirl 运行时红字；仅剩 Simplified Chinese 外部 DefInjected load errors 汇总
重复日志：未发现 MooGirl 重复刷日志
功能项通过：最小组合启动、进地图、保存、读档由用户确认完成
失败项：无 MooGirl 失败项；TranslationReport 中外部 HAR/原版/DLC 缺失项暂不由本 mod 修复
截图/存档：无
结论：最小组合对 MooGirl 判定通过，可继续全 DLC 组合；外部简中语言噪音需在后续组合中继续分离记录
```

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：02-all-dlc.xml；Harmony + Core + Royalty + Ideology + Biotech + Anomaly + Odyssey + Humanoid Alien Races + MooGirl Race
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：未重新生成；桌面报告仍为 19:42:42 的最小组合旧报告
静态脚本结果：`docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过
主菜单加载：已完成
红字/异常：无 MooGirl 加载、Def、贴图、XML 注入或 Harmony 可疑行
重复日志：读档时出现 14 条原版 hidden ritual precept 补齐 warning；已由源码确认为 RimWorld.Ideo 读档修复路径，不是本 mod 问题
功能项通过：全 DLC 组合启动、进地图、保存、读档由用户确认完成
失败项：无 MooGirl 失败项
截图/存档：无
结论：全 DLC 组合对 MooGirl 判定通过，可继续 Facial Animation 组合
```

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：03-facial-animation.xml；全 DLC 组合 + Nals.FacialAnimation
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：未重新生成
静态脚本结果：前一轮 `docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；本轮未改生产文件
主菜单加载：已完成
红字/异常：无 MooGirl 加载、Def、贴图、XML 注入、FA patch 或 Harmony 可疑行
重复日志：未发现 MooGirl 重复刷日志
功能项通过：FA 组合启动、进地图、保存、读档由用户确认完成
失败项：无 MooGirl 失败项；日志中的 `Mod Roren Facial Animation dependency ... downloadUrl` 为外部未启用 FA 包元数据提示，不属于 MooGirl
截图/存档：无
结论：Facial Animation 组合对 MooGirl 判定通过，可继续 Search and Destroy 组合
```

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：04-search-and-destroy.xml；全 DLC 组合 + MemeGoddess.SearchAndDestroy
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：未重新生成
静态脚本结果：前一轮 `docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；本轮未改生产文件
主菜单加载：已完成
红字/异常：无 MooGirl 加载、Def、贴图、XML 注入、SearchAndDestroy patch 或 Harmony 可疑行
重复日志：未发现 MooGirl 重复刷日志
功能项通过：Search and Destroy 组合启动、进地图、保存、读档由用户确认完成
失败项：无 MooGirl 失败项；日志中的 `OpenMapSearch`/`Command_ItemForbid` 按键冲突属于外部 mod/玩家按键配置，不由 MooGirl 定义
截图/存档：无
结论：Search and Destroy 组合对 MooGirl 判定通过，可继续 VCookE 组合
```

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：05-vcooke.xml；全 DLC 组合 + Vanilla Expanded Framework Core + Vanilla Cooking Expanded
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：%USERPROFILE%\Desktop\TranslationReport.txt
静态脚本结果：修复后 `docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过
主菜单加载：已完成
红字/异常：首次报告 `DefInjected/ProcessDef` 不对应 Def type；已修复为 `DefInjected/PipeSystem.ProcessDef` 并重跑确认该错误消失
重复日志：未发现 MooGirl 重复刷日志
功能项通过：VCookE 组合启动、进地图、保存、读档由用户确认完成；MooGirl milk cheese ProcessDef 语言目录加载成功
失败项：无 MooGirl 失败项；剩余 3 条 DefInjected load errors 为外部 `Blackboard`/`SchoolDesk` 家具翻译注入项；日志中的 `More Gravship Workbenches dependency ... downloadUrl` 为外部未启用包元数据提示
截图/存档：无
结论：VCookE 组合对 MooGirl 判定通过，可继续全集成组合
```

```text
日期：2026-06-07
RimWorld 版本：1.6
验证人：用户手动验证
mod 组合：06-all-integrations.xml；全 DLC 组合 + Nals.FacialAnimation + MemeGoddess.SearchAndDestroy + Vanilla Expanded Framework Core + Vanilla Cooking Expanded
ModsConfig 备份路径：用户手动管理
Player.log 路径：%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
TranslationReport 路径：沿用 20:36:56 的 VCookE fresh report；本轮未生成新的差异报告
静态脚本结果：`docs/tools/Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过
主菜单加载：已完成
红字/异常：无 MooGirl、FA、SearchAndDestroy、VCookE 加载、Def、贴图、XML 注入或 patch 可疑行
重复日志：未发现 MooGirl 重复刷日志
功能项通过：全集成组合启动、进地图、保存、读档由用户确认完成
失败项：无 MooGirl 失败项；日志中的 3 条 Simplified Chinese load errors 为外部 `Blackboard`/`SchoolDesk` 家具翻译注入项；`More Gravship Workbenches` 与 `Roren Facial Animation` 为外部未启用包元数据提示；`OpenMapSearch`/`Command_ItemForbid` 为外部按键配置冲突；`Thing_Human2986`/`Dino` 为原版 `AncientSoldier` cryptosleep casket 存档关系残留，未命中 MooGirl 生成路径
截图/存档：无
结论：全集成组合对 MooGirl 判定通过；阶段 A 的 6 组加载组合完成
```
