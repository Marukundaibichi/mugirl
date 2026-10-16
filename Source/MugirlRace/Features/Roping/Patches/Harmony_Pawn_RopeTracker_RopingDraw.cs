using HarmonyLib;

namespace Mugirl.Features.Roping;

// 拴在墙绳桩上的 Pawn：绳子画到绳桩朝向一侧的墙边，而不是格子中心。其他情况交给原版。
// RopingDraw 由 PawnRenderer.RenderPawnAt 为每个绘制中的 Pawn 调用（主线程），绝大多数 Pawn 没有拴到地点，先走快路径。
[HarmonyPatch(typeof(Pawn_RopeTracker), nameof(Pawn_RopeTracker.RopingDraw))]
[MugirlPatch(PatchModule.Roping, PatchRisk.High, "Wall-hitch rope drawing falls back to the base rope draw path.")]
public static class Harmony_Pawn_RopeTracker_RopingDraw
{
    public static bool Prefix(Pawn_RopeTracker __instance, Pawn ___pawn)
    {
        if (!__instance.IsRopedToSpot || ___pawn.Map is not { } map)
        {
            return true;
        }

        IntVec3 ropeCell = __instance.RopedTo.Cell;
        if (!ropeCell.IsValid || ropeCell.GetFirstThing(map, RopingDefOf.Mugirl_WallRopeHitch) is not Building hitch)
        {
            return true;
        }

        UVector3 anchor = WallRopeHitchGeometry.EdgeAnchor(hitch.Position.ToVector3Shifted().ToNumerics(), hitch.Rotation).ToUnity();
        GenDraw.DrawLineBetween(
            ___pawn.DrawPos.Yto0(),
            anchor.Yto0(),
            AltitudeLayer.PawnRope.AltitudeFor(),
            RopeGraphics.RopeLineMat,
            RopeGraphics.RopeLineWidth);
        return false;
    }
}
