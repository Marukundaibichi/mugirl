using HarmonyLib;

namespace Mugirl.Features.Appearance;

[HarmonyPatch(typeof(Dialog_StylingStation), "Reset", typeof(bool))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Medium, "造型台“重置”后固色服装重新出现在染色列表，可选颜色不会产生视觉效果（DrawColor 补丁仍锁定颜色）。")]
public static class Harmony_Dialog_StylingStation_Reset_FixedColor
{
    public static void Postfix(Dictionary<Apparel, Color>? ___apparelColors)
    {
        FixedColorApparelUtility.RemoveFixedColorEntries(___apparelColors);
    }
}
