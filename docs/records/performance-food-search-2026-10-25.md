# 雪牛娘觅食植物扫描性能调查（2026-10-25）

owner 在 1.7.4985 上用 Dubs Performance Analyzer 1.7.21（DPA）观察到“物体 Tick / Tick Def”中 Mugirl 排在首位，要求检查性能异常，并提示可以借助 1.7 内置的性能分析器。本记录保存调查方法、根因、修复和验证结果。

审阅结论后，owner 要求部署并同步 Git。修复已按[决策 §18](../refactor-1.7/decisions.md#18-觅食性能修复的部署与-git-同步2026-10-25)正式部署，DLL 为 `C3ED6AA4…`，部署后的检查见[部署与提交](#部署与提交2026-10-25)。下文“修复”“验证”两节记录的是部署前的源码修复与候选构建测试。

## 现象

截图中 Mugirl 每 tick 平均 0.147 ms，占物体 Tick 的 16.01%；每次调用 18.4 µs，每 tick 8 次；2000 tick 中有 3 个尖峰，单 tick 最高 18.237 ms。

- 同屏人类每次调用 12.7 µs，其他 HAR 种族为 18–32 µs/次。Mugirl 排第一主要是同屏数量多，单次均值不算异常。
- DPA 的尖峰判定是：该组一个 tick 的合计耗时超过 1 ms，且超过中位数的 5 倍。18 ms 的尖峰才是异常。
- 截图那一局的 Player.log 已被其他项目的测试覆盖。结论来自代码阅读与隔离实测，不来自该局日志。

## 方法

新增游戏内测量套件 `perf`（`Source/Tests/MugirlRace.GameValidation/Suites/PerfSuite.cs`），只按名运行，不进 default/all。它在 quicktest 的 250×250 地图上运行：

- 生成 4 名雪牛娘与 4 名人类殖民者，并触发一次雪牛娘迁徙；
- 地图植物不足时补种野草；
- 用法见[验证手册](../validation-runbook.md)。

四个步骤：

1. **觅食计时**：按 `JobGiver_GetFood` 的参数调用原版觅食。同时用 Harmony 反向补丁取得未补丁的原版 `FoodUtility.BestFoodSourceOnMap`，比较选中的食物与耗时。这是套件唯一的通过/失败判定。
2. **逐 tick 开销**：秒表探针统计地图上 Pawn 顶层 `Thing.DoTick`，口径同 DPA 的 Tick Def。
3. **内置分析器**：用游戏的 Waylith `FrameProfiler` 插桩 `MugirlRace!*` 与原版 `Verse.*`、`RimWorld.*`，约 2.5 万个方法。游戏保持暂停，每帧单步一个 tick；按种族给 DoTick 套区段，导出调用路径的耗时、调用次数、分配和最慢的 tick。
4. **候选构建**：在 `%TEMP%` 构建，布置为带标记的临时模组 `Mods/__MugirlLegacy_perfcand_*`（packageId `mugirl.legacy.perfcand`），用 `Test-InGame.ps1 -TargetModDir` 运行同一套件。正式 `1.7/Assemblies` 未写入。

关于内置分析器的发现：

- 原版 `ProfilerBlock` 在发布版是空实现。内置分析器默认也不插桩，所以不开插桩只能看到引擎区段，看不到原版调用路径。
- 高速运行时一帧包含多个 tick，全量插桩的区段事件会超出分析器的事件缓冲，绝大多数帧被丢弃。首轮只留下 3 帧，改为单步后每帧对应一个 tick。
- 插桩期间，打在原版 `Thing.DoTick` 上的 Harmony 探针仍然计数，说明本次插桩没有绕过已有的 Harmony 补丁。
- 插桩会抬高绝对耗时，只用于比较构成；实际开销以不插桩的秒表数字为准。

## 根因

雪牛娘种族的 `foodType` 是 `OmnivoreHuman, VegetarianRoughAnimal`。其中 `VegetarianRoughAnimal`（0x1F51）含 `Plant`，用于吃草。

原版 `FoodUtility.BestFoodSourceOnMap` 处理“食性含植物”的进食者时，候选组从 `FoodSourceNotPlantOrTree` 换成 `FoodSource`，后者包含全图每一株植物。对人形取食者，原版用 `SpawnedFoodSearchInnerScan` 遍历整组：对每个候选先计算 `FoodOptimality`，再做可口度等校验。`FoodOptimality` 内含 `ThoughtsFromIngesting`、Ideology 的食物种类判断，`GetFoodKind`、`IsProcessedFood`、`IsAnimalProduct` 每次调用都会分配内存。人类的食性不含植物，同一地图上只扫描十几个非植物食物源。

未到紧急饥饿时，原版要求的最低可口度是难吃的餐食（`MealAwful`）。植物最高只是 `RawBad`，必然被拒，但仍会被逐株评分；找不到可接受的饭菜时，例如没有饭或使用“草”食物方案，每次思考（每个 Job 结束）都会整图重扫一遍。从紧急饥饿起植物才是合法候选，雪牛娘这时才会去吃草。

内置分析器的归因：

- 部署版在 1500 个单步 tick 中，雪牛娘殖民者最慢的三个 tick 是 100 ms、96 ms、80 ms（沙漠图，补种到 15000 株）。最终基线中是 111 ms、105 ms、92 ms（温带森林 27011 株）。
- 这些耗时几乎全部位于 `JobGiver_GetFood → TryFindBestFoodSourceFor → BestFoodSourceOnMap → SpawnedFoodSearchInnerScan → FoodOptimality`。
- 同期分配 30.1 MiB（最终基线保留的帧内为 52.4 MiB），人类同期约 1.1 MiB。其中一个慢 tick 内还发生了 12.8 ms 的 GC 暂停。
- 除觅食外，雪牛娘的组件、轮盘、产奶等逐 tick 开销与人类同一量级，不是这次的异常来源。

## 修复

新增 `Features/Grazing/Harmony_FoodUtility_BestFoodSourceOnMap_PlantScan.cs` 与纯规则类 `PlantFoodSearchRules.cs`：

- 前缀以 `Priority.Last` 运行，只作用于雪牛娘进食者（`MugirlIdentity.IsMugirlPawn`）且 `allowPlant` 为 true 的调用。
- 按原版同样的规则计算最低可口度：显式下限、非人形或野人、濒死饥饿、紧急饥饿、“不介意生食”基因。若它高于“食性只含植物”的食物 Def 的最高可口度，就把 `allowPlant` 改为 false，原版随之改用不含植物的候选组。
- 最高可口度用原版 `ThingRequestGroup.FoodSource` 与 `FoodSourceNotPlantOrTree` 的 `Includes` 之差推导，进程级缓存。本机全部 Def 为 `RawBad`。

**等价性**：原版校验的第一项就是可口度不低于最低可口度，被跳过的候选在原版中必然被拒。它们也不会影响其余候选的比较，选中的食物与原版完全相同。

**不改变的情形**：紧急饥饿、濒死饥饿和野生状态下，植物是合法候选，搜索仍是原版的整图评分。这时一次觅食在这些地图上约 6–17 ms，每次开始吃草时发生一次。之后的连续啃食由既有的 `Harmony_JobDriver_Cleanup_GrazeChain` 在 30 格内查找，开销很小。若要进一步降低，需要改写原版搜索本身，本轮未做。

## 验证

离线检查结果：

- `Validate.ps1 -UpdateBaselines`：两个运行时各 45/45，结果一致；不带该参数复跑，结果相同。
- `Test-Docs.ps1`：只有 2 个既有失败，即 README 与 ThinkTree 记录链接的 `雪牛娘1017-兼容修复.zip` 不存在，与 10-22 记录相同。
- `patch-targets.txt` 只增加新补丁一行，`patch-order.txt` 不变。
- 单元测试 1090 项，0 失败，2 项显式未运行。新增 9 项：最低可口度与原版逐项一致（穷举全部输入组合），以及跳过判定的边界。
- ShaderCheck 通过。

游戏内对照。quicktest 每次随机生成地图，觅食耗时以同一局内的原版副本为准；跨局的 tick 数字只作参考。部署版两局：

| 场景 | 部署版 R4（森林 27011 株） | 部署版 R2（沙漠，补到 15000 株） |
| --- | --- | --- |
| 饥饿、无饭：一次觅食 | 28.0 ms | 22.6 ms |
| 饥饿、有饭（找到简易套餐） | 11.8 ms | 13.5 ms |
| 紧急饥饿（吃草） | 6.4 ms | 8.8 ms |
| 人类饥饿 | 0.016 ms | 0.017 ms |

候选版两局，每格为“候选 / 同局未补丁原版”：

| 场景 | 候选 R3（森林 26460 株） | 候选 R5（苔原，补到 15000 株） |
| --- | --- | --- |
| 饥饿、无饭 | 0.018 / 31.4 ms | 0.096 / 26.5 ms |
| 饥饿、“草”方案 | 0.013 / 31.5 ms | 0.015 / 17.9 ms |
| 饥饿、有饭 | 0.034 / 13.5 ms | 0.054 / 15.9 ms |
| 紧急饥饿 / 濒死饥饿 | 16.9 / 17.7 ms，16.9 / 17.5 ms | 9.0 / 8.8 ms，9.4 / 9.7 ms |
| 野生（迁徙） | 6.3 / 6.2 ms | 9.4 / 9.5 ms |

- 两局候选版的全部场景（含有饭、濒死、野生与人类），选中的食物都与未补丁原版相同。
- 逐 tick 开销取最终两局 R4 与 R5，都是无饭、预热 60 tick 后计量 2500 tick：

| 指标 | 部署版 R4 | 候选 R5 |
| --- | --- | --- |
| 雪牛娘殖民者平均每次 DoTick | 51.6 µs | 25.8 µs |
| 每 tick 合计的 p99 | 10.6 ms | 0.94 ms |
| 尖峰 tick 数 | 33 | 22 |
| 人类殖民者平均每次 DoTick | 15.3 µs | 17.3 µs |
| 内置分析器：雪牛娘最慢 tick | 111 / 105 / 92 ms | 4.7 / 4.5 / 3.0 ms |
| 内置分析器：雪牛娘的分配 | 52.4 MiB（保留的 791 帧） | 0.59 MiB（1500 帧） |

候选版中雪牛娘最慢的 tick 来自原版的 Ideology 风格统计、社交互动等，与人类的构成相同。

## 边界

- 候选构建是当前完整源码，还包含 2026-10-22 已写好、尚未部署的优化（骑乘逐 tick 推进、轮盘空闲去重等）。觅食对照在同一局内与原版副本比较，只反映本次修复；逐 tick 数字跨局，还受地图和这些改动影响。
- 测试环境只加载 Harmony、HAR、雪牛娘与测试模组，没有复现 owner 的完整模组列表与存档。其他模组若改写原版觅食的可口度校验，等价性需要另行确认；本补丁只在其他前缀之后读取参数。
- 秒表阶段每组最大值仍可能有十几毫秒，部署版与候选版、人类与雪牛娘都如此。它们来自原版工作搜索、社交互动、GC 或首次编译，不是本次问题。
- 内置分析器的历史帧数受内存限制。部署版那一局只保留了窗口内 1500 帧中的 791 帧，分配总量按保留的帧统计。
- 本轮没有打包发布或上传创意工坊。

## 部署与提交（2026-10-25）

按[决策 §18](../refactor-1.7/decisions.md#18-觅食性能修复的部署与-git-同步2026-10-25)部署当前完整源码。除本修复外，还包含已于 10-17 部署但未提交的 4981 兼容修复，以及此前未部署的巨企供货修复（10-19）与代码质量优化（10-22）。

- 构建与部署：`Test-InGame.ps1 -BuildMod -Deploy` 在 `%TEMP%` 源码副本中构建 Release，由工程部署步骤写入 `1.7/Assemblies`。
  - DLL：`C3ED6AA47454470F4EC61C2DDCAE114BA6ACEB97A2330C16FE4904003507DF5A`；
  - PDB：`BBBC0C491CAC0C5E352A81B8B51D4B8EC00F6EF82B1546B0F60F697146ED98BE`；
  - 上一正式构建 `1BD2FC94…` 的 DLL/PDB 已备份在 `DevData/Backups/PerfInvestigation-20261025/deployed-before/`。
- 部署后的检查：

| 检查 | 结果 |
| --- | --- |
| `Validate.ps1 -Shipped` | 两个运行时各 45/45，结果一致；单测 1090 项无失败（2 项显式未运行）；Shader 通过 |
| strict 存档升级（两个运行器，`--assume-active all`） | 均通过，必需行无未处置 |
| 正式 DLL 对 5 个历史夹具的离线升级（`--save-corpus`） | 全部 Upgraded，29 步，动作数 322/850/1188/1338/1312，与 10-22 一致 |
| 启动冒烟 `SmokeTest.ps1 -QuickTest -DevMode` | 通过，雪牛娘未新增错误或警告 |
| 默认实机套件 | 79 成功、0 失败、4 跳过；跳过项是未启用的 MeleeAnimation/YayoCombat 可选兼容 |
| `perf`（沙漠图，补种到 15000 株） | 饥饿雪牛娘 `BestFoodSourceOnMap` 0.021 ms，同局原版 13.2 ms；“草”方案 0.014 / 12.7 ms；有饭 0.023 / 14.1 ms；紧急饥饿 7.1 / 7.0 ms。所有场景与原版相同 |
| `corporate-supply` | 20 成功、0 失败、0 跳过 |
| 旧档夹具实机矩阵（`-Fixture all`，夹具副本加本轮重新生成的离线报告） | 5 个 P、X-alias、X-settings 各两次启动，合计 562/0/19，与 10-16 的完整矩阵相同 |
| 控制组 | 147/2/3。2 个失败是 `control-seed` 中既有的缺眼角色穿束具报错（`RestraintApparel.Notify_Equipped`，该文件本次未改）；两次读回无差异，本次没有出现以往的污渍叠格差异 |

- 夹具副本的 5 份存档和 7 份清单在测试前后哈希不变，原夹具目录未改写。
- 本轮没有打包发布或上传创意工坊。10-22 记录中 Q1、Q2、Q5 的手动实机项未逐项核对。
- 随后按 §18 提交当前源码、测试、文档与新 DLL/PDB，并按 `main` → `origin/main` 正常推送。

## 证据

`DevData/Evidence/PerfFoodSearch-20261025/`：

- `runs/`：五次游戏内运行的报告、`perf.json`、标准输出与配置。`think-trees.json` 按[压缩约定](workspace-cleanup-2026-09-21.md#2026-10-20-历史验证资料压缩)存为 `.gz`，已登记到 `compression-manifest.jsonl`。
- `logs/`：离线验证、候选构建与各次运行的日志。
- `scripts/`：候选构建布置与归档脚本。
- `dpa-screenshot-owner.png`：owner 的 DPA 截图。
- `deploy/`：部署后的检查。
  - `runs/` 是默认套件、`perf`、`corporate-supply` 与夹具矩阵的实机结果，各次的 `think-trees.json` 已压缩登记；
  - `smoke/` 是启动冒烟；
  - `offline/` 是 `-Shipped` 验证、strict 报告、夹具离线报告与输入哈希。
  - 夹具启动导出的存档对照文件（`canonical-*.rws` 等）未压缩，约 0.9 GB。
- `SHA256SUMS.txt`：目录内全部文件的校验和。

五次运行分别是：

| 运行 | DLL | 地图 | 说明 |
| --- | --- | --- | --- |
| R1 `20261025-001401` | 部署版 | 森林 21832 株 | 早期套件：无原版对照、tick 阶段无预热；高速插桩丢帧 |
| R2 `20261025-002031` | 部署版 | 沙漠，补到 15000 株 | tick 阶段地图上有饭 |
| R3 `20261025-002703` | 候选 | 森林 26460 株 | tick 阶段地图上有饭 |
| R4 `20261025-003029` | 部署版 | 森林 27011 株 | 最终对照：无饭、预热 |
| R5 `20261025-003134` | 候选 | 苔原，补到 15000 株 | 最终对照：无饭、预热 |
