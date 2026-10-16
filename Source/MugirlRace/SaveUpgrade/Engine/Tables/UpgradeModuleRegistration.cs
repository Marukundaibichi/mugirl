namespace Mugirl.SaveUpgrade;

/// <summary>一个已发现的升级模块：名称、全局顺序与实现。</summary>
internal sealed record UpgradeModuleRegistration(string Name, int Order, ISaveUpgradeModule Module);
