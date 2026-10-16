using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Milk;

// 程序化挤奶动画：由挤奶 JobDriver 在主线程调用 Start/Tick/End，维护每个 Pawn 的动画状态与原生 AnimationDef 槽位。
// 渲染端（AnimationWorker_Milking 与 GetDrawParms 补丁）只读状态，见 MilkingAnimation.Render.cs。
public static partial class MilkingAnimation
{
    private const float MugirlHelperHeadDownOffset = 0.03f;

    public static void Start(Pawn doer, Pawn target)
    {
        if (!Valid(doer) || !Valid(target) || !TickUtility.TryGetCurrentGameTick(out int now))
        {
            return;
        }

        SweepStaleStates(now);
        if (doer == target || !CanAnimateHelper(doer))
        {
            EnsureState(target, null, MilkingVisualRole.SelfMilking, now);
            return;
        }

        EnsureState(target, doer, MilkingVisualRole.AssistedTarget, now);
        EnsureState(doer, target, MilkingVisualRole.Helper, now);
        FaceEachOther(doer, target);
    }

    public static void Tick(Pawn doer, Pawn target)
    {
        if (!Valid(doer) || !Valid(target))
        {
            return;
        }

        if (!TickUtility.TryGetCurrentGameTick(out int now))
        {
            ResetTransientState();
            return;
        }

        SweepStaleStates(now);
        if (doer == target || !CanAnimateHelper(doer))
        {
            MilkingVisualState state = EnsureState(target, null, MilkingVisualRole.SelfMilking, now);
            state.lastTick = now;
            if (now >= state.nextPulseTick)
            {
                TriggerPulse(state, now, spawnMilkSpray: true);
                state.nextPulseTick = now + Rand.RangeInclusive(65, 120);
            }

            return;
        }

        FaceEachOther(doer, target);
        MilkingVisualState targetState = EnsureState(target, doer, MilkingVisualRole.AssistedTarget, now);
        MilkingVisualState helperState = EnsureState(doer, target, MilkingVisualRole.Helper, now);
        targetState.lastTick = now;
        helperState.lastTick = now;

        if (now >= targetState.nextPulseTick)
        {
            TriggerPulse(targetState, now, spawnMilkSpray: true);
            TriggerPulse(helperState, now, spawnMilkSpray: false);
            int next = now + Rand.RangeInclusive(45, 85);
            targetState.nextPulseTick = next;
            helperState.nextPulseTick = next;
        }
    }

    public static void End(Pawn? doer, Pawn? target)
    {
        if (doer == target)
        {
            RemoveIfMatches(doer, null);
        }
        else if (CanAnimateHelper(doer))
        {
            RemoveIfMatches(doer, target);
            RemoveIfMatches(target, doer);
        }
        else
        {
            RemoveIfMatches(target, null);
        }
    }

    private static void TriggerPulse(MilkingVisualState state, int now, bool spawnMilkSpray)
    {
        state.pulseStartTick = now;
        state.pulseSeed = Gen.HashCombineInt(state.pawn.thingIDNumber, now);
        if (spawnMilkSpray)
        {
            ThrowMilkSpray(state.pawn);
        }
    }

    // 喷乳 Fleck：在 Pawn 正前方略低处喷出 6-9 道奶雾。
    // 1.6 的循环条件每次迭代都重新掷 Rand.RangeInclusive(6, 9)，数量分布偏斜且多消耗随机数；这里只掷一次。
    private static void ThrowMilkSpray(Pawn pawn)
    {
        Map? map = pawn.Map;
        if (map == null)
        {
            return;
        }

        FleckDef sprayFleck = MilkDefOf.FoamSpray;
        Vector3 facing = pawn.Rotation.FacingCell.ToVector3().ToNumerics();
        Vector3 side = new(facing.Z, 0f, -facing.X);
        float frontLift = pawn.Rotation == Rot4.South ? 0.07f : 0f;
        Vector3 basePos = pawn.DrawPos.ToNumerics() + facing * 0.16f + new Vector3(0f, 0f, -0.13f + frontLift);
        float baseAngle = pawn.Rotation.AsAngle;

        int count = Rand.RangeInclusive(6, 9);
        for (int i = 0; i < count; i++)
        {
            Vector3 start = basePos + side * Rand.Range(-0.08f, 0.08f) + facing * Rand.Range(0f, 0.18f);
            FleckCreationData data = FleckMaker.GetDataStatic(start.ToUnity(), map, sprayFleck, Rand.Range(0.45f, 0.8f));
            data.instanceColor = new Color(1f, 1f, 1f, Rand.Range(0.72f, 0.95f));
            data.velocityAngle = baseAngle + Rand.Range(-18f, 18f);
            data.velocitySpeed = Rand.Range(0.45f, 0.82f);
            data.rotationRate = Rand.Range(-60f, 60f);
            data.airTimeLeft = Rand.Range(0.22f, 0.42f);
            map.flecks.CreateFleck(data);
        }
    }

    private static void FaceEachOther(Pawn doer, Pawn target)
    {
        doer.rotationTracker?.Face(target.DrawPos);
        target.rotationTracker?.Face(doer.DrawPos);
    }

    // 只读状态检查，可在并行预绘制线程中调用。
    private static bool Valid([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map != null;
    }

    private static bool CanAnimateHelper([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid;
    }

    private static int AgeOf(MilkingVisualState state, int now) => Math.Max(0, now - state.startTick);

    private static Vector3 DirectionToPartner(MilkingVisualState state)
    {
        Pawn? partner = state.partner;
        return partner == null
            ? Vector3.Zero
            : MilkingPoseMath.HorizontalDirection(state.pawn.DrawPos.ToNumerics(), partner.DrawPos.ToNumerics());
    }

    private static Vector3 FlatOffset(float z) => new(0f, 0f, z);

    private static float HelperHeadDownOffset(Pawn pawn) => MugirlIdentity.IsMugirlPawn(pawn) ? MugirlHelperHeadDownOffset : 0f;
}
