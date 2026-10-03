# 投掷伤害削减与冷却调整记录

## 版本与范围

- 日期：2026-09-26，基于投掷期间禁火（2026-09-25）之后的工作区。
- 需求：用户要求"削减投掷的伤害，并将投掷的 cd 设置为 2 小时"。
- 范围：雪牛投掷的伤害公式与 `Mugirl_Throw` 能力冷却；举起重量规则、飞行、禁火等其余逻辑不变。

## 改动

| 文件 | 改动 |
| --- | --- |
| `1.6/Source/Features/Genes/MugirlThrowUtility.cs` | `ImpactDamage` 各系数与上下限减半：`8 + √质量×7 + 距离×1.5`（12..220）→ `4 + √质量×3.5 + 距离×0.75`（6..110） |
| `Bio_1.6/Defs/AbilityDef.xml` | `Mugirl_Throw` 的 `cooldownTicksRange` 600 → 5000（5000 tick = 2 游戏小时；RimWorld 1 游戏小时 = 2500 tick） |

## 数值影响

- 花岗岩岩块（25 kg）投 20 格：73 → 37 伤害。
- 3000 kg 墙体（原版基础耐久对应重量）投 20 格：上限封顶 220 → 110 伤害。
- 穿甲（伤害 ×0.012）与爆炸半径（`1.7 + 伤害/45`，上限 2..6）按同一伤害值推导，自动随之降低：半径上限 6 → 约 4.1；岩块示例约 3.3 → 2.5。
- 瞄准读数（`OnGUI`）与瞄准高亮动态读取 `ImpactDamage`，无需改动即显示新值。
- 开发者模式的流星大灌篮复用 `ImpactDamage`（×1.7 系数），伤害同步约减半；该能力仅开发者可见，接受此连带影响。

## 验证结果与限制

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（PowerShell 7，完整构建）：通过，退出码 0；日志 `TMP/throw-damage-cooldown-validation.log`，正式 DLL 已更新。
- 游戏内验证待做：举起后瞄准读数与实际落地伤害按新公式显示/结算；冷却显示 2 小时并在到点后重置（`sendMessageOnCooldownComplete` 到点提示仍生效）。
