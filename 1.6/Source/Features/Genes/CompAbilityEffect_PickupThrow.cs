using RimWorld;
using Verse;

namespace Mugirl
{
    public class CompProperties_AbilityEffect_PickupThrow : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityEffect_PickupThrow()
        {
            compClass = typeof(CompAbilityEffect_PickupThrow);
        }
    }

    public class CompAbilityEffect_PickupThrow : CompAbilityEffect
    {
        // 瞄准悬停期间 GUI 每事件读取读数；Stat 管线按 30 tick 短缓存，
        // 质量按悬停目标引用失效，换目标立即重算。
        private const int ReadoutCacheTicks = 30;
        private float cachedMass = -1f;
        private Thing cachedMassThing;
        private int cachedMassTick = int.MinValue;
        private float cachedStrength = -1f;
        private int cachedStrengthTick = int.MinValue;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Thing targetThing = target.Thing;
            string reason;
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (MugirlThrowUtility.CanPickUp(caster, targetThing, out reason))
            {
                return true;
            }

            if (throwMessages && !reason.NullOrEmpty())
            {
                Messages.Message(reason, targetThing ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent?.pawn;
            Thing targetThing = target.Thing;
            string reason;
            if (MugirlThrowUtility.CanPickUp(caster, targetThing, out reason))
            {
                Thing_MugirlThrownObject.TryCreate(caster, targetThing, parent);
            }
            else if (!reason.NullOrEmpty())
            {
                Messages.Message(reason, targetThing ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }

            base.Apply(target, dest);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            Pawn caster = parent?.pawn;
            if (thing == null || caster == null)
            {
                return null;
            }

            int tick = MugirlTickUtility.CurrentGameTickOrFallback(0);
            float mass = GetCachedEffectiveMass(thing, tick);
            float strength = GetCachedThrowStrength(caster, tick);
            return "Mugirl.Throw.PickupReadout".Translate(mass.ToString("0.#"), strength.ToString("0.#")).ToString();
        }

        private float GetCachedEffectiveMass(Thing thing, int tick)
        {
            if (thing == cachedMassThing
                && cachedMassTick >= 0 && tick < cachedMassTick + ReadoutCacheTicks)
            {
                return cachedMass;
            }

            cachedMass = MugirlThrowUtility.EffectiveMass(thing);
            cachedMassThing = thing;
            cachedMassTick = tick;
            return cachedMass;
        }

        private float GetCachedThrowStrength(Pawn caster, int tick)
        {
            if (cachedStrengthTick >= 0 && tick < cachedStrengthTick + ReadoutCacheTicks)
            {
                return cachedStrength;
            }

            cachedStrength = MugirlThrowUtility.ThrowStrength(caster);
            cachedStrengthTick = tick;
            return cachedStrength;
        }
    }
}
