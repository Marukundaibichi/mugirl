# 性能优化实施记录 — 2026-09-24

依据当日性能审查（审查结论见本轮会话报告，覆盖 264 个 C# 文件、全量贴图与 XML），在不改变玩法行为的前提下修复了渲染热路径、投掷重扫、骑乘解析与巨企窗口每帧重建问题，并重建 `1.6/Assemblies/MugirlRace.dll`（860,160 字节，2026-09-24 20:05）。未调整任何数值、产量、伤害、概率或存档字段；未新增 Harmony patch 目标；工作区中用户既有改动全部保留。

## 行为边界（有意的时序权衡，均按项目既有先例）

| 位置 | 变化 | 界限 |
|---|---|---|
| 投掷/暴扣落点失败重试（[Thing_MugirlThrownObject.cs](../../1.6/Source/Features/Genes/Thing_MugirlThrownObject.cs)、[Thing_MugirlDunkProp.cs](../../1.6/Source/Features/Genes/Thing_MugirlDunkProp.cs)） | 首次放置仍立即尝试；失败后按 30→60→120 tick 指数退避重试，上限 120 tick | 原来不可放置载荷每 tick 全径向重扫（约 453 格 × CanPlaceAt，建筑载荷双轮）；现在地图腾出空间后最迟 120 tick（约 2 秒）放下。放置语义与成功路径不变 |
| `Comp_WeaponWheel.HasAutoloadingSystem` | 改为同 tick 备忘（与 `mountStateCacheTick` 同款）+ `CompTick` 每 tick 刷新 | 已生成 pawn 穿着变化最迟下一 tick 生效（原为同 tick）；未生成/暂停时 getter 现算，与原行为一致 |
| 巨企窗口列表缓存 | People/Finance/Trade/Missions/History 页派生数据按"操作版本号"缓存，`ShowFeedback`（全部操作结果的统一出口）递增版本号整体失效 | 窗口为 forcePause 模态，数据只在玩家操作后变化；操作后 UI 立即刷新（与原先一致）。Trade 搜索词/分类/排序变化立即重建 |

## 修复清单

**常驻渲染路径（每帧）**

- `HasAutoloadingSystem` 缓存；`GetAutoloadingWeapon` 先做边界检查；`CanDrawNow` 把立绘/死亡/卧床排除前移到读取武器之前（[Comp_WeaponWheel.Combat.cs](../../1.6/Source/Features/WeaponWheel/Comp_WeaponWheel.Combat.cs)、[Storage.cs](../../1.6/Source/Features/WeaponWheel/Comp_WeaponWheel.Storage.cs)、[PawnRenderNode_BackWeapon.cs](../../1.6/Source/Features/WeaponWheel/PawnRenderNode_BackWeapon.cs)）。渲染节点每帧 10~18 次的 apparel×GetComp 双重扫描降为每 tick 一次。
- `WeaponWheelHarmonyUtility.CompFor` 增加 Pawn→组件 `ConditionalWeakTable` 反查（读取校验 `comp.parent`，写路径加锁）；`Pawn.DrawPos` getter、`UpdateRotation`、`DynamicDrawPhaseAt` 等每帧补丁不再线性扫描 AllComps（[Harmony_WeaponWheel.cs](../../1.6/Source/Features/WeaponWheel/Harmony_WeaponWheel.cs)）。
- `WeaponWheelAnimationRenderer.Draw` 改为接收补丁入口已解析的 comp，消除二次 `TryGetComp`。
- `Harmony_MountRendering.DrawRider` 先做 `MugirlIdentity.IsMugirlPawn` 判断再取 comp（`Comp_MugirlMount` 仅存在于雪牛娘种族 Def，已核对 XML），非雪牛娘每帧零扫描。

**模拟 tick 路径**

- 投掷/暴扣落点失败退避（见上表）。
- `ReserveWeaponMass` 缓存总质量，`MarkBackWeaponsDirty`/`EnsureCollections`/`PostDestroy` 失效；`GearMass` 补丁不再每次对每把备用武器走完整 Stat 管线。
- `MountedCombatController.GetPrimaryRangedVerb(comp)` 以骑手主武器引用为键缓存 verb（换武器/换骑手自动失效，骑手为空清缓存）；`VerbTick`/`TickAim`/60 tick 寻敌与骑乘武器绘制的重复解析全部 O(1) 化。
- 冲锋/投掷的迭代器分配：`PunchThroughRoofs` 改为等价复刻 `GenSight.PointsOnLineOfSight` 写入复用静态缓冲（含端点语义）；`JobDriver_CastCharge.ApplyPassingImpact` 与 `PawnFlyer_LanceCharge.AttackPassingTargets` 改为 `RadialPattern` 直接遍历（pawn 恒单格，原版占格去重集合对该分支无作用，命中去重由 `hitPawns` 承担）；`LanceChargeWallUtility.CrossedCells` 迭代器改为 `CrossedCellsNonAlloc`。全部带 `StaticCacheLifecycle` 注释。
- 瞄准期读数：`Thing_MugirlThrownObject.MaxThrowDistance` 按 30 tick 短缓存（携带上限瞄准期内基本不变）；`CompAbilityEffect_PickupThrow` 的质量（按悬停目标引用）/力量按 30 tick 缓存；`targetParams` 构造一次复用。
- `WeaponWheelDevLog` 带字符串拼接的调用点加 `Enabled` 前置。
- 神经头盔黑暗精度补丁：`offsetFromDarkness` 私有字段写入改为预编译 DynamicMethod 委托（装箱反射路径保留为兜底），每次射击不再分配装箱对象（[Harmony_AdvancedArmor.cs](../../1.6/Source/Features/AdvancedArmor/Harmony_AdvancedArmor.cs)）。
- 暴扣头 `DrawHeadAt` 解析结果（shadowless graphic/材质）实例缓存，null 不缓存保留重试语义，读档重置。

**巨企窗口（forcePause 模态打开期间）**

- `CorporateTradeContext.AvailablePawns` 补上与 `AvailableThings` 相同的 0.5s 缓存（原来每 GUI 事件全图枚举可售 pawn）。
- People 页：报价/名册/报价字典按版本缓存；花名册行内 O(行×报价) 的 `FirstOrDefault` 改字典；技能排序、健康分组（含匿名类型 GroupBy）、关系过滤按（pawn, 版本）缓存。
- Finance 页：抵押品候选列表+HashSet 按版本缓存，失效选择清理从 O(n·m) `Contains` 降为 O(n)。
- Trade 页：周购/收购列表的筛选（`ProductFactor` 含 TryGetComp）与排序按（模式, 搜索词, 分类, 排序, 版本）缓存。
- Missions 页：任务卡排序与 Title/Description/状态行（多参数 Translate）按（页签, 版本）缓存为显示模型。
- History 页：记录标题与测量高度按（版本, 宽度）缓存，绘制循环加滚动视口裁剪（原 200 条记录每 GUI 事件约 400 次 CalcHeight+Translate 全量执行）。
- `CorporateUI.Money` 银币后缀翻译一次复用。

## 验证结果

| 检查 | 结果 |
|---|---|
| Release 构建 + `Invoke-Phase6StaticValidation.ps1`（pwsh 7） | 构建成功，全部约 38 项静态检查（XML、类型引用、生命周期、补丁元数据、静态缓存注释、Scribe 默认值等）通过，无违规输出 |
| 资源检查 | 仍因缺少 42 张 FA 烘焙脸红贴图失败——2026-09-23 基线的既有问题，与本次改动无关，按 AGENTS 约定不擅自恢复用户删改的素材 |
| `git diff --check` | 通过（仅仓库既有 CRLF 提示） |
| 未做 | 游戏内 TPS/FPS/GC 采样；投掷、骑乘炮塔、轮盘轮射、巨企各页的游戏内回归验证 |

完整日志：`TMP/validation-2026-09-24.log`。

## 建议的游戏内回归点

按验证手册抽查：投掷不可放置大建筑的失败→腾地→放置链；轮盘穿戴/脱下自装弹装甲后的背负布局切换与轮射重启；骑乘换武器后炮塔开火；巨企 People/Trade/Missions/History 各页打开、搜索、排序、买/卖/领取操作后的即时刷新。历史页滚动与窗口拉伸后的布局也应目检。

## 遗留未处理（有意）

- `Textures/UI/Mugirl_MilkGauge.png`（1264×919，约 4.43 MiB 无法压缩）与 `Mugirl_Slavegauntlet.png`（350×350，约 0.47 MiB）尺寸非 4 整除——涉及视觉输出与资源校验基线，需单独确认处理方式后另行实施。
- `Comp_WeaponWheel.TickWeaponOpenState` 每 tick `CarryWeaponOpenly`——降低频率会推迟开/收枪动画时点，属可见行为变化，未动。
- `Mugirl_XenotypeFix_GameComp` 读档后一次性 `All_AliveOrDead` 扫描——已有完成标志，属一次性成本，未动。

## 追记（同日）：FA 资源检查基线修正

上文"资源检查仍因缺少 42 张 FA 烘焙脸红贴图失败"一项已查证并解决，当时记录的失败原因保留如下事实：那不是资源缺失，而是检查器编码了 0.6.4–0.6.5 已废弃的"烘焙完整头图"方案。

- 证据链：42 张烘焙头图由 0.6.4 新增、0.6.5 更新、0.6.6（用户本人提交）随 Talos 方案切换删除，并新增 `blush/lovinblush_cover_east/south`、将 highlight 清为 70 字节占位、注释 `EmotionShapeDef lovinblush` 与 `EyeballShapeDef Mugirl_heart`；动画 XML 现有 `headShapeDef=blush/lovinblush` 引用 21 处仍有效，反编译 FA 1.6 程序集确认基础头经 `altShapeDef` 回退 normal、`{shape}_cover` 叠层照常解析绘制。
- 参考包比对：`TMP/FacialAnimation_0921_Talos修改版` 与 `1.6/FacialAnimation` 717 个文件清单一致，707 张 PNG 及其余 XML SHA256 逐字节一致，唯一差异为 `Mugirl_Shapes.xml` 3 行行尾空格（仓库侧已清理）。当前内容即权威参考。
- 基线修正：`Invoke-TextureAssetValidation.ps1` 的 FA 脸红契约改为"每个 `Normal~Normal7/Female` 齐备 `blush/lovinblush` 的 `cover_east/south`（28 张）；`{shape}_north/east/south/west` 烘焙头图与 `Emotions` 下旧脸红素材不得回流"；`docs/features/facial-animation-guide.md` 新增 2026-09-24 基线小节并同步修订特殊层规则、图层表、目录清单、引入矩阵与验证要点；README 已知基线段已更新。
- 修正后 `Invoke-Phase6StaticValidation.ps1 -SkipBuild` 整体通过，含 FA 资源检查（本轮首次通过）。
- 注意：Talos 参考包目前仅存于 `TMP/`（按约定核实后会清理）。若需长期保留，应移入 `DevData/Assets/`；未移动前请勿清理 TMP 中该目录。
