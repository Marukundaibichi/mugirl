# 启动日志语言空引用（2026-09-21）

## 原因和边界

用户日志中的 `Mugirl.Bootstrap.HarmonyPatchFailed` 翻译键在中英文语言文件中均存在。
实际游戏程序集 MVID `61e4173561894da49d210260257b5097` 的
`Verse.PlayDataLoader.DoPlayLoad()` 先执行 `LoadedModManager.LoadAllActiveMods()`，
然后才执行 `LanguageDatabase.InitAllMetadata()`。

`MugirlMod` 构造函数在前一阶段调用 `MugirlBootstrap.Initialize()`。某个 Harmony
补丁失败后，旧 `TryPatchClass` 的 catch 立即调用带参数的 `Translate`，先产生
“No active language”，随后在 `Find.ActiveLanguageWorker` 抛出空引用。
异常从 catch 再次逸出，使 Mod 构造和后续补丁注册中断，也遮住了原始补丁错误。
用户日志中的 Mugirl DLL MVID `69383643503343dbb2cb0cdc82883db7` 与本轮修改前
备份 DLL 一致。

本轮最小依赖和当前完整模组列表都没有复现原始 Harmony 补丁失败。因此可以确认
并修复异常处理的二次崩溃，但不能据此归因于某个具体补丁或第三方模组。

## 修复

- `MugirlLog.StartupWarningOnce` 只在语言已初始化时求值本地化回调，否则即时输出
  与原翻译键语义一致的英文默认文本，仍经由原有 `WarningOnce` 限次输出。
- 启动扫描、特性检查、逐类注册的四处 catch 共用 `WarnPatchFailure`。
- 手动注册的缺 Harmony、缺目标、缺补丁方法和 Patch 异常路径同样保护。
- 使用异常 `ToString()` 保留类名、内部异常和堆栈，避免只看到 Harmony 包装异常。
- 保持普通补丁的早期注册、造型台补丁的主线程延迟注册和重复初始化保护。

## 验证

`docs/tools/Test-BootstrapStartup.ps1` 编译实际三个 Core 源文件，通过受控 Harmony
和语言桩注入故障。13 项断言通过，覆盖未初始化语言、内部异常保留、其他补丁继续
注册、手动注册失败、限次日志、语言就绪后的翻译和延迟注册。

真实游戏冷启动使用独立配置和日志，不加载玩家存档；测试驱动完成后自行退出：

| 配置 | 结果 | 日志目录 |
| --- | --- | --- |
| Harmony、全 DLC、HAR、Mugirl | 170 PASS / 0 FAIL | `DevData/Evidence/StylingStartup-Bootstrap-0921-Actual` |
| 当前 40 个模组列表副本，含 Prepatcher、PurePatcher、YaOpt | 171 PASS / 0 FAIL | `DevData/Evidence/StylingStartup-Bootstrap-0921-CurrentMods` |

完成时间分别为 UTC `2026-09-21T12:00:36Z` 和 `2026-09-21T12:04:14Z`。
两轮均未出现 `No active language`、Mod 构造异常或 Mugirl 补丁跳过日志。
游戏仍报告已有的翻译数据错误数量；本轮未处理这些独立问题。第一次沙箱运行
`DevData/Evidence/StylingStartup-Bootstrap-0921-Initial` 因 Steam 未初始化而缺失 Workshop 依赖，
已关闭该测试进程，不计入通过结果。

正式 DLL 已重新 Release 构建，并通过反编译确认不含三个可选测试驱动。
输出 MVID 为 `a470009efd924d418ee3201719a46107`，SHA-256 为
`06F04F546E9DF66F76A6B67DC7BECD76F9255608DCEB6D6B535DB3E88AF0A9D0`。
修改前 DLL 保留在 `DevData/Backups/BootstrapFix-20260921/MugirlRace.before.dll`。

目录环路审计结果为 `affected_candidates=0`、`scan_errors=0`；本轮没有创建目录联接。
构建、静态验证和审计结果保存在 `DevData/Backups/BootstrapFix-20260921`。

若原始补丁错误再次出现，查找新的 `[Mugirl] Harmony patch skipped:` 或
“Harmony 补丁已跳过”，其后将包含具体补丁类名和原始内部异常。
