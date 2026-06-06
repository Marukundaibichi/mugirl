# 阶段 10：全量审查与发布整理记录

## 范围

- 清理发布包构建产物：删除 `1.6/Source/WRace/bin`、`1.6/Source/WRace/obj` 和 `1.6/Assemblies/MooGirlRace.pdb`，保留 Release 构建生成的 `1.6/Assemblies/MooGirlRace.dll`。
- 删除顶层 `TMP` 临时素材和 `Textures/Things/Apparel/MooGirl_Bar/备份` 备份贴图目录。
- 新增 `.gitignore`，阻止 `bin/obj`、PDB、`TMP` 和备份目录再次进入发布包。
- Release 构建输出改为 `1.6/Assemblies`；Debug 构建输出改到源码目录下的 `bin/Debug`，避免 Debug 符号污染发布程序集。
- 删除未编译旧源码和重复类型：旧 DefOf、旧束具空壳、旧脑控临时文件、未编译的 apparel unlock patch、未使用的 feature registry。
- 将生产源码路径中的 `Harmoney_*` 改为 `Harmony_*`，将 `Defof` 目录改为 `DefOf`，将未被 XML 引用的扩展类文件改为 `SlaveApparelExtensions.cs`。
- 清理 C# 英文注释、过期注释代码和硬编码格式文本；C# 玩家可见格式和诊断日志统一进入 Keyed。
- 将 `Bio_1.6`、`Versions/1.6/Integrations/VCookE`、`1.6/Patches` 和特殊 Def 字段中的中文源正文改为英文原文，并补齐对应中英文 DefInjected。
- 清理 `LoadFolders.xml` 的空加载项和重复 SearchAndDestroy 集成项。
- 更新 `About/About.xml` 的展示名和说明文本，`packageId` 保持不变。

## 行为保护

- 未改任何玩法数值、成本、时间、权重、伤害、范围、产量、hediff severity、事件点数或任务延迟。
- 未改任何 `defName`，包括历史拼写已有问题的 `SlaveApperal` 系列 Def。
- 未改 XML 中被运行时解析的 `thingClass`、`compClass`、`driverClass`、存档字段名和旧类名边界。
- R18 内容继续常驻，没有恢复旧 R18 过滤按钮或开关。
- 服装候选选择仅去掉 LINQ 写法，仍保留“收集候选后 `RandomElement()`”的旧随机语义。

## 设计修正

- 发布配置从“Debug 输出即发布程序集”改为“Release 输出发布程序集”，避免调试符号和源码中间产物进入发布包。
- `bin/obj` 和临时素材从仓库产物边界中移除，发布包只保留 RimWorld 实际加载所需内容。
- 可选 Biotech 与 VCookE 文本放在各自 LoadFolder 的 `Languages` 下，只在对应内容加载时提供注入。
- C# 菜单括号、目标拼接、状态标签、日志说明全部走 Keyed，避免硬编码英文/中文格式。
- 热路径和频繁调用路径中的 LINQ 残留继续压缩；唯一保留的 `ToList()` 位于 Harmony transpiler 初始化阶段，不属于游戏 tick 热路径。

## 已知边界

- `SlaveApperal` 拼写仍保留在 defName、XML class、存档字段和已确认的运行时类型边界中；如需彻底重命名，需要单独确认并同步迁移 XML、C# 和存档字段。
- 文档基线仍保留历史文件名与旧路径记录，它们是审计历史，不属于生产命名残留。
- 本阶段未在自动化环境中启动 RimWorld 做长时间 DevMode 新档运行；该项仍是发布前人工验收门禁。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU` 通过，输出 `1.6/Assemblies/MooGirlRace.dll`。
- `1.6`、`Bio_1.6`、`Versions`、`About` 和 `LoadFolders.xml` 共 241 个 XML 文件全部通过解析。
- 源 XML 非注释中文正文扫描结果为 0。
- `Languages/ChineseSimplified`、`Bio_1.6/Languages/ChineseSimplified` 和 VCookE 简中语言目录无 XML 注释残留。
- `1.6`、`Bio_1.6` 和 `Versions` 无 `TODO`、`FIXME`、`HACK` 残留。
- DefInjected 覆盖检查：574 个源文本键，英文缺失 0，简中缺失 0。
- C# 中文字符串字面量扫描结果为 0。
- C# 英文注释候选扫描结果为 0。
- 生产路径扫描无 `TMP`、`bin`、`obj`、PDB、`备份`、`不能用`、`临时`、`AiGenerated`、`NewAdded`、`Bongdage`、`Stcoking`、`NewBron`、`LegacyDefs`、`Harmoney`、`Defof`、`SlaveApperal_extensions` 残留。
