# 发布检查清单

本文档用于创意工坊或本地发布前的最后确认。目标是：发布包干净、可加载，不含临时文件和源码。

1.6 时代的 `docs/tools/New-WorkshopPackage.ps1` 已随 1.6 冻结，不再用于发布。1.7 使用 `tools/New-ReleasePackage.ps1` 和 `tools/Test-ReleasePackage.ps1`。

**1016.1 已完成同名奶量迁移修复的发布验收及本地打包。** 该发布包 DLL 为 `0B9364FFDF3DB3C6821C6E68EF347ECDB6D87948E8090F6BD195526D37904B7C`，2953 个发布文件及 ZIP 已逐哈希核对。实机矩阵保留原诊断失败，最终会话来源和发布信息见[1016.1 记录](records/release-1016.1-2026-10-16.md)。1016 的 4980 适配与升级信件验收见[1016 记录](records/release-1016-2026-10-16.md)，原 4979 重构验收留在[完成记录](records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)。未上传创意工坊；上传仍需 owner 明确授权。后续发布按下列流程重新验证。

随后按用户要求编译部署了 ThinkTreeKeys 修复，本地正式 DLL 为 `6B3145754F1833FF93F4E1B62C083586A84E17B664D82E1F04B9AF644480D406`，验证见[修复与部署记录](records/thinktree-key-migration-2026-10-16.md)。迁移专项与历史兼容通过，但扩展控制组存在未处置失败，尚不能作为完整发布验收通过。这一轮只编译部署和测试，未更新已有 1016.1 发布包。

用户在获知上述失败后明确要求更新 Git 并打包 1016.2，按[决策 §10](refactor-1.7/decisions.md#10-10162-本地交付要求2026-10-16)交付当前修复并保留已知问题。详见 [1016.2 发布记录](records/release-1016.2-2026-10-16.md)。这次本地交付不把失败检查改判为成功，也不改变后续发布的默认验证要求。

2026-10-17 的当前布局修复已另行正式部署并完成专项、默认套件、14 个历史读档会话及启动冒烟。用户随后要求打包 1016.3，按[决策 §13](refactor-1.7/decisions.md#13-10163-本地打包要求2026-10-17)沿用该次实测 DLL/PDB，使用 `-AllowDirty` 如实记录未提交源码。原备份未识别键与前次扩展控制组问题在[1016.3 发布记录](records/release-1016.3-2026-10-17.md)中继续披露，不据此改判完整验收状态。

随后按[决策 §14](refactor-1.7/decisions.md#14-ai-来源警告策略与部署授权2026-10-17)修正 AI 来源警告策略并正式部署。当前 DLL 与 1016.3 发布包中的构建不同；本轮没有重新打包或上传，既有 ZIP 不包含这次修复。两种日志复现、原键/进度保留及实际验证边界见[警告策略记录](records/thinktree-key-migration-2026-10-16.md#ai-来源改为警告与进度保留2026-10-17)。

用户随后要求更新 Git 并打包 1017，按[决策 §15](refactor-1.7/decisions.md#15-1017-git-同步与本地发布2026-10-17)提交同步并交付当前已测试的修复；来源及打包复核见[1017 发布记录](records/release-1017-2026-10-17.md)。已披露的旧控制组问题与完整玩家环境边界仍保留，不借此宣称全部发布矩阵通过。

About声明1.6与1.7。初始1.6基线的1323个文件最终逐哈希核对无改动、无缺失、无新增，v1.6加载列表相同。开发树中的旧NewbornBackstoryProbe按冻结规则保留，但发布白名单排除了它；对应警告不是发布包携带了探针。

## 发布前

1. 确认工作区没有无关改动。重构期间工作区本来就有大量未提交改动（owner 决定不提交），这种情况下要在清单中用 `-AllowDirty` 记录，并核对整树归档。
2. 在独立源码副本的 `Source/` 中构建候选，并通过 `-p:DeployToMod=true -p:MugirlDeployDir=<实际模组>/1.7/Assemblies` 显式部署，或使用 `Test-InGame.ps1 -BuildMod -Deploy`。保留 CompileOrder / TypeOrder 构建输入；最终 DLL/PDB 必须经过同一元数据顺序步骤。普通构建不写 `1.7/Assemblies`。
3. 运行完整离线验证 `pwsh tools/Validate.ps1 -Shipped -WorkRoot <独立目录>`，确认两个运行时一致且全部通过，其中包括：
   - `A.about`：About.xml 实际声明支持的版本，身份和依赖不变；
   - `C.public-api`：完整 151 项冻结契约未变；
   - `C.save-upgrade`：上述脚本默认执行 ratchet；还须对刚生成的验证器显式运行 `--save-upgrade strict --assume-active all --report <报告路径>`。标准运行器在 `<WorkRoot>/mg17-validation/bin/MugirlRace.Validation/release/MugirlRace.Validation.exe`，游戏运行器在 `<WorkRoot>/mg17-clr/MugirlRace.Validation.exe`；分别留存 strict 报告，两者都要通过。`--assume-active all` 与默认 Validate 的条件内容范围一致；漏掉它会排除 FA 等条件目录，不能据此判断这些目录中的冻结 Def 已退役。详见[验证器模式](validation-runbook.md#csave-upgraderatchet-与-strict)。
4. 运行 `pwsh tools/SmokeTest.ps1 -QuickTest -DevMode` 和 `pwsh tools/Test-InGame.ps1 -Suite default`。将历史夹具复制到本轮独立目录，用当前候选 DLL 的 `--save-corpus` 重新生成离线报告，核对夹具本体哈希不变后，只更新副本中的 `offline-report.json`；再运行 `pwsh tools/Test-InGame.ps1 -Fixture all -FixtureRoot <本轮副本>`。步骤归属或计数变化后不能沿用旧 DLL 的报告，也不能从实机结果反推期望。全部通过运行锁串行、静音且隐藏窗口；控制组和旧档均验证二次读回，比较不能豁免整个 consumed owner、claimed Job 或所有 null/default。
5. 按[验证手册](validation-runbook.md)核对界面、动画、在途工作及可选兼容。文案、数值、RNG 次序和调度相位必须守恒；Lance 等无法判别的历史状态须有明确保档失败，不能通过统一结束工作凑出绿色结果。
6. 检查 `About/About.xml`、`LoadFolders.xml` 和 `1.7/Mods/<名称>` 条件目录。`LoadFolders.xml` 的 `v1.6` 列表与冻结的 1.6 目录不能改动。
7. 确认中英翻译 key 对齐，玩家可见文本不缺 key（`C.keyed`、`C.language-files`、`C.translation-keys`）。
8. 运行 `pwsh tools/Test-Docs.ps1`。
9. 按[目录与清理约定](../README.md#文件放置与清理)核查 `TMP`：先归档原始素材、回退 DLL、存档和需追溯的证据，再清理可再生成的输出，不能整目录盲删。

## 生成发布包

```powershell
pwsh tools/New-ReleasePackage.ps1                 # 暂存到 %LOCALAPPDATA%\MugirlRace\Release\<版本>\HAR.MugirlRace
pwsh tools/New-ReleasePackage.ps1 -Archive        # 另外打包进 DevData/Releases
pwsh tools/Test-ReleasePackage.ps1                # 独立复核最新的暂存目录
```

`New-ReleasePackage.ps1` 默认只读开发目录；`-Archive` 仅在 `DevData/Releases` 新建 ZIP，不构建、不修改源码或正式 DLL。遇到以下情况，它会拒绝暂存：

- 工作区有未提交改动（除非使用 `-AllowDirty`）；
- 冻结的 1.6 DLL 与 `tools/release-baseline.json` 不符；
- 必需 DLL 或 PDB 缺失；`1.7/Assemblies/MugirlRace.dll` 比生产构建输入旧（包括 TypeOrder、SDK 固定文件及分析器配置，除非使用 `-SkipBuildCheck`），或 PDB 不属于它；`-SkipBuildCheck` 不能跳过必需文件检查；
- 目标位于模组目录或游戏的 Mods 目录中。

发布文件清单由 `LoadFolders.xml` 推导，两个脚本共用规则。暂存目录旁会写出 `HAR.MugirlRace.manifest.json`（每个文件的大小与 SHA-256、游戏版本、SDK 版本、git 提交）和 SteamCMD 使用的 `workshop_item.vdf`。

上传时推荐用 SteamCMD 的 `workshop_build_item` 加载 `workshop_item.vdf`。不要从开发目录直接上传：1.7 的游戏内创意工坊上传会复制模组目录中的全部文件，包括 `DevData`、`TMP`、`.git` 和源码。

## 发布包不应包含

以下内容由 `tools/release-baseline.json` 的 `excludedDirectories` 与 `excludedFiles` 排除，`Test-ReleasePackage.ps1` 会逐项复核：

- `Source`、`tools`、`docs`、`DevData`、`TMP`、`SourceAssets`、`.git` 等开发目录；
- `bin`、`obj`、`TestResults`；
- `.cs`、`.csproj`、`.sln`、`.slnx`、`.props`、`.targets`、`.ps1`、`.py`；
- `.sai2`、`.psd`、`.rar`、`.zip`、`.tmp`、`.bak`；
- 游戏生成的 `Assets.zip.*`、`*.spv-cache`；
- `README.md`、`AGENTS.md`。

需要保留的发布 ZIP 放在 `DevData/Releases`。展开副本经逐文件哈希与 ZIP 核对一致后，才可以清理。

## 发布包应包含

- `About`（`About.xml`、`Preview.png`、`PublishedFileId.txt`）与 `LoadFolders.xml`；
- `LoadFolders.xml` 每个加载目录中的 `Assemblies`、`Defs`、`Patches`、`Languages`、`Textures`、`Sounds`、`Shaders`、`AssetBundles`；
- 1.7：`1.7/Assemblies/MugirlRace.dll` 与 `MugirlRace.pdb`、`1.7/Defs`、`1.7/Patches`、`1.7/Languages`、`1.7/Shaders`、`1.7/Textures`、`1.7/Mods/<名称>`；
- 冻结的 1.6：`1.6/Assemblies/MugirlRace.dll`（大小与 SHA-256 必须等于 `release-baseline.json` 的记录）、`1.6/Defs`、`1.6/Languages`、`1.6/Patches`、`1.6/Resources`、`1.6/FacialAnimation`，以及条件目录 `Bio_1.6`、`Odyssey_1.6`、`Versions/1.6/Integrations/*`；
- 根 `Textures` 与 `Sounds`。

## 最后检查

```powershell
git diff --check
pwsh tools/Test-Docs.ps1
```

Windows 上出现 LF/CRLF 提示不等于空白错误。如果有行尾空格或冲突标记，必须修复。
