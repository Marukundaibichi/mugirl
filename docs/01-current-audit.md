# 当前代码审计结论

本文档记录第一轮结构审计发现的问题。它不是最终逐行审查结果；正式重构时仍需按 `05-line-review-checklist.md` 对每个文件逐行过一遍。

## 模块现状

当前 C# 源码已经收敛到 `1.6/Source`，主要模块如下：

- `Core`：启动入口、日志、文本翻译、Def 缓存、patch 注册、tick 工具。
- `DefOf`：核心 DefOf 与内容 DefOf。
- `Features/Apparel`：服装标签等通用服装工具。
- `Features/Restraints`：束具、锁、钥匙、脑控头盔、电击项圈、磁力镣铐、奴隶服装、R18 元数据、束具 hediff 效果。
- `Features/Milk`：产奶资源、挤奶、自动挤奶设备、奶量 UI、哺乳、奶制品效果、挤奶动画。
- `Features/Incidents`：开局坠机、逃脱奴隶、野人/流浪者/救援舱加入、巨型企业快递事件。
- `Features/Roping`：牵引、拴墙、被牵引 AI、禁止逃跑、约束 UI、牵引绘制。
- `Features/Mounting`：骑乘容器、骑乘绘制、骑手武器、自动射击、近战支援。
- `Features/Genes`：异种修正、能力强制 job、冲锋能力。
- `Features/Newborn`：出生和幼年外观修正。
- `Features/Misc`：污染、禁用、渲染刷新、药物配方等横切补丁。
- `UI`：Mod 设置界面。

源码路径应保持纯英文目录和文件名，中文说明保留在中文注释中。

## 入口与初始化问题

当前至少存在多个初始化入口：

- `MooGirlMod : Mod` 调用 `new Harmony("MooGirlMod.Mod").PatchAll()`。
- `RopedMooMod : Mod` 再次调用 `new Harmony("RopedMoo").PatchAll()`。
- `AiGenerated_Init` 静态构造只打日志。

问题：

- 多入口会让 patch 顺序和重复 patch 风险变高。
- Harmony ID 分散，不利于兼容排查。
- 设置、日志、兼容检测、Def 缓存没有统一生命周期。
- `RopedMooMod` 还会在正常加载时输出 `Roped loaded`，属于生产日志噪音。

重构要求：

- 保留一个 `MooGirlMod` 入口。
- 用一个 `MooGirlBootstrap` 完成 patch 注册、设置加载、Def 缓存初始化、兼容检测。
- Harmony patch 按模块注册，禁止全项目无边界 `PatchAll()`。

## 生产代码污染

当前项目编译项中包含“备份”“不能用”等文件，例如电击项圈备份类。还有 `AiGenerated_DefOf`、`AiGenerated_Init` 这类命名显示代码来源未被人工归档。

问题：

- 未使用或不能用的源码继续编译，会增加冲突面和认知负担。
- 备份文件容易与正式类产生类型名、逻辑或 Def 配置混淆。
- AI/临时代码命名会降低长期维护信心。

重构要求：

- 删除生产项目中的备份源码。
- 如确需参考，移到 `docs/archive/` 的文字说明或依赖 Git 历史。
- 所有类名必须表达业务职责，而不是来源或临时状态。

## 性能风险

已发现的高风险模式：

- 产奶组件在 `CompTick()` 中多次 `DefDatabase<HediffDef>.GetNamedSilentFail(...)`。
- 部分逻辑在 tick 中遍历地图 pawn、全局 pawn 或所有物品。
- `CompMooHasBodyResource` 内乳房大小判断重复出现多次，且每次都做 hediff 字符串查询。
- 牵引绘制/拴墙逻辑使用 `mapPawns.AllPawnsSpawned.FirstOrDefault(...)` 查找 follower。
- 事件组件中存在全局 pawn 扫描和旧档迁移逻辑。
- 骑乘炮塔为了驱动骑手武器，可能按 `delta` 循环调用 `verb.VerbTick()`。

重构要求：

- 所有 Def 字符串查询改为集中缓存。
- 运行期频繁查询改为事件驱动或状态索引。
- 每 tick 逻辑必须有最小化执行条件；默认使用 `CompTickInterval`、`IsHashIntervalTick` 或模块调度器。
- LINQ 不用于热路径。
- 对需要全图扫描的逻辑建立 MapComponent 索引或按低频计划执行。

## 存档状态问题

当前存在多个 GameComponent 和 ThingComp 存状态：

- `MooGirl_GameComp`：事件、派系、旧档修复、任务恢复。
- `MooGirl_XenotypeFix_GameComp`：异种修正定时扫描。
- `GameComponent_BrainwashPerformance`：脑控演出状态。
- `MooGirl_ConvertComp`：一次性转换。
- 多个装备 Comp 保存 tick 计数、冷却、启用状态。

问题：

- 旧档迁移逻辑和新游戏运行逻辑混在一起。
- Scribe 字段名分散，语义不统一。
- 静态集合如 `RopeStateTracker.PendingRope` 没有存档和生命周期边界。
- 骑乘炮塔用静态 Dictionary 记录 `Verb` 原始 caster，若卸载、销毁、异常中断，恢复风险较高。

重构要求：

- 按业务域建立状态所有者：`MooGirlGameState`、`MooGirlMapState`、装备/hediff 自己的局部状态。
- 不为旧档迁移保留字段。
- 静态状态只允许缓存只读 Def、反射句柄、短生命周期无存档临时对象。
- 所有状态必须有恢复和清理路径。

## Harmony 风险

当前 patch 覆盖范围广，包括 Pawn 生成、年龄阶段、渲染、Gear UI、奴隶反叛、门、逃跑 AI、污染、植物 TickLong、药物配方、育儿、技能学习、武器 Warmup 等。

问题：

- 横切范围大，容易与其他 mod 冲突。
- 部分 patch 是 Prefix 并阻断原方法，兼容风险高。
- 部分 patch 通过反射找内部方法，缺少版本适配层。
- 兼容 patch 和核心 patch 没有分层。

重构要求：

- Patch 分为 Core、Feature、Compat、Diagnostics 四组。
- 每个 patch 都必须有 `Prepare` 或模块级启用条件。
- 尽量使用 Postfix 或数据扩展，Prefix 阻断必须说明理由。
- 反射 patch 集中在 `Compatibility` 或 `ReflectionAccess` 中，不散落业务类。

## Def/XML 风险

当前 Def 侧最大文件包括种族、身体、服装、束具、hediff、think tree、武器和奶制品。风险如下：

- 大文件承担多个职责，难以逐项审查。
- 命名有 `Bongdage`、`Apperal`、`Gloden`、`Defof`、`Harmoney`、`NewBron` 等拼写问题。
- `LegacyDefs.xml` 与当前不保证旧档兼容的目标冲突。
- Bio_1.6、Versions/Integrations、1.6 主目录的内容边界需要重新定义。
- Def 与 C# 通过字符串互相依赖，缺少集中校验。

重构要求：

- Def 文件按功能域和 Def 类型拆分，不按“后来新增”命名。
- 拼写错误在新架构中修正；若改 defName 会改变行为/兼容，需要单独确认。
- `LegacyDefs.xml` 默认删除或仅保留在归档文档中。
- 所有 Def 依赖进入 DefOf/DefCache/ModExtension 三层之一。

## 玩家可见行为风险

以下行为在重构中需要特别保护，除非先确认：

- 产奶速度、产量、最大奶量、自动挤奶阈值、奶制品效果。
- 束具锁定、解锁、开锁钥匙、脑控、电击、磁力镣铐周期。
- 牵引距离、拴墙、逃跑禁止、牵引 UI。
- 骑乘可用条件、自动下马条件、骑手射击/近战频率、绘制层级。
- 开局坠机、野人/流浪者/救援舱加入、快递任务、派系关系和奖励。
- 基因/异种修正、出生性别和幼年外观处理。
- R18 内容已按用户确认改为常驻；不得恢复成人内容开关、过滤或清理逻辑。

## 初步优先级

P0 必须先做：

- 单入口化。
- 删除编译中的备份/不能用源码。
- 生成数值快照。
- 建立 Def 缓存和日志。
- 切断旧档迁移逻辑。

P1 必须在模块迁移前做：

- 建立模块接口、patch registry、tick scheduler。
- 统一命名规范。
- 为热路径列性能预算。

P2 按模块迁移：

- Milk 先迁，因为它有明显热路径问题。
- Restraints 第二，因为它连接服装、hediff、UI、事件。
- Roping 与 Mounting 需要一起审，因为二者互斥条件多。
- Incidents/Quests 最后迁，因为它牵涉 Def、QuestScript、Faction 和玩家可见叙事。
