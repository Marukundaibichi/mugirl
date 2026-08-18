using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public static partial class MugirlMilkingAnimation
    {
        private const int StaleAfterTicks = 6;

        // StaticCacheLifecycle: 每局游戏的挤奶动画状态，按 thingIDNumber 索引；工作结束、Pawn 离图/销毁、陈旧状态检查和全局重置时清空。
        private static readonly Dictionary<int, MilkingVisualState> states = new Dictionary<int, MilkingVisualState>();
        // StaticCacheLifecycle: 单次清理使用的临时 key 列表；复用前和移除后都会清空。
        private static readonly List<int> tmpStateKeysToRemove = new List<int>();

        private class MilkingVisualState
        {
            public Pawn pawn;
            public Pawn partner;
            public MugirlMilkingVisualRole role;
            public int startTick;
            public int lastTick;
            public int nextPulseTick;
            public int pulseStartTick = -99999;
            public int pulseSeed;
        }

        public static void NotifyPawnLifecycleEnded(Pawn pawn)
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

            if (tmpStateKeysToRemove.Count == 0)
            {
                return;
            }

            for (int i = 0; i < tmpStateKeysToRemove.Count; i++)
            {
                int key = tmpStateKeysToRemove[i];
                if (states.TryGetValue(key, out MilkingVisualState state))
                {
                    StopNativeAnimation(state.pawn);
                }
                states.Remove(key);
            }

            tmpStateKeysToRemove.Clear();
        }

        public static void ResetTransientState()
        {
            foreach (MilkingVisualState state in states.Values)
            {
                StopNativeAnimation(state.pawn);
            }
            states.Clear();
            tmpStateKeysToRemove.Clear();
        }

        public static bool HasActiveAnimation(Pawn pawn)
        {
            return TryGetState(pawn, out _);
        }

        private static MilkingVisualState EnsureState(Pawn pawn, Pawn partner, MugirlMilkingVisualRole role, int now)
        {
            int key = pawn.thingIDNumber;
            if (!states.TryGetValue(key, out MilkingVisualState state) || state.pawn != pawn || state.role != role || state.partner != partner)
            {
                state = new MilkingVisualState
                {
                    pawn = pawn,
                    partner = partner,
                    role = role,
                    startTick = now,
                    lastTick = now,
                    nextPulseTick = now + ((role == MugirlMilkingVisualRole.SelfMilking) ? Rand.RangeInclusive(35, 70) : Rand.RangeInclusive(25, 55))
                };
                states[key] = state;
            }
            else
            {
                state.pawn = pawn;
                state.partner = partner;
                state.lastTick = now;
            }
            EnsureNativeAnimation(state);
            return state;
        }

        private static bool TryGetState(Pawn pawn, out MilkingVisualState state)
        {
            state = null;
            if (pawn == null)
            {
                return false;
            }

            if (!states.TryGetValue(pawn.thingIDNumber, out state))
            {
                StopNativeAnimation(pawn);
                return false;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                ResetTransientState();
                state = null;
                return false;
            }

            if (state.pawn != pawn || !Valid(state.pawn) || now - state.lastTick > StaleAfterTicks)
            {
                states.Remove(pawn.thingIDNumber);
                StopNativeAnimation(pawn);
                state = null;
                return false;
            }

            if (StateNeedsPartner(state) && !Valid(state.partner))
            {
                RemoveIfMatches(state.partner, state.pawn);
                states.Remove(pawn.thingIDNumber);
                StopNativeAnimation(pawn);
                state = null;
                return false;
            }

            return true;
        }

        private static void RemoveIfMatches(Pawn pawn, Pawn expectedPartner)
        {
            if (pawn == null)
            {
                return;
            }

            if (states.TryGetValue(pawn.thingIDNumber, out MilkingVisualState state) && state.pawn == pawn && state.partner == expectedPartner)
            {
                states.Remove(pawn.thingIDNumber);
                StopNativeAnimation(pawn);
            }
        }

        private static void EnsureNativeAnimation(MilkingVisualState state)
        {
            PawnRenderer renderer = state?.pawn?.Drawer?.renderer;
            AnimationDef desired = AnimationFor(state);
            if (renderer == null || desired == null || renderer.CurAnimation == desired)
            {
                return;
            }

            // 其他原生动画优先；仅在动画槽为空或切换 Mugirl 自己的角色动画时接管。
            if (renderer.CurAnimation == null || IsMugirlMilkingAnimation(renderer.CurAnimation))
            {
                renderer.SetAnimation(desired);
            }
        }

        private static void StopNativeAnimation(Pawn pawn)
        {
            PawnRenderer renderer = pawn?.Drawer?.renderer;
            if (renderer != null && IsMugirlMilkingAnimation(renderer.CurAnimation))
            {
                renderer.SetAnimation(null);
            }
        }

        private static AnimationDef AnimationFor(MilkingVisualState state)
        {
            if (state == null)
            {
                return null;
            }

            switch (state.role)
            {
                case MugirlMilkingVisualRole.SelfMilking:
                    return Mugirl_DefOf.Mugirl_MilkingSelfAnimation;
                case MugirlMilkingVisualRole.AssistedTarget:
                    return Mugirl_DefOf.Mugirl_MilkingTargetAnimation;
                case MugirlMilkingVisualRole.Helper:
                    return Mugirl_DefOf.Mugirl_MilkingHelperAnimation;
                default:
                    return null;
            }
        }

        private static bool IsMugirlMilkingAnimation(AnimationDef animation)
        {
            return animation != null &&
                (animation == Mugirl_DefOf.Mugirl_MilkingSelfAnimation ||
                 animation == Mugirl_DefOf.Mugirl_MilkingTargetAnimation ||
                 animation == Mugirl_DefOf.Mugirl_MilkingHelperAnimation);
        }

        private static bool StateNeedsPartner(MilkingVisualState state)
        {
            return state != null && state.role != MugirlMilkingVisualRole.SelfMilking;
        }
    }
}
