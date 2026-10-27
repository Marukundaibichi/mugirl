# Mugirl Race · 雪牛娘开发手册

**下次开发从本文件开始。** 本文件是唯一的开发手册，统一维护开发流程、构建与验证命令、目录用途、功能导航和历史索引；[AGENTS.md](AGENTS.md) 只保存 AI 必须遵守的规则。子目录不设 README 或 AGENTS 入口，专题正文都在 `docs/`。

## 当前状态（2026-10-27）

- **1027 本地发布包已完成，源码与正式产物已提交 Git。** [雪牛娘1027.zip](DevData/Releases/雪牛娘1027.zip)包含投掷建筑连接修复、墙壁栓点开关移除和低优先级泌乳人物封存设置；2953 个运行文件及外置清单逐哈希核验通过，附[校验文件](DevData/Releases/雪牛娘1027.zip.sha256)。4990 的分片贴图检查与工坊依赖测试启动方式已适配；历史与控制组 17 次实机会话为 710/0/22。来源提交 `2c26e33`，本轮未推送远程或上传工坊，详见[1027 发布记录](docs/records/release-1027-2026-10-27.md)。

- **2026-10-27 泌乳 Hediff 已允许低优先级世界人物封存。** `Mugirl_Lactation` 设置 `allowMothballIfLowPriorityWorldPawn=true`，避免仅因泌乳状态让离图雪牛娘长期活跃。封存期间暂停后台产奶，已有奶量与设备库存保留；地图及商队中的产奶不受此项影响。重启游戏加载定义后生效，无须重新部署 DLL。行为边界见[泌乳与世界人物封存](docs/content-update-guide.md#泌乳与世界人物封存)。

- **2026-10-27 墙壁栓点多余电力开关已从 1.7 定义移除。** 4990 下专项实机 26/0/0，覆盖按钮、实际栓系、旧开关状态读档和拆除解绳；当时离线检查无新增失败，既有贴图检查与文档链接问题随后在 1027 发布阶段修正。此 XML 修改重新加载模组定义即可生效。原因、启动重试和验证边界见[墙壁栓点开关修复](docs/records/wall-hitch-switch-2026-10-27.md)。

- **2026-10-26 投掷建筑连接误禁修复已于 10-27 随 1027 正式部署。** 普通家具靠墙、桌椅相邻、覆盖隐藏电缆及普通设备接电不再被当成结构连接；墙、山体、围栏、沙袋和电缆等按相容的连接属性继续限制，重量规则不变。修复前后的真实建筑对照与验证边界见[投掷建筑连接修复](docs/records/throw-building-connections-2026-10-26.md)，本轮正式构建验证见[1027 发布记录](docs/records/release-1027-2026-10-27.md)。

- **觅食全图扫描植物造成的卡顿已修正，并于 10-25 正式部署、同步 Git。** 按[决策 §18](docs/refactor-1.7/decisions.md#18-觅食性能修复的部署与-git-同步2026-10-25)：
  - 雪牛娘食性含植物，原版人形觅食会对全图每株植物先评分再校验。植物多的地图上一次觅食 20–30 ms，没饭时每次思考都会重扫。
  - 现在植物必然被原版拒收时（未到紧急饥饿）只搜索非植物食物，选中的食物与原版相同。紧急饥饿、濒死与野生状态下的吃草搜索不变。
  - 用新增的游戏内套件 `perf` 和内置分析器实测：同局对照中饥饿雪牛娘的 `BestFoodSourceOnMap` 由 13–31 ms 降到 0.02–0.1 ms（完整的 `JobGiver_GetFood` 决策不超过 0.2 ms）；插桩下最慢的雪牛娘 tick 由约 100 ms 降到 5 ms。
  - 这次部署的是当前完整源码，同时带上此前未部署的巨企供货修复（10-19）与代码质量优化（10-22）。部署后的检查见下面的“当前显式部署”。
  - 详情、测量方法与边界见[觅食植物扫描性能调查](docs/records/performance-food-search-2026-10-25.md)。

- **2026-10-22 代码质量审查与优化已于 10-25 随觅食修复正式部署。** 按[决策 §17](docs/refactor-1.7/decisions.md#17-代码质量优化授权2026-10-22)：
  - 修复了两个缺陷：骑乘期间骑手的逐 tick 状态不推进，挤奶释放音效重复播放。
  - 未开户时不再在加载期构建巨企目录，武器轮盘空闲 tick 去重。
  - 移除了构建期类型排序工具、反射生命周期注册表和若干死代码。
  - 离线验证两个运行时各 45/45、strict 与 5 个夹具通过。部署后的自动实机检查见下面的“当前显式部署”；记录中 Q1、Q2、Q5 的手动实机项仍未逐项核对。
  - 审查结论、暂缓项与验证范围见[代码质量审查与优化](docs/records/code-quality-optimization-2026-10-22.md)。

- **开发目录已清理，体积由约 26.93 GB 降至 4.93 GB。** 1076 份大型历史验证文件已逐文件压缩并解压校验，472 个构建输出已校验归档后移除；模组源码、素材、正式 DLL/PDB 及未提交修复保持原样。原始证据可按下方清理约定恢复，详情见[历史验证资料压缩](docs/records/workspace-cleanup-2026-09-21.md#2026-10-20-历史验证资料压缩)。

- **物资订单的通用组件误禁已在源码修正并通过隔离实机验证。** 旧版复现了全目录 668 种物品全部禁购、单物品注入则仅该物品禁购；修复版 689 种可供物品在三种组件注入场景下均无新增禁购，20 项交易与合同检查通过。供货判定现只因明确产出入口触发相应限制，通用存档/记账组件不再导致禁购。价格、限购和已付款合同规则保持。已于 10-25 随觅食修复部署，`corporate-supply` 在正式 DLL 上为 20/0/0。验证范围与边界见[供货兼容重审](docs/records/corporate-supply-compatibility-2026-10-19.md)。

- **1017 在游戏 4981 上的版本拦截已确诊，升级层已按实际接口重审并部署修复。** `Player (5).log` 证实旧构建因 MVID 白名单失效；现不再按每日构建编号禁用迁移，可选 AI 布局捕获单独降级，缺树等来源问题保留原键和进度并警告。真实未完成步骤的提示包含原因和构建身份。具体证据、全量玩家来源重放及验证边界见[日更兼容重审](docs/records/thinktree-key-migration-2026-10-16.md#4981-日更后重新审视兼容边界2026-10-17)。**下述原 1017 ZIP 仍是旧构建，不包含本轮修复。**
- 当时交付的 `雪牛娘1017-兼容修复.zip` 已完成逐文件核验，当前工作区不再保留该 ZIP；[校验文件](DevData/Releases/雪牛娘1017-兼容修复.zip.sha256)与[兼容修复包记录](docs/records/thinktree-key-migration-2026-10-16.md#本地兼容修复包)仍可追溯。其来源清单记录未提交修复，当时未更新远程 Git 或上传创意工坊。
- **1017 已本地打包，包含已部署的 AI 来源警告修复。** 来源提交 `7c96f9c`，清单记录干净工作区；沿用本轮实机验证的 DLL/PDB，来源、检查和交付信息见[1017 发布记录](docs/records/release-1017-2026-10-17.md)。
- **AI 来源警告策略已修复并正式部署。** 按[决策 §14](docs/refactor-1.7/decisions.md#14-ai-来源警告策略与部署授权2026-10-17)，无法确认的来源保留原键与任务进度，只记警告，不再阻断整档升级。可选待解析标记防止 schema 1 重载误绑同号节点；已证明的来源正常迁移。`Player (4).log` 的跨树 `UseToilet/-1642323718` 和未捕获 `LayDown/566784108` 均有旧版失败、新版通过的实机对照。详情见[本轮修复与测试](docs/records/thinktree-key-migration-2026-10-16.md#ai-来源改为警告与进度保留2026-10-17)。
- **1016.3 原发布包保留，新的警告策略修复从 1017 交付。** 历史交付见[1016.3 发布记录](docs/records/release-1016.3-2026-10-17.md)。玩家备份中三条休息来源已通过抽取 Scribe 的升级、重载与原键保留；原节点并未恢复，完整 270 模组环境未重建，不能声称实际验证了卫生模组驱动或完整玩家世界。
- **1016.2 已本地打包，包含正式部署的 ThinkTreeKeys 外部未知任务键兼容修复。** 原版无法解析的外部来源不再一律否决雪牛娘迁移，原整数保留到后续保存；已识别键的布局、所有权和歧义检查仍生效。详情见[ThinkTreeKeys 修复与部署记录](docs/records/thinktree-key-migration-2026-10-16.md)。已有的 1016.1 发布包保留原样。
- **1016.2 的扩展控制组未全绿。** 前次两轮控制组均发现重叠污渍读回差异，第二轮另触发缺眼角色穿束具的既有报错；失败原样保留，未放宽比较或改动无关玩法。用户获知后要求更新 Git 并打包 1016.2，按该次要求交付；本轮正式部署未重跑这些控制组，不能据此宣称完整发布验收已通过。详见 [1016.2 发布记录](docs/records/release-1016.2-2026-10-16.md)。
- **1016.1 旧档同名奶量兼容修复已部署并通过发布验收。** `Milk.ProductionLabels` 分离雪牛娘与其他产奶组件的旧 `milkFullness`，保留各自数值；所提供完整存档通过离线迁移及幂等检查，16 对奶量通过隔离游戏中的双组件存读档。原理见[同名奶量迁移修复记录](docs/records/milk-label-migration-2026-10-16.md)，本轮结果见[1016.1 记录](docs/records/release-1016.1-2026-10-16.md)。
- 只维护 RimWorld 1.7。1.6 构建及其目录已冻结（[ADR-005](docs/architecture/ADR-005-version-layout.md)）。
- **1.7 迁移、R/R2/S 重构、V 验收已完成，1016 已适配 1.7.4980。** 按[追加要求](docs/refactor-1.7/decisions.md#8-1016-发布的追加要求2026-10-16)，旧档成功升级后发一封信说明自动升级和备份位置；保存 schema 1 后再次载入不重复发送。其他玩法、数值及旧文案保持原契约；升级层集中处理旧数据。详情见[1016 记录](docs/records/release-1016-2026-10-16.md)，重构历史见[完成记录](docs/records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)。
- 当前显式部署的 `1.7/Assemblies/MugirlRace.dll` SHA-256 为 `71EA8AE1CFD35C3AFEBC7BA2A6DC138E57920B87A77E7B19432E30E6BF80FAB3`（2026-10-27）。两个运行时常规与 strict 检查各 45/45，常规单测 1099 项通过、2 项显式未运行，Shader 检查通过；全 DLC 默认回归与投掷专项 153/0/4，启动冒烟无新增错误/警告。完整结果见[1027 发布记录](docs/records/release-1027-2026-10-27.md)。
- 上一正式构建 `C3ED6AA47454470F4EC61C2DDCAE114BA6ACEB97A2330C16FE4904003507DF5A`（2026-10-25）的历史检查如下：
  - `-Shipped` 两个运行时各 45/45，strict 存档升级通过，单测 1090 项无失败；
  - 默认实机 79/0/4，启动冒烟通过，`perf` 与 `corporate-supply`（20/0/0）通过；
  - 旧档夹具 562/0/19，与 10-16 相同；控制组 147/2/3，2 个失败是既有的缺眼角色穿束具报错。

  详见[部署与提交](docs/records/performance-food-search-2026-10-25.md#部署与提交2026-10-25)。上一构建 `1BD2FC9424693F39836E43440EBD039EEC95BF92B610F2B6569901B4A2BC4EE6` 的记录如下：常规单测 1086 项通过（另 2 项显式未运行），框架自测 410 项通过，正式 DLL 两运行时各 45 项 strict 检查通过。玩家完整 XML 离线迁移及幂等通过，106 条来源、缺树/任务进度专项和默认游戏回归合计 1335/0/5。一次新世界生成人物失败的 1332/4/5 记录仍保留，未声称已修复；完整玩家环境本机缺少 126 个模组，不能将来源重放当作完整世界复现。详情见[本轮证据](DevData/Evidence/ThinkTreeReaudit-20261017/)和[重审记录](docs/records/thinktree-key-migration-2026-10-16.md#4981-日更后重新审视兼容边界2026-10-17)。上一构建证据保留在[来源警告修复](DevData/Evidence/ThinkTreeWarnings-20261017/)。
- [1017 本地发布包](DevData/Releases/雪牛娘1017.zip)包含 2953 个发布文件和外置清单，共 2954 个 ZIP 文件均已核对路径、大小和 SHA-256，DLL/PDB 配对通过；另有[校验文件](DevData/Releases/雪牛娘1017.zip.sha256)与[发布记录](docs/records/release-1017-2026-10-17.md)。修复与记录已提交 Git，未上传创意工坊。冻结 1.6 的 1323 个文件和 v1.6 加载列表均未改动；[1016.3 原包](DevData/Releases/雪牛娘1016.3.zip)、[1016.2 原包](DevData/Releases/雪牛娘1016.2.zip)、[1016.1 原包](DevData/Releases/雪牛娘1016.1.zip)及[1016 原包](DevData/Releases/雪牛娘1016.zip)继续保留。
- 当前游戏是 **1.7.4990 rev1069**（1027 构建与实机验证所用；10-25 使用 1.7.4985 rev1269），编译依赖为实际 HAR 1.7。生产升级按当前接口、字段写出约定及实际数据条件工作；构建编号用于诊断，不设按每日 MVID 放行的白名单。历史 4979/4980 核验与 E57–E62、E64–E69、E71–E72 保留在[迁移知识库](../1.7迁移文档/资料来源与核验记录.md)。

后续开发先读本手册及[完成记录](docs/records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)。[原交接记录](docs/records/refactor-1.7-handoff-2026-10-15.md)和[波次计划](docs/refactor-1.7/plan.md)用于追溯，不能对当前树重放历史改名；[约束性决策](docs/refactor-1.7/decisions.md)继续生效。

## 开始一次开发

1. 先读[完成记录](docs/records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)，确认被测构建和已知边界。并行改动仍须有不相交的文件归属；新任务不能把历史计划中的行为修改当作授权。
2. 用 `git status --short` 记录工作区基线，以 `DevData/Backups/` 的整树归档保留已有源码、DLL、素材、存档和改动。1.7 迁移与重构阶段曾按 owner 要求保持未提交，1016 发布按新的明确要求更新 Git；后续仍以当次授权为准，不自行执行 commit、stash、checkout、reset 或建立 worktree。
3. 在下方“按任务找代码和文档”中定位功能。改数值前确认内容规则；改兼容前确认 DLC 或第三方模组的边界；改任何会进存档的名字或结构前，先读 [ADR-007](docs/architecture/ADR-007-save-schema-upgrade.md)。
4. 用自己的工作目录运行离线基线 `pwsh tools/Validate.ps1 -WorkRoot <独立目录>`，记录已有失败。
5. 修改功能，同步更新 Def、翻译、补丁元数据、升级表及专题文档。不要顺手改变无关的玩家行为或数值。
6. 运行相关检查。涉及交互、渲染、事件、读档或兼容时，按[验证手册](docs/validation-runbook.md)在游戏中验证；被测 DLL 只能通过显式部署写入 `1.7/Assemblies`。
7. 记录实际结果与限制。发布前按[发布清单](docs/release-checklist.md)检查；需要追溯的日志与备份按本页归档约定保存。

## 工程与构建

- 解决方案为 `Source/MugirlRace.slnx`，包括：
  - `MugirlRace/MugirlRace.csproj`：模组 DLL；
  - `Tests/MugirlRace.Tests`：单元测试，含 `SaveUpgrade/`；
  - `Tests/MugirlRace.Validation`：离线验证器；
  - `Tests/MugirlRace.ShaderCheck`；
  - `Tests/MugirlRace.GameValidation`：游戏内测试模组。
- 工程设置在 `Source/Directory.Build.props`：目标 `net10.0`、C# 14、nullable，警告即错误。构建依赖本机的 RimWorld 1.7、Harmony 3（创意工坊 2009463077 的 `1.7/Assemblies`）和 HAR。路径可以用 `-p:RimWorldDir=`、`-p:WorkshopDir=`、`-p:HarmonyAssembliesDir=` 覆盖。
- HAR 编译引用使用实际加载的 `839005762/1.7/Assemblies/AlienRace.dll`。它已直接引用 UnityStubs，因此旧 HAR 所需的 UnityEngine.CoreModule 编译门面已移除；生产程序集仍不得依赖 `UnityEngine*`，游戏边界类型的使用范围由 ADR-006 约束。
- SDK 固定为 10.0.401（`Source/global.json`，`rollForward latestFeature`，不接受预览版）。`global.json` 只在 dotnet 从 `Source/` 启动时生效。在其他目录启动、SDK 又不符时，`MugirlCheckSdkPin` 会报错，所以构建命令都在 `Source/` 中运行。
- 编译与类型顺序就是普通 SDK 工程的默认顺序，构建后不再重排元数据（[决策 §17](docs/refactor-1.7/decisions.md#17-代码质量优化授权2026-10-22)）。不要依赖类型枚举顺序；需要固定顺序的地方（补丁优先级、生命周期清理、升级模块）都显式排序。
- 构建与部署策略（K3）：普通构建只输出到 `Source/MugirlRace/bin/<配置>/`，从不写 `1.7/Assemblies`；部署必须显式进行。

  ```powershell
  cd Source
  dotnet build MugirlRace.slnx                                              # 全部工程，不部署
  dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true  # 把 DLL 与 PDB 写入 1.7/Assemblies
  ```

  `-p:MugirlDeployDir=<目录>\` 可改变部署目标；在源码副本中构建时，用它指回真实的模组目录。
- 隔离构建：多人或多个代理并行工作时，各自使用独立的 `-WorkRoot`，或在 `%TEMP%` 的源码副本中构建。`Validate.ps1` 的产物全部写进 `-WorkRoot`；`Test-InGame.ps1` 会把 `Source/` 复制到 `%TEMP%` 再构建。工作目录不能放在模组目录内，因为 1.7 的游戏内创意工坊上传会复制模组目录中的全部文件。
- Harmony 补丁顺序：同一目标上的多个 Mugirl 补丁只用显式 `[HarmonyPriority]`（或 `HarmonyBefore`/`HarmonyAfter`）排序。A4 用显式优先级复现了重构前的实际顺序（[推导记录](docs/refactor-1.7/wave-a/a4-patch-order.md)），由 `B.patch-order` 对照 `Baselines/patch-order.txt` 检查。

## 常用命令

所有命令从仓库根目录执行。参数、检查项和结果判读见[验证手册](docs/validation-runbook.md)。

```powershell
# 离线验证：在标准 .NET 10 与游戏运行时上各跑一遍全部检查，再运行单元测试与 Shader 检查
pwsh tools/Validate.ps1 -WorkRoot $env:TEMP\mg17-<名字>
# 验证器自检：负对照中的每条规则都必须触发
pwsh tools/Validate.ps1 -SelfTest -WorkRoot $env:TEMP\mg17-<名字>-st
# 重写生成的基线，然后审阅差异，替换 REVIEW 理由
pwsh tools/Validate.ps1 -UpdateBaselines -WorkRoot $env:TEMP\mg17-<名字>

# 无界面启动冒烟，测已部署的 DLL：比较雪牛娘新增的 ERROR/WARN
pwsh tools/SmokeTest.ps1 -QuickTest -DevMode
# 游戏内套件，默认测 1.7/Assemblies 中的 DLL
pwsh tools/Test-InGame.ps1
pwsh tools/Test-InGame.ps1 -BuildMod -Deploy     # 构建、部署后再测
pwsh tools/Test-InGame.ps1 -Fixture all          # 旧档夹具升级套件（每个夹具启动两次）
# 重放已发布的旧构建，制作 P 夹具（长时间占用游戏）
pwsh tools/New-SaveFixture.ps1 -Build 1004
# 旧存档名字语料：逐字节核对
pwsh tools/Export-LegacySchema.ps1 -Verify

# 发布：在模组目录外暂存并复核
pwsh tools/New-ReleasePackage.ps1
pwsh tools/Test-ReleasePackage.ps1
# 文档检查：必需文档、相对链接、子目录入口、空白错误
pwsh tools/Test-Docs.ps1
```

`SmokeTest.ps1`、`Test-InGame.ps1`、`New-SaveFixture.ps1` 共用游戏内运行锁 `%TEMP%\mugirl-ingame.lock`，同一时间只运行一个测试游戏。RimWorld 正在运行时，这三个脚本拒绝执行。它们使用临时的 `-savedatafolder`，不碰玩家的存档和配置。

`docs/tools/` 中的 `Invoke-Phase6StaticValidation.ps1`、`New-WorkshopPackage.ps1` 等是 1.6 时代的工具，已随 1.6 冻结，只作追溯，不用于 1.7。

## 按任务找代码和文档

下表代码目录均相对于 `Source/MugirlRace`。涉及 Def 数据时，同时检查 `1.7/Defs` 和 `1.7/Mods/<名称>` 条件加载目录：C# 负责规则和流程，XML/Def 承载数值与内容。

**Wave R/R2 已按 `tools/RenameApply/specs/` 完成内部名字与职责归属整理。** 并行开发以 `tools/RenameApply/specs/ownership-postR2.json` 为准。为保留 Def 枚举和随机调用顺序，共享 XML 与翻译文件保留原物理位置，各 Def 的逻辑归属见 R2 规格。

| 任务 | 代码入口 | 阅读材料 |
| --- | --- | --- |
| 启动、日志、设置、补丁注册 | `Core`（`MugirlMod`、`HarmonyBootstrap`、`MugirlPatchAttribute`、`MugirlLog`、`MugirlSettings`、`MugirlDefOf`），各功能自己的 DefOf | [启动修复记录](docs/records/bootstrap-startup-2026-09-21.md)、[core 计划](docs/refactor-1.7/module-plans/core-plan.md)、下方代码约束 |
| 存档升级与每局生命周期 | `SaveUpgrade`、`Core/GameComponent_Mugirl.cs`、`Features/GameLifecycle.cs` | [ADR-007](docs/architecture/ADR-007-save-schema-upgrade.md)、[决策 §2/§5](docs/refactor-1.7/decisions.md)、[A1 报告](docs/refactor-1.7/wave-a/a1-report.md)、[真实存档清单](docs/refactor-1.7/inventory/real-saves.md) |
| 平台封装、渲染与着色器 | `Platform`（Waylith 封装）、`Rendering`、`Mathematics`；界面在各功能的 `UI` 子目录或 Core 设置入口；`1.7/Shaders`、`1.7/Defs/ShaderDefs`、`1.7/Defs/MaterialDefs` | [ADR-006](docs/architecture/ADR-006-unity-boundary.md)、[描边](docs/features/outline-guide.md)、[骑枪运动模糊](docs/features/lance-charge.md) |
| 新增服装、装备、Hediff、Job 或事件 | 对应的 `Features` 模块 | [内容更新指南](docs/content-update-guide.md)、[7.15.3 服装设计](docs/features/apparel-7.15.3-design.md)、[content 计划](docs/refactor-1.7/module-plans/content-plan.md) |
| 开局坠舱、民间随机事件与未招募互动 | `Features/Incidents`、`Features/Milk` | [事件入口与生成规则](docs/content-update-guide.md#雪牛娘事件入口与生成规则)、[10.04 整合记录](docs/records/event-normalization-2026-10-04.md)、[incidents 计划](docs/refactor-1.7/module-plans/incidents-plan.md) |
| 外观、头发、脸部附件与造型台 | `Features/Appearance`（含 `Apparel`）、`Compatibility/HumanoidAlienRaces`；FA 内容在 `1.7/Mods/FacialAnimation` | [外观刷新](docs/features/styling-station-refresh.md)、[FA 专题](docs/features/facial-animation-guide.md)、[描边](docs/features/outline-guide.md) |
| 巨企、委托、贷款、投资与交易 | `Features/Corporate`、`Features/Incidents` | [设计](docs/features/giant-corporation-design.md)、[实现范围与验证](docs/features/giant-corporation-v1-validation.md)、[服务规则](docs/features/corporate-services.md)、[corporate 计划](docs/refactor-1.7/module-plans/corporate-plan.md) |
| 科技树、研究项目与内容解锁门槛 | `1.7/Defs/ResearchProjectDefs`（纯 XML，无专属 C# 模块） | [研究树设计](docs/features/research-tree-design.md) |
| 武器轮盘、自驱装弹、骑枪、神经战斗套装与能力 | `Features/WeaponWheel`、`Features/Weapons`、`Features/Lances`、`Features/AdvancedArmor`、`Features/Abilities`（投掷、灌篮、冲锋、吼叫） | [轮盘设计](docs/features/weapon-wheel-design.md)、[骑枪破墙与运动模糊](docs/features/lance-charge.md)、[9.20 更新记录](docs/records/update-2026-09-20.md)、[combat 计划](docs/refactor-1.7/module-plans/combat-plan.md) |
| 产奶、束具、牵引、骑乘 | `Features/Milk`、`Features/Restraints`、`Features/Roping`、`Features/Mounting` | [验证手册](docs/validation-runbook.md)、[性能实施记录](docs/records/performance-optimization-2026-09-07.md)、[milk 计划](docs/refactor-1.7/module-plans/milk-plan.md)、[restraints 计划](docs/refactor-1.7/module-plans/restraints-plan.md) |
| 基因、新生儿、放牧与社交 | `Features/Xenotype`、`Features/LifeStages`、`Features/Grazing`、`Features/Social` | [内容更新指南](docs/content-update-guide.md)、[怀孕心情记录](docs/records/pregnancy-mood-2026-09-21.md) |
| 第三方模组兼容 | `Compatibility`、`1.7/Mods/<名称>` | [兼容维护](docs/compatibility-guide.md)、[ADR-003](docs/architecture/ADR-003-compatibility-scope.md)、[ADR-008](docs/architecture/ADR-008-public-api.md) |
| 旧存档兼容 | `SaveUpgrade`：统一引擎、读取挂钩和各模块的升级步骤；领域代码只读写当前结构 | [ADR-007](docs/architecture/ADR-007-save-schema-upgrade.md)、[内容清单](docs/refactor-1.7/inventory/content.md) |
| 玩家文本、翻译键和中文注释 | `1.7/Languages`、`1.7/Mods/*/Languages` 与相关源码 | [本地化与注释](docs/localization-and-comments.md) |
| 验证工具与测试 | `tools/`、`Source/Tests/*` | [验证手册](docs/validation-runbook.md)、[验证工具清单](docs/refactor-1.7/inventory/validator-tooling.md) |

## 代码与内容约束

- 保持单运行时 DLL（[ADR-001](docs/architecture/ADR-001-single-dll.md)）。优先可读、可验证、低侵入，不为模组建立过度抽象。本次迁移和重构严格保持玩家文案与行为；旧计划中的缺陷修复不能作为改变行为的授权。
- **领域代码不承载旧档兼容**（[ADR-007](docs/architecture/ADR-007-save-schema-upgrade.md)）。领域代码中不写后备 Scribe 标签，不在 `LoadingVars` 中转换旧结构，不为存档保留退役的类型或 Def，也不写“为旧档保留”之类的注释。旧名字和旧结构只出现在 `Source/MugirlRace/SaveUpgrade`。
- 改动会进存档的名字或结构时（类型、Scribe 标签、defName、枚举成员、load ID、JobDriver/LordJob 图），同一次改动内要在升级表中加入行或步骤，并为步骤提供黄金夹具。发布验收必须以 `C.save-upgrade` 的 strict 模式通过；开发阶段也不得增加未处置的必需语料。
- **冻结的公开 API**（[ADR-008](docs/architecture/ADR-008-public-api.md)）永不改名，由 `C.public-api` 检查，范围包括：
  - packageId `HAR.MugirlRace`、Harmony id `MugirlMod.Mod`、Mod 短名 `MugirlMod`；
  - 依赖模组（WhiteFluff、Subspecies、KindSeal 等）使用的 defName 和抽象 `Name`；
  - XML 类名及其 XML 字段名；
  - `Mugirl.MountedPawnUtility.IsMugirl(Pawn)`：`public static`、`NoInlining`，必须是骑乘代码实际调用的判定点。

  权威清单是 `tools/RenameApply/specs/public-api.json`（151 项），`C.public-api` 直接读取这份清单。旧的 107 项副本已移除，避免两份清单漂移。
- **命名与结构约定**（[ADR-009](docs/architecture/ADR-009-naming-and-layout.md)）：
  - 命名空间为 `Mugirl.<Area>[.<Feature>]`。根命名空间只允许 `PublicApi/`；R/R2 的历史根命名空间债务已清理，不得扩大允许清单来容纳新问题。
  - 禁止与 Verse 静态类同名的命名空间段。
  - 一个文件只放一个顶层类型，类型按原版角色命名。
  - defName 为 `Mugirl_<PascalName>`。
  - Scribe 标签为小驼峰，引入时与字段名相同，之后冻结。
  - 补丁类为 `Harmony_<TargetType>_<TargetMember>`；`[MugirlPatch]` 的模块参数为封闭枚举 `PatchModule`。
- Def 引用走功能自己的 `DefOf`，运行期不散落 `DefDatabase.GetNamed` 字符串查找。确需动态查找时，写进验证器的 `dynamic-lookups.allow.txt` 并说明理由。
- 静态缓存写 `StaticCacheLifecycle:` 注释，说明生命周期和清理点。每局状态在新建、读档或初始化路径清理：清理函数在 `Features/GameLifecycle.cs` 中按固定顺序显式调用。GameComponent/MapComponent 的构造函数不得有副作用（`B.component-ctors`）；已审阅的既有行为例外必须有明确的保留理由。
- 每个 Harmony 补丁类带 `[MugirlPatch(module, risk, failureBehavior)]`，由 `HarmonyBootstrap` 注册。只有 `SaveUpgrade/Hooks` 可以补丁存档加载内部（`B.save-confinement`）。Compatibility 代码不补丁 Mugirl 自己的方法，而是使用功能提供的扩展点。
- 第三方反射和探测集中在 `Compatibility`（[ADR-003](docs/architecture/ADR-003-compatibility-scope.md)）。可选依赖缺失时返回安全结果，不能在 tick 或渲染中持续抛异常。
- 1.7 不使用 UnityStubs 和 `Waylith.UnityCompat` 提供的功能，Waylith 只在 `Mugirl.Platform` 中引用（[ADR-006](docs/architecture/ADR-006-unity-boundary.md)）。
- 日志走 `MugirlLog`，业务代码不直接调用 `Verse.Log`。启动阶段语言未就绪时，遵守[启动日志规则](docs/localization-and-comments.md)。
- 热路径中的每 tick 扫描、全图 Pawn 遍历和运行期 Def 查找必须有明确理由。
- 改 tick、概率、产量、价格、容量、伤害等数值时，记录原因和影响。可选 DLC 内容使用 `MayRequire` 或 `1.7/Mods/<名称>` 条件目录。
- XML patch 顶部保留 `PatchGovernance:`，写明 `Targets`、`Scope`、`Duplicate guard`、`Failure`。第三方目标必须有条件保护和缺失兜底。
- 玩家可见文本进入语言文件，中英 key 与占位符保持一致。注释用中文写当前的理由，不写历史（历史放 `docs/records`）。保留 `Pawn`、`Def`、`Harmony`、`Hediff`、`Job` 等技术名。
- 新增贴图和声音后，确认资源相对路径、大小写和打包结果。规则、外观和存档行为的修改要做相应的游戏内验证。

## 长期架构决策

| 决策 | 说明 |
| --- | --- |
| [ADR-001](docs/architecture/ADR-001-single-dll.md) | 保持单 DLL |
| [ADR-002](docs/architecture/ADR-002-harmony-registration.md) | Harmony 注册与元数据（1.7 中为 `[MugirlPatch]` 与 `HarmonyBootstrap`） |
| [ADR-003](docs/architecture/ADR-003-compatibility-scope.md) | Compatibility 层职责 |
| [ADR-004](docs/architecture/ADR-004-root-namespace-markers.md) | 根命名空间保留条件；1.7 重构中已被 ADR-008/ADR-009 取代（根命名空间只留 `PublicApi`） |
| [ADR-005](docs/architecture/ADR-005-version-layout.md) | 1.7 版本布局与 1.6 冻结 |
| [ADR-006](docs/architecture/ADR-006-unity-boundary.md) | 1.7 不使用 Unity 兼容层 |
| [ADR-007](docs/architecture/ADR-007-save-schema-upgrade.md) | 统一存档升级器；领域代码不承载旧档兼容，存档变更的义务 |
| [ADR-008](docs/architecture/ADR-008-public-api.md) | 冻结的公开 API |
| [ADR-009](docs/architecture/ADR-009-naming-and-layout.md) | 命名、命名空间、文件与 Scribe 约定 |

改变架构约束前，先更新相应 ADR。ADR 记录稳定的决策、背景与后果；日常流程只在本手册维护，一次性的执行结果放进日期记录。

## 1.7 重构文档

重构已完成，以下文档保留决策、设计与执行证据。当前状态以完成记录为准；`decisions.md`与`design.md`冲突时，以`decisions.md`为准。

| 文档 | 内容 |
| --- | --- |
| [重构交接记录](docs/records/refactor-1.7-handoff-2026-10-15.md) | 2026-10-15 收尾时的状态、证据位置、未完成事项与接手步骤 |
| [重构恢复记录](docs/records/refactor-1.7-resume-2026-10-15.md) | 恢复后的基线、行为不变约束、前置修复与当前验证证据 |
| [Core、Content 与存档身份整合](docs/records/refactor-1.7-core-content-2026-10-16.md) | 生命周期、设置、类型明确的引用升级与验证器加强 |
| [Corporate 模块重构](docs/records/refactor-1.7-corporate-2026-10-15.md) | 状态聚合、精确金额与历史文字保留、支援 Lord 的持久续接及离线验证 |
| [Milk 模块重构](docs/records/refactor-1.7-milk-2026-10-15.md) | 产奶与采集结构、计时余量、灌篮状态及一次性升级的离线验证 |
| [Incidents 模块重构](docs/records/refactor-1.7-incidents-2026-10-16.md) | 剧情、迁徙与惩罚者状态归组，随机数顺序与进行中事件保留 |
| [Restraints 模块重构](docs/records/refactor-1.7-restraints-2026-10-16.md) | 束具状态与公开 Def 适配、旧工作续接、牵引登记生命周期 |
| [Combat 模块重构](docs/records/refactor-1.7-combat-2026-10-15.md) | 武器轮盘职责拆分、兼容扩展点与进行中的骑枪工作升级 |
| [Mounting 模块重构](docs/records/refactor-1.7-mounting-2026-10-16.md) | 骑乘状态、持有物品身份、累计计时与武器借用恢复 |
| [重构计划](docs/refactor-1.7/plan.md) | 波次（0、A、R、R2、S、V、D）、闸门与执行顺序 |
| [约束性决策](docs/refactor-1.7/decisions.md) | orchestrator 决策、owner 答复（§5）、Wave 0 基线事实（§6） |
| [设计](docs/refactor-1.7/design.md) | 架构、约定、升级器设计与波次模型 |
| [改名工具](docs/refactor-1.7/rename-tool.md) | `tools/RenameApply` 的构建、运行、事务写入与规格格式 |
| [模块计划](docs/refactor-1.7/module-plans/) | Wave S 七个模块（core、incidents、corporate、milk、restraints、combat、content）的计划 |
| [盘点](docs/refactor-1.7/inventory/) | 已核实的代码、内容、真实存档、验证工具盘点与引擎事实（1.7.4977） |
| [评审](docs/refactor-1.7/reviews/) | 升级器与验证的批判性评审、计划之间的一致性检查 |
| [Wave A 报告](docs/refactor-1.7/wave-a/) | A1–A5 的实施报告、对抗性评审与修复报告 |

改名规格、公开 API 清单、owner 迁移清单、文件归属和 MooGirl 时代标记都在 `tools/RenameApply/specs/`。仓库外的 `..\1.7迁移文档`（与本模组目录同级）保存 1.6→1.7 迁移指南与证据编号 E*，部分工具说明会引用它。

## 文件放置与清理

所有路径相对于仓库根目录。

| 路径 | 用途 |
| --- | --- |
| `About/`、`LoadFolders.xml` | 模组元数据与条件加载规则。`v1.7` 列表加载 `/`、`1.7` 和 `1.7/Mods/<名称>` |
| `1.7/` | 1.7 的版本内容：`Defs`、`Patches`、`Languages`、`Shaders`、`Textures`（只放 1.7 专用覆盖贴图），以及 `Mods/<名称>`（Biotech、Odyssey、FacialAnimation、VCookE、SearchAndDestroy 条件内容） |
| `1.7/Assemblies/` | 已部署的 DLL 与 PDB。只能通过 `-p:DeployToMod=true` 显式写入，清理时保留 |
| `Source/` | 1.7 工程与测试（见“工程与构建”）。`bin`、`obj`、`TestResults` 是构建输出 |
| `tools/` | 1.7 的验证、夹具、发布和文档工具；`tools/RenameApply/` 是 Wave R/R2 改名工具及其 `specs/` |
| 根 `Textures/`、`Sounds/` | 1.6 与 1.7 共用的游戏资源 |
| `1.6/`、`Bio_1.6/`、`Odyssey_1.6/`、`Versions/1.6/` | 冻结的 1.6 内容，永不修改。`1.6/Source` 是冻结 DLL 对应的源码快照 |
| `SourceAssets/` | 需版本控制的源素材，不参与打包 |
| `docs/*.md` | 内容、兼容、本地化、验证、发布等专项指南及 RimSort 安全记录 |
| `docs/features/`、`docs/architecture/`、`docs/records/` | 功能专题、架构决策、带日期的实施记录 |
| `docs/refactor-1.7/` | 1.7 重构的计划、决策、设计、盘点、评审与 Wave 报告 |
| `docs/tools/` | 1.6 时代的验证驱动与脚本，已冻结，只作追溯 |
| `docs/outline/`、`docs/lance-motion-blur/` | 描边与骑枪运动模糊资源的源码 |
| `TMP/` | 本轮普通输出（如 `TMP/InGameValidation`、`TMP/SmokeTest`），核实内容后清理 |

本地长期资料统一放 `DevData`，由 `.gitignore` 排除，也不进入发布包。**被忽略不代表可以删除。**

| 路径 | 保留内容 |
| --- | --- |
| `DevData/Assets/` | 用户提供的原图、内容包和参考素材 |
| `DevData/Backups/` | 贴图、DLL 和清理前构建文件；`BuildOutputs` 含历史 DLL、PDB 与生成源码 |
| `DevData/Backups/PreRefactor-1.7-20261015/` | 重构前整树归档 `tree.zip` 与 `tree-manifest.sha256`，以及重构前的 DLL/PDB（SHA-256 `220E5D1A…`） |
| `DevData/Backups/PreR-1.7-20261015/` | Wave A 结束时的集成树归档与它的 Release 构建（DLL SHA-256 `5B0103C6…`）。这是 Wave R 的 parity 基线 A，从未部署 |
| `DevData/Evidence/` | 已完成测试的日志、截图、配置、测试存档、清单和审计 |
| `DevData/Evidence/Baseline-PreRefactor-20261015/` | Wave 0 基线报告：Validate、SelfTest、Smoke、Test-InGame |
| `DevData/Evidence/Integration-WaveA-20261015/` | 集成阶段的构建、Validate、SelfTest、基线更新和 patch-targets 差异 |
| `DevData/Evidence/SaveFixtures/` | 旧档夹具：`P-<构建>/`（`manifest.json` 与 `.rws.gz`），以及手写的 `X-alias/`、`X-settings/`。夹具是否纳入版本控制尚未决定 |
| `DevData/Experiments/` | 历史试验源码、编译结果、Unity 工程和着色器分析；复用旧脚本前先检查路径 |
| `DevData/Releases/` | 已生成的发布 ZIP，保留原文件名 |

清理时按以下顺序执行：

1. 核实目标的绝对路径及各级 Windows `ReparsePoint` 属性。发现 Junction 或目录符号链接时，停止对该目录的递归处理，不能跟随目标删除。
2. 原图、独有源码、回退 DLL/PDB、存档和需要追溯的证据先归档，并逐文件核对 SHA256。不能只凭 `TMP`、`Backup` 或旧日期判断可删。
3. 发布展开副本只在与对应 ZIP 的数量、相对路径、大小、SHA256 都一致后才删除，ZIP 继续保留。构建缓存和隔离 Unity `Library` 先保留独有文件再清理。
4. 核对正式文件与游戏资源路径未变，更新文档中的证据位置。2026-09-21 的搬移清单位于 `DevData/Evidence/MaintenanceCleanup-20260921/moves.json`，普通贴图合并清单为同目录的 `texture-layout.json`。

历史验证的大型 JSON、测试存档等原始数据可以压缩为同目录的 `.gz` 文件；删除未压缩副本前必须完整解压并核对 SHA256。原路径、原内容哈希和压缩文件哈希登记在 `DevData/Evidence/compression-manifest.jsonl`，小型报告、源码、发布 ZIP 及 `SaveFixtures` 保持原路径。阅读旧记录或重放需要这些原始文件时，用 [Restore-CompressedEvidence.ps1](tools/Restore-CompressedEvidence.ps1) 恢复一个文件，例如：

```powershell
pwsh tools/Restore-CompressedEvidence.ps1 -Path 'DevData/Evidence/CorporateSupply-20261019/game-fixed/20261019-232829/artifacts/think-trees.json'
```

恢复会核对压缩文件与原内容的哈希，已有同路径文件不一致时拒绝覆盖，`.gz` 原件继续保留。具体清理结果见[工作区整理记录](docs/records/workspace-cleanup-2026-09-21.md#2026-10-20-历史验证资料压缩)。

测试脚本只删除自己建立、带标记文件的临时目录，例如 `Mods/__MugirlRace_GameValidation_*` 和 `Mods/__MugirlLegacy_*`。中断后如有残留，按上面的规则核实后再清理。

**测试方式（2026-09-29 起）：** 旧的独立游戏副本已清理，RimSort 外置测试目录规避规则已撤销。默认使用构建、静态检查和现有的 Steam 游戏安装；不再为每轮验证复制游戏启动程序、运行库或创建游戏资源目录联接。复制游戏的旧启动脚本已停用，历史事故与迁移清单仅用于追溯。

## 历史记录索引

以下记录只证明所注明版本与配置下的结果，不能替代当前复测。日志正文中的历史绝对路径保留原样，可按搬移清单定位归档。2026-10-14 之前的记录描述的是 1.6 构建，其中的 `1.6/Source`、`docs/tools` 路径和命令只作追溯。

| 日期 | 记录 |
| --- | --- |
| 2026-10-27 | [1027 发布记录](docs/records/release-1027-2026-10-27.md)、[墙壁栓点多余开关修复](docs/records/wall-hitch-switch-2026-10-27.md) |
| 2026-10-26 | [投掷建筑连接修复](docs/records/throw-building-connections-2026-10-26.md) |
| 2026-10-25 | [雪牛娘觅食植物扫描性能调查](docs/records/performance-food-search-2026-10-25.md) |
| 2026-10-22 | [代码质量审查与优化](docs/records/code-quality-optimization-2026-10-22.md) |
| 2026-10-16 | [1016 发布与 1.7.4980 兼容](docs/records/release-1016-2026-10-16.md) |
| 2026-10-15 | [1.7 重构阶段性交接（Wave 0、Wave A、集成）](docs/records/refactor-1.7-handoff-2026-10-15.md) |
| 2026-10-11 | [近战供货、加工报酬、高价限购与作业中文修复](docs/records/bug-report-fixes-2026-10-11.md) |
| 2026-10-04 | [联络员独立入口、未招募产奶与民间事件整合](docs/records/event-normalization-2026-10-04.md)、[狩猎中轮盘换枪递归修复](docs/features/weapon-wheel-design.md#2026-10-04-狩猎换枪修复)、[1004 发布包记录](docs/records/release-1004-2026-10-04.md) |
| 2026-10-01 | [巨企价格算法、供货保护与加急备货](docs/records/corporate-pricing-2026-10-01.md)、[1001 发布包记录](docs/records/release-1001-2026-10-01.md) |
| 2026-09-30 | [固定材料装备制作工作量修复](docs/records/equipment-work-amount-2026-09-30.md)、[0930 / 0930.1 发布包记录](docs/records/release-0930-2026-09-30.md)、[巨企订单中文输入续修与白银落点修正](docs/features/giant-corporation-v1-validation.md) |
| 2026-09-29 | [巨企订单系统三项修复](docs/records/corporate-trade-fixes-2026-09-29.md)、[0929 发布包记录](docs/records/release-0929-2026-09-29.md) |
| 2026-09-27 | [0927 发布包与服装储存分类修复](docs/records/release-0927-2026-09-27.md) |
| 2026-09-26 | [机械体暂缓、投掷暴力限制与吃草调查](docs/records/mech-throw-graze-2026-09-26.md)、[投掷伤害削减与冷却调整](docs/records/throw-damage-cooldown-2026-09-26.md)、[灌篮文案与红色警示句](docs/records/dunk-description-red-line-2026-09-26.md)、[头饰护甲补齐](docs/records/headwear-armor-2026-09-26.md)、[固色服装名单扩展与隐藏背负武器设置](docs/records/fixed-color-apparel-2026-09-26.md)、[9.26 贴图实装：比基尼/公主套装与 Hairs10](docs/records/apparel-hairs-2026-09-26.md)、[装备平衡、穿戴与乳品发电机修正](docs/records/equipment-dairy-generator-fixes-2026-09-26.md) |
| 2026-09-25 | [投掷期间禁火](docs/records/throw-firing-lock-2026-09-25.md) |
| 2026-09-24 | [0.6.7 版本记录](docs/records/release-0.6.7-2026-09-24.md)、[战斗服饰收购与售价调整](docs/records/combat-apparel-trade-2026-09-24.md)、[性能优化实施](docs/records/performance-optimization-2026-09-24.md)、[巨企见面礼科技资料](docs/records/corporate-intro-tech-gift-2026-09-24.md)、[自驱装弹系统南向层级修复](docs/records/autoloading-south-layer-2026-09-24.md)、[背负武器布局数据化与调试窗口](docs/records/back-weapon-layout-debug-2026-09-24.md)、[多选挤奶Gizmo合并与名字条](docs/records/milk-gizmo-merge-2026-09-24.md) |
| 2026-09-23 | [0.6.6 版本记录](docs/records/release-0.6.6-2026-09-23.md) |
| 2026-09-21 | [研究树重做实施](docs/records/research-tree-2026-09-21.md)、[工作区与文档整理](docs/records/workspace-cleanup-2026-09-21.md)、[怀孕心情](docs/records/pregnancy-mood-2026-09-21.md)、[启动日志](docs/records/bootstrap-startup-2026-09-21.md) |
| 2026-09-20 | [服装、头型发型、武器与神经战斗套装](docs/records/update-2026-09-20.md) |
| 2026-09-18 | [RimSort 目录环路事故](docs/rimsort-directory-cycles-2026-09-18.md) |
| 2026-09-16 | [巨企报价](docs/records/corporate-pricing-2026-09-16.md)、[巨企界面](docs/records/corporate-ui-2026-09-16.md)、[界面异常修复](docs/records/corporate-ui-errors-2026-09-16.md) |
| 2026-09-07 | [性能优化实施](docs/records/performance-optimization-2026-09-07.md)、[优化前审查](docs/records/performance-review-2026-09-07.md) |

## 文档维护约定

- 全仓库的开发入口只保留根 `README.md` 与 `AGENTS.md`。不要在功能、工具、资源或资料子目录中新建同名入口。
- 通用开发流程、目录说明和导航更新本文件。AI 必守规则放 `AGENTS.md`，不要把专题设计或执行日志复制进去。
- 按主题更新现有指南；确有独立专题时，才新建带明确主题名的文档。功能约束放 `docs/features`，架构决策放 `docs/architecture`，日期记录放 `docs/records`，重构工作文档放 `docs/refactor-1.7`。
- 新文档直接加入本文件的对应表格，不再新增子目录索引。移动或合并文档时，同步更新所有引用及 `tools/Test-Docs.ps1` 的必需文档清单，并运行该脚本。
- 记录写明版本、验证范围、真实结果、限制和证据路径。原始大日志、截图和机器清单放 `DevData/Evidence`。不要把日期记录里的旧失败改写成已通过。
