using Verse.AI;

namespace Mugirl.Features.Mounting;

// 以载具身份调用原版 AttackTargetFinder：位置、敌对关系取载具，攻击记录取炮塔状态。
internal sealed class MountedAttackTargetSearcher(CompMount comp, Verb verb) : IAttackTargetSearcher
{
    public Thing? Thing => comp.MooPawn;

    public Verb CurrentEffectiveVerb => verb;

    public LocalTargetInfo LastAttackedTarget => comp.Turret.lastAttackedTarget;

    public int LastAttackTargetTick => comp.Turret.lastAttackTargetTick;
}
