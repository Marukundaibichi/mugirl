namespace Mugirl.Features.Milk;

/// <summary>可持久的结算续接动作，在组件下次有效的资源更新开始时执行一次。</summary>
internal interface IResourceAccrualContinuation : IExposable
{
    void Apply(CompMilkProduction source, int currentTick, int updateInterval);
}
