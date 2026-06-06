# 数值与行为保护

重构允许彻底改变内部架构，但不允许静默改变数值和玩家可见行为。本文件定义数值冻结、行为变更审批和回归对照规则。

## 数值冻结范围

以下内容全部视为数值，默认不得改动：

- XML 中的所有数字字段。
- C# 中的 const、默认字段、概率、倍率、tick、cooldown、damage、severity、range、amount、points、关系变化。
- Def 权重、commonality、tradeability、costList、statBases、equippedStatOffsets、apparel layers、bodyPartGroups。
- Hediff 阶段、severityPerDay、comp 参数、duration。
- JobDriver work ticks、toil duration、progress per tick。
- Incident/Quest 点数、触发条件、奖励数量。
- 贴图路径、声音路径、graphicData 尺寸与偏移。

## 基线记录格式

每次迁移前必须记录：

```text
模块：
文件：
字段：
旧值：
新值：
是否改变：
改变原因：
是否已确认：
```

如果新旧相同，记录 `是否改变：否`。

## 行为冻结范围

以下玩家可见行为默认不变：

- 设置默认值和设置含义。
- MooGirl 判定范围。
- 产奶、挤奶、喝奶、喂奶、哺乳。
- 束具锁定/解锁/破解/脑控/电击/镣铐。
- 牵引、拴墙、逃跑阻止、开门限制。
- 骑乘、自动下马、骑手武器、绘制层级。
- 出生、异种、幼年体型、backstory。
- 加入事件、任务、派系关系、快递分支。
- 成人内容开关的过滤和清理。

## 行为变更提案格式

如果发现旧行为有设计缺陷，先写提案，不直接改：

```text
编号：
模块：
当前行为：
问题：
建议行为：
会改变哪些玩家可见结果：
会影响哪些 Def 或数值：
兼容影响：
可回退方案：
建议是否执行：
等待确认：
```

## 已发现的潜在行为变更点

### Milk-001：乳房倍率实际生效范围

当前情况：

- `CompMooHasBodyResource` 多处计算 `BreastSize` 和 `BreastSizeDays`。
- 产奶增长实际使用 `GetProductionMultiplier`，而不是所有路径都使用 `BreastSizeDays`。
- 采集数量多处直接使用 `fullness * 100f`，不一定使用 `BreastSize`。

风险：

- 如果按注释意图修正，可能改变产奶速度或采集数量。

处理：

- 重构时先保持实际行为。
- 若要让注释和数值设计一致，必须提交行为变更提案。

### Milk-002：自动添加哺乳期 hediff

当前情况：

- `CompMooMilkable.CompTick()` 在 Active 时自动添加 `MooGirl_Lactation`。

风险：

- 改为事件驱动会改变添加时机。

处理：

- 默认保留结果。
- 若为性能优化改触发方式，需确认是否允许延迟或只在生成/加载时添加。

### Restraints-001：拼写修正是否影响 XML class

当前情况：

- `SlaveApperal` 等拼写错误已经进入 C# 类型和 XML class 引用。

风险：

- 改 C# class 名必须同步 XML；不影响数值，但会影响未迁移 Def。

处理：

- 新架构使用正确拼写。
- defName 是否改另行确认。

### Roping-001：MooGirl 判定范围

当前情况：

- 多处用 `RaceProps.body == MooGirlBody` 判定。
- 另一些地方用 `pawn.def == MooGirl || body == MooGirlBody`。

风险：

- 统一为更宽判定可能让更多 pawn 受影响。
- 统一为更窄判定可能破坏变体兼容。

处理：

- 先记录所有判定点。
- 建议统一到 `MooGirlIdentity.IsMooGirlPawn`，但行为范围需确认。

### Mounting-001：骑手武器 caster 修改

当前情况：

- 骑乘远程战斗通过临时修改 `Verb.caster` 为载体来实现。

风险：

- 更换实现可能改变射线起点、友军误伤、射程、冷却、warmup 或目标选择。

处理：

- 初版保留玩家可见结果，只加强状态恢复。
- 任何战斗行为修正都需单独确认。

### Incidents-001：旧档迁移删除

当前情况：

- 事件组件里包含 legacy faction、legacy courier quest 恢复等逻辑。

风险：

- 删除后旧档不兼容，但用户已允许不保证旧档兼容。

处理：

- 新档行为保持。
- 旧档迁移代码删除，不需要行为确认。

## 审批规则

无需确认：

- 删除旧档迁移代码。
- 删除未编译或不该编译的备份代码。
- 改内部命名、拆类、移动目录。
- 修复日志噪音。
- 提升性能但输出完全一致。

需要确认：

- 任何数值变化。
- 任何 defName 变化。
- 任何玩家可见行为变化。
- 任何会减少或扩大 mod 影响范围的判定变化。
- 任何任务/事件文本含义变化。

