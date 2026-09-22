# Mugirl Race · 雪牛娘开发手册

**下次开发从本文件开始。** 这里统一维护开发流程、目录用途、功能导航和历史索引；[AGENTS.md](AGENTS.md) 只保存 AI 必须遵守的规则。子目录不再设置 README 或 AGENTS 入口。专题正文均在 `docs`，按下面的任务表选择阅读。

## 开始一次开发

1. 检查 `git status --short` 和当前差异，保留已有源码、DLL、素材、存档及未提交改动。
2. 在下方“按任务找代码和文档”中定位功能；改数值前先确认内容规则，改兼容前先确认 DLC 或第三方模组边界。
3. 运行静态基线，记录已有失败。纯文档或文件搬移使用 `-SkipBuild`；修改 C# 后执行完整构建验证。
4. 修改对应功能，同时更新必要的 Def、翻译、补丁元数据和专题文档。不要顺手改变无关玩家行为或数值。
5. 运行相关检查；涉及交互、渲染、事件、读档或兼容时，按[验证手册](docs/validation-runbook.md)检查 fresh `Player.log`。
6. 记录实际结果及限制。发布前按[发布清单](docs/release-checklist.md)检查；需要追溯的日志与备份按本页归档约定保存。

工程为 [MugirlRace.sln](1.6/Source/MugirlRace.sln) / [MugirlRace.csproj](1.6/Source/MugirlRace.csproj)，目标 .NET Framework 4.8。依赖本机 RimWorld、Harmony、HAR 的程序集；Release 输出为 `1.6/Assemblies/MugirlRace.dll`。不要在测试中覆盖正在使用的正式 DLL。

所有命令从仓库根目录执行：

```powershell
# 仅静态复查，使用已有正式 DLL
.\docs\tools\Invoke-Phase6StaticValidation.ps1 -SkipBuild

# 修改 C# 后构建并验证
.\docs\tools\Invoke-Phase6StaticValidation.ps1

# 扫描当前游戏日志
.\docs\tools\Invoke-PlayerLogScan.ps1

# 使用已有 DLL 预检发布包
.\docs\tools\New-WorkshopPackage.ps1 -SkipBuild
```

专项测试、配置模板、资源处理和日志记录模板统一见[验证手册](docs/validation-runbook.md)，不要另建工具入口页。

**已知基线（2026-09-23）：** `Mugirl_Shapes.xml` 两处行尾空格已清理。静态检查主体通过，但资源检查仍因缺少 42 张 FA 烘焙脸红贴图失败；不能据历史“通过”记录认定当前资源无误，也不能为了消除失败擅自恢复用户删改的素材。下次修改 FA 时应核对现行方案和检查器要求。证据见[工作区整理记录](docs/records/workspace-cleanup-2026-09-21.md)及[0.6.6 版本记录](docs/records/release-0.6.6-2026-09-23.md)。

## 按任务找代码和文档

下表代码目录均相对于 `1.6/Source`。涉及 Def 数据时同时检查 `1.6/Defs` 及条件加载目录；C# 负责规则和流程，XML/Def 承载数值与内容。

| 任务 | 代码入口 | 阅读材料 |
| --- | --- | --- |
| 启动、日志、缓存、补丁注册 | `Core`、`DefOf` | [启动修复记录](docs/records/bootstrap-startup-2026-09-21.md)、下方代码约束 |
| 新增服装、装备、Hediff、Job 或事件 | 对应 `Features` 模块 | [内容更新指南](docs/content-update-guide.md)、[7.15.3 服装设计](docs/features/apparel-7.15.3-design.md) |
| 外观、头发、脸部附件与造型台 | `Features/Appearance`、`Compatibility/AlienRace`、`Compatibility/FacialAnimation` | [外观刷新](docs/features/styling-station-refresh.md)、[FA 专题](docs/features/facial-animation-guide.md)、[描边](docs/features/outline-guide.md) |
| 巨企、委托、贷款、投资与交易 | `Features/Corporate`、`Features/Incidents` | [设计](docs/features/giant-corporation-design.md)、[实现范围与验证](docs/features/giant-corporation-v1-validation.md)、[服务规则](docs/features/corporate-services.md) |
| 科技树、研究项目与内容解锁门槛 | `1.6/Defs/ResearchProjectDefs`（纯 XML，无专属 C# 模块） | [研究树设计](docs/features/research-tree-design.md) |
| 武器轮盘、自驱装弹、骑枪与神经战斗套装 | `Features/WeaponWheel`、`Features/Weapons`、`Features/Lances`、`Features/AdvancedArmor` | [轮盘设计](docs/features/weapon-wheel-design.md)、[骑枪破墙与运动模糊](docs/features/lance-charge.md)、[9.20 更新记录](docs/records/update-2026-09-20.md) |
| 产奶、束具、牵引、骑乘 | `Features/Milk`、`Features/Restraints`、`Features/Roping`、`Features/Mounting` | [专项验证](docs/validation-runbook.md)、[性能实施记录](docs/records/performance-optimization-2026-09-07.md) |
| 基因、新生儿、机械体与杂项行为 | `Features/Genes`、`Features/Newborn`、`Features/Mechanoids`、`Features/Misc` | [内容更新指南](docs/content-update-guide.md)、[怀孕心情记录](docs/records/pregnancy-mood-2026-09-21.md) |
| 第三方模组与存档兼容 | `Compatibility` | [兼容维护](docs/compatibility-guide.md) |
| 玩家文本、翻译键和中文注释 | 各加载目录的 `Languages` 与相关源码 | [本地化与注释](docs/localization-and-comments.md) |

## 代码与内容约束

- 保持单运行时 DLL；优先可读、可验证、低侵入，不为模组建立过度抽象。
- Def 引用使用 `DefOf`、`MugirlRequiredDefs` 或 `MugirlOptionalDefs`；运行期不要散落 `DefDatabase.GetNamed` 字符串查找。
- 静态缓存写 `StaticCacheLifecycle:` 注释，说明生命周期和清理点。每局状态在新建、读档或初始化路径清理，避免跨档残留。
- Harmony patch 同步更新 `MugirlPatchInfo` 元数据；说明目标、是否跳过原方法及失败时的行为。手动反射 patch 由 `MugirlPatchRegistry` 管理。
- 第三方反射和探测集中于 `Compatibility`。可选依赖缺失时返回安全结果，不能在 tick 或渲染中持续抛异常。
- 日志走 `MugirlLog`，业务代码不直接调用 `Verse.Log`；启动阶段语言未就绪时遵守[启动日志规则](docs/localization-and-comments.md)。
- 热路径中的每 tick 扫描、全图 Pawn 遍历和运行期 Def 查找必须有明确理由。
- 新存档字段提供默认值和 Scribe 兼容说明。XML 类名、Scribe 字段、根命名空间及补丁 XPath 不能随意改名。
- 改 tick、概率、产量、价格、容量、伤害等数值时记录原因和影响。可选 DLC 内容使用 `MayRequire` 或条件加载目录。
- XML patch 顶部保留 `PatchGovernance:`，写明 `Targets`、`Scope`、`Duplicate guard`、`Failure`。第三方目标必须有条件保护和缺失兜底。
- 玩家可见文本进入语言文件，保持中英 key 与占位符一致。注释写中文，保留 `Pawn`、`Def`、`Harmony`、`Hediff`、`Job` 等技术名。
- 新增贴图和声音后确认资源相对路径、大小写和打包结果。规则、外观、存档行为的修改须做相应游戏内验证。

长期架构决策：

| 决策 | 说明 |
| --- | --- |
| [ADR-001](docs/architecture/ADR-001-single-dll.md) | 保持单 DLL |
| [ADR-002](docs/architecture/ADR-002-harmony-registration.md) | Harmony 注册与元数据 |
| [ADR-003](docs/architecture/ADR-003-compatibility-scope.md) | Compatibility 层职责 |
| [ADR-004](docs/architecture/ADR-004-root-namespace-markers.md) | 根命名空间保留条件 |

改变架构约束前更新相应 ADR。ADR 记录稳定决策、背景与后果；日常流程只在本手册维护，一次性执行结果放入日期记录。

## 文件放置与清理

所有路径相对于仓库根目录。

| 路径 | 用途 |
| --- | --- |
| `About/`、`LoadFolders.xml` | 模组元数据与条件加载规则 |
| `1.6/Assemblies/` | 正式 DLL，清理时保留 |
| `1.6/Source/` | C# 工程与功能代码；`bin/obj` 为构建输出 |
| `1.6/Defs/`、`1.6/Patches/`、`1.6/Languages/`、`1.6/Resources/` | 1.6 游戏数据、补丁、翻译与 AssetBundle |
| 根 `Textures/`、`Sounds/` | 普通游戏资源统一位置，不另建 `1.6/Textures` |
| `1.6/FacialAnimation/` | 仅启用 `Nals.FacialAnimation` 时加载；专属贴图保留在其 `Textures/FA` 中 |
| `Bio_1.6/`、`Odyssey_1.6/`、`Versions/` | DLC 与其他模组的条件内容 |
| `SourceAssets/` | 需版本控制的源素材；`FA.rar` 从运行资源目录迁入，不参与打包 |
| `docs/*.md` | 内容、兼容、本地化、验证、发布等专项指南及 RimSort 安全记录 |
| `docs/features/`、`docs/architecture/`、`docs/records/` | 功能专题、架构决策、带日期的实施记录 |
| `docs/tools/`、`docs/outline/` | 维护脚本、验证驱动与描边资源源码，不存入口 README |
| `TMP/` | 本轮普通日志、配置与打包输出，核实内容后清理 |

本地长期资料统一放 `DevData`，由 `.gitignore` 排除，也不进入 Workshop 包。**被忽略不代表可以删除。**

| 路径 | 保留内容 |
| --- | --- |
| `DevData/Assets/` | 用户提供的原图、内容包和参考素材 |
| `DevData/Backups/` | 贴图、DLL 和清理前构建文件；`BuildOutputs` 含历史 DLL、PDB 与生成源码 |
| `DevData/Evidence/` | 已完成测试的日志、截图、配置、测试存档、清单和审计 |
| `DevData/Experiments/` | 历史试验源码、编译结果、Unity 工程和着色器分析；复用旧脚本前先检查路径 |
| `DevData/Releases/` | 已生成的发布 ZIP，保留原文件名 |

清理时按以下顺序执行：

1. 核实目标的绝对路径及各级 Windows `ReparsePoint` 属性。发现 Junction 或目录符号链接时停止该目录的递归处理，不能跟随目标删除。
2. 原图、独有源码、回退 DLL/PDB、存档和需要追溯的证据先归档，逐文件核对 SHA256；不能只凭 `TMP`、`Backup` 或旧日期判断可删。
3. 发布展开副本只有在对应 ZIP 的数量、相对路径、大小、SHA256 一致后才删除，ZIP 继续保留。构建缓存和隔离 Unity `Library` 先保留独有文件再清理。
4. 核对正式文件与游戏资源路径未变，更新文档中的证据位置。2026-09-21 搬移清单位于 `DevData/Evidence/MaintenanceCleanup-20260921/moves.json`，普通贴图合并清单为同目录 `texture-layout.json`。

**带联接的测试游戏只能放在 `%LOCALAPPDATA%\RimWorldModTests\`，且该目录必须位于所有 RimSort 的 Mods、Workshop、Data 扫描范围之外。不得用联接挂回任何模组。** 修改测试目录、联接或验证脚本后运行环路审计，要求 `affected_candidates=0`、`scan_errors=0`。详见[RimSort 事故与复查命令](docs/rimsort-directory-cycles-2026-09-18.md)及[应急迁移清单](docs/rimsort-emergency-migration-2026-09-18.json)。

## 历史记录索引

以下记录只证明所注明版本与配置的结果，不能替代当前复测。日志正文中的历史绝对路径保留原样，可按搬移清单定位归档。

| 日期 | 记录 |
| --- | --- |
| 2026-09-23 | [0.6.6 版本记录](docs/records/release-0.6.6-2026-09-23.md) |
| 2026-09-21 | [研究树重做实施](docs/records/research-tree-2026-09-21.md)、[工作区与文档整理](docs/records/workspace-cleanup-2026-09-21.md)、[怀孕心情](docs/records/pregnancy-mood-2026-09-21.md)、[启动日志](docs/records/bootstrap-startup-2026-09-21.md) |
| 2026-09-20 | [服装、头型发型、武器与神经战斗套装](docs/records/update-2026-09-20.md) |
| 2026-09-18 | [RimSort 目录环路事故](docs/rimsort-directory-cycles-2026-09-18.md) |
| 2026-09-16 | [巨企报价](docs/records/corporate-pricing-2026-09-16.md)、[巨企界面](docs/records/corporate-ui-2026-09-16.md)、[界面异常修复](docs/records/corporate-ui-errors-2026-09-16.md) |
| 2026-09-07 | [性能优化实施](docs/records/performance-optimization-2026-09-07.md)、[优化前审查](docs/records/performance-review-2026-09-07.md) |

## 文档维护约定

- 全仓库开发入口只保留根 `README.md` 与 `AGENTS.md`；不要在功能、工具、资源或资料子目录新建同名入口。
- 通用开发流程、目录说明和导航更新本文件。AI 必守规则放 `AGENTS.md`，不要把专题设计或执行日志复制进去。
- 按主题更新现有指南；确有独立专题才新建含明确主题名的文档。功能约束放 `docs/features`，架构决策放 `docs/architecture`，日期记录放 `docs/records`。
- 新文档直接加入本文件对应表格，不再新增子目录索引。移动或合并文档时同步更新所有引用及静态验证的必需文档清单。
- 记录写明版本、验证范围、真实结果、限制和证据路径。原始大日志、截图和机器清单放 `DevData/Evidence`，不要把日期记录里的旧失败改写成已通过。
