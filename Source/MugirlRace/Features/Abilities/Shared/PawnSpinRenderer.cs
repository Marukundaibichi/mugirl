namespace Mugirl.Features.Abilities;

// 以俯视顺时针角度旋转绘制一个不在地图上的 Pawn（被投掷或被扣篮的载荷）。由载体的 DynamicDrawPhaseAt 按阶段调用。
internal static class PawnSpinRenderer
{
    internal static void Draw(Pawn? pawn, DrawPhase phase, Vector3 drawPos, float clockwiseAngle)
    {
        if (pawn is null)
        {
            return;
        }

        PawnRenderer renderer = pawn.Drawer.renderer;
        if (phase == DrawPhase.EnsureInitialized)
        {
            renderer.EnsureGraphicsInitialized();
            return;
        }

        PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
        parms.facing = Rot4.South;
        parms.posture = PawnPosture.Standing;
        parms.flags |= PawnRenderFlags.NeverAimWeapon;
        parms.rotDrawMode = renderer.CurRotDrawMode;
        parms.tint = renderer.flasher.CurColor.ToTransparent(InvisibilityUtility.GetAlpha(pawn));
        parms.carriedThing = pawn.carryTracker?.CarriedThing;
        parms.matrix = UnityMatrix.TRS(
            drawPos + pawn.ageTracker.CurLifeStage.bodyDrawOffset.ToNumerics(),
            UnityQuaternion.AngleAxisY(clockwiseAngle),
            Vector3.One);

        if (phase == DrawPhase.ParallelPreDraw)
        {
            renderer.renderTree.ParallelPreDraw(parms);
        }
        else if (phase == DrawPhase.Draw)
        {
            // 调试选项 singleThreadedDrawing 下 DynamicDrawManager 跳过初始化与并行预绘制；
            // 与原版 PawnRenderer.RenderPawnAt 一样在主线程补做，避免沿用旧帧的绘制请求。
            if (DebugViewSettings.singleThreadedDrawing)
            {
                renderer.EnsureGraphicsInitialized();
                renderer.renderTree.ParallelPreDraw(parms);
            }

            renderer.renderTree.Draw(parms);
        }
    }
}
