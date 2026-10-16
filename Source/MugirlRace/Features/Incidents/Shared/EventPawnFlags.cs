namespace Mugirl.Features.Incidents;

/// <summary>事件 Pawn 身上的事件标记，一次扫描 questTags 得到。</summary>
[Flags]
internal enum EventPawnFlags
{
    None = 0,
    Migration = 1,
    RunawayFarm = 2,
    FusionInvestor = 4,

    // 迁徙与逃跑牧场的雪牛娘受伤或被驯服失败时都不进入原版的狂暴、逃跑反应。
    Passive = Migration | RunawayFarm,
}
