**Mugirl 1.6 性能审查 — 2026-09-07**

本文件保留优化前审查基线。后续实施与验证结果见 [性能优化实施记录](performance-optimization-2026-09-07.md)。

最明确的资源问题是非标准尺寸贴图无法进入原版压缩分支，以及 FA 资源无条件加载。持续逻辑的优化候选是产奶状态维护、绳索索引重建、骑乘空闲检查和部分兼容层反射。生成角色后的重复图形失效可能造成集中生成时的短时尖峰。以下顺序综合考虑覆盖范围、实施成本和行为风险，不代表实测耗时排名。

本次审查了 1.6/Source 的结构与高频调用路径：共 210 个 C# 文件、31,108 行；并检查加载清单、PNG 文件头和相关游戏程序集。没有启动游戏做耗时、TPS、FPS 或内存采样，没有修改代码、贴图、配置和 DLL。本文是静态审查结果，不能据此给出整个 mod 的 CPU 百分比或提速百分比。

当前 rimsearcher Def 快照没有 Mugirl，涉及组件实际绑定、覆盖对象及可配置间隔的结论仍需用加载本 mod 后的合并 Def 验证。源码中的固定常量与默认配置已区分。安装目录 DLL 的 CompMooMilkable.CompTick、MapRopingIndex.MapComponentTick、PawnRenderingRefreshUtility.TickPendingRefreshes 已反编译抽查，与本次源码结论一致。

**1. 优先处理贴图尺寸及 FA 条件加载：主要改善加载、内存与显存。**

已读取根 Textures 和 1.6/Textures 的全部 PNG 文件头，排除 TMP、发布备份和 About 预览：

| 项目 | 数量或大小 |
|---|---:|
| PNG 总数 | 1,389 |
| PNG 磁盘大小 | 26.39 MiB |
| 512×512 | 912 张 |
| 555×555 | 358 张 |
| 555×555 格式 | 全部为 8-bit RGBA PNG |
| Textures/FA | 707 张，其中 322 张为 555×555 |
| 全部 PNG 按 RGBA32 基础层折算 | 1,370.13 MiB，未计压缩、覆盖去重或 mipmap，非实际占用 |

当前游戏的 ModContentLoader.LoadTextureViaImageConversion 仅在开启纹理压缩且宽、高都能被 4 整除时压缩纹理。555×555 不满足条件。因此这 358 张图片的 RGBA32 基础层数据为 `358 × 555 × 555 × 4 / 1024² = 420.66 MiB`；完整 mip 链理论上约增加三分之一。它是像素数据估算，不是进程工作集或实际显存测量，图集、运行时格式和其他 mod 补丁还会影响结果。

此外，[LoadFolders.xml:4](../../LoadFolders.xml#L4) 无条件加载根目录，FA 的条件目录只覆盖 1.6/FacialAnimation。原版 ModContentHolder.ReloadAll 会枚举并加载内容文件，不等贴图被 Def 使用后再加载。因此根 Textures/FA 的资源在未启用 FA 时也进入原版加载流程。

建议先核对合并 Def 的引用，再将 FA 专用资源移到 1.6/FacialAnimation/Textures/FA，保持虚拟路径 FA/...，避免复制后仍留下根目录副本。C# 中未发现 FA/... 路径引用，但尚未取得当前模组合并 Def，所以整批移动前仍需验证非 FA 路径没有依赖。

需要继续保留的 555 素材，可统一整图等比例重采样到 512×512；特别精细的资源可另选分辨率或评估压缩 DDS。保留各图层的归一化位置与透明留白，不直接裁边，也不因像素尺寸变化同比修改 drawSize。验证正面眼白、眼皮、眉毛、嘴、头底的叠合，以及压缩后细线与透明边缘。仅减小 PNG 文件体积不能解决解码后的纹理开销。

**2. 产奶状态维护每 tick 扫描 Hediff：主要影响大量产奶角色的模拟 CPU。**

[CompMooMilkable.cs:176](../../1.6/Source/Features/Milk/CompMooMilkable.cs#L176) 每次 CompTick 都调用 EnsureLactationHediff；其第 197–205 行重新判断 Active 并执行 HasHediff。基类产量累计的 60 tick 节流没有覆盖这段维护。已反编译原版 HediffSet.HasHediff，确认它线性查找 hediffs，找到后早退。

对 N 个满足条件的人物、每人 H 个健康状态，每个逻辑 tick 最坏约 O(N×H)，不代表所有查找都会遍历到列表末尾。例：100 个组件在每秒各收到 60 次 Tick 时，每秒调用状态维护 6,000 次，这是调用数推算，非实测耗时。

建议在出生、成长、读档、状态激活或目标 Hediff 被移除时立即同步，平时用 60–250 tick 错峰校验兜底。缓存引用时必须处理移除和读档失效。当前注释明确要求尽快恢复哺乳 Hediff，不能只把检查延后而忽略行为变化。

**3. 牵引索引周期性扫描所有地图人物：主要影响多地图、动物多或牵引组多的存档。**

[MapRopingIndex.cs:20](../../1.6/Source/Features/Roping/MapRopingIndex.cs#L20) 固定每 250 tick 清空索引并扫描该地图 AllPawnsSpawned，即使没有雪牛娘绳索也扫描。RegisterPawnRope 第 84 行重新分配牵引者列表。另有逐牵引者的 250 tick 修复；相同关系重新注册时仍先 Remove 再 Contains/Add，产生重复工作。

建议沿用已有牵绳/断绳通知做增量维护，对已经一致的关系快退并复用列表；兜底改为更长周期或分批、错峰扫描。保留读档重建、跨地图、销毁清理和其他 mod 直接修改 RopeTracker 的修复能力。不能单纯因当前索引为空就永久停止扫描，否则可能漏掉外部新增关系。小地图的绝对收益可能有限，应结合采样决定改动深度。

**4. 生成角色后连续标记图形失效：主要影响大量角色同时生成后的短时帧耗。**

[Harmony_PawnGenerator_NewbornVisuals.cs:16](../../1.6/Source/Features/Newborn/Harmony_PawnGenerator_NewbornVisuals.cs#L16) 对满足条件的雪牛娘生成结果请求 queueRenderRefresh，不仅限婴儿；LifeStageVisualService 在 Biotech 启用且体型已正确时仍会入队。

[Harmony_GhoulRenderingRefresh.cs:41](../../1.6/Source/Features/Misc/Harmony_GhoulRenderingRefresh.cs#L41) 保留 300 tick 窗口，每 30 tick 重新 SetAllGraphicsDirty，通常每人约 10 次重试。实际游戏方法会清除已解析渲染树、绘制请求，并使头像与相关缓存失效；实际重建次数取决于期间是否重新解析和渲染，不能把每次调用都算成一次完整重建。

建议将重试限制到体型改变或 HAR 初始化尚未完成的对象，稳定成功后停止；可采用有限次数延后刷新。正常成长和读档未变化路径已有早退，应保留。验证批量生成、婴儿/儿童/成人和尸鬼转换，避免破坏此前用于解决初始化顺序的兼容逻辑。

**5. Yayo 兼容的反射在节流前执行：适合优先做的小改动。**

仅启用 Yayo's Combat 时成立。[YayoCombatCompatibility.cs:28](../../1.6/Source/Compatibility/WeaponWheel/YayoCombatCompatibility.cs#L28) 先读 AmmoEnabled，再判断征召、60 tick hash 和 job；AmmoEnabled 用 FieldInfo.GetValue 读取 bool，存在反射和装箱。该函数挂在轮盘每次 CompTick 上，未征召人物也可能到达设置读取。

将征召、job、hash 检查移到 AmmoEnabled 之前即可：合格对象的设置读取最多每 60 tick 一次，未征召对象不再读取。无须长期缓存可变设置。减少的是这项反射调用次数，不能称为 mod 整体提速 60 倍。

**6. Melee Animation 兼容在骑乘近战绘制时分配对象：影响相关场景的 FPS 和 GC。**

仅启用 Melee Animation 时成立。[MeleeAnimationCompat.cs:193](../../1.6/Source/Compatibility/MeleeAnimation/MeleeAnimationCompat.cs#L193) 每次骑乘近战武器绘制都会调用 SuspendIdleWeaponAnimation；当对方 AnimateAtIdle 开启时，反射读写 bool 并 new RestoreAnimateAtIdle。开销随可见骑乘近战人数和绘制频率增加。

建议缓存强类型字段访问器，用具体值类型的 Harmony __state 记录原值和恢复标记，避免通过 IDisposable 再次装箱。必须保留 Postfix/Finalizer 的只恢复一次及嵌套调用语义。不能为了省分配而让异常路径留下全局设置被更改。

**7. 骑乘瞄准的空闲路径可以更早返回：先简化空闲，再测量射线检查。**

[MountedPawnCombatTurret.cs:139](../../1.6/Source/Features/Mounting/MountedPawnCombatTurret.cs#L139) 的 TickAim 在完整资格检查、取武器和 caster 维护后，才检查是否有瞄准目标。无目标且没有施放/连射状态时可用更短路径返回。有目标时仍需及时取消无效目标。

若大型骑乘战斗实测射线检查较重，再评估单次搜索内结果缓存或短间隔校验，开火前完整复核。不要粗暴降低 VerbTick 频率，原版在其中推进连射倒计时和持续特效。自动寻敌已经有间隔、瞄准/连射/冷却门槛，并非每 tick 全图寻敌。

**其他次要候选与需先核实的路径。**

| 位置 | 发现和建议 | 限制 |
|---|---|---|
| [HediffComp_CureFoodEffects.cs:41](../../1.6/Source/Features/Milk/HediffComp_CureFoodEffects.cs#L41) | 每 tick 对目标疾病列表重复查 Hediff；考虑状态变化触发或短间隔兜底 | 当前缺合并 Def，绑定及目标数未验证；后续 Tick 实现持续免疫，不能直接删除 |
| [WorkGiver_GatherMilk.cs:24](../../1.6/Source/Features/Milk/WorkGiver_GatherMilk.cs#L24) | Active/奶量拒绝放在设备扫描前；需要时缓存无设备结果 | 保留 forced 与自动挤奶阈值的区别，穿脱设备时失效 |
| [IncidentWorker_Mugirl_Migration.cs:380](../../1.6/Source/Features/Incidents/IncidentWorker_Mugirl_Migration.cs#L380) | 被牵引、正在进食或不可打断任务等资格判断放在找植物前 | 已有 300 tick 及首次随机错峰，仅影响事件角色；保留重试时机 |
| [QuestPart_RunawayFarm.cs:569](../../1.6/Source/Features/Incidents/QuestPart_RunawayFarm.cs#L569) | 240 tick 节流仅覆盖任务完成判断，移动维护几乎每 tick 执行；可独立降频 | 固定仅 5 人；正常 GotoWander 时只是条件检查，不能称作每 tick 必然寻路 |
| [BrainwashPerformancePlayer.cs:90](../../1.6/Source/Features/Restraints/BrainwashPerformancePlayer.cs#L90) | 表演配置和结束时间可以开始时缓存、读档重建 | 只影响正在表演的对象；预计收益较小 |

**已有优化应保留，不建议作为首轮重写对象。**

- 产量按经过时间每 60 tick 累计；武器轮盘维护有 60 tick 按 pawn 错峰、备用 Verb 和背部武器的 dirty 缓存。
- 机械尾使用两张 mesh、复用数组，并按 tick、朝向和版本缓存，没有独立的 CompTick。
- 挤奶使用原生 AnimationWorker，人体网格测量在互动开始时执行，状态有结束、离图和读档清理。
- 无绳索、无待刷新人物、无洗脑表演均有快速返回。异种修复的全世界扫描完成后以存档标志停止，不是持续全局扫描。
- Dual Wield 优先使用缓存 delegate；原版 FadedMaterialPool 有材质缓存。不能把调用它们一概认定为每帧创建数组或材质。
- 原版 RopeTracker 同样调用 CanReach，且 Reachability 有相邻、同区域和缓存快路径。CanReach 不等于每次重新执行完整寻路。

**建议的验证顺序。**

1. 先建立基线：相同游戏与依赖版本、测试地图、人物和镜头；比较当前构建与单项优化构建。不要通过从现有存档直接移除本 mod 来制造基线。
2. 资源测试分别启用/禁用 FA，每轮完整重启，记录启动至菜单时间、进地图时间、进程内存、GPU 内存，并检查贴图叠合和透明边缘。不要把磁盘 PNG 大小作为显存指标。
3. 模拟测试使用 0/20/100 个相关人物，覆盖普通劳动、产奶、较多动物的多地图牵引、骑乘战斗。预热后采样约 60 秒并重复 3 次，记录实际 TPS、平均及较高分位 tick 耗时、每帧/每 tick 分配量。
4. 单独测试批量生成后的前 300 tick、Yayo 补弹、Melee Animation 骑乘近战绘制。前者看尖峰和渲染树失效次数，后两者看方法调用数与 GC。
5. 完成优化后核对产奶总量、Hediff 恢复时机、绳索读档与跨地图、骑乘连射和目标取消。性能数值与功能行为都通过后，再决定是否扩展改动。

**程序集证据。**

本次通过 DecompilerServer 核对安装目录的真实程序集，不使用参考程序集代替实现：

- 游戏：RimWorldWin64_Data/Managed/Assembly-CSharp.dll，MVID `61e4173561894da49d210260257b5097`。主要核对 ModContentHolder.ReloadAll、ModContentLoader.LoadAllForMod/LoadTextureViaImageConversion、HediffSet.HasHediff、PawnRenderer.SetAllGraphicsDirty、PawnRenderTree.SetDirty，以及 Verb、AttackTargetFinder、Reachability 和 FadedMaterialPool 相关实现。
- 模组：1.6/Assemblies/MugirlRace.dll，MVID `e157c9322f2b45d586570e50ece81301`，文件修改时间 2026-08-23 16:39:30，大小 514,560 字节。仅对关键热点做源码/DLL 一致性抽查，不声称已经逐方法比对全部程序集。

本次仅新增此审查记录。未进行游戏内性能采样，未执行构建或改变已安装模组行为。
