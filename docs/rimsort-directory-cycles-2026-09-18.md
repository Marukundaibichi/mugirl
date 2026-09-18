# RimSort 目录环路事故与应急处理（2026-09-18）

## 结论与适用范围

本机 RimSort v1.13.2（提交 `7c062235dcf8f23101e0951760974fb4950ce36b`）点击模组时，会在界面线程同步统计整个模组目录。其目录遍历跟随 Windows Junction，却没有已访问目录记录，导致测试游戏里的回指联接形成循环。`About/About.xml` 的语法、描述和预览图不是本次故障原因。

本次已将问题测试游戏迁出所有模组扫描目录。处理后的全量扫描为 **0 个目录环路、0 个受影响模组、0 个读取错误**。没有修改 RimSort 安装、模组 About、游戏 DLL、Def、用户存档或加载顺序。

来源：

- [上游问题 #2450](https://github.com/RimSort/RimSort/issues/2450)。
- [About 查找和解析](https://github.com/RimSort/RimSort/blob/7c062235dcf8f23101e0951760974fb4950ce36b/app/models/metadata/metadata_factory.py#L678-L795)：只枚举模组根目录和 About 目录，不递归查找所有 About.xml。
- [点击后的详情流程](https://github.com/RimSort/RimSort/blob/7c062235dcf8f23101e0951760974fb4950ce36b/app/views/mod_info_panel.py#L971-L1023)：大小统计发生在描述和预览图加载之前。
- [大小统计](https://github.com/RimSort/RimSort/blob/7c062235dcf8f23101e0951760974fb4950ce36b/app/sort/mod_sorting.py#L223-L236)：没有环路检测。
- [Windows 目录属性处理](https://github.com/RimSort/RimSort/blob/7c062235dcf8f23101e0951760974fb4950ce36b/app/utils/platform/windows.py#L27-L40)：只判断目录位 `0x10`，没有排除重解析点位 `0x400`。本机 Junction 属性为 `0x410`，`os.path.islink()` 对这些 Junction 返回 False。

## 扫描范围和发现

按本机 RimSort `settings.json` 的全部实例扫描；目前仅有 Default 实例。另检查 Steam 库配置，`D:\SteamLibrary` 没有 RimWorld 安装或 294100 创意工坊目录，不存在第二套已发现的扫描来源。结论适用于这些已配置/已发现的来源，不代表搜索了任意磁盘上的孤立备份。

| 来源 | 绝对路径 | 候选目录数 |
| --- | --- | ---: |
| 本地模组 | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods` | 118 |
| 创意工坊 | `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100` | 2718 |
| 游戏本体和 DLC | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Data` | 6 |
| 合计 | 其中 2840 个候选目录有可解析 About.xml | 2842 |

扫描先建立物理目录图，每个真实目录仅枚举一次，再检查强连通分量，并反向查找能够进入环路的模组。它能发现模组之间的间接环路，不依靠无限递归试错。处理前检查了 211407 个物理目录、737500 个文件、238 个目录联接，发现 2 组环路，影响下列 3 个模组；没有读取错误。

| 模组 | packageId | 处理前的实际环路（相对于该模组） | 迁出测试目录数 |
| --- | --- | --- | ---: |
| Mugirl Race | `HAR.MugirlRace` | `TMP\WhiteFluffGame\Mods\Mugirl`、`TMP\CorporateValidation-MotionGame\Mods\Mugirl` 指回自身；WhiteFluff 联接也可进入另一模组的环路 | 12 |
| Mugirl: Tales of White Fluff · 乳牛娘：白绒物语 | `MRK.WhiteFluff` | `TMP\Game\Mods\WhiteFluff` 指回自身，`Mods\Mugirl` 与父模组互相可达 | 1 |
| Archo Avatar StarCraftII Expansion | `MRK.ArchoAvatar.StarCraftII.Expansion` | `Validation\Game\Mods\MRK_ImperialWitness` 指回自身 | 1 |

另有两个模组包含目录联接，但本次没有形成环路：`海战DEMO`（风帆海战实验室，33 个）和 `疯狂喷气机`（Rimpack Joyride，5 个）。原版大小统计均能完成，分别约 0.998 秒和 0.338 秒；因此本次保留了它们的目录结构。联接到游戏资源仍会放大统计大小和工作量，后续新增测试环境仍应遵守外置规则。

## 已执行的应急处理

归档根目录：

```text
C:\Users\Fishiv\AppData\Local\RimWorldModTests\RimSortEmergency-20260918-010258
```

1. 将上述 3 个模组中的 14 个含联接测试游戏目录同盘移动到归档；mugirl 的 12 个目录包含其他无直接自环但会反复扫描游戏资源的 CorporateValidation 测试环境。
2. 保留了 200 个目录联接、176 个普通文件（533191077 字节）；没有递归删除任何目录或联接目标。原来的普通日志、配置和打包产物继续保留。
3. 对每个迁移目录比较了相对文件名、大小、文件修改时间、目录名和联接目标组成的清单 SHA256，14 项全部一致。这是目录清单校验，不是逐文件内容哈希。
4. 没有在旧路径创建回指归档的 Junction，否则 RimSort 仍会跟随进入测试环境。
5. `docs/tools/Start-CorporateValidation.ps1` 的测试游戏改为 `%LOCALAPPDATA%\RimWorldModTests\Mugirl\CorporateValidation-<RunName>-Game`。
6. 白绒物语的 `Tools/Start-Validation.ps1` 的游戏运行目录改为 `%LOCALAPPDATA%\RimWorldModTests\WhiteFluff\Game`。普通测试日志仍可写入该模组 `TMP`。
7. 三个受影响模组根目录均写入 `AGENTS.md`，指向本记录并明确禁止在扫描范围内重建联接测试环境。

历史 `Archo Avatar StarCraftII Expansion\tools\migrate_workspace.ps1` 是一次性迁移脚本，不应为了恢复旧测试环境而重新运行；它保存着旧 `Validation\Game` 位置。复用其归档测试游戏时应显式使用外部归档路径，更新自己调用命令中的工作目录。

精确源路径、目标路径、迁移状态及校验值见 [迁移清单](rimsort-emergency-migration-2026-09-18.json)。归档根目录内也保存了 `migration-manifest.json` 和处理前扫描结果。

## 处理后验证

- 相同 2842 个候选目录再次完整扫描，`cycle_components=0`、`affected_candidates=0`、`scan_errors=0`，仍在模组目录中的目录联接仅剩上述两个非循环模组的 38 个。
- 抽取本机版本对应的原始 `get_dir_size()`、`scanpath()` 和 `scanpath_win32()` 复测；函数不修改，只加 15 秒外层诊断上限，三个问题模组都在上限前正常返回。

| 模组 | 原版大小统计耗时 |
| --- | ---: |
| Archo Avatar StarCraftII Expansion | 0.007 秒 |
| Mugirl Race | 0.740 秒 |
| 乳牛娘：白绒物语 | 0.071 秒 |

没有启动完整 RimWorld 测试，也没有通过 GUI 点击验证 RimSort；这里验证的是已确认导致 UI 阻塞的原始文件遍历函数和完整目录图。后续文件增加、磁盘缓存和硬盘速度都会影响耗时。

证据文件：

- [处理前扫描](rimsort-directory-audit-before-2026-09-18.json)
- [处理后扫描](rimsort-directory-audit-after-2026-09-18.json)
- [迁移清单](rimsort-emergency-migration-2026-09-18.json)
- [原版大小统计复测](rimsort-size-verification-2026-09-18.json)

## 后续 AI 必须遵守的规则

1. 含目录联接或目录符号链接的测试游戏统一放在 `%LOCALAPPDATA%\RimWorldModTests\`，并确认该位置不属于任何 RimSort 实例的 local/workshop/game Data 来源。
2. 测试运行目录不能位于任何被引用模组内部，也不能通过另一个模组间接回到自身；只检查一层联接或只检查目标是否为当前根目录都不够。
3. 不要用 Junction 把外部测试目录挂回旧位置。`.gitignore`、`LoadFolders.xml`、模组启用状态都不能阻止 RimSort 的目录大小统计。
4. 普通日志、配置、实际复制出来的发布包可以放在 `TMP`；含联接的游戏环境不可以。目录名称是否叫 TMP 与问题无关。
5. 改动验证脚本、生成/迁移测试目录或创建联接后，必须重跑下述扫描。`affected_candidates=0` 且 `scan_errors=0` 才是本问题的通过条件。
6. 恢复旧文件时按迁移清单查找外部归档；不要整体搬回旧位置，也不要递归复制时跟随 Junction。需要恢复源码或日志，可仅复制所需普通文件。
7. 上游修复应同时考虑跳过重解析点/目录身份去重和把大小计算移出 UI 线程；不能仅靠增大超时或缩小 About.xml。

## 复查命令

仅需 Python 标准库；脚本只读模组和 RimSort 设置，唯一写入是指定的报告文件。

```powershell
$env:PYTHONIOENCODING = 'utf-8'
& 'C:\Users\Fishiv\AppData\Local\Programs\Python\Python312\python.exe' -B `
  'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\mugirl\docs\tools\Audit-RimSortDirectoryCycles.py' `
  --settings 'C:\Users\Fishiv\AppData\Local\RimSort\settings.json' `
  --output "$env:TEMP\rimsort-directory-audit.json"
```

退出码：0 表示无环路且读取完整；1 表示发现可达环路；2 表示存在读取错误，不能据此宣布全部安全。达到扫描时间/目录数上限会报错，不生成冒充完整结果的报告。`--source` 可以追加其他安装的 Mods、Workshop 或 Data 容器目录。

若当前 RimSort 窗口仍卡在旧遍历中，可在保存必要配置后重新打开 RimSort，再选择这些模组。应急处理没有主动终止用户正在运行的 RimSort。
