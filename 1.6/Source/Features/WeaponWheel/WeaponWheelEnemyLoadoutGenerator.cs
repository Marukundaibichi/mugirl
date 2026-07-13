using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    internal static class WeaponWheelEnemyLoadoutGenerator
    {
        private const int MinWeaponCount = 2;
        private const int MaxWeaponCount = 3;

        internal static void TryGenerateFor(Pawn pawn, PawnGenerationRequest request)
        {
            Faction playerFaction = Faction.OfPlayerSilentFail;
            Comp_WeaponWheel wheel = pawn?.TryGetComp<Comp_WeaponWheel>();
            if (wheel == null || !MugirlIdentity.IsMugirlPawn(pawn)
                || playerFaction == null || pawn.Faction == null || !pawn.Faction.HostileTo(playerFaction)
                || pawn.WorkTagIsDisabled(WorkTags.Violent) || pawn.WorkTagIsDisabled(WorkTags.Shooting)
                || pawn.equipment?.Primary == null || !pawn.equipment.Primary.def.IsRangedWeapon)
            {
                return;
            }

            int desiredCount = Rand.RangeInclusive(MinWeaponCount, MaxWeaponCount);
            int addCount = desiredCount - wheel.OccupiedUnlockedSlotCount;
            if (addCount <= 0)
            {
                return;
            }

            List<ThingStuffPair> candidates = BuildCandidates(pawn);
            if (candidates.Count == 0)
            {
                return;
            }

            HashSet<ThingDef> usedWeaponDefs = new HashSet<ThingDef>();
            for (int i = 0; i < wheel.MaxSlots; i++)
            {
                ThingWithComps existing = wheel.WeaponAt(i);
                if (existing != null)
                {
                    usedWeaponDefs.Add(existing.def);
                }
            }

            for (int i = 0; i < addCount; i++)
            {
                List<ThingStuffPair> unusedCandidates = candidates
                    .Where(pair => !usedWeaponDefs.Contains(pair.thing))
                    .ToList();
                List<ThingStuffPair> selectionPool = unusedCandidates.Count > 0 ? unusedCandidates : candidates;
                if (!selectionPool.TryRandomElementByWeight(SelectionWeight, out ThingStuffPair selected))
                {
                    break;
                }

                ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(selected.thing, selected.stuff);
                PawnGenerator.PostProcessGeneratedGear(weapon, pawn);
                ApplyWeaponStyleAndBiocoding(weapon, pawn, request);
                if (!wheel.TryAddGeneratedReserveWeapon(weapon))
                {
                    weapon.Destroy();
                    break;
                }

                usedWeaponDefs.Add(selected.thing);
            }
        }

        private static List<ThingStuffPair> BuildCandidates(Pawn pawn)
        {
            List<ThingStuffPair> candidates = new List<ThingStuffPair>();
            List<string> weaponTags = pawn.kindDef?.weaponTags;
            List<ThingStuffPair> allPairs = PawnWeaponGenerator.AllWeaponPairs;
            if (weaponTags.NullOrEmpty() || allPairs == null)
            {
                return candidates;
            }

            float budget = pawn.kindDef.weaponMoney.RandomInRange;
            for (int i = 0; i < allPairs.Count; i++)
            {
                ThingStuffPair pair = allPairs[i];
                ThingDef weaponDef = pair.thing;
                if (!weaponDef.IsRangedWeapon || pair.Price > budget
                    || !weaponTags.Any(tag => weaponDef.weaponTags.Contains(tag))
                    || (pawn.kindDef.weaponStuffOverride != null && pair.stuff != pawn.kindDef.weaponStuffOverride)
                    || (pair.stuff != null && !pair.stuff.stuffProps.allowedInStuffGeneration)
                    || (weaponDef.generateAllowChance < 1f
                        && !Rand.ChanceSeeded(weaponDef.generateAllowChance,
                            pawn.thingIDNumber ^ weaponDef.shortHash ^ 0x6D756769)))
                {
                    continue;
                }

                candidates.Add(pair);
            }
            return candidates;
        }

        private static float SelectionWeight(ThingStuffPair pair)
        {
            return pair.Commonality * pair.Price;
        }

        private static void ApplyWeaponStyleAndBiocoding(
            ThingWithComps weapon,
            Pawn pawn,
            PawnGenerationRequest request)
        {
            CompEquippable equippable = weapon.TryGetComp<CompEquippable>();
            if (equippable != null)
            {
                if (pawn.kindDef.weaponStyleDef != null)
                {
                    equippable.parent.StyleDef = pawn.kindDef.weaponStyleDef;
                }
                else if (pawn.Ideo != null)
                {
                    equippable.parent.StyleDef = pawn.Ideo.GetStyleFor(weapon.def);
                }
            }

            float biocodeChance = request.BiocodeWeaponChance > 0f
                ? request.BiocodeWeaponChance
                : pawn.kindDef.biocodeWeaponChance;
            if (Rand.Value < biocodeChance)
            {
                weapon.TryGetComp<CompBiocodable>()?.CodeFor(pawn);
            }
        }
    }
}
