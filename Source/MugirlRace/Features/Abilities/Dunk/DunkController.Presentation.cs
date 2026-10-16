using Mugirl.Platform;

namespace Mugirl.Features.Abilities;

// 绘制：各阶段位置由 DunkPresentationMath 计算，显示 tick 逐帧补间。
// DynamicDrawManager 在工作线程上执行 ParallelPreDraw，并在每个阶段读取 DrawPos；施术者 Pawn（以及 PawnFlyer）的 DrawPos
// 会改写自身的补间状态，只能在主线程读取。因此位置在主线程阶段（EnsureInitialized、Draw）计算并缓存，工作线程只读缓存。
public partial class DunkController
{
    // 仅供绘制补间；不存档，也不参与撞击或破碎判定。
    private RenderTickInterpolator presentation;
    // 施术者（或其飞行器）的绘制位置，只在主线程采样。
    private Vector3 carrierVisualDrawPos;
    // 主线程本帧计算的绘制位置与自转角，供 ParallelPreDraw 读取。
    private Vector3 presentationPos;
    private float presentationSpin;

    public override UVector3 DrawPos => (UnityData.IsInMainThread ? ComputeDrawPos() : presentationPos).ToUnity();

    private bool HasAnimatedPresentation => motion.phase == DunkPhase.Spin || motion.phase == DunkPhase.Flight
        || (motion.phase == DunkPhase.Slam && motion.impactAnimating);

    private float RenderSpinDegrees
    {
        get
        {
            if (motion.phase == DunkPhase.Dribbling)
            {
                return DunkPresentationMath.DribbleSpinDegrees(TickUtility.CurrentGameTickOrFallback(0));
            }

            if (motion.phase == DunkPhase.Slam && motion.impactAnimating)
            {
                return motion.impactStartSpinDegrees + presentation.Ticks * DunkPresentationMath.HeadSpinDegreesPerTick;
            }

            float spinTicks = motion.phase == DunkPhase.Spin ? presentation.Ticks
                : motion.phase == DunkPhase.Flight ? DunkPresentationMath.SpinTicks + presentation.Ticks
                : DunkPresentationMath.SpinTicks + DunkPresentationMath.FlightTicksBeforeImpact;
            return motion.launchSpinDegrees + spinTicks * DunkPresentationMath.HeadSpinDegreesPerTick;
        }
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        if (HasAnimatedPresentation)
        {
            presentation.Reset(motion.phaseTicks);
        }
    }

    private Vector3 ComputeDrawPos()
    {
        return motion.phase switch
        {
            DunkPhase.Spin => SpinDrawPosAt(presentation.Ticks),
            DunkPhase.Flight => FlightDrawPosAt(presentation.Ticks),
            DunkPhase.Shattering => SlamDrawPos(),
            DunkPhase.Slam => motion.impactAnimating ? SlamDrawPosAt(presentation.Ticks) : SlamDrawPos(),
            _ => DribbleDrawPos(),
        };
    }

    private Vector3 DribbleDrawPos()
    {
        Vector3 center = (carrier is not null && carrier.Spawned ? carrier.DrawPos : originalPosition.ToVector3Shifted()).ToNumerics();
        Vector3 facing = carrier is not null ? carrier.Rotation.FacingCell.ToVector3().ToNumerics() : UnityVector.Forward;
        return DunkPresentationMath.DribblePosition(center, facing, TickUtility.CurrentGameTickOrFallback(0), AltitudeLayer.Pawn.AltitudeFor());
    }

    private Vector3 SpinDrawPosAt(float renderTick)
    {
        Vector3 center = DunkPresentationMath.SpinPosition(motion.launchHeadPosition.ToNumerics(), renderTick, AltitudeLayer.MoteOverhead.AltitudeFor() + 0.06f);
        if (motion.followsCarrier)
        {
            center += carrierVisualDrawPos - motion.launchCarrierPosition.ToNumerics();
        }

        return center;
    }

    private Vector3 FlightDrawPosAt(float renderTick)
    {
        if (motion.followsCarrier)
        {
            // 篮球仍在施术者手边。飞行者按自身轨迹移动，头只在手边作小幅逐帧摆动。
            return DunkPresentationMath.HeldWobble(SpinDrawPosAt(DunkPresentationMath.SpinTicks), renderTick);
        }

        Vector3 end = impactCell.ToVector3Shifted().ToNumerics();
        end.Y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
        float distance = motion.launchHeadPosition.ToIntVec3().DistanceTo(impactCell);
        return DunkPresentationMath.FlightPosition(motion.launchHeadPosition.ToNumerics(), end, distance, renderTick);
    }

    private Vector3 SlamDrawPos()
    {
        Vector3 pos = impactCell.ToVector3Shifted().ToNumerics();
        pos.Y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
        return pos;
    }

    private Vector3 SlamDrawPosAt(float renderTick)
    {
        Vector3 movingStart = motion.impactStartHeadPosition.ToNumerics();
        if (motion.impactTracksCarrier)
        {
            // 开始下扣时仍继承飞行者的速度，随后逐渐脱手并追向篮筐。
            movingStart += carrierVisualDrawPos - motion.impactStartCarrierPosition.ToNumerics();
        }

        return DunkPresentationMath.SlamPosition(movingStart, SlamDrawPos(), renderTick);
    }

    // 只在主线程调用（tick、开始扣篮、主线程绘制阶段）。
    private void CacheCarrierVisualDrawPos()
    {
        if (!motion.followsCarrier || (motion.phase != DunkPhase.Spin && motion.phase != DunkPhase.Flight
            && (motion.phase != DunkPhase.Slam || !motion.impactTracksCarrier)))
        {
            return;
        }

        if (carrier?.ParentHolder is PawnFlyer flyer && !flyer.Destroyed && flyer.Spawned)
        {
            carrierVisualDrawPos = flyer.DrawPos.ToNumerics();
        }
        else if (carrier is not null && carrier.Spawned && carrier.Map == Map)
        {
            carrierVisualDrawPos = carrier.DrawPos.ToNumerics();
        }
        else
        {
            carrierVisualDrawPos = motion.launchCarrierPosition.ToNumerics();
        }
    }

    private void SamplePresentation()
    {
        if (!presentation.BeginFrame(EngineClock.FrameCount, motion.phaseTicks))
        {
            return;
        }

        CacheCarrierVisualDrawPos();
        if (TickUtility.TryGetPresentationTickRate(out float tickRateMultiplier))
        {
            float ceiling = DunkPresentationMath.PresentationCeiling(motion.phaseTicks, motion.phase == DunkPhase.Slam && motion.impactTracksCarrier);
            presentation.Advance(EngineClock.DeltaTime, tickRateMultiplier, ceiling);
        }
    }

    public override void DynamicDrawPhaseAt(DrawPhase phase, UVector3 drawLoc, bool flip = false)
    {
        Thing? thing = Payload;
        if (thing is null || motion.phase == DunkPhase.Finished)
        {
            return;
        }

        if (phase != DrawPhase.ParallelPreDraw)
        {
            if (HasAnimatedPresentation)
            {
                SamplePresentation();
            }

            presentationPos = ComputeDrawPos();
            presentationSpin = UnityMathf.Repeat(RenderSpinDegrees, 360f);
        }

        Vector3 pos = presentationPos;
        float spin = presentationSpin;
        if (thing is DunkHead head)
        {
            if (phase == DrawPhase.Draw)
            {
                head.DrawHeadAt(pos, spin);
            }

            return;
        }

        if (thing is Pawn pawn)
        {
            PawnSpinRenderer.Draw(pawn, phase, pos, spin);
            return;
        }

        if (phase == DrawPhase.Draw)
        {
            thing.Graphic?.GetShadowlessGraphic()?.Draw(pos.ToUnity(), thing.Rotation, thing, spin);
        }
    }
}
