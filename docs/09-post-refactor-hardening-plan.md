# 后重构挑刺整改计划

本文档基于第二轮重构后的审查结论制定。目标不是“修到能编译”，而是把兼容性、性能、代码规范、侵入性、设计缺陷、冗余和发布卫生问题逐项闭环到任何审查者都难以继续挑出硬伤。

当前静态状态：

- `Release` 构建已通过。
- `docs/tools/Invoke-Phase6StaticValidation.ps1` 已通过。
- 静态验证覆盖编译项、空生产类型、LoadFolders、XML 类型引用、patch xpath、直接 `PatchOperationAdd`、mod concrete defs、keyed keys、同文件语言键重复、英中 key 与数字占位符一致性、业务代码直接 `Verse.Log` 调用、TickManager 集中入口、`Find.*` 全局入口、动态 RecipeDef 生成、MooGirl 身份判断集中入口、PawnGenerator 生成后束具自动锁定、玩家派系 helper、束缚目标安全、束具 hediff 状态安全、牵引目标安全、骑乘状态安全、奶产交互安全、事件交互安全、Harmony 边界、ability/misc job 安全、装备生命周期 base call、高风险 Scribe 状态默认值、可选 DLC Def gate、音效路径和贴图路径。
- 尚未完成 RimWorld 游戏内加载、交互、长时间运行和读档验证。

静态通过只说明文件一致性足够好，不说明运行时生命周期、DLC 缺失、其他 mod 组合、长档 tick 和玩家可见行为已经可靠。

## 本轮执行记录

2026-06-07 已完成第一批整改：

- H01：给 Biotech-only DefOf 字段添加 `[MayRequire("Ludeon.RimWorld.Biotech")]`。
- H02：洗脑头盔计时器改为制作、生成、装备和读档后只在未初始化时补值。
- H03：骑乘 pawn 的 interval tick 从少量 tracker 白名单扩展为有边界的长期生理 tick；仍故意不推进隐藏 pawn 的 job、寻路和社交。
- H04：`SlaveApparel.isLocked` 的 Scribe 默认值改为 `true`。
- H05：`durationTick` 接入 forced job 的 `expiryInterval`。
- H06：乳房 profile 接入产奶生产倍率，产后加成接入出生事件，并删除无调用的 production-days 分支。
- H07：Ghoul 渲染刷新删除 `Hediff.Tick` 全局 patch，改为 PostAdd 后短时队列刷新。
- H08：删除对所有 `TraderKindDef` 添加 `StockGenerator_BuyTradeTag` 的全局 patch。
- H09：administer milk recipeUsers 从所有 flesh pawn 收窄到 MooGirl race/body。
- H10：删除采集工作里的恒真条件。
- H11：牵引到墙绳扣 job 增加全局 finish cleanup，失败和中断也会清 pending spot。
- H12：删除 `BreakAllRopes()` 后的重复断绳通知。
- H13：`.sai2` 源素材未删除；新增 `docs/tools/New-WorkshopPackage.ps1`，发布包生成时排除源素材、`Source`、PDB、bin/obj、临时和备份文件。
- 游戏内验证辅助：新增 `docs/tools/New-GameValidationConfigs.ps1` 生成 6 组 `ModsConfig.xml` 模板，新增 `docs/tools/Invoke-PlayerLogScan.ps1` 扫描 `Player.log` 疑似红字。

2026-06-07 已完成追加日志线索整改：

- H14：`MooGirl_Race.xml` 的 `soundMeleeHitBuilding` 从不存在的 `Pawn_Melee_Punch_HitBuilding` 改为原版存在的 `Pawn_Melee_Punch_HitBuilding_Generic`。
- H15：清理 `ChineseSimplified/DefInjected/QuestScriptDef/Script_MooGirl.xml` 中同文件重复的任务翻译键，避免 RimWorld 中文翻译加载报告重复 key。
- H16：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增同文件语言键重复扫描，当前检查 1477 个语言翻译节点。

2026-06-07 已完成追加规范和热重载整改：

- H17：`CompSlaveApparelGear` 菜单目标从 `AllPawns` 收窄为 `AllPawnsSpawned`，且只显示实际穿着束具的可解锁目标，避免空子菜单和无意义全图 pawn 噪音。
- H18：把业务代码中的直接 `Log.Warning`/`Log.Message`/`Log.Error` 收束到 `MooGirlLog`，异常路径使用 `WarningOnce` 限频；Phase 6 新增业务代码直接 `Verse.Log` 调用门禁。
- H19：`DrugAdministerDefs` 热重载复用动态生成的 administer milk recipe 时，先清空 `ingredients`、`fixedIngredientFilter` 和 `recipeUsers`，避免热重载后重复叠加牛奶原料。

2026-06-07 已完成追加可选 DLC gate 整改：

- H20：`MuGirl_Gun_Flame_Thrower` 继承 Biotech-only `LightMechanoidGunRanged`，并引用 Biotech-only `Shot_MiniFlameblaster`；现将整个武器 Def 及 race weapon list 引用标记为 `MayRequire="Ludeon.RimWorld.Biotech"`。
- H21：机械师背包系列的核心效果和研究链依赖 Biotech；现将四个背包 Def 及 race apparel list 引用整体标记为 Biotech-only。`MooGirl_RA_MechanicRecon` 的 `Gunlink` 研究前置标记为 Royalty-only。
- H22：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增高置信可选 DLC Def gate 扫描，覆盖 `ParentName`、研究前置、音效、弹药、配方材料、分类和 stat 字段，当前扫描 29 个可选 DLC Def 引用。

2026-06-07 已完成追加翻译一致性整改：

- H23：补齐 `ChineseSimplified/DefInjected/RulePackDef/RulePacksDefs.xml` 中缺失的 `MooGirl_NamerPerson.rulePack.rulesStrings`，避免中文语言下人名规则 key 缺项。
- H24：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增 English -> ChineseSimplified 相对路径/key 一致性与数字格式占位符检查；当前检查 550 个英文基线 key，允许中文为语法需要额外使用 `{PAWN_pronoun}` 等非数字 RimWorld 语法标签。

2026-06-07 已完成追加装备生命周期与 Scribe 状态整改：

- H25：`SlaveApparel` 和 `AdvancedSlaveApparel` 覆写 `Notify_Equipped` 时没有调用 base，`SlaveApparel.Notify_Unequipped` 也没有调用 base，导致 `ThingWithComps` 的装备/卸下 comp 回调被绕过；现恢复 base 调用，并去掉高级束具装备路径中的重复 hediff/锁定逻辑。
- H26：`Comp_MagneticShackles` 的 `OnEquipped/OnUnequipped` 不是 RimWorld 生命周期覆写，实际不会自动触发；现改为 `Notify_Equipped/Notify_Unequipped`，并在制作、生成、装备和读档后只补非法计时值，不覆盖合法存档值。
- H27：磁力镣铐读档缺少 `currentCycleInterval`、`currentBindDurationTicks`、`nextStateTick` 默认值时可能立即触发或重置周期；现补齐 Scribe 默认值，并在 `isActive` 状态下按束缚持续时间恢复下一次状态切换。
- H28：`SlaveApparel.lockCount` 读档缺少默认值时可能与 `isLocked` 不自洽；现使用 Def 的 `needkeynumber` 作为默认值，并在 locked + non-positive count 的非法组合中修复计数。
- H29：完整解锁路径会把 `lockCount` 减到 0 但不总是同步 `isLocked=false`，且存在先 `Remove` 再放置到地图的非原子掉落路径；现 job/消耗品解锁都通过 `Pawn_ApparelTracker.TryDrop` 掉落，成功解锁会同步 `isLocked=false`，失败时恢复锁定状态且不把装备悬空。
- H30：`AdvancedSlaveApparel.Notify_Equipped` 遍历其他束具时错误使用当前装备的 `isLocked`，可能由一件已解锁/破解装备影响其他束具锁定状态；现改为逐件读取自己的 `isLocked`/`IsCracked()`。
- H31：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增生命周期 base call 门禁和高风险 Scribe 状态默认值门禁；当前检查 7 个生命周期覆写，高风险裸默认 Scribe 命中为 0。
- H32：`QuestPart_SpawnCourier.spawnCell` 裸 Scribe 默认会退化为 `(0,0,0)`，该坐标在 RimWorld 中是有效格子，缺字段时不会重新寻找入场点；现将默认值改为 `IntVec3.Invalid`，并纳入高风险 Scribe 默认值门禁。
- H33：`JobDriver_UnlockSlaveApparelGear` 最终 toil 等待期间若目标束具已被其他路径移除，只检查 target 非空仍会继续扣锁/消耗钥匙；现确认目标 pawn 仍穿着该 apparel 后才执行解锁，否则退出并保留钥匙。
- H34：`Comp_BrainwashHelmet` 的 gizmo action 在生成按钮时检查 `Wearer`，但点击执行时重新取 wearer，装备状态若在 UI 帧间变化可能空引用；现 action 内重新捕获并判空，`ExecuteHediffLogic` 也能处理空 pawn/空转换列表。
- H35：`Comp_ShockCollar` 手动电击按钮执行时虽然重新检查 current wearer，但实际 `applyAction` 闭包仍捕获按钮生成时的 wearer；现改为将当前 wearer 传入 action，避免 UI 帧间换装时作用到旧 pawn。
- H36：`BrainWashSlaveApparel` 继承 `AdvancedSlaveApparel`，但多处仍写成 `AdvancedSlaveApparel || BrainWashSlaveApparel`，并导致破解器中 BrainWash 专用分支不可达；现删除冗余判断，调整破解器分支顺序，并在 Phase 6 禁止该冗余继承判断回流。

2026-06-07 已完成追加束缚交互入口整改：

- H37：束具解锁菜单用 `LocalTargetInfo == null` 表达“无目标/自身目标”，且目标只穿已解锁束具时仍可能打开空二级菜单；现统一解析 job target，目标无锁定束具时给出禁用原因，点击二级菜单时重新读取目标状态。
- H38：`JobDriver_UnlockSlaveApparelGear` 菜单允许尸体目标，但执行端只把 TargetB 当 `Pawn` 读取，尸体解锁会静默失败；现 TargetB 支持 `Pawn` 和 `Corpse`，执行前后重新校验目标仍穿着该 apparel、钥匙类型仍匹配，并在掉落失败时恢复原锁数且不消耗钥匙。
- H39：`CrackBondageGear` 目标选择器和 job driver 对目标有效性重复假设，直接强转目标并允许已破解/非地图装备进入执行链；现目标选择阶段只接受地图上未破解高级束具，job 执行端也做同样复核，目标失效时安静失败。
- H40：`JobDriver_UseItemOn` 对 TargetB 直接强转为 `Pawn`，异常 job 或跨 mod 调用给错目标时可能红字；现只接受 `Pawn`/`Corpse`，拾取失败以 `Incompletable` 结束，不再把无效目标传给 use effect。
- H41：`CompUnlockBondageGear` 直接 use effect 先调用 base、再检查 pawn，且忽略钥匙类型和 `LockedApparel`，部分解锁不会消耗钥匙；现先判空，只遍历匹配钥匙的锁定束具，部分解锁也消耗钥匙，完整解锁掉落失败则恢复原状态。
- H42：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增束缚目标安全门禁，禁止本轮修掉的 `LocalTargetInfo` null 比较、未经校验返回玩家目标和高风险 job 目标强转回流。

2026-06-07 已完成追加牵引交互入口整改：

- H43：`JobDriver_RopeMoo` 和牵引 FloatMenu 各自维护目标有效性规则，job 端还直接强转 TargetA；现把牵引开始条件收束到 `RopingService.CanStartPawnRope`，菜单和 job 执行前后共用同一套 spawned/dead/map/moving/mental/重复牵引规则。
- H44：`JobDriver_RemoveRopeMoo` 直接强转 TargetA，且等待结束时即使目标已无绳也会打断目标当前工作；现目标读取改为安全 `as Pawn`，预约、移动、等待和最终动作都复核目标仍有绳。
- H45：`JobDriver_RopeToWallRopeHitch` 直接强转建筑和 ropee，缺少地图/生成状态复核；同时先标记 pending spot rope 后又通过 `BreakAllRopesAndNotify(ropee)` 清掉 pending。现执行前后校验 roper、ropee、hitch 同图且仍生成，断绳后再标记 pending，失败和结束都会清理 pending。
- H46：`Comp_RopeToBuild.StartRoping` 点击后不复查建筑预约/路径，并直接 `StartJob(job)`；现点击时重新 `CanReserveAndReach`，并使用 `TryTakeOrderedJob(job, JobTag.Misc)` 进入标准玩家命令路径。
- H47：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增牵引目标安全门禁，禁止本轮修掉的 roping job 目标强转和 `StartJob(job)` 回流。

2026-06-07 已完成追加骑乘状态边界整改：

- H48：骑乘者和骑手 gizmo 的选择/下骑 action 捕获按钮生成时的 rider、carrier 或 comp，UI 帧间下骑、换乘或容器变化后可能作用到旧对象；现点击时重新解析当前 `MountedPawn` / `GetMountForRider`。
- H49：`TryEmergencyDismountNear` 和销毁/反生成兜底 `TryDropAt` 信任传入 cell 或无 validator 的 `ThingOwner.TryDrop`，极端情况下可能把骑手放到不可站立或被阻挡的位置；现所有应急/兜底下骑都复用 `DismountCellValidator`，必要时重新寻找合法下骑格。
- H50：`MountedCombatController` 的 mounted ranged 私有入口假设 comp、carrier 和 searcher 状态始终有效；现开始射击、完成射击和目标搜索入口都补充 comp/verb/carrier/map 防线，searcher 属性也能处理空 comp。
- H51：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增骑乘状态安全门禁，禁止无 validator 下骑、捕获旧 comp/carrier 的骑手 gizmo、以及 mounted combat searcher 直接假设 comp 非空回流。

已验证：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; msbuild .\1.6\Source\MooGirlRace.csproj /p:Configuration=Release /v:minimal
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-Phase6StaticValidation.ps1 -SkipBuild
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\New-WorkshopPackage.ps1 -SkipBuild
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\New-GameValidationConfigs.ps1
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-PlayerLogScan.ps1
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; git diff --check
```

验证结果：Release 构建通过，Phase 6 静态验证通过，发布包脚本试跑通过，验证配置模板生成通过，diff whitespace 检查通过。`Invoke-PlayerLogScan.ps1` 对当前旧 `Player.log` 返回失败，命中的是本轮已从源文件层面处理的 SoundDef cross-reference 与中文翻译报告疑点；该日志不是本轮改动后的标准验证日志，必须在重新启动 RimWorld 后用 fresh `Player.log` 复扫，游戏内验证仍未执行。

追加验证结果：本轮装备生命周期与 Scribe 状态整改后，Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增门禁检查到 7 个生命周期覆写，高风险无默认值 Scribe 状态命中为 0。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

H32/H33 追加验证结果：`spawnCell` 默认值与解锁 job 并发保护修复后，Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过，`New-WorkshopPackage.ps1 -SkipBuild` 通过，`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。

H34/H35 追加验证结果：洗脑头盔和电击项圈 gizmo 点击时序防护修复后，Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过，`New-WorkshopPackage.ps1 -SkipBuild` 通过，`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。

H36 追加验证结果：继承冗余判断清理后，`rg` 未再找到 `AdvancedSlaveApparel || BrainWashSlaveApparel` 或 BrainWash 不可达 `else if` 模式；Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过，`New-WorkshopPackage.ps1 -SkipBuild` 通过，`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。

H37-H42 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增束缚目标安全门禁检查 5 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

H43-H47 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增牵引目标安全门禁检查 4 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

H48-H51 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增骑乘状态安全门禁检查 5 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加奶产交互链整改：

- H52：成人喝奶、喂倒地者和儿童找奶喝的效果发放与奶量消耗不是原子操作，等待期间奶量被其他路径消耗后仍可能白送饱食、心情、疗愈或哺育进度；现所有直接奶交互都先按对应比例成功消费奶量，再发放效果，失败时 job 以不可完成结束。
- H53：儿童找奶喝显示和 job 前置沿用成人 5% 奶量门槛，但实际一次消耗 30%，导致 5%-30% 奶量时也能获得完整儿童收益；现儿童路径使用独立 30% 门槛，菜单、预约、等待和最终提交共用同一判定。
- H54：奶产 job driver 多处直接强转 `TargetA`，异常 job 或跨 mod 错目标可能红字；采集结束还会打断目标当前任意 `Wait_MaintainPosture`。现喝奶、喂奶、找奶喝和榨乳 job 都改为安全解析目标，榨乳强制等待只清理本 job 创建的等待 job。
- H55：右键强制榨乳绕过 `WorkGiver_GatherMilk` 的榨乳器管理限制，且点击时不复查预约、路径、奶量和设备状态；现菜单显示榨乳器管理禁用原因，点击 action 重新校验 `CanReserveAndReach`、奶量和设备状态后才派 job。
- H56：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增奶产交互安全门禁，禁止奶产 job 目标强转、非原子直接奶效果、儿童奶量门槛回退、菜单点击缺少预约/路径复查和榨乳器管理绕过回流。

H52-H56 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增奶产交互安全门禁检查 8 条规则，语言节点 1479、English -> ChineseSimplified parity keys 551。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加事件/信使交互链整改：

- H57：`IncidentWorker_MooGirl_CourierRaid` 使用 `GenerateQuestAndMakeAvailable(root, points)` 生成信使 quest，只传 points 不传 incident 目标 map；多地图存档中 quest root 会回退到 `AnyPlayerHomeMap`，可能在非 storyteller 目标地图生成信使。现改为显式创建 `Slate`，同时写入 `points` 和 `map`。
- H58：`JobDriver_TalkCourier` 直接强转 `TargetA`，异常 job 或跨 mod 错目标可能红字；信使右键菜单点击时也不复查目标、预约和路径。现交谈菜单、job 预约、等待和最终弹窗共用 `CanNegotiateWithCourier`，点击 action 重新 `CanReserveAndReach` 后才派 job。
- H59：信使对话框按钮捕获打开窗口时的 courier，若窗口打开期间信使已离开、死亡、进入敌对 mental 或货物已被解决，按钮仍可能继续执行“丢物离开/开战”。现两个按钮回调都重新 `CanTalkToCourier`，陈旧窗口只给出已解决提示。
- H60：运货员日记阅读菜单点击时不复查预约/路径，阅读 job 预约和最终打开窗口时也没有统一确认目标仍是可读日记；`Dialog_ReadBook` 还保留未使用的 `book` 字段。现菜单和 job 共用 `CanReadNow`，点击 action 重新 `CanReserveAndReach`，并删除无用字段。
- H61：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增事件交互安全门禁，覆盖信使目标强转、incident map slate、陈旧对话框回调、日记阅读点击复查和无用日记字段回流。

H57-H61 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增事件交互安全门禁检查 7 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加骑乘菜单与小型 AI job 整改：

- H62：骑乘 gizmo 已修复点击时重解析，但右键菜单仍捕获生成菜单时的 `Comp_MooGirlMount`，自目标下骑、他人命令下骑和骑乘命令在 UI 帧间状态变化后可能作用到旧状态；现右键菜单 action 点击时重新读取 comp、骑手状态、预约和路径。
- H63：`JobDriver_MountMooGirl` / `JobDriver_DismountMooGirl` 使用 `LocalTargetInfo.Pawn` 快捷属性，预约阶段未确认目标仍是有效 Pawn、可骑乘或仍有骑手；现改为从 `Thing` 安全解析 Pawn，并在预约和等待阶段复查当前 mount/dismount 条件。
- H64：`JobDriver_Nuzzle` 直接强转 `CurJob.targetA`，且 job giver 选择目标时不检查预约，目标被其他 job 占用或被跨 mod 错目标调用时可能红字或排队后立刻失败；现 nuzzle job 预约目标、执行前后共用 `CanNuzzle`，目标选择跳过不可预约候选。
- H65：`docs/tools/Invoke-Phase6StaticValidation.ps1` 扩展骑乘状态安全门禁并新增 misc job 安全门禁，覆盖骑乘右键菜单旧 comp 捕获、mount/dismount job 目标快捷读取、nuzzle 目标强转和无预约回流。

H62-H65 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；骑乘状态安全门禁扩展至 9 条规则，新增 misc job 安全门禁检查 3 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加能力 job 与放牧链整改：

- H66：`JobDriver_CastCharge` 预约阶段直接 `Reserve(job.targetA, job)`，并在 toil 构建时捕获目标 Pawn；异常 job、跨 mod 调用或 UI 帧间目标变化时可能让冲锋 hediff 留到下一 tick 依赖 comp 自清。现冲锋 job 使用当前 `TargetPawn` 安全解析，预约和执行阶段复查目标非自身、仍生成、未死亡且同地图，并用 `AddFinishAction` 保证所有结束路径都清理冲锋 hediff。
- H67：`CompAbilityEffect_ForceJob` 对范围内敌人直接 `StopAll` / `StartJob`，缺少共享目标验证、`jobs`/`mindState` 判空和平方距离检查；现统一 `CanForceJobOn`，跳过无 job tracker、死亡/倒地/非同地图/非敌对目标，用 `DistanceToSquared` 避免范围内循环开方，并在写入敌人目标前判空。
- H68：`Harmony_MooGirlGrazeChain` 在全局 `JobDriver.Cleanup` 后对 MooGirl 植物进食成功进行 9999f 全图搜索，且会覆盖玩家强制进食、草稿状态和已有 job 队列。现放牧链只在非玩家强制、非草稿、非 mental、无队列且未吃饱时触发，并将下一株植物搜索半径限制为 30 格。
- H69：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增 ability job safety 门禁，覆盖冲锋裸预约/陈旧目标捕获、放牧全图搜索、冲锋 hediff finish 清理、强制 job 共享校验和放牧链边界回流。

H66-H69 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 ability job safety 门禁检查 6 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 Harmony 边界整改：

- H70：`SkillRecord.Learn` 与 `Pawn_AgeTracker.GrowthPointsPerDay` 全局 patch 默认 MooGirl nurture trait/hediff def 一定存在；若发布包裁剪、跨版本 Def 缺失或热重载异常，可能在学习/成长热路径把 null 传入原版查询。现先缓存并判空 `NurturedTraitDef`、`NurtureAfterglowDef`、`MotherlyNurtureDef` 和 pawn `HediffSet`。
- H71：`Harmony_RopingTick`、`Harmony_RopingDraw` 与 milking render patch 读取原版私有字段时没有完整反射失败回退；若 RimWorld 小版本或其他补丁改变字段，可能在绳索 tick/绘制或 pawn 渲染热路径红字。现私有字段缺失时交回原版或跳过自定义视觉。
- H72：野人/招募/新生儿全局 patch 对异常输入仍有旧式空值假设；现 `IsWildMan` 先判 `p`，非玩家招募换 kind 前确认 `MooGirl_PreEscapeWildSlave` 存在，新生儿视觉 patch 确认 `RaceProps`、`ageTracker` 与 `story` 可用。
- H73：`docs/tools/Invoke-Phase6StaticValidation.ps1` 新增 Harmony boundary safety 门禁，覆盖全局学习/成长 patch 的缺 Def 回退、绳索/渲染反射回退、野奴与新生儿全局 patch 空值边界。

H70-H73 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 Harmony boundary safety 门禁检查 7 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加反射与 thought hediff 热路径整改：

- H74：`HediffComp_WhileHavingThoughts` 先用 `DefDatabase<HediffDef>.GetNamed(..., false)` 检查，再用 `HediffDef.Named(...)` 重新硬查找；tick 中还假设 pawn 一定有 mood memory tracker，并对同一个 memory 重复查询。现改为一次 `GetNamedSilentFail`，无 mood/memories 时清理 hediff，移除 thought 时只查询一次 memory。
- H75：`Harmony_MooGirlNurturedSkillLearnCap`、`MountedPawnMeleeSupport` 与 `MeleeAnimationCompat` 仍有反射值硬强转或私有方法调用异常可能；现学习 cap 字段和 melee animation 设置字段都做类型检查，骑乘近战私有命中/闪避/音效/cooldown helper 失败时回退默认值，不让 tick 路径红字。
- H76：`docs/tools/Invoke-Phase6StaticValidation.ps1` 扩展 Harmony boundary safety 门禁，新增 thought hediff hard lookup、mood memory 边界、mounted melee 反射 fallback、melee animation 类型检查和全源码直接反射强转扫描。

H74-H76 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Harmony boundary safety 门禁扩展至 11 条规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加空壳源码与结构卫生整改：

- H77：`Features/Milk/Harmony_BreastfedGrowthPoints.cs` 只剩一个空 `Harmony_BreastfedGrowthPoints` 类，生产代码无引用，csproj 仍编译该文件。现删除该空壳文件并移除 csproj 编译项，避免审计者误判存在未接入的 Harmony patch。
- H78：源码中仍允许少数“空但有语义”的类型：`CompAdultContentControl` 是 XML comp 兼容外壳，`BrainWashSlaveApparel` 是 `thingClass` 和破解器逻辑依赖的标记类型。现 Phase 6 新增空生产类型扫描，只允许这两个显式标记类型，其他空 `class/struct/interface` 会失败；同时把 `BrainWashSlaveApparel` 注释改为 XML marker 语义，避免“当前为空实现”的模糊说明继续留下挑刺点。

H77-H78 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；编译项从 147 降为 146，空生产标记类型为 2。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加信使事件残留路径整改：

- H79：`IncidentWorker_MooGirl_CourierRaid` 已在 H57 传入 incident 目标 map，但 `MooGirlStoryState.TickCourierRaid` 仍用 `GenerateQuestAndMakeAvailable(root, points)` 旧入口。多地图存档中，故事定时触发的信使任务会与 storyteller incident 触发路径产生不同落点语义。现故事定时触发也显式创建 slate，写入 `points` 和解析后的目标 `map`，并在派系或地图暂不可用时按日重试，不再把触发状态永久吞掉。
- H80：`QuestPart_CourierDemand` 没有被 quest root 加入，也没有有效 `SendDemand` 信号来源，只剩一段会全玩家基地计数/扣除 6000 钢铁和 300 零部件、失败则改敌对关系的死代码。现删除该 QuestPart 和对应 English/ChineseSimplified 孤儿 keyed 翻译，避免未接入设计继续污染审计面。
- H81：`docs/tools/Invoke-Phase6StaticValidation.ps1` 扩展事件交互安全门禁，检查 courier story timer 必须传 map slate、旧 demand 资源扣除类不能回流、`CourierDemand` 孤儿翻译键不能回流。

H79-H81 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；事件交互安全门禁扩展至 9 条规则，语言节点降为 1467、English -> ChineseSimplified parity keys 降为 545。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加奴隶库存与救援加入状态整改：

- H82：`StockGenerator_MooGirl_Slaves` 落在全局命名空间，XML patch 也用裸 `Class="StockGenerator_MooGirl_Slaves"`；同时 `HandlesThingDef` 默认 `thingDef.race` 一定存在，库存生成时默认 `Find.Storyteller.difficulty` 一定存在。现将类纳入 `MooGirl` 命名空间，XML patch 改为 `MooGirl.StockGenerator_MooGirl_Slaves`，并补齐 `ThingDef`/race/difficulty 空安全。
- H83：救援加入工具和 `HediffComp_JoinWhenRescued` 默认 pawn 一定有 `mindState`、`health`、mood thoughts memories；`QuestPart_MooGirlRescueJoin` 默认信号和 `pawns` 列表一定非空。现对 destroyed pawn、缺 mindState、缺 health/memories、空信号和空 pawn 列表补防线，避免异常 pawn 或读档边界把救援 tick/quest signal 路径打红。
- H84：`docs/tools/Invoke-Phase6StaticValidation.ps1` 扩展事件交互安全门禁，锁住奴隶库存生成器命名空间与空安全、XML 全限定 Class、救援加入工具状态防线和 rescue quest part 空信号/空列表防线。

H82-H84 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；事件交互安全门禁扩展至 13 条规则，MooGirl XML type refs 增至 70。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

- H85：`MooGirl XML type references` 原本只交叉检查 `MooGirl.` 前缀类型，无法发现 XML 里把本 mod C# 类型写成短名的情况。现扩展 Phase 6：如果 XML 类型字段写了无命名空间短名，且该短名命中 `1.6/Source` 中的 C# 类型，就作为未全限定 MooGirl 类型引用失败；当前 `Unqualified MooGirl XML type refs` 为 0。

H85 追加验证结果：`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过，`Unqualified MooGirl XML type refs: 0`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 Def 查找边界整改：

- H86：`MooGirlRequiredDefs` 直接使用 `DefDatabase<T>.GetNamed(...)`，语义上确实是必需 Def，但缺失时异常信息不指向 MooGirl 的必需 Def 边界。现改为 `Required<T>` helper：内部用 `GetNamedSilentFail` 探测，缺失时抛出明确的 `MooGirl required {DefType} is missing: {defName}`。
- H87：Phase 6 新增 Def lookup safety 门禁，要求 `MooGirlRequiredDefs` 只能走 `Required<T>` helper、`MooGirlOptionalDefs` 只能走 silent-fail 路径，并扫描全源码禁止普通业务代码回退到裸 `DefDatabase.GetNamed(...)` 或 `GetNamed(..., true)`。

H86-H87 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 Def lookup safety rules 3 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加野奴驯服/生成边界整改：

- H88：`IncidentWorker_MooGirl_WildManWandersIn.TryExecuteWorker` 直接把 `parms.target` 强转为 `Map`，入口格查找也默认 `map.reachability` 存在，生成 pawn 后默认非空。现改为安全解析 Map，缺 reachability 或生成失败时安静返回 false。
- H89：`TameUtility.CanTame`、`Designator_Tame.CanDesignateThing` 和 `WildManUtility.IsWildMan` 分别重复判断逃亡野奴与玩家派系，且直接比较 `Faction.OfPlayer`。现新增 `MooGirlWildSlaveUtility.IsNonPlayerEscapeWildSlave`，三个入口共用同一套非玩家逃亡野奴判定；招募 postfix 也改用 `IsPlayerFaction`，刷新殖民者栏时允许 `Find.ColonistBar` 为空。
- H90：Phase 6 扩展事件交互与 Harmony 边界门禁，覆盖 wild slave incident 的 Map/reachability/pawn 防线，以及 tame/designator/wildman patch 的共享非玩家逃亡野奴判定。

H88-H90 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Incident interaction safety rules 扩展至 14，Harmony boundary safety rules 扩展至 12。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加流浪加入信件边界整改：

- H91：`QuestNode_Root_MooGirl_WandererJoin_WalkIn.SendLetter_NewTemp` 沿用 vanilla 的 AcceptJoiner 信件路径时，直接把 `LetterMaker.MakeLetter` 结果强转为 `ChoiceLetter_AcceptJoiner`。在其他 mod 改动 `LetterDefOf.AcceptJoiner` 的 `letterClass` 或异常 Def 状态下，这会把一个可降级的 UI 兼容问题变成任务生成红字。现改为先生成 `Letter`，再用 `as ChoiceLetter_AcceptJoiner` 做类型检查；正常路径继续设置 accept/reject signal、quest、override map 和 timeout，异常路径仅限频警告并发送 fallback letter。
- H92：Phase 6 扩展事件交互门禁，禁止 `(ChoiceLetter_AcceptJoiner)LetterMaker.MakeLetter` 回归，并要求该任务节点保留 `Letter` 中间变量、`as ChoiceLetter_AcceptJoiner`、空类型分支和 `MooGirlLog.WarningOnce` 降级提示。

H91-H92 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Incident interaction safety rules 扩展至 16。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 courier/refugee pawn 生成韧性整改：

- H93：`QuestPart_SpawnCourier.SpawnCourier` 在 `PawnGenerator.GeneratePawn` 后默认 courier、inventory、mindState 和 Spawn 都成功；`AddToInventory` 默认 `innerContainer` 与 `ThingMaker.MakeThing` 成功；fight 分支默认 `mentalStateHandler` 存在。现改为生成后先验证 courier、inventory 和 mindState，不可用时限频警告并丢弃临时 pawn；Spawn 后确认 `courier.Spawned`；库存写入保护 `innerContainer` 和空 Thing；fight 分支在改 lord/派系前确认 mentalState handler。
- H94：`QuestNode_Root_MooGirl_RefugeePodCrash.GeneratePawn` 的 downed 生成循环默认每次 `PawnGenerator` 都返回非空 pawn，且 10 次内一定能打倒。现抽出 `GenerateDownedPawn`，每轮清理失败 fallback，显式处理 null 生成、死亡 fallback、活体 fallback 和完全失败异常，避免异常生成状态下走到后续穿衣、入世界 pawn 或信件路径才红字。
- H95：Phase 6 扩展 courier 交互门禁，要求 courier 生成、库存载荷、Spawn 成功和 fight mindState 分支都有显式防线。
- H96：Phase 6 扩展 refugee pod 生成门禁，要求 downed 生成循环保留最大尝试数、null 生成处理、失败 pawn 丢弃和限频 fallback 日志。

H93-H96 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Incident interaction safety rules 扩展至 18。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 opening crash story map slate 整改：

- H97：`MooGirlStoryService.TickOpeningCrash` 仍用 `GenerateQuestAndMakeAvailable(MooGirl_SlaveOpeningPodCrash, OpeningCrashQuestPoints)` 的 points-only 入口，实际目标地图留给 quest root 再解析。多地图时可能落到非预期地图；无可用玩家地图时也会把失败推迟到 quest 生成内部。现与 courier story 入口一致，先解析玩家事件地图，无地图则按 retry tick 重试，有地图时通过 `Slate` 同时传入 `points` 与 `map`。
- H98：Phase 6 扩展 opening crash story 门禁，禁止回到 `OpeningCrashQuestPoints` points-only quest 调用，并要求 `MooGirl_SlaveOpeningPodCrash` 使用带 `map` 的 slate 入口。

H97-H98 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Incident interaction safety rules 扩展至 19。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 opening pod root 本地边界整改：

- H99：`QuestNode_Root_MooGirl_OpeningPodCrash.RunInt` 依赖 story 入口传入有效 `Slate`/`Map`，且默认 `map.Parent`、生成 pawn 数组和信件目标都完整。debug 触发或其他 mod 直接生成 quest 时仍可能绕过 story 入口。现 root 本地验证 quest/slate/map/map.Parent，生成 pawn 后逐个确认可用，drop pod quest part 失败时丢弃临时 pawn；信件发送改为只使用有效 pawn 列表，并保护空数组、空 pawn、死亡/销毁 pawn 和缺 ageTracker。
- H100：Phase 6 扩展 opening pod root 门禁，要求保留 quest/slate/map/pawns/letter target 本地防线，避免只依赖 story 入口。

H99-H100 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Incident interaction safety rules 扩展至 20。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加玩家派系判断与死工具类整改：

- H101：`MooGirl_FactionUtility` 已无任何调用者，却仍被 csproj 编译，并保留直接 `Faction.OfPlayer` 敌对判断。现删除 `FactionUtility.cs` 并移除 csproj 编译项，Compile items 从 146 降至 145。
- H102：救援加入、救援 quest part、courier 交谈入口和 `HediffComp_JoinWhenRescued` 仍散落 `pawn.Faction == Faction.OfPlayer` / `!= Faction.OfPlayer` 等硬等值判断。现统一改为 `MooGirlWildSlaveUtility.IsPlayerFaction`，复用 `Faction.OfPlayerSilentFail` 边界，减少加载早期或异常派系状态下的直接依赖。
- H103：Phase 6 扩展门禁，禁止 `MooGirl_FactionUtility` 回归，并要求救援/信使/hediff tick 等高风险入口使用 `IsPlayerFaction`，不再直接等值比较 `Faction.OfPlayer`。

H101-H103 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Compile items 降至 145，Incident interaction safety rules 扩展至 21。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 158 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加跨模块玩家派系 helper 总闸整改：

- H104：全源码扫描发现能力强制 job AI 判断、束具 thought hediff、束具 mental-state hediff 仍直接等值比较 `Faction.OfPlayer`。现全部改为 `MooGirlWildSlaveUtility.IsPlayerFaction`；同时清理两个束具 hediff comp 的无用 `System.Collections.Generic`，并修复 mental-state hediff 先访问 `Pawn.Faction` 再判空、`mentalStateHandler` 未判空和无用局部变量的问题。
- H105：Phase 6 新增全源码 `Player faction helper safety` 总闸，禁止任何生产 C# 代码重新出现 `Faction.OfPlayer` 直接等值比较；事件局部门禁仍保留，用于约束高风险入口必须使用 helper。

H104-H105 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Player faction helper safety rules 新增 1 条，Compile items 保持 145，Incident interaction safety rules 保持 21。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 158 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加玩家敌对判断与束具 hediff 状态整改：

- H106：全源码仍有 `HostileTo(Faction.OfPlayer)` 和 `pawn.HostileTo(Faction.OfPlayer)` 裸敌对判断，覆盖 courier 派系选择、refugee/opening pod 信件文案和束具 trait hediff。现新增 `MooGirlWildSlaveUtility.IsHostileToPlayer`，统一走 `Faction.OfPlayerSilentFail`，并替换所有裸敌对判断。
- H107：`HediffComp_AddTrait` 在 tick 中默认 `pawnTypeTraitEntries`、entry、`pawnType`、`trait` 和 `Pawn.story.traits` 都有效；配置空项或跨 mod 生成异常 pawn 时可能红字。现跳过空 entry/trait/pawnType，抽出 `TryGainTrait` 保护 trait tracker，并保留 legacy `traitDef` fallback。
- H108：`HediffComp_ReduceWillorEnslave` 写成“到 tick 后触发”，但没有持久化 `triggered`，达到阈值后会每 tick 继续削意志或重复尝试奴役；同时直接访问 `Pawn.guest` 和 `Faction.OfPlayer`。现改为读档持久化的一次性触发，保护 pawn/guest/player faction，并用 `Faction.OfPlayerSilentFail` 执行奴役。
- H109：Phase 6 扩展玩家派系 helper 总闸：除直接等值和敌对判断外，生产 C# 中任何硬 `Faction.OfPlayer` 访问都会失败，保留 `Faction.OfPlayerSilentFail` 作为唯一可用边界。
- H110：Phase 6 新增束具 hediff 状态门禁，要求 `ReduceWillorEnslave` 保留持久化 `triggered`、`triggered || age < Props.triggerTicks` 一次性状态门和 `SetGuestStatus` 的非硬玩家派系入口。

H106-H110 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Player faction helper safety rules 扩展至 3，新增 Restraint hediff state safety rules 1 条，Compile items 保持 145，Incident interaction safety rules 保持 21。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 158 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加事件生成 pawn 生命周期整改：

- H111：courier、refugee pod、opening pod 各自复制 `Find.WorldPawns.Contains/RemovePawn/PassToWorld` 丢弃逻辑，且没有统一处理“误传已生成到地图的 pawn”边界。现新增 `MooGirlGeneratedPawnUtility`，集中提供 `TryPassToWorld` 与 `Discard`，对 null、Destroyed、Spawned pawn 做统一防线和限频警告。
- H112：`QuestPart_SpawnCourier.SpawnCourier` 在 `GenSpawn.Spawn` 后若 `courier.Spawned` 仍为 false，只写警告并保留 `courier` 字段；后续信号会因 `courier != null && !Destroyed` 直接返回，导致事件半卡死且临时 pawn 未丢弃。现 spawn 失败后统一丢弃并把 `courier = null`，允许后续安全重试。
- H113：`QuestNode_Root_MooGirl_RefugeePodCrash.SendLetter_NewTemp` 默认 pawn、ageTracker 均有效。debug/跨 mod 入口传入空、死亡或销毁 pawn 时会在信件构造红字。现发送前保护无效 pawn 并限频警告，未成年文本也改为先检查 `ageTracker`。
- H114：Phase 6 扩展事件交互门禁，要求 generated pawn 工具存在并保护 null/destroyed/spawned，opening/refugee/courier 三条事件路径必须走统一工具；courier spawn 失败路径必须清空 `courier` 字段。

H111-H114 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Compile items 增至 146，Incident interaction safety rules 扩展至 22。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加奶产倍率链路热路径整改：

- H115：`HediffComp_Lactation.ProductionMultiplier` 是奶产倍率 getter，可能从 tick、UI 说明或其他计算路径进入，但默认 `props`、Playing 状态、`Pawn.needs.food` 都可用。现改为安全转换 props，非 Playing 不计算产后窗口，饥饿惩罚读取使用 `pawn?.needs?.food`，并对倍率配置做非负夹取。
- H116：`CompMooMilkable.GetProductionMultiplier`、`CanProduceMilk`、`EnsureLactationHediff` 默认 pawn 的 `health.hediffSet`、`ageTracker.CurLifeStage` 和 `RaceProps` 都存在。现缺 tracker 时保留基础倍率或判定不可产奶，避免异常 pawn/跨 mod pawn 在热路径红字。
- H117：Phase 6 扩展奶产交互门禁，要求哺乳期倍率 getter 保留 props/Playing/food needs 防线，奶产 comp 保留 health/age/RaceProps 防线。

H115-H117 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Milk interaction safety rules 扩展至 10，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 Ghoul 渲染刷新全局缓存整改：

- H118：`Harmony_GhoulRenderingRefresh` 使用静态 `Dictionary<Pawn, int>` 延迟刷新 Ghoul 外观，但没有新游戏/读档清理入口；`Hediff.PostAdd` 也可能在非 Playing 状态下调度并硬取 `Find.TickManager`。现 GameComponent 构造时清空静态缓存，Notify/Tick 都避免在非 Playing 状态调度 TickManager，并跳过已销毁 pawn。
- H119：Phase 6 扩展 Harmony boundary 门禁，要求 Ghoul 渲染刷新保留静态缓存清理、非 Playing 防线和 destroyed pawn 防线。

H118-H119 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Harmony boundary safety rules 扩展至 13，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 Xenotype 出生/加载边界整改：

- H120：`MooGirl_XenotypeFix_GameComp` 使用静态 `tmpParents` 作为出生关系查询 scratch list，但旧逻辑没有 `finally` 清理；若关系查询或后续判断被其他 mod 异常打断，静态列表可能污染下一次出生判定。`ApplyXenotypeAndMissingEndogenes` 也默认 `pawn.genes`、目标 xenotype gene 列表和每个 `GeneDef` 均有效。现父母 scratch list 使用 `try/finally` 必清理，xenotype 写入前保护缺失 gene tracker/目标 gene 列表，并跳过空 `GeneDef`。
- H121：Phase 6 扩展 Harmony boundary 门禁，要求 Xenotype 出生/加载 patch 保留 `tmpParents` finally 清理、`targetXenotype.genes` 判空、空 gene 跳过和 `ModsConfig.BiotechActive` gate，防止全局出生 patch 的边界修复回流。

H120-H121 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Harmony boundary safety rules 扩展至 14，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加奶产/hediff 热路径边界整改：

- H122：`CompMilkingDeviceReleaseEffect` 对 `props` 使用硬强转，且 sound/fleck/text 列表条目默认非空；XML 或跨 mod 配置出现空 entry、负 start tick、反向 fleck speed range 或无效 scale 时可能在装备 tick 中红字。现改为 safe props，触发和 tick 入口缓存 props，schedule 对空条目写入禁用 sentinel，运行帧跳过空条目，并 clamp start tick、loop interval、fleck scale 与 speed min/max。
- H123：`CompMooHasBodyResource` 默认 `parent`、`Find.TickManager`、`doer`、`ResourceDef` 和采集双方同地图都有效；debug/dev、异常 job 或跨 mod 调用可在非 Playing 或错图上下文红字/错图生成产物。现 `Active` 判空 parent，资源 tick 只在 Playing 推进并通过 `CurrentGameTickOrFallback` 保护手动路径，采集入口显式校验 `ResourceDef`、doer、map 和双方同图，失败路径限频警告且不重置奶量。
- H124：`HediffComp_CureFoodEffects` 对 props 硬强转，tick 中默认 `Pawn.health.hediffSet` 可用；底层 `MooGirlFoodEffectUtility` 也默认 pawn health 完整。现 cure comp 使用 safe props 和 `TryGetRemoveHediffs`，food effect utility 在 remove/add 两条入口都先判 `pawn?.health?.hediffSet`。
- H125：`HediffComp_WhileHavingThoughts` 漏调 `base.CompExposeData()`，`checkingCounter` 不持久化，props 硬强转，tick 中默认 mood memories 与 health 可用。现补 base expose，持久化检查计数器，props safe cast，interval clamp，缺 memories 或无匹配 thought 时通过 `RemoveSelf` 判空移除。
- H126：`MooGirlMilkOutputUtility.SpawnStacksNear` 默认 `ThingDef`、`Map`、位置和 `stackLimit` 均有效；上层任何漏判都会放大成 ThingMaker/GenPlace 红字。现底层拒绝空 def、空 map、无效位置和非正数量，并将 stack limit clamp 到至少 1。
- H127：Phase 6 扩展奶产交互与 thought hediff 门禁，要求 release effect 保留 safe props/空 entry/fleck range 防线，body resource 保留 Playing/tick helper/同图采集防线，cure comp 与 food utility 保留 health 防线，milk output utility 保留无效 spawn 输入防线，thought hediff 保留 safe props、base expose、counter Scribe 与 memory tracker 防线。

H122-H127 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Milk interaction safety rules 扩展至 15，Harmony boundary safety rules 保持 14，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加束具 hediff 热路径整改：

- H128：`HediffComp_EleShock` 对 props 硬强转，默认 `shockSounds`、filth entry、Pawn/Map、Drawer jitterer 都有效；`hediffsToApplyWithParams` 和 `hediffsToRemove` 已在 XML 配置但代码未接线；filth timers 未 Scribe，读档后 filth 周期丢失。现改为 safe props，创建时播放音效、移除旧 hediff、应用配置 hediff、眩晕和初始化 filth timer；tick 中保护 Pawn/Map/jitterer/filth entry，按 `filthAmount` 生成污渍并持久化 timer。
- H129：`BrainwashPerformancePlayer` 与 `HediffComp_BrainWashingStar` 对 props 和 effect 列表条目仍默认有效，负 trigger/stun tick 或空 sound/fleck/text entry 可在洗脑演出热路径红字。现播放器初始化和补齐 timing list 时为空 entry 写禁用 sentinel，tick 时跳过空文本/声音/粒子，clamp stun duration，并将 comp Props 改为 safe cast。
- H130：束具 timed hediff 组件 `AddMentalState`、`AddThought`、`AddTrait`、`SuppressionEnhancer`、`ReduceWillorEnslave`、`DisappearsAndAddHediffs` 分散使用硬 Props 或默认 Pawn needs/health/map/trait entry 完整。现统一 safe props、trigger tick clamp、Pawn/need/map/body part 判空，并保留玩家派系 helper 与一次性 Scribe 状态。
- H131：`HediffComp_MooGirlNurtureProgress` 满进度检查默认 `parent` 和 `parent.def` 有效；异常 hediff 生命周期可在 tick 或手动完成路径红字。现将完成判断收敛到 `IsComplete`，读取 severity 上限前先保护 `parent?.def`。
- H132：Phase 6 扩展束具 hediff state 与奶产交互门禁，要求电击 hediff 保留配置接线、filth timer 持久化和 jitter/filth tick 防线，brainwash performance 保留空 entry 防线，timed hediff 保留 safe props/trigger/Pawn 边界，nurture progress 保留 parent/def 防线。

H128-H132 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Restraint hediff state safety rules 扩展至 8，Milk interaction safety rules 扩展至 16，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录，必须用 fresh RimWorld 启动日志复扫。

2026-06-07 已完成追加 ThingComp props 与装备按钮边界整改：

- H133：`Comp_MilkingDevice` 与 `CompMooMilkable` 仍保留 ThingComp `Props` 硬强转或同一流程重复取 props 的习惯；榨乳器存档字段若被异常写成负数、`maxCharges` 配成 0、释放图标缺失或释放/污物 Def 缺失，可能在检查面板、gizmo 或释放路径红字。现改为 safe props，产奶激活缓存 `milkProps`，保存键保留 fallback，榨乳器读档 clamp 储量/次数，`maxCharges` clamp 到至少 1，按钮图标走 fallback，释放/污物 Def 通过当前 props helper 查找并用 `MooGirlLog.WarningOnce` 限频。
- H134：`SlaveApparelExtensions.StartUnlockJob` 对 `CompProperties_Usable` 使用硬强转，异常 XML comp 或跨 mod use effect 可在创建解锁 job 时红字。现改为 safe cast，缺 `useJob` 时安静返回，并把该边界纳入束缚目标门禁。
- H135：洗脑头盔、电击项圈和磁力镣铐手动 gizmo 显示时计算过 wearer/props/cooldown，但点击时仍可能使用陈旧按钮对象；磁力镣铐解除路径还用 `?? new List<...>()` 在热路径分配临时列表。现三件装备均在点击时重新读取当前 props、当前 wearer、破解状态和冷却 tick，按钮进度 getter 动态读取冷却；图标使用 fallback helper，磁力镣铐解除空列表直接返回。
- H136：`CompReadableBook` 对 props 硬强转，菜单和阅读 job 打开窗口时默认标题配置存在；异常 XML 或热重载错配可让右键菜单红字。现 readable book comp safe-cast props，标题集中到 `BookTitle` fallback，菜单入口保护空 pawn/parent，job 打开窗口使用同一 fallback 标题。
- H137：`CompAbilityEffect_ForceJob` 和 `HediffComp_CheckJobOrRemove` 仍有 props 硬强转残留；能力释放和 hediff tick 在 XML 错配或异常父对象下可红字。现强制 job ability 缓存 `forceProps`、校验 `jobDef` 后再遍历目标，并把 props 传入启动 job；检查 job hediff safe-cast props、保护 `parent?.pawn`，移除自身时走 `pawn.health?.RemoveHediff(parent)`。
- H138：`Comp_MooGirlMount` 的 props 硬强转会把骑乘绘制、初始化和 interval tick 绑死在 XML 正确性上；改成 safe props 后，`MountedPawnUtility.OffsetForRot` 与 mounted combat cooldown 也必须容忍空 props。现骑乘 comp safe-cast props，缺 props 时跳过 mounted tick work，绘制偏移回退零偏移，tick interval 全部 clamp；mounted combat cooldown 使用 `TurretTickInterval` helper 和默认 60 tick。
- H139：全源码 `Props` 硬强转复扫后，剩余命中均为方法参数或嵌套类型名，不再是 `ThingComp`/`HediffComp` props 硬转入口；这批修复覆盖奶产、束具装备、日记、能力 job、检查 job hediff 与骑乘 comp。
- H140：Phase 6 扩展束缚目标、束具 hediff state、骑乘状态、奶产交互、事件交互和 ability job 门禁，锁住 safe props、当前按钮状态复查、safe icon、热路径零分配、骑乘 props fallback、readable book 标题 fallback 和 force/check job props 边界。

H133-H140 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Restraint target safety rules 扩展至 6，Restraint hediff state safety rules 扩展至 11，Mounting state safety rules 扩展至 12，Milk interaction safety rules 扩展至 17，Incident interaction safety rules 扩展至 23，Ability job safety rules 扩展至 7，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加婴儿喂养与榨乳动画边界整改：

- H141：`MooGirlMilkingAnimation` 在 Start/Tick、渲染矩阵、节点 transform 和 pulse envelope 中多处直接读取 `Find.TickManager.TicksGame`，只依赖调用方处于 Playing；渲染缓存或跨生命周期入口若在非 Playing 状态访问，可能让 transient 动画状态残留或红字。现统一通过 `TryGetCurrentGameTick` / `CurrentGameTickOrFallback` 读取 tick，非 Playing 时清理 transient 状态，渲染节点 tag 读取保护 `node?.Props`，反射读取继续使用 `PawnField?.GetValue`。
- H142：`Harmony_MooGirlBabyFeeding` 的 MooGirl 婴儿哺乳路径默认 `delta`、food max、milk fullness、nutritionPerFullness、feeder mindState 和 ideology tracker 都有效；异常 tick、极小营养值或跨 mod pawn 状态可能导致除零、过量填饱或空引用。现保护 `delta <= 0`、`food.MaxLevel <= 0`、负 fullness、非正营养换算、caravan mindState 和 ideo exposure，并将喂食后的食物等级 clamp 到 max。
- H143：Phase 6 扩展奶产交互与 Harmony boundary 门禁，要求 baby feeding 保留 Biotech gate、food/nutrition/mindState/ideo 防线，要求 milking animation 保留非 Playing tick helper、transient 状态清理、render node props 判空和 PawnRenderer 反射 fallback。

H141-H143 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Milk interaction safety rules 扩展至 18，Harmony boundary safety rules 保持 14，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加束具装备生命周期边界整改：

- H144：`SlaveApparel.Notify_Equipped` 默认 pawn 有 apparel tracker、health/hediffSet、RaceProps.body 和 body part 列表，且装备 hediff 不会重复叠加；异常 pawn、XML 空 body part entry 或重复装备回调可能红字或重复 hediff。现装备入口先保护 `pawn?.apparel`、`pawn.health?.hediffSet` 和 `pawn.RaceProps?.body?.AllParts`，跳过空 `BodyPartDef`，并在添加前用 `HasHediff(def.equipped_hediff, bodyPart)` 去重。
- H145：`SlaveApparel.Notify_Unequipped` 为移除装备 hediff 分配临时 `List<Hediff>`，并默认下一阶段 Def 一定是可穿 apparel；异常 `NextSlaveApparelDefs`、不可穿 Def 或无身体结构 pawn 可能触发原版 warning 或强转失败。现卸下时倒序原地移除匹配 hediff，不再分配临时列表；下一阶段装备通过 `CanAutoWearNextStage` 校验 `nextDef.IsApparel`、`RaceProps.body` 和 `ApparelUtility.HasPartsToWear`，`ThingMaker` 结果也必须是 `Apparel` 才穿戴，并直接用 `Wear(nextApparel, locked: true)` 保持锁定语义。
- H146：`AdvancedSlaveApparel.Crack` 破解后直接 `Find.Selector.Select(this.Wearer)`，非 Playing 或 selector 不可用时可能在调试/加载边界红字。现缓存 wearer，消息与选择使用同一当前 wearer，并统一通过 `MooGirlSelectionUtility.SelectInPlaying` 自动选中。Phase 6 新增 `Apparel lifecycle safety` 门禁，锁住上述 health/body/apparel 防线、hediff 去重、无临时列表移除、下一阶段穿戴校验和 selector gate。

H144-H146 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Apparel lifecycle safety rules` 1 条，Compile items 保持 146。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 159 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加热路径分配与全局 UI 选择边界整改：

- H147：`MooGirlApparelTagUtility.TryChooseAllowedApparel` 每个 apparel tag 都构造 `List<ThingDef>` 再 `RandomElement`，在 pawn 生成/补装路径会产生不必要分配；空 tag 也会白扫全部 ThingDef。现先拒绝空 tag，并用蓄水池抽样在单次扫描中保持候选等概率选择，避免临时候选列表分配。
- H148：`Patch_RopingTick.ForceUnrope` 在断绳前额外 `new List<Pawn>(tracker.Ropees)`，而原版 `DropRopes` 内部已经会复制 ropee 列表；长期牵引 tick 的异常断绳路径会产生重复分配，且 follow job 收尾默认 owner/jobs/CurJob 有效。现断绳前倒序遍历当前 `tracker.Ropees` 收尾自定义 follow job，保护 owner、jobs 和 `CurJob`，不再额外 snapshot。
- H149：骑乘、骑手 gizmo、冲刺落地 reselect 和束具破解各自直接访问 `Find.Selector`，有的只检查 Playing、有的完全未检查 selector；加载边界、测试上下文或 UI 单例不可用时会红字。现新增 `MooGirlSelectionUtility`，集中提供 `IsSelectedInPlaying`、`SelectInPlaying` 与 `ReselectIfSelectedInPlaying`，相关路径全部走同一个 no-op 安全入口。
- H150：Phase 6 扩展牵引、骑乘、ability、束具生命周期和 apparel generation 门禁，锁住零候选列表分配、牵引断绳无额外 ropee snapshot、全局 selector 只在统一 helper 内直接访问，以及冲刺/骑乘/束具选择路径不再散落 `Find.Selector`。

H147-H150 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Compile items 增至 147，Roping target safety rules 扩展至 5，Mounting state safety rules 扩展至 13，Ability job safety rules 扩展至 8，新增 Apparel generation safety rules 1 条，Apparel lifecycle safety rules 保持 1。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 160 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加事件/任务全局入口边界整改：

- H151：日记阅读、快递员接触/对话、救援加入、坠舱和流浪者加入路径直接调用 `Find.WindowStack` / `Find.LetterStack`；`LetterStack.ReceiveLetter` 内部还会访问 TickManager 与 Archive。加载、测试 runner、非 Playing UI 或异常 quest 生命周期下可能红字。现新增 `MooGirlGameUtility`，集中提供 `TryAddWindow` 与 `TryReceiveLetter`，检查 window、letterDef、letterStack、tickManager 和 archive，不可用时限频警告并 no-op。
- H152：快递员生成和战斗响应直接 `Find.TickManager.slower.SignalForceNormalSpeedShort()`，快递员 quest 结束直接 `Find.QuestManager.QuestsListForReading`；缺游戏上下文或 questManager 时会红字。现改为 `TrySignalForceNormalSpeedShort` 与 `TryGetQuestsListForReading`，正常状态行为不变，异常上下文直接返回。
- H153：生成 pawn 的 world storage/discard helper 直接访问 `Find.WorldPawns`，并在 spawned pawn 的 discard warning 后仍可能继续丢入 WorldPawns。现 WorldPawns 操作统一走 `MooGirlGameUtility`，并保证 spawned pawn discard warning 后立即返回，避免把已在地图上的 pawn 误塞进世界存储。
- H154：Phase 6 扩展事件交互门禁，要求 `MooGirlGameUtility` 保护 UI、letter、tick、archive、quest 和 WorldPawns 上下文；同时禁止 `Features/Incidents` 重新散落 `Find.WindowStack`、`Find.LetterStack`、`Find.QuestManager`、`Find.TickManager`、`Find.WorldPawns` 直接入口。

H151-H154 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Compile items 增至 148，Incident interaction safety rules 扩展至 25，其他 Phase 6 规则计数保持通过。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加事件派系与玩家事件地图解析边界整改：

- H155：快递员 incident、快递员 quest root 和故事状态计时器直接调用 `Find.FactionManager.FirstFactionOfDef`；`Find.FactionManager` 会经由 World 访问，测试、加载或世界组件未完成初始化时可红字。现新增 `MooGirlGameUtility.TryGetFirstFactionOfDef`，统一检查 faction def、Game、World 和 factionManager 后再解析派系。
- H156：快递员 quest root 与故事状态计时器各自直接读取 `Find.AnyPlayerHomeMap` / `Find.Maps`，并复制“无玩家家园地图时取第一张地图”的 fallback；异常游戏上下文或 maps 列表为空时边界不一致。现新增 `MooGirlGameUtility.TryResolvePlayerEventMap`，统一优先玩家家园地图，随后扫描当前 game maps 中第一张可用地图。
- H157：快递员 incident 与故事状态触发任务时以前只校验派系或地图的一半，容易在缺派系/缺地图时进入重试或 quest 生成不一致状态。现 incident、quest root 和 story timer 都通过同一派系/map helper 共同判定，缺任一条件则跳过或延后重试。
- H158：Phase 6 事件交互门禁扩展到派系和玩家事件地图入口，要求 `MooGirlGameUtility` 保护 factionManager、AnyPlayerHomeMap 和 Maps 上下文，并禁止 `Features/Incidents` 重新散落 `Find.FactionManager`、`Find.AnyPlayerHomeMap`、`Find.Maps` 直接入口。

H155-H158 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Compile items 保持 148，Incident interaction safety rules 保持 25，事件派系/map 入口门禁通过。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加剩余 tick/window 全局入口边界整改：

- H159：束具解锁二级菜单仍直接构造 `FloatMenu` 后进入全局窗口栈，和事件/任务窗口入口的安全语义不一致；现改为 `MooGirlGameUtility.TryAddWindow(new FloatMenu(options))`，非 Playing、缺 UI 或异常 runner 下 no-op/限频警告，不再裸调 `Find.WindowStack`。
- H160：`MapRopingIndex.MapComponentTick` 用 `Find.TickManager.TicksGame % 250 == 0` 驱动低频重建；地图组件自身已经每 tick 调用，直接读取全局 TickManager 会让测试、加载边界和非标准 tick 上下文多一个硬依赖。现改为组件本地 `rebuildTickCounter`，通过 `MooGirlTickUtility.Add` / `ConsumeReady` 每 250 tick 重建一次。
- H161：`MountedPawnCombatTurret` 用 `Find.TickManager.TicksGame` 记录 mounted ranged cast start 和 last attack tick；若战斗 helper 在异常状态、测试上下文或非 Playing 边界被调用，时间戳读取会红字或污染冷却状态。现统一通过 `MooGirlTickUtility.CurrentGameTickOrFallback`，不可读当前游戏 tick 时保留原字段值。
- H162：`HediffComp_Lactation` 的产后倍率与出生通知直接读取 TickManager，和 H141 后建立的 tick helper 方向不一致；现 `ProductionMultiplier` 与 `NotifyBirth` 都通过 `MooGirlTickUtility.TryGetCurrentGameTick`，非 Playing 时只跳过产后窗口/出生记录，不影响基础倍率和饥饿惩罚。
- H163：Phase 6 扩展束缚目标、牵引、骑乘和奶产门禁，锁住上述 `TryAddWindow`、地图本地重建计数、mounted ranged tick fallback 与 lactation tick helper；其中 `HediffComp_Lactation` 的门禁从“本地检查 Playing”更新为“必须走 `MooGirlTickUtility`”。

H159-H163 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；Restraint target safety rules 扩展至 7，Roping target safety rules 扩展至 6，Mounting state safety rules 扩展至 14，Milk interaction safety rules 保持 18 但 lactation tick 规则已改为要求 `MooGirlTickUtility`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加全源码 TickManager 集中入口整改：

- H164：`MountedPawnMeleeSupport.SetLastShotTick`、`GhoulRenderingRefreshUtility`、`CompMooHasBodyResource` 和 `MooGirlMilkingAnimation` 仍有直接或局部封装的 `Find.TickManager` 读取；这些路径都可能处于战斗 tick、渲染缓存、GameComponent tick 或采集热路径，一旦测试/加载/非 Playing 边界进入就会绕过统一防线。现全部改走 `MooGirlTickUtility.TryGetCurrentGameTick` 或 `CurrentGameTickOrFallback`，不可读 tick 时保留原状态或安全跳过。
- H165：洗脑头盔、电击项圈和磁力镣铐各自保留本地 `CurrentGameTickOrFallback`，内部重复直接检查 `Current.ProgramState` 与 `Find.TickManager`；这会让同类手动冷却按钮有多个实现口径。现三个本地 helper 只委托给 `MooGirlTickUtility.CurrentGameTickOrFallback`，按钮冷却、点击时复查和读档恢复继续保持原语义。
- H166：Phase 6 新增 `Tick manager access safety` 总闸：要求 `MooGirlTickUtility` 保留 ProgramState/TickManager 判空，并扫描 `1.6/Source` 中除该 helper 外不得出现 `Find.TickManager`；同时更新榨乳动画与 Ghoul 刷新门禁，不再要求模块本地 tick 判断，而是要求统一 helper。

H164-H166 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Tick manager access safety rules` 新增 2 条，`rg -n "Find\.TickManager" .\1.6\Source` 只剩 `MooGirlTickUtility.cs` 内部 2 处集中出口。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加业务层 `Find.*` 全局入口整改：

- H167：全源码复扫后，业务层仍剩 `MooGirlWildSlaveUtility` 直接刷新 `Find.ColonistBar`、奴隶库存生成器直接读取 `Find.Storyteller.difficulty`、两处束具菜单直接比较 `Find.CurrentMap`。这些入口目前已有空安全，但仍把 UI/game 单例访问散在功能模块中。现新增 `MooGirlGameUtility.ChildrenAllowedByCurrentDifficulty`、`IsCurrentMap` 与 `TryMarkColonistsDirty`，对应调用全部改走统一 helper，行为语义保持不变。
- H168：Phase 6 新增 `Find access safety` 总闸：仅允许 `MooGirlGameUtility`、`MooGirlSelectionUtility`、`MooGirlTickUtility` 三个核心 helper 直接访问 `Find.*`；其他生产 C# 发现 `Find.` 立即失败。同时更新奴隶库存和 game utility 门禁，要求库存生成读取难度必须走 `MooGirlGameUtility`，game utility 必须继续提供当前地图、殖民者栏和 storyteller 难度边界。

H167-H168 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Find access safety rules` 新增 2 条，`rg -n "\bFind\." .\1.6\Source` 只剩三个核心 helper 内部的集中出口。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加 `Current.Game` / `ProgramState` 入口整改：

- H169：`MooGirlStoryService`、Ghoul 刷新和奶资源 tick 仍直接判断 `Current.ProgramState == ProgramState.Playing`，结构坠舱 incident 还直接 `Current.Game.GetComponent<MooGirlStoryState>()`。这些入口安全性尚可，但和前面建立的全局入口 helper 口径不一致。现新增 `MooGirlGameUtility.IsPlaying` 与 `TryGetGameComponent<T>`，相关业务路径全部改走 helper。
- H170：`GameComponent_BrainwashPerformance.StartFor` 在运行时找不到组件时会手动 `new GameComponent_BrainwashPerformance(Current.Game)` 并 `Current.Game.components.Add(comp)`。原版 `Game.FillComponents()` 会自动实例化所有非抽象 `GameComponent`，运行时手动插入组件既冗余又容易被审查为生命周期污染。现组件缺失时限频警告并跳过演出，不再动态改写 `Current.Game.components`。
- H171：Phase 6 新增 `Current game access safety` 总闸：要求 `MooGirlGameUtility` 提供 Playing 与 GameComponent helper；除 `MooGirlGameUtility`、`MooGirlSelectionUtility`、`MooGirlTickUtility` 外，生产源码不得直接访问 `Current.Game` / `Current.ProgramState`，且禁止任何运行时 `components.Add` 回流。

H169-H171 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Current game access safety rules` 新增 3 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加动态 administer milk recipe 整改：

- H172：`Patch_DrugAdministerDefs.Postfix` 直接 `new List<RecipeDef>(__result)`，若原版或其他 mod 的 postfix 把结果置空会在加载期 NRE；`ContainsRecipe` 也默认列表项永远非空。现对 `__result == null` 回退空列表，并在重复检测时跳过 null recipe entry。
- H173：`MooGirl_Milk` 若被 XML/补丁改成非 ingestible，旧逻辑仍会用 250 tick 与 humanlike fallback 生成 administering recipe，语义上把不可食用物伪装成可给药物。现缺 `ingestible` 时限频警告并不生成动态配方；热重载复用旧 `RecipeDef` 时同时清空 `defaultIngredientFilter`，确保 `RecipeDef.ResolveReferences()` 重新从新的 `fixedIngredientFilter` 拷贝默认过滤器。
- H174：Phase 6 新增 `Dynamic recipe safety` 门禁：要求动态配方注入保留 null result/null recipe 防线、非 ingestible 不生成、热重载重置 ingredients/fixed/default filters/recipeUsers、`recipeUsers` 只覆盖 MooGirl race/body，并禁止回到原版式 `race.IsFlesh` 扩散扫描。

H172-H174 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Dynamic recipe safety rules` 4 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加 MooGirl 身份判断入口整改：

- H175：`Thought_MooGirlOnly`、`RopingService`、新生儿视觉修正和动态 administer milk recipe 各自手写 `MooGirlBody` / `body.defName` 判断，同一个“谁算 MooGirl”的规则散在多个模块里。现 `MooGirlIdentity` 增加 Pawn 与 ThingDef 两层 `IsMooGirlDef`、`HasMooGirlBody`、`IsMooGirlPawnDef` helper，相关调用全部改走集中入口，保留历史“ThingDef 或 MooGirl body 都算目标”的兼容边界。
- H176：`Thought_MooGirlOnly` 在非 MooGirl 或异常 pawn 上移除自身时仍硬访问 `pawn.needs.mood.thoughts.memories`，`WorkGiver_GatherBodyResources` 也默认调用者有 map/mapPawns 且目标 `RaceProps` 完整。现移除 memory 时全链路空安全，采集 WorkGiver 对 `pawn.Map.mapPawns`、目标 Humanlike/RaceProps 和 MooGirl body 判定都显式防护。
- H177：Phase 6 新增 `MooGirl identity safety` 门禁：要求 `MooGirlIdentity` 提供 Pawn/ThingDef 双层身份 helper，禁止功能模块重新直接引用 `MooGirl_DefOf.MooGirlBody` 或 `body.defName == "MooGirlBody"`，并锁住 thought、牵引、采集、新生儿和动态 recipe 的集中入口调用。

H175-H177 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `MooGirl identity safety rules` 3 条，同时 Harmony newborn 旧门禁已更新为要求 `MooGirlIdentity.HasMooGirlBody(__result)`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加 PawnGenerator 生成后束具锁定入口整改：

- H178：`PawnGenerator_GeneratePawn_Patch.Postfix` 直接遍历 `__result.apparel.WornApparel` 并锁定 `SlaveApparel`，作为全局 Pawn 生成后处理入口，副作用逻辑过于隐式，且默认 worn apparel 列表和条目都完整。现抽为 `SlaveApparelExtensions.LockGeneratedSlaveApparel(this Pawn pawn)`，由 helper 统一处理 pawn/apparel/list/null item 边界。
- H179：生成后自动锁定束具的旧循环每次遇到 `SlaveApparel` 都调用 `Lock`，虽然原版 `Lock` 内部会防重复，但审计上看不出本 mod 自身是否避免重复 locked-apparel entry。现 helper 在锁定前显式检查 `!pawn.apparel.IsLocked(apparel)`，并保持只锁本 mod `SlaveApparel`，不扩展到普通服装或其他 mod apparel。
- H180：Phase 6 新增 `Generated apparel lock safety` 门禁：要求 PawnGenerator 手动 postfix 只委托共享 helper，helper 必须保留 pawn/apparel/WornApparel 防线、索引遍历、`SlaveApparel` 限定和重复锁定防线，同时锁住 `MooGirlPatchRegistry` 中 `PawnGenerator.GeneratePawn(PawnGenerationRequest)` 的手动注册入口。

H178-H180 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Generated apparel lock safety rules` 3 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加手动 Harmony patch 注册失败边界整改：

行为变更说明：手动 Harmony patch 目标和 patch 方法存在、但 `harmony.Patch(...)` 本身抛异常时，mod 不再让该异常向上传播并中断 `MooGirlBootstrap.Initialize()`；会输出一次本地化 warning 并继续后续加载。成功 patch 列表只记录真正应用成功的手动 patch。

- H181：`MooGirlPatchRegistry.TryPatch` 只检查 `target == null` 与 `prefix/postfix == null`，真正的 `harmony.Patch(...)` 裸露在初始化链路中。若 Harmony 因签名漂移、反射异常或跨 mod patch 冲突抛出异常，会把整个手动 patch 注册乃至 mod 初始化带崩。现将 `harmony.Patch(...)` 包入 `try/catch (Exception ex)`，失败时限频输出 `MooGirl.PatchRegistry.PatchFailed` warning 并继续。
- H182：旧代码虽然把 `manualPatchNames.Add(nameKey)` 放在 `harmony.Patch(...)` 之后，但没有显式成功路径边界和失败日志，审计者必须依赖异常中断来推断“失败不会入已注册列表”，玩家也无法从本地化日志知道哪个手动 patch 失败。现成功记录被封在 `try` 的 `harmony.Patch(...)` 之后，失败路径只记录一次 warning，不污染 `ManualPatchNames`。
- H183：Phase 6 新增 `Manual patch registry safety` 门禁：要求手动 Harmony patch 注册器捕获 `Exception ex`、保留失败详情、本地化 `MooGirl.PatchRegistry.PatchFailed` 文案、`PatchRegistry.PatchFailed.` 限频 key，且 `manualPatchNames.Add(nameKey)` 必须处在 `harmony.Patch(...)` 成功路径之后；同时锁住英中语言 key 与占位符一致性。

H181-H183 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Manual patch registry safety rules` 2 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加 Harmony bootstrap 粗粒度 `PatchAll()` 整改：

行为变更说明：特性标注的 Harmony patch 不再通过一个粗粒度 `Harmony.PatchAll()` 批量注册。启动时改为枚举本程序集内带 `[HarmonyPatch]` 的 patch 类，并逐类 `CreateClassProcessor(patchClass).Patch()`；某个 patch 类因目标签名漂移、属性读取异常或 Harmony 应用失败而跳过时，只输出一次本地化 warning，不再阻断其它 patch 类和手动 patch 注册。

- H184：`MooGirlBootstrap.Initialize()` 直接调用 `Harmony.PatchAll()`，任何一个特性 patch 类的目标方法、`TargetMethod()`、静态字段初始化或 Harmony 应用流程失败，都可能把整批 patch 和后续手动 patch 注册一起中断。现改为 `RegisterAttributePatches(Harmony)`，逐类发现、逐类应用、逐类记录成功 patch 类名。
- H185：逐类注册如果只处理 `Patch()` 失败，仍可能被 `Assembly.GetTypes()`、`ReflectionTypeLoadException.Types == null` 或单个类型读取 `[HarmonyPatch]` 属性时的异常打断。现 `GetHarmonyPatchTypes()` 对 `ReflectionTypeLoadException` 降级为可加载类型列表，类型数组为空时回退空数组；`HasHarmonyPatchAttribute(Type type)` 单独捕获属性读取异常并跳过该类型。
- H186：Phase 6 新增 `Harmony bootstrap safety` 门禁：禁止 bootstrap 退回 `.PatchAll(`；要求保留逐类 `CreateClassProcessor(patchClass).Patch()`、发现/属性/应用三段降级 warning、成功类名只在 patch 成功后追加，并锁住 `MooGirl.Bootstrap.HarmonyPatchFailed` 英中文案与占位符一致性。

H184-H186 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Harmony bootstrap safety rules` 2 条。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加 refugee/opening pod 生成失败硬异常整改：

行为变更说明：开局逃生舱和普通逃生舱的 pawn 生成彻底失败时，任务节点不再抛出 `InvalidOperationException` 交给 `QuestNode.Run()` 记录红字。现在会输出一次本地化 warning，并在生成链本地返回/退出；已生成但不可用的临时 pawn 仍走统一 discard 清理。

- H187：`QuestNode_Root_MooGirl_OpeningPodCrash.GenerateFactionlessPawn` 在 20 次仍无法生成无派系可战斗 pawn 后调用 `MooGirlLog.Error` 并 `throw new InvalidOperationException`。这把 PawnKindDef 漂移、装备补丁冲突或其它 mod 修改生成器造成的事件失败升级成任务运行异常。现改为 `WarningOnce("OpeningPodCrashFactionlessGenerationFailed", ...)` 并返回 `null`，由已有 `RunInt` pawn 可用性检查清理并退出。
- H188：`QuestNode_Root_MooGirl_RefugeePodCrash.GenerateDownedPawn` 在 10 次生成失败且没有存活 fallback 时同样硬抛异常；但它继承的原版 `QuestNode_Root_WandererJoin.RunInt()` 默认 `GeneratePawn()` 永远非空，不能只把失败改成 `null`。现 refugee root 增加本地 `RunInt()`，先验证 quest/slate/map，再验证生成出的 pawn；失败时限频 warning、discard 后退出，成功路径保留原版救援信号、tended/recruited/left/killed 结算和 left-map 后续逻辑。
- H189：Phase 6 扩展 `Incident interaction safety` 门禁：要求 opening/refugee pod 生成失败路径都使用 `WarningOnce`/`return null` 降级，不得回归 `throw new` 或 `MooGirlLog.Error`；refugee pod 还必须保留本地 `RunInt()` 的 quest/slate/map/pawn 防线与英中日志 key。

H187-H189 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Incident interaction safety rules` 保持 25 条但扩展了 opening/refugee pod 生成失败硬异常门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；这不是本轮改动后的 fresh 日志，必须重新启动 RimWorld 后复扫 fresh `Player.log`，游戏内验证仍未执行。

2026-06-07 已完成追加文本格式化失败降级整改：

行为变更说明：`MooGirlText.Resolve(string, params NamedArgument[])` 处理 XML 文本或非翻译键文本时，如果 `{0}` 等占位符与传入参数不匹配，不再写 `Log.ErrorOnce` 红字；现在会输出一次带异常摘要的 MooGirl warning，并返回原始文本作为 fallback。

- H190：`MooGirlText.Resolve` 的 `string.Format` 失败属于配置/翻译文本降级问题，旧代码直接 `Verse.Log.ErrorOnce`，会把一个可回退的按钮/提示文本问题升级成红字。现改为 `MooGirlLog.WarningOnce("Text.FormatFailed." + hash, ...)`，日志中只保留异常类型与消息，并继续返回原始文本。
- H191：Phase 6 的直接 `Verse.Log` 门禁此前仍把 `MooGirlText.cs` 列入豁免，导致以后可能重新绕开 `MooGirlLog`。现只允许 `MooGirlLog.cs` 直接访问 `Verse.Log`。
- H192：Phase 6 新增 `Text formatting safety` 门禁，要求格式化失败必须捕获 `Exception ex`、使用 `MooGirlLog.WarningOnce`、保留稳定限频 key、返回原始文本，且禁止 `Log.ErrorOnce` / `Log.Error` 回归。

H190-H192 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；新增 `Text formatting safety rules` 1 条，直接 `Verse.Log` 豁免已收窄到 `MooGirlLog.cs`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前 XML 已确认使用存在的 `Pawn_Melee_Punch_HitBuilding_Generic`，当前语言文件也已通过 Phase 6 重复 key/parity/keyed 引用扫描，但仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭日志阻断项。

2026-06-07 已完成追加骑乘作战 tracker 边界整改：

行为变更说明：正常 pawn 的骑乘、远程炮位、近战支援和武器绘制数值不变。异常 pawn 若缺少 health capacities、meleeVerbs、hediffSet 或 ageTracker/lifeStage，旧逻辑可能在入口、tick 或绘制阶段红字；现在会在骑乘资格/作战可用性检查中返回不可用，近战命中部位缺失时交给 `DamageInfo` 无指定部位处理，武器绘制距离系数回退为原版 `LifeStageDef` 默认值 `1f`。

- H193：`MountEligibilityService`、`MountedCombatController.CanUseMountedRangedWeapon` 和 `MountedPawnMeleeSupport.CanRiderMelee` 直接访问 `pawn.health.capacities` 或调用原版 `Awake()`，异常生成 pawn、跨 mod 临时 pawn 或损坏 tracker 会在菜单/自动下骑/骑乘作战 tick 中红字。现新增 `MountedPawnUtility.HasCapacity` / `IsAwake` / `IsHumanlike`，入口和热路径统一走 null-safe helper。
- H194：骑乘近战热路径直接调用 `rider.meleeVerbs.TryGetMeleeVerb(target)` 和 `pawnTarget.health.hediffSet.GetRandomNotMissingPart(...)`，异常 rider 或目标 pawn 缺 tracker 时可能在每次 melee tick 红字。现改为 `MountedPawnUtility.TryGetMeleeVerb` 与 `GetRandomNotMissingPart`，缺 tracker 时跳过攻击或使用无指定部位伤害。
- H195：骑乘远程/近战绘制直接读取 `rider.ageTracker.CurLifeStage.equipmentDrawDistanceFactor`，渲染阶段若 pawn 生命周期 tracker 不完整会打断绘制。现集中为 `MountedPawnUtility.EquipmentDrawDistanceFactor`，缺 lifeStage 时回退 `1f`；Phase 6 扩展 `Mounting state safety` 门禁，禁止 `Features/Mounting` 中除 helper 外回归裸 health capacity、melee verb、hediffSet、lifeStage 和 `RaceProps.Humanlike` 访问。

H193-H195 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Mounting state safety rules` 扩展至 16，新增骑乘 tracker/lifeStage 集中 helper 与裸访问回归扫描。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加牵引 pending spot 状态生命周期整改：

行为变更说明：成功拴到墙绳扣、普通牵引、解除牵引的玩家可见行为不变。改变的是中断/失败/换图/反生成等边界：`pendingSpotRope` 现在被视为 map-local 临时缓存，必须在原 map 和当前 map 上都能清理，并在低频重建时裁掉已失效或已变成真实定点绳状态的条目。

- H196：`JobDriver_RopeToWallRopeHitch` 在断开原绳索后才 `MarkPendingSpotRope(ropee)`，但 finish action 只通过 `ropee.Map` 清当前 map 索引；如果 ropee 在 job 中断前换图、反生成或被其它 mod 移走，旧 map 的 `pendingSpotRope` 会残留。现记录 `pendingSpotRopeMap`，finish/fail/success 全部调用本地 `ClearPendingSpotRope`，优先清原 map，再清当前 map fallback。
- H197：`MapRopingIndex.RebuildFromMap()` 只重建真实 pawn rope / spot rope 缓存，没有审计 `pendingSpotRope` 这种临时状态；长期运行后无效 pawn、换图 pawn 或已经成功 `IsRopedToSpot` 的 pawn 仍可能留在 pending set。现新增 `PruneInvalidPendingSpotRopes()`，重建时用临时列表无枚举修改风险地裁掉失效 pending，并在 `RegisterPawnRope` / `RegisterRopedToSpot` 时主动移除对应 pending。
- H198：Phase 6 扩展 `Roping target safety` 门禁，要求 pending spot-rope cache 必须有原 map 清理入口、rope-to-hitch job 必须记录并清理 `pendingSpotRopeMap`，`MapRopingIndex` 必须在 rebuild/register 路径裁剪 pending，避免旧 map 临时状态回归。

H196-H198 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Roping target safety rules` 扩展至 8，新增 pending spot-rope 原 map 清理与重建裁剪门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加榨乳动画静态状态缓存整改：

行为变更说明：正常榨乳动画的节奏、姿态、脉冲和喷溅效果不变。改变的是静态渲染状态缓存的边界：`thingIDNumber` 命中字典后必须确认缓存中的 pawn 引用就是当前 pawn；双人动画的 partner 失效时立即清掉双方状态；pawn 生命周期结束时复用临时 key 列表，不再为每次清理临时分配 `List<int>`。

- H199：`MooGirlMilkingAnimation.states` 以 `thingIDNumber` 为 key，但 `EnsureState` / `TryGetState` / `RemoveIfMatches` 旧逻辑只看 key、role 和 partner，未确认 `state.pawn == pawn`。跨新游戏、读档或异常 pawn ID 复用时，旧 pawn 状态可能短暂污染当前 pawn 的渲染缓存判断。现写入、读取和移除路径都要求 `state.pawn` 与当前 pawn 一致，不一致时重建或丢弃状态。
- H200：双人榨乳动画只在当前 pawn 生命周期结束或 stale tick 时清理当前 key；若 partner 先反生成/换图，另一侧状态可能继续影响绘制到 stale 窗口结束。现 `StateNeedsPartner(state) && !Valid(state.partner)` 时清掉 partner 侧匹配状态并移除当前状态；`NotifyPawnLifecycleEnded` 改用 `tmpStateKeysToRemove` 复用列表，减少生命周期清理路径分配。
- H201：Phase 6 扩展 `Harmony boundary safety` 中的榨乳动画门禁，要求保留 `state.pawn` 一致性检查、partner 失效清理、`tmpStateKeysToRemove` 复用、集中 tick helper、反射 fallback 和 render node props 判空。

H199-H201 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Harmony boundary safety rules` 保持 14 条但扩展了榨乳动画 `state.pawn` 一致性、partner 失效清理和临时 key 列表复用门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加牵引 UI 热路径与束具破解器反馈边界整改：

行为变更说明：被牵引或定点拴住的 MooGirl 仍会禁用征召按钮，玩家可见禁用原因不变；改变的是 `GetGizmos` 后处理不再为每次 UI 枚举构造临时 `List<Gizmo>`。束具破解器的成功、失败和“不适用目标”反馈从开发日志改为原版玩家消息，未知可破解类型仍保留限频警告作为 XML/继承漂移告警。

- H202：`Harmony_PawnDraftController.GetGizmosPostfix` 为了禁用 Draft gizmo 先复制 `__result` 到新列表，属于 UI 高频枚举路径上的无意义分配；且入口没有把 `__instance.pawn` 判空写成显式边界。现改为 `DisableDraftGizmo` 惰性迭代器，枚举时原地禁用 Draft toggle 并 `yield return gizmo`，入口通过 `Pawn pawn = __instance?.pawn` 统一处理异常 controller。
- H203：`CompTargetCrackBondageGear` 将“目标不可用、破解成功、无可破解类型”等玩家操作反馈写入 `MooGirlLog.Message` 或 warning/log，正常交互会污染开发日志，也让玩家得不到 RimWorld 原生消息反馈。现所有玩家可见结果走 `Messages.Message` 和合适的 `MessageTypeDef`，消息目标集中到 `MessageTarget(user, fallback)`；只有未知 apparel 子类仍保留 `MooGirlLog.WarningOnce`。
- H204：Phase 6 扩展 `Roping target safety` 与 `Restraint target safety` 门禁，要求牵引征召 gizmo patch 保留惰性枚举并禁止 `new List<Gizmo>` 回归，要求束具破解器反馈必须使用 `Messages.Message`、保留未知类型限频告警且不得回退裸日志输出。

H202-H204 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Restraint target safety rules` 扩展至 8，`Roping target safety rules` 扩展至 9，新增束具破解器反馈边界与牵引征召 gizmo 热路径分配门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加电击束具 hediff 热路径分配整改：

行为变更说明：电击束具的音效、眩晕、抖动、hediff 添加/移除和污物生成节奏不变。改变的是内部 tick 实现：污物计时同步不再每 tick 创建临时 `HashSet` / `List`，抖动效果不再每 tick 通过 Harmony `Traverse` 重新查私有字段，污物计时字典更新也减少重复查表。

- H205：`HediffComp_EleShock.EnsureFilthTimers()` 在 `CompPostTick` 每 tick 调用时都会创建 `HashSet<ThingDef>` 和 `List<ThingDef>`，多个电击 hediff 长跑时会制造无意义 GC 压力。现改为复用实例级 `tmpStaleFilthDefs`，并用 `IsConfiguredFilthDef` 直接扫描配置项确认有效污物 Def。
- H206：`HediffComp_EleShock.TryAddJitter()` 每个抖动 tick 都 `Traverse.Create(pawn.Drawer).Field<JitterHandler>("jitterer")`，热路径反射包装过重且失败边界分散。现改为静态 `FieldInfo JittererField`，tick 内只做一次 null-safe `GetValue`；`TickFilth` 也改为 `TryGetValue(..., out int ticksUntilFilth)`，避免 `ContainsKey` 后二次索引。
- H207：Phase 6 扩展 `Restraint hediff state safety` 门禁，要求 EleShock 保留 `tmpStaleFilthDefs`、`IsConfiguredFilthDef`、静态 jitter 字段读取和 `TryGetValue` 计时更新，禁止 `EnsureFilthTimers` 回归 tick-time `HashSet` / `List` 分配或 `Traverse.Create(pawn.Drawer)`。

H205-H207 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Restraint hediff state safety rules` 保持 11 条但扩展了 EleShock 热路径零分配与静态 jitter 反射门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加普通信息日志边界整改：

行为变更说明：幼年 MooGirl 体型自动修正逻辑仍会在新游戏、读档和初始化后执行，修正行为不变。改变的是日志可见性：普通玩家环境不再因为成功自修复输出 `[MooGirl] Corrected juvenile...` 信息日志；只有 DevMode 下才保留这类诊断信息。警告和错误日志语义不变。

- H208：`MooGirlStoryState.NormalizeJuvenileGraphics()` 在正常加载/读档自修复后调用 `MooGirlLog.Message` 输出修正数量。这个信息对开发诊断有用，但对普通玩家是日志噪音，且容易被 fresh load 审查误判为“加载期仍有可疑输出”。现改为 `MooGirlLog.DevMessage`，只在 `Prefs.DevMode` 下输出。
- H209：`MooGirlLog` 暴露通用 `Message` 入口，语义过宽，后续玩家操作反馈或普通自修复统计很容易重新写进开发日志。现删除普通 `Message` 入口，改为带 DevMode gate 的 `DevMessage`；业务反馈继续要求走 `Messages.Message`，异常配置/降级继续走 warning once。
- H210：Phase 6 扩展直接日志门禁，要求 `MooGirlLog` 的普通信息输出必须通过 `DevMessage` 和 `Prefs.DevMode` gate，禁止业务代码调用 `MooGirlLog.Message` 或重新暴露普通 `Message` wrapper。

H208-H210 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Direct Verse.Log calls` 门禁扩展了 `MooGirlLog.DevMessage` 与业务代码 `MooGirlLog.Message` 禁用检查。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加同进程复测静态状态重置整改：

行为变更说明：警告内容、食物效果 hediff 移除/刷新逻辑和榨乳动画/骑乘瞬态状态行为不变。改变的是新游戏、读档和初始化后的静态状态边界：once-warning key 集合和食物效果 hediff Def 缓存都会随 `MooGirlStoryState` 的 transient reset 一起清理，避免同一 RimWorld 进程内多轮验证互相污染结果。

- H211：`MooGirlLog.WarningOnce` 的 `warnedKeys` 是静态集合，旧逻辑没有新游戏/读档重置入口；同一进程中第一轮触发过的告警会压掉第二轮 fresh 验证的同 key 告警。现新增 `MooGirlLog.ResetOnceWarnings()` 并在 `ResetTransientRuntimeState()` 中调用。
- H212：`MooGirlFoodEffectUtility.HediffDefCache` 会缓存 XML 字符串到 `HediffDef` 或 null 的解析结果。缓存本身可接受，但缺少重置入口时，Dev 热重载、同进程验证或异常 Def 初始化顺序会让旧解析结果影响后续场景。现新增 `ResetDefCache()` 并纳入同一 transient reset。
- H213：Phase 6 扩展日志、奶产交互和事件交互门禁，要求 `MooGirlLog` 暴露 once-warning reset、食物 hediff Def cache 暴露 reset，并要求 `MooGirlStoryState.ResetTransientRuntimeState()` 同时清日志 once keys、食物 Def cache、榨乳动画状态和骑乘作战瞬态状态。

H211-H213 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Incident interaction safety rules` 扩展至 26，新增 transient reset 门禁，日志与奶产交互门禁扩展了 once-warning 与 hediff Def cache reset 检查。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加非限频 Warning 日志收束整改：

行为变更说明：Courier raid 缺 map、缺派系、缺生成格，Courier spawn part 缺 map/派系/生成格，以及束具解锁目标在等待后消失时，降级行为不变，仍会跳过当前生成/解锁路径并保留警告诊断。改变的是日志频率：这些可重复进入的异常边界不再每次重试/信号/Job 执行都写一条普通 warning，而是使用稳定 key 的 `WarningOnce`。

- H214：`QuestNode_Root_MooGirl_CourierRaid.RunInt()` 在缺 map、缺巨型企业派系或找不到 pawn 入口格时直接 `MooGirlLog.Warning`。Quest root 可能被 debug、story retry 或跨 mod 条件反复触发，旧逻辑会刷同类 warning。现改为 `CourierRaid.Root.*` key 的 `WarningOnce`。
- H215：`QuestPart_SpawnCourier.SpawnCourier()` 在重复信号、读档后异常状态或 spawnCell 重新解析失败时直接 `MooGirlLog.Warning`；`JobDriver_UnlockSlaveApparelGear` 等待后目标束具消失也直接 warning。现分别改为 `CourierRaid.Spawn.*` 与 `UnlockSlaveApparel.TargetMissing` 的 `WarningOnce`。
- H216：Phase 6 扩展直接日志门禁，要求 `MooGirlLog.Warning` 只能作为私有 wrapper 被 `WarningOnce` 调用，业务代码禁止直接调用非限频 warning；同时保留 `DevMessage`、`ResetOnceWarnings` 和 `WarningOnce` 语义门禁。

H214-H216 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Direct Verse.Log calls` 门禁扩展为禁止业务代码 `MooGirlLog.Warning`，并要求 `MooGirlLog.Warning` 仅为私有实现。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加红字日志入口删除整改：

行为变更说明：没有玩家可见行为变化。删除的是未使用的 `MooGirlLog.Error` 红字 wrapper，保留 `WarningOnce` 降级诊断和 `DevMessage` 开发信息语义；后续业务代码若需要记录可恢复异常，必须继续使用限频 warning 或玩家消息，而不是重新暴露红字入口。

- H217：`MooGirlLog.Error` 当前未被使用，但公开为 `internal static` 入口后，后续维护很容易把可降级配置问题、跨 mod 边界或玩家操作失败重新升级成 RimWorld 红字日志。现删除该 wrapper，让日志工具只保留 DevMode message、私有 warning 实现、`WarningOnce` 与 transient reset。
- H218：日志边界在 H208-H216 已收束普通信息和 warning，但红字入口本身没有自动防回归保护；仅靠代码审查容易在后续功能中重新加入 `Log.Error` 包装或业务侧 `MooGirlLog.Error` 调用。
- H219：Phase 6 扩展 `Direct Verse.Log calls` 门禁，禁止 `MooGirlLog` 内出现 `Log.Error` 或 `internal static void Error(...)`，并禁止业务代码调用 `MooGirlLog.Error(...)`。后续如确实需要红字，必须先有明确发布阻断级理由并修改门禁说明。

H217-H219 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Direct Verse.Log calls` 门禁扩展为禁止 `MooGirlLog.Error` wrapper、`MooGirlLog` 内 `Log.Error` 和业务代码 `MooGirlLog.Error`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加日志限频误用与玩家反馈历史噪音整改：

行为变更说明：正常日志文案和玩家操作反馈文本不变。改变的是防御边界：未来误传空白 `WarningOnce` key 时会按调用成员和行号生成稳定兜底 key，空白 warning message 会输出明确占位文本；快递员已处理、破解器无效目标/已破解、解锁菜单无锁定束具这类瞬时拒绝反馈仍会弹出，但不再写入历史消息。

- H220：`MooGirlLog.WarningOnce` 直接使用调用方传入 key。当前业务调用都传稳定 key，但 API 本身没有防御空 key、空白 key 或空 message；未来误用会把不相关告警压成同一条，或写出只有 `[MooGirl]` 前缀的无意义 warning。现对 key 做 `IsNullOrWhiteSpace` 与 `Trim()`，空白 key 用 `CallerMemberName` + `CallerLineNumber` 生成稳定兜底 key，空白 message 回退为 `Unspecified warning.`；同时避免 `CallerFilePath`，不把本机源码路径编进发布 DLL。
- H221：快递员 stale dialog/重复按钮的 `CourierAlreadyResolved`，束具破解器地面目标的 `AlreadyCracked` / `InvalidTarget`，以及解锁菜单没有锁定束具时的 `NotWearingLockedApparel` 都是玩家短反馈。旧逻辑默认 `historical: true`，重复点击会污染 Archive。现全部显式 `historical: false`，保留即时反馈，不留历史噪音。
- H222：Phase 6 扩展日志与玩家反馈门禁，要求 `WarningOnce` 保留空白 key/message 兜底、禁止 `CallerFilePath`；`Restraint target safety` 要求目标无效/已破解/未穿锁定束具反馈不进历史；`Incident interaction safety` 要求快递员已处理拒绝反馈不进历史。

H220-H222 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Direct Verse.Log calls` 扩展了 `WarningOnce` 空白 key/message 兜底与 `CallerFilePath` 禁用检查，`Restraint target safety rules` 扩展至 10，`Incident interaction safety rules` 扩展至 27。额外复扫 `MooGirlLog.WarningOnce(null|""|string.Empty)` 无命中。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 quest signal 重入边界整改：

行为变更说明：正常 courier raid 生成、救援加入和招募完成流程不变。改变的是异常/重复信号边界：空 `inSignal` 不再能触发快递员生成；救援 pawn 已经加入玩家派系后，重复 rescued signal 不再重新写入 `WillJoinColonyIfRescued` 状态。

- H223：`QuestPart_SpawnCourier.Notify_QuestSignalReceived()` 只比较 `signal.tag == inSignal`。正常 quest 会赋值，但 debug、坏档或跨 mod 构造出空 `inSignal` 时，空 tag signal 可以误触发生成逻辑。现加 `!string.IsNullOrEmpty(inSignal)`，空信号配置直接忽略。
- H224：`QuestPart_MooGirlRescueJoin.TryJoinRescuedPawn()` 在检查 pawn 是否已是玩家派系前先调用 `PrepareRescueJoinPawn()`；重复 rescued signal 命中已加入 pawn 时，会把 `mindState.WillJoinColonyIfRescued` 再次设为 true，然后立刻返回。现先识别玩家派系 pawn 并返回，再对未加入 pawn 设置救援加入准备状态。
- H225：Phase 6 扩展事件交互门禁，要求 courier spawn quest part 显式忽略空 `inSignal`，并要求 rescue join quest part 在写 `WillJoinColonyIfRescued` 前先跳过已加入玩家派系的 pawn，防止重复 signal 污染状态。

H223-H225 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Incident interaction safety rules` 扩展至 29，新增 courier spawn 空信号与 rescue join 重入状态门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 Ghoul 渲染刷新静态状态 reset 收束：

行为变更说明：Ghoul hediff 添加后的外观刷新行为和刷新窗口不变。改变的是新游戏、读档和初始化后的静态状态清理口径：Ghoul 渲染刷新 pending 队列现在和 once-warning、食物 Def cache、榨乳动画、骑乘作战 transient 状态一起由 `MooGirlStoryState.ResetTransientRuntimeState()` 统一清理。

- H226：`GhoulRenderingRefreshUtility` 使用静态 `pendingRefreshUntilTick` 和 `tmpPawnsToRemove`。H118 已在自身 GameComponent 构造里清理，但 H211-H213 建立了中央 transient reset 口径后，Ghoul pending 队列仍游离在外；同一进程多轮验证或非标准初始化顺序下，静态状态边界不够统一。现将 `GhoulRenderingRefreshUtility.ClearPendingRefreshes()` 接入 `MooGirlStoryState.ResetTransientRuntimeState()`。
- H227：Ghoul 渲染刷新已有自己的 Harmony boundary 门禁，检查 centralized tick、非 Playing 跳过和本地 clear，但中央 reset 没有要求包含它，后续维护可能再次遗漏这类静态 pending 队列。
- H228：Phase 6 扩展事件交互 transient reset 门禁，除 once-warning、食物 Def cache、榨乳动画和骑乘作战状态外，还要求中央 reset 调用 `GhoulRenderingRefreshUtility.ClearPendingRefreshes()`。

H226-H228 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Incident interaction safety rules` 扩展至 30，新增 Ghoul pending refresh 中央 reset 门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 DevFill 失败反馈历史噪音整改：

行为变更说明：正常 DevFill 成功反馈和奶量填充行为不变。改变的是 DevMode 调试按钮失败路径：失败消息仍会即时弹出，但不再写入历史消息 Archive，避免反复点击无效调试按钮污染玩家消息记录。

- H229：`CompMooMilkable` 的 DevFill 失败路径是瞬时拒绝反馈，旧逻辑默认 `historical: true`。开发模式下反复点击无效 pawn 或异常状态时，会把 `MooGirl.Milk.DevFill.Failed` 写入历史消息，制造可避免的 Archive 噪音。现显式设置 `historical: false`。
- H230：`Comp_MilkingDevice` 的 DevFill 失败路径同样只是调试按钮拒绝反馈，旧逻辑默认写入历史消息。现将 `MooGirl.MilkingDevice.DevFill.Failed` 显式改为 `historical: false`，保留即时反馈但不留历史记录。
- H231：Phase 6 扩展奶产交互门禁，要求 `CompMooMilkable` 与 `Comp_MilkingDevice` 两处 DevFill reject feedback 均显式 `historical: false`，防止后续调试按钮或失败反馈回流历史消息。

H229-H231 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Milk interaction safety rules` 扩展至 19，新增两处 DevFill reject feedback 历史消息门禁。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加牵引入口侵入性边界收束：

行为变更说明：正常右键牵引 MooGirl、牵引到栓点和解除牵引流程不变；MooGirl 牵引菜单仍显示 100% 成功率。改变的是异常入口边界：debug、坏档或跨 mod 直接塞入 `JobDriver_RopeMoo` 时，非 MooGirl pawn 不再能进入本 mod 的牵引 job、预约、原版逮捕概率、拒绝牵引消息或狂暴反击分支。

- H232：`FloatMenuProvider_RopeMoo.TargetPawnValid()` 只给 MooGirl 目标显示牵引菜单，但 `RopingService.CanStartPawnRope()` 原本没有把目标必须是 MooGirl 作为服务层前置条件；异常 job 入口可以绕过菜单，把普通 pawn 带入本 mod 牵引系统。现 `CanStartPawnRope()` 先拒绝非 MooGirl ropee，`TryMakePreToilReservations()` 也先走同一服务层检查，避免先预约后失败。
- H233：`JobDriver_RopeMoo` 保留了普通 pawn 逮捕概率、`MessageRefusedRope`、`Notify_MemberCaptured`、`RopeRejected` 狂暴反击等分支。正常菜单路径下这些分支不可达，却扩大了跨 mod/job 注入时对原版 pawn 的侵入面。现删除普通 pawn 分支，只保留 MooGirl 成功牵引时需要的 `NotifyRopeAccepted()` 回调。
- H234：牵引菜单仍残留非 MooGirl 目标的 prisoner/slave 特判和 `GetAcceptArrestChance()` 成功率显示分支，与目标验证和服务层边界不一致。现菜单始终按 MooGirl 目标显示 100% 成功率；Phase 6 扩展牵引目标门禁，要求服务层拒绝非 MooGirl ropee，并禁止牵引 job/菜单重新出现普通 pawn 逮捕概率、拒绝牵引或狂暴分支。

H232-H234 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Roping target safety rules` 扩展至 12，新增服务层 MooGirl ropee 边界、牵引 job 普通 pawn 分支删除和菜单非 MooGirl 成功率分支删除门禁。额外复扫 `GetAcceptArrestChance`、`MessageRefusedRope`、`RopeRejected`、`MentalStateDefOf.Berserk`、`Notify_MemberCaptured`、`CheckAcceptRope`、`target.IsPrisonerOfColony`、`target.IsSlave` 在 `1.6\Source\Features\Roping` 下无命中。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 nurture Def 缓存生命周期收束：

行为变更说明：nurture 进度、完成、afterglow、trait 和技能热情提升数值不变。改变的是同进程复测、Dev XML 热重载和读档初始化边界：`MooGirlNurtureUtility` 的静态 Def 引用现在会随中央 transient reset 清空，后续访问重新从当前 `DefDatabase` 解析。

- H235：`MooGirlNurtureUtility` 使用静态 `motherlyNurtureDef`、`nurtureAfterglowDef` 和 `nurturedTraitDef` 缓存 DefDatabase 解析结果。正常游戏流程下可用，但和 H212 的食物效果 Def cache 一样，Dev 热重载、同进程多轮验证或异常 Def 初始化顺序下会持有旧 Def 引用。现新增 `ResetDefCache()`，清空三个静态 Def 缓存。
- H236：中央 `MooGirlStoryState.ResetTransientRuntimeState()` 已覆盖 once warning、食物效果 Def cache、榨乳动画、骑乘作战状态和 Ghoul pending 刷新，但没有纳入 nurture Def cache，静态缓存 reset 口径不完整。现将 `MooGirlNurtureUtility.ResetDefCache()` 接入同一入口。
- H237：Phase 6 扩展奶产交互与中央 transient reset 门禁，要求 nurture utility 暴露 Def cache reset hook，并要求 story game component 的中央 reset 实际调用它，防止新增静态 Def 缓存游离在 reset 之外。

H235-H237 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Milk interaction safety rules` 扩展至 20，新增 nurture static Def cache reset hook 门禁；`Incident interaction safety rules` 计数保持 30，但中央 transient reset 检查已要求调用 `MooGirlNurtureUtility.ResetDefCache()`。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加旧 `PawnBool` 伪兼容 shim 清理：

行为变更说明：原版奴隶、囚犯、倒地 pawn 的束具钥匙菜单行为不变；显示名仍优先使用短姓名，缺失时回退 label。改变的是未接线的 Simple Slavery hediff 兼容入口：旧 `PawnBool.SimpleSlaveryIsActive` / `PawnBool.Enslaved` 在本 mod 内没有任何初始化路径，实际默认不生效；现删除这组公开可变静态字段和 lower-case helper，避免把一个虚假的兼容层暴露成公共状态写入口。

- H238：`PawnBool` 公开 `SimpleSlaveryIsActive` 与 `Enslaved` 两个可变静态字段，但本 mod 没有初始化它们，也没有可选 mod 依赖或 Def 解析入口；`is_modslave()` 默认永远返回 false。该代码看起来像 Simple Slavery 兼容，实际是未接线死路径，还增加了跨 mod 外部写状态的侵入面。现删除 `PawnBool.cs`，改为内部 `PawnSlaveStatusUtility`。
- H239：旧 helper 使用 `get_pawnname`、`is_slave`、`is_vanillaslave`、`is_modslave` 等 lower-case 方法名，且 `is_vanillaslave(Pawn pawn)` 直接访问 `pawn.IsSlave`。现改为 `DisplayName(Pawn pawn)` 与 `IsSlave(Pawn pawn)`，显示名和奴隶判断均为空安全，调用点更新到 `CompSlaveApparelGear` 与 `CompStampedApparelKey`。
- H240：Phase 6 扩展束具目标门禁，要求旧 `PawnBool` 文件保持删除，`PawnSlaveStatusUtility` 保持 internal/null-safe，并禁止 `SimpleSlaveryIsActive`、`Enslaved`、`is_slave`、`get_pawnname` 等旧 shim 名称回流到束具菜单代码。

H238-H240 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Restraint target safety rules` 扩展至 11，新增旧 `PawnBool` shim 删除与 `PawnSlaveStatusUtility` 调用门禁。额外复扫旧符号 `PawnBool`、`SimpleSlaveryIsActive`、`is_slave`、`is_vanillaslave`、`is_modslave`、`get_pawnname`、`HediffDef Enslaved`，源码中无业务命中，仅剩 Phase 6 禁用规则。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 mod 入口公开状态与旧式 helper 命名收束：

行为变更说明：设置窗口、结构坠落事件开关和快速榨乳开关语义不变；Harmony 初始化仍由 `MooGirlBootstrap` 负责。改变的是 API 暴露面：`MooGirlMod` 不再公开可变 `harmony` / `settings` 字段，程序集内代码通过只读 `Settings` 属性读取配置；stamped key 菜单标签 helper 改为 PascalCase。

- H241：`MooGirlMod.harmony` 只是复制 `MooGirlBootstrap.Harmony`，本 mod 内没有调用者，却以 public static mutable 字段暴露 Harmony 实例，扩大外部可写状态面。现删除该字段，mod 入口只调用 `MooGirlBootstrap.Initialize()`，Harmony 继续由 bootstrap 持有。
- H242：`MooGirlMod.settings` 原本是 public static mutable 字段，故事计时和榨乳 job 直接读取该字段。现改为 private static 字段加 internal `Settings` 只读属性，调用点更新到 `MooGirlMod.Settings`，避免外部直接替换设置对象。
- H243：`CompStampedApparelKey.make_label()` 保留旧式 lower-case 方法名，与本轮清理后的 helper 命名口径不一致。现改为 `MakeLabel()`；Phase 6 扩展 Harmony bootstrap 与束具目标门禁，禁止 `MooGirlMod.settings/harmony`、public static Harmony/settings 字段和 `make_label` 回流。

H241-H243 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Harmony bootstrap safety rules` 扩展至 3，新增 mod 入口公开状态门禁；`Restraint target safety rules` 扩展至 12，新增 stamped key `MakeLabel` 命名门禁。额外复扫旧业务符号 `MooGirlMod.settings`、`MooGirlMod.harmony`、`public static Harmony`、`public static MooGirlSettings`、`make_label` 无源码业务命中；`using HarmonyLib` 仅保留在实际 Harmony patch/registry 文件。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

2026-06-07 已完成追加 Def 查询规范与 Xenotype scratch 状态收束：

行为变更说明：动态 administer milk recipe 的生成结果、热重载复用、recipeUsers 范围和 MooGirl 新生儿 xenotype 修正语义不变。改变的是加载期查询和静态临时容器边界：动态 recipe 热重载复用现在显式走 `GetNamedSilentFail`；Xenotype 出生关系查询的静态父母 scratch list 不再能被 `ref` API 重绑，若 RimWorld API 返回了不同局部列表也会在 `finally` 中清理。

- H244：`Patch_DrugAdministerDefs.CreateAdministerMilkRecipe` 在 hotReload 路径使用 `DefDatabase<RecipeDef>.GetNamed(defName, false)`。该调用不会红字，但和 H86/H87 建立的“普通业务代码只使用 silent-fail 查询”口径不一致，后续审查还需要区分 `false` 与真正 hard lookup。现改为 `DefDatabase<RecipeDef>.GetNamedSilentFail(defName)`，缺失时再创建新的 `RecipeDef`。
- H245：Phase 6 的 Def lookup 扫描原先只禁止无 silent 参数或 `errorOnFail=true` 的 `DefDatabase.GetNamed`，仍允许 `GetNamed(..., false)` 漏回源码。现收紧为全源码禁止裸 `DefDatabase<...>.GetNamed(`，动态 recipe 门禁也要求 hotReload 使用 `GetNamedSilentFail(defName)`，让 Def 查询风格只剩必需 helper 与 silent-fail 两种可审计形态。
- H246：`MooGirlXenotypeService.tmpParents` 是出生关系查询用静态 scratch list，H120 已保证 `finally` 清理，但字段仍被直接作为 `ref` 参数传给 RimWorld 关系 API。理论上被调用方可通过 `ref` 重绑整个静态字段，破坏“只复用并清内容”的状态边界。现将字段改为 `private static readonly`，通过局部 `parents` 变量传 `ref`，避免静态字段被外部 API 替换。
- H247：`GetDirectRelations(..., ref parents)` 若未来或跨版本实现重绑了局部列表，原始 `tmpParents` 和局部返回列表都需要清理。现 `finally` 中在非同一引用时先清理局部 `parents`，再清理 `tmpParents`；Phase 6 的 Xenotype Harmony boundary 门禁同步要求 `tmpParents` readonly、局部 `parents` ref 用法、局部清理、`tmpParents.Clear()`、Biotech gate 和 gene list/null gene 防线同时存在。

H244-H247 追加验证结果：Release 构建通过，`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过；`Def lookup safety rules` 计数保持 3 但全源码 `DefDatabase.GetNamed` 禁令已收紧，`Dynamic recipe safety rules` 计数保持 4 但 hotReload 复用已要求 `GetNamedSilentFail`，`Harmony boundary safety rules` 计数保持 14 但 Xenotype scratch list 检查已要求 readonly 与局部 ref 清理。额外复扫 `DefDatabase<...>.GetNamed(` 无源码命中，精确复扫 private static 非 readonly 集合字段无命中。`New-WorkshopPackage.ps1 -SkipBuild` 通过，复制 877 个文件、排除 161 个文件。`New-GameValidationConfigs.ps1` 通过，生成 6 组验证配置。`git diff --check` 无 whitespace 错误，仅报告 Git 的 LF/CRLF 工作区提示。`Invoke-PlayerLogScan.ps1` 仍命中旧 `Player.log` 中的 `Pawn_Melee_Punch_HitBuilding` 和 Simplified Chinese translation report 旧记录；当前代码/静态校验未复现这两条，仍必须重新启动 RimWorld 后复扫 fresh `Player.log` 才能关闭最终日志阻断项。

## 执行原则

1. 先修会红字、破存档状态或影响全局行为的问题。
2. 再修生命周期、长期 tick、可选 DLC、跨 mod 兼容和性能问题。
3. 最后清理死代码、冗余配置、资源卫生和发布包噪音。
4. 每个问题必须有代码修改、验收标准和验证记录，不能只写“已知”。
5. 涉及玩家可见行为、数值、概率、tick 间隔、产量、伤害、Def 名称或翻译含义的修改，必须先写行为变更说明。
6. 不用“旧档兼容”当设计目标，但新档、当前 Def 引用和当前 Scribe 字段必须自洽。
7. 全局 Harmony patch 默认有罪；保留时必须证明边界、必要性和失败回退。

## 优先级定义

P0：发布阻断。缺失 DLC、加载、读档、长期运行或全局 patch 可能直接产生红字、状态损坏或跨 mod 污染。

P1：发布前必须完成。当前可能不红字，但有性能、设计漂移、配置无效、玩家功能失真或维护风险。

P2：最终发布包不得遗留。主要是冗余、风格、命名、资源卫生和小型重复逻辑。

## 阶段 0：锁定整改基线

目标：开始改代码前，让每个风险项都有可复查的原始位置和验证入口。

工作：

- 保存当前构建与静态验证结果。
- 对所有 P0/P1 文件记录当前行号、字段名、XML defName 和调用路径。
- 确认 `git status --short` 为空或只包含本计划文档。
- 建立整改检查表，后续每修一项都更新状态。

建议命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; git status --short
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; msbuild .\1.6\Source\MooGirlRace.csproj /p:Configuration=Release /v:minimal
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-Phase6StaticValidation.ps1
```

验收：

- 每个待修问题都能定位到文件和行。
- 没有把未验证的猜测写成事实。
- 后续修改可以逐项对照本计划回归。

## 阶段 1：修复加载与存档硬风险

### 1.1 Biotech DefOf 缺少 MayRequire

优先级：P0。

涉及文件：

- `1.6/Source/DefOf/MooGirl_DefOf.cs`
- `Bio_1.6/Defs/Hediffs_Gene.xml`
- `Bio_1.6/Defs/GoldenRaceBioTitle.xml`

问题：

- `MooGirl_Stun`、`MooGirl_Charge`、`MooGirl_Xenotype` 只在 Biotech 加载目录中定义。
- 主程序集 DefOf 字段无 DLC 条件标记时，未启用 Biotech 的最小组合可能在 DefOf 解析阶段报缺失。

计划：

- 给三个 Biotech-only DefOf 字段添加 `[MayRequire("Ludeon.RimWorld.Biotech")]`。
- 若后续 Biotech 字段继续增加，统一放入带 MayRequire 的区域，禁止散落在核心必需 DefOf 中。
- 检查是否存在其他 DLC-only 或 optional-mod-only DefOf 字段未标注。

验收：

- 不开 Biotech 的最小组合加载不出现 DefOf 缺失红字。
- 开 Biotech 的组合中三个字段仍能正常解析。
- `rg -n "MooGirl_Stun|MooGirl_Charge|MooGirl_Xenotype" 1.6 Bio_1.6` 的结果能解释所有引用。

### 1.2 BrainwashHelmet 计时器生命周期不完整

优先级：P0。

涉及文件：

- `1.6/Source/Features/Restraints/Comps/CompProperties_BrainwashHelmet.cs`

问题：

- `currentTicksToChange` 主要在 `PostSpawnSetup` 初始化。
- 穿戴中的 apparel、生成后直接装备、读档恢复等路径不一定会重新走同一初始化路径。
- `CompTick` 又依赖 `currentTicksToChange > 0`，因此计时器为 `0` 时可能永久不工作。
- 当前 Scribe 字段存在，但加载后不应被随机初始化覆盖。

计划：

- 增加一个私有初始化辅助方法，只在 `currentTicksToChange <= 0` 且不是加载覆盖场景时补默认随机值。
- 在 `PostPostMake`、装备通知或等价生命周期入口补初始化。
- `PostSpawnSetup` 中区分 `respawningAfterLoad`，读档时不重置已保存值。
- `PostExposeData` 在 `LoadSaveMode.PostLoadInit` 后只修复非法旧值，不覆盖合法保存值。

验收：

- 新生成后直接穿戴的头盔会正常倒计时。
- 地图上生成、商队转移、装备中读档后都不会卡在 `0`。
- 已保存的剩余时间不会在读档后被重新随机。

### 1.3 SlaveApparel isLocked 存档默认值不一致

优先级：P0。

涉及文件：

- `1.6/Source/Features/Restraints/Defs/SlaveApparelDef.cs`

问题：

- 字段默认值为 `true`。
- `Scribe_Values.Look(ref isLocked, "isLocked", false)` 的缺省值为 `false`。
- 缺失字段或早期新档对象可能被读成解锁，和类型默认语义相反。

计划：

- 将 Scribe 默认值改为 `true`，让未写入字段时保持类型默认行为。
- 若需要兼容已经生成但字段缺失的新档对象，在注释中写清“旧档不兼容，但当前新档默认锁定”。

验收：

- 新生成束具默认锁定。
- 读档缺少 `isLocked` 字段时不会变成默认解锁。
- 相关保存字段记录与 `docs/baseline/scribe-fields.md` 一致或补充说明。

## 阶段 2：补齐长期运行与功能语义

### 2.1 Mounted rider 手动 tick 不完整

优先级：P0。

涉及文件：

- `1.6/Source/Features/Mounting/Comp_MooGirlMount.cs`
- `1.6/Source/Features/Mounting/MountedPawnUtility.cs`

问题：

- 当前骑乘中手动调用 `MountedPawnUtility.PhysiologyTick`。
- 该方法只覆盖 health、needs、age、apparel、skills、genes 等局部 tracker。
- 与原版 `Pawn.TickInterval` 相比，长期骑乘会漏掉 mindState、carry、infection、comfort、caller、drafter、relations、psychic entropy、guest、ideo、royalty、style、learning、pollution/gas/toxic/vacuum、anomaly、records、guilt 等大量 tracker。
- 这类缺口短测不容易暴露，但长档和复杂 mod 组合会逐步累积状态漂移。

计划：

- 先明确设计目标：骑乘 pawn 是否应作为“暂停中实体”还是“仍完整生活的 pawn”。
- 如果应完整生活，优先寻找能复用原版 tick 的安全方式，避免手工维护 tracker 白名单。
- 如果应暂停部分行为，建立明确 allowlist，并在注释中说明哪些 tracker 故意不 tick、原因和玩家影响。
- 增加长时间骑乘验证场景，覆盖疾病、心情、客人/奴隶状态、DLC tracker 和需求变化。

验收：

- 不再存在“看似 tick，实际漏掉大半 pawn 状态”的含混实现。
- 被跳过的 tracker 都有设计说明。
- 骑乘 1 个游戏日以上不会出现明显需求、疾病、关系、DLC 状态卡死。

### 2.2 ForceJob ability durationTick 配置无效

优先级：P1。

涉及文件：

- `1.6/Source/Features/Genes/CompProperties_AbilityEffect_ForceJob.cs`
- `Bio_1.6/Defs/AbilityDef.xml`

问题：

- C# 定义了 `durationTick`。
- XML 配置了 `<durationTick>240</durationTick>`。
- 当前施加 forced job 时没有消费该字段，配置成为假参数。

计划：

- 若能力应有持续时间，则把 `durationTick` 接入 forced job 或状态释放逻辑。
- 若能力只需要瞬时强制任务，则删除字段和 XML 配置，避免误导后续维护者。
- 修改前先确认玩家可见设计：240 tick 是持续锁定、冷却窗口、还是旧实现残留。

验收：

- XML 中不存在无效配置。
- C# 字段要么被有效使用，要么被删除。
- 能力实际行为和说明一致。

### 2.3 Milk profile 与 postpartum 逻辑悬空

优先级：P1。

涉及文件：

- `1.6/Source/Features/Milk/Comps/CompMooHasBodyResource.cs`
- `1.6/Source/Features/Milk/MooGirlBreastProfileUtility.cs`
- `1.6/Source/Features/Milk/Hediffs/HediffComp_Lactation.cs`

问题：

- `BreastSize` 被赋值但产量计算未使用。
- `TryGetProductionDays` 没有调用点。
- `NotifyBirth()` 没有调用点，产后加成永远不会触发。
- 这会让 XML 或 C# 中看似存在的体型与产后生产设计成为死设计。

计划：

- 明确乳量是否应受体型、成长阶段或产后状态影响。
- 若应影响，接入产量计算，并写明数值不变约束或行为变更提案。
- 若不应影响，删除无调用工具、无效字段赋值和不可触发方法。
- 对产后事件接入点使用事件或低侵入 patch，避免全局扫描。

验收：

- 没有“写了配置但永远不生效”的乳量逻辑。
- 产量公式中每个输入都能说明来源和效果。
- 产后逻辑要么能被触发，要么被明确删除。

## 阶段 3：收束侵入性和全局影响

### 3.1 Ghoul 渲染刷新不应 patch 所有 Hediff.Tick

优先级：P1。

涉及文件：

- `1.6/Source/Features/Genes/Rendering/Harmony_GhoulRenderingRefresh.cs`

问题：

- 当前通过全局 `Hediff.Tick` patch 处理 Ghoul 渲染刷新。
- `Hediff.Tick` 是所有 hediff 的热路径，影响范围远大于 MooGirl 和 Ghoul。
- 这个实现把一个局部刷新需求放进全局 tick，兼容性和性能都不够干净。

计划：

- 改为在 Ghoul 添加、状态变化或 pawn 外观状态变化时登记刷新队列。
- 用 `GameComponent` 或模块状态在有限 tick 窗口内处理目标 pawn。
- 保留失败回退，但不得每个 hediff tick 都检查。

验收：

- 删除或显著收窄 `Hediff.Tick` 全局 patch。
- 刷新只针对相关 pawn 和短时间窗口。
- 大量 hediff 存在时不会引入无意义检查。

### 3.2 TraderKind buy tag patch 范围过宽

优先级：P1。

涉及文件：

- `1.6/Patches/MilkFood_TraderStock_Patch.xml`

问题：

- patch 给所有缺少指定 buy tag 的 `TraderKindDef` 添加标签。
- 这会影响其他 mod 和原版商人的库存逻辑，属于内容层面的全局污染。

计划：

- 改为只 patch 明确需要出售 MooGirl milk food 的 trader。
- 或改用专属 tradeTags、stockGenerator、ThingSetMaker 或显式兼容列表。
- 对每个被 patch 的 trader 写明理由。

验收：

- 不再对所有 trader 做兜底添加。
- 非目标商人库存不被本 mod 改变。
- 可选 mod 商人只在对应 mod 存在时 patch。

### 3.3 Drug administer recipeUsers 自动扩散过宽

优先级：P1。

涉及文件：

- `1.6/Source/Features/Misc/Harmony_DrugAdministerDefs.cs`

问题：

- 当前会把 recipeUsers 扩展到所有 `race.IsFlesh` pawn defs。
- 这可能把 MooGirl 需求扩散到不该支持的生物、异种族或其他 mod pawn。

计划：

- 明确目标是“所有肉体 pawn 可用”还是“只补 MooGirl/HAR 相关 pawn”。
- 若只为兼容 MooGirl，收窄到本 mod race 或可配置 allowlist。
- 若确实要全局扩散，需要设置开关并记录兼容风险。

验收：

- 不再无说明地修改所有 flesh race。
- recipeUsers 变更有边界、有开关或有完整理由。
- 与 HAR 和常见 race mod 组合时不产生异常 recipe。

## 阶段 4：删除冗余与修小缺陷

### 4.1 GatherBodyResources 条件恒真

优先级：P2。

涉及文件：

- `1.6/Source/Features/Milk/WorkGivers/WorkGiver_GatherBodyResources.cs`

问题：

- `if (pawn2 == pawn || pawn2 != pawn)` 是恒真条件。
- 这会让读者怀疑这里曾经有未完成筛选逻辑。

计划：

- 删除恒真分支或恢复真实判定条件。
- 如果原意是允许自采和他采，改成清晰注释或明确变量名。

验收：

- 不存在恒真或恒假条件。
- 工作给予逻辑仍符合预期。

### 4.2 RopeToWallRopeHitch pending spot 清理不完整

优先级：P2。

涉及文件：

- `1.6/Source/Features/Roping/Jobs/JobDriver_RopeToWallRopeHitch.cs`

问题：

- 开始作业时标记 pending spot。
- 当前主要在成功路径清理，失败、中断、pawn 被打断等路径可能残留。

计划：

- 给 job 添加 finish action 或统一 cleanup，确保所有退出路径都释放 pending spot。
- 验证失败、中断、取消和成功四种路径。

验收：

- pending spot 不会因 job 失败永久占用。
- 成功路径行为不变。

### 4.3 RopingTick 断绳通知重复

优先级：P2。

涉及文件：

- `1.6/Source/Features/Roping/Harmony_RopingTick.cs`

问题：

- 手动调用 `BreakAllRopes()` 后又直接通知。
- `BreakAllRopes` postfix 也会通知，当前依赖幂等去抵消重复。

计划：

- 保留一个通知来源。
- 如果需要区分手动断绳和原版断绳，在事件参数中表达，不重复触发。

验收：

- 断绳事件只触发一次。
- 下游状态更新不依赖重复通知。

### 4.4 发布包资源卫生

优先级：P2。

涉及文件：

- `Textures/**/*.sai2`
- 其他未引用源工程文件、临时图层文件和构建产物。

问题：

- `.sai2` 源文件适合源码归档，但不适合 Workshop 发布包。
- 发布包应只包含运行时需要的 XML、DLL、纹理、声音、About、语言和必要文档。

计划：

- `.sai2` 保留在 Git 中作为源素材。
- 使用 `docs/tools/New-WorkshopPackage.ps1` 生成 Workshop 发布包，脚本必须排除 `.sai2`、`1.6/Source`、PDB、bin/obj、临时和备份文件。
- 检查是否还有 `bin`、`obj`、`.pdb`、临时文件、备份文件进入发布包。

建议命令：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\New-WorkshopPackage.ps1
```

验收：

- Workshop 包不包含源工程图层文件。
- Git 仓库可以保留必要源资产，但发布路径干净。

## 阶段 5：验证矩阵

每完成一个阶段都必须运行：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; msbuild .\1.6\Source\MooGirlRace.csproj /p:Configuration=Release /v:minimal
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; .\docs\tools\Invoke-Phase6StaticValidation.ps1
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; git status --short
```

发布前必须完成以下游戏内验证：

- 最小必需组合：Harmony、Core、HAR、MooGirl。
- 全 DLC 组合：Core、Royalty、Ideology、Biotech、Anomaly、Odyssey、HAR、MooGirl。
- 可选兼容组合：Facial Animation、Search and Destroy、Vanilla Cooking Expanded。
- 新殖民地开局、MooGirl 生成、装备束具、洗脑头盔倒计时、产奶工作、牵引、骑乘、能力释放、商人库存。
- 保存、退出、重进、读档后重复上述关键交互。
- 长时间运行至少 1 个游戏日，重点观察骑乘 pawn、hediff、需求、疾病、DLC tracker 和日志。

日志扫描建议：

```powershell
& 'C:\Users\Fishiv\bin\utf8-env.ps1'; Select-String -LiteralPath "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log" -Pattern "Error|Exception|Could not|Failed|missing|NullReference|Translation data|Could not resolve|XML error|Patch operation" -CaseSensitive:$false
```

验收：

- 无红字。
- 无重复刷日志。
- 无缺贴图、缺音效、缺翻译 key。
- 可选 DLC 或可选 mod 缺失时不报错。
- 非 MooGirl 内容不出现可观察行为变化。

## 整改完成定义

必须同时满足以下条件，才允许把本轮整改标记为完成：

- 所有 P0 关闭。
- 所有 P1 关闭或有明确、合理、已验证的保留说明。
- 所有 P2 不进入发布包。
- 构建和静态验证通过。
- 游戏内验证按 `docs/08-game-validation-runbook.md` 完成并记录结果。
- 每个保留的全局 patch 都有边界说明、性能理由和失败回退。
- 每个无效字段、无调用方法、无效 XML 配置都被删除、接入或解释。
- 文档索引、验证记录和实际代码状态一致。

## 当前整改清单

| ID | 优先级 | 问题 | 状态 |
| --- | --- | --- | --- |
| H01 | P0 | Biotech-only DefOf 缺少 MayRequire | 已修复，待最小组合游戏内加载验证 |
| H02 | P0 | BrainwashHelmet 计时器生命周期不完整 | 已修复，待读档和装备路径游戏内验证 |
| H03 | P0 | Mounted rider 手动 tick 语义缺口 | 已收敛，待 1 游戏日以上长测 |
| H04 | P0 | SlaveApparel isLocked Scribe 默认值不一致 | 已修复，待读档验证 |
| H05 | P1 | ForceJob ability durationTick 无效 | 已修复，待能力释放验证 |
| H06 | P1 | Milk profile 与 postpartum 逻辑悬空 | 已修复，待产奶和出生验证 |
| H07 | P1 | Ghoul 渲染刷新 patch 侵入全局 Hediff.Tick | 已收束，待 Ghoul 外观刷新验证 |
| H08 | P1 | TraderKind buy tag patch 范围过宽 | 已收束，待商人库存验证 |
| H09 | P1 | Drug administer recipeUsers 扩散过宽 | 已收束，待配方可用性验证 |
| H10 | P2 | GatherBodyResources 恒真条件 | 已清理 |
| H11 | P2 | RopeToWallRopeHitch pending spot 清理不完整 | 已清理，待失败/中断路径验证 |
| H12 | P2 | RopingTick 断绳通知重复 | 已清理 |
| H13 | P2 | 发布包 `.sai2` 和源资产卫生 | 已提供发布包脚本，待实际 Workshop 上传前复跑 |
| H14 | P0 | RaceProperties 引用不存在的 melee building SoundDef | 已修复，待 fresh Player.log 验证 |
| H15 | P0 | 中文 QuestScriptDef 翻译键重复导致加载报告错误 | 已清理，待 fresh Player.log 或 translation report 验证 |
| H16 | P1 | 语言重复键缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H17 | P2 | 束具工具菜单对无束具目标生成空子菜单 | 已修复，待菜单交互验证 |
| H18 | P2 | 业务代码直接调用 Verse.Log，日志前缀和限频不统一 | 已收束，已加入 Phase 6 静态校验 |
| H19 | P1 | administer milk recipe 热重载可能重复累积 ingredients | 已修复，待 DevMode 热重载或 fresh load 验证 |
| H20 | P0 | Biotech-only 火焰喷射器 parent/sound 在最小组合下可能缺 Def | 已修复，待最小组合 fresh load 验证 |
| H21 | P1 | Biotech/Royalty 研究和装备引用缺少 gate | 已修复，待最小组合与全 DLC 验证 |
| H22 | P1 | 可选 DLC Def 引用缺少自动 gate 检查 | 已加入 Phase 6 静态校验 |
| H23 | P1 | 中文 RulePackDef 缺少 MooGirl_NamerPerson 翻译 key | 已修复，待 fresh Player.log 验证 |
| H24 | P1 | 英中 key/数字占位符缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H25 | P0 | SlaveApparel/AdvancedSlaveApparel 覆写装备生命周期时漏掉 base，comp 回调被绕过 | 已修复，待装备/卸下游戏内验证 |
| H26 | P0 | MagneticShackles 使用非生命周期 OnEquipped/OnUnequipped，装备路径不触发 | 已修复，待装备/卸下游戏内验证 |
| H27 | P0 | MagneticShackles 计时器 Scribe 默认值与读档恢复不完整 | 已修复，待读档验证 |
| H28 | P0 | SlaveApparel.lockCount 与 isLocked 读档后可能不自洽 | 已修复，待读档验证 |
| H29 | P1 | 完整解锁后状态不同步，且先 Remove 再地图放置可能在掉落失败时悬空装备 | 已修复，待解锁/掉落失败路径验证 |
| H30 | P1 | AdvancedSlaveApparel 遍历锁定状态时误用当前装备 isLocked | 已修复，待多件束具混穿验证 |
| H31 | P1 | 装备生命周期 base call 与高风险 Scribe 默认值缺少防回归检查 | 已加入 Phase 6 静态校验 |
| H32 | P1 | Courier quest spawnCell 裸默认可能把缺字段读成地图角落有效坐标 | 已修复，待 courier quest 读档/触发验证 |
| H33 | P1 | 解锁 job 等待结束时目标束具若已移除仍可能消耗钥匙 | 已修复，待并发/中断解锁路径验证 |
| H34 | P1 | BrainwashHelmet gizmo 点击时 wearer 状态变化可能导致空引用 | 已修复，待按钮交互验证 |
| H35 | P1 | ShockCollar gizmo 执行时检查 current wearer 但施加效果使用旧 wearer | 已修复，待按钮交互验证 |
| H36 | P2 | BrainWashSlaveApparel 继承关系导致冗余类型判断和不可达破解器分支 | 已修复，已加入 Phase 6 静态校验 |
| H37 | P2 | 束具解锁入口混用 LocalTargetInfo null 语义且可出现空二级菜单 | 已修复，待菜单交互验证 |
| H38 | P1 | 解锁 job 不支持尸体 TargetB，且执行前后目标/钥匙校验不足 | 已修复，待尸体解锁、并发和掉落失败路径验证 |
| H39 | P1 | 破解器目标选择和 job driver 对目标强转/失效缺少防线 | 已修复，待破解器交互验证 |
| H40 | P1 | UseItemOn 对异常 TargetB 强转 Pawn，跨 mod 或异常 job 可能红字 | 已修复，待穿戴束具交互验证 |
| H41 | P1 | 直接解锁 use effect 忽略钥匙类型、已锁状态和部分解锁消耗 | 已修复，待直接使用钥匙路径验证 |
| H42 | P1 | 束缚目标安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H43 | P1 | 牵引菜单和 RopeMoo job 目标规则分散，job 端直接强转 TargetA | 已修复，待牵引交互验证 |
| H44 | P1 | RemoveRope job 目标失效或已无绳时仍可能打断目标工作 | 已修复，待解除牵引交互验证 |
| H45 | P1 | RopeToWallRopeHitch 目标强转且 pending spot rope 会被自身断绳流程清掉 | 已修复，待墙绳扣中断/完成路径验证 |
| H46 | P2 | RopeToBuild 点击后不复查预约/路径且绕过 ordered-job 路径 | 已修复，待墙绳扣菜单验证 |
| H47 | P1 | 牵引目标安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H48 | P1 | 骑乘 gizmo action 捕获旧 rider/carrier/comp，UI 帧间状态变化可能作用到旧对象 | 已修复，待 gizmo 交互验证 |
| H49 | P1 | 应急/兜底下骑未统一使用 DismountCellValidator | 已修复，待下骑无格、销毁和反生成路径验证 |
| H50 | P1 | mounted ranged 私有入口对 comp/carrier/searcher 空状态防线不足 | 已修复，待骑乘远程作战验证 |
| H51 | P1 | 骑乘状态安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H52 | P1 | 直接喝奶/喂奶/找奶喝效果与奶量消费不原子，等待期间奶量变化可能白送收益 | 已修复，待直接奶交互游戏内验证 |
| H53 | P1 | 儿童找奶喝使用成人 5% 门槛但消耗 30% 奶量 | 已修复，待儿童找奶喝门槛验证 |
| H54 | P1 | 奶产 job 目标强转且榨乳结束可能误打断目标等待 | 已修复，待异常目标和榨乳中断路径验证 |
| H55 | P1 | 右键强制榨乳绕过榨乳器管理并缺少点击时预约/路径复查 | 已修复，待榨乳器穿戴者菜单验证 |
| H56 | P1 | 奶产交互安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H57 | P1 | 信使 incident 生成 quest 时未把目标 map 传入 slate，多地图可能落错地图 | 已修复，待多地图 incident 验证 |
| H58 | P1 | 信使交谈 job 目标强转且菜单点击缺少当前状态复查 | 已修复，待交谈菜单和 job 中断验证 |
| H59 | P1 | 信使对话框按钮捕获旧 courier，窗口陈旧后仍可能继续解决事件 | 已修复，待信使离开/死亡/已解决后对话框验证 |
| H60 | P2 | 运货员日记阅读菜单/job 缺少共享目标验证，Dialog 保留无用 book 字段 | 已修复，待读日记交互验证 |
| H61 | P1 | 事件/信使交互安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H62 | P1 | 骑乘右键菜单仍捕获旧 comp，点击时不复查骑乘状态、预约和路径 | 已修复，待骑乘右键菜单验证 |
| H63 | P1 | mount/dismount job 预约阶段未显式确认目标 Pawn 和当前骑乘条件 | 已修复，待 mount/dismount job 中断验证 |
| H64 | P2 | nuzzle job 直接强转 CurJob target，且目标选择不检查预约 | 已修复，待 nuzzle AI 长跑验证 |
| H65 | P1 | 骑乘菜单和小型 AI job 安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H66 | P1 | 冲锋 job 预约和执行阶段缺少当前 Pawn 目标复查，冲锋 hediff 清理依赖间接 tick 自清 | 已修复，待冲锋命中/中断/目标消失验证 |
| H67 | P1 | Shout 强制 job 缺少共享目标验证、jobs/mindState 判空和范围循环平方距离优化 | 已修复，待喊叫能力战斗验证 |
| H68 | P1 | 放牧链全图搜索下一株植物，且可能覆盖玩家强制进食、草稿状态和已有队列 | 已修复，待 MooGirl 自然进食与玩家强制进食验证 |
| H69 | P1 | 能力 job 与放牧链缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H70 | P1 | 全局学习/成长 patch 缺少自定义 trait/hediff Def 缺失回退 | 已修复，待成长/学习长跑验证 |
| H71 | P1 | 绳索 tick/draw 与 milking render patch 反射字段缺失时可能在热路径红字 | 已修复，待牵引绘制与榨乳动画验证 |
| H72 | P2 | 野奴/招募/新生儿全局 patch 对异常 pawn 或缺 fallback kind/story 有旧式空值假设 | 已修复，待野奴招募和新生儿生成验证 |
| H73 | P1 | Harmony 全局边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H74 | P1 | thought hediff comp 使用硬 HediffDef 查找，且 tick 中假设 mood memory tracker 存在 | 已修复，待饮奶 hediff/心情路径验证 |
| H75 | P1 | 学习 cap、骑乘近战和 melee animation 兼容层仍有反射硬强转或私有调用异常回流 | 已修复，待学习、骑乘近战和 melee animation 兼容验证 |
| H76 | P1 | 反射强转与 thought hediff hard lookup 缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H77 | P2 | 重构后残留空 Harmony 类仍被 csproj 编译，制造未接入 patch 的审计噪音 | 已清理 |
| H78 | P2 | 空生产类型缺少自动防回归检查，标记型空类语义不够显式 | 已加入 Phase 6 静态校验 |
| H79 | P1 | 故事定时触发的 courier quest 仍用旧 points 入口，未传目标 map slate | 已修复，待多地图 courier 触发验证 |
| H80 | P2 | 未接入的 CourierDemand QuestPart 与索赔翻译残留，包含危险资源扣除副作用 | 已清理 |
| H81 | P1 | courier story map slate 与 dead demand 残留缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H82 | P2 | 奴隶库存生成器在全局命名空间，XML 裸 Class，且 HandlesThingDef/difficulty 缺少空安全 | 已修复，待商人库存验证 |
| H83 | P1 | 救援加入 tick/quest signal 默认 pawn 状态完整，异常 pawn 或读档边界可能红字 | 已修复，待救援加入/读档验证 |
| H84 | P1 | 奴隶库存与救援加入状态安全缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H85 | P1 | XML 类型交叉检查未覆盖本 mod 短名类型引用，裸 Class 可绕过门禁 | 已加入 Phase 6 静态校验 |
| H86 | P2 | 必需 Def 查找使用裸 GetNamed，缺失时错误信息不够指向 MooGirl 必需边界 | 已修复 |
| H87 | P1 | 必需/可选 Def 查找边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H88 | P1 | 野奴 incident 直接强转 incident target，缺少 reachability 和生成 pawn 空值防线 | 已修复，待野奴 incident 验证 |
| H89 | P1 | 驯服/Designator/WildMan 全局入口分散判断逃亡野奴和玩家派系 | 已修复，待驯服入口验证 |
| H90 | P1 | 野奴 incident 与驯服全局入口边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H91 | P1 | 流浪加入任务直接强转 AcceptJoiner 信件，跨 mod letterClass 改动时可能红字 | 已修复，待流浪加入信件交互验证 |
| H92 | P1 | 流浪加入信件类型边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H93 | P1 | courier 生成后默认 pawn/inventory/mindState/Spawn 成功，异常生成状态可能红字或半处理 | 已修复，待 courier 触发、交谈、战斗/离开验证 |
| H94 | P1 | refugee pod downed 生成循环默认 PawnGenerator 非空且 10 次内必定 downed | 已修复，待 refugee pod 触发验证 |
| H95 | P1 | courier 生成、库存载荷和 fight mindState 边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H96 | P1 | refugee pod downed 生成韧性缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H97 | P1 | opening crash story 仍用 points-only quest 入口，多地图/无地图时目标地图边界不显式 | 已修复，待开局坠舱多地图触发验证 |
| H98 | P1 | opening crash story map slate 边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H99 | P1 | opening pod root 仍假设 quest/slate/map/pawns/letter targets 均有效，debug 或跨 mod 入口可绕过 story 防线 | 已修复，待开局坠舱 debug/正常触发验证 |
| H100 | P1 | opening pod root 本地边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H101 | P2 | `MooGirl_FactionUtility` 已无调用者却仍被编译，保留直接玩家派系敌对判断审计噪音 | 已清理 |
| H102 | P1 | 救援/信使/hediff tick 仍散落直接 `Faction.OfPlayer` 等值判断 | 已修复，待救援加入与 courier 交互验证 |
| H103 | P1 | 高风险事件入口玩家派系 helper 边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H104 | P1 | 能力/束具 hediff 跨模块仍残留直接 `Faction.OfPlayer` 等值判断，且 mental-state hediff 先取 Pawn.Faction 再判空 | 已修复，待能力 AI 与束具 hediff tick 验证 |
| H105 | P1 | 全源码玩家派系 helper 边界缺少总闸防回归检查 | 已加入 Phase 6 静态校验 |
| H106 | P1 | 全源码仍残留裸 `HostileTo(Faction.OfPlayer)` 玩家敌对判断 | 已修复，待 courier/refugee/opening pod 与束具 trait hediff 验证 |
| H107 | P1 | AddTrait hediff tick 默认配置项、TraitDef 和 story.traits 均有效 | 已修复，待束具 trait hediff 验证 |
| H108 | P1 | ReduceWillorEnslave 达到阈值后每 tick 重复削意志/奴役且硬访问 guest/player faction | 已修复，待读档和洗脑束具触发验证 |
| H109 | P1 | 玩家派系 helper 总闸未禁止硬 `Faction.OfPlayer` 属性访问 | 已加入 Phase 6 静态校验 |
| H110 | P1 | ReduceWillorEnslave 一次性状态与 Scribe 持久化缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H111 | P1 | 事件生成 pawn 的世界池丢弃逻辑三处复制且缺少 Spawned 边界 | 已修复，待 courier/refugee/opening pod 触发验证 |
| H112 | P1 | courier spawn 失败后保留未生成 pawn 字段，后续信号无法重试 | 已修复，待 courier spawn 失败路径验证 |
| H113 | P1 | refugee pod 信件默认 pawn 和 ageTracker 有效，异常入口可能红字 | 已修复，待 refugee pod 信件验证 |
| H114 | P1 | 事件生成 pawn 生命周期边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H115 | P1 | Lactation 生产倍率 getter 默认 props、Playing 状态和 food need 均有效 | 已修复，待产奶倍率与出生后窗口验证 |
| H116 | P1 | Moo milkable 奶产链路默认 health/hediffSet、ageTracker 和 RaceProps 均有效 | 已修复，待异常 pawn 与产奶 tick 验证 |
| H117 | P1 | 哺乳期倍率与奶产 tracker 防线缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H118 | P1 | Ghoul 渲染刷新静态 pawn 缓存缺少新游戏/读档清理且非 Playing 可调度 TickManager | 已修复，待 Ghoul 外观刷新与读档验证 |
| H119 | P1 | Ghoul 渲染刷新全局缓存边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H120 | P1 | Xenotype 出生 patch 静态父母 scratch list 缺少 finally 清理且 gene 写入默认列表/条目有效 | 已修复，待 MooGirl 新生儿 xenotype 与读档验证 |
| H121 | P1 | Xenotype 出生/加载边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H122 | P1 | MilkingDeviceReleaseEffect 默认 props 与 XML 列表条目有效，热路径可能被空 entry 或无效 fleck 参数打红 | 已修复，待榨乳器释放效果游戏内验证 |
| H123 | P1 | BodyResource tick/gather 默认 Playing、ResourceDef、doer/map 与同图上下文有效 | 已修复，待异常采集、DevFill 和榨乳 job 验证 |
| H124 | P1 | CureFoodEffects 与 FoodEffectUtility 默认 Pawn.health/hediffSet 完整 | 已修复，待饮奶 hediff/食物效果验证 |
| H125 | P1 | WhileHavingThoughts 漏 base expose 且 props/counter/memories 边界不完整 | 已修复，待读档和 thought hediff 验证 |
| H126 | P1 | MilkOutputUtility 默认产物 Def、地图、位置和 stackLimit 有效 | 已修复，待产物生成路径验证 |
| H127 | P1 | 奶产/hediff 热路径边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H128 | P1 | EleShock 默认配置列表/Pawn/Map/jitterer 完整，且 XML hediff apply/remove 配置未接线 | 已修复，待电击 hediff、读档和污渍路径验证 |
| H129 | P1 | Brainwash performance 播放器默认 props 与 effect 列表条目有效 | 已修复，待洗脑演出验证 |
| H130 | P1 | 束具 timed hediff 组件分散硬 Props 和 Pawn/need/map 默认完整假设 | 已修复，待束具 hediff tick 长跑验证 |
| H131 | P1 | MooGirlNurtureProgress 默认 parent/def 有效 | 已修复，待 nurture 满进度验证 |
| H132 | P1 | 束具 hediff 热路径缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H133 | P1 | 奶产 ThingComp props、榨乳器储量/次数、按钮图标和释放 Def 边界不完整 | 已修复，待榨乳器储奶、手动/自动释放和读档验证 |
| H134 | P1 | 解锁 use comp 对 CompProperties_Usable 硬强转，异常 XML comp 可红字 | 已修复，待解锁工具交互验证 |
| H135 | P1 | 束具装备手动 gizmo 可能使用陈旧 cooldown/wearer/props，磁力镣铐解除空列表热路径分配 | 已修复，待头盔/项圈/镣铐按钮交互验证 |
| H136 | P2 | readable book comp 默认 props/title 有效，菜单或 job 打开窗口可被 XML 错配打红 | 已修复，待读日记交互验证 |
| H137 | P1 | ForceJob ability 与 CheckJobOrRemove hediff 仍有 props 硬强转残留 | 已修复，待能力释放与 hediff 移除路径验证 |
| H138 | P1 | 骑乘 comp props 错配会影响绘制、tick interval 和 mounted combat cooldown | 已修复，待骑乘显示、远程作战和长跑验证 |
| H139 | P1 | 全源码 ThingComp/HediffComp Props 硬强转缺少复扫记录 | 已复扫，剩余命中均非硬 props 转换入口 |
| H140 | P1 | 本轮 props/cooldown/fallback 边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H141 | P1 | 榨乳动画多处直接读取 TickManager，非 Playing/渲染缓存路径可能残留 transient 状态或红字 | 已修复，待榨乳动画、渲染缓存和读档验证 |
| H142 | P1 | MooGirl 婴儿哺乳默认 food max、delta、营养换算、mindState 和 ideo tracker 有效 | 已修复，待 Biotech 婴儿哺乳验证 |
| H143 | P1 | 婴儿喂养与榨乳动画边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H144 | P1 | `SlaveApparel.Notify_Equipped` 默认 pawn apparel/health/body 完整且装备 hediff 不重复 | 已修复，待束具装备、重复装备回调和异常 pawn 验证 |
| H145 | P1 | `SlaveApparel.Notify_Unequipped` 临时 List 分配与下一阶段 Def 可穿假设 | 已修复，待束具卸下、阶段替换和跨体型 pawn 验证 |
| H146 | P1 | 破解后 selector 直接访问与束具生命周期防回归门禁 | 已加入 Phase 6 静态校验，待破解交互验证 |
| H147 | P1 | apparel tag 随机补装每次构造候选列表且空 tag 白扫 DefDatabase | 已修复，待 pawn 生成/补装分布验证 |
| H148 | P1 | 牵引断绳路径额外复制 ropee 列表且 follow job 收尾默认 owner/jobs/CurJob 有效 | 已修复，待牵引断绳和长跑验证 |
| H149 | P1 | 骑乘、冲刺和束具破解散落直接 `Find.Selector` 访问 | 已修复，待骑乘选择、冲刺落地和破解交互验证 |
| H150 | P1 | 热路径分配与全局 UI 选择边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H151 | P1 | 事件/任务路径直接调用 `Find.WindowStack` / `Find.LetterStack`，缺 UI/game 上下文时可红字 | 已修复，待日记、快递员、救援和坠舱信件验证 |
| H152 | P1 | 快递员 quest 直接访问 TickManager 和 QuestManager | 已修复，待快递员接触、战斗响应和 quest 结束验证 |
| H153 | P1 | 生成 pawn 的 WorldPawns 操作直接访问 `Find.WorldPawns`，spawned discard 后仍可能继续丢入世界存储 | 已修复，待坠舱/快递员生成失败路径验证 |
| H154 | P1 | 事件/任务全局入口边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H155 | P1 | 快递员 incident、quest root 和故事计时器直接访问 `Find.FactionManager` | 已修复，待缺派系/正常派系触发验证 |
| H156 | P1 | 事件地图解析散落直接 `Find.AnyPlayerHomeMap` / `Find.Maps` fallback | 已修复，待无家园地图与多地图触发验证 |
| H157 | P1 | 快递员 incident/story 触发对派系和地图条件判定不统一 | 已修复，待 courier raid 触发与重试验证 |
| H158 | P1 | 事件派系与玩家地图解析缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H159 | P1 | 束具解锁二级菜单仍直接进入 `Find.WindowStack`，缺少统一 UI/game 上下文边界 | 已修复，待束具解锁二级菜单验证 |
| H160 | P1 | `MapRopingIndex` 低频重建直接读取全局 TickManager，地图缓存组件不应依赖全局 tick 单例 | 已修复，待牵引长跑和重建兜底验证 |
| H161 | P1 | mounted ranged cast/last attack 时间戳直接读取 TickManager，异常上下文可能红字或污染冷却 | 已修复，待骑乘远程作战验证 |
| H162 | P1 | Lactation 产后倍率和出生通知仍直接读取 TickManager，和 tick helper 边界不一致 | 已修复，待出生后产奶倍率验证 |
| H163 | P1 | 剩余 tick/window 全局入口缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H164 | P1 | 骑乘近战、Ghoul 刷新、奶资源和榨乳动画仍有直接或局部封装的 TickManager 读取 | 已修复，待骑乘近战、Ghoul 外观刷新、奶资源和榨乳动画验证 |
| H165 | P1 | 三件束具装备手动冷却各自重复实现 tick fallback，TickManager 边界口径分散 | 已修复，待头盔/项圈/镣铐按钮冷却验证 |
| H166 | P1 | 全源码 TickManager 集中入口缺少总闸防回归检查 | 已加入 Phase 6 静态校验 |
| H167 | P1 | 业务层仍散落 `Find.ColonistBar`、`Find.Storyteller` 和 `Find.CurrentMap` 直接入口 | 已修复，待殖民者栏刷新、奴隶库存和束具菜单验证 |
| H168 | P1 | 全源码 `Find.*` 访问缺少核心 helper 总闸 | 已加入 Phase 6 静态校验 |
| H169 | P1 | 业务层仍直接访问 `Current.ProgramState` / `Current.Game.GetComponent`，全局游戏状态入口口径不一致 | 已修复，待故事计时、Ghoul 刷新和奶资源 tick 验证 |
| H170 | P1 | 洗脑演出缺 GameComponent 时运行时手动 `Current.Game.components.Add`，生命周期污染风险 | 已修复，待洗脑演出组件存在性和演出触发验证 |
| H171 | P1 | `Current.Game` / `Current.ProgramState` 和运行时 GameComponent 列表改写缺少总闸防回归检查 | 已加入 Phase 6 静态校验 |
| H172 | P1 | 动态 administer milk recipe 默认 `__result` 和 recipe entry 永远非空，跨 mod postfix 或异常枚举可加载期红字 | 已修复，待 fresh load 和 DevMode 热重载验证 |
| H173 | P1 | 非 ingestible MooGirl_Milk 仍生成给药配方，且热重载复用 RecipeDef 时 defaultIngredientFilter 可能残留 | 已修复，待配方可用性和热重载验证 |
| H174 | P1 | 动态 RecipeDef 注入缺少 null/热重载/recipeUsers 扩散自动防回归检查 | 已加入 Phase 6 静态校验 |
| H175 | P1 | MooGirl 身份判断散落在 body defName、MooGirlBody 直比和动态 recipe 本地 helper 中，口径容易漂移 | 已修复，待牵引、新生儿视觉、配方用户和跨 HAR body 变体验证 |
| H176 | P1 | MooGirl-only thought 和采集 WorkGiver 默认 pawn/mood/map/RaceProps 完整，异常入口可能红字 | 已修复，待异常 pawn、采集工作和 thought 移除路径验证 |
| H177 | P1 | MooGirl 身份判断集中入口缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H178 | P1 | PawnGenerator 生成后束具自动锁定逻辑裸写在全局 postfix 内，边界和副作用不够可审计 | 已修复，待 PawnGenerator 生成束具锁定验证 |
| H179 | P1 | 生成后束具锁定默认 WornApparel/条目完整且未显式避免重复锁定 | 已修复，待随机补装、奴隶库存和新 pawn 生成验证 |
| H180 | P1 | PawnGenerator 手动 patch 与生成后束具锁定缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H181 | P1 | 手动 Harmony patch 的 `harmony.Patch(...)` 裸露在初始化链路中，签名漂移或冲突异常可能中断 mod 初始化 | 已修复，待 fresh load 与可选 HAR 缺失/存在验证 |
| H182 | P1 | 手动 patch 失败缺少本地化限频日志，成功列表边界不够显式 | 已修复，待异常注入或签名漂移模拟验证 |
| H183 | P1 | 手动 Harmony patch 失败降级与英中日志文案缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H184 | P1 | `MooGirlBootstrap.Initialize()` 直接 `Harmony.PatchAll()`，单个特性 patch 失败可能拖垮整批 patch 与手动注册 | 已修复，待 fresh load 与 patch 成功列表抽查 |
| H185 | P1 | Harmony patch 类发现阶段缺少 `GetTypes()`、部分类型加载和属性读取异常降级 | 已修复，待类型加载异常模拟验证 |
| H186 | P1 | Harmony bootstrap 逐类注册与失败文案缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H187 | P1 | 开局逃生舱无派系 pawn 生成彻底失败时硬抛异常，可能把可降级事件失败变成 QuestNode 红字 | 已修复，待 opening pod 生成失败模拟验证 |
| H188 | P1 | 普通逃生舱 pawn 生成彻底失败时硬抛异常，且原版 RunInt 默认 GeneratePawn 永远非空 | 已修复，待 refugee pod 生成失败模拟验证 |
| H189 | P1 | opening/refugee pod 生成失败降级缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H190 | P2 | `MooGirlText.Resolve` 文本占位符格式化失败直接 `Log.ErrorOnce`，可回退文本问题会污染红字日志 | 已修复，待异常占位符文本模拟验证 |
| H191 | P2 | 直接 `Verse.Log` 门禁仍豁免 `MooGirlText.cs`，日志收束边界不够干净 | 已修复，已收紧 Phase 6 静态校验 |
| H192 | P2 | 文本格式化失败降级缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H193 | P1 | 骑乘资格和 mounted combat 可用性默认 pawn health capacities / Awake tracker 完整 | 已修复，待异常 pawn 和骑乘作战验证 |
| H194 | P1 | 骑乘近战默认 rider meleeVerbs 与目标 health.hediffSet 完整 | 已修复，待骑乘近战异常目标验证 |
| H195 | P1 | 骑乘武器绘制直接读取 ageTracker.CurLifeStage，异常 lifeStage 可渲染红字 | 已加入 Phase 6 静态校验，待骑乘绘制验证 |
| H196 | P1 | rope-to-hitch 中断或换图后 pending spot-rope 只按当前 map 清理，旧 map 缓存可残留 | 已修复，待墙绳扣中断/换图验证 |
| H197 | P1 | `MapRopingIndex` 低频重建不裁剪无效 pending spot-rope 临时状态 | 已修复，待牵引长跑和重建兜底验证 |
| H198 | P1 | pending spot-rope 生命周期缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H199 | P1 | 榨乳动画状态字典只按 thingIDNumber 命中，未确认缓存 pawn 引用一致 | 已修复，待榨乳动画读档/新游戏验证 |
| H200 | P1 | 双人榨乳动画 partner 失效时状态可能残留到 stale 窗口，生命周期清理临时分配列表 | 已修复，待榨乳中断/反生成验证 |
| H201 | P1 | 榨乳动画静态状态缓存边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H202 | P2 | 牵引征召 gizmo 禁用路径每次 UI 枚举分配 `List<Gizmo>` | 已修复，待牵引 UI 长时间打开验证 |
| H203 | P2 | 束具破解器玩家操作反馈写入开发日志而非原版消息 | 已修复，待破解器交互验证 |
| H204 | P1 | 牵引 UI 热路径和破解器反馈边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H205 | P2 | EleShock 污物计时同步在 hediff tick 中分配临时 `HashSet` / `List` | 已修复，待电击束具长跑验证 |
| H206 | P2 | EleShock 抖动效果每 tick 通过 `Traverse` 重新查私有 jitter 字段 | 已修复，待电击抖动效果验证 |
| H207 | P1 | EleShock 热路径零分配和 jitter 反射边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H208 | P2 | 幼年体型自动修正统计在普通加载/读档时写入信息日志 | 已修复，待 fresh load 日志验证 |
| H209 | P2 | `MooGirlLog.Message` 语义过宽，容易让普通业务信息回流开发日志 | 已修复 |
| H210 | P1 | 普通信息日志 DevMode gate 缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H211 | P2 | `MooGirlLog.WarningOnce` 静态 key 集合跨新游戏/读档保留，影响同进程复测 | 已修复，待同进程重复验证 |
| H212 | P2 | 食物效果 hediff Def/null 缓存缺少 transient reset 入口 | 已修复，待 Dev 热重载/重复读档验证 |
| H213 | P1 | 静态 once 日志、食物 Def cache 与 transient 状态重置缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H214 | P2 | Courier quest root 缺 map/派系/入口格时使用非限频 warning，重试路径可刷日志 | 已修复，待 courier 异常触发验证 |
| H215 | P2 | Courier spawn part 和束具解锁目标消失路径仍使用非限频 warning | 已修复，待重复信号/目标消失验证 |
| H216 | P1 | 业务代码非限频 `MooGirlLog.Warning` 缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H217 | P2 | `MooGirlLog.Error` 未使用但暴露红字 wrapper，未来容易把可降级问题升级成红字 | 已修复 |
| H218 | P1 | 红字日志入口删除缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H219 | P1 | 业务侧 `MooGirlLog.Error` / wrapper 内 `Log.Error` 回流缺少总闸 | 已加入 Phase 6 静态校验 |
| H220 | P2 | `MooGirlLog.WarningOnce` 对空白 key/message 缺少自防御，未来误用会压错告警或输出空 warning | 已修复 |
| H221 | P2 | 快递员已处理、束具目标无效/已破解、未穿锁定束具等瞬时拒绝反馈默认写入历史消息 | 已修复，待重复点击/无效目标交互验证 |
| H222 | P1 | 日志限频 fallback 与瞬时拒绝反馈历史噪音缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H223 | P2 | `QuestPart_SpawnCourier` 空 `inSignal` 可被空 tag signal 误触发生成逻辑 | 已修复，待 courier debug/坏档信号验证 |
| H224 | P2 | rescue join 重复 rescued signal 会对已入玩家派系 pawn 重新写 `WillJoinColonyIfRescued` | 已修复，待救援加入重复信号验证 |
| H225 | P1 | quest signal 空信号与救援加入重入状态缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H226 | P2 | Ghoul 渲染刷新静态 pending 队列未纳入中央 transient reset 口径 | 已修复，待 Ghoul 外观刷新与同进程重复验证 |
| H227 | P1 | Ghoul pending refresh 只靠自身 GameComponent 构造清理，缺少中央 reset 防回归检查 | 已加入 Phase 6 静态校验 |
| H228 | P1 | 中央 transient reset 未覆盖全部静态 pending/cache 状态的审计边界不够明确 | 已加入 Phase 6 静态校验 |
| H229 | P2 | `CompMooMilkable` DevFill 失败反馈默认写入历史消息，反复点击调试按钮污染 Archive | 已修复，待 DevFill 失败路径验证 |
| H230 | P2 | `Comp_MilkingDevice` DevFill 失败反馈默认写入历史消息，同样是开发按钮瞬时拒绝反馈 | 已修复，待 DevFill 失败路径验证 |
| H231 | P1 | DevFill reject feedback 缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H232 | P1 | 牵引服务层未拒绝非 MooGirl ropee，异常 job 入口可把普通 pawn 带入本 mod 牵引系统 | 已修复，待异常 job/debug 入口验证 |
| H233 | P1 | `JobDriver_RopeMoo` 保留普通 pawn 逮捕概率、拒绝牵引和狂暴反击死分支，扩大侵入面 | 已修复，待牵引 MooGirl 正常路径验证 |
| H234 | P1 | 牵引菜单非 MooGirl 成功率分支与目标边界不一致，缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H235 | P2 | `MooGirlNurtureUtility` 静态 Def 缓存缺少 reset hook，Dev 热重载/同进程复测可能持有旧 Def 引用 | 已修复，待 Dev 热重载/重复读档验证 |
| H236 | P1 | 中央 transient reset 未覆盖 nurture Def cache，静态缓存 reset 口径不完整 | 已修复，待同进程重复验证 |
| H237 | P1 | nurture Def cache 生命周期缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H238 | P2 | `PawnBool` 公开未初始化的 Simple Slavery shim 状态，形成看似兼容但默认不生效的公共写入口 | 已清理 |
| H239 | P2 | 束具菜单旧 helper 命名与空安全不达标，`is_vanillaslave` 对空 pawn 无防御 | 已修复 |
| H240 | P1 | 旧 `PawnBool` shim 与束具菜单 slave status helper 缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
| H241 | P2 | `MooGirlMod.harmony` 公开复制 bootstrap-owned Harmony 实例，扩大外部可写状态面 | 已清理 |
| H242 | P1 | `MooGirlMod.settings` 作为 public static mutable 字段被业务代码直接读取 | 已修复，待设置窗口/事件开关验证 |
| H243 | P2 | `CompStampedApparelKey.make_label` 保留旧式 lower-case helper 命名且缺少防回归检查 | 已加入 Phase 6 静态校验 |
| H244 | P2 | 动态 administer milk recipe hotReload 使用 `GetNamed(..., false)`，Def 查询风格仍需人工区分 | 已修复，待 DevMode 热重载验证 |
| H245 | P1 | 全源码 Def lookup 门禁未禁止 `GetNamed(..., false)` 回流 | 已加入 Phase 6 静态校验 |
| H246 | P2 | Xenotype 出生关系静态 scratch list 直接作为 `ref` 参数，理论上可被外部 API 重绑 | 已修复，待 MooGirl 新生儿 xenotype 验证 |
| H247 | P1 | Xenotype scratch list readonly/ref 局部清理边界缺少自动防回归检查 | 已加入 Phase 6 静态校验 |
