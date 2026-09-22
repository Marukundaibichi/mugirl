# 文档与工作区整理（2026-09-21）

本轮整理文档、文件位置和可再生成的临时产物。正式源码、DLL、Def、贴图、声音与用户已有改动均按整理前工作区保留，没有重建正式 DLL 或运行玩家存档。

## 文件结构

- 根 [README.md](../../README.md) 作为唯一开发手册，合并开发流程、代码入口、目录说明、资料归档、架构导航和历史索引；根 `AGENTS.md` 仅保存 AI 必守规则，要求后续不再创建子目录 README/AGENTS。
- 功能专题归入 `docs/features`，日期记录归入 `docs/records`，ADR 保留在 `docs/architecture`。描边说明改为 `docs/features/outline-guide.md`，描边源码和所有工具路径保持不变。
- 工具说明合并到[验证手册](../validation-runbook.md)，删除 9 份内容已并入其他文档的重复说明。维护范围内只剩根 `README.md` 和 `AGENTS.md` 两个入口文件，并同步静态验证的文档清单。
- 将原 `TMP` 中长期资料分为 `DevData/Assets`、`Backups`、`Evidence`、`Experiments`、`Releases`。其中素材、回退 DLL、测试存档、日志和截图全部保留，搬移逐文件 SHA256 校验一致。
- 原 `Textures/FA.rar` 移到 `SourceAssets/FA.rar`，内容不变，继续作为应纳入版本控制的源素材，避免进入游戏资源包。
- 怀孕心情测试源码与启动脚本从 `TMP/PregnancyMoodProbe` 提升到 `docs/tools/PregnancyMoodProbe.cs` 和 `Start-PregnancyMoodValidation.ps1`，只调整源码文件名引用。测试游戏仍写入外部 `RimWorldModTests`。
- 三个纹理处理脚本的备份位置改为 `DevData/Backups`，对应现有原图与清单；处理规则不变。

## 清理结果

删除原有临时文件 **10,249 个，共 245,647,549 字节（约 234.3 MiB）**。

| 清理项 | 处理方式 |
| --- | --- |
| 5 个发布展开目录，共 9,859 个文件 | 对照原 ZIP 逐文件检查路径、数量、长度和 SHA256 后删除；6 个历史发布 ZIP 均保留 |
| `1.6/Source/bin`、`obj` | 将 DLL、PDB、生成源码归档至 `DevData/Backups/BuildOutputs`，清理剩余构建缓存 |
| 两个描边测试 Unity 工程的 `Library` | 保留 DLL/PDB 后删除缓存；工程、资源源码、日志和 QA 图仍在 `DevData/Experiments` |
| 隔离冷启动测试的 `MissileGirl/Cache` | 清理 6 个生成缓存文件；测试配置与证据仍保留 |

清理前检查实际绝对路径和 Windows `ReparsePoint` 属性；仅处理本仓库内普通文件。未访问或清理外部应急归档中的测试游戏，也未创建目录联接。

## 验证与限制

- 核对 2,174 个正式工作区文件的 SHA256，全部与整理前一致，其中源素材压缩包按新路径核对。
- RimSort 全量审计：2,849 个候选目录，`cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。
- 整理前标准静态检查在 `git diff --check` 处失败，原因是已有 `1.6/FacialAnimation/Defs/FaceShapeDefs/Mugirl_Shapes.xml` 第 3、50 行行尾空格。本轮保留该 XML 内容，不将此基线问题算作整理新增问题。
- 为检查后续项目，另在单个验证进程中排除行尾空格规则后继续静态检查；纹理检查报告缺少 42 张 `Heads_Blank/Normal*/Female/blush_*`、`lovinblush_*` 烘焙贴图。整理前文件快照确认这 42 张均已缺失，未修改检查器或还原用户已删除的纹理。因此完整静态验证仍未通过。
- 文档最终合并后保留 29 份 Markdown、81 个有效本地链接，所有文档均可从根 README 到达，仅根目录保留 README 与 AGENTS。静态脚本的必需文档检查、本轮文档及忽略规则的 `git diff --check`、修改脚本的语法检查均通过；完整静态命令仍在上述原有 XML 空白问题处停止。
- `New-WorkshopPackage.ps1 -SkipBuild` 成功复制 1,913 个发布文件、排除 261 项；未带入开发资料、源素材压缩包、源码或 PDB，包内 DLL 与正式 DLL 相同。检查后删除本轮生成的展开副本，验证日志归档，`TMP` 清空。

本机证据目录为 `DevData/Evidence/MaintenanceCleanup-20260921`：

- `moves.json`：227 项资料搬移与内容校验摘要。
- `deleted.json`、`cleanup-plan.json`、`summary.json`：清理范围、发布 ZIP 核对与释放空间。
- `production-verification.json`：正式文件及生产 DLL 哈希校验。
- `directory-audit.json`：完整目录环路审计。
- `document-check.json`、`package-verification.json`、`package-check.log`：文档、脚本语法与打包复查。
- `static-before.log`、`static-after-existing-whitespace-excluded.log`：原始静态基线及排除已有空白规则后的复查日志。
- `document-consolidation.json`：重复文档合并与路径映射；最终文档入口、链接和可达性检查另见 `document-consolidation-check.json`。
- `document-consolidation-static.log`、`document-consolidation-directory-audit.json`：入口调整后的静态复查与目录环路审计，后者 `affected_candidates=0`、`scan_errors=0`。

历史日志和机器清单中的原绝对路径保留原样，按 `moves.json` 查找归档位置。归档资料不进入 Git 或发布包，不能仅依据 `.gitignore` 将其视作可删除缓存。

## 普通贴图目录合并

随后将原 `1.6/Textures/Things/Pawn/Mechanoid/MugirlBulldog` 的 6 张普通贴图合并到根 `Textures/Things/Pawn/Mechanoid/MugirlBulldog`，移除空的 `1.6/Textures`，并同步维护脚本的扫描目录。没有目标文件冲突，贴图内容不变。

依据当前 `LoadFolders.xml`，对合并前后全部 1,591 个贴图文件比较“启用条件 + 游戏内相对路径 + SHA256”，结果完全一致；搬移清单见同一证据目录的 `texture-layout.json`。FA 贴图仍在 `1.6/FacialAnimation/Textures/FA`，仅随 `Nals.FacialAnimation` 条件加载。
