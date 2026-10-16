using Verse.AI;

namespace Mugirl.Features.Milk;

// 自动挤奶扫描器：先拒绝无产量或由设备接管的目标，再检查工作、互动和预约。
public sealed class WorkGiver_GatherMilk : WorkGiver_Scanner
{
    public override PathEndMode PathEndMode => PathEndMode.Touch;

    private static JobDef JobDef => MilkDefOf.Mugirl_GatherMilk;

    // 只扫描本地图殖民者和囚犯中的雪牛娘，避免对全图所有 Pawn 做宽扫描。
    public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
    {
        List<Pawn>? candidates = pawn?.Map?.mapPawns?.FreeColonistsAndPrisonersSpawned;
        if (candidates == null)
        {
            yield break;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Pawn candidate = candidates[i];
            if (MugirlIdentity.HasMugirlBody(candidate))
            {
                yield return candidate;
            }
        }
    }

    public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        CompMilkProduction? milkComp = (thing as Pawn)?.TryGetComp<CompMilkProduction>();

        // 先用奶量和激活条件拒绝候选，避免为不能挤奶的目标扫描穿戴设备。
        // forced 仍允许任意正奶量，自动任务仍使用玩家设定的阈值。
        if (milkComp == null || (forced ? milkComp.Fullness <= 0f : milkComp.Fullness < milkComp.MilkThreshold)
            || !milkComp.Active || milkComp.IsManagedByMilkingDevice)
        {
            return false;
        }

        if (!CanDoGatherWork(pawn))
        {
            return false;
        }

        if (thing is not Pawn target || target.RaceProps?.Humanlike != true || !MugirlIdentity.HasMugirlBody(target))
        {
            return false;
        }

        CompMilkProduction? comp = GetComp(target);
        if (comp == null || !comp.Active)
        {
            return false;
        }

        // 允许与征召中的雪牛娘互动（喝奶可互动但榨乳被原版 CanCasuallyInteractNow 拦截）。
        if (!PawnUtility.CanCasuallyInteractNow(target, false) && !target.Drafted)
        {
            return false;
        }

        // forced 强制挤奶：任意饱满度 > 0 即可；自动挤奶需要达到阈值。
        if (forced ? comp.Fullness <= 0f : comp.Fullness < comp.MilkThreshold)
        {
            return false;
        }

        return pawn.CanReserve(target, 1, -1, null, forced);
    }

    public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
    {
        Job job = JobMaker.MakeJob(JobDef, t);
        job.playerForced = forced;
        return job;
    }

    private static CompMilkProduction? GetComp(Pawn animal) => animal.TryGetComp<CompMilkProduction>();

    private static bool CanDoGatherWork(Pawn? pawn)
    {
        return pawn?.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid && pawn.skills != null;
    }
}
