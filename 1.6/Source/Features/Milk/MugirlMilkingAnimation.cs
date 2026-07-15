using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public enum MugirlMilkingVisualRole
    {
        SelfMilking,
        AssistedTarget,
        Helper
    }

    public static partial class MugirlMilkingAnimation
    {
        private const int PulseDurationTicks = 18;
        private const float MugirlHelperHeadDownOffset = 0.03f;

        public static void Start(Pawn doer, Pawn target)
        {
            if (!Valid(doer) || !Valid(target))
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                return;
            }

            if (doer == target)
            {
                EnsureState(target, null, MugirlMilkingVisualRole.SelfMilking, now);
            }
            else if (CanAnimateHelper(doer))
            {
                EnsureState(target, doer, MugirlMilkingVisualRole.AssistedTarget, now);
                EnsureState(doer, target, MugirlMilkingVisualRole.Helper, now);
                FaceEachOther(doer, target);
            }
            else
            {
                EnsureState(target, null, MugirlMilkingVisualRole.SelfMilking, now);
            }
        }

        public static void Tick(Pawn doer, Pawn target)
        {
            if (!Valid(doer) || !Valid(target))
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                ResetTransientState();
                return;
            }

            if (doer == target)
            {
                MilkingVisualState state = EnsureState(target, null, MugirlMilkingVisualRole.SelfMilking, now);
                state.lastTick = now;
                if (now >= state.nextPulseTick)
                {
                    TriggerPulse(state, spawnMilkSpray: true);
                    state.nextPulseTick = now + Rand.RangeInclusive(65, 120);
                }
            }
            else if (CanAnimateHelper(doer))
            {
                FaceEachOther(doer, target);
                MilkingVisualState targetState = EnsureState(target, doer, MugirlMilkingVisualRole.AssistedTarget, now);
                MilkingVisualState helperState = EnsureState(doer, target, MugirlMilkingVisualRole.Helper, now);
                targetState.lastTick = now;
                helperState.lastTick = now;

                if (now >= targetState.nextPulseTick)
                {
                    TriggerPulse(targetState, spawnMilkSpray: true);
                    TriggerPulse(helperState, spawnMilkSpray: false);
                    int next = now + Rand.RangeInclusive(45, 85);
                    targetState.nextPulseTick = next;
                    helperState.nextPulseTick = next;
                }
            }
            else
            {
                MilkingVisualState state = EnsureState(target, null, MugirlMilkingVisualRole.SelfMilking, now);
                state.lastTick = now;
                if (now >= state.nextPulseTick)
                {
                    TriggerPulse(state, spawnMilkSpray: true);
                    state.nextPulseTick = now + Rand.RangeInclusive(65, 120);
                }
            }
        }

        public static void End(Pawn doer, Pawn target)
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

        public static bool TryGetPawnTransform(Pawn pawn, PawnRenderFlags flags, out Vector3 offset, out Quaternion rotation, out Vector3 scale)
        {
            offset = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (flags.FlagSet(PawnRenderFlags.Portrait) || flags.FlagSet(PawnRenderFlags.Cache) || !TryGetState(pawn, out MilkingVisualState state))
            {
                return false;
            }

            int now = MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick);
            int age = Mathf.Max(0, now - state.startTick);
            switch (state.role)
            {
                case MugirlMilkingVisualRole.SelfMilking:
                    ApplySelfMilkingBodyMotion(state, age, ref offset, ref rotation, ref scale);
                    AddPulseTransform(state, 1f, ref offset, ref scale);
                    return true;

                case MugirlMilkingVisualRole.AssistedTarget:
                    Vector3 toHelper = DirectionToPartner(state);
                    offset += toHelper * 0.13f + new Vector3(0f, 0f, -0.08f);
                    offset += Vector3.right * (Mathf.Sin(age * 0.13f + pawn.thingIDNumber * 0.17f) * 0.014f);
                    rotation *= Quaternion.AngleAxis(LeanAngleForDirection(toHelper, 7f), Vector3.up);
                    AddPulseTransform(state, 1f, ref offset, ref scale);
                    return true;

                case MugirlMilkingVisualRole.Helper:
                    Vector3 toTarget = DirectionToPartner(state);
                    offset += toTarget * 0.08f;
                    rotation *= Quaternion.AngleAxis(-LeanAngleForDirection(toTarget, 3f), Vector3.up);
                    AddPulseTransform(state, 0.45f, ref offset, ref scale);
                    return true;
            }

            return false;
        }

        private static void ApplySelfMilkingBodyMotion(MilkingVisualState state, int age, ref Vector3 offset, ref Quaternion rotation, ref Vector3 scale)
        {
            float counterSway = Mathf.Sin(age * 0.15f + 1.2f);
            float pulse = PulseEnvelope(state);

            offset += new Vector3(0f, 0f, -0.018f + Mathf.Sin(age * 0.05f) * 0.012f);
            scale = new Vector3(
                scale.x * (1f + counterSway * 0.012f - pulse * 0.018f),
                scale.y,
                scale.z * (1f - counterSway * 0.01f + pulse * 0.028f));
        }

        public static void AdjustDrawFacing(Pawn pawn, PawnRenderFlags flags, ref Rot4 bodyFacing)
        {
            if (flags.FlagSet(PawnRenderFlags.Portrait) || flags.FlagSet(PawnRenderFlags.Cache) || !TryGetState(pawn, out MilkingVisualState state) || !Valid(state.partner))
            {
                return;
            }

            bodyFacing = RotFacingPartner(state);
        }

        public static void ModifyNodeTransform(PawnRenderNode node, PawnDrawParms parms, ref Vector3 offset, ref Vector3 pivot, ref Quaternion rotation, ref Vector3 scale)
        {
            if (parms.flags.FlagSet(PawnRenderFlags.Portrait) || !TryGetState(parms.pawn, out MilkingVisualState state))
            {
                return;
            }

            PawnRenderNodeTagDef tagDef = node?.Props?.tagDef;
            if (tagDef != PawnRenderNodeTagDefOf.Head && tagDef != PawnRenderNodeTagDefOf.ApparelHead)
            {
                return;
            }

            if (parms.pawn?.ageTracker != null && !parms.pawn.ageTracker.Adult)
            {
                return;
            }

            if (state.role == MugirlMilkingVisualRole.Helper)
            {
                ApplyHelperHeadLift(state, ref offset, ref rotation, ref scale);
                return;
            }

            ApplyMugirlHeadRhythm(state, parms, ref offset, ref pivot, ref rotation);
        }

        private static void ApplyHelperHeadLift(MilkingVisualState state, ref Vector3 offset, ref Quaternion rotation, ref Vector3 scale)
        {
            int age = Mathf.Max(0, MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.startTick);
            Vector3 toTarget = DirectionToPartner(state);
            float headDownOffset = MugirlIdentity.IsMugirlPawn(state.pawn) ? MugirlHelperHeadDownOffset : 0f;
            offset += new Vector3(0f, 0f, 0.045f - headDownOffset + Mathf.Sin(age * 0.12f) * 0.01f);
            offset += toTarget * 0.025f;
            rotation *= Quaternion.AngleAxis(-LeanAngleForDirection(toTarget, 6f), Vector3.up);
            scale = new Vector3(scale.x * 0.985f, scale.y, scale.z * 1.025f);
        }

        private static void ApplyMugirlHeadRhythm(MilkingVisualState state, PawnDrawParms parms, ref Vector3 offset, ref Vector3 pivot, ref Quaternion rotation)
        {
            int age = Mathf.Max(0, MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.startTick);
            float wave = Mathf.Sin(age * 0.075f);
            float pulse = PulseEnvelope(state);
            float beat = wave * 0.55f + pulse;

            if (parms.facing == Rot4.North || parms.facing == Rot4.South)
            {
                offset += new Vector3(0f, 0f, wave * 0.012f + pulse * 0.03f);
            }
            else if (parms.facing == Rot4.East || parms.facing == Rot4.West)
            {
                float sideSign = parms.facing == Rot4.East ? -1f : 1f;
                pivot += new Vector3(0f, 0f, -0.08f);
                rotation *= Quaternion.AngleAxis(sideSign * beat * 5.5f, Vector3.up);
                offset += new Vector3(0f, 0f, pulse * 0.012f);
            }
        }

        private static void TriggerPulse(MilkingVisualState state, bool spawnMilkSpray)
        {
            int now = MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick);
            state.pulseStartTick = now;
            state.pulseSeed = Gen.HashCombineInt(state.pawn.thingIDNumber, now);

            if (spawnMilkSpray)
            {
                ThrowMilkSpray(state.pawn);
            }
        }

        private static void AddPulseTransform(MilkingVisualState state, float strength, ref Vector3 offset, ref Vector3 scale)
        {
            int pulseAge = MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.pulseStartTick;
            if (pulseAge < 0 || pulseAge >= PulseDurationTicks)
            {
                return;
            }

            float progress = pulseAge / (float)PulseDurationTicks;
            float envelope = PulseEnvelope(state);
            float stretch = Mathf.Sin(progress * Mathf.PI * 2f);
            scale = new Vector3(
                scale.x * (1f - stretch * 0.035f * strength),
                scale.y,
                scale.z * (1f + stretch * 0.075f * strength));

            int jitterStep = pulseAge / 2;
            Vector3 jitter = new Vector3(
                NoiseSigned(state.pulseSeed + jitterStep * 92821),
                0f,
                NoiseSigned(state.pulseSeed + jitterStep * 17137));
            if (jitter.sqrMagnitude > 0.0001f)
            {
                offset += jitter.normalized * (envelope * 0.045f * strength);
            }
        }

        private static float PulseEnvelope(MilkingVisualState state)
        {
            int pulseAge = MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.pulseStartTick;
            if (pulseAge < 0 || pulseAge >= PulseDurationTicks)
            {
                return 0f;
            }
            return Mathf.Sin(pulseAge / (float)PulseDurationTicks * Mathf.PI);
        }

        private static void ThrowMilkSpray(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return;
            }

            FleckDef foamSpray = MugirlOptionalDefs.FleckDefs.FoamSpray;
            FleckDef fallbackSplash = MugirlOptionalDefs.FleckDefs.MugirlMilkSpray
                ?? MugirlOptionalDefs.FleckDefs.GroundWaterSplash;

            FleckDef mainFleck = foamSpray ?? fallbackSplash;
            if (mainFleck == null)
            {
                return;
            }

            Vector3 facing = pawn.Rotation.FacingCell.ToVector3();
            Vector3 side = new Vector3(facing.z, 0f, -facing.x);
            float frontLift = pawn.Rotation == Rot4.South ? 0.07f : 0f;
            Vector3 basePos = pawn.DrawPos + facing * 0.16f + new Vector3(0f, 0f, -0.13f + frontLift);
            float baseAngle = pawn.Rotation.AsAngle;

            for (int i = 0; i < Rand.RangeInclusive(6, 9); i++)
            {
                Vector3 start = basePos + side * Rand.Range(-0.08f, 0.08f) + facing * Rand.Range(0f, 0.18f);
                FleckCreationData data = FleckMaker.GetDataStatic(start, pawn.Map, mainFleck, Rand.Range(0.45f, 0.8f));
                data.instanceColor = new Color(1f, 1f, 1f, Rand.Range(0.72f, 0.95f));
                data.velocityAngle = baseAngle + Rand.Range(-18f, 18f);
                data.velocitySpeed = Rand.Range(0.45f, 0.82f);
                data.rotationRate = Rand.Range(-60f, 60f);
                data.airTimeLeft = Rand.Range(0.22f, 0.42f);
                pawn.Map.flecks.CreateFleck(data);
            }
        }

        private static void FaceEachOther(Pawn doer, Pawn target)
        {
            if (Valid(doer) && Valid(target))
            {
                doer.rotationTracker?.Face(target.DrawPos);
                target.rotationTracker?.Face(doer.DrawPos);
            }
        }

        private static Rot4 RotFacingPartner(MilkingVisualState state)
        {
            Vector3 direction = state.partner.DrawPos - state.pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = (state.partner.Position - state.pawn.Position).ToVector3();
                direction.y = 0f;
            }
            if (direction.sqrMagnitude < 0.0001f)
            {
                return state.pawn.Rotation;
            }
            return Pawn_RotationTracker.RotFromAngleBiased(direction.AngleFlat());
        }

        private static Vector3 DirectionToPartner(MilkingVisualState state)
        {
            if (state.partner == null)
            {
                return Vector3.zero;
            }

            Vector3 direction = state.partner.DrawPos - state.pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }
            return direction.normalized;
        }

        private static float LeanAngleForDirection(Vector3 direction, float maxAngle)
        {
            if (direction == Vector3.zero)
            {
                return 0f;
            }
            return Mathf.Clamp(0f - direction.x * maxAngle, 0f - maxAngle, maxAngle);
        }

        private static float NoiseSigned(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return ((x & 65535U) / 32767.5f) - 1f;
            }
        }

        private static bool Valid(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map != null;
        }

        private static bool CanAnimateHelper(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid;
        }

    }
}
