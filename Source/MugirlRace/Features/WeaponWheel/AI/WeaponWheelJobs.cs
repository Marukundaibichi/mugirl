using Verse.AI;

namespace Mugirl.Features.WeaponWheel;

// 装填轮盘的 Job 约定：目标槽位借用 Job.count 传递（槽位 + 1，使默认值 -1 不会被误读为 0 号槽）。
// Job.count 保存目标槽位加一；创建任务与执行任务必须使用同一编码。
internal static class WeaponWheelJobs
{
    internal static Job MakeLoadJob(ThingWithComps weapon, int slotIndex)
    {
        Job job = JobMaker.MakeJob(WeaponWheelDefOf.Mugirl_LoadWeaponWheel, weapon);
        job.count = slotIndex + 1;
        return job;
    }

    internal static int SlotIndexOf(Job job) => job.count - 1;
}
