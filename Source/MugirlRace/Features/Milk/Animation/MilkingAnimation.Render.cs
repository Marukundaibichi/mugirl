namespace Mugirl.Features.Milk;

// 渲染端查询：AnimationWorker 的偏移/角度/缩放与 GetDrawParms 的朝向修正。
// 1.7 在 DrawPhase.ParallelPreDraw 中用多个工作线程并行调用这些方法（DynamicDrawManager.PreDrawVisibleThings →
// ManagedJobParallelFor）。这里只读状态：不删除条目、不切换原生动画（PawnRenderer.SetAnimation 会让渲染树 SetDirty）。
// 1.6 在这条路径上清理过期状态，并行预绘制时会与其他线程的读取竞争。过期状态由主线程的 SweepStaleStates 清理。
public static partial class MilkingAnimation
{
    public static bool HasActiveAnimation(Pawn? pawn) => TryGetLiveState(pawn, out _);

    internal static bool TryGetAnimationTransform(PawnRenderNodeTagDef? tagDef, PawnDrawParms parms, out UVector3 offset, out float angle, out UVector3 scale)
    {
        offset = new UVector3(0f, 0f, 0f);
        angle = 0f;
        scale = new UVector3(1f, 1f, 1f);
        if (tagDef == null || parms.flags.FlagSet(PawnRenderFlags.Portrait) || !TryGetLiveState(parms.pawn, out MilkingVisualState? state))
        {
            return false;
        }

        int now = TickUtility.CurrentGameTickOrFallback(state.lastTick);
        Vector3 resultOffset = Vector3.Zero;
        Vector3 resultScale = Vector3.One;
        float resultAngle = 0f;
        bool applied;
        if (tagDef == PawnRenderNodeTagDefOf.Head)
        {
            applied = TryGetHeadTransform(state, parms, now, ref resultOffset, ref resultAngle, ref resultScale);
        }
        else if (tagDef == MilkDefOf.Root)
        {
            ApplyRootTransform(state, parms, now, ref resultOffset, ref resultAngle, ref resultScale);
            applied = true;
        }
        else
        {
            applied = false;
        }

        if (!applied)
        {
            return false;
        }

        offset = resultOffset.ToUnity();
        angle = resultAngle;
        scale = resultScale.ToUnity();
        return true;
    }

    // 原生 AnimationWorker 只能调整位移/角度/缩放，不能选择朝向贴图。等待姿态、征召和其他转向逻辑可能覆盖 tick 中的 Face，
    // 因此在生成绘制参数时让挤奶双方面向彼此。每帧对每个 Pawn 调用，没有任何挤奶状态时第一句即返回。
    internal static void AdjustDrawFacing(Pawn? pawn, PawnRenderFlags flags, ref Rot4 bodyFacing)
    {
        if (states.Count == 0 || pawn == null
            || flags.FlagSet(PawnRenderFlags.Portrait) || flags.FlagSet(PawnRenderFlags.Cache) || flags.FlagSet(PawnRenderFlags.Statue)
            || !states.TryGetValue(pawn.thingIDNumber, out MilkingVisualState? state)
            || state.pawn != pawn
            || (state.role != MilkingVisualRole.AssistedTarget && state.role != MilkingVisualRole.Helper))
        {
            return;
        }

        Pawn? partner = state.partner;
        if (!Valid(pawn) || !Valid(partner) || partner.Map != pawn.Map
            || !TickUtility.TryGetCurrentGameTick(out int now) || now - state.lastTick > StaleAfterTicks)
        {
            return;
        }

        Vector3 direction = partner.DrawPos.ToNumerics() - pawn.DrawPos.ToNumerics();
        direction.Y = 0f;
        if (UnityVector.SqrMagnitude(direction) < MilkingPoseMath.DirectionEpsilonSquared)
        {
            direction = (partner.Position - pawn.Position).ToVector3().ToNumerics();
        }

        if (UnityVector.SqrMagnitude(direction) >= MilkingPoseMath.DirectionEpsilonSquared)
        {
            bodyFacing = Pawn_RotationTracker.RotFromAngleBiased(UnityVector.AngleFlat(direction));
        }
    }

    private static void ApplyRootTransform(MilkingVisualState state, PawnDrawParms parms, int now, ref Vector3 offset, ref float angle, ref Vector3 scale)
    {
        int age = AgeOf(state, now);
        int pulseAge = now - state.pulseStartTick;
        switch (state.role)
        {
            case MilkingVisualRole.SelfMilking:
            {
                float counterSway = UnityMathf.Sin(age * 0.15f + 1.2f);
                float pulse = MilkingPoseMath.PulseEnvelope(pulseAge);
                offset += FlatOffset(-0.018f + UnityMathf.Sin(age * 0.05f) * 0.012f);
                scale = new Vector3(
                    scale.X * (1f + counterSway * 0.012f - pulse * 0.018f),
                    scale.Y,
                    scale.Z * (1f - counterSway * 0.01f + pulse * 0.028f));
                MilkingPoseMath.AddPulseTransform(pulseAge, state.pulseSeed, 1f, ref offset, ref scale);
                break;
            }

            case MilkingVisualRole.AssistedTarget:
            {
                Vector3 toHelper = DirectionToPartner(state);
                offset += toHelper * 0.13f + new Vector3(0f, 0f, -0.08f);
                offset += UnityVector.Right * (UnityMathf.Sin(age * 0.13f + state.pawn.thingIDNumber * 0.17f) * 0.014f);
                angle += MilkingPoseMath.LeanAngleForDirection(toHelper, 7f);
                MilkingPoseMath.AddPulseTransform(pulseAge, state.pulseSeed, 1f, ref offset, ref scale);
                break;
            }

            case MilkingVisualRole.Helper:
            {
                Vector3 toTarget = DirectionToPartner(state);
                offset += toTarget * 0.08f;
                angle -= MilkingPoseMath.LeanAngleForDirection(toTarget, 3f);
                MilkingPoseMath.AddPulseTransform(pulseAge, state.pulseSeed, 0.45f, ref offset, ref scale);
                break;
            }

            default:
                ApplyMilkInteractionBodyMotion(state, parms, age, ref offset, ref angle, ref scale);
                break;
        }
    }

    private static bool TryGetHeadTransform(MilkingVisualState state, PawnDrawParms parms, int now, ref Vector3 offset, ref float angle, ref Vector3 scale)
    {
        int age = AgeOf(state, now);
        switch (state.role)
        {
            case MilkingVisualRole.FeedingSource:
                offset += FlatOffset(-0.026f + UnityMathf.Sin(age * 0.075f + 0.8f) * 0.008f);
                return true;

            case MilkingVisualRole.FeedingRecipient:
            {
                if (!state.hasExplicitHead)
                {
                    return false;
                }

                float wave = UnityMathf.Sin(age * 0.075f);
                offset += FlatOffset(wave * 0.005f);
                angle += wave * 0.8f;
                return true;
            }
        }

        if (parms.pawn?.ageTracker != null && !parms.pawn.ageTracker.Adult)
        {
            return false;
        }

        if (state.role == MilkingVisualRole.Helper)
        {
            Vector3 toTarget = DirectionToPartner(state);
            offset += FlatOffset(0.045f - HelperHeadDownOffset(state.pawn) + UnityMathf.Sin(age * 0.12f) * 0.01f);
            offset += toTarget * 0.025f;
            angle -= MilkingPoseMath.LeanAngleForDirection(toTarget, 6f);
            scale = new Vector3(scale.X * 0.985f, scale.Y, scale.Z * 1.025f);
            return true;
        }

        ApplyMugirlHeadRhythm(parms.facing, age, MilkingPoseMath.PulseEnvelope(now - state.pulseStartTick), ref offset, ref angle);
        return true;
    }

    // 被挤奶者的头部节奏：正面/背面上下点头，侧面随节拍左右偏转。
    private static void ApplyMugirlHeadRhythm(Rot4 facing, int age, float pulse, ref Vector3 offset, ref float angle)
    {
        float wave = UnityMathf.Sin(age * 0.075f);
        float beat = wave * 0.55f + pulse;
        if (facing == Rot4.North || facing == Rot4.South)
        {
            offset += FlatOffset(wave * 0.012f + pulse * 0.03f);
        }
        else if (facing == Rot4.East || facing == Rot4.West)
        {
            float sideSign = facing == Rot4.East ? -1f : 1f;
            angle += sideSign * beat * 5.5f;
            offset += FlatOffset(pulse * 0.012f);
        }
    }

    // 喂奶双方的 Root：在建立时求解的世界位移与角度上叠加呼吸起伏；位移转回到 Pawn 当前身体角度的局部坐标。
    private static void ApplyMilkInteractionBodyMotion(MilkingVisualState state, PawnDrawParms parms, int age, ref Vector3 offset, ref float angle, ref Vector3 scale)
    {
        float baseAngle = parms.pawn?.Drawer?.renderer?.BodyAngle(parms.flags) ?? 0f;
        Vector3 worldOffset = state.rootWorldOffset;
        float wave = UnityMathf.Sin(age * 0.075f + state.pawn.thingIDNumber * 0.11f);
        float motionScale = UnityMathf.Clamp(state.bodyHeight / MilkingPoseMath.FallbackBodySize, 0.65f, 1.35f);
        float angleWave = 0f;

        if (state.role == MilkingVisualRole.FeedingSource)
        {
            worldOffset.Z += wave * 0.008f * motionScale;
            scale = new Vector3(scale.X * (1f - wave * 0.004f), scale.Y, scale.Z * (1f + wave * 0.006f));
        }
        else if (state.role == MilkingVisualRole.FeedingRecipient)
        {
            worldOffset += DirectionToPartner(state) * (wave * 0.006f * motionScale);
            angleWave = wave * 1.5f;
        }

        offset += UnityVector.RotatedBy(worldOffset, 0f - baseAngle);
        angle += UnityMathf.DeltaAngle(baseAngle, state.rootWorldAngle) + angleWave;
    }
}
