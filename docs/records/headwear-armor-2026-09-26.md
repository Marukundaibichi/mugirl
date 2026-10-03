# 头饰护甲补齐（2026-09-26）

## 范围与原因

排查发现 7 件头饰通过 `Mugirl_HeadBase` → 原版 `HatMakeableBase → ApparelBase` 继承了品质组件，但自身没有任何受品质系数影响的属性（原版中品质只作用于护甲三防、冷暖隔热、护盾等 StatPart_Quality 属性；MaxHitPoints 与 equippedStatOffsets 不受品质影响），高品质实际只提高售价。本次为这 7 件补少量护甲，使品质真正影响防护：

- `Apparel_0920.xml`：`Mugirl_CorporateBaseballCap`、`Mugirl_CorporateSunHat` 各加 `StuffEffectMultiplierArmor 0.10`；`Mugirl_PoliceCap` 加 `0.12`（对齐警察套基线 `Mugirl_0920CasualBase` 的 0.12 与警靴 0.25 档位）；`Mugirl_NunVeil` 加 `0.08`。
- `Apparel_Head.xml`：`Mugirl_Veil`、`Mugirl_Mask` 各加 `StuffEffectMultiplierArmor 0.05`；`Mugirl_Sunglasses` 不可缝制，改为固定三防 `ArmorRating_Sharp 0.04 / Blunt 0.02 / Heat 0.02`。

数值参照：原版牛仔帽材料护甲系数 0.2，本模组便服基线 0.12、警靴 0.25、白色内裤 0.09；帽子取 0.08–0.12、轻纱件 0.05、墨镜固定小值。品质系数（0.6×–1.8×）作用于最终护甲，材料护甲 = 系数 × 材料护甲力（布料约 0.036 锐防，魔鬼织等更高）。

刻意未改：隔热数值维持为零，不改变现有保暖/避暑行为；MaxHitPoints、工时、售价机制不变。`Mugirl_RA_MechanicBackpack` Lv1–4 与 `Mugirl_AutoloadingSystem` 没有品质组件（`Mugirl_ApparelMakeableNoQualityBase` 的 `comps Inherit="False"` 与原版 `ApparelNoQualityBase`），不存在品质问题，不在本次范围。

## 验证

一次性扫描脚本 `TMP/scan_quality_useless.py`（解析模组 Def 继承链，检查品质组件存在且无任何品质生效属性）复扫：7 件目标全部清零，剩余 5 项均为无品质组件的已知排除项。`Invoke-Phase6StaticValidation.ps1 -SkipBuild`（pwsh 运行）退出码 0，服饰贴图路径与资源检查通过，日志在 `TMP/phase6-static-20260926.log`。纯 Def 数值改动，未构建 DLL，未做游戏内实测；品质对护甲的实际放大依赖原版 StatPart_Quality，无需代码验证。
