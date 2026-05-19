# 雪牛娘骑乘系统实现计划

## 目标

为雪牛娘实现一个专用骑乘系统：人类或类人 Pawn 可以骑在雪牛娘头上，由雪牛娘负责地图移动、寻路和主要战斗判定。骑乘者在基础状态下作为乘客存在，不作为地图上的独立 Pawn 参与寻路、碰撞、被攻击判定或常规 AI。

系统优先级：

1. 性能最好。
2. 兼容性最高。
3. 行为边界清晰。
4. 尽量少 patch 原版核心逻辑。

## 最终设计基线

采用“容器化骑手 + 雪牛娘单 Pawn 模拟 + 骑手渲染 + 可选自动远程炮位”的结构。

骑乘时：

- 骑乘者从地图网格中移除。
- 骑乘者被雪牛娘的专用 `Comp_MooGirlMount` 持有。
- 地图上只有雪牛娘一个真实 spawned Pawn。
- 骑乘者绘制在雪牛娘头部锚点附近。
- 骑乘者没有独立碰撞、寻路、选中命中、攻击目标判定。
- 骑乘者默认不执行 AI，不接收常规工作。
- 骑乘者可以保留生理状态 tick，用于饥饿、困倦、hediff 等状态推进。

## 非目标

第一版不做以下内容：

- 不让骑乘者近战。
- 不让骑乘者手动指定攻击目标。
- 不让骑乘者使用能力、跳跃背包、灵能、基因技能、装备主动技能。
- 不保证 CE、弹药系统、双持、特殊武器、蓄力武器、复杂装备 comp 完全兼容。
- 不让敌人直接攻击骑乘者。
- 不把骑乘者作为独立地图 Pawn 暴露给原版 AI。
- 不实现其它 Pawn 对骑乘者使用技能、物品或其它效果。

## 核心模块

### 1. Comp_MooGirlMount

挂在雪牛娘 Pawn 上，负责骑乘关系、保存、tick、下骑、安全检查、绘制辅助数据。

职责：

- 持有骑乘者 `ThingOwner<Thing>`，限制最多 1 个 Pawn。
- 提供 `MountedPawn`、`HasMountedPawn`、`CanMount`、`Mount`、`Dismount` API。
- 保存和读取骑乘者。
- 处理雪牛娘死亡、倒地、销毁、换地图、被容器化等异常。
- 按低频间隔检查骑乘者需求和危险状态。
- 按低频间隔驱动自动远程炮位。
- 提供骑手绘制位置和武器绘制位置。

建议接口：

```csharp
public class Comp_MooGirlMount : ThingComp, IThingHolder
{
    public ThingOwner<Thing> innerContainer;
    public Pawn MountedPawn { get; }
    public bool HasMountedPawn { get; }

    public bool CanMount(Pawn rider, out string reason);
    public bool TryMount(Pawn rider);
    public bool TryDismount(IntVec3? preferredCell = null);
    public Vector3 RiderDrawPos { get; }
    public Vector3 WeaponDrawPos { get; }
}
```

注意：

- 不使用原版 `carryTracker`，避免占用搬运槽。
- 如果希望骑手生理继续变化，不实现 `ISuspendableThingHolder` 或保持 `IsContentsSuspended=false`，并由本 comp 明确 tick 必要内容。
- 不要让骑乘者完整跑 `JobTrackerTick`、`PatherTick`、`StanceTrackerTick`。

### 2. 骑乘与下骑 Job

新增 Job：

- `Job_MountMooGirl`
- `Job_DismountMooGirl`

上骑流程：

1. 骑乘者走到雪牛娘附近。
2. 检查双方状态。
3. 结束骑乘者当前 job。
4. 取消骑乘者征召和攻击指令。
5. 从地图 despawn 骑乘者。
6. 放入雪牛娘 `Comp_MooGirlMount.innerContainer`。
7. 播放提示/音效。

下骑流程：

1. 从容器取出骑乘者。
2. 在雪牛娘附近找可站立格。
3. drop/spawn 骑乘者。
4. 给骑乘者短暂 `Wait`，避免刚下骑立即接奇怪任务。
5. 恢复玩家选择或提示。

### 3. FloatMenu / Gizmo

右键菜单：

- 对雪牛娘：`骑上 TargetA`
- 对有骑手的雪牛娘：`让骑乘者下来`

雪牛娘 Gizmo：

- `选中骑乘者`
- `骑乘者下骑`
- `骑乘者停火/允许射击`

骑乘者被选中时只显示有限 Gizmo：

- `选中雪牛娘`
- `下骑`
- 查看信息类 gizmo

不显示：

- 征召。
- 攻击。
- 武器。
- 能力。
- 跳跃背包。
- 装备主动技能。

### 4. 骑乘者绘制

通过 Harmony postfix 绘制，而不是依赖 `ThingComp.PostDraw`。

原因：

- Pawn 的 `Comps_PostDraw()` 在 `PawnRenderer` 前面执行。
- 如果直接用 comp post draw，骑手可能画在雪牛娘身体下方。

建议 patch：

- `Pawn.DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)` postfix。
- 仅当 `phase == Draw` 且 Pawn 是雪牛娘且有骑手时绘制。

绘制内容：

- 骑乘者 pawn 本体。
- 骑乘者远程武器贴图。
- 可选：骑乘状态图标或小型阴影。

位置来源：

- `moo.DrawPos`
- `moo.Drawer.renderer.BaseHeadOffsetAt(moo.Rotation)`
- XML 或 comp props 中配置每方向偏移。

### 5. 骑乘者生理 tick

推荐模式：生理继续，AI 暂停。

保留：

- needs 低频 tick。
- health / hediff 低频 tick。
- gene / ability cooldown 可选 tick。
- 年龄、出血、疾病等必要状态推进。

暂停：

- jobs。
- pather。
- mindState 主动 AI。
- stances。
- 自动工作。
- 自动战斗。

低频检查规则：

- 每 250 tick 检查一次需求和危险状态。
- 每 60 tick 或更低频率处理自动远程炮位。

自动下骑触发：

- 饥饿低于阈值。
- 困倦达到阈值。
- 严重伤病需要医疗。
- 着火。
- 倒地。
- 死亡。
- 精神状态异常。
- 雪牛娘死亡、倒地、被销毁、离图。

## 自动远程炮位

可选功能：骑乘者作为雪牛娘身上的远程挂载武器来源。

规则：

- 只允许远程武器。
- 不允许近战。
- 只允许自动开火。
- 不允许手动指定目标。
- 提供停火按钮，启用后骑乘者不再自动射击。
- 骑乘者不会被敌人攻击。
- 骑乘者没有独立命中判定。
- 实际射击者逻辑上仍是雪牛娘。
- 骑乘者提供武器、射击技能、命中修正。
- 在骑乘者渲染位置渲染武器和开火效果。
- 不保证复杂武器 mod 完全兼容。

### 炮位目标选择

由 `Comp_MooGirlMount` 定期扫描目标。

推荐频率：

- 每 30 到 60 tick 扫描一次。
- 正在开火或有目标时可略高频。

目标条件：

- 敌对目标。
- 在骑手武器有效射程内。
- 从雪牛娘当前格有可用射线。
- 避免明显友军误伤。
- 不攻击非威胁目标，除非设置允许。

实现方式：

- 可以借用 `AttackTargetFinder.BestShootTargetFromCurrentPosition`，但 searcher 需要使用雪牛娘作为 `Thing`。
- 当前有效 verb 使用代理远程 verb 或专用炮位 verb。
- 不直接调用容器里骑手原始 verb，因为原版 `Verb.TryStartCastOn` 要求 caster spawned。

### 炮位开火

推荐实现为代理开火：

- caster = 雪牛娘。
- weapon = 骑乘者主武器。
- shooter stats = 骑乘者射击技能和自定义修正。
- projectile source cell = 雪牛娘当前格。
- projectile visual origin = 骑乘者武器绘制位置。

移动规则二选一：

1. 简单版：雪牛娘移动时不能开火。
2. 进阶版：移动中可开火，但雪牛娘移动速度降低，命中率降低。

第一版推荐简单版。

### 炮位限制

停止自动开火条件：

- 骑乘者停火开关已启用。
- 骑乘者无远程武器。
- 骑乘者倒地、睡着、昏迷、死亡。
- 骑乘者手部操作能力不足。
- 骑乘者处于严重精神状态。
- 雪牛娘倒地、死亡、着火、被击晕。
- 武器需要特殊弹药或特殊 comp 且未适配。

## 能力、物品和外部效果规则

骑乘者作为 caster：

- 禁止所有能力。
- 禁止跳跃背包。
- 禁止灵能。
- 禁止基因能力。
- 禁止装备主动技能。
- 禁止手动攻击命令。

其它 Pawn 对骑乘者使用效果：

- 不实现。
- 骑乘者在骑乘中不作为技能、物品、医疗、喂食、检查、传送、折跃等效果的合法目标。
- 如果需要对骑乘者进行任何外部互动，必须先让骑乘者下骑，回到地图上的正常 Pawn 状态。
- 不提供 `对骑乘者使用...` 类型菜单或 Gizmo。

## 与现有牵引系统关系

当前 mod 已有雪牛娘牵引系统。骑乘系统应避免和牵引叠加。

建议规则：

- 被牵引中的雪牛娘不能被骑乘。
- 正在骑乘的雪牛娘不能被牵引。
- 被牵引中的 Pawn 不能上骑。
- 骑乘者不能牵引其它目标。
- 若强制进入牵引状态，优先让骑乘者下骑。

## 存档与异常恢复

必须处理：

- 保存骑乘者。
- 读取后修复容器 owner。
- 读取后修复骑乘者与雪牛娘的关系。
- 读取后如果雪牛娘状态非法，自动下骑。
- 如果骑乘者丢失、destroyed 或 dead，清理容器。
- 如果卸载骑乘系统，避免存档硬崩。可保留降级逻辑。

## Def 和文件建议

建议新增目录：

- `1.6/Source/WRace/Mounting骑乘系统的编程/`

建议新增 C#：

- `Comp_MooGirlMount.cs`
- `CompProperties_MooGirlMount.cs`
- `JobDriver_MountMooGirl.cs`
- `JobDriver_DismountMooGirl.cs`
- `FloatMenuProvider_MountMooGirl.cs`
- `Harmony_MountRendering.cs`
- `Harmony_MountedPawnGizmos.cs`
- `MountedPawnCombatTurret.cs`
- `MountedPawnUtility.cs`

建议新增 XML：

- `1.6/Defs/JobDefs/JobDefs_Mounting.xml`
- 给 `MooGirl` ThingDef 添加 `CompProperties_MooGirlMount`。
- keyed 翻译加入骑乘相关文本。

## 实施阶段

### 阶段 1：基础骑乘

目标：

- 能上骑。
- 能下骑。
- 能保存读取。
- 骑乘者不参与地图 AI。
- 骑乘者绘制在雪牛娘头上。

验收：

- 骑乘后地图上只剩雪牛娘一个 spawned Pawn。
- 骑乘者可通过 Gizmo 选中。
- 读档后关系仍存在。
- 雪牛娘死亡或倒地时骑乘者安全落地。

### 阶段 2：需求和自动下骑

目标：

- 骑乘者生理状态继续推进。
- 饥饿、困倦、伤病等状态能自动触发下骑。

验收：

- 饥饿到阈值后自动下骑找食物。
- 严重受伤后自动下骑进入医疗流程。
- 不出现骑乘者在容器内跑原版 job 的情况。

### 阶段 3：交互入口

目标：

- 选中雪牛娘可管理骑乘者。
- 选中骑乘者可返回雪牛娘或下骑。
- 基础检查面板可读。

验收：

- 骑乘者不会显示攻击、能力、征召按钮。
- 可以稳定从 UI 找到骑乘者和雪牛娘。

### 阶段 4：自动远程炮位

目标：

- 骑乘者持远程武器时自动攻击敌人。
- 无手动攻击。
- 无近战。
- 武器绘制在骑乘者位置。

验收：

- 静止雪牛娘上的骑乘者能自动开火。
- 移动中的雪牛娘按规则停止开火或降低速度。
- 骑乘者不会被敌人直接攻击。
- 普通远程武器可用。
- 复杂武器 mod 不保证兼容，但不会造成硬错误。

## 风险点

高风险：

- 原版 `Verb` 对 spawned caster 的硬要求。
- 武器 comp 假设持有者是地图 Pawn。
- 存档中容器关系损坏。
- 绘制层级与 HAR/Facial Animation/服装绘制冲突。

中风险：

- 需求 tick 与暂停 tick 的边界。
- 自动下骑找不到可站立格。
- 雪牛娘进入其它容器或地图转移。
- 牵引系统与骑乘状态冲突。

低风险：

- 右键菜单。
- 基础 Gizmo。
- 简单骑手绘制。

## 推荐默认设置

- 默认允许骑乘。
- 默认启用需求推进。
- 默认启用自动下骑。
- 默认禁用骑乘者能力。
- 默认禁用骑乘者手动攻击。
- 默认禁用骑乘者近战。
- 默认不启用停火，即骑乘者允许自动远程射击。
- 默认远程炮位只在雪牛娘静止时开火。
- 默认复杂武器不适配时静默跳过并在开发模式记录日志。

## 总结

推荐实现路线是先完成稳定的“乘客骑乘系统”，再逐步加入“自动远程炮位”。不要试图让容器里的骑乘者恢复完整原版战斗 AI。这样可以保留骑乘玩法的核心表现，同时避免寻路、碰撞、目标选择、敌人攻击判定和复杂装备 mod 造成的大量兼容问题。
