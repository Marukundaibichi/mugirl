using HarmonyLib;

namespace Mugirl.Features.Appearance;

// 固色服装不进入造型台的服装染色列表。原版 Dialog_StylingStation 只按 CompColorable 过滤，
// 列表在构造函数与 Reset 两个入口填充，两处填充后统一剔除固色成员；
// 染色 Tab 不再显示这些服装，ApplyApparelColors 也不会再给它们写 DesiredColor。
// 私有字段 apparelColors 由 Harmony 注入：字段改名或改类型时补丁类在启动时失败并记录，而不是静默跳过。
[HarmonyPatch(typeof(Dialog_StylingStation), MethodType.Constructor, typeof(Pawn), typeof(Thing))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Medium, "固色服装重新出现在造型台染色列表，可选颜色不会产生视觉效果（DrawColor 补丁仍锁定颜色）。")]
public static class Harmony_Dialog_StylingStation_Ctor_FixedColor
{
    public static void Postfix(Dictionary<Apparel, Color>? ___apparelColors)
    {
        FixedColorApparelUtility.RemoveFixedColorEntries(___apparelColors);
    }
}
