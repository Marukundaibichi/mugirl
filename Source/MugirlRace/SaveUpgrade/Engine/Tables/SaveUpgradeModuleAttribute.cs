namespace Mugirl.SaveUpgrade;

/// <summary>标记一个 <see cref="ISaveUpgradeModule"/> 实现，给出模块名与全局顺序。</summary>
/// <param name="name">模块名（PascalCase，与 <c>SaveUpgrade/Modules/</c> 下的文件夹名相同），写入升级清单的 module 字段。</param>
/// <param name="order">全局顺序，唯一。模块的步骤按 (顺序, 登记顺序) 运行，跨模块依赖用顺序表达。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class SaveUpgradeModuleAttribute(string name, int order) : Attribute
{
    internal string Name { get; } = name;

    internal int Order { get; } = order;
}
