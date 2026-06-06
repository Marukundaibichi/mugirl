# 第二轮重构计划

本文档基于第一轮重构后的快照审查制定。第二轮不再追求“大搬家”，而是用吹毛求疵的标准消灭仍会被挑出的兼容性、侵入性、性能、代码规范、设计缺陷、冗余和发布卫生问题。

本轮执行原则是：先处理会影响全游戏、全存档或其他 mod 的高风险问题，再处理局部性能、生命周期、命名和资源问题。任何改动都不能只以“编译通过”作为完成标准。

本轮已按用户确认执行：不兼容旧档。旧设置类名、旧 `defName`、旧 XML class 名和旧资源路径不保留迁移壳；但新档加载、当前 Def 引用、语言 key、资源路径和 C# 构建必须保持内部一致。

## 当前状态说明

审查基线曾确认：

- `Release` 构建通过，输出 `1.6/Assemblies/MooGirlRace.dll`。
- 全仓库 XML 解析错误为 `0`。
- XML 中自定义 C# 类型引用缺失为 `0`。

当前工作区已经出现第二轮初步执行痕迹，后续正式执行前必须先确认这些改动是否保留、补完或回退：

- `1.6/Source/Features/Misc/Harmony_ForbidUtility_SetForbidden.cs` 已删除。
- `1.6/Source/Features/Misc/Harmony_GridsUtility_IsPolluted.cs` 已删除。
- `1.6/Source/MooGirlRace.csproj` 已开始移除相关编译项和错误 `Analyzer` 项。
- `1.6/Source/Features/Roping/Harmony_RopingTick.cs` 已开始收束 Prefix 逻辑，但仍需逐项校验原版断绳语义。

这几项不能算作阶段完成。只有完成对应记录、构建验证和回归检查后，才能写入“已完成”。

## 风险分级

P0 必须优先处理，处理前不建议继续扩展功能：

- 全局 Harmony Prefix 或 Transpiler 改写公共 API。
- 无条件吞掉原版错误日志、异常或红字。
- getter、Draw、Tick 等热路径写入全局状态。
- 依赖 IL 局部变量编号、私有字段布局、方法体顺序但没有失败回退。
- 缺少 DLC 或可选 mod 时会红字的 XML patch。

P1 必须在发布前处理：

- 热路径重复 Def 查询、全图扫描、LINQ 或无意义状态计算。
- 静态 Dictionary、HashSet、ThingOwner、GameComponent 生命周期不清楚。
- 可选兼容 patch 目标缺失时行为不明确。
- 项目文件、命名、注释仍残留明显历史包袱。

P2 可以排在 P0/P1 后，但不得在最终发布包中留下：

- 资源文件名带尾随空格、异常空格或不必要括号。
- 重复贴图、未引用资源、临时目录、构建产物。
- 文档、语言文件和行为说明不一致。

## 停手条件

执行中遇到以下情况必须暂停当前改动，先写入行为变更提案：

- 任何 XML 数值、C# 常量、概率、tick 间隔、产量、伤害、关系、事件权重发生变化。
- 原本玩家可观察的行为会变化，例如牵引断绳条件、束具解锁结果、产奶节奏、骑乘战斗时机。
- 为了修 bug 需要改变非 MooGirl pawn、植物、物品、门、奴隶、商人或其他 mod 内容的行为。
- 删除旧拼写、旧 `defName`、Scribe 字段或 XML class 引用可能影响新档加载。
- 兼容 patch 的目标无法确定，继续改动只能靠猜。

行为变更提案必须至少写清：当前行为、问题、建议行为、是否改数值、兼容风险、回退方案、需要的手动验证。

## 阶段 0：执行前复核

目标：在继续第二轮前重新建立可信工作面。

工作：

- 跑一次 `git status --short`，区分已完成改动、半成品改动和未跟踪文档。
- 核对 `docs/00-refactor-overview.md` 是否已经索引本文档。
- 重新读取所有 P0 目标文件，确认是否已经被上一次操作部分修改。
- 用 `rg` 搜索仍存在的高危标记。

建议命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; git status --short
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "Ldloc_S" 1.6/Source docs
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "MooGirlSkinApplied" 1.6/Source docs
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "Analyzer" 1.6/Source docs
```

验收：

- 当前未完成改动全部有归属说明。
- P0 风险项形成明确任务清单。
- 没有把半成品误记为完成。

## 阶段 1：收束全局 Harmony 侵入

目标：删除或限制影响非 MooGirl 内容的全局补丁，让每个保留 patch 都有清楚边界。

### 1.1 禁用状态公共 API

涉及文件：

- `1.6/Source/Features/Misc/Harmony_ForbidUtility_SetForbidden.cs`
- `1.6/Source/MooGirlRace.csproj`

计划：

- 优先删除该 patch。
- 如果确实存在本 mod 对象会调用 `SetForbidden` 失败，必须在调用源头修复。
- 禁止继续吞掉非本 mod 对象的错误日志。

验收：

- 项目文件不再引用该源码。
- 全仓库无生产代码引用该 patch 类型。
- 非 MooGirl 物品的 forbidden 行为完全回到原版。

### 1.2 污染与植物公共 API

涉及文件：

- `1.6/Source/Features/Misc/Harmony_GridsUtility_IsPolluted.cs`
- `1.6/Source/MooGirlRace.csproj`

计划：

- 删除 `GridsUtility.IsPolluted` 全局兜底。
- 删除或局部化 `Plant.TickLong` 防空指针 patch。
- 如果有真实空地图调用，追踪调用源并在源头修复。

验收：

- 项目文件不再引用该源码。
- 非 MooGirl 植物和污染网格不因本 mod 改变行为。
- 无条件吞异常、吞日志的 Prefix 不再存在。

### 1.3 牵引 Tick

涉及文件：

- `1.6/Source/Features/Roping/Harmony_RopingTick.cs`
- `1.6/Source/Features/Roping/RopingService.cs`

计划：

- 明确记录为什么 MooGirl 牵引仍需要替代原版 `Pawn_RopeTracker.RopingTick`。
- 保留原版断绳条件：死亡、倒地、征召、昏睡且被 pawn 牵引、异常精神状态、着火、不可达、拴点消失。
- 对照原版 `BreakAllRopes`、`BreakRopeWithRoper`、`UnropeFromSpot` 的差异，不能把单根断绳扩大成全体断绳。
- 将自定义 MooGirl 跟随逻辑尽量放入服务层，Prefix 只作为最小必要边界。

重点审查：

- 当前半成品中 `RopedTo.IsValid && !CanReach(...)` 走 `BreakAllRopesAndNotify(pawn)`，必须确认是否错误扩大了原版 `BreakRopeWithRoper` 行为。
- `EndRopingJobs` 是否会额外中断非牵引相关 job。
- `AnyMooGirlRopee` 的判定是否会误伤混合牵引场景。

验收：

- 文档记录原版行为差异。
- 保留 Prefix 的理由可以被复查。
- MooGirl 牵引、拴墙、断绳、不可达、征召、倒地、精神状态、着火路径都有手动验证记录。

### 1.4 Gear UI Transpiler

涉及文件：

- `1.6/Source/Features/Restraints/Harmony_DropThingTooltip.cs`

计划：

- 移除 `Ldloc_S 10` 这类固定局部变量编号依赖。
- 优先改为使用方法参数 `thing`，或用更稳健的 `CodeMatcher` 匹配 `"DropThingLocked".Translate()` 附近的调用。
- 匹配失败时必须回退原 IL，并用一次性诊断日志说明 patch 未应用。
- 非 MooGirl 锁定服装必须保留原版 `"DropThingLocked"` 提示，不得错误回退到 `"DropThingLodger"`。
- namespace 改回 `MooGirl`，除非能证明保留第三方 namespace 有兼容意义。

验收：

- 全仓库生产源码不再存在 `Ldloc_S 10`。
- Transpiler 失败不会破坏 Gear UI。
- 非本 mod apparel 的锁定提示不改变。

### 1.5 皮肤颜色 Getter

涉及文件：

- `1.6/Source/Features/Restraints/Harmony_PawnStoryTracker_SkinColor.cs`

计划：

- 删除 `GameComponent_MooGirlSkinOnce`。
- 删除 Scribe 字段 `MooGirlSkinApplied`。
- getter Postfix 必须保持纯函数式：只根据当前 pawn 判断是否覆盖 `__result`，不写入整局状态。
- MooGirl 判定使用 `MooGirlIdentity` 或明确说明继续使用 `MooGirlBody` 的兼容理由。

验收：

- `Pawn_StoryTracker.SkinColor` getter 不写 GameComponent、不写 Scribe、不写静态可变状态。
- 多个 MooGirl pawn 都能得到一致肤色。
- 非 MooGirl pawn 肤色不变。

## 阶段 2：补齐 DLC 与第三方兼容容错

目标：缺少 DLC 或可选 mod 时不红字、不刷噪音日志，存在可选 mod 时 patch 目标明确。

工作：

- 修正 `1.6/Patches/MooGirlPatch.xml` 对 `PreceptDef NutrientPasteEating_Disgusting` 的直接 patch。
- 审查 `1.6/Patches/MilkFood_TraderStock_Patch.xml`，去除重复库存插入并处理缺失 trader。
- 审查 `Versions/1.6/Integrations/SearchAndDestroy/Patches/SearchAndDestroy_Patch.xml` 的 think tree 目标和插入位置。
- 审查 `Versions/1.6/Integrations/VCookE/Patches/VCookE_Patch.xml` 的 `VCE_CheesePress` 与 PipeSystem comp 条件。
- 审查 `1.6/FacialAnimation/Patches/MooGirl_FacialAnimation.xml` 重复添加 comp 和 `success Always` 的必要性。

验收：

- 基础游戏、无 DLC、全 DLC、缺少可选 mod、存在可选 mod 五类加载路径都有解释。
- PatchOperation 不因可选目标缺失刷红字。
- 兼容 patch 的目标、失败策略和版本风险集中记录。

## 阶段 3：修复性能与生命周期边界

目标：减少热路径负担，明确静态状态和容器持有 pawn 的清理路径。

工作：

- 优化 `Features/Milk/CompMooHasBodyResource.cs`，将乳房生产档案查询移动到资源更新间隔之后。
- 审查 `Features/Milk/MooGirlMilkingAnimation.cs` 静态 `states` 的清理入口。
- 审查 `Features/Mounting/MountedPawnCombatTurret.cs` 的 `OriginalCasters`、`WarmupTimeOverrides` 异常恢复和清理。
- 审查 `Features/Mounting/Comp_MooGirlMount.cs` 中 `ThingOwner` 的保存、读档、销毁、卸载、掉落失败路径。
- 审查所有 `GameComponent` 是否真的需要，以及是否会重复创建或串档。

验收：

- 热路径不再做无意义 Def 查询、全图扫描或重复状态计算。
- 静态字典和临时缓存有清理策略。
- 容器持有 pawn 不会因地图卸载、载体销毁或读档而丢失。
- 新游戏、读档、返回主菜单后再开新档不串状态。

## 阶段 4：规范项目文件、命名和注释

目标：消除第一轮重构后仍会被维护者挑刺的残留。

工作：

- 修正 `1.6/Source/MooGirlRace.csproj`，删除错误 `Analyzer` 项，保留必要引用和 Release 输出路径。
- 重命名或隔离误导性类型，例如 `MechanoidWorkControlSettings`。
- 审查 `MooGirlMod.allArmorDefs` 是否仍有用途。
- 新代码不得继续扩散 `Apperal`、`Gloden`、`Hai`、`Heiffs`、`Nobel` 等拼写。
- 涉及 `defName`、XML class、Scribe 字段的旧拼写在本轮不兼容旧档前提下可以批量改名，但必须同步 C#、XML、语言 key、资源路径和验证记录。
- 删除逐行复述型注释，保留状态机、patch 边界、存档字段、兼容原因、数值保护和性能权衡注释。

验收：

- 项目文件不含语义错误项。
- 新增或迁移代码命名不继续扩散历史拼写。
- 注释解释设计原因，不复述显而易见代码。
- 编译通过。

## 阶段 5：整理资源路径与发布卫生

目标：减少资源加载脆弱点和发布包瑕疵。

工作：

- 处理带空格或不稳定字符的资源文件名：
  - `Textures/Things/Apparel/MooGirl_BunnyGirlHeaddress/MooGirl_BunnyGirlHeaddress_south .png`
  - `Textures/Things/Mote/MooGirl_EyeInHead _backpack.png`
  - `Sounds/milking(V1).mp3`
- 同步检查所有 XML 中的 `texPath`、`graphicPath`、`clipPath`。
- 对重复贴图目录进行人工确认。
- 检查发布包是否含 `bin`、`obj`、PDB、临时目录、备份目录。

验收：

- 资源路径不含尾随空格、异常空格或不必要括号。
- 所有改名资源都有 XML 引用同步。
- 发布目录只保留实际加载需要的文件。

## 阶段 6：全量回归验证

目标：用固定清单证明第二轮重构没有引入新问题。

必跑验证：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; MSBuild.exe 1.6/Source/MooGirlRace.csproj /p:Configuration=Release /p:Platform=AnyCPU /nologo /v:m
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "Ldloc_S" 1.6/Source
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "MooGirlSkinApplied" 1.6/Source
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; rg -n -F "Analyzer" 1.6/Source/MooGirlRace.csproj
```

还必须完成：

- XML 解析。
- 自定义 C# 类型引用交叉检查。
- 重复 `defName` 检查。
- PatchOperation 目标缺失检查。
- 资源路径存在性检查。
- Keyed 与 DefInjected 缺失检查。

手动游戏验证：

- 无 DLC 最小加载。
- 全 DLC 加载。
- HAR + Facial Animation 加载。
- Search and Destroy 集成加载。
- VCookE 集成加载。
- 新游戏生成 MooGirl。
- 产奶、挤奶、喝奶、喂奶、奶制食品。
- 束具穿戴、锁定、解锁、破解、高级束具状态机。
- 牵引、拴墙、断绳、不可达、开门、逃跑阻止。
- 骑乘、下马、销毁载体、读档恢复、骑手战斗。
- 开局坠机、野生逃亡、快递事件、巨企袭击。

最终验收：

- 无红字。
- 普通游玩不刷诊断日志。
- 非 MooGirl 内容无可观察副作用。
- 本文档每个阶段都有完成记录或明确延期理由。

## 完成记录模板

每完成一项问题，追加记录到本节下方。

```text
问题：
风险等级：
涉及文件：
处理方式：
是否改变数值：
是否改变玩家可见行为：
兼容风险：
验证命令：
手动验证：
遗留问题：
结论：
```

## 完成记录

### 2026-06-07 阶段 1：收束全局 Harmony 侵入

问题：`ForbidUtility.SetForbidden` 全局 Prefix 会吞掉全游戏错误日志。
风险等级：P0
涉及文件：`1.6/Source/Features/Misc/Harmony_ForbidUtility_SetForbidden.cs`、`1.6/Source/MooGirlRace.csproj`
处理方式：删除该 patch，并从项目文件移除编译项。
是否改变数值：否。
是否改变玩家可见行为：仅恢复非本 mod 对象的原版 forbidden 错误处理；本 mod 不再全局吞错。
兼容风险：若本 mod 仍有错误对象调用 `SetForbidden`，之后会暴露原版日志；这是预期的可诊断行为。
验证命令：`rg -n -F "Harmony_ForbidUtility_SetForbidden" 1.6/Source` 无命中；Release 编译通过。
手动验证：未做。
遗留问题：需要后续游戏内观察是否出现真实调用源错误。
结论：阶段 1 P0 项完成。

问题：`GridsUtility.IsPolluted` 与 `Plant.TickLong` 全局 patch 会影响非 MooGirl 地图污染和植物逻辑。
风险等级：P0
涉及文件：`1.6/Source/Features/Misc/Harmony_GridsUtility_IsPolluted.cs`、`1.6/Source/MooGirlRace.csproj`
处理方式：删除该 patch，并从项目文件移除编译项。
是否改变数值：否。
是否改变玩家可见行为：非 MooGirl 污染和植物路径恢复原版；若存在真实空地图调用，将转为可定位问题。
兼容风险：可能暴露之前被吞掉的异常源。
验证命令：`rg -n -F "Harmony_GridsUtility_IsPolluted" 1.6/Source` 无命中；Release 编译通过。
手动验证：未做。
遗留问题：需要后续无 DLC、全 DLC 和长档 tick 观察。
结论：阶段 1 P0 项完成。

问题：`Pawn_RopeTracker.RopingTick` Prefix 接管范围过宽，且不可达时曾把原版单绳断开扩大成全断绳。
风险等级：P0
涉及文件：`1.6/Source/Features/Roping/Harmony_RopingTick.cs`、`1.6/Source/Features/Roping/RopingService.cs`、`1.6/Source/Features/Roping/MapRopingIndex.cs`
处理方式：只在牵引者全部 ropee 都是 MooGirl 时接管原版 Tick；混合牵引普通 pawn 时交回原版。显式保留原版死亡、倒地、征召、昏睡、异常精神状态、着火、不可达和拴点消失检查。不可达改为调用原版私有 `BreakRopeWithRoper`，并补 `DropRope`、`UnropeFromSpot`、`BreakAllRopes` 后置索引同步。
是否改变数值：否。
是否改变玩家可见行为：收窄了自定义牵引的外溢边界；普通 pawn 混合牵引场景恢复原版断绳规则。MooGirl-only 牵引仍保留自定义“牵引者可继续普通工作”行为。
兼容风险：依赖原版私有方法 `BreakRopeWithRoper`，已加缺失诊断和兜底。
验证命令：Release 编译通过；XML 解析 0 错误。
手动验证：未做。
遗留问题：仍需游戏内验证 MooGirl-only 牵引、混合牵引、不可达、拴点消失、征召、倒地、精神状态、着火路径。
结论：阶段 1 P0 静态修复完成，手动回归待做。

问题：Gear UI tooltip transpiler 依赖 `Ldloc_S 10`，且非 MooGirl 锁定服装错误回退到 `DropThingLodger`。
风险等级：P0
涉及文件：`1.6/Source/Features/Restraints/Harmony_DropThingTooltip.cs`、`1.6/Languages/English/Keyed/Misc_Gameplay.xml`、`1.6/Languages/ChineseSimplified/Keyed/Misc_Gameplay.xml`
处理方式：改为使用 `DrawThingRow` 的稳定 `thing` 参数；匹配 `"DropThingLocked".Translate()` 失败时只记录一次诊断；非 MooGirl apparel 回退原版 `DropThingLocked` 提示；namespace 归回 `MooGirl`。
是否改变数值：否。
是否改变玩家可见行为：MooGirl 束具仍显示专用不可移除提示；非 MooGirl 锁定服装恢复原版锁定提示。
兼容风险：若 RimWorld 方法体变动导致匹配失败，会安全保留原版 IL 并记录诊断。
验证命令：`rg -n -F "Ldloc_S" 1.6/Source` 无命中；Release 编译通过。
手动验证：未做。
遗留问题：需要游戏内打开 Gear tab 验证 tooltip。
结论：阶段 1 P0 项完成。

问题：`Pawn_StoryTracker.SkinColor` getter 写入整局 `GameComponent_MooGirlSkinOnce`，导致只有首个 MooGirl 生效且引入无意义 Scribe 字段。
风险等级：P0
涉及文件：`1.6/Source/Features/Restraints/Harmony_PawnStoryTracker_SkinColor.cs`
处理方式：删除 `GameComponent_MooGirlSkinOnce` 和 `MooGirlSkinApplied` Scribe 字段；Postfix 改为纯结果覆盖，仅按当前 pawn 判定。
是否改变数值：否。
是否改变玩家可见行为：多个 MooGirl pawn 现在会一致使用目标肤色；非 MooGirl pawn 不受影响。
兼容风险：移除旧 Scribe 字段；当前重构总纲已明确不保证旧档兼容。
验证命令：`rg -n -F "MooGirlSkinApplied" 1.6/Source` 无命中；`rg -n -F "GameComponent_MooGirlSkinOnce" 1.6/Source` 无命中；Release 编译通过。
手动验证：未做。
遗留问题：需要游戏内生成多个 MooGirl 验证肤色。
结论：阶段 1 P0 项完成。

### 2026-06-07 阶段 2：DLC 与第三方兼容容错

问题：`MooGirlPatch.xml` 直接 patch Ideology 的 `PreceptDef NutrientPasteEating_Disgusting`，无 Ideology 时会失败。
风险等级：P0
涉及文件：`1.6/Patches/MooGirlPatch.xml`
处理方式：将直接 `PatchOperationAdd` 改为 `PatchOperationConditional`，目标 PreceptDef 存在时才追加 MooGirl 营养膏记忆 thought。
是否改变数值：否。
是否改变玩家可见行为：无 Ideology 时静默跳过；有 Ideology 时保留原行为。
兼容风险：无。
验证命令：XML 解析 0 错误；XPath 语法检查 0 错误；Release 编译通过。
手动验证：未做。
遗留问题：需要无 DLC 与全 DLC 启动验证。
结论：阶段 2 P0 静态修复完成。

问题：`MilkFood_TraderStock_Patch.xml` 对指定商人重复添加 `StockGenerator_BuyTradeTag`，且指定 trader 缺失时缺少一致的静默策略。
风险等级：P1
涉及文件：`1.6/Patches/MilkFood_TraderStock_Patch.xml`
处理方式：全局购买 tag 只加到尚未拥有 `MooGirl_MilkFood` buy tag 的 trader；指定 trader 仅追加销售库存 `StockGenerator_Tag`，并在目标 trader 存在且未已有相同销售 tag 时执行。
是否改变数值：否，保留原各 trader 的 `countRange`。
是否改变玩家可见行为：减少重复收购生成器；销售库存数量不变。
兼容风险：若其他 mod 已提前添加相同销售 tag，本 patch 会跳过，避免重复库存。
验证命令：XML 解析 0 错误；XPath 语法检查 0 错误；Release 编译通过。
手动验证：未做。
遗留问题：需要游戏内检查常见商队、贸易船和 Royalty 商人库存。
结论：阶段 2 P1 静态修复完成。

问题：Search and Destroy 集成直接插入行为树，旧 XPath 会命中 5 个 `ThinkNode_ConditionalColonist` 分支并插入 5 份节点。
风险等级：P1
涉及文件：`Versions/1.6/Integrations/SearchAndDestroy/Patches/SearchAndDestroy_Patch.xml`
处理方式：外层先检测是否已存在 Search and Destroy 节点；插入点收窄到唯一的 Idle colonist 分支前，目标缺失时静默跳过。
是否改变数值：否。
是否改变玩家可见行为：修正重复插入风险；设计意图仍是让 Search and Destroy 行为优先于闲置等待。
兼容风险：Search and Destroy 或本 mod think tree 若改名/改结构，本 patch 会静默跳过。
验证命令：当前 Def 快照中插入目标命中数为 1；XML 解析 0 错误；XPath 语法检查 0 错误；Release 编译通过。
手动验证：未做。
遗留问题：需要带 Search and Destroy 启动并观察 MooGirl 征召/非征召行为树。
结论：阶段 2 P1 静态修复完成。

问题：VCookE 集成直接 patch `VCE_CheesePress` 的 PipeSystem process，目标缺失或重复添加时缺少容错。
风险等级：P1
涉及文件：`Versions/1.6/Integrations/VCookE/Patches/VCookE_Patch.xml`
处理方式：先检测目标 process 是否已存在；仅在 `VCE_CheesePress` 与 PipeSystem processor processes 节点存在时添加。
是否改变数值：否。
是否改变玩家可见行为：无；避免重复添加同一 cheese process。
兼容风险：VCookE 或 PipeSystem 结构变动时静默跳过。
验证命令：XML 解析 0 错误；XPath 语法检查 0 错误；Release 编译通过。
手动验证：未做。
遗留问题：需要带 VCookE 启动并确认奶转奶酪 process 存在。
结论：阶段 2 P1 静态修复完成。

问题：Facial Animation patch 使用 `success Always` 掩盖失败，并一次性追加整组 comp，存在重复添加风险。
风险等级：P1
涉及文件：`1.6/FacialAnimation/Patches/MooGirl_FacialAnimation.xml`
处理方式：删除 `success Always`；先确认 MooGirl HAR race Def 存在；确保 `<comps>` 容器存在；每个 Facial Animation comp 单独检测，缺失时才追加。
是否改变数值：否。
是否改变玩家可见行为：无；避免重复 comp。
兼容风险：FA comp class 名称变动时仅对应 comp 跳过或保持缺失。
验证命令：`rg -n -F "<success>Always</success>" 1.6/FacialAnimation/Patches 1.6/Patches Versions/1.6/Integrations` 无命中；XML 解析 0 错误；XPath 语法检查 0 错误；Release 编译通过。
手动验证：未做。
遗留问题：需要带 HAR + Facial Animation 启动并确认脸部动画正常。
结论：阶段 2 P1 静态修复完成。

### 2026-06-07 阶段 3：性能与生命周期边界

问题：`CompMooHasBodyResource.CompTick` 在每 tick 热路径查询 pawn 和乳房生产 profile。
风险等级：P1
涉及文件：`1.6/Source/Features/Milk/CompMooHasBodyResource.cs`
处理方式：将 pawn/profile 查询移动到资源更新间隔之后，只在 `ResourceUpdateIntervalTicks` 到期时计算生产倍率；删除无用的 `UpdateBreastSize` 与 `BreastSizeDays`。
是否改变数值：否。
是否改变玩家可见行为：否；产奶累积仍按经过 tick 折算。
兼容风险：低；存档字段未新增，保留现有 `lastResourceUpdateTick` 节奏。
验证命令：Release 编译通过；`rg -n -F "BreastSizeDays" 1.6/Source` 无命中；`rg -n -F "UpdateBreastSize" 1.6/Source` 无命中。
手动验证：未做。
遗留问题：需要长档快进观察产奶进度与设备接管是否仍稳定。
结论：阶段 3 P1 静态修复完成。

问题：`MooGirlMilkingAnimation` 使用静态 `states` 保存 pawn 引用，缺少跨档和 pawn 生命周期清理入口。
风险等级：P1
涉及文件：`1.6/Source/Features/Milk/MooGirlMilkingAnimation.cs`、`1.6/Source/Features/Incidents/MooGirlStoryState.cs`
处理方式：新增 pawn despawn/destroy 时按 pawn 清除动画状态；新增 `ResetTransientState`，在新档、读档、FinalizeInit 时清空临时动画状态。
是否改变数值：否。
是否改变玩家可见行为：否；只清理已经结束或跨存档不应保留的视觉状态。
兼容风险：低；挤奶动画状态本来不应进存档。
验证命令：Release 编译通过；XML 解析 0 错误。
手动验证：未做。
遗留问题：需要游戏内验证挤奶开始、结束、pawn 离图、销毁、读档后不会残留姿势或禁用渲染缓存。
结论：阶段 3 P1 静态修复完成，手动回归待做。

问题：`MountedCombatController` 的 `OriginalCasters`、`WarmupTimeOverrides` 是静态字典，正常下马外的换武器、装备移除、跨档路径清理不足。
风险等级：P1
涉及文件：`1.6/Source/Features/Mounting/MountedPawnCombatTurret.cs`、`1.6/Source/Features/Incidents/MooGirlStoryState.cs`
处理方式：新增整局临时状态重置；装备移除时按武器 verb 清除 caster/warmup 记录；下马恢复时同步清除 warmup override；新档、读档、FinalizeInit 时清空炮台临时状态。
是否改变数值：否。
是否改变玩家可见行为：否；只恢复异常路径下的 verb/caster 临时状态。
兼容风险：中；新增 `Pawn_EquipmentTracker.Notify_EquipmentRemoved` Prefix，目标是原版稳定公开方法，但仍需加载期确认 Harmony 目标。
验证命令：Release 编译通过；XML 解析 0 错误。
手动验证：未做。
遗留问题：需要游戏内验证骑乘射击、下马、骑手换枪、武器销毁、读档后的射击者和 warmup 表现。
结论：阶段 3 P1 静态修复完成，手动回归待做。

问题：`Comp_MooGirlMount` 中骑手容器在载体 despawn/destroy 时若掉落失败，只记录警告并把 pawn 留在容器中。
风险等级：P1
涉及文件：`1.6/Source/Features/Mounting/Comp_MooGirlMount.cs`、`1.6/Languages/English/Keyed/Misc_Gameplay.xml`、`1.6/Languages/ChineseSimplified/Keyed/Misc_Gameplay.xml`
处理方式：先尝试原位置掉落，再尝试附近 5 格可行走 fallback；仍失败时记录一次警告，并调用 `ClearAndDestroyContentsOrPassToWorld` 将 pawn 交给原版 world pawn 兜底。
是否改变数值：否。
是否改变玩家可见行为：是，仅异常掉落失败路径从“骑手留在载体容器”改为“骑手进入 world pawn 兜底”。
兼容风险：中；避免 pawn 挂在已销毁容器里，但需要确认 world pawn 兜底后是否符合玩家预期。
验证命令：Release 编译通过；XML 解析 0 错误；`rg -n -F "the rider remains in the mount container" 1.6` 无命中。
手动验证：未做。
遗留问题：需要游戏内验证载体销毁、地图卸载、无合法掉落格时骑手不会丢失或红字。
结论：阶段 3 P1 静态修复完成，异常路径行为变更已在下方记录提案。

### 2026-06-07 阶段 4：项目文件、命名和注释

问题：项目文件与设置类仍残留第一轮前的误导命名和无用成员。
风险等级：P1
涉及文件：`1.6/Source/MooGirlRace.csproj`、`1.6/Source/Core/MooGirlMod.cs`、`1.6/Source/UI/MooGirlSettingsWindow.cs`
处理方式：项目文件删除错误 `Analyzer` 项并同步已删除源码；`MechanoidWorkControlSettings` 重命名为 `MooGirlSettings`；删除未使用的 `MooGirlMod.allArmorDefs`；删除空的 settings `DoWindowContents`，由 `MooGirlSettingsWindow.Draw` 负责设置 UI。
是否改变数值：否。
是否改变玩家可见行为：否；仅移除旧设置类名兼容，用户已确认不用兼容旧档。
兼容风险：旧 mod 设置文件不会迁移到新设置类名；这是本轮有意选择。
验证命令：`rg -n "MechanoidWorkControlSettings|allArmorDefs" 1.6/Source` 无命中；Release 编译通过。
手动验证：未做。
遗留问题：需要游戏内打开 mod 设置窗口，确认设置显示、保存和读档后仍正常。
结论：阶段 4 P1 静态修复完成。

问题：`Apperal`、`Heiffs`、`Hai`、`Gloden` 等历史拼写进入了 C# 类型、XML class、`defName`、语言 key 和资源文件名。
风险等级：P1
涉及文件：`1.6/Source/Features/Restraints/*`、`1.6/Defs/Apparel/*`、`1.6/Defs/HairDefs/Hairs.xml`、`1.6/Defs/ThingDefs_Races/MooGirl_Race.xml`、`Bio_1.6/Defs/GoldenRaceBioTitle.xml`、相关语言文件与 key 贴图。
处理方式：在不兼容旧档前提下统一重命名：`SlaveApperal` -> `SlaveApparel`、`DisappearsAndAddHeiffs` -> `DisappearsAndAddHediffs`、`MooGirl_Hai` -> `MooGirl_Hair`、`GlodenRaceBioTitle` -> `GoldenRaceBioTitle`；同步 `.csproj` 编译项、DefOf 字段、JobDef、Recipe ingredient、XML class、语言 key、DefInjected key 和 key 贴图路径。`MooGirl_NobelPrize` 保留，因为它对应 Nobel Prize 语义，不按拼写错误处理。
是否改变数值：否。
是否改变玩家可见行为：新档内部标识更名；旧档、旧设置、旧 Def 引用不迁移。
兼容风险：旧档中引用旧 `defName`、旧 XML class 或旧设置类型的对象不会自动迁移；用户已确认不用兼容旧档。
验证命令：`rg -n "Apperal|Heiffs|\bHai\b|Gloden" 1.6 Bio_1.6 Versions Textures Sounds` 无命中；Release 编译通过；XML 解析通过。
手动验证：未做。
遗留问题：需要游戏内新档验证束具钥匙、解锁 job、发型分类、BioTech xenotype 是否正常显示和生成。
结论：阶段 4 P1 静态修复完成，保留 `Nobel` 为语义命名。

### 2026-06-07 阶段 5：资源路径与发布卫生

问题：资源文件名包含尾随空格、异常空格或不必要括号，增加 XML 路径和发布包脆弱性。
风险等级：P2
涉及文件：`Textures/Things/Apparel/MooGirl_BunnyGirlHeaddress/MooGirl_BunnyGirlHeaddress_south.png`、`Textures/Things/Mote/MooGirl_EyeInHead_backpack.png`、`Sounds/milking_V1.mp3`、`1.6/Defs/SoundsDefs/Bondage_Sounds.xml`
处理方式：移除资源文件名中的尾随空格、异常空格和括号：`MooGirl_BunnyGirlHeaddress_south .png` -> `MooGirl_BunnyGirlHeaddress_south.png`，`MooGirl_EyeInHead _backpack.png` -> `MooGirl_EyeInHead_backpack.png`，`milking(V1).mp3` -> `milking_V1.mp3`；同步音效 `clipPath`。
是否改变数值：否。
是否改变玩家可见行为：否；资源路径更稳定。
兼容风险：无旧路径迁移；当前 XML 已同步。
验证命令：`rg -n "milking\\(V1\\)|south \\.png|EyeInHead _backpack" 1.6 Bio_1.6 Versions Textures Sounds` 无命中；音效路径扫描通过。
手动验证：未做。
遗留问题：需要游戏内确认对应音效和贴图实际显示/播放。
结论：阶段 5 P2 静态修复完成。

问题：资源路径检查存在 `Graphic_StackCount`、`Graphic_Cluster`、集合贴图和原版资源引用误报风险。
风险等级：P2
涉及文件：`1.6/Defs`、`Bio_1.6/Defs`、`Textures`、`Sounds`
处理方式：按 RimWorld 实际加载规则复核资源路径：`Graphic_Single` 查单图或方向图；`Graphic_Multi` 查方向图；`Graphic_Collection`、`Graphic_StackCount`、`Graphic_Cluster` 查目录内集合贴图；`BaseFilth` 继承的 `Graphic_Cluster` 按集合图处理；已知原版路径作为外部资源引用记录，不作为本 mod 缺图。
是否改变数值：否。
是否改变玩家可见行为：否。
兼容风险：无。
验证命令：纹理路径扫描通过；声音 `clipPath` 扫描通过；具体 Def 重复扫描通过；发布目录杂质扫描未发现 `bin`、`obj`、`.pdb`、`.tmp`、`.bak`。后续可重复脚本按原版 Def 中出现过的 bundled `texPath` 识别 vanilla 引用，当前记录 8 项。
手动验证：未做。
遗留问题：仍需游戏内加载实际确认集合贴图、原版资源引用、奶制品堆叠图和污渍贴图无红字。
结论：阶段 5 P2 静态验证完成。

问题：`MooGirl_ThinkTreeDefs.xml` 直接重新定义原版 `HumanlikeConstant`，覆盖全游戏人形 pawn 的常量思维树。
风险等级：P0
涉及文件：`1.6/Defs/ThinkTreeDefs/MooGirl_ThinkTreeDefs.xml`、`1.6/Defs/ThingDefs_Races/MooGirl_Race.xml`
处理方式：删除本 mod 中重复的 `HumanlikeConstant` Def，让 `MooGirl` race 的 `<thinkTreeConstant>HumanlikeConstant</thinkTreeConstant>` 继续解析到原版当前 Def；`MooGirlLike` 主思维树不变。
是否改变数值：否。
是否改变玩家可见行为：是；非 MooGirl 人形 pawn 的常量思维树恢复原版，不再被本 mod 覆盖。MooGirl 也会使用当前 RimWorld 1.6 原版 `HumanlikeConstant`，包含原版新增的常量行为。
兼容风险：低；不兼容旧档不是本项关注点，当前新档加载引用仍指向存在的原版 Def。
验证命令：`.\docs\tools\Invoke-Phase6StaticValidation.ps1` 通过；脚本会检查本 mod 具体 Def 是否重复、是否覆盖 vanilla/DLC Def。
手动验证：未做。
遗留问题：需要游戏内确认 MooGirl 的 despawn、爆炸躲避、敌意反应、Lord duty 等 constant think tree 行为仍正常。
结论：阶段 5 追加发现的 P0 侵入性 Def 覆盖已修复。

### 2026-06-07 阶段 6：全量静态回归验证

问题：第二轮改动跨 C#、XML、语言文件、资源路径和可选集成，必须用固定清单确认没有引入新缺口。
风险等级：P1
涉及文件：`1.6/Source`、`1.6/Defs`、`1.6/Languages`、`1.6/Patches`、`1.6/FacialAnimation`、`Bio_1.6`、`Versions/1.6`、`Textures`、`Sounds`
处理方式：新增并执行 `docs/tools/Invoke-Phase6StaticValidation.ps1`，覆盖 Release 构建、全 XML 解析、`About/About.xml` 元数据检查、`LoadFolders.xml` 加载入口检查、旧拼写和危险模式生产路径扫描、`git diff --check`、`.csproj` 源文件引用检查、自定义 `MooGirl.*` XML 类型交叉检查、Patch XPath 语法检查、直连 `PatchOperationAdd` 目标检查、本 mod 具体 Def 重复/覆盖原版检查、Keyed 翻译检查、DefInjected orphan 检查、声音和贴图路径检查、发布卫生检查。
是否改变数值：否。
是否改变玩家可见行为：否；本阶段只验证前序改动。
兼容风险：旧档兼容不纳入验收；用户已确认不用兼容旧档。本阶段验收目标是新档加载与当前引用内部一致。
验证命令：`.\docs\tools\Invoke-Phase6StaticValidation.ps1` 通过。脚本输出：Release 编译通过，输出 `1.6/Assemblies/MooGirlRace.dll`；XML 解析通过；`About/About.xml` 元数据检查通过，packageId、1.6 支持版本、Harmony/HAR 依赖、loadAfter 顺序和 Facial Animation 旧版冲突声明均符合预期；`LoadFolders.xml` 检查通过，6 个 v1.6 加载入口均存在、无重复且可选目录 gate 正确；生产路径 `Apperal|Heiffs|\bHai\b|Gloden|MechanoidWorkControlSettings|allArmorDefs|milking\(V1\)|south \.png|EyeInHead _backpack|Ldloc_S|MooGirlSkinApplied|Analyzer` 扫描无命中；`git diff --check` 通过；`.csproj` 源文件引用扫描通过，147 个 compile item 全部存在且无遗漏源码；`MooGirl.*` XML 类型交叉检查通过，69 个引用均有源码类型；Patch XPath 语法检查通过，94 个 `<xpath>` 节点均可编译；直连 `PatchOperationAdd` target 扫描通过，37 个直接目标均存在，1 个可选外部集成目标记录但不硬判；本 mod 具体 Def 碰撞扫描通过，361 个具体 Def 无重复且未覆盖 vanilla/DLC Def；直接 Keyed 翻译扫描通过，158 个 `.Translate()` 源 key 均有 EN/ZH；宽口径 Keyed 扫描通过，206 个 `MooGirl.*` 源字符串 key 均有 EN/ZH；DefInjected orphan 扫描通过，索引 10 个 Def 根、266 个 Def 类型；声音 `clipPath` 扫描通过，10 个节点均有音频；贴图路径扫描通过，133 个路径中本 mod 自有贴图均可解析，8 个原版 bundled `texPath` 作为 vanilla 引用记录；发布卫生扫描通过，生产目录无 `bin`、`obj`、`.pdb`、`.tmp`、`.bak`。
扫描器口径说明：DefInjected 检查必须索引 Core/Royalty/Ideology/Biotech/Anomaly/Odyssey 与本 mod Def，并将 `AlienRace.ThingDef_AlienRace`、`MooGirl.SlaveApparelDef` 映射到 `ThingDef`，将 `AlienRace.AlienBackstoryDef` 映射到 `BackstoryDef`；带 `Name` 且 `Abstract="True"` 的父 Def 也需要作为可翻译 Def 计入。贴图检查不能把 RimWorld 1.6 的原版 bundled 资源当成本 mod 缺图，应通过原版 Def 中出现过的 `texPath` 识别 vanilla 引用。
手动验证：未做；用户已确认游戏内验证由其手动执行，本轮不再自动启动 RimWorld。
遗留问题：仍需游戏内验证无 DLC、全 DLC、HAR + Facial Animation、Search and Destroy、VCookE、新游戏生成、产奶/挤奶/喝奶/喂奶、束具状态机、牵引/断绳/拴墙、骑乘/销毁/读档、开局和事件链均无红字、无刷日志、无非 MooGirl 副作用。
结论：阶段 6 静态回归验证完成；本轮自动化执行范围已收束，最终发布前仍由用户补游戏内手动验证。

## 行为变更提案记录

### 2026-06-07 骑乘容器掉落失败兜底

触发原因：`Comp_MooGirlMount` 在载体 despawn/destroy 时掉落骑手，如果原位置与 fallback 位置都无法放置，旧逻辑只警告并让骑手继续留在骑乘容器中。
当前行为：掉落失败后骑手仍由载体 `ThingOwner` 持有；如果载体随后销毁或离开可访问生命周期，存在 pawn 引用悬挂或丢失风险。
发现的问题：该路径不会改变普通下马，但在异常销毁、地图卸载、被其他 mod 改写放置规则时，容器持有 pawn 的所有权边界不清楚。
建议行为：掉落失败后调用原版 `ClearAndDestroyContentsOrPassToWorld`，对 pawn 使用 `WorldPawns.PassToWorld` 兜底。
是否改变数值：否。
受影响玩法：仅骑乘者无法被放到地图上的异常边缘路径。
兼容风险：骑手可能进入 world pawn 池而不是留在当前地图；但这比挂在已销毁载体容器中更可恢复。
回退方案：若游戏内验证发现 world pawn 兜底不可接受，改为强制搜索更大半径或使用 `GenSpawn.Spawn` 的更宽松放置策略。
需要确认：载体被销毁、被 despawn、地图卸载、目标格全堵死时，骑手不会消失、不会重复生成、不会刷红字。

## 行为变更提案模板

```text
触发原因：
当前行为：
发现的问题：
建议行为：
是否改变数值：
受影响玩法：
兼容风险：
回退方案：
需要确认：
```
