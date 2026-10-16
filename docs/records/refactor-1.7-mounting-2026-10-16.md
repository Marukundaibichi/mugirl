# Wave S · Mounting 状态封装与旧档升级

日期：2026-10-16。按 [decisions §7](../refactor-1.7/decisions.md#7-owner-补充约束2026-10-15恢复重构后优先于所有既有计划) 保持行为与玩家文本。[Combat 旧计划](../refactor-1.7/module-plans/combat-plan.md) 中删除计数器、改 hash interval、缩窄容器类型或绕开公开身份入口的部分不执行。

本子任务只修改 Mounting 自有文件和对应升级／测试／记录。`MountedMeleeSupport.cs`、`MountingHooks.cs`、Combat 注册主入口、WeaponWheel、Lance 均由其他 owner 维护，没有越界写入。

## 状态结构

`CompMount` 继续持有唯一 `ThingOwner<Thing>`，构造参数仍为 `oneStackOnly: true`、`LookMode.Deep`、`removeContentsIfDestroyed: false`。没有改成 `ThingOwner<Pawn>`，没有增加代理 holder。`IThingHolder.ParentHolder` 原实现 `parent.MapHeld ?? parent.ParentHolder` 完整保留。

组件增加两块状态：

- `MountTickSchedule`：physiology、safety、turret、melee 四个累计值，以及原样的 Add／ConsumeReady 合同。
- `MountTurretState`：burstCooldownTicksLeft、aimTarget、aimTicksLeft、aimTicksTotal、castStartTick、lastAttackedTarget、lastAttackTargetTick 七个字段；封装清除瞄准与纯计时运算。

共享 Pawn 元素上的新标签及对应字段为 `mugirlMountRiders`、`mugirlMountFireAtWill`、`mugirlMountSchedule`、`mugirlMountTurret`。嵌套状态的标签等于其字段名。值对象通过 EnterNode/ExitNode 在原 PostExposeData 中同步暴露，不额外登记回调，从而维持容器 → 开火设置 → 四个计数 → 炮塔字段的原加载顺序。

`MooPawn`、`MountedPawn`、`HasMountedPawn`、`WeaponDrawPosAt` 入口保留，继续服务近战、外观及其他功能调用。`MountedPawn` 仍扫描容器中的第一个 Pawn；其他 Thing 仍可持有，不强制清除。开火设置的内部读取改为 `FireAtWill` 属性，实际 bool 状态仍在组件内。

## 计时与控制顺序

- CompTick 仍只按每 tick 推进远程 Verb，不把一次 interval 的 delta 拆成同一 tick 的多次连射。
- CompTickInterval 先检查骑手／Props，再依次累加四个计数，然后 Aim → Physiology → Safety → Turret → Melee。
- 禁用生理或自动下骑时，对应累计值仍增长，只跳过原来被条件保护的 Consume。重新启用后仍传入全部累计值。
- ConsumeReady 在达到阈值时输出整个 counter，再置零；没有改成 modulo、固定间隔或 hash。
- 不钳制已保存的负值，不改变 `+= delta` 的 unchecked 溢出。瞄准仍使用 `max(1, delta)`，冷却仍使用原始 delta，两者不混同。
- aimTicksTotal 保留独立保存，不从剩余值、当前 Verb 或属性重新计算。
- 下骑、装备移除、目标校验、施放／冷却和载具 stance 处理保持原语句次序。`TransientStateReset` 的 Order 500 以及原 caster 恢复、清索引、不 Reset 连射的行为未改。

纯状态推进的十万步测试没有产生新的托管分配。这里只证明状态操作不引入逐 tick 分配，不将其表述为整个游戏的帧率测量。

## 旧档升级

[CombatMountUpgradeRegistration](../../Source/MugirlRace/SaveUpgrade/Modules/Combat/CombatMountUpgradeRegistration.cs) 提供 `Register(UpgradeModuleBuilder)`；Combat owner 已在主结构注册处调用。没有重复登记 Mount／Dismount／LoadWheel 的 Preserve 策略。

`Combat.MountState` 使用 `OwnerSelector.ThingComp(typeof(CompMount))`，结合中央完整历史 comp 关系和当前第三方 Def 目录确定所属 Pawn。原 13 个标签全部被消费并无损重挂：innerContainer／fireAtWill 改为上述带 mugirl 前缀的标签，四计数及七炮塔字段分别移入状态子节点。

- 不解析或重写计数、目标文本、骑手／其他载荷、maxStacks、removeContentsIfDestroyed 或 innerList 内容。
- 使用中央 AppendElement／Move／Rename；原载荷节点身份保留，不会因搬迁被记成删除的 load ID。
- 其他组件恰好使用 innerContainer、fireAtWill 等同名标签时不处理。
- 完全没有旧 Mount 标签时不编辑 DOM。Def 后加 comp 的情形由相同的新实例默认值接管，也避免给其他模块的 Pawn 夹具追加无关空节点。
- 新旧形状同时存在时中止，避免选择性覆盖。

开火设置缺失时，仍由实际 `Props?.turretFireAtWillDefault ?? true` 解释，不在 DOM 猜成 true。新保存 forceSave 写出实际 bool 值。真实 Scribe 测试覆盖 Props 默认 true、默认 false、缺省值与已保存值不一致的情况。

## 冻结公开 API

`PublicApi/MountedPawnUtility.cs` 的 SHA-256 与开始时相同。`IsMugirl(Pawn)` 仍为 public static、参数名 pawn、带 NoInlining。`MountEligibilityUtility.CanMount` 和右键目标判定仍实际调用它；没有替换成“有 Comp 就能骑”的判定，也没有绕过 WhiteFluff 等依赖的 Harmony 扩展点。

CompProperties 的 XML 字段与其 PublicApi 别名不改。没有修改 Def、翻译、载具外观偏移或玩家消息。

## 验证与边界

- 基线状态：`%TEMP%/mg17-mount-preS-status.txt`；开始时源码副本及受保护文件摘要：`%TEMP%/mg17-mount-source-preS`。
- 不可变 TEMP Source 副本独立 Release 构建：0 警告、0 错误。首次目标集 **33 项全部通过，无跳过**，日志 `%TEMP%/mg17-mount-target.log`；后续缺组边界复核见下文。
- 一组 `Combat.MountState` golden 覆盖全部 13 旧标签，经完整生产流水线核对并重复运行验证幂等。负对照覆盖非 Mount 组件、第三方 Mount Def、完全缺失字段和混合结构。
- 真实 Scribe、CrossRefHandler、PostLoadIniter 往返验证：ThingOwner<Thing> 类型、非 Pawn 测试载荷、原 Comp holder、载荷 ParentHolder、炮塔目标指向同一个重建对象、全部计数／目标／时间值，以及第二次保存一致。实际 Pawn 放入容器后还验证了 MountedPawn facade 和 CompTickInterval 累计行为。完整 Pawn 在正式游戏中的资产往返仍由集成实机套件验证。
- 10,000 组累计差分包含禁用任务余量、负 delta、负 counter 与溢出；瞄准／冷却边界和异常中的 caster／warmup 恢复均通过。只推进状态的 100,000 次循环测得 0 新托管分配。
- 反向审查 `%TEMP%/mg17-mount-reverse-audit.json`：显式还原状态访问后 93 个原方法 token 相同；7 个差异分别为保存结构、Add／Consume 提取、Tick／TickAim／ClearAim 委托。逐项复核仍保持原条件、语句顺序和算式。中文文本与 Mugirl 玩家键字面量多重集无增删。
- 标准 CLR 的 45 项验证完成，C.public-api 的 151 项契约通过；Mount 的 C.save-upgrade、默认值及结构没有新增失败。全仓仍包含其他模块施工与中央基线未更新的失败，不能据此宣称整个 Wave S 已验收。
- Waylith 首次验证在读取游戏原版 World_Oneshots_Explosions.xml 时出现一次 FileNotFound，后续只执行了 24 项。只读复核文件仍存在、mtime 未变；不推断是 Steam 更新。复用已准备的 runner 重试后完整执行 45 项。与之前标准 CLR 的剩余差异是三个随后变更的 Restraints golden，不能把非冻结输入的两次运行冒充同步验收。日志分别为 `mg17-mount-v1.log`、`mg17-mount-waylith-retry.log`。

新增类型仅为普通状态对象和升级步骤，不新增 GameComponent、ThinkNode 或 Harmony patch。中央需要刷新 ownership／根 README；本子任务没有新增需要豁免的基线。没有部署、启动游戏、操作玩家存档或修改 1.6。

## 缺组读档边界复核（2026-10-16）

独立审查发现首次测试只验证状态值，没有捕获原版 Log.Error：LoadingVars 缺少状态组时 EnterNode 返回 false，但在 CrossRefs 阶段 curXmlParent 已清空，EnterNode 可以返回 true。直接再次 ExposeData 会领取从未登记的目标引用。这影响完全缺组、只存在一个组等合法情况，原 33 项结果不足以排除它。

Comp 现在分别记录两个组在本次 LoadingVars 中是否进入；只有实际读过的组参与 ResolvingCrossRefs／PostLoadInit。Saving 始终写规范状态，缺组实例仍保持原默认值；没有改变标签、计数、holder 或调用顺序。真实负对照复现旧 helper 的 “never added during LoadingVars” 错误，修复后的缺组、空组和部分组全程断言零原版错误。

共享测试 ScribeErrorCapture 只在串行测试作用域内拦截并记录 Log.Error，异常后撤销本作用域补丁。真实类型解析也暴露离线测试缺 HAR 依赖；中央在测试项目复制已安装 HAR，未掩盖依赖错误或修改生产引用。三个模块的联合目标集 **112/112** 通过（`%TEMP%/mg17-group-fix-target.log`），独立构建 0 警告、0 错误。Combat owner 的另一个快照联合 Combat／Wheel／Mount **129/129** 通过。

分配测试曾在混合 Harmony 测试进程测得一次 96 字节冷启动开销。现先预热同一个完整推进循环及四种 Consume，再测量 100,000 次；断言仍严格为 0 字节，没有提高允许阈值。
