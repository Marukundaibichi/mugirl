using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public enum CorporateMissionKind { Processing, Purge, Investment, Distribution, Native, Fusion }
    public enum CorporateMissionState { Available, Active, Ready, Completed, Failed, Expired, Abandoned }
    public enum CorporateResearchChoice { Undecided, Execute, Protect }

    public sealed class CorporateMission : IExposable
    {
        public int id;
        public CorporateMissionKind kind;
        public CorporateMissionState state;
        public int offeredTick;
        public int acceptByTick;
        public int acceptedTick;
        public int deadline;
        public int duration = 7 * 60000;
        public int cost;
        public int reward;
        public int goodwill;
        public int count;
        public int progress;
        public int salesGoal;
        public int batches;
        public bool highRisk;
        public bool spawned;
        public bool dialogueShown;
        public bool originalInvestor;
        public bool gratitudeRevoked;
        public CorporateResearchChoice choice;
        public float threatPoints;
        public PlanetTile tile = PlanetTile.Invalid;
        public ThingDef product;
        public ThingDef stuff;
        public RecipeDef recipe;
        public QuestScriptDef nativeRoot;
        public Faction enemy;
        public Site site;
        public Quest quest;
        public Pawn investor;
        public List<Pawn> targets = new List<Pawn>();
        public List<ThingDefCountClass> ingredients = new List<ThingDefCountClass>();
        public List<Thing> pendingGoods = new List<Thing>();
        public bool IsSide => kind == CorporateMissionKind.Fusion;
        public bool IsFinished => state == CorporateMissionState.Completed || state == CorporateMissionState.Failed
            || state == CorporateMissionState.Expired || state == CorporateMissionState.Abandoned;
        public string Title => KindKey(kind).Translate().ToString()
            + (product != null ? " · " + product.LabelCap.ToString() : "")
            + (kind == CorporateMissionKind.Investment && highRisk ? " · " + "Mugirl.CQ.HighRisk".Translate().ToString() : "");
        public string Status => StateKey(state).Translate();

        private static string KindKey(CorporateMissionKind value)
        {
            switch (value)
            {
                case CorporateMissionKind.Purge: return "Mugirl.CQ.Kind.Purge";
                case CorporateMissionKind.Investment: return "Mugirl.CQ.Kind.Investment";
                case CorporateMissionKind.Distribution: return "Mugirl.CQ.Kind.Distribution";
                case CorporateMissionKind.Native: return "Mugirl.CQ.Kind.Native";
                case CorporateMissionKind.Fusion: return "Mugirl.CQ.Kind.Fusion";
                default: return "Mugirl.CQ.Kind.Processing";
            }
        }

        private static string StateKey(CorporateMissionState value)
        {
            switch (value)
            {
                case CorporateMissionState.Active: return "Mugirl.CQ.State.Active";
                case CorporateMissionState.Ready: return "Mugirl.CQ.State.Ready";
                case CorporateMissionState.Completed: return "Mugirl.CQ.State.Completed";
                case CorporateMissionState.Failed: return "Mugirl.CQ.State.Failed";
                case CorporateMissionState.Expired: return "Mugirl.CQ.State.Expired";
                case CorporateMissionState.Abandoned: return "Mugirl.CQ.State.Abandoned";
                default: return "Mugirl.CQ.State.Available";
            }
        }
        // 旧合同未记录目标时沿用首版 80%，不受后来平衡配置变化影响。
        public int SalesTarget => salesGoal > 0 ? salesGoal : Mathf.Max(1, Mathf.CeilToInt(count * 0.8f));

        public string Description
        {
            get
            {
                switch (kind)
                {
                    case CorporateMissionKind.Processing:
                        return "Mugirl.CQ.ProcessingDesc".Translate(recipe?.LabelCap ?? "?", batches,
                            product?.LabelCap ?? "?", count, cost, reward,
                            string.Join(", ", ingredients.Select(x => x.count + " × " + x.thingDef.LabelCap).ToArray()),
                            stuff?.LabelCap ?? "Mugirl.CQ.AnyMaterial".Translate());
                    case CorporateMissionKind.Purge:
                        return "Mugirl.CQ.PurgeDesc".Translate(enemy?.Name ?? "?", Mathf.RoundToInt(threatPoints), reward);
                    case CorporateMissionKind.Investment:
                        return (highRisk ? "Mugirl.CQ.InvestmentHighDesc" : "Mugirl.CQ.InvestmentDesc").Translate(cost);
                    case CorporateMissionKind.Distribution:
                        return "Mugirl.CQ.DistributionDesc".Translate(count, product?.LabelCap ?? "?", cost, SalesTarget);
                    case CorporateMissionKind.Native:
                        return "Mugirl.CQ.NativeDesc".Translate(nativeRoot?.label.NullOrEmpty() == false ? nativeRoot.label : nativeRoot?.defName ?? "?");
                    default:
                        return "Mugirl.CQ.FusionDesc".Translate();
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref offeredTick, "offeredTick");
            Scribe_Values.Look(ref acceptByTick, "acceptByTick");
            Scribe_Values.Look(ref acceptedTick, "acceptedTick");
            Scribe_Values.Look(ref deadline, "deadline");
            Scribe_Values.Look(ref duration, "duration", 7 * 60000);
            Scribe_Values.Look(ref cost, "cost");
            Scribe_Values.Look(ref reward, "reward");
            Scribe_Values.Look(ref goodwill, "goodwill");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref salesGoal, "salesGoal", 0);
            Scribe_Values.Look(ref batches, "batches");
            Scribe_Values.Look(ref highRisk, "highRisk");
            Scribe_Values.Look(ref spawned, "spawned");
            Scribe_Values.Look(ref dialogueShown, "dialogueShown");
            Scribe_Values.Look(ref originalInvestor, "originalInvestor");
            Scribe_Values.Look(ref gratitudeRevoked, "gratitudeRevoked");
            Scribe_Values.Look(ref choice, "choice");
            Scribe_Values.Look(ref threatPoints, "threatPoints");
            Scribe_Values.Look(ref tile, "tile", PlanetTile.Invalid);
            Scribe_Defs.Look(ref product, "product");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_Defs.Look(ref recipe, "recipe");
            Scribe_Defs.Look(ref nativeRoot, "nativeRoot");
            Scribe_References.Look(ref enemy, "enemy");
            Scribe_References.Look(ref site, "site");
            Scribe_References.Look(ref quest, "quest");
            Scribe_References.Look(ref investor, "investor");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref ingredients, "ingredients", LookMode.Deep);
            Scribe_Collections.Look(ref pendingGoods, "pendingGoods", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targets = targets ?? new List<Pawn>();
                ingredients = ingredients ?? new List<ThingDefCountClass>();
                pendingGoods = pendingGoods ?? new List<Thing>();
                pendingGoods.RemoveAll(t => t == null || t.Destroyed);
                if (kind == CorporateMissionKind.Distribution && salesGoal <= 0) salesGoal = SalesTarget;
            }
        }
    }

    [DefOf]
    public static class CorporateQuestDefOf
    {
        public static CorporateQuestConfigDef Mugirl_CorporateQuestConfig;
        public static SitePartDef Mugirl_CorporatePurgeSite;
        public static SitePartDef Mugirl_CorporateResearchSite;
        public static QuestScriptDef Mugirl_CorporateMission;
        static CorporateQuestDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CorporateQuestDefOf)); }
    }

    public sealed class CorporateQuestConfigDef : Def
    {
        public int weeklyOffers = 6;
        public int activeLimit = 3;
        public int processingDays = 7;
        public int batchMin = 5;
        public int batchMax = 20;
        public float processingFeeMin = 0.25f;
        public float processingFeeMax = 0.4f;
        public int processingMinimumFee = 150;
        public int processingMaxDeposit = 20000;
        public int processingFailureGoodwill = -5;
        public int purgeDays = 10;
        public int purgeMinReward = 1000;
        public int purgeMaxReward = 5000;
        public int purgeGoodwill = 5;
        public int distributionDays = 10;
        public float wholesaleFactor = 0.45f;
        public float salesTarget = 0.8f;
        public int distributionGoodwill = 3;
        public int investmentDays = 7;
        public int investmentMin = 1000;
        public int investmentMax = 3000;
        public int highInvestmentDays = 10;
        public int highInvestmentMin = 3000;
        public int highInvestmentMax = 8000;
        public int fusionInviteMinDays = 1;
        public int fusionInviteMaxDays = 3;
        public int fusionOfferDays = 15;
        public int fusionActionDays = 10;
        public int fusionReward = 4000;
        public int fusionGoodwill = 10;
        public int fusionRefusalGoodwill = -60;
    }

    internal static class CorporateQuestDefs
    {
        // StaticCacheLifecycle: immutable, process-lifetime Def references only; never stores game objects.
        internal static readonly ThingDef[] DistributionProducts = new[] { "ComponentIndustrial", "MedicineIndustrial", "MealSurvivalPack", "Beer" }
            .Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(x => x != null).ToArray();
        internal static readonly QuestScriptDef[] NativeQuests = new[] { "TradeRequest", "OpportunitySite_ItemStash" }
            .Select(DefDatabase<QuestScriptDef>.GetNamedSilentFail).Where(x => x != null).ToArray();
        internal static readonly PawnKindDef Guard = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mercenary_Gunner") ?? PawnKindDefOf.Villager;
        internal static readonly ThingDef ResearchBench = DefDatabase<ThingDef>.GetNamedSilentFail("SimpleResearchBench");
        internal static readonly ThingDef Table = DefDatabase<ThingDef>.GetNamedSilentFail("Table1x2c");
        internal static readonly ThingDef Bed = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
    }
}
