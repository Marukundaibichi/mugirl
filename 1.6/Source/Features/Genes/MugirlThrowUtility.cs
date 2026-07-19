using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class MugirlThrowUtility
    {
        private const float BuildingMassPerCell = 25f;
        private const float BuildingCostMassFactor = 0.5f;
        private const float LandingSearchRadius = 12f;

        internal static float ThrowStrength(Pawn pawn)
        {
            return pawn == null ? 0f : Mathf.Max(0f, pawn.GetStatValue(StatDefOf.CarryingCapacity));
        }

        internal static float EffectiveMass(Thing thing)
        {
            if (thing == null)
            {
                return 0f;
            }

            float mass = Mathf.Max(0.1f, thing.GetStatValue(StatDefOf.Mass));
            if (!(thing is Pawn))
            {
                mass *= Mathf.Max(1, thing.stackCount);
            }

            if (thing.def.category == ThingCategory.Building && !thing.def.statBases.StatListContains(StatDefOf.Mass))
            {
                float footprintMass = thing.def.Size.x * thing.def.Size.z * BuildingMassPerCell;
                float materialMass = Mathf.Max(thing.def.costStuffCount, 0) * BuildingCostMassFactor;
                mass = Mathf.Max(mass, Mathf.Max(footprintMass, materialMass));
            }

            return mass;
        }

        internal static float MaxThrowDistance(Pawn pawn, float mass)
        {
            float strength = ThrowStrength(pawn);
            if (strength <= 0f || mass <= 0f || mass > strength)
            {
                return 0f;
            }

            float ratio = Mathf.Clamp(strength / mass, 1f, 10f);
            return Mathf.Clamp(6f + 6f * Mathf.Sqrt(ratio), 8f, 25f);
        }

        internal static int ImpactDamage(float mass, float distance)
        {
            return Mathf.RoundToInt(Mathf.Clamp(8f + Mathf.Sqrt(Mathf.Max(mass, 0.1f)) * 7f + distance * 1.5f, 12f, 220f));
        }

        internal static float ExplosionRadius(int damage)
        {
            return Mathf.Clamp(1.7f + damage / 45f, 2f, 6f);
        }

        internal static bool CanPickUp(Pawn caster, Thing target, out string reason)
        {
            reason = null;
            if (caster == null || target == null || target == caster || target.Destroyed || !target.Spawned || target.Map != caster.Map)
            {
                reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
                return false;
            }

            if (target is Blueprint || target is Frame || target is Mote || target is Projectile || target is Skyfaller || target is Explosion || target is Fire || target is Thing_MugirlThrownObject || target is Thing_MugirlDunkProp)
            {
                reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
                return false;
            }

            ThingCategory category = target.def.category;
            if (category != ThingCategory.Item && category != ThingCategory.Pawn && category != ThingCategory.Building && category != ThingCategory.Plant)
            {
                reason = "Mugirl.Throw.InvalidTarget".Translate().ToString();
                return false;
            }

            if (!target.def.destroyable)
            {
                reason = "Mugirl.Throw.IndestructibleTarget".Translate(target.LabelCap).ToString();
                return false;
            }

            Thing_MugirlThrownObject existing = FindHeldController(caster);
            if (existing != null || FindDunkController(caster) != null)
            {
                reason = "Mugirl.Throw.AlreadyHolding".Translate().ToString();
                return false;
            }

            float mass = EffectiveMass(target);
            float strength = ThrowStrength(caster);
            if (mass > strength)
            {
                reason = "Mugirl.Throw.TooHeavy".Translate(mass.ToString("0.#"), strength.ToString("0.#")).ToString();
                return false;
            }

            return true;
        }

        internal static Thing_MugirlThrownObject FindHeldController(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            ThingDef controllerDef = Mugirl_DefOf.Mugirl_ThrowController;
            if (map == null || controllerDef == null)
            {
                return null;
            }

            List<Thing> controllers = map.listerThings.ThingsOfDef(controllerDef);
            for (int i = 0; i < controllers.Count; i++)
            {
                Thing_MugirlThrownObject controller = controllers[i] as Thing_MugirlThrownObject;
                if (controller != null && controller.Carrier == pawn && controller.IsHeld)
                {
                    return controller;
                }
            }

            return null;
        }

        internal static Thing_MugirlDunkProp FindDunkController(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            ThingDef controllerDef = Mugirl_DefOf.Mugirl_DunkProp;
            if (map == null || controllerDef == null)
            {
                return null;
            }

            List<Thing> controllers = map.listerThings.ThingsOfDef(controllerDef);
            for (int i = 0; i < controllers.Count; i++)
            {
                Thing_MugirlDunkProp controller = controllers[i] as Thing_MugirlDunkProp;
                if (controller != null && controller.Carrier == pawn && controller.HasPayload)
                {
                    return controller;
                }
            }

            return null;
        }

        internal static bool TryFindPlacementCell(Thing thing, IntVec3 preferred, Map map, Rot4 rotation, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (thing == null || map == null)
            {
                return false;
            }

            int cells = GenRadial.NumCellsInRadius(LandingSearchRadius);
            for (int i = 0; i < cells; i++)
            {
                IntVec3 candidate = preferred + GenRadial.RadialPattern[i];
                if (CanPlaceAt(thing, candidate, map, rotation))
                {
                    result = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool CanPlaceAt(Thing thing, IntVec3 cell, Map map, Rot4 rotation)
        {
            if (!cell.IsValid || !cell.InBounds(map))
            {
                return false;
            }

            if (thing.def.category == ThingCategory.Building)
            {
                AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                    thing.def,
                    cell,
                    rotation,
                    map,
                    godMode: false,
                    thingToIgnore: null,
                    thing: thing,
                    stuffDef: thing.Stuff,
                    ignoreEdgeArea: true,
                    ignoreInteractionSpots: true,
                    ignoreClearableFreeBuildings: false);
                return report.Accepted;
            }

            if (thing is Pawn)
            {
                return cell.Standable(map) && cell.GetFirstPawn(map) == null;
            }

            if (thing.def.category == ThingCategory.Item)
            {
                Building edifice = cell.GetEdifice(map);
                return edifice == null || edifice.def.surfaceType == SurfaceType.Item || edifice.def.surfaceType == SurfaceType.Eat || edifice is Building_Door;
            }

            return thing.def.CanSpawnAt(cell, rotation, map);
        }
    }
}
