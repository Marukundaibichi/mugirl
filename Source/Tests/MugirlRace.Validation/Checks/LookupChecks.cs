using System.Text.RegularExpressions;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 代码传给按名称查找的游戏 API 的字符串：Def 名（<c>GetNamed</c>、<c>GetNamedSilentFail</c>、<c>XDef.Named</c>）与 Keyed 翻译键。
/// 字面量必须存在；分析无法证明是一组字面量的值，必须带上安全理由列入 <c>Baselines/dynamic-lookups.allow.txt</c>。
/// 另外，程序集中每个形如 <c>Mugirl.&lt;段&gt;.&lt;段&gt;</c> 的字符串字面量（不是本程序集的类型名）都按翻译键核对：字面量数组、
/// 返回键的 switch 辅助方法与 CompProperties 的默认键不经过可追踪的 Translate 调用，例外清单的理由依赖这条检查。
/// </summary>
internal static class LookupChecks
{
    public const string DynamicFile = "dynamic-lookups.allow.txt";

    // 至少两段的 Mugirl 键：单段的 "Mugirl.X" 与根命名空间的类型名同形，只由调用点检查。
    private static readonly Regex KeyShape = new(@"^Mugirl(\.[A-Za-z0-9_]+){2,}$", RegexOptions.CultureInvariant);

    /// <param name="legacyTypeNames">
    /// 旧语料中的名字（类型、defName、存进字符串字段的字面量与旧 Keyed 键、存档观测名字）：升级层（<c>Mugirl.SaveUpgrade</c>）中等于它们的
    /// 键形字面量是旧名，不是翻译键。
    /// </param>
    public static void Run(Report report, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic, ModuleDefinition module, IReadOnlySet<string> legacyTypeNames)
    {
        report.Run("C.def-lookups", "Def names used by code exist (in the required content)", recorder => CheckDefs(recorder, content, sites, dynamic));
        report.Run("C.translation-keys", "Translation keys used by code exist in English and Simplified Chinese", recorder => CheckKeys(recorder, content, sites, dynamic, module, legacyTypeNames));
    }

    /// <summary>处理值不是已知字面量的站点；返回 true 时调用方跳过它。</summary>
    public static bool HandleDynamic(CheckRecorder recorder, LookupSite site, AllowList dynamic, HashSet<string> reported)
    {
        if (site.Value.IsKnown)
        {
            return false;
        }

        if (!dynamic.AllowsOrReview(site.AllowKey, "computed value; explain why every value exists") && reported.Add(site.AllowKey))
        {
            recorder.Fail($"dynamic {site.Category} in {site.Method}", "The value is computed at run time, so it cannot be verified here. "
                + $"Make it a literal, or add \"{site.Category}<TAB>{site.Method}<TAB>reason\" to Baselines/{DynamicFile} explaining why every value exists.", site.Location);
        }

        return true;
    }

    private static void CheckDefs(CheckRecorder recorder, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic)
    {
        int literals = 0;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (LookupSite site in sites.Where(s => s.Kind is LookupKind.DefName or LookupKind.DefNameSilent))
        {
            if (HandleDynamic(recorder, site, dynamic, reported))
            {
                continue;
            }

            Type type = site.DefType ?? typeof(Def);
            foreach (string defName in site.Value.Values!)
            {
                literals++;
                IReadOnlyList<DefEntry> found = content.Defs.Find(type, defName);
                string subject = $"{type.Name} '{defName}'";
                if (found.Count == 0)
                {
                    // 兼容层静默查找其他 Mod 的 Def 是设计如此（ADR-003）：那些 Mod 不在本机的完整配置中时无从核对。
                    if (site.Kind == LookupKind.DefNameSilent && site.InCompatibilityLayer)
                    {
                        recorder.Info(subject, "Optional third-party def looked up silently in the Compatibility layer; not defined by the content loaded here.", site.Location);
                        continue;
                    }

                    recorder.Fail(subject, site.Kind == LookupKind.DefName
                        ? "Looked up by name but defined nowhere (the game logs an error and returns null)."
                        : "Looked up silently but defined nowhere: the lookup always returns null.", site.Location);
                }
                else if (!found.Any(entry => entry.InMinimal))
                {
                    string providers = string.Join(", ", found.Select(entry => entry.Pack.PackageId).Distinct());
                    if (site.Kind == LookupKind.DefName)
                    {
                        recorder.Fail(subject, $"Exists only with optional content ({providers}); without it this lookup logs an error. "
                            + "Use a [MayRequire…] DefOf field, or a silent lookup with a fallback.", site.Location);
                    }
                    else
                    {
                        recorder.Info(subject, $"Optional content ({providers}); the silent lookup returns null without it.", site.Location);
                    }
                }
            }
        }

        dynamic.ReportProblems(recorder, key => key.StartsWith("def\t", StringComparison.Ordinal));
        recorder.Summary = $"{literals} def name literal(s) checked";
    }

    private static void CheckKeys(CheckRecorder recorder, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic, ModuleDefinition module, IReadOnlySet<string> legacyTypeNames)
    {
        int literals = 0;
        var checkedKeys = new HashSet<string>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (LookupSite site in sites.Where(s => s.Kind is LookupKind.TranslationKey or LookupKind.TranslationProbe))
        {
            if (HandleDynamic(recorder, site, dynamic, reported))
            {
                continue;
            }

            foreach (string key in site.Value.Values!)
            {
                literals++;
                checkedKeys.Add(key);
                foreach (string language in KeyedIndex.Languages.Keys)
                {
                    IReadOnlyList<KeyedEntry> found = content.Keyed.Find(language, key);
                    string subject = $"{key} ({language})";
                    if (found.Count == 0)
                    {
                        if (site.Kind == LookupKind.TranslationKey)
                        {
                            recorder.Fail(subject, "Keyed translation missing: players see the raw key. Add it to 1.7/Languages.", site.Location);
                        }
                        else
                        {
                            recorder.Info(subject, "Probed key does not exist (the caller handles that).", site.Location);
                        }
                    }
                    else if (site.Kind == LookupKind.TranslationKey && !found.Any(entry => entry.InMinimal))
                    {
                        recorder.Warn(subject, $"Defined only by optional content ({string.Join(", ", found.Select(e => e.Pack.PackageId).Distinct())}).", site.Location);
                    }
                }
            }
        }

        int shaped = CheckKeyShapedLiterals(recorder, content, module, checkedKeys, legacyTypeNames);
        dynamic.ReportProblems(recorder, key => key.StartsWith("translation\t", StringComparison.Ordinal));
        recorder.Summary = $"{literals} translation key literal(s) at Translate calls and {shaped} other key-shaped literal(s) checked";
    }

    // 调用点检查之外的键形字面量：每个都必须是两种语言中都有的 Keyed 键。返回核对的个数。
    private static int CheckKeyShapedLiterals(CheckRecorder recorder, ContentModel content, ModuleDefinition module, HashSet<string> alreadyChecked, IReadOnlySet<string> legacyTypeNames)
    {
        var typeNames = new HashSet<string>(module.GetTypes().Select(type => type.FullName.Replace('/', '+')), StringComparer.Ordinal);
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (MethodDefinition method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string literal && KeyShape.IsMatch(literal)
                    && !typeNames.Contains(literal) && !alreadyChecked.Contains(literal) && !IsLegacyNameInUpgradeLayer(method, literal, legacyTypeNames))
                {
                    found.TryAdd(literal, CodeLookups.Owner(method));
                }
            }
        }

        foreach ((string key, string owner) in found)
        {
            foreach (string language in KeyedIndex.Languages.Keys)
            {
                if (content.Keyed.Find(language, key).Count == 0)
                {
                    recorder.Fail($"key literal {key} ({language})", "String literal shaped like a Keyed key (Mugirl.<Area>.<Name>) that no Keyed file "
                        + "defines. Key arrays, key-returning helpers and CompProperties defaults reach Translate this way: add the key to 1.7/Languages, "
                        + "or rename a literal that is not a key.", owner);
                }
            }
        }

        return found.Count;
    }

    // 升级层的旧名表：Mugirl.SaveUpgrade 中等于旧语料名字的字面量（旧类型名、旧 defName、旧存档值与旧 Keyed 键）。
    private static bool IsLegacyNameInUpgradeLayer(MethodDefinition method, string literal, IReadOnlySet<string> legacyTypeNames)
        => legacyTypeNames.Contains(literal)
            && Metadata.CecilNames.NamespaceOf(method.DeclaringType).StartsWith(SaveUpgrade.NewBuild.UpgradeNamespace, StringComparison.Ordinal);
}
