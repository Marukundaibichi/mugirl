namespace Mugirl.GameValidation.Core;

/// <summary>套件运行前必须满足的条件；不满足时整套记为 skip，不算失败。</summary>
[Flags]
internal enum SuiteNeeds
{
    None = 0,

    /// <summary>HAR.MugirlRace 已加载（-Plumbing 运行不加载）。</summary>
    MugirlMod = 1,

    /// <summary>宿主渲染画面帧并运行界面：-NoWindow 满足，-Headless 不满足（E48）。</summary>
    RenderedFrames = 2,
}

/// <summary>套件中的一个步骤：在游戏主循环里分帧执行的迭代器。</summary>
/// <param name="Name">报告中使用的步骤名。</param>
/// <param name="Body">步骤本体；产出的每个 <see cref="Wait"/> 让出到下一帧。</param>
/// <param name="TimeoutSeconds">步骤的实时上限，超过即放弃该步骤，避免一处卡住拖垮整次运行。</param>
internal sealed record ValidationStep(string Name, Func<ValidationContext, IEnumerable<Wait>> Body, double TimeoutSeconds = 120);

/// <summary>一组有序步骤。</summary>
/// <remarks>
/// 新增功能套件：写一个返回 <see cref="ValidationSuite"/> 的静态 Create()，只用原版 API 的放在 Suites/ 并加入 <see cref="SuiteCatalog.All"/>，
/// 引用被测 Mod 类型的放在 Suites/Mod/ 并在 ModSuiteRegistration.cs 中登记（见 <see cref="SuiteCatalog"/>）。
/// 还没写的功能套件以 <see cref="Planned"/> 登记：选中时输出一条 skip 并说明计划覆盖的内容，报告因此能看出覆盖缺口。
/// 每个套件应能单独运行（-Suite 名称），需要的殖民者等前置条件由 <see cref="GameFixtures"/> 按需补齐。
/// </remarks>
internal sealed class ValidationSuite
{
    public ValidationSuite(string name, string description, SuiteNeeds needs, IReadOnlyList<ValidationStep> steps, bool inDefaultRun = true)
    {
        Name = name;
        Description = description;
        Needs = needs;
        Steps = steps;
        InDefaultRun = inDefaultRun;
    }

    public string Name { get; }

    public string Description { get; }

    public SuiteNeeds Needs { get; }

    public IReadOnlyList<ValidationStep> Steps { get; }

    /// <summary>是否属于 "default" 选择。</summary>
    public bool InDefaultRun { get; }

    /// <summary>步骤结束时评判日志的策略。</summary>
    public SuiteLogPolicy LogPolicy { get; init; } = SuiteLogPolicy.ModMentions;

    /// <summary>非 null 时整个套件不运行，只输出一条 skip 写明原因（尚未实现，或本次构建没有编译它）。</summary>
    public string? SkipReason { get; private init; }

    /// <summary>尚未实现的功能套件：选中时报告计划覆盖的内容，报告因此能看出覆盖缺口。</summary>
    public static ValidationSuite Planned(string name, string coverage)
        => new(name, coverage, SuiteNeeds.MugirlMod, [], inDefaultRun: false) { SkipReason = "not implemented yet; planned coverage: " + coverage };

    /// <summary>
    /// 是否属于 "all" 选择。框架自检、夹具制作与存档升级套件需要专门的启动方式（只加载 Harmony、临时旧构建、从主菜单读夹具），
    /// 只能按名字选择。
    /// </summary>
    public bool InAllRun { get; init; } = true;

    /// <summary>本次构建中不可用的套件（例如不引用 MugirlRace 的 -Plumbing 构建中，引用 Mod 类型的套件）。</summary>
    public static ValidationSuite Unavailable(string name, string reason, bool inDefaultRun = true)
        => new(name, reason, SuiteNeeds.MugirlMod, [], inDefaultRun) { SkipReason = reason, InAllRun = inDefaultRun };
}
