# 验证运行手册

离线检查只能证明结构、引用、存档名字覆盖和常见风险没有明显问题，不能替代 RimWorld 实际加载和游玩。涉及交互、渲染、事件、读档或第三方模组的改动，都要在游戏中验证并查看本次运行的日志。

本手册描述 1.7 的工具（根目录 `tools/`）。`docs/tools/` 中的 1.6 工具已随 1.6 冻结，只作追溯，见文末“1.6 冻结工具”。

## 当前测试方式

- 2026-09-29 起，旧的独立游戏副本及临时运行环境已清理，RimSort 外置目录规避规则已撤销。默认使用构建、离线检查和现有的 Steam 安装；不复制游戏启动程序、运行库，也不建立游戏资源联接。
- 游戏内运行只通过 `SmokeTest.ps1`、`Test-InGame.ps1`、`New-SaveFixture.ps1` 进行。三者都会：
  - 取得运行锁 `%TEMP%\mugirl-ingame.lock`，同一时间只运行一个测试游戏；
  - 在 RimWorld 正在运行时拒绝执行；
  - 使用 `%TEMP%` 中的临时 `-savedatafolder`，不碰玩家的 `Saves/`、`Config/` 与 `ModsConfig.xml`；
  - 音量写为 0，使用隐藏窗口/无窗口参数，不抢前台焦点；`-NoFileLog` 保持 owner 日志不被覆盖；
  - 结束时只删除自己建立、带标记文件的临时目录。
- 被测 DLL 是 `1.7/Assemblies/MugirlRace.dll`。普通构建不写这里；新构建通过独立源码副本显式部署，或用 `Test-InGame.ps1 -BuildMod -Deploy`。每次以当次报告中的 DLL SHA-256 为准，不沿用历史构建的结果；当前验收状态见[恢复记录](records/refactor-1.7-resume-2026-10-15.md)。
- 输出：游戏内结果写入 `TMP/InGameValidation/<时间戳>/`，冒烟结果写入 `TMP/SmokeTest/<时间戳>/`。需要追溯的结果归档到 `DevData/Evidence/<主题>-<日期>/`。

## 工具一览

| 工具 | 作用 | 启动游戏 |
| --- | --- | --- |
| `tools/Validate.ps1` | 离线验证：构建验证器和模组，在标准 .NET 10 与游戏运行时（Waylith CLR）上各运行一遍全部检查并比较结果，再运行单元测试与 Shader 检查 | 否 |
| `tools/Validate.ps1 -SelfTest` | 负对照：`Controls/expected-failures.txt` 中的每条规则在两个运行时上都必须触发 | 否 |
| `tools/SmokeTest.ps1` | 无界面启动两次（只有依赖 / 依赖加雪牛娘），比较雪牛娘新增的 ERROR/WARN 行 | 是 |
| `tools/Test-InGame.ps1` | 在隐藏窗口中运行游戏内套件；`-Fixture` 运行旧档升级套件 | 是 |
| `tools/New-SaveFixture.ps1` | 在 1.7 上重放已发布的旧构建，制作 P 夹具 | 是，耗时长 |
| `tools/Export-LegacySchema.ps1` | 收集旧构建与真实存档，提取旧存档名字语料；`-Verify` 逐字节核对 | 否 |
| `tools/WaylithClr.ps1` | 在游戏自带的运行时上运行一个已构建的 net10.0 程序，供 `Validate.ps1` 使用 | 否 |
| `tools/New-ReleasePackage.ps1`、`tools/Test-ReleasePackage.ps1` | 暂存发布副本并复核，见[发布清单](release-checklist.md) | 否 |
| `tools/Test-Docs.ps1` | 检查必需文档、Markdown 相对链接与锚点、子目录入口文件、`git diff --check` | 否 |
| `tools/RenameApply/` | Wave R/R2 改名工具，见[改名工具](refactor-1.7/rename-tool.md) | 否 |

每个脚本的完整参数和说明，写在脚本开头的注释帮助中。直接阅读脚本头部即可：`Test-InGame.ps1`、`New-SaveFixture.ps1` 等以 `#Requires` 开头的脚本，`Get-Help` 读不到它们的帮助。

### 重构中使用的契约名

Wave A 报告用 K1–K5 指代以下共享契约：

| 名称 | 内容 |
| --- | --- |
| K1 | `Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson()`，manifestVersion=2；含逐行 schema、类型/Def/标签、枚举/值位置、默认值、步骤、Job/Lord 策略、设置、容器和历史组件关系。验证器与 UpgradeSuite 使用同一清单 |
| K2 | `SaveUpgradeDiagnostics.LastReportJson`：上一次读档的升级报告 |
| K3 | 构建与部署策略：普通构建不写 `1.7/Assemblies`，部署只能用 `-p:DeployToMod=true` |
| K4 | `SaveUpgradeBootstrap.Initialize(this)`：在 `MugirlMod` 构造函数中、`GetSettings` 之前调用 |
| K5 | 游戏内运行锁 `%TEMP%\mugirl-ingame.lock`：每行一项，为 `label=`、`pid=`、`token=`、`started=`。环境变量 `MUGIRL_INGAME_LOCK_TOKEN` 与锁中令牌一致时，表示父脚本已持锁 |

## 离线验证（Validate.ps1）

```powershell
pwsh tools/Validate.ps1 -WorkRoot $env:TEMP\mg17-<名字>          # 构建源码并验证
pwsh tools/Validate.ps1 -Shipped -WorkRoot ...                      # 验证已部署的 1.7/Assemblies/MugirlRace.dll
pwsh tools/Validate.ps1 -ModDll <路径> -WorkRoot ...                # 验证指定 DLL（同目录 PDB 一并使用）
pwsh tools/Validate.ps1 -SelfTest -WorkRoot $env:TEMP\mg17-<名字>-st
pwsh tools/Validate.ps1 -UpdateBaselines -WorkRoot ...              # 重写生成的基线
```

- `-WorkRoot` 默认为 `%TEMP%`。脚本每次运行都会清空其中自己的子目录，所以并行的验证必须各用一个 `-WorkRoot`。WorkRoot 不能位于模组目录内（包括经联接指向的路径），路径也不宜太深：Waylith apphost 要求路径短于 260 个字符。
- dotnet 在 `Source/` 中启动，`Source/global.json` 固定的 SDK 因此生效。源码树中的 `bin/obj` 和 `1.7/Assemblies` 都不会被写入。
- 其他参数：`-GameDir`、`-HarmonyDir`、`-AssumeActive`（默认 all：`LoadFolders.xml` 中出现的全部 IfModActive 包）、`-SkipWaylithClr`、`-SkipUnitTests`、`-SkipShaderCheck`。
- 报告：`<WorkRoot>\mg17-validation\report-dotnet.json` 与 `report-waylithclr.json`。`C.save-upgrade` 在同一目录写 `save-upgrade-coverage.tsv` 和 `legacy-token-set.txt`。两个运行时的检查结果和失败/警告键必须一致。
- 退出码：0 全部通过；1 有检查失败或两个运行时不一致；2 构建或环境错误。
- 单元测试 `Source/Tests/MugirlRace.Tests` 由 `Validate.ps1` 构建到 `<WorkRoot>\mg17build\validation-tests\bin\MugirlRace.Tests.exe` 后运行，包括 `SaveUpgrade/` 中的升级器测试。显式基准测试默认不运行，需要时单独执行：`MugirlRace.Tests.exe -explicit only -class Mugirl.Tests.SaveUpgrade.PerformanceBenchmarks`。

### 检查项

检查分为 `A.*` 产物、`B.*` 程序集与运行时、`C.*` 内容与存档；确切项目数以当前程序报告为准。

| 检查 | 内容 |
| --- | --- |
| `A.about`、`A.layout`、`A.assembly-refs`、`A.dependencies` | About.xml 身份与支持版本；`LoadFolders.xml` 与各版本加载的程序集；1.7 程序集的身份与引用；依赖模组能否在 1.7 解析 |
| `B.jit` | 所有类型可加载，所有方法可 JIT，元数据令牌可解析 |
| `B.harmony-apply` | `HarmonyBootstrap.Apply` 在 1.7 游戏上零失败地打完补丁，然后 `UnpatchAll` |
| `B.patch-targets` | 补丁目标按 Harmony 3 语义可解析，与基线一致，并拒绝对本模组自身程序集打 Harmony 补丁 |
| `B.patch-order` | 同一目标、同一种类的多个 Mugirl 补丁优先级互不相同，实际顺序与 `Baselines/patch-order.txt` 一致 |
| `B.patch-metadata` | 每个补丁类都带 `[MugirlPatch]`（模块、风险、失效表现） |
| `B.transpilers` | Transpiler 能在 1.7 IL 上找到锚点 |
| `B.save-confinement` | 补丁存档加载内部的代码只在 `SaveUpgrade/` 中 |
| `B.namespaces` | `PublicApi` 之外没有根命名空间类型，没有遮蔽 Verse/RimWorld 类型的命名空间段；R/R2 原命名空间债务已清理 |
| `B.component-ctors` | Game/Map/World 组件有引擎构造函数，构造函数没有副作用 |
| `B.scribe-defaults` | `Scribe_Values.Look` 的默认值等于字段初值，动态默认值使用 `forceSave`（IL 检查，例外在 `scribe-defaults.allow.txt`） |
| `B.architecture`、`B.base-calls`、`B.empty-types` | 受限 API 与字面量只出现在允许的位置；生命周期调用基类；空类型须有明确的公开 XML 别名或能力标记用途 |
| `B.banned-symbols`、`B.unity-boundary`、`B.waylith-namespace` | `BannedSymbols.txt` 有效且未被绕过；UnityStubs 引用限于审过的边界；Waylith 只在 `Mugirl.Platform` 中引用（[ADR-006](architecture/ADR-006-unity-boundary.md)） |
| `B.reflection-names` | 按名字反射查找的成员在 1.7 中存在 |
| `B.static-caches`、`B.static-assets` | 静态缓存有 `StaticCacheLifecycle:` 注释；静态资源字段只在 `[StaticConstructorOnStartup]` 类型中 |
| `B.log` | 模组代码运行期间没有 `Verse.Log` 警告或错误 |
| `C.content`、`C.xml-types`、`C.patches` | 加载所有启用包的 Def、补丁、语言、贴图与声音；Class/Type 值可解析，元素对应字段；每个补丁操作都能生效，并插入类型正确的 XML |
| `C.def-references`、`C.def-conflicts`、`C.defof`、`C.def-lookups` | Def 交叉引用可解析，可选内容在 `MayRequire` 之后；不重复定义、不覆盖游戏或依赖的 Def；每个 `[DefOf]` 字段都有 Def；代码使用的 defName 存在 |
| `C.content-contracts`、`C.forbidden-patterns`、`C.patch-governance` | `content-contracts.json` 中的内容契约；旧名与拼写错误不进入 1.7 内容和源码（升级层豁免）；每个补丁文件以 `PatchGovernance` 注释开头 |
| `C.content-shadowing` | 没有 Defs/Patches 文件被另一个加载目录中的同名文件遮蔽 |
| `C.definjected-paths` | 每个 DefInjected 键路径都能解析到 Def 的字段（含列表句柄） |
| `C.keyed`、`C.language-files`、`C.translation-keys`、`C.job-reports` | 中英 Keyed 键集与占位符一致；语言文件无遮蔽、重复或孤立的 DefInjected 键；代码中的翻译键中英都存在；每个 JobDef 的 `reportString` 都有简体中文 |
| `C.textures`、`C.sounds`、`C.shaders` | 贴图与声音路径（区分大小写）可解析；ShaderDef、MaterialDef 与 GLSL 源一致 |
| `C.public-api` | 直接读取 `tools/RenameApply/specs/public-api.json` 的完整 151 项，检查冻结形状（[ADR-008](architecture/ADR-008-public-api.md)） |
| `C.save-upgrade` | 旧存档语料中的每个名字都已处置：未变、已改名、被已验证的步骤消费或已丢弃（[ADR-007](architecture/ADR-007-save-schema-upgrade.md)） |

### C.save-upgrade：ratchet 与 strict

- 输入是 `Source/Tests/MugirlRace.Validation/Baselines/legacy/` 中的旧存档语料，一组 TSV 文件（`builds.tsv`、`legacy-types.tsv`、`legacy-labels.tsv`、`legacy-defs.tsv` 等），和升级器的 K1 清单。语料分三层：published（已发布构建）与 observed（真实存档中出现的名字）为必需，dev 只作提示。
- 默认是 **ratchet** 模式：一条未处置的必需行，只有不在 `Baselines/save-upgrade-ratchet.<模块>.txt` 中时才失败；模块划分见 `save-upgrade-modules.tsv`。Wave S 中，各模块逐步清空自己的 ratchet 文件，结果不得变差。
- `--save-upgrade strict` 让每条未处置行都失败，用于 Wave V 的最终闸门。`--ratchet-reset` 用当前未处置行重写 ratchet 文件，只由集成者使用。
- `-UpdateBaselines` 会删除 ratchet 文件中已处置的行。
- 旧 A 阶段的 152 输入/4169 行/108 未处置是历史快照。当前语料已重提取到 v3，并加入 post-R/post-R2 开发证据；处置数量必须取本次 coverage 报告。ratchet 文件中的键引用永久旧语料，不能被改名工具重写来制造“偿还”。

### 验证器的其他模式

`Validate.ps1` 只运行默认模式。其他模式需要直接运行验证器：先用 `Validate.ps1` 构建一次，然后在 `<WorkRoot>\mg17-validation\bin\MugirlRace.Validation\release\MugirlRace.Validation.exe` 上运行，用 `--help` 查看全部选项。

- **parity**（Wave R 闸门）：`--parity <A.dll> <B.dll> --map <RenameApply 报告或 TSV> [--content-a <模组根> --content-b <模组根>] [--parity-accept <tsv>] [--report <json>]`。它经过改名映射比较两个构建的 IL 和内容，未解释的差异必须为零。基线 A 是 `DevData/Backups/PreR-1.7-20261015/` 中的 Wave A 结束构建。
- **parity 负对照**：`Controls/Parity/{A,B}` 是两个对照工程，配合 `--map Controls/Parity/map.tsv --expect Controls/Parity/expected.txt` 使用。`Validate.ps1 -SelfTest` 目前**不运行** parity 对照和提取器对照（`Controls/Legacy`），这两组对照需要手动运行，或者给 `-SelfTest` 补上。
- **--save-corpus `<目录>`**：只读地读取目录下所有 `*.rws` 与 `*.rws.gz`，对每个存档运行升级器的离线入口两次，要求得到已知的非失败状态、第二次没有动作、每个存档不超过 2000 ms。
- **--extract-legacy-schema**：由 `tools/Export-LegacySchema.ps1` 调用。语料对同一提取器版本只追加；提取器版本改变后用 `-Rewrite` 重写，并审阅差异。

### 基线维护

`Source/Tests/MugirlRace.Validation/Baselines/` 中的文件分两类：

- 生成的清单：`load-folders.txt`、`patch-targets.txt`、`patch-order.txt`、`transpiler-hooks.txt`。用 `-UpdateBaselines` 重写，然后像审代码改动一样审阅差异。
- 人工维护的清单：例外、契约、`structure-debt.allow.txt` 等；公开 API 唯一清单在 `tools/RenameApply/specs/public-api.json`。每条例外都要写理由；自动加入的 `REVIEW:` 条目审阅前仍是警告，不能靠换基线接受行为变化。

推荐流程：先 `-UpdateBaselines`，审阅 diff，替换每个 `REVIEW` 理由，再不带该参数运行一次，确认两个运行时一致且通过。

真实 Scribe 测试必须捕获原版 `Log.Error`，同时断言状态和引用身份；解析失败后原版回退创建对象不算成功。离线测试显式登记需要的模组程序集，提供 HAR 运行依赖。4979 的 `GenTypes.ClearCache` 不清理 `GetTypeInAnyAssembly` 的正/负缓存；临时程序集作用域须隔离并恢复它，且用每类全新进程和完整套件分别验证，不能依赖测试顺序。

`Source/Tests/MugirlRace.GameValidation/OfflineTests` 是独立的框架测试工程，目前不在 slnx，也不由 Validate.ps1 自动执行。改动保存比较、读取轨迹或夹具代码后，在独立 Source 副本中另行构建并运行它。`--save-corpus` 只证明 DOM 流程；实际引用别名、ThinkTree、在途 Toil/Lord、暂停保存重载还需要真实 Scribe 或游戏验证。

### 严格读档比较的证据边界

首次重存以原始夹具和纯 DOM 升级预览构造期望，保留精确数值、文本空白、列表位置及容器内 Pawn 的完整状态。比较器不整体豁免 `Consumed` 对象、claimed Job、所有新增字段或默认值。只有具体 reader、实际 Def/组件、确切源路径和自然调用均有证据时，才能投影原版的序列化形式；任何未解释差异仍失败。第二次独立进程读回用于核验升级幂等和当前结构稳定。

测试观察器只在隔离验证命令及临时配置标记均有效时安装；检查 Harmony 3 的四组普通补丁和三组 inner 补丁，也核验观察器自身未被额外补丁改变。观察器不能主动调用市值、业务续接、初始化或随机逻辑来获得期望，不能从被比较的重存档反推答案。IL 指纹或引擎 MVID 改变时，应重新核验源代码证据，不能直接刷新指纹。

`reference-resave` 是诊断套件：在临时目录读入夹具后立即重存，并核验读入、保存前后均未推进 tick。它可运行真正的重构前构建，帮助辨别原本就存在的加载行为。旧构建若报错，只能使用已独立证明的局部证据，不能当作整档正确基准。`think-trees` 启动快照可校准旧版实际节点键；跨布局同一个整数可能指向不同节点，必须核对完整实际读取集合及最终节点身份。

长时间播种可用 `-SuiteOption sweep.ignoreCombatSlowdown=true`。它只在测试隔离条件成立时暂时设置原版加速开关，退出时恢复；仍逐 tick 执行，不跳过游戏逻辑。普通游戏不启用。

## 无界面冒烟（SmokeTest.ps1）

```powershell
pwsh tools/SmokeTest.ps1 -QuickTest -DevMode
```

脚本先启动只含依赖（Harmony、HAR）的基线，再启动依赖加 `HAR.MugirlRace`。它只看 “Running with command line mod list” 之后的 ERROR/WARN 行，求“候选有、基线没有”的行。`-Headless` 下不渲染、不编译 Shader，画面相关的检查由 `Test-InGame.ps1` 负责。退出码：0 通过；1 雪牛娘新增了 ERROR/WARN；2 启动失败或环境错误。

## 游戏内套件（Test-InGame.ps1）

```powershell
pwsh tools/Test-InGame.ps1                                   # default 套件，测已部署的 DLL
pwsh tools/Test-InGame.ps1 -BuildMod -Deploy -Suite default  # 在 %TEMP% 副本中构建，经 K3 部署后测试
pwsh tools/Test-InGame.ps1 -Suite startup,spawn -Config NoBiotech -Language ChineseSimplified
pwsh tools/Test-InGame.ps1 -Plumbing                         # 只加载 Harmony 与测试模组，自检框架
pwsh tools/Test-InGame.ps1 -Fixture all                      # 旧档夹具升级套件
```

- 测试模组 `Source/Tests/MugirlRace.GameValidation` 在 `%TEMP%` 的源码副本中构建，再以带标记的临时模组 `Mods/__MugirlRace_GameValidation_<随机>` 加载。套件在 `Suites/` 下，例如 `startup`、`spawn`、`saveload`、`render`、`plumbing`、`fixture-gate`、`sweep`、`upgrade`；`-Suite default` 和 `all` 是预设组合。
- `-Config` 可选 AllDlc、CoreOnly、NoRoyalty、NoIdeology、NoBiotech、NoAnomaly、NoOdyssey。无 DLC 组合通过临时 `ModsConfig.xml` 实现，因为 `-mod=` 总会启用全部 DLC。
- 结果在 `TMP/InGameValidation/<时间戳>/`，包括 `summary.json`、报告、标准输出、截图和截图分析。退出码：0 通过；1 游戏运行了但有失败；2 构建或环境错误。
- 基线：重构前的 DLL 在 Wave 0 为 65 ok / 1 failed / 14 skipped（`DevData/Evidence/Baseline-PreRefactor-20261015/ingame-default.txt`）。那次失败是测试本身的缺陷，A5 已修复；修复后的 default 运行为 78 ok / 0 failed / 14 skipped（A5 报告）。

### 旧档夹具与升级套件

- 夹具在 `DevData/Evidence/SaveFixtures/<id>/`，每个夹具一个 `manifest.json`，`kind` 表示种类：
  - **P**：在 1.7 上重放已发布的旧构建制作的存档，由 `New-SaveFixture.ps1` 生成。目录为 `P-1004`、`P-0927`、`P-0920`、`P-0.5.0`、`P-0.4.7`。
  - **X-alias**：一个很小的依赖模组，在 Def 中使用旧的公开 XML 类名 `Mugirl.StockGenerator_Mugirl_Slaves`；
  - **X-settings**：已发布构建的设置文件，所有键都设为非默认值；
  - **control**：对照组。被测构建先自己写一份存档，再在新进程中读入，升级器必须不动、差异必须为空。
- `-Fixture <id|all|control>` 对每个夹具启动两次：
  - 第 1 次从主菜单读入夹具，运行 `upgrade` 套件，然后写重存档；
  - 第 2 次在新进程中读入重存档，验证存读档往返时升级器保持不动。

  `all` 等于清单标为 feasible 的全部夹具加 control。`-Fixture` 时默认语言为 ChineseSimplified。其他相关参数：`-TargetModDir`（改测 `Mods` 下的临时旧构建）、`-TokenSet`、`-LegacyCorpusDir`、`-SuiteOption key=value`、`-CollectSaves`。
- 制作 P 夹具：

  ```powershell
  pwsh tools/New-SaveFixture.ps1 -Build 1004            # 或 0927、0920、0.5.0、0.4.7、all
  pwsh tools/New-SaveFixture.ps1 -Build 0927 -GateOnly  # 只运行可行性闸门
  ```

  脚本从 `Baselines/legacy/builds.tsv` 核对构建来源，复制（不联接）到带标记的 `Mods/__MugirlLegacy_<构建>_<随机>/`。随后它先运行可行性闸门，再播种、运行两天，在本模组的工作进行中存档，并从原始 XML 提取事实与覆盖。每个夹具要在真实游戏中运行约 8–10 分钟。
- **当前夹具状态（2026-10-16 文档同步）：** 五个 P 夹具已有可行清单。恢复工作已重做 P-0927、P-0.5.0、P-0.4.7，并核对 gzip、原始存档与来源 DLL 哈希；旧过期文件归档保留，不能用旧 manifest 配另一轮存档。具体播种动作、不可用功能和覆盖事实以各夹具 manifest 及[恢复记录](records/refactor-1.7-resume-2026-10-15.md)为准。
- A5 比较器已改为生产纯 DOM 预览加真实源路径读取轨迹，不因 owner 被 consumed、Job 被 claimed、新增节点或全局 null/default 就略过差异。数组顺序、字典键值、ThingOwner 成员和深层对象身份必须守恒；额外的允许等价须有具体 writer/default 证据与负控。
- 当前 S 候选仍在控制组校准和整合验证中，旧档全套与二次读回尚待最终 V 闸门。历史 R 的 78/0/14、A5 初版失败或模块定向通过都不能代替当前候选的结果。
- control、P、X-settings、X-alias 分别证明自身读回、旧发布数据、设置与公开 XML 契约；不能互相替代。对 Lance/物品工作等真歧义状态，要检查明确失败、备份与旧戳保留，不能要求用结束任务或放宽比较实现“全绿”。
## 游戏内手动验证

自动套件覆盖不到的交互、动画和观感仍需手动检查。用临时的 `-savedatafolder` 或专门的测试存档，不改玩家的配置和存档。

1.7 的日志路径（`-savedatafolder` 不改变它的位置）：

```text
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player-prev.log
%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml
```

### 记录模板

每次手动验证后追加一条记录，避免只留下“已跑完”而缺少可复核的证据。

```text
日期时间：
验证人：
DLC 与模组组合：
RimWorld 版本：
Mugirl DLL SHA-256 与时间戳：
日志路径与 LastWriteTime：
进入主菜单：是/否
新档进入地图：是/否
保存读档：是/否
旧档读入（来源构建）：是/否，升级报告摘要：
红字：无/有，摘要：
黄字：无/有，摘要：
Mugirl 相关异常：无/有，摘要：
外部 mod 噪音：无/有，摘要：
本轮功能点：
结论：通过/不通过/需复查
后续处理：
```

### 建议验证组合

- 最小必需：Harmony、Core、HAR、Mugirl。
- 全 DLC：Royalty、Ideology、Biotech、Anomaly、Odyssey 全开。
- Facial Animation：按 `features/facial-animation-guide.md` 检查头部 comp、表情动画、贴图回退和渲染日志。重点看：
  - `normal_Mugirl`、眨眼；
  - 疼痛时不哭泣；倒地；穿脱衣不用爱心眼；Lovin；
  - `blush/lovinblush` 位于眼白下方；
  - `AttackStatic`/`Wait_Combat` 使用 `sad` 严肃嘴，`Wait_Combat` 不全程半眯；
  - 整张眼睛贴图与高光。

  只有重新启用左右眼 mask 时，才检查 `FA/Eyes/Common`。
- Search and Destroy：雪牛娘闲置战斗行为树不出红字。
- VCookE：奶酪压制机工序被追加，且不重复。
- 依赖模组：WhiteFluff、Subspecies、KindSeal 等使用冻结公开 API 的模组（[ADR-008](architecture/ADR-008-public-api.md)）能正常加载，相关 Def 生效。
- 全集成：以上组合一起加载。

### 功能抽查点

- 新建地图、保存、读档；读入 1.6 时代的旧档后，升级报告、`.rws.mugirl-schema0.bak` 备份与重存档。
- 生成雪牛娘、成长阶段、外观、头发、角、尾巴。
- 产奶、挤奶、喝奶、奶制食物效果。
- 喝奶时 Job 收益正常，双方不播放喝奶动画。喂奶时双方姿态正常。开发者模式下选中雪牛娘打开喂奶姿态微调：邻近被喂者的预览应在地图中立即响应六项滑杆，关闭后恢复朝向和动画状态；配置导出后重启能加载。
- 穿戴 X 乳贴和高叉毛衣：乳贴被毛衣遮挡，但仍位于裸身之上。检查雪牛投掷、流星大灌篮的能力图标和三种地板奶渍贴图。
- 束具：穿戴、卸下、破解、Hediff 清理、Gizmo 点击。
- 牵引：牵引、被牵引、牵到建筑、混合牵引普通 pawn。
- 骑乘：骑乘、下骑、骑乘近战、装备绘制。
- 雪牛投掷（Biotech）：
  - 重量上限：等于 `CarryingCapacity` 的目标可以举起，超过的被拒绝；整堆物品按整堆重量判定。
  - 建筑重量：分别尝试孤立的墙体/山体，以及占用格或四边邻格连接墙、家具、墙灯或地下电缆的 `Building` 目标。所有未显式定义 `Mass` 的 `Building` 至少按完整最大耐久度 × 10 计算，同时保留占地面积、建造材料造成的更高重量。`Mineable` 即使有较小的显式 `Mass`，也不能低于完整最大耐久度 × 10。
  - 参考值：原版墙体基础耐久 300，对应 3000 kg，具体材质按实际最大耐久计算；花岗岩山体 9000 kg。550 kg 搬运上限都应被拒绝，提升到足够高后才允许举起；打残目标不能降低门槛。显式有 `Mass` 的花岗岩岩块 `ChunkGranite` 仍按原规则判定。
  - 流程：举起、瞄准落点、命中、放下、中断，以及存读档后，目标不丢失、不复制。
- 投掷飞行视觉：
  - 在正常、快速和暂停状态下，位置与旋转连续；空中旋转保持每游戏 tick 42°，飞行途中能转多圈。物体有多个渲染帧而没有新游戏 tick 时，仍平滑前进和旋转。
  - 镜头移到落点、施术者离开视野时，飞物不消失；读档后继续飞行，不跳到终点。
  - 显示补间不能提前穿透屋顶或触发命中，落地仍在预定的游戏 tick 发生。
- 投掷禁火：举起瞄准与物体飞行期间，持枪施术者的右键射击菜单和小队齐射显示“正举着待投掷的物体”，不可下达。物体落地或放回后立即恢复射击。投掷期间近战指令与能力施放不受影响；放下物体取消投掷后可以正常开枪。
- 雪牛流星大灌篮（Biotech）：
  - 普通模式不显示能力，也不能从残留命令触发；开发者模式下只接受仍有头的人形 `Pawn`。
  - 摘头后身体留在原处。对照目标摘头前的头部和头发，手中篮球的实际绘制尺寸一致；拍球、起跳、灌篮和落点效果正常。
  - 篮筐附近没有可跳落点时，在选目标阶段拒绝，不能出现原地起落、头在远处爆裂。
  - 起跳和上升阶段，头颅贴近施术者手部，与 `PawnFlyer` 同步移动，不提前脱手或消失。
  - 空中头部的位置与旋转：在正常、快速和暂停状态下观察，旋转保持每游戏 tick 46°，飞行途中能转多圈；同一游戏 tick 内的多个渲染帧平滑补间；镜头移到头部、控制器离开视野时，头部仍然显示。
  - 下扣：施术者越过最高点开始下坠时，完整头颅从手边连续地转为加速下扣，边界处不先悬停或回跳。下扣明显快于雪牛娘；头的触地点在空中雪牛娘面朝方向的前方、画面下方，并在她落地前砸地。
  - 触地：下扣期间不提前爆血或碎头。触地时音效、血液与碎裂在同一瞬间出现，头立刻消失，不弹跳。
  - 血液：分别观察摘头瞬间、拍球触地和灌篮触地碎裂时的血液飞溅；`CanBleed` 为 false 的目标不产生血液或血沫。
  - 异常情况：对无头、非人形、目标死亡或离图、施术者中断分别检查拒绝提示和清理；保存读档后没有残留的控制器或异常。
- 事件和任务：开局坠舱、逃奴加入、野人事件、快递事件。

## 1.6 冻结工具（只作追溯）

`docs/tools/` 中的脚本和 `.cs` 驱动是为 1.6 构建（`1.6/Source`、.NET Framework 4.8）编写的：

- 静态验证与发布：`Invoke-Phase6StaticValidation.ps1`、`Invoke-PlayerLogScan.ps1`、`New-WorkshopPackage.ps1`、`New-GameValidationConfigs.ps1`；
- 专项入口：`Start-*Validation.ps1`、`Test-*.ps1`；
- 运行期探针：`Corporate*`、`EventNormalizationRuntimeProbe`、`WeaponWheelHuntRuntimeProbe` 等。

它们随 1.6 一起冻结，不用于 1.7。历史记录（例如 [10.11 记录](records/bug-report-fixes-2026-10-11.md)、[10.04 记录](records/event-normalization-2026-10-04.md)）中的命令只说明当时如何验证。复制游戏的五个旧入口（`Start-CorporateValidation.ps1`、`Start-GrazeRuntimeValidation.ps1`、`Start-ThrowRuntimeValidation.ps1`、`Start-LanceRuntimeValidation.ps1`、`Start-PregnancyMoodValidation.ps1`）早在 2026-09-29 就已停用。这些 `.cs` 文件不是清理对象，也不能加入发布 DLL。

1.7 的同类检查分别是：

- 离线规则：`Validate.ps1` 的检查项；
- 运行期行为：`Test-InGame.ps1` 的套件；
- 着色器：`C.shaders` 与 `Source/Tests/MugirlRace.ShaderCheck`。1.7 的着色器是 `1.7/Shaders` 下的 GLSL，不使用 AssetBundle（[ADR-006](architecture/ADR-006-unity-boundary.md)）。

资源工具：

| 工具 | 行为 | 1.7 状态 |
| --- | --- | --- |
| `Invoke-TextureAssetValidation.ps1` | 只读检查资源路径、PNG 与 FA 纹理约定 | 1.6 工具；1.7 的贴图路径由 `C.textures` 检查 |
| `Build-OutlineAssets.ps1`、`Build-LanceMotionBlurAssets.ps1` | 在 `TMP` 建立 Unity 工程，构建描边与骑枪运动模糊的 AssetBundle | 只服务冻结的 1.6 构建（`1.6/Resources`） |
| `Optimize-TextureAssets.py` | 555→512 纹理转换的准备、应用和验证；`--verify` 只读复查 | 作用于共用的根 `Textures/`，使用前核对路径 |
| `Normalize-TransparentPixels.py` | 默认只读扫描；`--apply` 先备份，再修正透明像素，保留受保护遮罩 | 同上 |
| `Repair-AppearanceTextureArtifacts.py` | 默认只读验证；`--apply` 先备份，再执行指定的身体纹理修复 | 同上 |

Python 资源工具需要 Pillow 和 numpy。原图和处理清单存入 `DevData/Backups`，不要因为目录未被 Git 跟踪而删除。描边的源码和详细验收边界见[描边维护](features/outline-guide.md)。
