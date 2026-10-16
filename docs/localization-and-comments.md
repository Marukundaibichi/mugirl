# 翻译与注释规范

本文档用于维护玩家可见文本和代码注释。目标是让内容容易本地化，注释能帮助后续维护，而不是制造噪音。

## 玩家可见文本

以下文本必须进语言文件：

- 按钮、Gizmo、FloatMenu、Message、Letter、提示文本。
- Def 的 label、description、reportString、jobString。
- 调试时会出现在玩家日志里的诊断文本。

C# 中不要硬编码玩家可见的中文或英文。确实需要默认值时，默认值也应清楚地对应翻译键的语义。

存档中不保存翻译键或翻译后的文本：保存枚举或 ID，显示时再翻译。

Mod 构造阶段早于 `LanguageDatabase.InitAllMetadata()`，启动诊断必须使用 `MugirlLog.StartupWarningOnce`。语言未初始化时，它输出与翻译键语义一致的英文 fallback；语言就绪后，才求值翻译回调。不要提前构造 `Translate(...)` 参数，也不要用 Verse `Formatted` 格式化 fallback：两者都会访问尚未就绪的语言对象。补丁失败应保留完整异常及内部堆栈。

存档升级器的提示走 `1.7/Languages/*/Keyed/SaveUpgrade.xml` 中的 `Mugirl.SaveUpgrade.*` 键。它们在主线程显示；`SaveUpgradeNotices` 中有英文 fallback。

## 翻译文件

- 1.7 的语言文件在 `1.7/Languages/<语言>` 和 `1.7/Mods/<名称>/Languages/<语言>`；1.6 的语言文件已冻结。
- 不同语言保持 key 对齐。
- 新增 key 后运行 `pwsh tools/Validate.ps1 -WorkRoot <独立目录>`。`C.keyed` 比较中英 Keyed 键集与占位符，`C.translation-keys` 检查代码中使用的键，`C.language-files` 检查遮蔽、重复和孤立的 DefInjected 键，`C.definjected-paths` 检查 DefInjected 键路径能否解析到 Def 字段。
- 占位符如 `{0}`、`{PAWN_nameDef}`、`TargetA` 必须保留语义和数量。
- 自定义 Job 的 `reportString` 除中英对齐外，还由 `C.job-reports` 检查：每个 JobDef 的 `reportString` 都要有简体中文。新增带 `reportString` 的 Job 时，在对应加载目录的 `ChineseSimplified/DefInjected/JobDef` 补齐同名键。
- 改 defName 时同步改 DefInjected 键和文件；Wave R 的 `tools/RenameApply` 会自动完成这一步。
- 翻译文件本身不按“代码注释中文化”规则处理，避免误改占位符或原文对照。

## 注释风格

- 注释统一写中文。
- 注释量保持中等：说明这块代码做什么、为什么这样做、哪些条件不能随便改。
- 注释写当前的理由，不写历史，例如“9.20 起……”“1.6 时……”“为旧档保留……”。历史放在 `docs/records` 中。
- 不写逐行复述，例如“定义字段”“调用方法”“返回结果”。
- 技术名可以保留英文，例如 `Pawn`、`Def`、`Hediff`、`Harmony`、`Gizmo`、`Tick`、`Job`、`ThingDef`、`Scribe`。
- 机器可读标签保留英文前缀，例如 `StaticCacheLifecycle:`、`PatchGovernance:`、`Targets:`、`Scope:`。

## 必写注释的场景

- 静态缓存的生命周期和清理点。
- Harmony patch 的边界、是否跳过原方法、失败行为。补丁类的 `[MugirlPatch]` 元数据写明模块、风险和失效表现。
- 反射访问和兼容降级。
- Tick 热路径和性能取舍。
- Scribe 字段、保存读档状态和默认值。
- 升级器的步骤和表格行：说明消费哪些旧名字、为什么这样处置（只在 `SaveUpgrade/` 中）。
- XML patch 的目标、范围、重复保护和失败行为。
- 为保持现有行为而看起来“不自然”的实现。

## 可删注释的场景

- 单纯重复类型名、字段名或方法名。
- 过期的重构过程记录。
- 和当前数值不一致的旧说明。
- 领域代码中关于旧档兼容的说明。旧名字只在 `SaveUpgrade/` 中处理，见 [ADR-007](architecture/ADR-007-save-schema-upgrade.md)。
- “临时”“以后再改”“AI 生成”等没有维护价值的备注。
