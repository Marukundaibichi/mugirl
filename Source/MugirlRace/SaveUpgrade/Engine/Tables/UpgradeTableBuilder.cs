using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>收集各模块登记的行与步骤，校验后编译成不可变的 <see cref="UpgradeTables"/>。</summary>
/// <remarks>
/// 校验把全部问题一次列出（<see cref="UpgradeTableException"/>）：重名、链式改名（旧名 A→B 且 B→C，生成器必须合并成 A→C）、
/// 新名称占用其他行的旧名、同一所属元素上的标签改名链或互换、ThingDef/RecipeDef 改名目标（含隐含 Def）以数字结尾
/// （会改变原版从物品 ID 解析出的编号）、没有保存位置的枚举改名、删除的 LordJob 类型却要重置状态图、
/// 结构版本越界、被消耗的标签或认领的类型指向不存在的步骤等。
/// </remarks>
internal sealed partial class UpgradeTableBuilder
{
    /// <summary>框架自身使用的模块名，模块不能占用。</summary>
    internal const string FrameworkModule = "Framework";

    private readonly List<UpgradeModuleBuilder> modules = [];

    /// <param name="currentSchema">行与步骤允许的最高存档结构版本；只有测试会传入 <see cref="SaveSchema.Current"/> 以外的值。</param>
    internal UpgradeTableBuilder(int currentSchema = SaveSchema.Current)
    {
        CurrentSchema = currentSchema;
    }

    internal int CurrentSchema { get; }

    internal List<TypeRow> Types { get; } = [];

    internal List<DefRow> Defs { get; } = [];

    internal List<LabelRow> Labels { get; } = [];

    internal List<EnumRow> Enums { get; } = [];

    internal List<EnumSite> EnumSites { get; } = [];

    internal List<ValueContext> ValueContexts { get; } = [];

    internal List<ValueRow> Values { get; } = [];

    internal List<DefaultRow> Defaults { get; } = [];

    internal List<LegacyCompsRow> LegacyComps { get; } = [];

    internal List<RemovableContainer> Containers { get; } = [];

    internal List<ParallelGroup> ParallelGroups { get; } = [];

    internal List<JobDriverPolicyRow> JobPolicies { get; } = [];

    internal List<LordJobPolicyRow> LordPolicies { get; } = [];

    internal List<SettingsRow> Settings { get; } = [];

    internal List<string> MooGirlTypePrefixes { get; } = ["MooGirl.", "MooGirlSkinFix."];

    internal List<string> MooGirlDefPatterns { get; } = MooGirlEraNames.CreatePatterns();

    internal List<UpgradeStep> Steps { get; } = [];

    /// <summary>按模块顺序编译已发现的模块。</summary>
    internal static UpgradeTables Build(IEnumerable<UpgradeModuleRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var builder = new UpgradeTableBuilder();
        foreach (UpgradeModuleRegistration registration in registrations.OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.Ordinal))
        {
            registration.Module.Register(builder.Module(registration.Name, registration.Order));
        }

        return builder.Build();
    }

    /// <summary>开始登记一个模块；模块必须按顺序依次登记。</summary>
    internal UpgradeModuleBuilder Module(string name, int order)
    {
        var module = new UpgradeModuleBuilder(this, name, order);
        modules.Add(module);
        return module;
    }

    internal UpgradeTables Build()
    {
        List<string> problems = [];
        ValidateModules(problems);
        Dictionary<string, UpgradeStep> steps = ValidateSteps(problems);
        ValidateSchemas(problems);
        ValidateTypes(problems);
        List<DefRow> derived = ValidateDefs(problems);
        ValidateLabels(problems, steps);
        ValidateValues(problems);
        ValidatePolicies(problems, steps);
        ValidateSettings(problems);
        ValidateStructure(problems);
        if (problems.Count > 0)
        {
            throw new UpgradeTableException(problems);
        }

        return new UpgradeTables(this, [.. modules.Select(m => (m.Name, m.Order))], derived);
    }

    private static void RequireElementName(List<string> problems, string name)
    {
        if (!XmlReader.IsName(name))
        {
            problems.Add("'" + name + "' is not a valid XML element name.");
        }
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleNamePattern();

    private void ValidateModules(List<string> problems)
    {
        foreach (UpgradeModuleBuilder module in modules)
        {
            if (!ModuleNamePattern().IsMatch(module.Name) || module.Name == FrameworkModule)
            {
                problems.Add("Invalid module name '" + module.Name + "'.");
            }
        }

        foreach (IGrouping<string, UpgradeModuleBuilder> duplicate in modules.GroupBy(m => m.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add("Module '" + duplicate.Key + "' is registered more than once.");
        }

        foreach (IGrouping<int, UpgradeModuleBuilder> duplicate in modules.GroupBy(m => m.Order).Where(g => g.Count() > 1))
        {
            problems.Add("Modules " + string.Join(", ", duplicate.Select(m => m.Name)) + " share order " + duplicate.Key + "; orders must be unique.");
        }
    }

    private Dictionary<string, UpgradeStep> ValidateSteps(List<string> problems)
    {
        var byId = new Dictionary<string, UpgradeStep>(StringComparer.Ordinal);
        foreach (UpgradeStep step in Steps)
        {
            if (!byId.TryAdd(step.Id, step))
            {
                problems.Add("Step id '" + step.Id + "' is registered more than once.");
            }

            if (!step.Id.StartsWith(step.Module + ".", StringComparison.Ordinal) || step.Id.Length == step.Module.Length + 1)
            {
                problems.Add("Step id '" + step.Id + "' must have the form '" + step.Module + ".<Name>'.");
            }

            if (step.Schema < 1 || step.Schema > CurrentSchema)
            {
                problems.Add("Step '" + step.Id + "' targets schema " + step.Schema + " outside 1.." + CurrentSchema + ".");
            }

            if (step is not (DomUpgradeStep or PostLoadUpgradeStep))
            {
                problems.Add("Step '" + step.Id + "' must derive from DomUpgradeStep or PostLoadUpgradeStep.");
            }
        }

        return byId;
    }

    private void ValidateSchemas(List<string> problems)
    {
        IEnumerable<(string Row, int Schema)> rows = Types.Select(r => ("Type '" + r.Old + "'", r.Schema))
            .Concat(Defs.Select(r => ("Def '" + r.Root + "/" + r.Old + "'", r.Schema)))
            .Concat(Labels.Select(r => ("Label '" + r.Owner.Text + "/" + r.Old + "'", r.Schema)))
            .Concat(Enums.Select(r => ("Enum member '" + r.Enum + "." + r.Old + "'", r.Schema)))
            .Concat(Values.Select(r => ("Value '" + r.Context + "/" + r.Old + "'", r.Schema)))
            .Concat(Defaults.Select(r => ("Default '" + r.Owner.Text + "/" + r.Label + "'", r.Schema)))
            .Concat(LegacyComps.Select(r => ("Legacy comps of '" + r.Root + "/" + r.DefName + "'", r.Schema)))
            .Concat(JobPolicies.Select(r => ("JobDriver policy '" + r.Type + "'", r.Schema)))
            .Concat(LordPolicies.Select(r => ("LordJob policy '" + r.Type + "'", r.Schema)));
        foreach ((string row, int schema) in rows)
        {
            if (schema < 1 || schema > CurrentSchema)
            {
                problems.Add(row + " targets schema " + schema + " outside 1.." + CurrentSchema + ".");
            }
        }
    }

    private void ValidateTypes(List<string> problems)
    {
        var olds = new HashSet<string>(SaveTypeNames.Comparer);
        foreach (TypeRow row in Types)
        {
            if (!olds.Add(SaveTypeNames.Normalize(row.Old)))
            {
                problems.Add("Type '" + row.Old + "' has more than one row.");
            }

            if ((row.New is null) == (row.Policy is null))
            {
                problems.Add("Type row '" + row.Old + "' must be either a rename or a drop.");
            }
        }

        foreach (TypeRow row in Types)
        {
            if (row.NewSaveName is { } target && olds.Contains(target))
            {
                problems.Add("Type rename '" + row.Old + "' -> '" + target + "' targets a legacy name; compose chains and never reuse legacy names.");
            }
        }
    }

    private List<DefRow> ValidateDefs(List<string> problems)
    {
        var declared = new HashSet<(string, string)>();
        foreach (DefRow row in Defs)
        {
            if (row.Root == "Def" || row.Root.Contains('.', StringComparison.Ordinal))
            {
                problems.Add("Def row '" + row.Old + "' must name a DefDatabase root short name, not '" + row.Root + "'.");
            }

            if (!declared.Add((row.Root, row.Old)))
            {
                problems.Add("Def '" + row.Root + "/" + row.Old + "' has more than one row.");
            }

            if ((row.New is null) == (row.Policy is null))
            {
                problems.Add("Def row '" + row.Root + "/" + row.Old + "' must be either a rename or a drop.");
            }
        }

        List<DefRow> derived = [];
        var derivedByKey = new Dictionary<(string, string), DefRow>();
        foreach (DefRow row in Defs)
        {
            foreach (DefRow implied in DerivedDefNames.Expand(row))
            {
                if (declared.Contains((implied.Root, implied.Old)))
                {
                    continue;
                }

                if (derivedByKey.TryGetValue((implied.Root, implied.Old), out DefRow? existing))
                {
                    if (existing.New != implied.New || existing.Schema != implied.Schema)
                    {
                        problems.Add("Implied def '" + implied.Root + "/" + implied.Old + "' follows both " + existing.DerivedFrom + " and " + implied.DerivedFrom
                            + " with different results; declare it explicitly.");
                    }

                    continue;
                }

                derivedByKey.Add((implied.Root, implied.Old), implied);
                derived.Add(implied);
            }
        }

        var allOlds = new HashSet<(string, string)>(declared);
        allOlds.UnionWith(derivedByKey.Keys);
        foreach (DefRow row in Defs.Concat(derived))
        {
            if (row.New is not { } target)
            {
                continue;
            }

            if (allOlds.Contains((row.Root, target)))
            {
                problems.Add("Def rename '" + row.Root + "/" + row.Old + "' -> '" + target + "' targets a legacy name; compose chains and never reuse legacy names.");
            }

            if (row.Root is "ThingDef" or "RecipeDef" && char.IsAsciiDigit(target[^1]))
            {
                problems.Add("Def rename '" + row.Root + "/" + row.Old + "' -> '" + target + "' ends in a digit; thing and bill load ids would parse a different number.");
            }
        }

        return derived;
    }

    private void ValidateLabels(List<string> problems, Dictionary<string, UpgradeStep> steps)
    {
        var keys = new HashSet<(string, string)>();
        var keysByOwner = new HashSet<(string, string)>(Labels.Select(row => (row.Owner.Text, row.Old)));
        foreach (LabelRow row in Labels)
        {
            if (row.Owner == OwnerSelector.Any)
            {
                problems.Add("Label '" + row.Old + "' needs a concrete owner selector; labels are never renamed by name alone.");
            }

            if (!keys.Add((row.Owner.Text, row.Old)))
            {
                problems.Add("Label '" + row.Owner.Text + "/" + row.Old + "' has more than one row.");
            }

            RequireElementName(problems, row.Old);
            if (row.New is not null)
            {
                RequireElementName(problems, row.New);
            }

            if (row.Disposition == LabelDisposition.Rename && (row.New is null || row.New == row.Old))
            {
                problems.Add("Label rename '" + row.Owner.Text + "/" + row.Old + "' needs a different new label.");
            }

            if (row.Disposition == LabelDisposition.Rename && row.New is { } target && keysByOwner.Contains((row.Owner.Text, target)))
            {
                problems.Add("Label rename '" + row.Owner.Text + "/" + row.Old + "' -> '" + target + "' targets a legacy label of the same owner; compose chains and swaps into direct renames.");
            }

            if (row.Disposition != LabelDisposition.Consumed)
            {
                continue;
            }

            if (row.Step is null || !steps.TryGetValue(row.Step, out UpgradeStep? step))
            {
                problems.Add("Label '" + row.Owner.Text + "/" + row.Old + "' is consumed by unknown step '" + row.Step + "'.");
            }
            else if (!step.Consumes.Contains("label:" + row.Owner.Text + "/" + row.Old, StringComparer.Ordinal))
            {
                problems.Add("Step '" + step.Id + "' must list 'label:" + row.Owner.Text + "/" + row.Old + "' in its Consumes.");
            }
        }

        foreach (EnumSite site in EnumSites)
        {
            RequireElementName(problems, site.Label);
            if (site.Owner == OwnerSelector.Any)
            {
                problems.Add("Enum site '" + site.Label + "' needs a concrete owner selector.");
            }
        }

        var enumKeys = new HashSet<(string, string)>();
        var sitedEnums = new HashSet<string>(EnumSites.Select(site => site.Enum), StringComparer.Ordinal);
        foreach (EnumRow row in Enums)
        {
            if (!enumKeys.Add((row.Enum, row.Old)) || row.Old == row.New)
            {
                problems.Add("Enum member '" + row.Enum + "." + row.Old + "' has a duplicate or identity row.");
            }

            if (!sitedEnums.Contains(row.Enum))
            {
                problems.Add("Enum '" + row.Enum + "' has member renames but no declared site; the renames would never be applied.");
            }
        }

        foreach (EnumRow row in Enums)
        {
            if (enumKeys.Contains((row.Enum, row.New)))
            {
                problems.Add("Enum member rename '" + row.Enum + "." + row.Old + "' -> '" + row.New + "' targets a legacy member; compose chains.");
            }
        }
    }

    private void ValidateValues(List<string> problems)
    {
        var contexts = new Dictionary<string, ValueContext>(StringComparer.Ordinal);
        foreach (ValueContext context in ValueContexts)
        {
            if (!contexts.TryAdd(context.Name, context))
            {
                problems.Add("Value context '" + context.Name + "' is declared more than once.");
            }

            RequireElementName(problems, context.Label);
            if (context.HoldsDefNames != (context.DefRoot is not null))
            {
                problems.Add("Value context '" + context.Name + "' must name a Def root exactly when its kind is Def or DefReference.");
            }

            if (context.DefRoot is "Def")
            {
                problems.Add("Value context '" + context.Name + "' must name a DefDatabase root short name, not 'Def'.");
            }
        }

        var keys = new HashSet<(string, string)>();
        foreach (ValueRow row in Values)
        {
            if (!contexts.TryGetValue(row.Context, out ValueContext? context) || context.Kind != ValueContextKind.Text)
            {
                problems.Add("Value row '" + row.Old + "' refers to '" + row.Context + "', which is not a declared text context.");
            }

            if (!keys.Add((row.Context, row.Old)) || row.Old == row.New)
            {
                problems.Add("Value '" + row.Context + "/" + row.Old + "' has a duplicate or identity row.");
            }
        }

        foreach (DefaultRow row in Defaults)
        {
            RequireElementName(problems, row.Label);
            if (row.Evidence == LegacyEvidence.None)
            {
                problems.Add("Default '" + row.Owner.Text + "/" + row.Label + "' needs positive legacy evidence; a missing stamp alone is not enough.");
            }

            if (row.Evidence.HasFlag(LegacyEvidence.LegacyLabel) && row.EvidenceLabels.Count == 0)
            {
                problems.Add("Default '" + row.Owner.Text + "/" + row.Label + "' uses LegacyLabel evidence without evidence labels.");
            }

            if (row.Owner == OwnerSelector.Any)
            {
                problems.Add("Default '" + row.Label + "' needs a concrete owner selector.");
            }
        }
    }

    private void ValidatePolicies(List<string> problems, Dictionary<string, UpgradeStep> steps)
    {
        var jobTypes = new HashSet<string>(SaveTypeNames.Comparer);
        foreach (JobDriverPolicyRow row in JobPolicies)
        {
            if (!jobTypes.Add(row.Type))
            {
                problems.Add("JobDriver '" + row.Type + "' has more than one policy.");
            }

            if (row.Policy == JobDriverPolicy.Claimed && (row.Step is null || !steps.ContainsKey(row.Step)))
            {
                problems.Add("JobDriver '" + row.Type + "' is claimed by unknown step '" + row.Step + "'.");
            }
        }

        var lordTypes = new HashSet<string>(SaveTypeNames.Comparer);
        foreach (LordJobPolicyRow row in LordPolicies)
        {
            if (!lordTypes.Add(row.Type))
            {
                problems.Add("LordJob '" + row.Type + "' has more than one policy.");
            }

            if (row.Policy == LordJobPolicy.Claimed && (row.Step is null || !steps.ContainsKey(row.Step)))
            {
                problems.Add("LordJob '" + row.Type + "' is claimed by unknown step '" + row.Step + "'.");
            }

            // 删除的 LordJob 类型读成 null，Lord 在 PostLoadInit 解引用它；类型步骤总是删除整个 Lord，重置或认领都无法执行。
            if (row.Policy != LordJobPolicy.RemoveLord
                && Types.Any(type => type.New is null && SaveTypeNames.Comparer.Equals(SaveTypeNames.Normalize(type.Old), SaveTypeNames.Normalize(row.Type))))
            {
                problems.Add("LordJob '" + row.Type + "' is a dropped type; its lords are always removed, so a " + row.Policy + " policy cannot apply.");
            }
        }
    }

    private void ValidateSettings(List<string> problems)
    {
        var keys = new HashSet<(SettingsRowKind, string)>();
        foreach (SettingsRow row in Settings)
        {
            if (row.Schema < 1 || row.Schema > SaveSchema.SettingsCurrent)
            {
                problems.Add("Settings row '" + row.Old + "' targets settings schema " + row.Schema + " outside 1.." + SaveSchema.SettingsCurrent + ".");
            }

            if (!keys.Add((row.Kind, row.Old)))
            {
                problems.Add("Settings row '" + row.Kind + " " + row.Old + "' is registered more than once.");
            }

            if (row.Kind != SettingsRowKind.ClassRename)
            {
                RequireElementName(problems, row.Old);
                if (row.New is not null)
                {
                    RequireElementName(problems, row.New);
                }
            }
        }
    }

    private void ValidateStructure(List<string> problems)
    {
        var compKeys = new HashSet<(string, string)>();
        foreach (LegacyCompsRow row in LegacyComps)
        {
            if (row.Root is not ("ThingDef" or "HediffDef"))
            {
                problems.Add("Legacy comps of '" + row.DefName + "' must use root ThingDef or HediffDef.");
            }

            if (!compKeys.Add((row.Root, row.DefName)))
            {
                problems.Add("Legacy comps of '" + row.Root + "/" + row.DefName + "' are declared more than once.");
            }
        }

        foreach (ParallelGroup group in ParallelGroups)
        {
            foreach (string label in group.Labels)
            {
                RequireElementName(problems, label);
            }

            if (group.Labels.Count < 2)
            {
                problems.Add("A parallel group under '" + group.Owner.Text + "' needs at least two labels.");
            }
        }

        foreach (string pattern in MooGirlDefPatterns)
        {
            if (pattern.Trim('*').Length == 0)
            {
                problems.Add("MooGirl def pattern '" + pattern + "' would match every name.");
            }
        }
    }
}
