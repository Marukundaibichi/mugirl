# Stage 4 Restraints Module Notes

本文件记录束具模块本轮重构进展。规则以 `docs/06-localization-and-comments.md` 为准：C# 玩家文本使用 Keyed；源 Def XML 使用英文原文，并在相邻注释写中文含义；中文翻译文件中的注释写英文原文。

## Completed This Pass

- 新增 `Core/MooGirlText.cs`，统一处理“可能是 C# 默认 Keyed，也可能是 XML/DefInjected 实际文本”的显示字段。
- `Comp_MagneticShackles` 的按钮、Mote、Message 改为通过 `MooGirlText.Resolve` 输出。
- `Comp_MagneticShackles` 移除激活/解除路径中的 LINQ 查询，改为显式循环检查与移除 Hediff。
- `Comp_ShockCollar` 的手动按钮文本和冷却提示改为统一解析，不再把 XML 文本强制当作翻译键。
- `Comp_BrainwashHelmet` 的主动洗脑、文化转换按钮和消息迁移到 Keyed 或统一解析。
- `BongdageApparel_Spacer.xml` 中磁力镣铐、电击项圈、洗脑头盔和榨乳器的玩家文本改为英文原文，旁边保留中文注释。
- `Hediffs_Advance.xml` 中束具 hediff、洗脑 mental state 和洗脑演出 `TextWithParams.text` 改为英文原文，旁边保留中文注释，并补齐中英 DefInjected。
- 本轮已触碰的 `TraitDefs.xml`、`JobDefs.xml` 和 `MooGirl_Race.xml` 残留中文源文本改为英文原文与中文注释，并补齐或整理中英 DefInjected。
- `ChineseSimplified/DefInjected/ThingDef/BongdageApparel_Spacer.xml` 和 `English/DefInjected/ThingDef/BongdageApparel_Spacer.xml` 补齐自定义 comp 字段翻译。
- `Comp_MilkingDevice` 的 XML 字段解析改为 `MooGirlText.Resolve`，配合源 XML 不再使用翻译键占位。

## Value Lock

本轮未改以下数值：

- 磁力镣铐 `cycleInterval`、`bindDurationTicks`、`useCooldownTicks`、绑定 Hediff、绑定部位、音效 Def。
- 电击项圈 `ticks`、`rand`、`powerShockChance`、`useCooldownTicks`、普通/强力/非雪牛娘 Hediff 列表。
- 洗脑头盔 `useCooldownTicks`、`ticksToChange`、`blockHediff`、`convertList`、`defaultHediff`。
- 榨乳器 `maxCharges`、释放产物、污物范围、音效、强力释放相关配置。

## Behavior Preserved

- 磁力镣铐未破解时仍自动循环，破解后仍只显示手动控制。
- 磁力镣铐解除按钮在冷却中仍保持原解除描述，没有改成冷却描述。
- 电击项圈保留 `ParentIsCracked()` 的既有语义：返回 true 表示未破解，返回 false 表示已破解。
- 洗脑头盔文化转换仍只在 Ideology 激活时显示。
- R18 内容保持常驻，本轮未恢复任何隐藏、过滤或清理开关。

## Validation

已执行：

```text
MSBuild 1.6/Source/WRace/MooGirlRace.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU
```

结果：通过，无新增警告。

已解析 `1.6/Defs`、`1.6/Languages/ChineseSimplified`、`1.6/Languages/English` 下全部 XML：

```text
183 XML files OK
```

已检索确认：

- `BongdageApparel_Spacer.xml` 和 `Hediffs_Advance.xml` 中本轮处理的玩家字段不再使用翻译键占位或中文源文本。
- 本轮已触碰文件的中文翻译注释不再使用 `EN:` 前缀。
- 磁力镣铐组件不再包含 `System.Linq`、`.Where()`、`.Any()`、`AddRange()`。
- 旧的 `MooGirl.MagneticShackles.*`、`MooGirl.ShockCollar.*`、`MooGirl.BrainwashHelmet_ManualTrigger_Message` 运行引用已移除。

## Deferred / Requires Confirmation

- `labelWhenActive` / `labelWhenInactive` 当前仍只是配置字段，未接入实际标签覆盖。本轮不擅自新增显示行为。
- `SlaveApperal` 拼写统一会影响 C# 类型名与 XML class 引用，需要单独批次处理。
- 全局源 XML 仍有大量旧中文玩家文本，需在后续 Def/XML 专项阶段继续按同一规则迁移。
