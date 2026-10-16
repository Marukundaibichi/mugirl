# 1.7 重构执行计划：Wave R、R2、S、V、D

本文保留原波次计划及验收清单。**2026-10-16 状态：R、R2、S、V与D本地交付均已完成；发布包已核对，未上传创意工坊。不要对当前树重新应用下文历史 R/R2 改名命令。** 最终范围及证据见[完成记录](../records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)与[根开发手册](../../README.md)；[原交接记录](../records/refactor-1.7-handoff-2026-10-15.md)描述的是恢复前现场。

实际实现服从 decisions §7 的行为与文案冻结：保留累计计时、公开判定入口、原触发时点和仍有效的内容；旧工作按已证明的布局续接，不统一中断。表中被此约束取代的改动保留为原计划记录，不能据此重新删除状态或修正玩法。Core100、Incidents200、Corporate300、Milk400、Restraints500、Combat600、Content700 已实际登记；151 项公开 API 由 `tools/RenameApply/specs/public-api.json` 单一清单验证。生成器现位于 `tools/RenameApply/specs/generators`，下文的旧 scratchpad 路径与 DLL 哈希仅用于追溯原执行记录。

各文档的优先级：[decisions.md](decisions.md) 高于 [design.md](design.md)，design.md 高于 [模块计划](module-plans/)。模块计划写于 owner 答复之前，与决策 §5 冲突时以决策为准，例如 MooGirl 时代改为不支持（D3），依赖 Mod 必须继续可用（D5）。命令参数已对照脚本的 `param` 块和工具的选项源码（`tools/RenameApply/Options.cs`、`Source/Tests/MugirlRace.Validation/Infrastructure/ValidationOptions.cs`）核对。

## 0. 前提、顺序与约定

- 顺序为 R → R2 → S（七个模块）→ V → D。R 与 R2 由单一代理执行，执行期间整棵树冻结。S 按模块并行，并行规则见交接记录 §7。V 由集成者执行，游戏内运行只有一个执行者。
- 不提交 git，也不建 worktree（D6）。每个波次开始前和结束后都按 §1.1 做整树归档，归档是唯一的恢复点。
- 命令在 PowerShell 7 中运行，按用户规则先加载 UTF-8 环境（`C:\Users\Fishiv\bin\utf8-env.ps1`）。下文的变量含义如下：

```powershell
$Root = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\mugirl'
$Game = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld'
$W    = "$env:TEMP\mg17-<代理名>"     # 每个代理一个，必须在 Mod 目录之外，路径不宜太深
$V    = "$W\mg17-validation\bin\MugirlRace.Validation\release\MugirlRace.Validation.exe"   # Validate.ps1 -WorkRoot $W 构建的验证器
$Dll  = "$W\mg17build\validation\mod\MugirlRace.dll"                                     # 同一次运行隔离构建的 Mod
Set-Location $Root
```

- dotnet 一律在 `Source/` 中启动，这样 `Source/global.json` 固定的 SDK 10.0.401 才会生效。本机装有 8.0.303、9.0.315、10.0.401 三个 SDK。
- 普通构建只输出到 `Source/MugirlRace/bin/<Config>/`。只有显式部署才写入 `1.7/Assemblies`（K3）。

## 1. 通用程序

### 1.1 波次前归档与比对

归档内容与 `DevData/Backups/PreR-1.7-20261015/` 相同。下面的命令在 2026-10-15 用 `tools`、`About` 等目录试运行过。

```powershell
$Dest = "$Root\DevData\Backups\<波次>-1.7-<yyyyMMdd>"      # 例如 PostR-1.7-20261020；不要覆盖已有目录
New-Item -ItemType Directory -Force $Dest | Out-Null
$top = 'Source', '1.7', 'tools', 'docs', 'About', 'Textures', 'Sounds', 'LoadFolders.xml', 'README.md', 'AGENTS.md', '.gitignore'
& "$env:SystemRoot\System32\tar.exe" -a -c -f "$Dest\tree.zip" --exclude=bin --exclude=obj --exclude=TestResults --exclude=.vs @top
$prefix = "$Root\"
$top | ForEach-Object { if (Test-Path -LiteralPath $_ -PathType Leaf) { Get-Item -LiteralPath $_ -Force } else { Get-ChildItem -LiteralPath $_ -Recurse -File -Force } } |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|TestResults|\.vs)\\' } | Sort-Object FullName |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($prefix.Length).Replace('\', '/') } |
    Set-Content -LiteralPath "$Dest\tree-manifest.sha256" -Encoding utf8NoBOM
# 有对应构建时：Copy-Item $Dll, ($Dll -replace '\.dll$', '.pdb') $Dest，写 SHA256SUMS.txt，并在 SNAPSHOT-NOTE.txt 中写明波次、时间、构建来源、是否部署过
```

比对现场树与某份归档清单，列出改动或缺失的文件：

```powershell
$m = Get-Content -LiteralPath "$Dest\tree-manifest.sha256" | Where-Object { $_ } | ForEach-Object { $h, $p = $_ -split '  ', 2; [pscustomobject]@{ Hash = $h; Path = $p } }
$m | Where-Object { -not (Test-Path -LiteralPath $_.Path) -or (Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $_.Hash } | Select-Object -ExpandProperty Path
```

### 1.2 构建与离线验证

```powershell
Push-Location Source
dotnet build MugirlRace.slnx                                                           # 0 个警告、0 个错误；1.7/Assemblies 的哈希不变
dotnet build Tests/MugirlRace.GameValidation/MugirlRace.GameValidation.csproj -p:MugirlPlumbingOnly=true
dotnet build Tests/MugirlRace.GameValidation/OfflineTests/MugirlRace.GameValidation.OfflineTests.csproj -c Release
& ./Tests/MugirlRace.GameValidation/OfflineTests/bin/Release/net10.0/MugirlRace.GameValidation.OfflineTests.exe
Pop-Location
pwsh tools/Validate.ps1 -WorkRoot $W           # 两个运行时 + 单元测试 + ShaderCheck；结果在 $W\mg17-validation\report-*.json
pwsh tools/Validate.ps1 -SelfTest -WorkRoot "$W-st"
```

- 在现场树中不能并发执行 `dotnet build`，多个代理之间要串行。`Validate.ps1` 用 `--artifacts-path` 把中间文件隔离到 WorkRoot，所以各代理用各自的 `-WorkRoot` 就可以并行。
- `-SelfTest` 目前还不包括 parity 与提取器的负对照（A2 提出过请求，未实现）。在补进 `Validate.ps1` 之前，手动运行 parity 负对照，命令取自 A2 的自测驱动：

```powershell
Push-Location Source
foreach ($side in 'A', 'B') { dotnet build "Tests/MugirlRace.Validation/Controls/Parity/$side/ParityControl$side.csproj" -c Release --artifacts-path "$W/parity/$side" "-p:RimWorldDir=$Game" }
Pop-Location
$pa, $pb = 'A', 'B' | ForEach-Object { (Get-ChildItem "$W\parity\$_" -Recurse -Filter ParityControl.dll | Where-Object FullName -match '\\bin\\' | Select-Object -First 1).FullName }
& $V --parity $pa $pb --map Source/Tests/MugirlRace.Validation/Controls/Parity/map.tsv --expect Source/Tests/MugirlRace.Validation/Controls/Parity/expected.txt --game $Game
```

- 只有集成者可以改写基线：
  - `pwsh tools/Validate.ps1 -WorkRoot $W -UpdateBaselines -SkipUnitTests`；
  - 重置 ratchet：`& $V --mod $Dll --assume-active all --ratchet-reset`。

  改写之后逐条审阅差异。基线目录没有纳入 git，可以先把它复制到 `$W`，再用 `git diff --no-index --stat <副本> Source/Tests/MugirlRace.Validation/Baselines` 比较。

### 1.3 部署与游戏内运行

游戏内运行由 `%TEMP%\mugirl-ingame.lock` 串行化。本安装的 RimWorld 正在运行时（例如 owner 正在玩），脚本会拒绝启动；这时只能等待，不要结束 owner 的进程。

```powershell
pwsh tools/Test-InGame.ps1 -BuildMod -Deploy -Suite default     # 在 %TEMP% 的源码副本中构建，由工程的 DeployToMod 写入 1.7/Assemblies
pwsh tools/SmokeTest.ps1 -QuickTest -DevMode                     # 测试已部署的 DLL
pwsh tools/Test-InGame.ps1 -Fixture control                      # 对照组：被测构建自己写存档、再读回，升级器不动作，diff 为空
pwsh tools/Test-InGame.ps1 -Fixture all -SuiteOption upgrade.requireOffline=true   # 清单标为 feasible 的全部夹具，加上 control
pwsh tools/Validate.ps1 -Shipped -WorkRoot $W                    # 已部署 DLL 的离线验证
```

- 只部署、不运行：`Push-Location Source; dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true; Pop-Location`。
- 结果位置：Test-InGame 在 `TMP/InGameValidation/<时间戳>/summary.json`，SmokeTest 在 `TMP/SmokeTest/<时间戳>/`。
- `-Fixture` 时默认语言为 ChineseSimplified；需要英文时加 `-Language English`。
- DLC 组合默认取夹具清单中的组合。其他组合用 `-Config`，可选 AllDlc、CoreOnly、NoRoyalty、NoIdeology、NoBiotech、NoAnomaly、NoOdyssey。

### 1.4 恢复

- **恢复部署的 DLL**：游戏必须已关闭。

  ```powershell
  Copy-Item DevData/Backups/PreRefactor-1.7-20261015/MugirlRace.dll, DevData/Backups/PreRefactor-1.7-20261015/MugirlRace.pdb 1.7/Assemblies/
  (Get-FileHash 1.7/Assemblies/MugirlRace.dll).Hash    # 应为 220E5D1A0CD5D2178A4643C56C1058B91520CCC2A3089EE679FFB570702F69D2
  ```

- **RenameApply 中断后恢复**：`dotnet tools/RenameApply/bin/RenameApply.dll --restore <备份目录>`。备份目录就是运行时 `--backup` 指定的目录，里面有 `backup-manifest.json`。
- **整树恢复**：只在 owner 同意后进行。步骤是：
  1. 把 `tree.zip` 解压到临时目录：`tar.exe -x -f <tree.zip> -C <目录>`；
  2. 用 §1.1 的比对确认差异；
  3. 逐个复制需要恢复的文件。

  不要对仓库使用镜像删除。

### 1.5 闸门（“绿”的定义）

本节由 [critique-validation §2.5](reviews/critique-validation.md) 按决策改编而来。每个波次适用哪几项，在各节中注明。

| # | 条件 |
|---|---|
| G1 | slnx 构建 0 警告 0 错误，且不写入 `1.7/Assemblies`；GameValidation 在完整模式和 plumbing 模式下都能构建；OfflineTests 构建通过且全部通过；parity 负对照全部触发（§1.2） |
| G2 | `MugirlRace.Tests` 的测试集合不变（测试名经改名映射对应）并且全部通过；唯一允许的“未运行”是显式基准测试 |
| G3 | `Validate.ps1` 两个运行时结果相同；除 `A.about`（Wave D 之前）外全部通过；不加 `-UpdateBaselines` 也通过，加上之后基线零差异 |
| G4 | IL parity 没有未解释的差异，content parity 为零差异。被接受的差异只能来自 owner 签认的 `parity-accept.tsv`（每行 `category<TAB>subject<TAB>reason`）。只适用于 R、R2 |
| G5 | RenameApply 的一致性检查通过（applied == spec，工具内置）；报告中 `literal-*`、`unedited-mention`、`public-alias-needed`、`root-namespace-remaining` 都已逐条归类；`C.forbidden-patterns` 通过。只适用于 R、R2 |
| G6 | `C.save-upgrade` 在 ratchet 模式下不比上一波差：各模块的 ratchet 文件只减不增。到 V 结束时用 `--save-upgrade strict` 通过 |
| G7 | 部署后串行运行游戏内检查：`Test-InGame -Suite default` 与 `SmokeTest -QuickTest -DevMode` 相对 Wave 0（65/1/14，唯一失败是已修复的测试缺陷）没有新失败；`-Fixture control` 的 diff 为空 |
| G8 | `B.patch-order` 与 `Baselines/patch-order.txt` 一致，同一目标上的补丁实际执行顺序不变，除非该波次明确声明了改变 |

## 2. Wave R：保持身份的改名

范围（决策 §3、critique F §2.1）：命名空间、类型、文件移动（包括所有跨模块移动和多类型文件拆分）、defName、DefInjected 键与 handle、XML 引用、abstract Name、基线中的名字翻译。行为不变。Keyed 改名与非日期类的内容改名已推迟到 S（`specs/deferred-S.json`）。

### 2.1 生成器工作区

`tools/RenameApply/specs/generators/common.py` 写死了 `SCRATCH`（会话 scratchpad），其中 `SPEC = SCRATCH/spec`，`FINAL = SPEC/final`，生成器的输出写到 `FINAL/_gen`。仓库里的 `specs/` 对应当时的 `spec/final/`，`specs/generators/` 对应 `spec/final/_gen/`。所以运行生成器前要重建这个布局。下面的 `$S` 是交接记录 §6 第 1 步保全的副本。

```powershell
$S = "$Root\DevData\Backups\Scratchpad-WaveA-20261015"
$G = "$env:TEMP\mg17-specgen"
New-Item -ItemType Directory -Force "$G\spec\final", "$G\understand\dlls" | Out-Null
Copy-Item "$S\spec\*.json" "$G\spec\"
Copy-Item "$S\understand\defs.json" "$G\understand\"
Copy-Item "$S\understand\dlls\types.tsv" "$G\understand\dlls\"
Copy-Item tools\RenameApply\specs\* "$G\spec\final\" -Recurse
Rename-Item "$G\spec\final\generators" _gen
# 把 $G\spec\final\_gen\common.py 中 SCRATCH 那一行改成 $G。生成完成后，把 $G\spec\final 复制回 tools\RenameApply\specs（_gen 复制回 generators）。
```

生成器的用法以各脚本文件头为准（[rename-tool](rename-tool.md) 的“Spec generators and linter”一节也有说明）：

| 脚本 | 用法 |
|---|---|
| `gen_R.py` | `gen_R.py <tree>` |
| `lint_spec.py` | `lint_spec.py <R 作用的树> [<post-R 树>] [--post-r2 <post-R2 树> --r2-applied <json>]` |
| `defof_facts.py` | `defof_facts.py <post-R 树>` |
| `gen_R2.py` | `gen_R2.py <post-R 树> <R applied json>` |
| `gen_deferred.py` | 无参数 |
| `gen_ownership.py` | `gen_ownership.py <post-R2 树> <R2 applied json>` |
| `gen_public_api.py` | `gen_public_api.py <R applied json> <R2 applied json>` |
| `applied_to_manifest.py` | `applied_to_manifest.py <R applied> [<R2 applied>] --dll-types <tsv> --tree <树> [--ownership <json>]` |
| `dll_types.ps1` | `dll_types.ps1 -Dll <MugirlRace.dll> -Out <tsv>` |

### 2.2 前置条件与归属

- 交接记录 §6 的第 1–6 步已完成：A5 复审完成，集成构建已在游戏中通过 G7，引擎事实已在 4979 上复核（读档相关方法已由迁移知识库 E52–E54 覆盖，剩余条目见交接记录 §6 第 6 步）。
- 已经确定 K1 行如何变成 C# 升级表。生产代码中没有 `ISaveUpgradeModule` 实现，`applied_to_manifest.py` 只输出 JSON。R 必须同时落地类型与 Def 的改名行，否则改名后的构建读旧档会丢失数据。做法有两种，任选其一：写一个生成器，把 rows.json 转成 `SaveUpgrade/Modules/<Module>/` 中带 `[SaveUpgradeModule("<Module>", order)]` 的分部类；或者约定一个嵌入资源格式。确定后补单元测试。
- 冻结名单只保留一份：`Source/Tests/MugirlRace.Validation/Baselines/public-api.json`（107 条）与 `tools/RenameApply/specs/public-api.json`（151 条，`entries` 格式）统一，并确认 `C.public-api` 读的是终版。
- owner 已签认 A3 列出的顺序变化（D9），并已写成 `parity-accept.tsv`。
- 归属：一个代理独占整棵树，其他代理停止写入。

### 2.3 步骤

1. 在 §2.1 的工作区中，用集成树重新生成 R 规格并 lint，lint 必须 0 错误：

   ```powershell
   python "$G\spec\final\_gen\gen_R.py" $Root
   python "$G\spec\final\_gen\lint_spec.py" $Root
   ```

   生成后复制回 `tools/RenameApply/specs/`。可选：先用 scratchpad 副本中的 `tooltest/final/pipeline.ps1` 在树的副本上演练整条流水线，它的路径写死在 scratchpad，使用前要改。
2. 构建工具。要在 `Source/` 中启动，使 Roslyn 与 Mod 构建所用的 SDK 一致：

   ```powershell
   Push-Location Source
   dotnet build ../tools/RenameApply/RenameApply.csproj -c Release -o ../tools/RenameApply/bin
   $T = (Resolve-Path ../tools/RenameApply/bin/RenameApply.dll).Path
   ```

3. 试运行（dry-run），只写报告：

   ```powershell
   $Out = "$env:TEMP\mg17-waveR"
   dotnet $T --repo $Root --spec "$Root\tools\RenameApply\specs\R" --frozen-names "$Root\tools\RenameApply\specs\public-api.json" --game $Game --report "$Out\R-dry.json" --dry-run
   ```

   逐条审阅报告中的 findings，归类要求见 G5。
4. 按 §1.1 归档为 `PreRApply-1.7-<日期>`，然后真实运行，同时写 applied 日志并保留备份：

   ```powershell
   dotnet $T --repo $Root --spec "$Root\tools\RenameApply\specs\R" --frozen-names "$Root\tools\RenameApply\specs\public-api.json" --game $Game --report "$Out\R.json" --applied "$Out\R.applied.json" --backup "$Out\R-backup" --keep-backup
   Pop-Location
   ```

   退出码：0 表示成功；1 表示有错误，什么都没写；2 表示用法或规格错误；3 表示环境错误，已自动回滚。进程中断时，用 §1.4 的 `--restore` 恢复。R 与 R2 不能在同一次运行中执行。
5. 生成 K1 行，按 §2.2 确定的方式接入升级器：

   ```powershell
   pwsh tools/Validate.ps1 -WorkRoot $W
   pwsh tools/RenameApply/specs/generators/dll_types.ps1 -Dll $Dll -Out "$Out\postR-dll-types.tsv"
   python "$G\spec\final\_gen\applied_to_manifest.py" "$Out\R.applied.json" --dll-types "$Out\postR-dll-types.tsv" --tree $Root > "$Out\R-rows.json"   # exit 0
   ```

   其中设置类改名（`Mugirl.MugirlSettings` → `Mugirl.Core.MugirlSettings`）要随 R 一起落地（a3-report）。
6. 跑 parity。A 是 PreR 归档，B 是 post-R 构建：

   ```powershell
   $A = "$W\preR"; New-Item -ItemType Directory -Force $A | Out-Null
   & "$env:SystemRoot\System32\tar.exe" -x -f DevData/Backups/PreR-1.7-20261015/tree.zip -C $A
   Copy-Item -LiteralPath Textures, Sounds -Destination $A -Recurse    # content parity 要比较根目录的 Textures/Sounds，归档中没有这两个目录
   # 校准：把 A 在另一个目录重建，与归档 DLL 比较，必须 0 差异
   Push-Location "$A\Source"; dotnet build MugirlRace/MugirlRace.csproj -c Release "-p:RimWorldDir=$Game"; Pop-Location
   & $V --parity DevData/Backups/PreR-1.7-20261015/MugirlRace.dll "$A\Source\MugirlRace\bin\Release\MugirlRace.dll" --content-a $A --content-b $A --report "$W\parity-calibration.json"
   # 正式比较
   & $V --parity DevData/Backups/PreR-1.7-20261015/MugirlRace.dll $Dll --map "$Out\R.json" --content-a $A --content-b $Root --parity-accept <parity-accept.tsv> --report "$W\parity-R.json"
   ```

### 2.4 闸门与产出

- 闸门：G1–G8 全部满足。G7 要先部署 post-R 构建（§1.3）。夹具上的 upgrade 套件这时只记录结果，不作为闸门。
- 产出：
  - `$Out` 中的 `R-dry.json`、`R.json`、`R.applied.json`、`R-rows.json`、parity 报告，存档到 `DevData/Evidence/WaveR-<日期>/`；
  - post-R 整树归档 `DevData/Backups/PostR-1.7-<日期>/`，包含 Release DLL（取自 `$Dll`），它是 R2 的 parity 基线 A；
  - 在 `docs/records/` 写一份波次记录，并从根 README 链接。

## 3. Wave R2：DefOf、PatchModule、共享文件拆分与成员移动

范围（决策 §3、[a3-report](wave-a/a3-report.md)）：

- 按“Def 定义者所有”规则（C04）拆散 DefOf：150 处移动、48 处删除、新建 15 个 DefOf、拆散 14 个；
- 封闭的 `PatchModule` 枚举（27 个成员），并改写 `[MugirlPatch]` 的参数；
- XML 与 DefInjected 拆分：JobDefs、Thoughts_Traits、TraitDefs、Items_Filth、音效、挤奶服装；`Patches/MugirlPatch.xml` 拆成 5 个文件，带 PatchGovernance 头；
- `Keyed/Misc_Gameplay.xml` 拆分（357 个键）；
- 5 个带日期的文件改名；
- 跨 owner 的成员移动：PlayerFactionUtility、TickUtility、GeneratedPawnUtility 移到 Incidents；MountedPawnUtility 拆到 `Core.PawnPredicates`/`MountUtility`，`IsMugirl` 保留为公开 API。

R2 结束后，每个文件恰好有一个 Wave S owner。

步骤与 R 相同，只是规格换成 `specs\R2`，并且必须在 post-R 树上运行：

1. 重新生成并 lint：

   ```powershell
   python "$G\spec\final\_gen\defof_facts.py" $Root
   python "$G\spec\final\_gen\gen_R2.py" $Root "$Out\R.applied.json"
   python "$G\spec\final\_gen\gen_deferred.py"
   python "$G\spec\final\_gen\lint_spec.py" "$W\preR" $Root     # 第一个参数是 R 之前的树（PreR 解压目录）
   ```

2. 试运行，归档后真实运行，参数与 §2.3 第 3–4 步相同，换用 `--spec "$Root\tools\RenameApply\specs\R2"`，输出改为 `R2.json`、`R2.applied.json`，备份目录改为 `R2-backup`。
3. 运行后：

   ```powershell
   python "$G\spec\final\_gen\gen_ownership.py" $Root "$Out\R2.applied.json"          # 生成 ownership-postR2.json，exit 0
   python "$G\spec\final\_gen\lint_spec.py" "$W\preR" <post-R 归档解压目录> --post-r2 $Root --r2-applied "$Out\R2.applied.json"   # L16：每个文件一个 owner
   python "$G\spec\final\_gen\gen_public_api.py" "$Out\R.applied.json" "$Out\R2.applied.json"     # 生成 public-api.json、owner-migration-list.md、frozen-names.json
   ```

4. K1 行用两份日志生成：`applied_to_manifest.py R.applied.json R2.applied.json --dll-types <post-R2> --tree $Root --ownership <ownership-postR2.json>`。`pendingWaveS` 中的行留给 S 中对应的模块处理。
5. 顺序报告：用 scratchpad 副本中的 `tooltest/final/order_diff.py` 比较反射顺序、Def 加载顺序和共享目标上的补丁操作顺序，每一项变化都要签认。
6. parity：A 是 post-R 归档的 DLL 和解压目录，B 是 post-R2 构建，`--map "$Out\R2.json"`。
7. 封闭枚举落地后，`B.patch-metadata` 必须改为按 `PatchModule` 检查。这是 A3 对验证器提出的请求，由集成者完成。

闸门：G1–G8。产出：`DevData/Evidence/WaveR2-<日期>/`、`DevData/Backups/PostR2-1.7-<日期>/`（作为 S 的起点）、重新生成的 `specs/ownership-postR2.json`、波次记录。

## 4. Wave S：按模块的结构重构与旧代码移除

### 4.1 每个模块的共同义务

- **文件归属**：只改 `ownership-postR2.json` 中归本模块的文件。需要改别人的文件时，在报告中提出请求（各计划的 S4 节）。各模块共用的部分归属固定：验证器基线归集成者；`SaveUpgrade/Engine/**` 与 `SaveUpgrade/Hooks/**` 归 upgrader。
- **升级表**：在 `SaveUpgrade/Modules/<Module>/` 中实现 `ISaveUpgradeModule`，标记 `[SaveUpgradeModule("<Module>", order)]`。order 采用 A1 的建议值：Core 100、Incidents 200、Corporate 300、Milk 400、Restraints 500、Combat 600、Content 700（ADR-007 记为尚未定案，第一个模块登记前由集成者确认）。模块计划中的 `SaveUpgrade/Steps/<Module>/` 写法以 `SaveUpgrade/Modules/<Module>/` 为准。
- **每删除一个旧形状，都要同时登记**：
  - 对应的表行：类型、Def、标签、枚举、值、默认值；
  - 每个旧版 Mugirl JobDriver 与 LordJob 类型的策略，或者由某个步骤认领（决策 §2 “In-flight state”）；
  - 可移除容器与并行组。

  步骤分计划和应用两段，可重复执行，每个步骤都要有 golden 夹具 `Source/Tests/MugirlRace.Tests/SaveUpgrade/Golden/<StepId>/{before,after}.xml`，内容只能是合成或匿名化的 Mugirl 节点（F15）。新增的 schema 变化通过 `ForSchema(n)` 登记。
- **规则**（决策 §2）：
  - 实体化旧默认值必须有正面的旧版证据，“没有印记”不算；
  - 动态默认值要 `forceSave`；
  - 标签首次出现时等于字段名，此后冻结；
  - 共享元素中的组件标签用 `mugirl` 前缀（`mugirlMilk…`、`mugirlMount…`），这一条优先于 design §3.4 的 `milk…`；
  - Scribe_Defs 位置用 `DefReference`，值上下文与枚举位置填写**旧**标签。
- **推迟项**：`applied_to_manifest.py` 输出中的 `pendingWaveS` 行，以及 `specs/deferred-S.json` 中属于本模块的项（keyedRenames、keyedDeletes、contentFileRenames、transitionalTypes），要与删除对应类型或 Def 的改动放在同一次改动中。
- **ratchet**：`Baselines/save-upgrade-ratchet.<module>.txt` 要清零；文件头说明“文件消失即模块结束”。`unassigned`（11 行）与 `moogirl`（2 行）在 S 开始前由集成者分配给模块。
- **行为**：只允许计划中声明的“有意改变/缺陷修复”，每一项都写进模块的记录。[a4-patch-order §6](wave-a/a4-patch-order.md) 列出的过时补丁顺序注释由所属模块改写。
- **结束条件**：对抗审查，然后修复；闸门 G1–G3、G6（本模块 ratchet 清零）、G8；部署后跑 G7；可用时再跑相关夹具上的 upgrade 套件。

### 4.2 模块任务

每个模块的条目编号沿用其计划。S1 是旧代码清单与替代方式，S3 是新存档 schema，S4 是对其他 owner 的请求，S5 是待决问题。

| 模块 | 计划 | MUST | SHOULD | 升级义务（S1/S3） |
|---|---|---|---|---|
| core | [core-plan](module-plans/core-plan.md) | S2-1 生命周期中枢与每局注册（A1 已实现，契约归 core）；S2-2 Mod 设置：`MugirlSettings` 写入 `settingsSchema = SaveSchema.SettingsCurrent`（forceSave），登记设置行；S2-3 用扩展点取代 Compatibility 的自补丁（与 combat S2-1 配合；C07 以 combat 为准）；S2-4、S2-5 在 R2 已完成，S 只核对；S2-6 共享辅助类；S2-7 死代码；S2-11 注释 | S2-8 Rendering 整理；S2-9 可见性；S2-10 外部胸部 Hediff | L1–L3：`Compatibility/Savegames/*` 中的迁移代码移入升级步骤；各功能的重置改为 `[GameLifecycleHook]`；`GameComponent_GhoulRenderingRefresh` 的处理见 incidents S2-12 |
| incidents | [incidents-plan](module-plans/incidents-plan.md) | S2-1 剧情组件改为嵌套子状态加绝对 tick；S2-2 Migration 记录加绝对 tick；S2-3 Punisher 记录（原为三个并行列表）；S2-4 合并事件 Pawn 标签工具；S2-5 野生雪牛娘与共享辅助类；S2-6 删除旧内容与仅供开发的内容；S2-7 逃跑农场完成判定不再保存 tick 计数；S2-10 草食政策只创建一次；S2-11 生命周期职责移出剧情组件；S2-13 DefOf 收尾；S2-14 整理 | S2-8 拆分 FusionInvestmentUtility；S2-9 信使日记组件；S2-15 内容整理；S2-16 跨模块字符串契约 | A01–A22 与 X1–X3：设置键 `enableStructuralCrashEvent` → `enableOpeningStoryline`；ThinkTreeDef `MugirlPunisher` → `Mugirl_Punisher`；并行列表要声明为并行组 |
| corporate | [corporate-plan](module-plans/corporate-plan.md) | 原则：凡改动存档 schema 的都是 MUST，schema 1 一次定型。S2-1 `CorporateNetwork` 聚合与按领域划分的状态对象；S2-2 历史记录不保存键或译文；S2-3 按种类划分的 Mission 子类；S2-4 贷款金额改为定点 `long`；S2-5 `CorporateIntroduction` 状态枚举；S2-6 交易与人员记录；S2-7 支援 Lord 状态图；S2-8 缺陷修复：融合地点生成失败时立即重开邀请；S2-9 热路径组件查找；S2-10 合并为一个 `CorporateDefOf`；S2-11 可见性 | S2-12 至 S2-18 | DOM 步骤 C1–C7、post-load 步骤 P2/P3/P5（S1“DOM steps”“Post-load steps”）；A1–A23 |
| milk | [milk-plan](module-plans/milk-plan.md) | S2-1 产奶组件重新设计；S2-2 不保存 tick 计数（删除 `pendingResourceTicks`）；S2-3 合并采集 JobDriver 与 WorkGiver；S2-4 灌篮的旧路径与状态；S2-5 删除 `GameComponent_XenotypeRepair`（改为 post-load 的 `XenotypeRepairPostLoad`），并移出泌乳补丁；S2-9 按 §3.4 改标签；S2-10 `ThrowController` 的默认值（实体化旧默认值）；S2-11 每局重置交给生命周期中枢；S2-12、S2-13 已在 R/R2 完成，S 只核对；S2-14 可见性与 sealed | S2-6 后发重设计；S2-7、S2-8；S2-15 至 S2-19 | L1–L15、K1–K4、G1–G5；先读计划 §0“改变盘点结论的已核实事实” |
| restraints | [restraints-plan](module-plans/restraints-plan.md) | S2.1 `SlaveApparelDef` → `RestraintExtension`，同时在 `PublicApi/` 保留别名 Def 子类 `Mugirl.SlaveApparelDef : ThingDef`，保留旧字段名并在 ResolveReferences 中转换（D5/D6）；S2.2 删除死代码与死内容；S2.3 洗脑延迟触发 Hediff 组件的最终标签；S2.4 删除 `MapRopingIndex`；S2.5 改用原版 `ThinkNode_ConditionalRoped`/`JobGiver_WanderRoped`；S2.6(a) 束具锁状态；S2.8 组件与 owner 标签整理；S2.11 功能 DefOf；S2.12 补丁（束具锁保持 `Priority.Last`，C08）；S2.13 可见性与文件布局 | S2.6(b)、S2.7、S2.9、S2.10、S2.14–S2.17 | L1–L18；ratchet 中必需行最多（11 行） |
| combat | [combat-plan](module-plans/combat-plan.md) | S2-1 Hook API 取代 Compatibility 自补丁；S2-2 拆分 `CompWeaponWheel`；S2-3 轮盘槽位记录，不用并行列表；S2-4 `CompMount` 的 schema 与 `MountTurret`；S2-6 每局状态交给中枢；S2-7 骑枪整理；S2-8 补丁类：计划原文说“不加多余的优先级”，已被 C08/F6 推翻，必须保留 A4 的显式优先级（golden 为 `Baselines/patch-order.txt`，见 [a4-patch-order §6](wave-a/a4-patch-order.md)），计划文本尚未修订；S2-9 功能 DefOf 与死内容；S2-10 武器内容；S2-14 去掉“Moo”，成员改名 | S2-5、S2-11、S2-12、S2-13、S2-15 | M1–M11、L1–L8、W1–W6。`MountedPawnUtility.IsMugirl` 是冻结的公开 API，必须 `public static`、NoInlining、仍是骑乘代码实际调用的判断点；`HasMountedPawn`/`MountedPawn` 改名会影响 WhiteFluff 的验证代码（`specs/owner-migration-list.md`） |
| content | [content-plan](module-plans/content-plan.md) | S2-1 拆散 `JobDefs.xml`（R2 已拆，S 核对）；S2-3 拆分 `Misc_Gameplay.xml`（R2 已拆）并删除死键，包括 A3 暂时移到前缀文件的 19 个；S2-4 种族角的 addon 条件（已核实的缺陷）；S2-5 种族 Def 中重复的 stat 节点；S2-10 内容命名与放置规则写入验证器（由 A2/V owner 实现） | S2-2、S2-6 至 S2-9 | L1–L12。L1–L4 的 MooGirl 映射作废，改为 D3：只识别与剥离，并给玩家一次提示；标记表 `specs/moogirl-era-markers.json` 通过 `MooGirlTypePrefix`/`MooGirlDefPattern` 接入。race-wide 文件归 content（C11） |

## 5. Wave V：验证

### 5.1 输入与归属

- 输入：post-S 构建、全部夹具、owner 的真实存档、语料。
- 归属：集成者，外加唯一的游戏内执行者。开始前在 4979（或当时的游戏版本）上再核对一次引擎事实；游戏版本若有变化，要重新生成 P 夹具。

### 5.2 离线语料与 offline-report

```powershell
pwsh tools/Validate.ps1 -WorkRoot $W
& $V --mod $Dll --assume-active all --save-upgrade strict --report "$W\strict.json"       # 不允许任何未处置的必需行
& $V --save-corpus "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves" --mod $Dll --artifacts "$W\corpus-owner"   # 只读
& $V --save-corpus DevData\Evidence\SaveFixtures --mod $Dll --artifacts "$W\corpus-fixtures"
& $V --save-corpus DevData\Evidence\CorporateValidation-AllDlc6\Saves --mod $Dll --artifacts "$W\corpus-allDlc6"
Get-ChildItem "$W\corpus-fixtures\*.rws.gz.upgrade.json" | ForEach-Object { $id = $_.Name -replace '\.rws\.gz\.upgrade\.json$'; Copy-Item $_.FullName "DevData\Evidence\SaveFixtures\$id\offline-report.json" }
```

`--save-corpus` 要求每个存档：状态已知且不是 Failed；第二次运行 0 个动作；每个存档不超过 2000 ms（决策 §2：67 MB 的 S7 也要在 2 s 内）。owner 的 S1–S4 必须被识别为 MooGirl 时代，并按 D3 剥离。

### 5.3 游戏内断言

命令：`pwsh tools/Test-InGame.ps1 -BuildMod -Deploy -Fixture all -SuiteOption upgrade.requireOffline=true`。需要时再用 `-Language English` 跑一遍。断言依据 critique-validation §4.3、决策 §2/§5 和 `Suites/Mod/UpgradeSuite.cs` 的类注释。

- **第 1 次启动（读入旧档夹具）：**
  - 读档时，任何带 Mugirl 记号或致命模式的 ERROR 都算失败；
  - 印记等于 `SaveSchema.Current`，诊断中每一步的动作数与 `offline-report.json` 一致；
  - 存档旁生成 `<存档>.rws.mugirl-schema0.bak`，其 SHA-256 等于夹具；
  - Mod 的 DefInjected 键全部注入；
  - 夹具清单中的事实经映射后等于重存档中的事实；
  - 重存档中 Mod 拥有的位置没有旧名；
  - 夹具与重存档的 diff 中，每一处差异都能由映射、步骤声明的消费或策略解释；
  - 运行 2500 tick，没有 Mod 错误，播种的雪牛娘没有消失。
- **X-settings**：设置文件被改写成新类名和新键，只写一次，没有出现“Could not find class”。
- **X-alias**：依赖 Mod 的 Def 解析到新类型，旧 XML 字段名有效。
- **第 2 次启动（新进程读入重存档）与对照组**：升级器不动作（0 个动作，不做备份）；diff 为空，原版读档时的重置除外。
- **X-newer**：印记为 `Current+1` 的存档不被改写、不做备份，恰好一条警告，再存档时保留原印记。
- **夹具范围**：P-1004、P-0927、P-0920、P-0.5.0、P-0.4.7、X-alias、X-settings，外加 control。

  A5 列出的覆盖缺口要补上，或者明确记为不适用：CoreOnly 组合、Lord、Corporate 状态的深度。

### 5.4 修复循环与结束条件

- 每个失败都交给所属模块修复，修复后从 G1 开始重跑。结束条件：
  - G1–G3、G6（strict 通过）、G7、G8 全部满足；
  - §5.2 与 §5.3 全部通过。
- 证据存档到 `DevData/Evidence/WaveV-<日期>/`，并写一份波次记录。

## 6. Wave D：文档与发布

交付物：

- `About/About.xml`：`supportedVersions` 加入 1.7，`A.about` 由此通过。1.6 的冻结目录和 `LoadFolders.xml` 的 v1.6 列表保持不变。
- 根 README：
  - 构建与部署策略（K3：普通构建不部署；部署命令；dotnet 从 `Source/` 启动）；
  - `Validate.ps1 -WorkRoot`；
  - 游戏内运行锁；
  - `Test-InGame.ps1` 的 `-Fixture`、`-TargetModDir`、`-TokenSet`、`-SuiteOption`；
  - `New-SaveFixture.ps1` 与夹具目录。

  同步更新 [验证手册](../validation-runbook.md)。
- ADR：[ADR-007](../architecture/ADR-007-save-schema-upgrade.md)、[ADR-008](../architecture/ADR-008-public-api.md)、[ADR-009](../architecture/ADR-009-naming-and-layout.md) 写于 2026-10-15 收尾时，记录的是目标状态。Wave D 要按实际结果更新它们的状态说明，包括 `PublicApi/` 是否已建立、各模块的升级表与步骤，以及 R/R2 之后的名字。ADR-004 已被 ADR-009 取代，ADR-005 的规则 2 已被 ADR-007 取代，D 波只需核对。
- 功能文档中的名字改为新名；每个波次的记录写在 `docs/records/`；`docs/tools` 中只适用于 1.6 的说明标为退役。
- 迁移知识库 `..\1.7迁移文档`：写入 [engine-hooks §12](inventory/engine-hooks.md) 和各报告中值得保留的发现（design §0 第 5 条）。
- 依赖 Mod：把 `tools/RenameApply/specs/owner-migration-list.md` 交给 owner，用于更新 WhiteFluff/Subspecies 的验证代码，以及准备 1.7 版 WhiteFluff 的引用。
- 发布：只有 V 完成且 owner 批准后才能发布。按 [发布检查清单](../release-checklist.md) 执行：
  - `pwsh tools/New-ReleasePackage.ps1 -Version <版本> -Archive`
  - `pwsh tools/Test-ReleasePackage.ps1 -Package <暂存目录>`
  - `pwsh tools/Test-Docs.ps1`
