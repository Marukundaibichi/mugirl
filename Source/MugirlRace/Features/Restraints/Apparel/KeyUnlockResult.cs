namespace Mugirl.Features.Restraints;

// 钥匙开锁一次的结果（RestraintApparel.UseKey）。
internal enum KeyUnlockResult
{
    // 锁数减一后仍未归零。
    StillLocked,

    // 锁数归零，束具已解除锁定并脱下。
    Released,

    // 锁数归零但脱下失败，已回滚到开锁前的锁定状态；不应消耗钥匙。
    DropFailed,
}
