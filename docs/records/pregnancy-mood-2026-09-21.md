# 雪牛娘正面怀孕心情（2026-09-21）

## 行为与范围

- 按用户要求，将雪牛娘的 `PregnancyMood` 随机心情限定为正面结果：原版“怀孕心情提升”`+4` 和“怀孕心情高涨”`+8`，不再抽到“低落”`-8` 或“崩溃”`-14`。
- 新生成心情时保留已有正面结果，负面结果从当前 Def 的正面候选中等概率重选。数值、发生频率和持续时间继续由原版 Def 控制。
- 旧存档在 `PostLoadInit` 转换雪牛娘已有的负面怀孕心情；保存过程不重选，已有正面结果不重选。
- 只处理 Biotech 的 `PregnancyMood` 和 `MugirlIdentity.IsMugirlPawn` 判定的 Pawn。其他种族、其他随机心情不受影响；`PregnancyAttitude`（孕育新生命）、流产、分娩等独立心情未调整。
- 不修改共享 Def 候选列表。如果其他 mod 移除了全部正面候选，则保留其原结果，避免空引用或无效 Thought。

## 实现

`1.6/Source/Features/Misc/Harmony_MugirlPregnancyMood.cs` 在 `HediffComp_GiveRandomSituationalThought.CompPostMake` 和 `CompExposeData` 后处理当前 Comp，保留原始创建及存档流程。两个补丁均已登记到 `MugirlPatchInfo.cs`，源码已加入项目编译列表。没有新增存档字段、Def 或翻译键，也没有逐 tick 扫描。

原版来源通过 rimsearcher 的 `PregnancyMood` HediffDef 与四个 ThoughtDef 核对，并反编译实际 `Assembly-CSharp.dll` 的 Comp 和 `ThoughtWorker_HediffThoughtRandom`，确认心情与健康页括号标签均读取 `selectedThought`。

## 验证记录

- 日期时间：2026-09-21 21:12（Asia/Shanghai）。RimWorld：1.6.4871 rev591。Release DLL 时间戳：2026-09-21 21:06:16。
- 修改前静态基线及修改后 Release 编译、`Invoke-Phase6StaticValidation.ps1` 均通过。
- 独立测试源码及启动脚本：`docs/tools/PregnancyMoodProbe.cs`、`docs/tools/Start-PregnancyMoodValidation.ps1`（2026-09-21 整理时从 `TMP/PregnancyMoodProbe` 提升为维护工具）。测试 DLL 只进入外部隔离游戏的模组副本，正式 `1.6/Assemblies` 只保留生产 DLL。
- Biotech 配置：Harmony、Core、Biotech、HAR、Mugirl。日志 `DevData/Evidence/PregnancyMoodValidation-20260921-211200-Biotech/Player.log`；`checks.txt` 共 24 项通过，`complete.txt` 为 `PASS failures=0`。日志无 Error、Exception 或缺失引用。
- 验证内容：两个 Harmony 后置补丁注册成功；256 次新生成雪牛娘心情全部正面且覆盖两种正面结果；普通人类仍能生成负面结果；共享候选列表未变；两种种族、四种心情的真实 Scribe 序列化往返共八组均符合预期；无关随机心情保持不变。
- 无 Biotech 配置：Harmony、Core、HAR、Mugirl。日志 `DevData/Evidence/PregnancyMoodValidation-20260921-211204-NoBiotech/Player.log`；4 项检查通过。日志仍有 HAR 图形条件引用缺失 Baby/Child BodyType 的 4 条错误及 58 项中文翻译问题汇总，与本次仅修改 C# 心情逻辑的范围无关；没有怀孕心情补丁异常。
- 这是实际游戏进程中的启动和 Comp/Scribe 专项验证；没有生成地图或加载用户完整存档。前两轮无图形模式虽通过功能检查，但 Unity 图集初始化产生空引用，故最终证据改用正常图形初始化的两轮日志。
- 测试游戏均位于 `%LOCALAPPDATA%\RimWorldModTests\Mugirl\PregnancyMoodValidation-*-Game`。最终目录审计报告：`DevData/Evidence/PregnancyMoodProbe/directory-audit.json`，要求 `affected_candidates=0`、`scan_errors=0`。
