# ADR-009: 命名与目录约定

状态：Accepted（2026-10-15）。取代 [ADR-004](ADR-004-root-namespace-markers.md)。

来源：

- [design.md](../refactor-1.7/design.md) §3，以及 [decisions.md](../refactor-1.7/decisions.md) §2（标签）、§4（C04、C18、C25、C26、C29）对它的修订。两者不一致时以 decisions.md 为准。
- R/R2已按`tools/RenameApply/specs/R`、`specs/R2`完成改名与归属整理，S模块实现、V验收与本地交付均已完成。实施细节与原计划冲突时，以decisions §7的文案、行为和保存状态守恒为先；本ADR于2026-10-16同步最终实现。

## Decision

### 命名空间与目录

- 命名空间形如 `Mugirl.<Area>[.<Feature>]`，每个功能一层。子文件夹只用来组织文件，不延长命名空间。可用的 Area：

  | Area | 说明 |
  |---|---|
  | `Mugirl.Core` | 核心 |
  | `Mugirl.Platform.*` | 不变（[ADR-006](ADR-006-unity-boundary.md)） |
  | `Mugirl.Mathematics` | 数学 |
  | `Mugirl.Rendering` | 视觉效果与 Pawn 效果 |
  | `Mugirl.Compatibility.<OtherMod>` | 兼容层；无后缀的 `Mugirl.Compatibility` 只放共享的兼容基础设施（C29） |
  | `Mugirl.SaveUpgrade` | 存档升级 |
  | `Mugirl.Features.<Feature>` | 功能 |

- 根命名空间 `Mugirl` 只允许 [ADR-008](ADR-008-public-api.md) 的公开 API 类型，并且只放在 `Source/MugirlRace/PublicApi/`。
- 禁止的命名空间段（会遮蔽游戏名字）：
  - `UI`、`Text`、`Log`、`Find`、`Gen`、`Widgets`、`Scribe`、`AlienRace`、`HarmonyLib`、`Harmony`；
  - 原版忽略的命名空间名（`RimWorld`、`Verse`、`LudeonTK`、`System` 等）；
  - Mugirl 代码用到的任何 Verse/RimWorld 类型短名，例如 `Apparel`。

  HAR 兼容放在 `Mugirl.Compatibility.HumanoidAlienRaces`。
- 服装例外（C25）：服装代码放在 `Features/Appearance/Apparel/`，命名空间是 `Mugirl.Features.Appearance`，不采用 design 中的 `Features/Apparel`。原因：C# 先在外层命名空间查找成员，然后才看 global using；如果存在 `Mugirl.Features.Apparel`，所有 `Mugirl.Features.*` 文件中的 `Apparel` 都会解析成这个命名空间，而不是 `RimWorld.Apparel`。
- 功能文件夹（`Source/MugirlRace/Features/`）：

  | 文件夹 | 范围 |
  |---|---|
  | `Corporate` | 含企业袭击点数分级（原 `Incidents/Raids`） |
  | `Incidents` | 剧情、事件、任务、运货员、惩罚者、迁移、出逃农场、野生 Mugirl |
  | `Milk` | 含原 Misc 中的产奶配方 |
  | `Abilities` | 原 `Genes/{Charge,Dunk,Shout,Throw,Shared}` |
  | `Xenotype` | 原 `Genes/Xenotype` |
  | `Appearance` | 含服装、发型、身体配件、描边 |
  | `AdvancedArmor`、`Restraints`、`Roping`、`Mounting`、`Lances`、`WeaponWheel`、`Weapons` | 名称即范围 |
  | `Grazing` | 来自 Misc |
  | `Social` | Nuzzle 与原 Misc 中的想法 |
  | `LifeStages` | 原 `Newborn` |
  | `Trade` | 原 Incidents 中的 StockGenerator |

  其他移动：
  - `Misc` 取消。
  - `Incidents/VanillaFixes` 移到 `Core/VanillaFixes`，`RenderRefresh` 移到 `Rendering`。
  - 共享的玩家派系、意识形态与生成 Pawn 辅助类移到 `Core`；与 Pawn 对话的基类也移到 `Mugirl.Core`（C18）。
- 默认每文件一个顶层类型，文件名等于类型名；partial 使用 `Type.Part.cs`，小的私有辅助类型可嵌套。`PublicApi/XmlAliases.cs` 是经过审阅的公开 XML 别名集合例外。
- `SaveUpgrade/` 的布局：
  - `Engine/**`：框架；
  - `Hooks/**`：读档钩子；
  - `Modules/<Module>/`：每个模块的升级表与步骤，一个文件夹一个所有者（[ADR-007](ADR-007-save-schema-upgrade.md)）。

### 类型名

- 采用原版的角色形状：
  - 组件：`CompX` + `CompProperties_X`、`HediffComp_X` + `HediffCompProperties_X`、`CompAbilityEffect_X`、`CompUseEffect_X`、`CompTargetEffect_X`、`CompTargetable_X`、`CompUsable_X`；
  - AI 与思考：`JobDriver_X`、`JobGiver_X`、`WorkGiver_X`、`ThinkNode_X`、`ThoughtWorker_X`、`Thought_X`；
  - 事件与任务：`IncidentWorker_X`、`QuestNode_X` / `QuestNode_Root_X`、`QuestPart_X`、`LordJob_X`、`LordToil_X`；
  - 其他：`PawnFlyer_X`、`PawnRenderNode*_X`、`StockGenerator_X`、`DamageWorker_X`、`Dialog_X`、`FloatMenuOptionProvider_X`、`GameComponent_X`、`MapComponent_X`；
  - 静态辅助类用 `XUtility`，DefModExtension 用 `<X>Extension`。
- `Mugirl.*` 命名空间中的类型名不带 `Mugirl`/`Mugirl_`，以下两种情况除外：
  - 名字本身表达“只针对 Mugirl”，例如 `Thought_MugirlOnly`、`JobDriver_MountMugirl`；
  - 去掉后会与同角色的原版类型重名，例如 `JobDriver_MugirlNuzzle`、`QuestNode_Root_MugirlRefugeePodCrash`。

  与 Verse 静态类重名的例外：`MugirlLog`、`MugirlText`、`MugirlMod`、`MugirlSettings`。
- 新内部名字不用 MooGirl 时代遗留的 “Moo”。永久公开 API 的 `CompProperties_MooMilkable` 与当前保留的 `CompMount.MooPawn` 门面不能据此机械删除；旧名字只在 PublicApi 契约和 SaveUpgrade 历史表中出现。
- 术语：代码中用 Restraint，不用 SlaveApparel/BondageGear；企业任务用 Mission，原版派生的 `QuestPart_`/`QuestNode_` 类型除外。
- 可见性：
  - 默认 `internal`，叶子类型加 `sealed`。
  - 只有两类类型用 `public`：引擎从 XML 或存档按反射实例化的类型（组件与属性、Worker、Driver、GameComponent、DefModExtension、Def 子类、Mod/ModSettings），以及有文档的公开 API。

### Harmony 补丁类

- 形如 `internal static class Harmony_<TargetType>_<TargetMember>[_<Purpose>]`，放在拥有该行为的功能中，例如 `Harmony_Pawn_DynamicDrawPhaseAt_Rider`、`Harmony_Pawn_EquipmentTracker_Notify_EquipmentRemoved_MountedVerbs`（`specs/R/combat.json`）。
- 每个补丁类都标注 `[MugirlPatch(module, risk, failureBehavior)]`。module 已是封闭枚举 `Mugirl.Core.PatchModule`（当前 27 个成员），不使用字符串。
- 同一目标、同一种类上有多个 Mugirl 补丁时，只能用互不相同的显式 `[HarmonyPriority]` 排序，不依赖类名排序或登记顺序。
  - 本 Mod 的补丁共用一个 owner，所以 `[HarmonyBefore]`/`[HarmonyAfter]` 只能用来对其他 Mod 的 Harmony id 排序。
  - 详见 [ADR-002](ADR-002-harmony-registration.md)。

### Def 名

- 本 Mod 定义的每个 Def 都命名为 `Mugirl_<PascalName>`。例外：
  - 公开 API 名字（ADR-008）；
  - 遵循 Facial Animation 生态约定的 FA Def（`<faName>_Mugirl` 与共享形状名）；
  - 原版派生的隐含名（`Blueprint_*`、`Frame_*`、`Corpse_*`、`Meat_*`、`Make_*`）。
- 类别段放在前缀之后（C26）：`Mugirl_Bullet_*`、`Mugirl_Filth_*`、`Mugirl_Mote_*`、`Mugirl_Fleck_*`。已发布旧名的映射由 SaveUpgrade 保存。
- 只在名字错误或不一致时改名：
  - 缺少或用错前缀：`Job_*`、`JobDriver_*`、无前缀、`AI_GC_*`、`Hediff_Mugirl*`、`Work_*`，以及非公开的 `PMC_*`/`OPC_*`；
  - 笔误，例如 `Mugirl_Bar` → `Mugirl_Bra`；
  - 含 “Moo”；
  - 含义误导。

  不因口味改名。
- 每次改名都要登记到升级表（ADR-007）。另有两条限制：
  - ThingDef/RecipeDef 的新名不能以数字结尾（物品与账单的加载 ID 会解析出不同的编号）；
  - ManeuverDef 与武器 tool id 会进入 Verb 身份，改动需明确身份升级；ToolCapacityDef 是否保存必须按真实 writer 核实，不能为过检查捏造 verb-ID 字段。
- 新 Def XML 文件按类型与主题命名。现存日期文件名与目录若参与加载顺序，不能为了外观规范直接改名；原版文件枚举不承诺按路径排序。移动、拆分须验证完整 Def 序列与行为，不能只比较 XML 集合。新抽象 Name 使用 `Mugirl_` 前缀，公开 API 除外。
- 删除内容必须有引用和行为证据。公开 XML 元数据、已有退役事件的安全读取行为、可被外部补丁使用的现存 Def，不能仅凭“没有 C# 读取方”清理。重构不改玩家文案或玩法；已授权的历史退役对象由专门升级步骤处置。

### DefOf

- 每个功能一个 `[DefOf]` 类，命名为 `<Feature>DefOf`：
  - 默认 `internal`，只有 XML 或反射需要时才 `public`；
  - 静态构造函数调用 `DefOfHelper.EnsureInitializedInCtor(typeof(<Feature>DefOf))`；
  - DLC 内容加 `[MayRequire]`；
  - 不保留没有使用的字段，Def 是否存在由验证器的内容契约检查。
- 所有者规则（C04）：DefOf 字段放在**定义该 Def 的功能**（拥有其 XML 文件的功能）的 DefOf 中，使用方引用那个类。种族核心 Def（`Mugirl`、`MugirlBody`、核心背景故事与 PawnKind）放在 `Mugirl.Core.MugirlDefOf`。
- `Mugirl_DefOf`、`MugirlContentDefOf`、`MugirlMilkDefOf` 已在 R2 解散。跨数据库同名字段需要明确绑定，例如 `SocialDefOf.Mugirl_NuzzleInteraction` 用 `[DefAlias("Mugirl_Nuzzle")]` 绑定 InteractionDef，JobDef 保持原字段名。

### Scribe 标签（新代码）

- 标签用 lowerCamel，引入时等于字段名，之后是冻结的字符串。验证器拿标签与旧版语料比较，而不是与字段名比较，所以字段改名不改标签。
- 在所属元素内部不重复所属者前缀。组件写在**共享元素**（与原版和其他组件共用的 pawn/thing/hediff 节点）中时：
  - 标签带 `mugirl` 前缀，例如 `mugirlMilk…`、`mugirlMount…`，因为第三方种族会把 Mugirl 组件与它们自己的 `milk…` 组件放在一起（decisions §2 修订了 design 的 `milk…` 前缀）；
  - 不得与原版标签（`def`、`id`、`health`、`story` 等）冲突。
- 相关状态放进专用对象，不用并行列表。可用 `Scribe_Deep`，也可在保持原回调顺序和对象身份的前提下同步读写嵌套状态；后者须记录 LoadingVars 实际进入的组，CrossRefs/PostLoadInit 不得读取没有登记的引用。
- 默认值：
  - `Scribe_Values.Look` 传入的默认值等于字段初始值；
  - 默认值取决于 Def 数据时使用 `forceSave: true`；
  - 运行时从 Props/Initialize 设置的值必须 `forceSave`。
- 不保存可安全重建的缓存。影响调度相位、累计时长、动画或已发生动作的计数器必须保存并迁移；Mount 四累计值、炮塔 aim total、飞行烟雾/镜头计数不是可随意丢弃的缓存。新业务字段通常存枚举或 ID 后翻译，现存文本字段则按原语义保留。
- 以下内容都属于存档结构，改动要配套升级步骤（ADR-007）：
  - 按名保存的枚举成员；
  - `GetUniqueLoadID()` 格式；
  - JobDriver 的 Toil 顺序；
  - ThinkNode 的类型短名、ThinkTreeDef 名字种子、Duty 展开和全局键分配顺序（它们决定 Job 保存的思考节点键）；
  - LordJob 状态图；
  - DefMap 保存的 Def 列表与短哈希 Def。
- 新的 GameComponent/MapComponent：
  - 提供 public `(Game)`/`(Map)` 构造函数，构造函数无副作用；
  - 把“已构造但从未 ExposeData”（加入旧存档时）当作有效状态处理。

现有 `CorporateNetwork` 构造期的债务覆盖重置是已审阅的行为冻结例外：BeforeLoad/BeforeNewGame 相对 Game 构造的时点不同，没有等价证据不能搬到 hub。允许项记录的是这一理由，而不是待完成的 TODO，见[Corporate 记录](../records/refactor-1.7-corporate-2026-10-15.md)。

### 代码

- 每局瞬时状态的清理写在 `Mugirl.Features.GameLifecycle` 中，按固定顺序显式调用（2026-10-22 起取代反射扫描的 `[GameLifecycleHook]` 注册表）。读档开始与新游戏生成前清理牵引待系登记；Storyline.FinalizeInit 原位置调用 `ResetTransientState`，依次清空日志限次键、挤奶动画、骑乘 verb 登记与延后渲染刷新。不能把清理移动到另一个组件的 FinalizeInit，也不新增未经证明的清理。
- Core 不依赖 Features：设置常量放在 Core，由功能读取。
- Compatibility 不对 Mugirl 自己的方法打 Harmony 补丁（[ADR-003](ADR-003-compatibility-scope.md)）。
- 日志只经 `MugirlLog`。运行时不调用 `DefDatabase.GetNamed*`，除非列在验证器的动态查找允许清单中。静态缓存带 `StaticCacheLifecycle:` 注释。
- 注释用中文（技术名保持英文），写当前的原因，不写历史；历史留给 git 与 `docs/records`。

## Context

ADR-004 允许以下类型留在根命名空间：XML 直接引用的类型、存档稳定类型、Harmony 补丁类型、DefOf 与 Mod 入口。前提是“改命名空间会同时破坏 XML、Scribe 类名与补丁元数据”。这些前提现在都不成立：

- 统一升级层（ADR-007）负责改写存档中的类型名与 defName。
- 补丁元数据改为 `[MugirlPatch]` 特性，不依赖类名。
- 依赖 Mod 使用的 XML 类名由 PublicApi 别名保留（ADR-008）。

所以可以给代码一套一致的命名，只保留公开 API 这一个例外。

## Enforcement

| 约定 | 检查 |
|---|---|
| 根命名空间、禁止的段、遮蔽类型短名 | 验证器 `B.namespaces`；历史允许项不是新增债务的许可 |
| 组件构造函数无副作用、有 public 构造函数 | `B.component-ctors` |
| `Scribe_Values.Look` 默认值等于初始值（按 IL） | `B.scribe-defaults`，债务在 `scribe-defaults.allow.txt` |
| 静态缓存注释 | `B.static-caches` |
| DefOf 绑定、静态构造 | `C.defof` |
| 补丁元数据与补丁顺序 | `B.patch-metadata`，`B.patch-order`（`Baselines/patch-order.txt`） |
| 日志、`Find`/`Current`、`GetNamed`、第三方类型查找、存档内部的位置 | `B.architecture`（`Baselines/architecture-rules.json`） |
| 改名规格本身（单射、冻结名字、命名空间段、数字结尾、Moo、所有者规则等） | `tools/RenameApply/specs/generators/lint_spec.py`（L01–L16） |
| 一文件一类型、`Mugirl_` 类型名规则、可见性、注释规则 | 只靠审阅（验证器尚未检查） |

## Consequences

- 已执行 R/R2 的规格和报告用于追溯，不能在新的树上盲目重放。后续重命名应重新产生适用于当前树的规格并 lint。
- 2026-10-22 起移除 `CompileOrder.props`、`TypeOrder.txt` 与构建后元数据重排工具，编译与类型顺序采用 SDK 默认顺序（[决策 §17](../refactor-1.7/decisions.md#17-代码质量优化授权2026-10-22)）。影响只限新开局中本模组游戏/地图组件的创建与 tick 先后，各组件之间没有顺序依赖；已有存档沿用存档中的组件顺序。代码不得依赖类型枚举顺序。
- R/R2 的原根命名空间债务已清理。新增问题须修实现，不能为了通过检查扩大允许清单。
- 改名在存档与 XML 中可见的名字时，要同时登记升级行（ADR-007），并确认它不在公开 API 清单中（ADR-008）。
