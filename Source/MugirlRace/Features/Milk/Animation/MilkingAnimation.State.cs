using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Milk;

// 动画状态存储与原生 AnimationDef 槽位管理。所有写入（增删条目、切换 AnimationDef）只在主线程的 tick、
// Job 结束、Pawn 离图/销毁与每局重置中发生；1.7 的 DrawPhase.ParallelPreDraw 在多个工作线程上并行调用
// 渲染端读取，读取期间主线程阻塞在 Complete()，因此 Dictionary 只有并发读，没有并发写。
public static partial class MilkingAnimation
{
    private const int StaleAfterTicks = 6;

    // StaticCacheLifecycle: 每局游戏的挤奶动画状态，按 thingIDNumber 索引；工作结束、Pawn 离图/销毁、
    // 主线程过期清理（SweepStaleStates）与 GameComponent_Storyline 的每局重置（ResetTransientState）时移除。
    private static readonly Dictionary<int, MilkingVisualState> states = [];

    // StaticCacheLifecycle: 清理时复用的临时 key 列表；每次使用前后清空。
    private static readonly List<int> tmpStateKeysToRemove = [];

    // StaticCacheLifecycle: 上次过期清理的游戏 tick，保证每 tick 至多清理一次；每局重置时归位。
    private static int lastSweepTick = -1;

    public static void NotifyPawnLifecycleEnded(Pawn? pawn)
    {
        if (pawn == null || states.Count == 0)
        {
            return;
        }

        tmpStateKeysToRemove.Clear();
        foreach (KeyValuePair<int, MilkingVisualState> entry in states)
        {
            MilkingVisualState state = entry.Value;
            if (state.pawn == pawn || state.partner == pawn)
            {
                tmpStateKeysToRemove.Add(entry.Key);
            }
        }

        RemoveCollectedStates();
    }

    // 在剧情状态原有重置时点清空动画状态，并结束奶量 Gizmo 的弹窗会话。
    [GameLifecycleHook(GameLifecycleEvent.TransientStateReset, Order = 400)]
    public static void ResetTransientState()
    {
        foreach (MilkingVisualState state in states.Values)
        {
            StopNativeAnimation(state.pawn);
        }

        states.Clear();
        tmpStateKeysToRemove.Clear();
        lastSweepTick = -1;
        MilkGaugePopupSession.Reset();
    }

    private static MilkingVisualState EnsureState(Pawn pawn, Pawn? partner, MilkingVisualRole role, int now, InteractionPose? pose = null)
    {
        int key = pawn.thingIDNumber;
        if (!states.TryGetValue(key, out MilkingVisualState? state) || state.pawn != pawn || state.role != role || state.partner != partner)
        {
            int firstPulseDelay = role == MilkingVisualRole.SelfMilking ? Rand.RangeInclusive(35, 70) : Rand.RangeInclusive(25, 55);
            state = new MilkingVisualState(pawn, partner, role, now, now + firstPulseDelay);
            states[key] = state;
        }
        else
        {
            state.lastTick = now;
        }

        if (pose is { } value)
        {
            state.rootWorldOffset = value.RootWorldOffset;
            state.rootWorldAngle = value.RootWorldAngle;
            state.hasExplicitHead = value.HasExplicitHead;
            state.bodyHeight = value.BodyHeight;
        }

        EnsureNativeAnimation(state);
        return state;
    }

    // 状态仍有效：属于该 Pawn、Pawn 在图上、最近 StaleAfterTicks 内被 tick 过，需要搭档的角色其搭档也有效。只读。
    private static bool IsLive(MilkingVisualState state, Pawn pawn, int now)
    {
        return state.pawn == pawn
            && Valid(pawn)
            && now - state.lastTick <= StaleAfterTicks
            && (state.role == MilkingVisualRole.SelfMilking || Valid(state.partner));
    }

    // 只读查询，可在并行预绘制线程中调用。
    private static bool TryGetLiveState(Pawn? pawn, [NotNullWhen(true)] out MilkingVisualState? state)
    {
        state = null;
        if (pawn == null || states.Count == 0 || !states.TryGetValue(pawn.thingIDNumber, out state))
        {
            return false;
        }

        if (!TickUtility.TryGetCurrentGameTick(out int now) || !IsLive(state, pawn, now))
        {
            state = null;
            return false;
        }

        return true;
    }

    // 主线程清理：移除过期或搭档失效的状态并交还原生动画槽。代替 1.6 在渲染路径中的删除。
    private static void SweepStaleStates(int now)
    {
        if (states.Count == 0 || now == lastSweepTick)
        {
            return;
        }

        lastSweepTick = now;
        tmpStateKeysToRemove.Clear();
        foreach (KeyValuePair<int, MilkingVisualState> entry in states)
        {
            if (!IsLive(entry.Value, entry.Value.pawn, now))
            {
                tmpStateKeysToRemove.Add(entry.Key);
            }
        }

        RemoveCollectedStates();
    }

    private static void RemoveCollectedStates()
    {
        for (int i = 0; i < tmpStateKeysToRemove.Count; i++)
        {
            int key = tmpStateKeysToRemove[i];
            if (states.Remove(key, out MilkingVisualState? state))
            {
                StopNativeAnimation(state.pawn);
            }
        }

        tmpStateKeysToRemove.Clear();
    }

    private static void RemoveIfMatches(Pawn? pawn, Pawn? expectedPartner)
    {
        if (pawn != null
            && states.TryGetValue(pawn.thingIDNumber, out MilkingVisualState? state)
            && state.pawn == pawn
            && state.partner == expectedPartner)
        {
            states.Remove(pawn.thingIDNumber);
            StopNativeAnimation(pawn);
        }
    }

    // 其他原生动画优先：只在动画槽为空或正在播放 Mugirl 自己的角色动画时接管。
    private static void EnsureNativeAnimation(MilkingVisualState state)
    {
        PawnRenderer? renderer = state.pawn.Drawer?.renderer;
        AnimationDef desired = AnimationFor(state.role);
        if (renderer == null || renderer.CurAnimation == desired)
        {
            return;
        }

        if (renderer.CurAnimation == null || IsMugirlMilkingAnimation(renderer.CurAnimation))
        {
            renderer.SetAnimation(desired);
        }
    }

    private static void StopNativeAnimation(Pawn? pawn)
    {
        PawnRenderer? renderer = pawn?.Drawer?.renderer;
        if (renderer != null && IsMugirlMilkingAnimation(renderer.CurAnimation))
        {
            renderer.SetAnimation(null);
        }
    }

    private static AnimationDef AnimationFor(MilkingVisualRole role)
    {
        return role switch
        {
            MilkingVisualRole.AssistedTarget => MilkDefOf.Mugirl_MilkingTargetAnimation,
            MilkingVisualRole.Helper => MilkDefOf.Mugirl_MilkingHelperAnimation,
            MilkingVisualRole.FeedingSource => MilkDefOf.Mugirl_MilkInteractionSourceAnimation,
            MilkingVisualRole.FeedingRecipient => MilkDefOf.Mugirl_MilkInteractionRecipientAnimation,
            _ => MilkDefOf.Mugirl_MilkingSelfAnimation,
        };
    }

    private static bool IsMugirlMilkingAnimation(AnimationDef? animation)
    {
        return animation != null
            && (animation == MilkDefOf.Mugirl_MilkingSelfAnimation
                || animation == MilkDefOf.Mugirl_MilkingTargetAnimation
                || animation == MilkDefOf.Mugirl_MilkingHelperAnimation
                || animation == MilkDefOf.Mugirl_MilkInteractionSourceAnimation
                || animation == MilkDefOf.Mugirl_MilkInteractionRecipientAnimation);
    }

    // 单个 Pawn 的动画状态。字段只在主线程写入。
    private sealed class MilkingVisualState(Pawn pawn, Pawn? partner, MilkingVisualRole role, int now, int nextPulseTick)
    {
        public readonly Pawn pawn = pawn;
        public readonly Pawn? partner = partner;
        public readonly MilkingVisualRole role = role;
        public readonly int startTick = now;
        public int lastTick = now;
        public int nextPulseTick = nextPulseTick;
        public int pulseStartTick = -99999;
        public int pulseSeed;
        public Vector3 rootWorldOffset;
        public float rootWorldAngle;
        public bool hasExplicitHead;
        public float bodyHeight = MilkingPoseMath.FallbackBodySize;
    }
}
