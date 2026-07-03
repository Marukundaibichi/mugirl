# 验证运行手册

静态脚本只能证明结构、引用和常见风险没有明显问题，不能替代 RimWorld 实际加载和游玩验证。涉及交互、渲染、事件、读档或第三方 mod 的改动，都应按本文档做 fresh log 验证。

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

使用方式：先备份当前 `ModsConfig.xml`，再把对应模板复制为当前配置。验证完成后恢复用户原配置。`TMP` 是临时输出目录，可以随时删除，需要时重新生成。

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

## 建议验证组合

- 最小必需：Harmony、Core、HAR、MooGirl。
- 全 DLC：Royalty、Ideology、Biotech、Anomaly、Odyssey 全开。
- Facial Animation：按 `facial-animation-guide.md` 检查头部 comp、表情动画、贴图回退和渲染日志。重点看 `normal_MooGirl`、眨眼、疼痛不哭泣、倒地、穿脱衣不用爱心眼、Lovin、`blush/lovinblush` 位于眼白下方、`AttackStatic`/`Wait_Combat` 使用 `sad` 严肃嘴、`Wait_Combat` 不全程半眯、整张眼睛贴图与高光；只有重新启用左右眼 mask 时才检查 `FA/Eyes/Common`。
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
- 事件和任务：开局坠舱、逃奴加入、野人事件、快递事件。
