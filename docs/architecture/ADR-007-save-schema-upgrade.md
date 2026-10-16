# ADR-007: 存档结构版本与统一升级层

状态：Accepted（2026-10-15）；2026-10-16已实现并完成V验收与本地交付。取代[ADR-005](ADR-005-version-layout.md)规则2。R/R2名称映射及S结构升级均已登记；实际构建、实机范围和证据见[完成记录](../records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)。

## Decision

1. `Mugirl.Core.GameComponent_Mugirl` 保存整数版本戳 `mugirlSaveSchema`。没有戳且 `meta/modIds` 含 `har.mugirlrace`（或 `_steam` 形式）的存档为 schema 0；`SaveSchema.Current = 1`。
2. 唯一升级层 `Mugirl.SaveUpgrade` 单向升级旧档。仅在整次读档升级和安装检查成功后，下一次正常保存才写当前版本；此后正常读取。
3. 旧类型名、Def 名、标签与旧形状转换只在 `Source/MugirlRace/SaveUpgrade/` 中维护。领域代码不保留旧档分支；[ADR-008](ADR-008-public-api.md) 的永久外部 API 别名是另一种契约，不能删除。
4. 后续结构版本通过 `UpgradeModuleBuilder.ForSchema(n)` 登记，并递增 `Current`。标签、枚举、加载 ID、Toil 序列、Lord 图及 DefMap 下标含义都属于保存协议。
5. [决策 §7](../refactor-1.7/decisions.md#7-owner-补充约束2026-10-15恢复重构后优先于所有既有计划)要求保留文案、行为与可恢复状态。不能把结束所有旧工作、清空状态图或重置计时作为成功迁移；证据不能唯一决定续接方式时，明确失败、保留原文件与旧版本戳。

## Context

- 读档前整个 XML DOM 已在内存中。游戏版本与 Mod 列表相同时，原版兼容转换可能跳过；存档也没有逐 Mod 版本号。不能依赖 `gameVersion` 推断历史模组布局。
- 已发布 1.6 构建曾通过回退目录在 1.7 上运行，因此 1.7 游戏保存的文件也可能是 schema 0。
- 1.7.4979 rev109 的相关入口已直接复核，见[引擎入口](../refactor-1.7/inventory/engine-hooks.md)及[迁移知识库核验记录](../../../1.7迁移文档/资料来源与核验记录.md) E57–E59。历史 4977 证据保留供追溯，不把它当作当前程序集的核验结果。
- Mugirl 时代发布构建是升级目标；MooGirl 时代（≤0.4.4）不支持，只作明确识别、限定清理与一次提示。历史发布 DLL 的在途工作审计见 `DevData/Evidence/Resume-PreR-20261015/InFlightAudit/`。

## 实现（2026-10-16 代码现状）

### 启动

`MugirlMod` 构造期依次执行 `SaveUpgradeBootstrap.Initialize(this)`、`GetSettings`、`HarmonyBootstrap.ApplyAtStartup`、`CombatCompatibilityBootstrap.Register()`。设置升级先于读取；安装检查通过加载长事件排到补丁应用之后。

`SaveUpgradeInstallation.Verify` 用 `Harmony.GetPatchInfo` 检查必需钩子的 owner 为 `MugirlMod.Mod`。缺少必需入口或 ThinkTree 兼容账本建立失败时状态为 `Broken`，读入存档不能获得当前版本戳。可选的新游戏生命周期和删除 Def 静默处理入口缺失只报告降级。

### 钩子（`SaveUpgrade/Hooks/`，每类一个文件）

| 目标 | 用途 |
|---|---|
| `SavedGameLoaderNow.LoadGameFromSaveFileNow` Prefix | 捕获文件名；这是强制备份的必需入口 |
| `Game.LoadGame` First Prefix / Postfix / Finalizer | 建立会话并运行 DOM；阶段 C 完成诊断；退出或异常时清理会话，不吞原版异常 |
| `ScribeLoader.FinalizeLoading` Postfix | 阶段 A：引用解析与 PostLoadInit 之后 |
| `Game.FinalizeInit` Prefix | 阶段 B：运行明确的工作/Lord 策略及模块步骤 |
| `BackCompatibility.BackCompatibleDefName` Last Postfix | 按数据库根处理 Def 改名 |
| `BackCompatibility.WasDefRemoved` Postfix | 已明确删除的 Def 残留引用由原版读为空时不重复报缺失错误 |
| `LoadIDsWantedBank.RegisterLoadIDReadFromXml` / `RegisterLoadIDListReadFromXml` Prefix | 在真实 Scribe 引用登记边界重映射身份 |
| `Verb.ExposeData` Postfix | 保持已保存 Verb 身份与 Thing 改名一致，保留 burst 等状态 |
| `ThinkTreeKeyAssigner`、`ThinkNode.DeepCopy(bool)`、`ThinkTreeDef` 与 `Job.ExposeData` 的身份钩子 | 记录原版展开/复制的真实节点关系，建立历史布局账本，在本次旧档 Job 读取范围解析旧思考节点键，不删除 lastJobGiverKey |
| `Game.InitNewGame` Prefix | 发出 `BeforeNewGame` 生命周期事件 |

存档类型改名由 DOM 完成；没有全局 `GenTypes` 别名补丁。外部 XML 公开类由 PublicApi 保留。新增侧文档根类型（ScenPart、Precept 等）的改名时，必须单独证明它的读取入口，不能假定主存档 DOM 覆盖所有文件。

### 会话与识别

`SaveUpgradeSession.Start` 从 DOM 的 `meta` 和 `game/components/li/mugirlSaveSchema` 识别，不依赖易被嵌套文件覆盖的 ScribeMetaHeaderUtility 静态状态。

| 状态 | 处理 |
|---|---|
| `NoMugirl` | 模组新加入已有存档；不改旧数据，完成后发出 AddedToExistingGame |
| `NotLegacy` | 当前 schema；不重复升级 |
| `Upgraded` | 旧版本的适用步骤全部完成 |
| `MooGirlEra` | 检测到明确旧时代证据，执行限定清理后提示 |
| `Newer` | 来自更高版本；不修改，提示保存可能丢失新版数据 |
| `Failed` | 识别、备份、安装或步骤失败；保留旧戳并提示不要覆盖原档 |

无法解析版本戳时在 `Framework.Detect` 失败。引用别名还要求实际 bank、LoadingVars 与主存档 XmlDocument 身份匹配；嵌套读取其他文件、没有会话、更新 schema 或失败后的会话不会继续套用别名。

历史 ThinkTree 布局用五个已发布构建的实际树快照逐节点校准。早期发布缺少后加的巨企 Duty，原版键碰撞会影响其他树，不能仅凭最终节点路径或存档游戏版本猜键。`LegacyThinkJobReadSet` 收集本次实际读取的完整 Job 集合，在首次 PostLoadInit 冻结后求共同布局；不同布局允许留下候选，但每个实际键必须指向同一个当前节点。空交集、晚捕获、未知键、歧义或最终 lookup 对象不符均失败，不改思考树，不消耗随机数。

### DOM 流水线（`UpgradePipeline`，主存档读取前）

1. 一次完整遍历建立 `SaveIndex`，收集类型、Thing、Hediff、账单、Lord 和登记标签。
2. 框架处理 MooGirl 证据、类型、Def 删除、明确 Lord 策略、标签/枚举/值和有历史证据的默认值。
3. 按模块 order 与登记顺序运行结构步骤。
4. `Framework.LoadIds` 改写真实对象定义 ID 和显式身份上下文，发布本次读取使用的不可变引用映射。

每个步骤先只读 `Plan`，验证完再应用 `DomEditor` 原语。失败即停止后续升级；原磁盘文件没有被原位改写。已发生的内存步骤不等于可以覆盖原档，恢复应从保留的原文件或 `.bak` 重新开始。

**禁止全 XML 叶子字符串替换。** 玩家 nick、label、笔记或字典文字即使等于 `Thing_<旧Def><编号>` 也不能改动。Def 值只在明确数据库根和 owner 的上下文处理；原版 ThingFilter 的两个集合通过 `TypedDefReferenceScope` 证明 writer/路径，不能仅凭 `allowedDefs` 字段名认定类型。未知第三方同名结构保持原文，定向支持通过 ValueContext 登记。

真实 Reference 列表按需复制并保持长度、顺序和 null 位置。遇到退役对象作为未知引用字典的 key，只有已验证的原版 typed 调用链、主文档和等长 keys/values 都成立时才成对删除；不猜测任意 keys/values 结构。key 的 deep value 可能拥有外部引用对象而不能证明生命周期时明确失败，保留备份与旧戳，不静默丢对象，也不屏蔽日志。

### Def 改名

`DefNameResolver` 根据原始 defName、数据库根和当前结果是否可解析决定是否覆盖。`Scribe_Defs` 引用通过 `BackCompatibleDefName` 处理；该入口还服务思想体系、异种和剧本等侧文档。它不将普通文字当 Def。

DOM 的 Def 处理限定在真实对象定义、登记值上下文、明确删除对象和身份格式。隐含 Def 名依据实际引擎生成条件派生；不能为通过验证而捏造从未保存的字段。

### 读档后阶段

- 阶段 A：引用解析与 PostLoadInit 完成后。
- 阶段 B：Game.FinalizeInit 前。`InFlightJobSelection` 只选显式 `EndInFlight` 行；没有版本戳本身不触发结束任务。已证明同构的任务登记 Preserve，变化的布局由 Claim 步骤维护原状态。
- 阶段 C：全部 GameComponent.LoadedGame 之后，完成会话并把诊断交给中心组件，再发出 LoadedGame / AddedToExistingGame。

当前模块不把所有旧工作统一结束。骑枪旧两 Toil 步行流程用 SaveUpgrade 内的等布局续接驱动，保持 cur/next/want/ticks、目标和冷却，不重放 initAction。未开始且 count=-1、Thing 目标时无法区分旧步行与新起飞，明确失败；精确 count=1 或直线冲锋的有效纯地格目标有独立证据。详见[战斗实施记录](../records/refactor-1.7-combat-2026-10-15.md)。物品工作同样按保存状态证明阶段；真歧义不重置、不结束。Corporate 支援 Lord 保留图状态与触发计时。

模块 PostLoadUpgradeStep 只在旧档升级未失败时运行。首次升级后需要继续完成的旧动作，其最小续接状态留在 SaveUpgrade 中保存；正常新任务仍使用领域实现。

### 版本戳的真实性

中心组件 `GameComponent_Mugirl` 的构造函数无副作用。保存总是写戳；只有新游戏，或完成必要阶段且安装检查通过的读档会话才写 Current。其他情况写回来源戳并记录原因。默认 0 代表未核实，而非已经升级。

### 备份

旧档实际读入前必须建立 `<名称>.rws.mugirl-schema<N>.bak`，已有文件不覆盖。文件名未知、原文件不存在或备份失败时中止升级；不能只警告后继续。离线语料模式只在内存运行，不创建备份、不写 owner 存档。

### MooGirl 时代（owner 决定 D3）

按明确历史类型前缀及已发布 Def 精确名单识别，不靠包含 MooGirl 的任意字符串。删除已确认的旧组件和可移除的 typed Def 引用，其余由原版缺失 Def 行为处理，并提示一次。不建立 MooGirl→Mugirl 映射；相似的新名字、玩家文字、第三方未知 owner 不作为清理授权。

### 设置文件

`SettingsUpgrade` 在 GetSettings 前处理 `Config/Mod_<文件夹名>_MugirlMod.xml`，按独立 settingsSchema 升级，临时文件写好后替换；无改动不写盘。Core 已登记设置类改名和四个事件开关改名，保留原布尔值、界面顺序和默认值。`Mugirl.Core.MugirlSettings` 已 forceSave settingsSchema。MooGirlMod 设置文件不迁移。

### 模块登记

- 模块位于 `SaveUpgrade/Modules/<Module>/`，实现 ISaveUpgradeModule 并标注 SaveUpgradeModule；按显式 order 发现，不依赖反射枚举。
- 当前顺序为 Core 100、Incidents 200、Corporate 300、Milk 400、Restraints 500、Combat 600、Content 700。
- Register 只登记数据，不读取 Find/Current/DefDatabase。登记类型与 Def、标签/枚举/值、默认值证据、历史组件关系、容器、在途策略、设置和步骤。
- DomUpgradeStep / PostLoadUpgradeStep 声明实际消费的旧名；每个模块结构步骤有 `Source/Tests/MugirlRace.Tests/SaveUpgrade/Golden/<StepId>/{before,after}.xml`，只用合成或匿名节点。
- 表格构建集中检查冲突、非单射、无位置的枚举改名等问题；失败时整次读档报告 Framework.Tables，不缓存成可用空表。

### 契约与诊断

| 契约 | 内容 |
|---|---|
| K1 `SaveUpgradeManifest.ToJson()` | manifestVersion=2；含逐行 schema、值/枚举位置、容器、历史组件关系、步骤和策略，供验证器与实机框架读取 |
| K2 `SaveUpgradeDiagnostics.LastReportJson` | 会话、状态、动作数、耗时、失败步骤和备份路径 |
| `SaveUpgradeOffline.Run` | 同一识别与 DOM 流程的只读离线入口；不替代实际 Scribe / Toil / Lord 续接验证 |
| `ValidationReadTrace` | 仅带隔离验证开关与标记文件时启用；记录真实引用读取节点和路径，不作为正常游戏功能 |

中心组件的 lastUpgrade 保存最近成功升级记录。读档在线程中执行；提示通过 LongEventHandler 排到主线程。结构步骤与真实引用边界可能各自产生动作，离线 DOM 报告不能假定等于完整实机报告。

按 2026-10-16 的用户要求，成功升级的旧档发送一封 PositiveEvent 信件，说明自动升级完成及原档备份路径。发送条件是阶段完整结束、安装检查未失败、备份已建立且中心组件已接受当前版本戳；每个升级会话只能领取一次。回调只在原游戏仍为当前游戏时显示。保存继续使用强制写出的 `mugirlSaveSchema=1` 标记，再次载入新档为 NotLegacy，不创建备份、不新增成功信件。失败、较新存档和不支持的 MooGirl 时代不发送成功信件，保留各自原有提示。

## 业务代码的规则

- 旧名与旧形状转换只在本模块 SaveUpgrade 中；永久外部 API 见 ADR-008。
- 既有隐藏退役野人事件的 Def、文字与拒绝执行行为仍保留，worker 移至 SaveUpgrade。删除它会改变现有队列/历史的读取行为，原删内容计划因此不执行，见[Core/Content 记录](../records/refactor-1.7-core-content-2026-10-16.md)。
- 只有升级层访问 Scribe 读档内部。领域 `ExposeData` 的新状态组必须记录 LoadingVars 是否实际进入；缺组时不能在 CrossRefs/PostLoadInit 凭空读取未登记的引用。
- 保存计时器是否可省略取决于语义：未结算时长、相位、已发生动作等必须保留，不能把所有 tick 计数都称为缓存删除。
- `C.save-upgrade` 对照 published/observed 必需语料检查 Unchanged、Renamed、Consumed、Dropped 等处置；开发中 ratchet 不得新增债，最终 Wave V 使用 strict。
- 金样同时检查结果、幂等、对象身份与同形玩家文字不变。真实 Scribe 测试还须断言没有原版 Log.Error，不能接受解析失败后原版回退对象导致的假通过。
- `mugirlSaveSchema`、中心组件类名、lastUpgrade 及其子标签保持稳定。

## Consequences

- 升级单向；1.7 新保存不能交给冻结 1.6 读取。回退使用升级前文件或自动备份。
- 两版本共用设置文件时，1.6 不理解新设置类/标签并可能改写它；1.6 本身不再维护。
- R/R2/S 已有实质映射和结构步骤，不再是空升级表。模块离线通过不等于完整旧档实机验收。
- 当前交付已通过strict、两运行时、控制组、全部可行旧档夹具及二次读回验证。后续修改仍须按[发布清单](../release-checklist.md)复验，不能用本轮或历史A阶段结果替代新构建的检查。
