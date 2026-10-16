using HarmonyLib;

namespace Mugirl.Features.Milk;

// 生成绘制参数时让挤奶双方面向彼此。目标是私有方法 PawnRenderer.GetDrawParms(Vector3, float, Rot4, RotDrawMode, PawnRenderFlags)，
// 只能按名字指定；每帧对每个 Pawn 调用（含并行预绘制线程），补丁体只读状态，没有挤奶状态时立即返回。
[HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
[MugirlPatch(PatchModule.Milk, PatchRisk.Medium, "Paired milking facing falls back to pawn rotation and may be overridden by job posture.")]
public static class Harmony_PawnRenderer_GetDrawParms_MilkingFacing
{
    private static void Prefix(Pawn ___pawn, PawnRenderFlags flags, ref Rot4 bodyFacing)
    {
        MilkingAnimation.AdjustDrawFacing(___pawn, flags, ref bodyFacing);
    }
}
