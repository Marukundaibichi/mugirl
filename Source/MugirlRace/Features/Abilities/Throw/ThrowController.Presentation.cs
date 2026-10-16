using Mugirl.Platform;

namespace Mugirl.Features.Abilities;

// 绘制：举起时跟随持物者，飞行时按显示 tick 逐帧补间。
// DynamicDrawManager 在工作线程上执行 ParallelPreDraw，并在每个阶段读取 DrawPos；持物者 Pawn 的 DrawPos 会推进其 PawnTweener，
// 只能在主线程读取。因此位置在主线程阶段（EnsureInitialized、Draw）计算并缓存，工作线程只读缓存。
public partial class ThrowController
{
    // 保留原有空中连转数圈的转速；绘制使用显示 tick 逐帧补间。
    private const float SpinDegreesPerTick = 42f;

    // 仅供绘制补间；不存档，不参与屋顶、命中和落点判定。
    private RenderTickInterpolator flightPresentation;
    // 主线程本帧计算的绘制位置与自转角，供 ParallelPreDraw 读取。
    private Vector3 presentationPos;
    private float presentationSpin;

    public override UVector3 DrawPos => (UnityData.IsInMainThread ? ComputeDrawPos() : presentationPos).ToUnity();

    private float RenderTick => state == ThrowState.Flying ? flightPresentation.Ticks : 0f;

    private Vector3 ComputeDrawPos()
    {
        return state == ThrowState.Flying ? FlyingDrawPosAt(flightPresentation.Ticks) : HeldDrawPos();
    }

    private Vector3 HeldDrawPos()
    {
        Vector3 carrierPos = (carrier is not null && carrier.Spawned ? carrier.DrawPos : originalPosition.ToVector3Shifted()).ToNumerics();
        return ThrowFlightMath.HeldPosition(carrierPos, TickUtility.CurrentGameTickOrFallback(0), AltitudeLayer.MoteOverhead.AltitudeFor());
    }

    private Vector3 FlyingDrawPosAt(float renderTick)
    {
        float distance = flightStartGround.ToIntVec3().DistanceTo(destination);
        return ThrowFlightMath.FlyingPosition(flightStartDraw.ToNumerics(), destination.ToVector3Shifted().ToNumerics(), renderTick, flightTicks, distance,
            AltitudeLayer.MoteOverhead.AltitudeFor());
    }

    private void SampleFlightPresentation()
    {
        // 仅追随已推进的游戏 tick，不预测屋顶穿透和命中后的未来位置。
        if (flightPresentation.BeginFrame(EngineClock.FrameCount, ticksFlying)
            && TickUtility.TryGetPresentationTickRate(out float tickRateMultiplier))
        {
            flightPresentation.Advance(EngineClock.DeltaTime, tickRateMultiplier, ticksFlying);
        }
    }

    public override void DynamicDrawPhaseAt(DrawPhase phase, UVector3 drawLoc, bool flip = false)
    {
        Thing? thing = HeldThing;
        if (thing is null)
        {
            return;
        }

        if (phase != DrawPhase.ParallelPreDraw)
        {
            if (state == ThrowState.Flying)
            {
                SampleFlightPresentation();
            }

            presentationPos = ComputeDrawPos();
            presentationSpin = state == ThrowState.Flying ? ThrowFlightMath.SpinDegrees(RenderTick, SpinDegreesPerTick) : 0f;
        }

        Vector3 pos = presentationPos;
        float spin = presentationSpin;
        if (thing is Pawn pawn)
        {
            if (state == ThrowState.Flying)
            {
                PawnSpinRenderer.Draw(pawn, phase, pos, spin);
            }
            else
            {
                pawn.DynamicDrawPhaseAt(phase, pos.ToUnity(), flip);
            }

            return;
        }

        if (thing is Corpse corpse)
        {
            // 尸体 ThingDef 没有 graphicData，走 Graphic 分支会画成红叉（BadGraphic）；
            // 持有态交给 Corpse 自身绘制（同 Building_GibbetCage），飞行态复用 Pawn 旋转渲染器。
            if (corpse.InnerPawn is null)
            {
                return;
            }

            if (state == ThrowState.Flying)
            {
                PawnSpinRenderer.Draw(corpse.InnerPawn, phase, pos, spin);
            }
            else
            {
                corpse.DynamicDrawPhaseAt(phase, pos.ToUnity(), flip);
            }

            return;
        }

        if (phase == DrawPhase.Draw)
        {
            thing.Graphic?.GetShadowlessGraphic()?.Draw(pos.ToUnity(), thing.Rotation, thing, spin);
        }
    }
}
