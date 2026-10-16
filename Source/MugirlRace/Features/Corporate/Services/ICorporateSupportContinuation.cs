using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 必须在队伍开始推进前完成的持久动作；具体工作由实现持有，不改变 Lord 的资产或身份。
internal interface ICorporateSupportContinuation : IExposable
{
    bool IncludeFleeToil { get; }
    void Complete(Lord lord);
}
