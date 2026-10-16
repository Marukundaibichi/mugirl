using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 1.7 中 TryDrop(Apparel) 与 TryDrop(Apparel, out Apparel) 只是转调这个四参数重载（已核对 rw17-game），
// 因此只补丁这一个；1.6 另外两个重载上的补丁已删除。特性按声明类型精确查找，目标消失时 Harmony 报错而不会落到基类。
[HarmonyPatch(
    typeof(Pawn_ApparelTracker),
    nameof(Pawn_ApparelTracker.TryDrop),
    new[] { typeof(Apparel), typeof(Apparel), typeof(IntVec3), typeof(bool) },
    new[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Locked slave apparel can be removed through Pawn_ApparelTracker.TryDrop and its overloads.")]
public static class Harmony_Pawn_ApparelTracker_TryDrop
{
    public static bool Prefix(Apparel ap, ref Apparel? resultingAp, ref bool __result)
    {
        if (!RestraintDropGuard.ShouldBlockDirectDrop(ap))
        {
            return true;
        }

        resultingAp = null;
        __result = false;
        RestraintDropGuard.RejectDropMessage();
        return false;
    }
}
