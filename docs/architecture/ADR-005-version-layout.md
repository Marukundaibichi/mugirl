# ADR-005: 1.7 版本布局与 1.6 冻结

状态：Accepted（2026-10-15 修订）。规则 2，以及 Context 中由“存档双向读取”推出的“名字必须两版一致”，已被 [ADR-007](ADR-007-save-schema-upgrade.md) 取代；其余规则不变。

## Decision

模组同时支持 RimWorld 1.6 与 1.7，但只维护 1.7：

- 1.6 冻结：`1.6/Assemblies/MugirlRace.dll` 是最后一个 1.6 构建，`LoadFolders.xml` 的 `v1.6` 列表及其引用的目录（`1.6/`、`Bio_1.6/`、`Odyssey_1.6/`、`Versions/1.6/`）不再修改。`1.6/Source` 保留为冻结 DLL 对应的源码快照，`docs/tools` 中的 1.6 验证驱动与脚本只作追溯。
- 1.7 的版本相关内容全部放在 `1.7/`：`Assemblies`、`Defs`、`Patches`、`Languages`、`Shaders`、`Textures`（只放 1.7 专用的覆盖贴图），可选 DLC 与第三方内容放在 `1.7/Mods/<名称>`，由 `IfModActive` 条件加载。
- 根目录 `/` 两个版本共用，只提供 `Textures/` 与 `Sounds/`。
- 1.7 源码在根目录 `Source/`（`net10.0`），维护工具在根目录 `tools/`。

## Context

1.7 换用 Waylith 引擎与 .NET 10 运行时，构建引用、Harmony 主版本（3.0.0-rc.2）与渲染路径都与 1.6 不同；本机已没有 1.6 游戏安装，1.6 工程无法再按原引用编译。官方说明 1.6 与 1.7 存档可以双向读取。原先据此要求 XML 与存档中的类型名、Scribe 标签、defName 在两版保持一致；2026-10-15 起改为由统一升级层单向升级旧存档（见规则 2）。

`ModContentPack.InitLoadFolders` 在没有当前版本列表时回退到不高于当前版本的最高列表。增加 `v1.7` 后，1.7 不再加载任何 1.6 目录；1.6 也不会读取 `1.7/`。

## Rules

1. 不修改 `v1.6` 列表引用的任何文件；共用根目录中的贴图与声音不改名、不删除、不改内容。1.7 需要不同的贴图（例如带 `_Uncompressed` 后缀以避开 BC7 压缩、或修正命名错误的遮罩）时，在 `1.7/Textures` 中按相同相对路径放置覆盖文件。后加载的目录在同一相对路径上优先。
2. **（2026-10-15 被 ADR-007 取代）** 原规则：“1.7 的 Def、补丁和翻译独立演进。1.6 的 defName、Keyed 键、存档类型与 Scribe 标签在 1.7 中保持存在，删除必须登记到验证基线并写明理由。”

   现规则：
   - 1.7 的 Def、补丁和翻译独立演进，名字可以按 [ADR-009](ADR-009-naming-and-layout.md) 改动，不再为旧存档保留。
   - 旧存档读入 1.7 时由 `Mugirl.SaveUpgrade` 单向升级一次（1.6 → 1.7）。旧名字的处置由验证器 `C.save-upgrade` 对照旧版语料检查。
   - 依赖 Mod 使用的名字仍然冻结（[ADR-008](ADR-008-public-api.md)）。
3. 一次加载只选中一个版本的程序集：`1.6/Assemblies` 与 `1.7/Assemblies` 各自只有 `MugirlRace.dll`（1.7 另带 portable PDB）。编译期 Facade、测试模组和验证程序从不进入任何加载目录。

   普通构建（包括 `dotnet build Source/MugirlRace.slnx`）只输出到 `Source/MugirlRace/bin/<Config>/`。只有显式部署才写 `1.7/Assemblies`：在 `Source/` 下运行 `dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true`（`-p:MugirlDeployDir=` 可改目标）。SDK 由 `Source/global.json` 固定。
4. 发布只用 `tools/New-ReleasePackage.ps1` 生成的暂存目录。1.7 的游戏内上传会复制整个模组目录（含 `Source`、`docs`、`DevData`、`TMP` 与 `.git`），不能直接从开发目录上传。

## Consequences

- 1.7 的 `Defs`、`Languages` 与 FA 内容在仓库中与 1.6 各存一份；改动只发生在 1.7 一侧。
- Humanoid Alien Races 目前只有 1.6 构建，1.7 通过版本回退加载它的 1.6 程序集并运行在兼容层上。它仍是硬依赖，HAR 发布 1.7 构建后需要复核编译期 Facade 与验证结果。
- 若将来需要彻底移除 1.6，只需删除 `v1.6` 列表与对应目录；1.7 不依赖它们。
- 升级是单向的。旧存档在 1.7 中升级并保存后，冻结的 1.6 构建无法读取它（找不到新的类型名、defName 与标签，相应数据丢失）。要回到 1.6，只能用升级前自动生成的 `<名称>.rws.mugirl-schema0.bak`（[ADR-007](ADR-007-save-schema-upgrade.md)）。
- 设置文件 `Config/Mod_<文件夹>_MugirlMod.xml` 由 1.6 与 1.7 共用（配置目录不分游戏版本）：
  - 1.7 启动时按 `settingsSchema` 升级它。
  - 设置类改名为 `Mugirl.Core.MugirlSettings` 之后，1.6 每次启动报一次找不到类，改名的设置项回到默认值。
  - 1.6 写回会去掉 `settingsSchema` 与新标签，下次 1.7 启动再升级一次。
  - 设置行与改名尚未落地（Wave R/S）。
