# 目标架构

目标架构以“一个入口、多个业务模块、集中基础设施、显式兼容层”为核心。仍然产出一个 RimWorld 1.6 DLL，但源码组织和运行时责任必须模块化。

## 顶层目录

当前源码目录：

```text
1.6/Source/
    MooGirlRace.csproj
    MooGirlRace.sln
    Core/
    DefOf/
    Features/
        Apparel/
        Milk/
        Restraints/
        Roping/
        Mounting/
        Incidents/
        Genes/
        Newborn/
        Misc/
    Properties/
    UI/
```

建议命名空间：

```text
MooGirl
MooGirl.Core
MooGirl.Data
MooGirl.Defs
MooGirl.Patching
MooGirl.Features.Milk
MooGirl.Features.Restraints
MooGirl.Features.Roping
MooGirl.Features.Mounting
MooGirl.Features.Incidents
MooGirl.Features.Genes
MooGirl.Features.Rendering
MooGirl.Compatibility.*
MooGirl.Diagnostics
MooGirl.UI
```

## 启动流程

只保留一个 Mod 入口：

```text
MooGirlMod
    -> MooGirlSettings.Load
    -> MooGirlBootstrap.Initialize
            -> ModEnvironment.Scan
            -> MooGirlDefCache.Initialize
            -> FeatureRegistry.RegisterAll
            -> PatchRegistry.ApplyEnabledPatches
            -> Diagnostics.ReportOnce
```

要求：

- 不再有第二个 `Mod` 子类。
- 不再在任意静态构造里散落 `Log.Message`。
- 初始化只执行一次，热重载或返回主菜单后重新开局不能残留静态状态。

## 基础设施

### Def 访问层

分三层：

1. `MooGirlDefOf`：必须存在的核心 Def。
2. `MooGirlOptionalDefs`：DLC 或第三方 mod 可选 Def，使用 `GetNamedSilentFail` 一次性缓存。
3. `MooGirlDefExtensions`：内容侧扩展，用 `DefModExtension` 让 XML 驱动 C# 行为。

禁止：

- 在 `CompTick`、`JobDriver` 热路径、Harmony patch 热路径里直接字符串查 Def。
- 在多个文件重复硬编码同一个 defName。

### 日志

建立 `MooGirlLog`：

- `Debug`：只在 DevMode 或设置启用时输出。
- `WarnOnce`：相同 key 只输出一次。
- `Error`：保留堆栈和模块名。
- 正常加载不输出无意义成功日志。

### Tick 调度

建立轻量调度约束，而不是让每个类随意 tick：

- ThingComp 局部状态使用 `CompTickInterval(int delta)`。
- 跨 pawn/map 查询使用 `MapComponent` 索引。
- 全局一次性修正只在新游戏初始化或明确事件触发时执行。
- 周期性检查必须记录 tick interval 和原因。

### Patch Registry

每个 patch 有元数据：

- 模块名。
- 目标方法。
- patch 类型：Prefix/Postfix/Transpiler/Manual。
- 是否阻断原方法。
- 启用条件。
- 兼容风险。

Patch 注册由模块集中声明，避免全项目盲目 `PatchAll()`。

## 业务模块

### Milk

职责：

- 产奶资源状态。
- 手动/自动挤奶。
- 奶量 UI。
- 哺乳和奶制品 hediff。
- 挤奶动画和特效。

目标设计：

- `MilkSourceComp` 只管理 fullness 和产出。
- `MilkProductionRules` 计算倍率，缓存乳房 hediff 分类。
- `MilkingDeviceComp` 管理设备存储与释放。
- `MilkInteractionService` 提供喝奶、喂奶、哺乳、采集等统一入口。
- `MilkVisuals` 只管动画、mote、sound。

热路径要求：

- 产奶增长按 interval 累计，不逐 tick 做 Def 查询。
- 乳房类型判定使用缓存分类，hediff 变更时刷新或低频刷新。
- UI 字符串本地化，不硬编码中文或英文消息。

### Restraints

职责：

- 奴隶服装锁定/解锁。
- 高级束具状态。
- 电击项圈、磁力镣铐、脑控头盔。
- 束具 hediff 和 thought 效果。
- 钥匙、破解、开发者操作。

目标设计：

- `RestraintApparel` 只表达“可锁/可解/状态”。
- `RestraintLockState` 作为可存档状态对象。
- `RestraintEffectRunner` 执行 hediff/thought/mental/trait 效果。
- `AdvancedRestraintComp` 作为高级束具基类。
- 电击、脑控、镣铐实现为明确状态机：Idle、Armed、Active、Cooldown、Blocked。

要求：

- 移除 `Apperal` 拼写在新 C# 类型中的继续扩散。
- 旧 defName 是否改名单独确认，因为这会影响内容引用和旧档。
- 所有周期、概率、hediff 严重度保持不变，先记录后迁移。

### Roping

职责：

- 牵引关系。
- 拴墙。
- 被牵引 AI。
- 约束 UI 与逃跑/开门行为。
- 绳索绘制。

目标设计：

- 用 `RopeLink` 表达 roper、ropee、target spot/building、状态。
- 用 `RopingTrackerComp` 或 `MapRopingIndex` 查询当前地图牵引关系。
- 绘制逻辑只读 `RopeLink`，不扫描全图找 pawn。
- AI job 只通过服务查询是否被牵引。

要求：

- 与 RimWorld 原生 rope/restraints 的交互必须通过兼容封装。
- Prefix 阻断逃跑/开门等行为时必须写明条件，避免影响非 MooGirl pawn。

### Mounting

职责：

- 骑乘容器。
- 上马/下马。
- 骑乘绘制。
- 骑手生理 tick。
- 骑手武器射击和近战支援。

目标设计：

- `MountComp` 只管理容器和骑乘状态。
- `MountEligibilityService` 判断能否上马/下马。
- `MountedCombatController` 管理武器逻辑。
- `MountedRendering` 管理位置和绘制。
- `MountedStateCleanup` 负责销毁、卸载、掉落、异常恢复。

高风险点：

- 当前实现会临时修改 `Verb.caster`，必须包装为严格的 acquire/release 生命周期。
- 静态 `Dictionary<Verb, Thing>` 必须可清理，地图卸载、装备切换、pawn 消失时不能残留。
- 自动射击算法不能改变射程、warmup、cooldown 等数值；如为了修 bug 需改变，先提案。

### Incidents

职责：

- 开局坠机。
- 野人/流浪者/救援舱加入。
- 巨型企业快递事件。
- 巨型企业派系与敌对 raid。
- 快递对话和物资交付。

目标设计：

- `MooGirlStoryState` 保存事件是否已触发。
- `PawnGenerationService` 统一生成 MooGirl pawn。
- `ApparelSelectionService` 根据 PawnKind 标签选衣服，避免到处扫描 DefDatabase。
- `CourierQuestService` 管理快递任务状态。
- 旧档修复逻辑删除。

要求：

- 事件权重、奖励、物资数量、派系关系不变。
- 叙事文本移入语言文件。

### Genes

职责：

- MooGirl 异种与出生修正。
- 年龄/幼年体型修正。
- 能力 job。

目标设计：

- `MooGirlIdentity` 提供统一判定：是否 MooGirl pawn、是否 MooGirl body、是否相关出生。
- `XenotypeBirthService` 处理出生事件。
- `LifeStageVisualService` 处理体型和 backstory。

要求：

- Biotech 不启用时完全静默。
- 不清空玩家已有 xenogene，除非明确确认行为变更。

### Compatibility

兼容层必须独立：

- `Compatibility.AlienRace`：HAR body/graphic/render node 相关。
- `Compatibility.FacialAnimation`：仅 XML/patch 和必要 C# glue。
- `Compatibility.SearchAndDestroy`：搜索摧毁兼容 patch。
- `Compatibility.VCookE`：流程/食谱兼容。
- `Compatibility.OtherMods`：乳房 hediff、body part、污染等第三方 Def 识别。

要求：

- 缺少目标 mod 时不报错。
- 可选 Def 只在兼容模块内解析。
- 兼容模块不得反向依赖业务模块内部状态，只能走公开服务。

## XML/Def 组织

建议重组：

```text
1.6/Defs/
    Core/
        Bodies/
        Race/
        LifeStages/
        Xenotypes/
    Features/
        Milk/
        Restraints/
        Roping/
        Mounting/
        Incidents/
        Factions/
        Apparel/
        Weapons/
    UI/
    Sounds/
    Thoughts/
    Hediffs/
    Recipes/
    Work/
    Compatibility/
```

原则：

- Def 文件名表达功能，不使用 `NewAdded`、`Advance`、`AiGenerated`。
- 数值迁移先生成旧/新对照，不能静默改变。
- Patch XML 按目标 mod 分目录。
- `LoadFolders.xml` 保持 1.6 主入口清晰，Bio/Integration 内容不混入主 Def。
- 所有玩家可见文本进入语言文件；C# 只能引用翻译键或 Def 字段，源 Def XML 使用英文原文加中文注释，并由 DefInjected 提供语言覆盖。

## 命名规范

C#：

- 类型名 PascalCase。
- 字段 private camelCase；常量 PascalCase 或 private const camelCase 统一决定后执行。
- 不使用中文目录名作为新源码目录。
- 修正 `Harmoney` -> `Harmony`、`Defof` -> `DefOf`、`Apperal` -> `Apparel`、`Bongdage` -> `Bondage`、`NewBron` -> `Newborn`。

Def：

- 是否改 defName 需要单独审批。
- label/description 可翻译文本进入语言文件。
- 兼容用 defName 明确前缀。

## 注释规范

重构后的代码必须有足够注释，但注释必须服务维护，不堆砌噪音。

硬性规则：

- 所有源码注释必须使用中文。
- 不新增英文注释、拼音注释、机器翻译腔注释或中英混杂注释。
- 迁移旧文件时，英文注释必须改为中文或删除；低价值中文注释也必须删除。

必须写注释的位置：

- 状态机：说明状态含义、状态转换条件、保存/恢复规则。
- 性能路径：说明为什么需要 tick、扫描范围、interval 选择和不能改成事件的原因。
- Harmony patch：说明 patch 目标、为什么 patch、为什么 Prefix/Postfix/Transpiler、对其他 mod 的影响边界。
- 反射访问：说明目标成员、失败时行为、版本变动风险。
- 兼容层：说明依赖哪个 DLC/mod、缺失时如何降级。
- 存档字段：说明字段代表的状态、默认值意义、是否允许旧档丢弃。
- 数值保护：当代码旁有概率、倍率、tick、数量、距离、伤害等值时，说明其来源或标注“保持旧版数值”。
- 非显然算法：说明不变量、边界条件、为什么不用更简单写法。

不需要写的注释：

- 不复述显而易见的赋值、返回、空判断。
- 不保留过期 TODO、调试说明、个人备注、生成来源说明。
- 不用注释掩盖命名不清；能用清晰类型/方法名表达的，先改命名。

## 本地化规范

- 所有玩家可见字符串必须使用翻译键。
- 设置项、按钮、Gizmo、FloatMenu、Message、Letter、InspectString、DevMode 文本、错误/诊断文本都属于本地化范围。
- 翻译键命名使用稳定前缀，例如 `MooGirl.Milk.*`、`MooGirl.Restraints.*`、`MooGirl.Settings.*`。
- 迁移硬编码文本时，语言文件中的中文文本必须保持原含义；任何文案语义变化都按玩家可见行为变更处理。
- 只有非显示用途的内部 key、defName、Scribe 字段名、日志去重 key 可以保留硬编码，但必须能证明不是玩家文本。

## 测试与验收

每个模块迁移完成需通过：

- 编译无警告或只有已记录的外部引用警告。
- DevMode 新档加载无红字。
- 模块核心工作流能完成。
- 数值快照对比无未确认差异。
- Harmony patch 列表和目标方法可审计。
- 长时间 tick 测试无刷日志、无明显 TPS 下降。
