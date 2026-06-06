# 分阶段重构路线

本路线按“先建立可控环境，再迁移业务模块，最后删除旧架构”的顺序执行。每阶段都必须可编译、可检查、可回退。

## 阶段 0：冻结基线

目标：在动代码前建立事实基线，避免重构中无意改数值或行为。

工作：

- 生成 C#、XML、贴图、声音、程序集清单。
- 导出所有 Def 的关键数值快照。
- 导出所有 C# 常量、默认字段、tick 间隔、概率、伤害、产量、冷却等数值。
- 列出所有 Harmony patch 目标。
- 列出所有 Scribe 字段。
- 建立“行为保护清单”。

输出：

- `docs/baseline/file-inventory.md`
- `docs/baseline/def-values.csv`
- `docs/baseline/code-values.md`
- `docs/baseline/harmony-patches.md`
- `docs/baseline/scribe-fields.md`
- `docs/baseline/behavior-map.md`

验收：

- 所有数值都有旧值记录。
- 所有 patch 都知道目标方法和模块归属。
- 所有 GameComponent/ThingComp/HediffComp 存档字段都有记录。

## 阶段 1：项目与入口清理

目标：清理项目结构，但不改变业务行为。

工作：

- 创建新的源码目录结构。
- 保留唯一 `MooGirlMod` 入口。
- 移除第二个 `Mod` 子类和散落静态初始化日志。
- 从 `.csproj` 移除 bin/obj、备份、不能用、临时源码。
- 删除或归档旧档迁移专用代码。
- 统一输出路径和 Debug/Release 配置。
- 建立 `MooGirlBootstrap`、`MooGirlLog`、`ModEnvironment`。

禁止：

- 不改 Def 数值。
- 不改玩家可见行为。
- 不改 defName。

验收：

- 编译通过。
- 新游戏加载只 patch 一次。
- 日志不出现无意义加载成功刷屏。
- Harmony ID 只有统一入口。

## 阶段 2：核心基础设施

目标：建立后续模块迁移依赖的稳定底座。

工作：

- 建立 `MooGirlDefOf`、`MooGirlOptionalDefs`、`MooGirlDefCache`。
- 建立 `FeatureRegistry` 与 `PatchRegistry`。
- 建立 `MooGirlSettings`，保留原设置默认值。
- 建立 `MooGirlGameState` 和 `MooGirlMapState` 基类。
- 建立 `TickScheduler` 或明确的 tick interval 工具。
- 建立 `MooGirlIdentity`，统一 MooGirl pawn 判定。
- 建立 `ValueLock` 检查脚本或手工清单。
- 建立注释规范，并在新基础设施中示范必要中文注释：状态所有权、缓存生命周期、patch 边界、性能原因。
- 建立本地化规范：C# 中不得新增硬编码中文或英文玩家文本，必须使用翻译键；源 Def XML 使用英文原文、中文注释和 DefInjected。

验收：

- 业务代码能通过新基础设施访问 Def。
- 可选 Def 缺失不报错。
- Patch 注册可输出诊断清单。
- 设置项默认值与旧版一致。

## 阶段 3：Milk 模块迁移

目标：重写产奶、挤奶、哺乳和奶量 UI 的内部结构，同时保持数值不变。

工作：

- 将 `CompMooHasBodyResource` 拆成资源状态、产量规则、采集流程。
- 把乳房 hediff 分类从热路径字符串查询改为缓存规则。
- 统一 `CompMooMilkable`、挤奶设备、哺乳、喝奶、喂奶的消费接口。
- 把开发者 gizmo 文本、玩家文本、FloatMenu 文本、消息文本移入语言文件。
- 把 Milk 相关 Def 的玩家文本迁移到 `DefInjected`。
- 把喷乳特效、挤奶动画与资源逻辑分离。
- 消除重复的乳房大小判断。

必须保护的数值：

- `GatherFullnessPerUse = 0.2f`
- 默认 `MilkThreshold = 0.8f`
- 各乳房类型产量倍率和生长倍率。
- 产奶间隔、奶量、设备容量、释放数量、哺乳进度、奶制品 hediff。

可能需要确认的行为：

- 当前代码存在乳房倍率字段计算后未实际影响部分产出路径的情况。修正是否改变实际产量，需要先确认。
- 当前 `CompTick()` 自动添加哺乳期 hediff。是否改成事件驱动，会影响添加时机，需要先确认。

验收：

- 手动挤奶、自动挤奶设备、喝奶、喂倒地 pawn、哺乳、奶量 gizmo 全部可用。
- 新旧数值对照无未确认差异。
- 产奶 tick 不再反复查 DefDatabase。
- Milk 模块新增或迁移后的注释均为中文。
- Milk 模块新增或迁移后的玩家可见字符串均使用 Keyed 或 DefInjected。

## 阶段 4：Restraints 模块迁移

目标：把束具、钥匙、高级束具和 hediff 效果改成明确状态机。

工作：

- 重建 `RestraintApparel`、`RestraintLockState`、`RestraintEffectRunner`。
- 电击项圈、脑控头盔、磁力镣铐分别实现状态机。
- 移除备份电击项圈源码。
- 将 hediff/thought/trait/mental 效果执行统一入口。
- 将 R18 元数据与束具系统解耦；运行时 R18 内容常驻，不再设计隐藏、过滤或清理开关。
- 将开发者解锁和玩家解锁分离。

必须保护的数值：

- 锁数量、解锁条件、钥匙消耗。
- 电击概率、强力电击概率、冷却、伤害、hediff。
- 脑控周期、转换列表、文化转换条件。
- 镣铐启用/关闭周期、绑定 hediff、声音和消息。

已确认的行为变更：

- R18 设置按钮取消，R18 内容常驻。旧的地图/角色/商队/贸易库存成人内容清理逻辑删除，不再作为玩家可见功能保留。

可能需要确认的行为：

- 是否修正 `SlaveApperal` 拼写对应的 C# 类型和 XML class 引用。
- 高级束具破解成功后的选择、消息和选中 pawn 行为。

验收：

- 穿戴、锁定、解锁、钥匙、破解、脑控、电击、镣铐工作流通过。
- R18 XML 扩展和 comp 引用正常加载，但不会触发隐藏、过滤或删除。
- 非相关 apparel 不受 patch 影响。

## 阶段 5：Roping 模块迁移

目标：把牵引关系从 job 扫描推导，改为明确关系状态。

工作：

- 建立 `RopeLink` 和 `MapRopingIndex`。
- 上绳、下绳、拴墙、断绳统一修改关系状态。
- 绘制层只读取索引，不扫描全图 pawn。
- AI job、thought、door/escape/restraints patch 走统一服务判断。
- 移除静态 `PendingRope` 或改成有生命周期的 transient state。

必须保护的行为：

- MooGirl 被牵引时的 job 和 follow 行为。
- 拴墙后的限制。
- 被牵引时是否可开门、逃跑、显示约束信息。
- 牵引 thought。

可能需要确认的行为：

- 当前牵引通过 `RaceProps.body == MooGirlBody` 判定。是否改成 `MooGirlIdentity` 更宽松判定，可能影响 HAR/变体兼容。

验收：

- 牵引、解除牵引、拴墙、绘制、thought、逃跑阻止均可用。
- 大地图多 pawn 情况不再每次绘制扫描全图。

## 阶段 6：Mounting 模块迁移

目标：保留骑乘玩法，重建骑乘状态、绘制和战斗控制。

工作：

- `MountComp` 只保留容器和状态。
- `MountEligibilityService` 统一上/下马条件。
- `MountedCombatController` 管理射击和近战。
- `MountedVerbScope` 封装 `Verb.caster` 临时修改，保证异常也恢复。
- 装备切换、pawn 销毁、地图卸载时清理静态缓存。
- 与牵引模块共享互斥条件。

必须保护的数值：

- 生理 tick interval。
- safety check interval。
- turret tick interval。
- aim tick 计算、最小瞄准 tick、冷却。
- 绘制 offset、altitude。

可能需要确认的行为：

- 当前骑手射击以载体位置找目标并临时改 caster。若改为独立虚拟 caster 或自定义 verb，目标选择和友军误伤可能变化。
- 当前载体移动时多数情况禁止射击。若优化，需要确认。

验收：

- 上马、下马、销毁掉落、选择骑手、自动下马都正常。
- 骑手远程/近战行为与确认后的基线一致。
- 没有残留 `Verb.caster` 污染。

## 阶段 7：Incidents、Quests、Faction 模块迁移

目标：重建加入方式和巨型企业事件，删除旧档修复混杂逻辑。

工作：

- 建立 `MooGirlStoryState`。
- 重写开局坠机触发控制。
- 重写野人/流浪者/救援舱 pawn 生成服务。
- 重写快递事件服务、对话、物资掉落、赔偿逻辑。
- 巨型企业派系生成和 hostile/legacy 处理分离；旧档逻辑删除。
- 建立服装生成服务；R18 常驻后，apparel tag 候选缓存不再需要因成人内容开关失效，但仍必须处理 Def 加载和可选 mod 条件。

必须保护的数值：

- 事件是否默认启用。
- 点数、奖励、物资数量。
- PawnKind、装备标签、派系关系、raid tier。
- 任务信号、letter 类型和触发条件。

可能需要确认的行为：

- 旧 `MooGirl_GiantCorporations` 与新 hostile faction 的关系如何处理。因为不保证旧档兼容，可以删除 legacy 分支，但新档派系行为必须确认。

验收：

- 新档开局事件、野人加入、流浪者加入、救援舱、快递任务均可触发。
- 任务完成/拒绝/战斗分支无红字。

## 阶段 8：Genes、Rendering、Misc 迁移

目标：整理横切补丁和 DLC 条件逻辑。

工作：

- 异种修正改为出生事件优先，低频扫描兜底。
- 幼年体型和 backstory 处理纳入 `LifeStageVisualService`。
- Ghoul 渲染刷新、污染、禁用、药物配方等横切 patch 归类。
- 所有 DLC 条件用 `ModsConfig.*Active` 或兼容层封装。

必须保护的行为：

- Biotech 未启用时静默。
- 不清空已有 xenogene。
- 新生/幼年 MooGirl 外观正确。

验收：

- Biotech、Ideology、Anomaly 开关组合下不报错。
- HAR/AlienRace 相关 patch 只有目标存在时启用。

## 阶段 9：Def/XML 重组

目标：在不改数值的前提下重整 Def 结构。

工作：

- 按目标目录拆分 Def 文件。
- 删除或归档 `LegacyDefs.xml`。
- 清理 `NewAdded`、`Advance`、`AiGenerated` 等命名。
- 为每个业务模块建立 Def 对照清单。
- 对所有 XML class 引用进行新类型映射。
- 检查语言文件缺失项。
- C# 玩家文本迁移为翻译键；源 Def XML 玩家文本统一为英文原文加中文注释，并补齐 DefInjected。

必须保护：

- 数值不变。
- defName 是否变更需单独确认。
- texturePath、soundDef、thingClass、compClass 映射正确。

验收：

- XML 加载无红字。
- Def 快照对比无未确认差异。
- 语言缺失检查通过。
- C# 中无新增硬编码中文或英文玩家文本。

## 阶段 10：全量审查与发布整理

目标：删除旧架构残留，完成逐行审查和发布包清理。

工作：

- 按文件逐行审查。
- 按中文注释规范补齐注释，尤其是状态机、tick、Harmony patch、反射、兼容层、存档字段和数值保护点。
- 按本地化规范清理硬编码文本：C# 玩家可见字符串使用翻译键，源 Def XML 玩家文本使用英文原文加中文注释并配套 DefInjected。
- 跑长时间 DevMode 新档测试。
- 清理 TMP、bin/obj、重复贴图、未引用资源。
- Release 编译输出到 `1.6/Assemblies`。
- 更新 About、LoadFolders、版本说明。

验收：

- 全仓库无“备份”“不能用”“临时”“AiGenerated”生产命名。
- 无未解释 Harmony patch。
- 无英文源码注释。
- 无未本地化的 C# 玩家文本；源 Def XML 无翻译键占位，英文原文、中文注释和 DefInjected 完整。
- 无未确认数值变更。
- 新档核心玩法通过。
- 发布包不包含源码构建产物和临时素材，除非明确决定随包发布。
