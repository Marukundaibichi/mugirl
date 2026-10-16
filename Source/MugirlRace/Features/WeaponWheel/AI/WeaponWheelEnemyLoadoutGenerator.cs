namespace Mugirl.Features.WeaponWheel;

// 敌对的雪牛娘生成时往轮盘里补 1–2 把远程储备武器，使其能使用火力满载。
// 候选取自 PawnKind 的武器标签与预算，Rand 的调用顺序与 1.6 一致（同种子生成结果不变）。
internal static class WeaponWheelEnemyLoadoutGenerator
{
    private const int MinWeaponCount = 2;
    private const int MaxWeaponCount = 3;
    // 与 thingIDNumber、武器 shortHash 混合的固定盐，让 generateAllowChance 对同一 Pawn 稳定。
    private const int AllowChanceSalt = 0x6D756769;

    internal static void TryGenerateFor(Pawn? pawn, PawnGenerationRequest request)
    {
        if (!MugirlIdentity.IsMugirlPawn(pawn))
        {
            return;
        }

        Faction? playerFaction = Faction.OfPlayerSilentFail;
        CompWeaponWheel? wheel = pawn.TryGetComp<CompWeaponWheel>();
        ThingWithComps? primary = pawn.equipment?.Primary;
        if (wheel == null
            || playerFaction == null || pawn.Faction == null || !pawn.Faction.HostileTo(playerFaction)
            || pawn.WorkTagIsDisabled(WorkTags.Violent) || pawn.WorkTagIsDisabled(WorkTags.Shooting)
            || primary == null || !primary.def.IsRangedWeapon)
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

        HashSet<ThingDef> usedWeaponDefs = [];
        for (int i = 0; i < wheel.MaxSlots; i++)
        {
            ThingWithComps? existing = wheel.WeaponAt(i);
            if (existing != null)
            {
                usedWeaponDefs.Add(existing.def);
            }
        }

        List<ThingStuffPair> unusedCandidates = new(candidates.Count);
        for (int i = 0; i < addCount; i++)
        {
            // 优先选还没装备过的武器种类，全部用过时再允许重复。
            unusedCandidates.Clear();
            for (int j = 0; j < candidates.Count; j++)
            {
                if (!usedWeaponDefs.Contains(candidates[j].thing))
                {
                    unusedCandidates.Add(candidates[j]);
                }
            }
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
        List<ThingStuffPair> candidates = [];
        PawnKindDef? kindDef = pawn.kindDef;
        List<string>? weaponTags = kindDef?.weaponTags;
        List<ThingStuffPair>? allPairs = PawnWeaponGenerator.AllWeaponPairs;
        if (kindDef == null || weaponTags == null || weaponTags.Count == 0 || allPairs == null)
        {
            return candidates;
        }

        float budget = kindDef.weaponMoney.RandomInRange;
        for (int i = 0; i < allPairs.Count; i++)
        {
            ThingStuffPair pair = allPairs[i];
            ThingDef weaponDef = pair.thing;
            if (!weaponDef.IsRangedWeapon || pair.Price > budget
                || !SharesWeaponTag(weaponTags, weaponDef.weaponTags)
                || (kindDef.weaponStuffOverride != null && pair.stuff != kindDef.weaponStuffOverride)
                || (pair.stuff != null && !pair.stuff.stuffProps.allowedInStuffGeneration)
                || (weaponDef.generateAllowChance < 1f
                    && !Rand.ChanceSeeded(weaponDef.generateAllowChance, pawn.thingIDNumber ^ weaponDef.shortHash ^ AllowChanceSalt)))
            {
                continue;
            }

            candidates.Add(pair);
        }
        return candidates;
    }

    private static bool SharesWeaponTag(List<string> kindTags, List<string>? weaponTags)
    {
        if (weaponTags == null)
        {
            return false;
        }
        for (int i = 0; i < kindTags.Count; i++)
        {
            if (weaponTags.Contains(kindTags[i]))
            {
                return true;
            }
        }
        return false;
    }

    private static float SelectionWeight(ThingStuffPair pair)
    {
        return pair.Commonality * pair.Price;
    }

    private static void ApplyWeaponStyleAndBiocoding(ThingWithComps weapon, Pawn pawn, PawnGenerationRequest request)
    {
        CompEquippable? equippable = weapon.TryGetComp<CompEquippable>();
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
