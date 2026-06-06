using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MooGirl
{
    public static class MountedPawnMeleeSupport
    {
        private static readonly MethodInfo GetNonMissChanceMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "GetNonMissChance");
        private static readonly MethodInfo GetDodgeChanceMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "GetDodgeChance");
        private static readonly MethodInfo SoundHitPawnMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "SoundHitPawn");
        private static readonly MethodInfo SoundHitBuildingMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "SoundHitBuilding");
        private static readonly MethodInfo SoundMissMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "SoundMiss");
        private static readonly MethodInfo SoundDodgeMethod = AccessTools.Method(typeof(Verb_MeleeAttack), "SoundDodge");
        private static readonly FieldInfo LastShotTickField = AccessTools.Field(typeof(Verb), "lastShotTick");

        public static void Tick(Comp_MooGirlMount comp)
        {
            Pawn carrier = comp?.MooPawn;
            Pawn rider = comp?.MountedPawn;
            Thing target = CurrentMeleeTarget(carrier);
            if (!CanRiderMelee(carrier, rider, target))
            {
                return;
            }

            Verb verb = rider.meleeVerbs.TryGetMeleeVerb(target);
            if (verb == null)
            {
                return;
            }

            Stance originalStance = carrier.stances?.curStance;
            try
            {
                using (new MountedVerbScope(verb, carrier, rider))
                {
                    if (verb.Available())
                    {
                        carrier.stances?.SetStance(new Stance_Mobile());
                        TryMountedMeleeAttack(rider, carrier, target, verb);
                    }
                }
            }
            finally
            {
                if (carrier.stances != null && carrier.stances.curStance != originalStance)
                {
                    carrier.stances.SetStance(originalStance ?? new Stance_Mobile());
                }
            }
        }

        private static void TryMountedMeleeAttack(Pawn rider, Pawn carrier, Thing target, Verb verb)
        {
            if (!(verb is Verb_MeleeAttack meleeVerb) || verb.verbProps == null)
            {
                return;
            }

            if (!Rand.Chance(GetNonMissChance(meleeVerb, target)))
            {
                PlaySound(SoundMissMethod, meleeVerb, target);
                SetLastShotTick(verb);
                return;
            }

            if (target is Pawn targetPawn && Rand.Chance(GetDodgeChance(meleeVerb, target)))
            {
                PlaySound(SoundDodgeMethod, meleeVerb, target);
                MoteMaker.ThrowText(targetPawn.DrawPos, targetPawn.Map, "TextMote_Dodge".Translate(), 1.9f);
                SetLastShotTick(verb);
                return;
            }

            DamageDef damageDef = verb.verbProps.meleeDamageDef ?? DamageDefOf.Blunt;
            float damageAmount = verb.verbProps.AdjustedMeleeDamageAmount(verb.tool, rider, verb.EquipmentSource, verb.HediffCompSource);
            float armorPenetration = verb.verbProps.AdjustedArmorPenetration(verb.tool, rider, verb.EquipmentSource, verb.HediffCompSource);
            BodyPartRecord hitPart = null;
            if (target is Pawn pawnTarget)
            {
                hitPart = pawnTarget.health.hediffSet.GetRandomNotMissingPart(damageDef, BodyPartHeight.Undefined, BodyPartDepth.Outside);
            }

            DamageInfo dinfo = new DamageInfo(
                damageDef,
                damageAmount,
                armorPenetration,
                (target.Position - carrier.Position).AngleFlat,
                rider,
                hitPart,
                verb.EquipmentSource?.def,
                DamageInfo.SourceCategory.ThingOrUnknown,
                target);
            dinfo.SetTool(verb.tool);
            target.TakeDamage(dinfo);
            PlaySound(target is Pawn ? SoundHitPawnMethod : SoundHitBuildingMethod, meleeVerb, target);
            SetLastShotTick(verb);
        }

        private static float GetNonMissChance(Verb_MeleeAttack verb, Thing target)
        {
            object result = GetNonMissChanceMethod?.Invoke(verb, new object[] { new LocalTargetInfo(target) });
            return result is float chance ? chance : 1f;
        }

        private static float GetDodgeChance(Verb_MeleeAttack verb, Thing target)
        {
            object result = GetDodgeChanceMethod?.Invoke(verb, new object[] { new LocalTargetInfo(target) });
            return result is float chance ? chance : 0f;
        }

        private static void PlaySound(MethodInfo method, Verb_MeleeAttack verb, Thing target)
        {
            SoundDef sound = method?.Invoke(verb, method == SoundDodgeMethod ? new object[] { target } : null) as SoundDef;
            sound?.PlayOneShot(target);
        }

        private static void SetLastShotTick(Verb verb)
        {
            LastShotTickField?.SetValue(verb, Find.TickManager.TicksGame);
        }

        public static void DrawWeapon(Comp_MooGirlMount comp)
        {
            Pawn rider = comp?.MountedPawn;
            Pawn carrier = comp?.MooPawn;
            ThingWithComps weapon = rider?.equipment?.Primary;
            if (rider == null || carrier == null || weapon == null || !weapon.def.IsMeleeWeapon)
            {
                return;
            }

            Vector3 drawPos = comp.WeaponDrawPos;
            drawPos += carrier.Rotation.RighthandCell.ToVector3() * 0.12f;
            using (MeleeAnimationCompat.SuspendIdleWeaponAnimation())
            {
                PawnRenderUtility.DrawCarriedWeapon(weapon, drawPos, carrier.Rotation, rider.ageTracker.CurLifeStage.equipmentDrawDistanceFactor);
            }
        }

        public static Thing CurrentMeleeTarget(Pawn carrier)
        {
            if (carrier == null)
            {
                return null;
            }

            LocalTargetInfo target = carrier.CurJob?.targetA ?? LocalTargetInfo.Invalid;
            if (carrier.CurJobDef == JobDefOf.AttackMelee && target.HasThing)
            {
                return target.Thing;
            }

            if (carrier.stances?.curStance is Stance_Busy busy && busy.verb?.verbProps?.IsMeleeAttack == true && busy.focusTarg.HasThing)
            {
                return busy.focusTarg.Thing;
            }

            Thing enemyTarget = carrier.mindState?.enemyTarget;
            if (enemyTarget != null)
            {
                return enemyTarget;
            }

            Pawn meleeThreat = carrier.mindState?.meleeThreat;
            if (meleeThreat != null)
            {
                return meleeThreat;
            }

            return null;
        }

        private static bool CanRiderMelee(Pawn carrier, Pawn rider, Thing target)
        {
            if (carrier == null || rider == null || target == null || target.Destroyed)
            {
                return false;
            }

            if (!carrier.Spawned || carrier.Map == null || target.Map != carrier.Map || target == carrier || target == rider)
            {
                return false;
            }

            if (!carrier.HostileTo(target) || !target.Position.AdjacentTo8WayOrInside(carrier.Position))
            {
                return false;
            }

            if (target is Pawn pawn && (pawn.Dead || pawn.Downed))
            {
                return false;
            }

            return !rider.Dead
                && !rider.Downed
                && !rider.InMentalState
                && !rider.IsBurning()
                && rider.Awake()
                && rider.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && !rider.WorkTagIsDisabled(WorkTags.Violent)
                && rider.stances?.FullBodyBusy == false;
        }

    }
}
