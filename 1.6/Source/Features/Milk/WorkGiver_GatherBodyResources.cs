using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 抽象工作给予者类：用于收集生物资源
    public abstract class WorkGiver_GatherBodyResources : WorkGiver_Scanner
    {
        // 抽象属性：定义具体的工作类型（由子类实现）
        protected abstract JobDef JobDef { get; }

        // 抽象方法：获取指定Pawn的生物资源组件（由子类实现）
        protected abstract CompMooHasBodyResource GetComp(Pawn animal);

        // 获取全局潜在工作目标（所有符合条件的殖民地成员和囚犯）
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            // 遍历当前地图上的所有殖民地成员和囚犯
            foreach (Pawn pawn2 in pawn.Map.mapPawns.FreeColonistsAndPrisonersSpawned)
            {
                // 筛选出具有MooGirlBody身体类型的Pawn
                if (pawn2.RaceProps.body == MooGirl_DefOf.MooGirlBody)
                {
                    yield return pawn2; // 返回符合条件的Pawn
                }
            }
            yield break; // 结束迭代
        }

        // 定义路径结束模式为"接触"（表示工作需要接触到目标）
        public override PathEndMode PathEndMode
        {
            get
            {
                return PathEndMode.Touch;
            }
        }

        // 检查指定事物是否可执行工作
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!CanDoGatherWork(pawn))
            {
                return false;
            }

            // 尝试将事物转换为Pawn类型
            Pawn pawn2 = thing as Pawn;
            // 如果转换失败或目标不是类人生物，则返回false
            if (pawn2 == null || !pawn2.RaceProps.Humanlike)
            {
                return false;
            }

            // 获取目标的生物资源组件
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

            // 检查Pawn是否可以预订目标
            if (pawn2 == pawn || pawn2 != pawn)
            {
                LocalTargetInfo localTargetInfo = pawn2;
                if (ReservationUtility.CanReserve(pawn, localTargetInfo, 1, -1, null, forced))
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual bool CanDoGatherWork(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid && pawn.skills != null;
        }

        // 创建具体的工作任务
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // 使用子类定义的JobDef创建新工作，目标为指定事物
            Job job = JobMaker.MakeJob(JobDef, t);
            job.playerForced = forced;
            return job;
        }
    }
}
