# 灌篮文案与红色警示句记录

## 版本与范围

- 日期：2026-09-26，基于同日投掷伤害/冷却调整之后的工作区。
- 需求：用户要求更改雪牛流星大灌篮文案为"我要把你的头拧下来当球踢！\n\n你看到了不该看到的东西"，且后一句显示为红色。
- 范围：仅 `Mugirl_Dunk` 的 description（能力 Gizmo 提示框与能力列表）；玩法逻辑不变。

## 实现方式

- RimWorld 1.5+ 的 `ColoredText` 走 Unity 富文本：`AbilityDef.GetTooltip` 直接拼接 `description`，提示框由 `ActiveTip.DrawInner` → `Widgets.Label` → `GUI.Label(..., Text.CurFontStyle)` 绘制，富文本开启（Gizmo 标题的黄色即同一路径的 `<color>` 着色）。
- 因此只需在描述文本内嵌 `<color=#ff0000>…</color>`（`#ff0000` 与原版 `ColoredText.WarningColor` 一致），XML 中以 `&lt;`/`&gt;` 转义；无 C# 改动、无补丁。

## 改动

| 文件 | 改动 |
| --- | --- |
| `Bio_1.6/Defs/AbilityDef.xml` | `Mugirl_Dunk.description` 改为新英文文案，末句嵌红色标记 |
| `Bio_1.6/Languages/ChineseSimplified/DefInjected/AbilityDef/AbilityDef.xml` | 中文新文案："我要把你的头拧下来当球踢！\n\n" + 红色"你看到了不该看到的东西" |
| `Bio_1.6/Languages/English/DefInjected/AbilityDef/AbilityDef.xml` | 英文新文案，末句同样红色标记 |

## 验证结果与限制

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（PowerShell 7）：通过，退出码 0；日志 `TMP/dunk-redline-validation.log`。
- 游戏内验证待做：开发者模式选中带灌篮能力的雪牛娘，Gizmo 提示框第一段默认色、第二段红色；无富文本的旁路（如日志输出）会显示原始 `<color>` 标签，属预期。
