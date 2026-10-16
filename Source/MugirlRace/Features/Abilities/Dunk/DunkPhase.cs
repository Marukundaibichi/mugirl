namespace Mugirl.Features.Abilities;

// 阶段名随存档保存；Shattering 只完成已经结算撞击后的碎裂演出。
internal enum DunkPhase : byte
{
    Dribbling,
    Spin,
    Flight,
    Slam,
    Finished,
    Shattering
}
