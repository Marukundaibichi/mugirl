using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 身体资源采集 WorkGiver 基类，扫描可采集雪牛娘并创建对应 Job。
    public abstract class WorkGiver_GatherBodyResources : WorkGiver_Scanner
    {
        protected abstract JobDef JobDef { get; }

        protected abstract CompMooHasBodyResource GetComp(Pawn animal);

        // 只扫描本地图殖民者和囚犯中的雪牛娘，避免对全图所有 Pawn 做宽扫描。
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map?.mapPawns == null)
            {
                yield break;
            }

            List<Pawn> candidates = pawn.Map.mapPawns.FreeColonistsAndPrisonersSpawned;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn pawn2 = candidates[i];
                if (MooGirlIdentity.HasMooGirlBody(pawn2))
                {
                    yield return pawn2;
                }
            }
        }

        public override PathEndMode PathEndMode
        {
            get
            {
                return PathEndMode.Touch;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!CanDoGatherWork(pawn))
            {
                return false;
            }

            Pawn pawn2 = thing as Pawn;
            if (pawn2 == null || pawn2.RaceProps?.Humanlike != true || !MooGirlIdentity.HasMooGirlBody(pawn2))
            {
                return false;
            }

            CompMooHasBodyResource comp = GetComp(pawn2);
            if (comp == null || !comp.Active)
            {
                return false;
            }

            // 允许与征召中的雪牛娘互动（喝奶可互动但榨乳被原版 CanCasuallyInteractNow 拦截）
            if (!PawnUtility.CanCasuallyInteractNow(pawn2, false) && !pawn2.Drafted)
            {
                return false;
            }

            // forced 强制挤奶：任意饱满度 > 0 即可
            // auto 自动挤奶：需要达到阈值
            if (forced)
            {
                if (comp.Fullness <= 0f)
                    return false;
            }
            else
            {
                if (comp.Fullness < comp.MilkThreshold)
                    return false;
            }

            LocalTargetInfo localTargetInfo = pawn2;
            if (ReservationUtility.CanReserve(pawn, localTargetInfo, 1, -1, null, forced))
            {
                return true;
            }

            return false;
        }

        protected virtual bool CanDoGatherWork(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid && pawn.skills != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job job = JobMaker.MakeJob(JobDef, t);
            job.playerForced = forced;
            return job;
        }
    }
}
