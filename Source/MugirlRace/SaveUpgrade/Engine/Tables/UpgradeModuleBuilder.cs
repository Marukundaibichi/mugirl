namespace Mugirl.SaveUpgrade;

/// <summary>
/// 模块登记升级表格的接口。每一行自动带上模块名；全部行在 <see cref="UpgradeTableBuilder.Build"/> 时统一校验。
/// </summary>
/// <remarks>
/// <para>
/// 旧名称只出现在 <c>SaveUpgrade/</c> 中。类型改名用 <c>typeof(新类型)</c>，重命名工具会随代码一起更新；
/// 旧名称是写入存档的字符串，永远不改。
/// </para>
/// <para>
/// 每一行带有引入它的存档结构版本（<see cref="Schema"/>，默认 1，即从没有版本戳的旧档升级）。以后的结构变更用
/// <see cref="ForSchema"/> 登记新版本的行；存档只应用版本高于其来源版本的行。改名链在登记时合并：
/// 版本 1 的 A→B 与版本 2 的 B→C 登记为版本 1 的 A→C 与版本 2 的 B→C。
/// </para>
/// </remarks>
internal sealed class UpgradeModuleBuilder
{
    private readonly UpgradeTableBuilder owner;

    internal UpgradeModuleBuilder(UpgradeTableBuilder owner, string name, int order, int schema = 1)
    {
        this.owner = owner;
        Name = name;
        Order = order;
        Schema = schema;
    }

    internal string Name { get; }

    internal int Order { get; }

    /// <summary>通过本对象登记的行所属的存档结构版本。</summary>
    internal int Schema { get; }

    /// <summary>登记存档结构版本 <paramref name="schema"/> 引入的行（同一模块、同一顺序）。</summary>
    internal UpgradeModuleBuilder ForSchema(int schema) => new(owner, Name, Order, schema);

    // ---- 类型 ------------------------------------------------------------------------------------------------------------

    /// <summary>旧完整类型名改为 <paramref name="newType"/>（<c>Class</c> 属性、泛型参数与类型值上下文）。</summary>
    internal void RenameType(string oldName, Type newType, string reason)
    {
        ArgumentNullException.ThrowIfNull(newType);
        owner.Types.Add(new TypeRow(Required(oldName), newType, null, Name, Required(reason), Schema));
    }

    /// <summary>旧类型已删除，带该 <c>Class</c> 的元素按 <paramref name="policy"/> 处理。</summary>
    internal void DropType(string oldName, TypeDropPolicy policy, string reason) =>
        owner.Types.Add(new TypeRow(Required(oldName), null, policy, Name, Required(reason), Schema));

    // ---- Def -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="root"/> 数据库中的旧 defName 改名。存档中由 Scribe_Defs 读取的引用由 <c>BackCompatibleDefName</c> 后缀
    /// 改名；DOM 只改写加载 ID（物品、账单）与登记的 Def 值上下文。
    /// </summary>
    internal void RenameDef(string root, string oldName, string newName, string reason, string? legacyRoot = null) =>
        owner.Defs.Add(new DefRow(Required(root), Required(oldName), Required(newName), null, Name, Required(reason), Schema, LegacyRoot: legacyRoot));

    /// <summary><paramref name="root"/> 数据库中的旧 Def 已删除，依赖数据按 <paramref name="policy"/> 处理。</summary>
    internal void DropDef(string root, string oldName, DefDropPolicy policy, string reason, string? legacyRoot = null) =>
        owner.Defs.Add(new DefRow(Required(root), Required(oldName), null, policy, Name, Required(reason), Schema, LegacyRoot: legacyRoot));

    // ---- 标签 ------------------------------------------------------------------------------------------------------------

    internal void RenameLabel(OwnerSelector ownerSelector, string oldLabel, string newLabel, string reason) =>
        owner.Labels.Add(new LabelRow(Selector(ownerSelector), Required(oldLabel), Required(newLabel), LabelDisposition.Rename, null, Name, Required(reason), Schema));

    internal void DropLabel(OwnerSelector ownerSelector, string oldLabel, string reason) =>
        owner.Labels.Add(new LabelRow(Selector(ownerSelector), Required(oldLabel), null, LabelDisposition.Drop, null, Name, Required(reason), Schema));

    /// <summary>旧标签由步骤 <paramref name="stepId"/> 读取并改写；该步骤的 Consumes 必须列出它。</summary>
    internal void ConsumeLabel(OwnerSelector ownerSelector, string oldLabel, string stepId, string reason) =>
        owner.Labels.Add(new LabelRow(Selector(ownerSelector), Required(oldLabel), null, LabelDisposition.Consumed, Required(stepId), Name, Required(reason), Schema));

    // ---- 枚举与值 --------------------------------------------------------------------------------------------------------

    /// <summary>按名称保存的枚举成员改名；枚举必须至少登记一个保存位置（<see cref="DeclareEnumSite"/>）。</summary>
    internal void RenameEnumMember(string enumName, string oldMember, string newMember) =>
        owner.Enums.Add(new EnumRow(Required(enumName), Required(oldMember), Required(newMember), Name, Schema));

    /// <summary>登记保存枚举 <paramref name="enumName"/> 的位置；该枚举的成员改名在这些位置生效。</summary>
    /// <param name="part">值所在的部分：标签文本或列表项、字典键、字典值。</param>
    internal void DeclareEnumSite(OwnerSelector ownerSelector, string label, string enumName, bool flags = false, ValuePart part = ValuePart.Items) =>
        owner.EnumSites.Add(new EnumSite(Selector(ownerSelector), Required(label), Required(enumName), flags, part, Name));

    /// <summary>登记一个字符串、类型名或 defName 的保存位置；值映射与 Def 删除只作用于登记过的位置。</summary>
    /// <param name="part">值所在的部分：标签文本或列表项、字典键、字典值。</param>
    internal void DeclareValueContext(string contextName, OwnerSelector ownerSelector, string label, ValueContextKind kind = ValueContextKind.Text, string? defRoot = null, ValuePart part = ValuePart.Items) =>
        owner.ValueContexts.Add(new ValueContext(Required(contextName), Selector(ownerSelector), Required(label), kind, defRoot, part, Name));

    /// <summary>文本上下文中的旧值改为 <paramref name="newValue"/>；为 null 时删除该值（按容器规则删除或置空）。</summary>
    internal void MapValue(string contextName, string oldValue, string? newValue) =>
        owner.Values.Add(new ValueRow(Required(contextName), Required(oldValue), newValue, Name, Schema));

    /// <summary>默认值改了：旧档缺失 <paramref name="label"/> 且所属元素有 <paramref name="evidence"/> 时补写旧默认值。</summary>
    internal void MaterializeDefault(OwnerSelector ownerSelector, string label, string oldDefault, LegacyEvidence evidence, string reason, params string[] evidenceLabels) =>
        owner.Defaults.Add(new DefaultRow(Selector(ownerSelector), Required(label), oldDefault ?? throw new ArgumentNullException(nameof(oldDefault)), evidence, [.. evidenceLabels], Name, Required(reason), Schema));

    // ---- 结构 ------------------------------------------------------------------------------------------------------------

    /// <summary>旧版构建中 <paramref name="defName"/> 带有的组件类（旧类型名）；组件选择器优先按它判断。</summary>
    internal void DeclareLegacyComps(string root, string defName, params string[] legacyCompClasses) =>
        owner.LegacyComps.Add(new LegacyCompsRow(Required(root), Required(defName), [.. legacyCompClasses], Name, Schema));

    /// <summary>登记一个读取方能容忍缺项的列表容器（元素名路径后缀，如 <c>storyState/recentRandomIncidents</c>）。</summary>
    internal void DeclareRemovableContainer(string path, string reason) =>
        owner.Containers.Add(new RemovableContainer(Required(path).Split('/'), Name, Required(reason)));

    /// <summary>登记按下标一一对应的列表标签组。</summary>
    internal void DeclareParallelGroup(OwnerSelector ownerSelector, params string[] labels) =>
        owner.ParallelGroups.Add(new ParallelGroup(Selector(ownerSelector), [.. labels], Name));

    // ---- 进行中的工作与 Lord ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 旧 JobDriver 类型在阶段 B 结束进行中与排队的工作。只有显式登记的类型会结束；
    /// 保持行为的改名应使用 PreserveInFlightJobs，结构变化应通过 ClaimJobDriver 保留并迁移状态。
    /// </summary>
    internal void EndInFlightJobs(string legacyDriverType) =>
        owner.JobPolicies.Add(new JobDriverPolicyRow(Required(legacyDriverType), JobDriverPolicy.EndInFlight, null, Name, Schema));

    /// <summary>已核对 Toil 序列与保存字段含义不变，保留旧工作的当前状态与队列位置。</summary>
    internal void PreserveInFlightJobs(string legacyDriverType) =>
        owner.JobPolicies.Add(new JobDriverPolicyRow(Required(legacyDriverType), JobDriverPolicy.Preserve, null, Name, Schema));

    /// <summary>旧 JobDriver 类型由步骤 <paramref name="stepId"/> 处理，默认处理跳过它。</summary>
    internal void ClaimJobDriver(string legacyDriverType, string stepId) =>
        owner.JobPolicies.Add(new JobDriverPolicyRow(Required(legacyDriverType), JobDriverPolicy.Claimed, Required(stepId), Name, Schema));

    /// <summary>旧 LordJob 类型的状态图重置到起始 LordToil（DOM 删除状态图数据，阶段 B 重新进入起始 LordToil）。</summary>
    internal void ResetLordGraph(string legacyLordJobType) =>
        owner.LordPolicies.Add(new LordJobPolicyRow(Required(legacyLordJobType), LordJobPolicy.ResetGraph, null, Name, Schema));

    /// <summary>已核对 Lord 状态图及保存字段含义不变，保留当前状态和计时。</summary>
    internal void PreserveLordGraph(string legacyLordJobType) =>
        owner.LordPolicies.Add(new LordJobPolicyRow(Required(legacyLordJobType), LordJobPolicy.Preserve, null, Name, Schema));

    /// <summary>删除旧 LordJob 类型的整个 Lord，并置空对它的引用。删除的 LordJob 类型总是这样处理。</summary>
    internal void RemoveLords(string legacyLordJobType) =>
        owner.LordPolicies.Add(new LordJobPolicyRow(Required(legacyLordJobType), LordJobPolicy.RemoveLord, null, Name, Schema));

    internal void ClaimLordJob(string legacyLordJobType, string stepId) =>
        owner.LordPolicies.Add(new LordJobPolicyRow(Required(legacyLordJobType), LordJobPolicy.Claimed, Required(stepId), Name, Schema));

    // ---- 设置文件（版本是设置文件自己的 settingsSchema，与存档结构版本无关） -----------------------------------------

    internal void RenameSettingsClass(string oldClass, string newClass, int schema) =>
        owner.Settings.Add(new SettingsRow(SettingsRowKind.ClassRename, Required(oldClass), Required(newClass), schema, Name));

    internal void RenameSetting(string oldLabel, string newLabel, int schema) =>
        owner.Settings.Add(new SettingsRow(SettingsRowKind.LabelRename, Required(oldLabel), Required(newLabel), schema, Name));

    internal void DropSetting(string oldLabel, int schema) =>
        owner.Settings.Add(new SettingsRow(SettingsRowKind.LabelDrop, Required(oldLabel), null, schema, Name));

    // ---- MooGirl 时代（不支持，只识别并丢弃） ----------------------------------------------------------------------------

    internal void MooGirlTypePrefix(string prefix) => owner.MooGirlTypePrefixes.Add(Required(prefix));

    internal void MooGirlDefPattern(string pattern) => owner.MooGirlDefPatterns.Add(Required(pattern));

    // ---- 步骤 ------------------------------------------------------------------------------------------------------------

    /// <summary>登记一个 DOM 或读档后步骤；同阶段内按 (模块顺序, 登记顺序) 运行。</summary>
    internal void Step(UpgradeStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        step.AssignModule(Name);
        owner.Steps.Add(step);
    }

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static OwnerSelector Selector(OwnerSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return selector;
    }
}
