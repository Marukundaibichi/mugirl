using System.Linq;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 委托板上的一份合同（含核聚变研究支线）；所有字段以扁平 Scribe 键保存。
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
    public ThingDef? product;
    public ThingDef? stuff;
    public RecipeDef? recipe;
    public QuestScriptDef? nativeRoot;
    public Faction? enemy;
    public Site? site;
    public Quest? quest;
    public Pawn? investor;
    // 失效引用保留为 null：空引用不能证明死亡，由任务校验判定失败。
    public List<Pawn?> targets = new List<Pawn?>();
    public List<ThingDefCountClass> ingredients = new List<ThingDefCountClass>();
    public List<Thing> pendingGoods = new List<Thing>();

    public bool IsSide => kind == CorporateMissionKind.Fusion;

    public bool IsFinished => state == CorporateMissionState.Completed || state == CorporateMissionState.Failed
        || state == CorporateMissionState.Expired || state == CorporateMissionState.Abandoned;

    public string Title => KindKey(kind).Translate().ToString()
        + (product != null ? " · " + product.LabelCap.ToString() : "")
        + (kind == CorporateMissionKind.Investment && highRisk ? " · " + "Mugirl.CQ.HighRisk".Translate().ToString() : "");

    public string Status => StateKey(state).Translate();

    public int SalesTarget => salesGoal;

    private static string KindKey(CorporateMissionKind value) => value switch
    {
        CorporateMissionKind.Purge => "Mugirl.CQ.Kind.Purge",
        CorporateMissionKind.Investment => "Mugirl.CQ.Kind.Investment",
        CorporateMissionKind.Distribution => "Mugirl.CQ.Kind.Distribution",
        CorporateMissionKind.Native => "Mugirl.CQ.Kind.Native",
        CorporateMissionKind.Fusion => "Mugirl.CQ.Kind.Fusion",
        _ => "Mugirl.CQ.Kind.Processing",
    };

    private static string StateKey(CorporateMissionState value) => value switch
    {
        CorporateMissionState.Active => "Mugirl.CQ.State.Active",
        CorporateMissionState.Ready => "Mugirl.CQ.State.Ready",
        CorporateMissionState.Completed => "Mugirl.CQ.State.Completed",
        CorporateMissionState.Failed => "Mugirl.CQ.State.Failed",
        CorporateMissionState.Expired => "Mugirl.CQ.State.Expired",
        CorporateMissionState.Abandoned => "Mugirl.CQ.State.Abandoned",
        _ => "Mugirl.CQ.State.Available",
    };

    public string Description
    {
        get
        {
            switch (kind)
            {
                case CorporateMissionKind.Processing:
                    return "Mugirl.CQ.ProcessingDesc".Translate(recipe?.LabelCap ?? "?", batches,
                        product?.LabelCap ?? "?", count, cost, reward,
                        string.Join(", ", ingredients.Select(x => x.count + " × " + x.thingDef.LabelCap)),
                        stuff?.LabelCap ?? "Mugirl.CQ.AnyMaterial".Translate(), (long)cost + reward);
                case CorporateMissionKind.Purge:
                    return "Mugirl.CQ.PurgeDesc".Translate(enemy?.Name ?? "?", UnityMathf.RoundToInt(threatPoints), reward);
                case CorporateMissionKind.Investment:
                    return highRisk ? "Mugirl.CQ.InvestmentHighDesc".Translate(cost) : "Mugirl.CQ.InvestmentDesc".Translate(cost);
                case CorporateMissionKind.Distribution:
                    return "Mugirl.CQ.DistributionDesc".Translate(count, product?.LabelCap ?? "?", cost, SalesTarget);
                case CorporateMissionKind.Native:
                    return "Mugirl.CQ.NativeDesc".Translate(nativeRoot?.label.NullOrEmpty() == false ? nativeRoot.label : nativeRoot?.defName ?? "?");
                default:
                    return "Mugirl.CQ.FusionDesc".Translate();
            }
        }
    }

    internal void RecordDeadTargets()
    {
        // 击杀进度取代死亡 Pawn 的引用；空引用不能证明死亡，仍交给任务校验处理。
        if ((kind != CorporateMissionKind.Purge && !IsSide) || !spawned
            || (state != CorporateMissionState.Active && state != CorporateMissionState.Ready))
        {
            return;
        }

        progress += targets.RemoveAll(p => p != null && p.Dead);
    }

    public void ExposeData()
    {
        // 必须先于 progress 和 targets 写入。原版默认引用保存会把死亡 Pawn 写成 null；
        // 保存前补齐低频检查的空窗，也不依赖尸体是否还存在。此处不结算或发送任务通知。
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            RecordDeadTargets();
        }

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
            targets ??= new List<Pawn?>();
            ingredients ??= new List<ThingDefCountClass>();
            pendingGoods ??= new List<Thing>();
            pendingGoods.RemoveAll(t => t == null || t.Destroyed);
        }
    }
}
