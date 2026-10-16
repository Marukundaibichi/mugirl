# 1.7 重构交接记录（2026-10-15）

本文是接手 1.7 生产级重构的入口，读者是熟悉 RimWorld 模组与 C# 的开发者和 AI 代理。读完后依次读 [执行计划](../refactor-1.7/plan.md) 与 [约束性决策](../refactor-1.7/decisions.md)。决策文件优先于 [设计](../refactor-1.7/design.md)。本文只记录状态与证据位置，详细内容放在链接的文档中。

文中路径相对仓库根目录 `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\mugirl`。`%TEMP%` 指 `C:\Users\Fishiv\AppData\Local\Temp`。

## 1. 一句话现状

Wave 0 与 Wave A 的 A1–A4 已完成，集成也已完成。集成后的源码可以构建，离线验证 45 项中 44 项通过，唯一失败的 `A.about` 留给 Wave D。这个构建**从未部署，也从未在游戏中运行**，`1.7/Assemblies` 中仍是重构前的 DLL。A5（游戏内框架与旧档夹具）在修复阶段被 owner 叫停（2026-10-15 约 17:20，owner 要求停止制作夹具、立即收尾），修复做到哪一步无法确定，必须重新审查。R、R2、S、V、D 五个波次都还没有开始。

接手前必须知道的四件事：

1. **不要用集成构建读写真实存档，也不要发布它。** 生产代码中还没有任何 `[SaveUpgradeModule]` 实现，所以升级表是空的。但运行时仍会给旧档做 `.bak` 备份，并在会话完成后写入 `mugirlSaveSchema = 1`。以后登记为 schema 1 的升级行只作用于 schema 低于 1 的存档，被提前盖上 1 的存档就再也得不到这些升级。游戏内验证只用 `tools/Test-InGame.ps1` 的临时存档目录。
2. **没有 git 基线。** `Source/`、`tools/`、`1.7/` 与 `docs/refactor-1.7/` 都没有纳入 git，`DevData/` 被 `.gitignore` 排除。能用来恢复的只有 `DevData/Backups/` 中的整树归档（见 §9）。
3. **一部分生成器的输入只存在于会话 scratchpad（`%TEMP%` 下，随时可能被清理）。** `tools/RenameApply/specs/generators/common.py` 写死了 scratchpad 路径。2026-10-15 18 时后，orchestrator 已把 scratchpad 复制到 `DevData/Backups/Scratchpad-WaveA-20261015/`（543 个文件，64.6 MiB，清单 `SCRATCHPAD-MANIFEST.sha256`；仓库副本与 bin/obj 未复制），见 §6 第 1 步。
4. **游戏已更新到 1.7.4979 rev109**（2026-10-15 08:26）。[引擎事实清单](../refactor-1.7/inventory/engine-hooks.md) 和 A1 的引擎核对都是在 1.7.4977 rev1248 上做的。4977 的 `Assembly-CSharp.dll` 文件已被覆盖，只在 DecompilerServer 已加载的上下文 `rw17-game`（MVID `de18e239…`）中还有一份；这个别名指向游戏目录中的同一路径，服务重启后再加载得到的就是 4979。读档、Def 加载与设置文件相关的方法已在 4979（上下文 `rw17-4979`）上复核，方法体与 4977 相同，记录在 `..\1.7迁移文档\资料来源与核验记录.md` 的 E52–E54；其余条目见 §6 第 6 步。

## 2. 已完成内容

| 波次/流 | 内容 | 主要路径 | 报告与审查 |
|---|---|---|---|
| Wave 0 | 整树归档与重构前基线：Validate 两个运行时、SelfTest、单元测试、ShaderCheck、SmokeTest、Test-InGame default | `DevData/Backups/PreRefactor-1.7-20261015/`（`tree.zip`、`tree-manifest.sha256`、`MugirlRace.dll/.pdb`，SHA-256 `220e5d1a…`）、`DevData/Evidence/Baseline-PreRefactor-20261015/` | [决策 §6](../refactor-1.7/decisions.md) |
| A1 升级器框架 | `Mugirl.SaveUpgrade`：表模型、owner 选择器、单次遍历索引、计划/应用两段式 DOM 步骤、运行时（会话、备份、提示、进行中工作策略、安装检查）、设置升级；7 个 Hook 文件共 9 个补丁；生命周期中枢；离线入口 `SaveUpgradeOffline.Run`。契约：K1 `SaveUpgradeManifest.ToJson()`，K2 `SaveUpgradeDiagnostics.LastReportJson`，K4 `SaveUpgradeBootstrap.Initialize` | `Source/MugirlRace/SaveUpgrade/**`（96 个 .cs）、`Source/MugirlRace/Core/GameComponent_Mugirl.cs`、`Core/GameLifecycle*.cs`、`Source/Tests/MugirlRace.Tests/SaveUpgrade/**` | [a1-report](../refactor-1.7/wave-a/a1-report.md)、[a1-review](../refactor-1.7/wave-a/a1-review.md) |
| A2 验证器 | 旧版存档语料（152 个输入，14 个 TSV）；`C.save-upgrade`，默认 ratchet 模式；`--parity`、`--save-corpus`、`--extract-legacy-schema` 三种模式；新检查 B.namespaces、B.component-ctors、B.save-confinement、B.patch-order、C.content-shadowing、C.definjected-paths、C.public-api 等；负对照 179 条 | `Source/Tests/MugirlRace.Validation/**`、`Baselines/legacy/*.tsv`、`Baselines/save-upgrade-ratchet.<module>.txt`、`Baselines/save-upgrade-modules.tsv`、`tools/Export-LegacySchema.ps1` | [a2-report](../refactor-1.7/wave-a/a2-report.md)、[a2-review](../refactor-1.7/wave-a/a2-review.md) |
| A3 规格与改名工具 | R/R2 最终规格；`public-api.json`（151 条，冻结名单）；WhiteFluff/Subspecies 迁移清单；R2 之后的文件归属；MooGirl 时代标记；推迟到 S 的项；生成器与 linter（L01–L16）；Roslyn 加 XML 的事务式改名工具。**还没有应用到仓库**，只在 scratchpad 副本上试运行过 | `tools/RenameApply/`（源码；只有 IDE 生成的 `bin/Debug`，使用前须构建）、`tools/RenameApply/specs/{R,R2}/*.json`、`specs/public-api.json`、`specs/owner-migration-list.md`、`specs/ownership-postR2.json`、`specs/moogirl-era-markers.json`、`specs/deferred-S.json`、`specs/upgrade-hand-rows.json`、`specs/generators/` | [rename-tool](../refactor-1.7/rename-tool.md)、[a3-report](../refactor-1.7/wave-a/a3-report.md)、[a3-review](../refactor-1.7/wave-a/a3-review.md) |
| A4 构建与补丁顺序 | K3：普通构建只输出到 `Source/MugirlRace/bin/<Config>/`，加 `-p:DeployToMod=true` 才写入 `1.7/Assemblies`；`Source/global.json` 固定 SDK 10.0.401，并由 `MugirlCheckSdkPin` 检查；17 个补丁文件中加了 21 处显式 `[HarmonyPriority]`，复现重构前的实际顺序；`Validate.ps1 -WorkRoot`；SmokeTest 加锁 | `Source/Directory.Build.props`、`Source/MugirlRace/MugirlRace.csproj`、`Source/MugirlRace/Core/HarmonyBootstrap.cs`、`tools/Validate.ps1`、`tools/SmokeTest.ps1` | [a4-report](../refactor-1.7/wave-a/a4-report.md)、[a4-review](../refactor-1.7/wave-a/a4-review.md)、[a4-patch-order](../refactor-1.7/wave-a/a4-patch-order.md) |
| A5 游戏内框架与夹具（**被中断**） | UpgradeSuite、FixtureGateSuite、ContentSweepSuite；离线单元测试工程；`Test-InGame.ps1` 新增 `-Fixture`/`-TargetModDir`/`-TokenSet`/`-SuiteOption`、运行锁、`-BuildMod -Deploy`；`New-SaveFixture.ps1`；P/X 夹具 | `Source/Tests/MugirlRace.GameValidation/**`（含 `OfflineTests/`）、`tools/Test-InGame.ps1`、`tools/New-SaveFixture.ps1`、`DevData/Evidence/SaveFixtures/*` | [a5-report](../refactor-1.7/wave-a/a5-report.md)、[a5-review](../refactor-1.7/wave-a/a5-review.md)、本文 §4 |
| 集成（orchestrator） | 在 `MugirlMod` 构造函数中调用 `SaveUpgradeBootstrap.Initialize(this)`（`Core/MugirlMod.cs:17`）；为 SaveUpgrade 运行时类型补架构规则例外；`GameComponent_Mugirl.FinalizeInit` 补调 `base.FinalizeInit()`；补 dynamic-lookup 白名单；新增 `1.7/Languages/{English,ChineseSimplified}/Keyed/SaveUpgrade.xml`（4 个提示键）；`MountedPawnUtility.IsMugirl` 加 `[MethodImpl(NoInlining)]`；patch-targets 基线新增 9 行。随后把集成后的树归档为 Wave R 的 parity 基线 A | `DevData/Backups/PreR-1.7-20261015/`（`tree.zip`、`tree-manifest.sha256`、Release `MugirlRace.dll`，SHA-256 `5b0103c6…`，从未部署）、`DevData/Evidence/Integration-WaveA-20261015/` | 本文 §3 |
| 规划文档 | 盘点、七个模块的 Wave S 计划、对抗审查、一致性审查 | `docs/refactor-1.7/{inventory,module-plans,reviews}/` | 见 §9 |

2026-10-15 17:4x 与 18 时前后两次核对，现场树（`Source`、`1.7`、`tools`、`About`、`LoadFolders.xml`、`.gitignore`，跳过 `bin/obj`）与 `DevData/Backups/PreR-1.7-20261015/tree-manifest.sha256` 完全一致：2161 个文件中没有改动、缺失或多出的文件。

## 3. 验证结果

| 项目 | Wave 0 基线（游戏 4977，DLL `220e5d1a`） | 集成后（游戏 4979，未部署的集成构建） |
|---|---|---|
| `dotnet build MugirlRace.slnx` | —（A4 时 GameValidation 尚不能编译） | 0 个警告，0 个错误（`slnx-build.txt`） |
| `Validate.ps1`，两个运行时 | 43 项中 42 项通过，只有 `A.about` 失败 | 首次 45 项中 38 项通过（`validate.txt`），修正后 44 项通过，只有 `A.about` 失败（`validate-final.txt`）；两个运行时结果相同 |
| 单元测试 `MugirlRace.Tests` | 473 个通过 | 589 个：0 个失败，1 个未运行（显式基准测试 `PerformanceBenchmarks`） |
| ShaderCheck | 通过 | 通过 |
| `Validate.ps1 -SelfTest` | 114 条规则通过 | 179 条规则通过：集成修正前一次（`selftest.txt`，17:23），集成修正后又重跑一次，两个运行时仍全部通过（`selftest-final.txt`，18 时后） |
| `C.save-upgrade`（ratchet） | 当时没有这项检查 | 通过：152 个输入，4169 行语料；未变 1875，删除 737，不适用 7，未处置 108（其中 63 行属必需行；4 个警告） |
| `SmokeTest.ps1 -QuickTest -DevMode` | PASS（`smoke.txt`） | 未对集成构建运行 |
| `Test-InGame.ps1` default | 65 ok / 1 failed / 14 skipped（`ingame-default.txt`） | 未对集成构建运行。A5 在 4979 上用重构前 DLL 跑过一次：78/0/14（`TMP/InGameValidation/20261015-144507`） |
| `Test-InGame.ps1 -Fixture` | — | 从未对集成构建运行（见 §4） |

基线证据在 `DevData/Evidence/Baseline-PreRefactor-20261015/`，集成证据在 `DevData/Evidence/Integration-WaveA-20261015/`。

已知失败及原因：

- `A.about`：`About/About.xml` 的 `supportedVersions` 只列了 1.6，计划在 Wave D 修改。只要它还失败，`Validate.ps1` 结尾就会把 dotnet 与 waylithclr 两次运行都标为 failed，并以 `exit=1` 结束，这是预期结果。
- Wave 0 时 Test-InGame default 的唯一失败是测试本身的缺陷：`ui/corporate-comms` 补丁打在了未声明该方法的派生类型上。A5 已改为补丁声明它的类型（`Core/DrawProbe.cs`）。
- 首次集成验证有 7 项失败：`A.about`；`B.architecture` 5 处；`B.base-calls` 1 处；`C.def-lookups` 4 处；`C.translation-keys` 9 处；`C.public-api` 1 处（`IsMugirl` 缺 NoInlining）；`B.patch-targets` 9 行。除 `A.about` 外都已在集成中修正，详见 `validate.txt` 与 `patch-targets.diff.txt`。
- `C.save-upgrade` 的 63 行必需未处置项分布在各模块的 ratchet 文件中：combat 5、content 13、core 6、corporate 4、incidents 5、milk 6、moogirl 2、restraints 11、unassigned 11。它们由 Wave S 各模块处置，ratchet 文件只许减少。

## 4. A5 的中断状态与复审要求

时间线（证据为 scratchpad 中的 `a5f/fixtures.log`、`a5f/fixtures2.log`、`a5f/control-quick.log`，以及文件时间）：

- 15:09，审查完成（[a5-review](../refactor-1.7/wave-a/a5-review.md)：0 blocker、9 major、14 minor）。
- 15:23–16:53，修复代理修改代码，修改脚本是 scratchpad 中的 `a5f_*.py`，脚本修改前的副本是 `a5f/Test-InGame.before.ps1` 与 `a5f/New-SaveFixture.before.ps1`。
- 15:54，`-Fixture control` 在重构前 DLL 上运行（`TMP/InGameValidation/20261015-155416`）。
- 15:58–16:51，第一次重新生成夹具。0.5.0 与 0.4.7 失败，原因是“RimWorld is running”，与构建本身无关。
- 16:54 起，第二次重新生成，顺序为 1004、0920、0.5.0。0.5.0 的闸门启动在 17:17 通过（ok=34 failed=0），随后的播种被中断。
- 约 17:20，owner 叫停。

以下状态是按 2026-10-15 收尾时的源码逐条核对得出的。“代码已改”只表示能在代码中看到修改，**没有一项**对集成构建跑过游戏内验证。

| # | 审查发现（摘要） | 状态 | 依据 |
|---|---|---|---|
| 1 | SaveNameMap 把 K1 名字空间压平 | 部分修复 | Def 已按（根类型, 旧名）登记，见 `Core/SaveNameMap.cs` 的 `defs`/`defsByName` 与 `Core/SaveDefRoots.cs`；标签按 owner 选择器求值（`LabelFate`）。值行仍只按旧值登记（`valuesByOld`），因为 K1 没有导出值的上下文 |
| 2 | 删除项无法解释，隐含 Def 未处理 | 代码已改 | `SaveNameMap.DerivedRules`，`Core/SaveDiffExplainer.cs` 的 `DroppedOwner`；`OfflineTests/SaveDiffTests.cs` |
| 3 | diff 与事实忽略 K1 策略与步骤声明的消费 | 代码已改 | SaveNameMap 已解析 `endedJobTypes`、`claimedJobTypes`、`lordPolicies` 与各类 `consumed*`；`SaveDiffExplainer.PolicyFor`、`LordGraphLabels` |
| 4 | diff 范围过大，有原版与 HAR 噪声 | 部分修复，**未校准** | 已有 `Core/SaveDiffScope.cs`（只比较 Mod 拥有的项）、`SaveXml.SameValue` 的误差容许、`UnresolvedThinkNodeKeys`，以及对照组 `-Fixture control`。15:54 的对照组第 1 次启动失败 39 项：9 项是重构前 DLL 没有 K1/K2，属预期；另外 30 项是 diff 差异，被测构建读入自己写的存档后，服装多出 `color#2`/`colorActive#2`，`abilities` 由 null 变为非 null，`CorporateNetwork` 也有 null 差异。15:54 之后的改动没有再跑过 |
| 5 | DefInjected 断言恒真 | 代码已改 | `UpgradeSuite.DefInjected` 枚举 Mod 加载目录中的 DefInjected 文件，0 个键即失败；`-Fixture` 时默认语言为 ChineseSimplified（`Test-InGame.ps1:206`） |
| 6 | 残留旧名扫描不限于 Mod 拥有的位置 | 代码已改 | `UpgradeSuite.TokenScan` |
| 7 | 束具事实受改名与默认值影响 | 代码已改 | 播种时把 `restraintDefaults` 写入清单，并有 `restraint-states` 步骤；P-1004 的覆盖为 42 个上锁、5 个未上锁、9 个非默认锁次数、3 个计时 |
| 8 | 播种覆盖不足 | 代码已改，夹具中已体现 | 清单中有 `coverage`/`coverageMissing`；P-1004、P-0927、P-0920 的缺口都为空 |
| 9 | 锁接管有竞态，锁实现有三份 | 部分修复 | 三个脚本都改为 FileShare.None 复核后原地接管，并继承令牌；但仍是三份副本（`Test-InGame.ps1:277-352`、`New-SaveFixture.ps1:153-240`、`SmokeTest.ps1:131-215`），没有共享模块 |
| 10 | X-newer 检查恒真 | 代码已改 | `UpgradeSuite.cs` 约 551 行起：不改写、不备份、恰好一条警告、再存档保留印记 |
| 11 | 未断言备份 | 代码已改 | `UpgradeSuite.cs:168-197` |
| 12 | “播种的雪牛娘存活”检查不稳定 | 代码已改 | 播种有 `settle` 步骤；死亡只记 info，消失才算失败 |
| 13 | 当前 defName 被当作失败记号 | 代码已改 | 只用旧名独有的记号（`UpgradeSuite.cs:130-136`） |
| 14 | X-settings 与 X-alias 是死代码 | 代码已改，**从未运行** | `Test-InGame.ps1:498-556`、`:897`；两个手写夹具 |
| 15 | 离线一致性比较静默跳过 | 代码已改 | 套件选项 `upgrade.requireOffline=true`；但目前没有任何夹具带 `offline-report.json` |
| 16 | `-ModDll -Deploy` 手工复制 DLL | 代码已改 | 改用工程的 DeployToMod |
| 17 | 重复代码与写死的值 | 部分修复 | 构建信息改从 `builds.tsv` 读取，`FixtureKind` 改为枚举；锁等辅助函数仍有多份副本 |
| 18 | 存档被重复解析 | 代码已改 | `UpgradeState` 延迟加载并缓存文档，`Apply` 前先克隆 |
| 19 | 缺离线单元测试 | 工程已建，**未验证** | `OfflineTests/` 下有 8 个测试文件。它不在 slnx 中。15:54–16:01 有过一次 Debug 构建尝试，只在 `obj/` 留下还原文件，`bin/Debug/net10.0` 是空的；没有构建产物，也没有运行记录 |
| 20 | 空列表事实记为 "" | 代码已改 | `SaveFactsTests.EmptyAndMissingCollectionsReadTheSame` |
| 21 | 原版转换器路径依赖游戏构建号 | 代码已改 | `UpgradeSuite.ReportConverterPath`，只记 info |
| 22 | 事实比较的是重存档 XML，不是实时状态 | 未修复 | 仍比较重存档 XML，套件中也没有写明这个限制 |
| 23 | 多次启动时截图路径不对 | 代码已改 | `Test-InGame.ps1:1040` |

另有两项未核对：A3 要求替换 `ContentSweepSuite.cs` 中的旧名字面量，修复脚本 `a5f_edit4.py` 已做；A1 请求 A5 覆盖的六种情形（Hediff 组件标签迁移、删除的 LordJob 等）是否已覆盖，尚未核对。

接手者必须重新做的复审：

1. 对照 a5-review 的 23 条和上表，审阅 `Source/Tests/MugirlRace.GameValidation/**`、`tools/Test-InGame.ps1`、`tools/New-SaveFixture.ps1` 的现状。两个脚本可以与 `a5f/*.before.ps1` 对比。
2. 构建并运行 `OfflineTests`（命令见 [plan §1.2](../refactor-1.7/plan.md#12-构建与离线验证)）。
3. 修复 #1 的值上下文（需要 K1 扩展）、#4 的校准、#9 和 #17 的共享锁模块、#22 的限制说明。
4. 在集成构建上跑 `-Fixture control`，diff 必须为空，然后才能依赖 upgrade 套件的任何结论。

## 5. 夹具清单

夹具目录为 `DevData/Evidence/SaveFixtures/<id>/`，内容是 `manifest.json` 加 `<id>.rws.gz`。三个可用的 P 夹具都由 1.7.4979 rev109 写出（见清单的 `game.version`），DLC 组合为 AllDlc。

| id | 种类与来源 | 清单 feasible | 存档时间 / 清单 producedAt | 现状 | 可用？ |
|---|---|---|---|---|---|
| P-1004 | P，发布 ZIP 1004 | true | 17:03 / 16:55（第二次） | 1563 条事实，覆盖完整；播种日志 1 条警告 | 可用 |
| P-0920 | P，发布 ZIP 0920 | true | 17:17 / 17:04（第二次） | 1307 条事实，覆盖完整；播种日志 23 条，主要是 0920 构建自身缺贴图 | 可用 |
| P-0927 | P，发布 ZIP 0927（等于创意工坊 3578882219） | true | 16:15 / 16:08（第一次） | 1450 条事实，覆盖完整。由 `ContentSweepSuite.cs` 16:53 修复（重新牵引等）之前的代码生成 | 有条件可用，建议重新生成以统一版本 |
| P-0.5.0 | P，git `46d2190` | false | 存档 14:01（旧）/ 清单 16:50 | 存档来自更早的 A5 运行，已过期；清单写“闸门启动没有结束”。第二次运行的闸门已在 17:17 通过，播种被中断。它的工作目录 `%TEMP%\mugirl-fixtures\20261015-165428\v050` 已在收尾时被 orchestrator 删除，删除前没有保留其中的 `gate.log`/`sweep.log`（疏忽；这次中断运行的日志已不可得） | 不可用，须重新生成 |
| P-0.4.7 | P，git 0.4.7 | false | 存档 14:32（旧）/ 清单 16:51 | 存档已过期；闸门通过，播种因“RimWorld is running”被拒 | 不可用，须重新生成 |
| X-alias | 手写，base 为 P-1004 | true | 15:50 | `mod/` 是一个使用 `Mugirl.StockGenerator_Mugirl_Slaves` 与 `slaveCountRange` 的小依赖 Mod | 可用，从未运行 |
| X-settings | 手写，base 为 P-1004；内容取自 `legacy-settings.tsv` | true | 15:50 | `config/Mod_{folder}_MugirlMod.xml` | 可用，从未运行 |

所有夹具都没有 `offline-report.json`。MooGirl 时代的情形（D3）不做重放夹具，改用 owner 的真实存档 S1–S4 做离线语料运行，见 [真实存档清单](../refactor-1.7/inventory/real-saves.md)。

## 6. 立即可做的下一步

按顺序执行。命令的参数与“绿”的定义见 [执行计划](../refactor-1.7/plan.md)。

1. **保全 scratchpad。** scratchpad 路径为 `%TEMP%\claude\c--Program-Files--x86--Steam-steamapps-common-RimWorld-Mods-mugirl\de907362-7ceb-4fb9-ba6b-b0a761817e4f\scratchpad`，要复制到 `DevData/Backups/Scratchpad-WaveA-20261015/`：
   - `spec/*.json` 与 `spec/*.md`，这是生成器输入，包括 `merged.json`、`core-member-moves.json`、`content-keyed-split.json`、`content-legacy-defs.json`、`moogirl-types.json`、`moogirl-defs.json`；
   - `understand/*.md`、`understand/*.json`、`understand/dlls/types.tsv`，其中 `lint_spec.py` 要读后两类；
   - `tooltest/final/*.{ps1,py,txt,log,json}`，即 pipeline、`order_diff.py`、`compare_validation.py` 与试运行报告；
   - `a2f/*.ps1`（parity 与提取器对照的自测驱动）、`a4/*.md`、`a4f/*.ps1`（锁测试）、`a5f/*`（修复日志和脚本修改前副本）、根目录的 `*.py`（包括 `archive_preR.py`、`a5f_*.py`）。

   **已完成**（2026-10-15 18 时后，收尾的 orchestrator）：复制的是整个 scratchpad，不只上面这份清单，共 543 个文件、64.6 MiB，清单为 `SCRATCHPAD-MANIFEST.sha256`。只省略了仓库副本（`tooltest/{repo,base,out,out-base}`、`tooltest/final/` 下按次生成的整树副本、`copytest`）和 `bin/obj`，说明见目录中的 `SNAPSHOT-NOTE.txt`。生成器里写死的是原 scratchpad 路径，使用前把 `generators/common.py` 中的根路径改成这个目录。目录中的 `understand/` 含从 owner 真实存档提取的名字表，只能留在 DevData，不能纳入版本控制。
2. **记录工作区基线**（AGENTS.md 要求）：
   - 运行 `git status --short`；
   - 记录 `1.7/Assemblies/MugirlRace.dll` 的 SHA-256，应为 `220E5D1A…`；
   - 用 [plan §1.1](../refactor-1.7/plan.md#11-波次前归档与比对) 的比对脚本把现场树与 `DevData/Backups/PreR-1.7-20261015/tree-manifest.sha256` 比较。有差异就先弄清来源，不要覆盖。
   - 在自己的 WorkRoot 中重跑 `Validate.ps1` 与 `Validate.ps1 -SelfTest`（[plan §1.2](../refactor-1.7/plan.md#12-构建与离线验证)），确认仍是 44/45 与 179 条。
3. **复审 A5**，按 §4 的四步做完，并补齐未修复项。在此之前，任何 upgrade 套件的结果都只能作参考。
4. **部署集成构建，在游戏中串行运行**（详见 plan §1.3）：
   - `pwsh tools/Test-InGame.ps1 -BuildMod -Deploy -Suite default`：与 65/1/14 比较，不得有新失败；
   - `pwsh tools/SmokeTest.ps1 -QuickTest -DevMode`：应为 PASS；
   - `pwsh tools/Test-InGame.ps1 -Fixture control`：升级器不动作，diff 为空；
   - 结束后由 owner 决定 `1.7/Assemblies` 留哪个 DLL。留下集成构建意味着 owner 平时玩的游戏也会加载它，见 §1 第 1 条；恢复重构前 DLL 的命令在 plan §1.4。
5. **重新生成过期夹具。** owner 在 2026-10-15 要求停止跑夹具，恢复前先与 owner 确认。确认后：
   - 关闭本安装的 RimWorld，运行 `pwsh tools/New-SaveFixture.ps1 -Build 0927,0.5.0,0.4.7 -Force`；
   - 按 plan §5.2 为每个 P 夹具生成 `offline-report.json`；
   - 决定夹具是否纳入版本控制（`DevData/` 被忽略，A5 提出过这个问题）。
6. **补完 4979 上的引擎事实复核。**
   - 已完成的部分见迁移知识库 E52–E54：`Game.LoadGame`、`FinalizeInit`、`InitNewGame`、`FillComponents`、`ScribeLoader.FinalizeLoading`、`ScribeExtractor`、`BackCompatibility` 的成员表与 `BackCompatibleDefName`/`GetBackCompatibleType`/`WasDefRemoved`、`GetSettingsFilename`/`WriteModSettings`、`DefInjectionPackage.TryAddInjection`、`Lord.ExposeData_StateGraph`、bill 与 verb 的加载 ID、`DefMap`、`ShortHashGiver` 等。方法是在 `rw17-4979` 与 `rw17-game`（4977）之间逐方法比较方法体，没有做全程序集差异，也没有核对 Waylith 等引擎文件。
   - 还要复核的（E52 末条列出的，加上 A1/A5/C05 引用而 E52 未覆盖的）：`GenTypes.GetTypeInAnyAssembly` 的 null 缓存、`BackCompatibleEnum`、`GetBackCompatibleTypeDirect`、其余被跳过的入口（`PostExposeData` 等）、`PostLoadIniter` 的顺序、`WorldComponent.FinalizeInit`、`Map`/`World` 的 `FillComponents`、`Thing.IDNumberFromThingID`、`LoadedObjectDirectory`、`JobDriver.ExposeData` 的 toil 序号、`LoadedModManager.ReadModSettings`、隐含 Def 生成器、`JobQueue`、`Job.ExposeData` 对 `lastJobGiverKey` 的处理、C05 的 `DefOfHelper` 是否支持 `DefAlias`。
   - engine-hooks.md §11 第 20 条（第三方翻译中的旧 defName 无法兼容）已被 E53 与 [critique-upgrader](../refactor-1.7/reviews/critique-upgrader.md) #16 推翻，引用时以 E53 为准。
   - 结果写进 engine-hooks.md，新增一节“4979 复核”，可直接引用 E52–E54。
7. **准备 Wave R**，见 plan §2 的前置条件：
   - 在集成树上重新生成规格并通过 lint；
   - 先写出“K1 行 → C# 升级表”的落地方式；
   - 统一 `public-api.json` 的两份副本；
   - 请 owner 签认 A3 列出的顺序变化。

   这些都完成后，才能执行 R。

## 7. 没有 git worktree 时的并行规则

决策 D6 规定不提交 git、不建 worktree，所以并行只能靠严格不相交的文件归属。

- **一个文件同一时刻只有一个写入者。**
  - Wave R 与 R2 期间整棵树冻结，只有执行改名的代理可以写。
  - Wave S 按 `tools/RenameApply/specs/ownership-postR2.json` 分配文件，模块包括 core、content、corporate、incidents、milk、restraints、combat、upgrader、validator、gamevalidation。这份文件是在 scratchpad 的试运行树上生成的，真实 R2 之后要用 `gen_ownership.py` 重新生成，并通过 L16 检查（每个文件恰好一个 owner）。
  - 需要改别人的文件时，在自己的报告中提出请求，由 owner 去改。
- **共享资源归属固定：**
  - 验证器基线 `Source/Tests/MugirlRace.Validation/Baselines/**` 归集成者。只有集成者运行 `--update-baselines` 和 `--ratchet-reset`，各模块不得自行改写。
  - 升级器中央部分（`SaveUpgrade/Engine/**`、`SaveUpgrade/Hooks/**`）归 upgrader。各模块只在 `SaveUpgrade/Modules/<Module>/` 中登记自己的表，用 `[SaveUpgradeModule("<Module>", order)]`。
  - 共享 XML 与 Keyed 文件由 R2 按功能拆分，之后各有唯一 owner。
- **构建隔离。**
  - 不要在现场树中对同一工程并发执行 `dotnet build`，因为 `obj/` 会冲突。
  - 验证一律用 `pwsh tools/Validate.ps1 -WorkRoot <各自目录>`，它通过 `--artifacts-path` 把中间文件放在 WorkRoot 中。
  - WorkRoot 必须在 Mod 目录之外，路径不能太深（Waylith 的 apphost 路径要短于 260 个字符）。
- **闸门串行。** 并行编辑时，现场树中可能有其他模块改到一半的文件，所以闸门（Validate、单元测试、游戏内运行）放在各模块都到达一致点的窗口里串行执行。哪个模块的检查失败，由该模块负责处理。
- **游戏内运行串行。** `Test-InGame.ps1`、`New-SaveFixture.ps1` 与 `SmokeTest.ps1` 共用 `%TEMP%\mugirl-ingame.lock`。本安装的 RimWorld 正在运行时，脚本会拒绝启动；owner 正在玩时要等待，不要结束 owner 的进程。
- **冻结目录与清理。**
  - 永远不改 `1.6/`、`Bio_1.6/`、`Odyssey_1.6/`、`Versions/1.6/`，也不改 `LoadFolders.xml` 中的 v1.6 列表。
  - 清理只删本次生成、带标记的临时目录。要核实绝对路径，不跟随目录链接。

## 8. 风险与未决问题

| 风险/问题 | 说明 | 出处 |
|---|---|---|
| 空升级表也会盖印 | 见 §1 第 1 条。在 Wave V 完成前不得发布，也不得用任何构建读写 owner 的真实存档 | A1 报告“Stamp”、决策 §2 |
| K1 行没有落地路径 | `applied_to_manifest.py` 输出 JSON 行，但还没有把它们变成 `ISaveUpgradeModule` 实现的生成器或约定。生产代码中没有任何模块，`moogirl-era-markers.json` 也未接入（`UpgradeTables.MooGirlTypePrefixes`/`MooGirlDefPatterns` 为空）。R 落地时必须同时带上改名行 | a3-report Requests、`SaveUpgrade/Engine/Tables/` |
| K1 缺 schema、值上下文与可移除容器 | A1 把 K1 v2 推迟了。在 `SaveSchema.Current` 变成 2 之前必须补上；GameValidation 的值匹配也因此只是近似 | a1-report Deferred、a2-report Requests |
| 规格基于 Wave A 中途的快照 | R/R2 规格要在集成树上重新生成并重新 lint，`ownership-postR2.json` 要在 R2 后重新生成 | a3-report Open risks |
| 生成器依赖 scratchpad | `generators/common.py` 写死了 `SCRATCH`；`FINAL/_gen` 在仓库中对应的是 `specs/generators` | §6 第 1 步，plan §2.1 |
| 冻结名单有两份 | 验证器基线 `Baselines/public-api.json` 是 A2 初版（107 条，数组）；`tools/RenameApply/specs/public-api.json` 是 A3 终版（151 条，`entries`）。还没有统一 | a2/a3 报告 |
| 顺序变化待 owner 签认（D9） | A3 列出的变化：GameComponent、MapComponent、FloatMenuOptionProvider、StaticConstructorOnStartup 的反射顺序（右键菜单顺序会变），以及 JobDef、ThingDef、ThoughtDef、SoundDef、TraitDef、FleckDef 的加载顺序。`parity-accept.tsv` 还不存在 | a3-report |
| SelfTest 不含 parity 与提取器对照 | A2 请 A4 把 `Controls/Parity` 与 `Controls/Legacy` 加入 `Validate.ps1 -SelfTest`，没有做；`-SelfTest` 仍传入已被忽略的 `--frozen` | a2-report Requests |
| 升级器从未在游戏中运行 | Harmony 应用、工作线程上的对话框、Phase B 结束工作与 `GotoToil`、安装检查、`WasDefRemoved` 都未经运行验证。字典值目前置空而不是删除；被删除 Pawn 的工作 ID 没有收集，预约可能报错 | a1-report Open risks |
| 引擎版本 | 见 §1 第 4 条。夹具由 4979 写出，在 4979 上读档时原版转换器会被跳过（这是类型改名最严苛的路径）；游戏再更新后会改走转换器路径，UpgradeSuite 会报告走的是哪条路径 | a5-review #21 |
| 补丁优先级 | 移到 399、398、401 的补丁与其他 Mod 在 400 的补丁之间相对位置会变；束具锁现在排在所有其他 Mod 的 GeneratePawn postfix 之后。SDK 检查使得从仓库根目录用更新的 SDK 构建会失败，所以要从 `Source/` 启动 dotnet | a4-report Risks |
| combat S2-8 与 C08 | [combat-plan](../refactor-1.7/module-plans/combat-plan.md) S2-8 仍写“不加多余的优先级”，照做会删掉 A4 的显式优先级。A4 请求修订计划，尚未修订。`B.patch-order` 只与 golden 比较，没有实现“束具锁在所有 Mugirl GeneratePawn postfix 中优先级最低”这条专门断言 | a4-patch-order §6 |
| 模块计划早于 owner 答复 | 计划中的 MooGirl 映射项（如 content L1–L4）、`SaveUpgrade/Steps/<Module>` 目录写法，均以决策 §5（MooGirl 不支持；保持依赖 Mod 可用）与决策 §2（`SaveUpgrade/Modules/<Module>/`）为准 | [决策](../refactor-1.7/decisions.md) |
| 公开 API 落位 | `PublicApi/` 目录还不存在，由 R 创建 `PublicApi/XmlAliases.cs`；`IsMugirl` 目前在 `Features/Mounting/MountedPawnUtility.cs`。规则见 [ADR-008](../architecture/ADR-008-public-api.md) | 决策 §5 |
| 设置文件与冻结的 1.6 构建 | 设置类搬到 `Mugirl.Core.MugirlSettings` 后，1.6 构建每次启动报一次错，改名的键被重置。这是已记录的后果 | 决策 §2 Settings |
| 依赖 Mod | WhiteFluff/Subspecies 的验证代码需要按 `specs/owner-migration-list.md` 更新；1.7 版 WhiteFluff 必须引用 1.7 的 DLL | owner-migration-list |
| 数据只存在本机 | 备份与夹具都在被忽略的 `DevData/` 中，磁盘损坏或误删就无法恢复 | §1 第 2 条 |
| 遗留临时目录 | 已清理：中断的 0.5.0 工作目录 `%TEMP%\mugirl-fixtures\20261015-165428` 已删除（日志未保留，见 §5 表格），锁文件与 `Mods/__*` 临时 Mod 也已清理。`%TEMP%` 下的 `mg17-*` 构建缓存仍在，可随时删除 | §4 |

## 9. 资料位置

| 位置 | 内容 |
|---|---|
| [docs/refactor-1.7/plan.md](../refactor-1.7/plan.md) | R、R2、S、V、D 的执行手册与闸门 |
| [decisions.md](../refactor-1.7/decisions.md) | 约束性决策；§5 是 owner 答复，§6 是 Wave 0 基线事实 |
| [design.md](../refactor-1.7/design.md) | 架构、约定、升级器设计（与决策冲突时以决策为准） |
| [rename-tool.md](../refactor-1.7/rename-tool.md) | RenameApply 的构建、运行、事务、规格格式与生成器 |
| `docs/refactor-1.7/inventory/` | 盘点：[content](../refactor-1.7/inventory/content.md)、[engine-hooks](../refactor-1.7/inventory/engine-hooks.md)（4977）、[real-saves](../refactor-1.7/inventory/real-saves.md)、[validator-tooling](../refactor-1.7/inventory/validator-tooling.md)，以及 module-*.md |
| `docs/refactor-1.7/module-plans/` | 七个模块的 Wave S 计划：[core](../refactor-1.7/module-plans/core-plan.md)、[content](../refactor-1.7/module-plans/content-plan.md)、[corporate](../refactor-1.7/module-plans/corporate-plan.md)、[incidents](../refactor-1.7/module-plans/incidents-plan.md)、[milk](../refactor-1.7/module-plans/milk-plan.md)、[restraints](../refactor-1.7/module-plans/restraints-plan.md)、[combat](../refactor-1.7/module-plans/combat-plan.md) |
| `docs/refactor-1.7/reviews/` | [critique-upgrader](../refactor-1.7/reviews/critique-upgrader.md)（U#）、[critique-validation](../refactor-1.7/reviews/critique-validation.md)（F#；§2.5 闸门、§4 upgrade 套件）、[consistency](../refactor-1.7/reviews/consistency.md)（C#） |
| `docs/refactor-1.7/wave-a/` | A1–A5 的报告与审查，以及 [a4-patch-order](../refactor-1.7/wave-a/a4-patch-order.md) |
| [ADR-007](../architecture/ADR-007-save-schema-upgrade.md)、[ADR-008](../architecture/ADR-008-public-api.md)、[ADR-009](../architecture/ADR-009-naming-and-layout.md) | 存档架构版本与统一升级层、依赖 Mod 使用的公开 API、命名与目录约定（ADR-009 取代 ADR-004）。三份都写于 2026-10-15 收尾时，记录的是目标状态 |
| `tools/RenameApply/` | 改名工具源码（`Program.cs`、`Options.cs` 等）、`frozen-names.json`（与 `specs/public-api.json` 相同） |
| `tools/RenameApply/specs/` | `R/`（7 个模块）、`R2/`（defof、members、patchmodule、content-splits、keyed-split、controls）、`public-api.json`、`owner-migration-list.md`、`ownership-postR2.json`、`moogirl-era-markers.json`、`deferred-S.json`、`upgrade-hand-rows.json`、`*.sample.json`、`lint-report.json`、`generators/` |
| `Source/Tests/MugirlRace.Validation/Baselines/legacy/` | 旧版存档语料，只许追加：`builds.tsv` 与 `legacy-*.tsv` |
| `Source/Tests/MugirlRace.Validation/Baselines/` | 生成的与人工审阅的基线、`save-upgrade-ratchet.<module>.txt`、`save-upgrade-modules.tsv` |
| `DevData/Backups/PreRefactor-1.7-20261015/` | 重构前的整树与 DLL `220e5d1a`（Wave 0） |
| `DevData/Backups/PreR-1.7-20261015/` | Wave A 结束时的整树与 Release DLL `5b0103c6`，即 Wave R 的 parity 基线 A |
| `DevData/Evidence/Baseline-PreRefactor-20261015/` | Wave 0 的验证输出 |
| `DevData/Evidence/Integration-WaveA-20261015/` | 集成的验证输出 |
| `DevData/Evidence/SaveFixtures/` | 夹具（§5） |
| `DevData/Releases/` | 已发布构建的 ZIP（语料与夹具的来源，以 `builds.tsv` 为准） |
| `TMP/InGameValidation/<时间戳>/` | 游戏内运行结果（被忽略的目录） |
| `..\1.7迁移文档` | 迁移知识库，由 Wave D 补充其中的发现 |
