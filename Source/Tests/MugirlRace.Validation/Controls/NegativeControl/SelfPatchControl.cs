using HarmonyLib;

namespace Mugirl.Control;

public static class SelfPatchTarget
{
    public static int Apply(int value) => value + 1;
}

[HarmonyPatch(typeof(SelfPatchTarget), nameof(SelfPatchTarget.Apply))]
[MugirlPatch(PatchModule.Core, PatchRisk.Low, "control: an explicit extension point is required")]
public static class SelfPatchControl
{
    public static void Postfix(ref int __result) => __result++;
}
