using Mugirl.Features.Milk;

namespace Mugirl.Features.Incidents;

// 奖励生成顺序保持食品在前、Pawn 在后，再按原位置空投与发送通知。
internal static class FusionInvestmentRewards
{
    private const int NoMapRetryTicks = GenDate.TicksPerDay;

    internal static void DeliverResult(GameComponent_Storyline state)
    {
        int tier = FusionInvestmentMath.TierForAmount(state.Investment.amount);

        if (!GameUtility.TryResolvePlayerEventMap(out Map? map))
        {
            state.Investment.resultRemaining = NoMapRetryTicks;
            return;
        }

        List<Thing> rewards = new List<Thing>();
        AddMilkFoodRewards(rewards, FusionInvestmentMath.MilkFoodRewardCountForTier(tier));
        AddWildMugirlRewards(rewards, tier);

        IntVec3 dropCell = DropCellFinder.TradeDropSpot(map);
        DropPodUtility.DropThingsNear(dropCell, map, rewards, 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: false);

        GameUtility.TryReceiveLetter(
            "Mugirl.FusionInvestment.ResultLetterLabel".Translate(),
            "Mugirl.FusionInvestment.ResultLetterText".Translate(),
            LetterDefOf.PositiveEvent,
            new TargetInfo(dropCell, map));

        state.Investment.pending = false;
        state.Investment.resultRemaining = 0;
    }

    private static void AddMilkFoodRewards(List<Thing> rewards, int totalCount)
    {
        List<ThingDef> foodDefs =
        [
            MilkDefOf.Mugirl_MilkPudding,
            MilkDefOf.Mugirl_Cheese,
            MilkDefOf.Mugirl_AgedCheese,
            MilkDefOf.Mugirl_BaotaSugar,
            MilkDefOf.Mugirl_MilkCandy,
            MilkDefOf.Mugirl_CowCake,
            MilkDefOf.Mugirl_MilkPowder,
            MilkDefOf.Mugirl_MilkTablet,
            MilkDefOf.Mugirl_MilkCreamApple,
        ];

        int remaining = totalCount;
        while (remaining > 0 && foodDefs.Count > 0)
        {
            ThingDef def = foodDefs.RandomElement();

            Thing? thing = ThingMaker.MakeThing(def);
            if (thing == null)
            {
                foodDefs.Remove(def);
                continue;
            }

            thing.stackCount = Math.Min(remaining, Math.Max(1, def.stackLimit));
            remaining -= thing.stackCount;
            rewards.Add(thing);
        }
    }

    private static void AddWildMugirlRewards(List<Thing> rewards, int count)
    {
        for (int i = 0; i < count; i++)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                IncidentsDefOf.Mugirl_WildMugirl,
                PlayerFactionUtility.PlayerFaction,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                forceRecruitable: true,
                dontGiveWeapon: true,
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult);
            request.ForceNoIdeoGear = true;

            Pawn? pawn = PawnGenerator.GeneratePawn(request);
            if (pawn != null)
            {
                WildMugirlUtility.CleanupAfterJoiningPlayer(pawn);
                rewards.Add(pawn);
            }
        }
    }
}
