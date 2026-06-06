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

验证前命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-Phase6StaticValidation.ps1
```

日志扫描建议：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; Select-String -LiteralPath "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log" -Pattern "Error|Exception|Could not|Failed|missing|NullReference|Translation data|Could not resolve|XML error|Patch operation" -CaseSensitive:$false
```

## 加载组合

### 1. 最小必需组合

目的：证明无 DLC 和无可选集成时，核心 mod 加载不红字。

`activeMods`：

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

`activeMods`：

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

`activeMods`：在全 DLC 组合后追加：

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

`activeMods`：在全 DLC 组合后追加：

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

`activeMods`：在全 DLC 组合后追加：

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

`activeMods`：全 DLC 组合 + FA + Search and Destroy + VCookE。

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
- 游戏内验证尚未执行。
- 最终发布前必须至少完成“最小必需组合”“全 DLC 组合”“全集成组合”和全部功能验证。
