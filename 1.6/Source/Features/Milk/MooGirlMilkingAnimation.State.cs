using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    public static partial class MooGirlMilkingAnimation
    {
        private const int StaleAfterTicks = 6;

        // StaticCacheLifecycle: per-game animation state keyed by thingIDNumber; cleared on job end, pawn despawn/destroy, stale-state checks and MooGirlStoryState reset.
        private static readonly Dictionary<int, MilkingVisualState> states = new Dictionary<int, MilkingVisualState>();
        // StaticCacheLifecycle: per-call scratch key list for safe state removal; cleared before reuse and after removal.
        private static readonly List<int> tmpStateKeysToRemove = new List<int>();

        private class MilkingVisualState
        {
            public Pawn pawn;
            public Pawn partner;
            public MooGirlMilkingVisualRole role;
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
                states.Remove(tmpStateKeysToRemove[i]);
            }

            tmpStateKeysToRemove.Clear();
        }

        public static void ResetTransientState()
        {
            states.Clear();
            tmpStateKeysToRemove.Clear();
        }

        public static bool HasActiveAnimation(Pawn pawn)
        {
            return TryGetState(pawn, out _);
        }

        private static MilkingVisualState EnsureState(Pawn pawn, Pawn partner, MooGirlMilkingVisualRole role, int now)
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
                    nextPulseTick = now + ((role == MooGirlMilkingVisualRole.SelfMilking) ? Rand.RangeInclusive(35, 70) : Rand.RangeInclusive(25, 55))
                };
                states[key] = state;
            }
            else
            {
                state.pawn = pawn;
                state.partner = partner;
                state.lastTick = now;
            }
            return state;
        }

        private static bool TryGetState(Pawn pawn, out MilkingVisualState state)
        {
            state = null;
            if (pawn == null || !states.TryGetValue(pawn.thingIDNumber, out state))
            {
                return false;
            }

            if (!MooGirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                states.Clear();
                state = null;
                return false;
            }

            if (state.pawn != pawn || !Valid(state.pawn) || now - state.lastTick > StaleAfterTicks)
            {
                states.Remove(pawn.thingIDNumber);
                state = null;
                return false;
            }

            if (StateNeedsPartner(state) && !Valid(state.partner))
            {
                RemoveIfMatches(state.partner, state.pawn);
                states.Remove(pawn.thingIDNumber);
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
            }
        }

        private static bool StateNeedsPartner(MilkingVisualState state)
        {
            return state != null && state.role != MooGirlMilkingVisualRole.SelfMilking;
        }
    }
}
