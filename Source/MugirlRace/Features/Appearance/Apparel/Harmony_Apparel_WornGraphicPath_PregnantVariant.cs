using HarmonyLib;

namespace Mugirl.Features.Appearance;

// 只改读取路径；Apparel.WornGraphicPath 在原版中仅被 ApparelGraphicRecordGetter.TryGetGraphicApparel 消费，
// 追加后缀会自然组合成 ..._Pregnant_<BodyType>_<Rot> 的贴图文件名。
[HarmonyPatch(typeof(Apparel), nameof(Apparel.WornGraphicPath), MethodType.Getter)]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Low, "带 MugirlPregnantSwap 标签的服装怀孕时不切换 _Pregnant 变体贴图，保持原贴图。")]
public static class Harmony_Apparel_WornGraphicPath_PregnantVariant
{
    public static void Postfix(Apparel __instance, ref string? __result)
    {
        if (__result.NullOrEmpty()
            || !PregnantApparelUtility.IsSwapApparel(__instance)
            || !PregnantApparelUtility.IsPregnant(__instance.Wearer))
        {
            return;
        }

        __result += PregnantApparelUtility.VariantSuffix;
    }
}
