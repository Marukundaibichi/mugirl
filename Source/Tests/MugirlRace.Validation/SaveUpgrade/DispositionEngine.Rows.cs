using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>枚举、Def、工具、存档字符串、加载 id、设置、JobDriver/LordJob 与存档观测行的处置，以及模块归属。</summary>
internal sealed partial class DispositionEngine
{
    private static readonly (string Prefix, string Kind)[] ImpliedPrefixes =
    [
        ("Blueprint_Install_", "Blueprint_Install"), ("Blueprint_", "Blueprint"), ("Frame_", "Frame"), ("Corpse_", "Corpse"), ("UnnaturalCorpse_", "UnnaturalCorpse"), ("Meat_", "Meat"), ("Make_", "Make"),
    ];

    // 动词加载 id 是 <ownerID>_<toolId>_<ManeuverDef>：这些名字改了，存档中的动词引用就找不到（决定 §2 加载 id）。
    // ToolCapacityDef 只用于选择 maneuver；CalculateUniqueLoadID 实际只嵌入 tool.id 和 maneuver.defName。
    private static readonly string[] VerbIdRoots = ["ManeuverDef"];

    private Verdict Enum(string legacyEnum, string member)
    {
        string? newEnum = MapType(legacyEnum);
        if (newEnum is null)
        {
            return Verdict.Dropped("enum type dropped", "types", null);
        }

        // 清单的枚举行用新枚举名（契约 K1）；旧名写的行同样接受。
        if (enumMap.TryGetValue((newEnum, member), out ManifestEntry? entry) || enumMap.TryGetValue((legacyEnum, member), out entry))
        {
            if (entry.New is null || !EnumHas(newEnum, entry.New))
            {
                Failures.Add((entry.ToString(), $"Mapped to '{entry.New}', which is not a member of {newEnum}."));
            }

            return Verdict.Renamed(entry.New ?? "", entry.ToString(), entry.Module);
        }

        if ((Consumption($"enum:{newEnum}.{member}") ?? Consumption($"enum:{legacyEnum}.{member}")) is { } step)
        {
            return Verdict.Consumed(step);
        }

        return EnumHas(newEnum, member) ? Verdict.Unchanged() : Verdict.Undisposed($"{newEnum} has no member {member}; old saves would fail to parse the value");
    }

    private bool EnumHas(string enumType, string member)
        => build.Types.TryGetValue(enumType, out TypeDefinition? type) && type.IsEnum && type.Fields.Any(f => f.IsStatic && f.IsLiteral && f.Name == member);

    private Verdict Def(string root, string defName, string defClass)
    {
        if (DefEntry(root, defName) is { } entry)
        {
            if (Array.IndexOf(VerbIdRoots, root) >= 0 && !steps.Values.Any(step => step.Consumes.Contains($"def:{root}/{defName}", StringComparer.Ordinal)))
            {
                Failures.Add((entry.ToString(), $"{root} names are frozen: verb load IDs embed them (<ownerID>_<toolId>_<ManeuverDef>); only a step that rewrites verb IDs and consumes def:{root}/{defName} may change one."));
            }

            if (entry.Disposition == "drop")
            {
                return Verdict.Dropped(entry.Policy ?? "", entry.ToString(), entry.Module);
            }

            string target = entry.New ?? "";
            string targetRoot = entry.Root is null or "*" ? root : entry.Root;
            if (!build.HasDef(targetRoot, target))
            {
                Failures.Add((entry.ToString(), $"Target def {target} does not exist in DefDatabase<{targetRoot}> of the new content."));
            }

            if (targetRoot is "ThingDef" or "RecipeDef" && target.Length > 0 && char.IsAsciiDigit(target[^1]))
            {
                Failures.Add((entry.ToString(), $"Target {target} ends in a digit: thing and bill load IDs (<defName><number>) would parse a different number."));
            }

            // 新名若是另一个已发布旧 Def 的名字，且那个旧 Def 本身不再以原意存在（改名或删除），引用它的旧档会静默绑定到这个 Def。
            if (target != defName && corpus.Rows(LegacyCorpus.DefsFile).Any(r => r[0] == root && r[1] == target && r.IsRequired) && DefEntry(root, target) is not null)
            {
                Failures.Add((entry.ToString(), $"Name reuse: {target} was another published def that the manifest renames or drops; old references to it would bind to {defName}'s successor."));
            }

            return Verdict.Renamed(target, entry.ToString(), entry.Module);
        }

        if (Consumption($"def:{root}/{defName}") is { } step)
        {
            return Verdict.Consumed(step);
        }

        if (IsMooGirlDef(defName))
        {
            return Verdict.Dropped("MooGirl era", "moogirlEra.defPatterns", null);
        }

        if (build.HasDef(root, defName))
        {
            return Verdict.Unchanged();
        }

        if (Array.IndexOf(VerbIdRoots, root) >= 0)
        {
            return Verdict.Undisposed($"{root} {defName} is gone: verb load IDs embed it, so it is frozen unless a verb-ID step consumes it");
        }

        // 游戏生成的 Def 跟随其来源 Def 的处置。
        if (defClass.StartsWith("implied:", StringComparison.Ordinal) && ImpliedPrefixes.FirstOrDefault(p => defName.StartsWith(p.Prefix, StringComparison.Ordinal)) is { Prefix: { } prefix })
        {
            string source = defName[prefix.Length..];
            string sourceRoot = defClass == "implied:Make" ? "ThingDef" : root;
            if (DefEntry(sourceRoot, source) is { } sourceEntry)
            {
                if (sourceEntry.Disposition == "drop")
                {
                    return Verdict.Dropped("source def dropped", sourceEntry.ToString(), sourceEntry.Module);
                }

                return build.HasDef(root, prefix + sourceEntry.New)
                    ? Verdict.Renamed(prefix + sourceEntry.New, "derived from " + sourceEntry, sourceEntry.Module)
                    : Verdict.Undisposed($"source def renamed to {sourceEntry.New}, but {prefix}{sourceEntry.New} is not generated");
            }
        }

        return Verdict.Undisposed($"no {root.TrimStart('?')} named {defName} in the new content and no manifest entry");
    }

    private ManifestEntry? DefEntry(string root, string defName)
        => defMap.GetValueOrDefault((root, defName)) ?? defMap.GetValueOrDefault(("*", defName))
            ?? (root.StartsWith('?') ? defMap.FirstOrDefault(pair => pair.Key.Name == defName).Value : null);

    // 工具 id 出现在动词加载 id 中：映射后的 ThingDef 必须仍有同 id 的工具（丢弃的 Def 不需要）。
    private Verdict Tool(string thingDef, string toolId)
    {
        string target = thingDef;
        if (DefEntry("ThingDef", thingDef) is { } entry)
        {
            if (entry.Disposition == "drop")
            {
                return Verdict.Dropped("def dropped", entry.ToString(), entry.Module);
            }

            target = entry.New ?? thingDef;
        }
        else if (IsMooGirlDef(thingDef))
        {
            return Verdict.Dropped("MooGirl era", "moogirlEra.defPatterns", null);
        }

        if (Consumption($"value:tool/{toolId}") is { } step)
        {
            return Verdict.Consumed(step);
        }

        if (!build.HasDef("ThingDef", target))
        {
            return Verdict.NotApplicable("the def itself is undisposed");
        }

        return build.ToolIds(target).Contains(toolId, StringComparer.Ordinal)
            ? Verdict.Unchanged()
            : Verdict.Undisposed($"ThingDef {target} has no tool '{toolId}' any more: saved verbs (<ownerID>_{toolId}_<ManeuverDef>) would not resolve");
    }

    private Verdict Value(string field, string quoted, string valueKind)
    {
        string value = Unquote(quoted);
        int separator = field.LastIndexOf("::", StringComparison.Ordinal);
        string owner = separator > 0 ? field[..separator] : field;
        string? newOwner = MapType(owner);
        if (valueMap.TryGetValue(value, out List<ManifestEntry>? entries))
        {
            ManifestEntry entry = entries[0];
            return entry.New is null ? Verdict.Dropped("value dropped", entry.ToString(), entry.Module) : Verdict.Renamed(entry.New, entry.ToString(), entry.Module);
        }

        if (consumed.Keys.FirstOrDefault(token => token.StartsWith("value:", StringComparison.Ordinal) && SplitValueToken(token).Value == value) is { } token
            && Consumption(token) is { } step)
        {
            return Verdict.Consumed(step);
        }

        if (newOwner is null)
        {
            return Verdict.Dropped("owner dropped", "types", null);
        }

        string newField = newOwner + field[(separator > 0 ? separator : field.Length)..];
        if (build.SavedValues.Contains((newField, quoted)))
        {
            return Verdict.Unchanged();
        }

        return valueKind == "keyed" && build.KeyedKeys.Contains(value)
            ? Verdict.Unchanged("Keyed key still defined")
            : Verdict.Undisposed($"the new build never stores {quoted} into {newField} and no value mapping covers it");
    }

    private Verdict LoadId(string type, string literals)
    {
        string? current = MapType(type);
        if (current is null)
        {
            return Verdict.Dropped("type dropped", "types", null);
        }

        return build.Schema.LoadIds.Any(row => row[0] == current && row[1] == literals)
            ? Verdict.Unchanged()
            : Verdict.Undisposed($"GetUniqueLoadID of {current} no longer uses {literals}: saved references would not resolve");
    }

    private Verdict Setting(string settingsClass, string label, string legacyDefault, bool mooGirlOnly)
    {
        if (mooGirlOnly)
        {
            return Verdict.Dropped("MooGirl-era settings files are not migrated", "decisions §2", null);
        }

        string? current = build.SettingsClass;
        if (current is null)
        {
            return Verdict.Undisposed("the new build has no ModSettings class");
        }

        bool classOk = settingsClass == current || (manifest?.SettingsClass is { } fileClass && fileClass.Old == settingsClass && fileClass.New == current);
        ManifestEntry? entry = manifest?.SettingsLabels.FirstOrDefault(e => e.Old == label);
        if (!classOk)
        {
            return Verdict.Undisposed($"settings class {settingsClass} became {current} without a settings.fileClass entry (one error per startup, values ignored)");
        }

        if (entry is not null)
        {
            if (entry.New is not null && !build.OwnLabels(current).Contains(entry.New, StringComparer.Ordinal))
            {
                Failures.Add(($"settings.labels {label}", $"Renamed to '{entry.New}', which {current} does not write."));
            }

            return entry.New is null ? Verdict.Dropped("settings label dropped", "settings.labels", null) : Verdict.Renamed(entry.New, "settings.labels", null);
        }

        IReadOnlyList<LabelFact> facts = build.LabelsOf(current, label);
        if (facts.Count == 0)
        {
            return Verdict.Undisposed($"{current} no longer writes '{label}'; the player's value would silently reset");
        }

        // 设置文件同样省略等于默认值的标签：默认值变了，玩家保留旧默认值的设置会静默变成新默认值。
        return IsConstant(legacyDefault) && facts.All(fact => fact.Default != legacyDefault && IsConstant(fact.Default))
            ? Verdict.Undisposed($"default drift: the settings file omitted '{label}' when it was {legacyDefault}; the new build reads a missing node as {facts[0].Default}")
            : Verdict.Unchanged();
    }

    private Verdict JobOrLord(string family, string type, string abstractFlag)
    {
        if (abstractFlag == "abstract")
        {
            return Verdict.NotApplicable("abstract");
        }

        Dictionary<string, ManifestEntry> policies = family == "JobDriver" ? jobPolicies : lordPolicies;
        string? current = MapType(type);
        if (policies.TryGetValue(type, out ManifestEntry? entry) || (current is not null && policies.TryGetValue(current, out entry)))
        {
            if (entry.Policy == "preserve")
            {
                string expectedBase = family == "JobDriver" ? "Verse.AI.JobDriver" : "Verse.AI.Group.LordJob";
                if (current is null || !build.Types.TryGetValue(current, out TypeDefinition? target) || target.IsAbstract
                    || !CecilNames.BaseTypeNames(target).Contains(expectedBase, StringComparer.Ordinal))
                {
                    return Verdict.Undisposed($"preserve policy has no concrete {family} target: {current ?? "(dropped)"}");
                }

                // 保留只声明不中断在途对象，不消耗任何标签、默认值或图结构；这些语料行仍逐项独立验证。
                // Toil/图构造方法的身份由该波次 IL parity 和在途存档夹具共同验证。
                return new Verdict("unchanged", current, entry.Section, string.IsNullOrWhiteSpace(entry.Module) ? null : entry.Module, "");
            }

            return new Verdict("consumed", entry.Policy ?? "", entry.Section, string.IsNullOrWhiteSpace(entry.Module) ? null : entry.Module, "");
        }

        if (IsMooGirlType(type))
        {
            return Verdict.Dropped("MooGirl era", "moogirlEra.typePrefixes", null);
        }

        return Verdict.Undisposed($"no {(family == "JobDriver" ? "jobPolicies" : "lordPolicies")} entry: in-flight {family} state (toil index / lord graph) of old saves has no defined handling");
    }

    private Verdict Observed(string kind, string token, string context)
    {
        switch (kind)
        {
            case "type":
            case "type?":
                return Type(token, legacyTypes.Contains(token) ? RootOfLegacy(token) : "-");
            case "label":
                return Label(context, token, null);
            case "label?":
                return Verdict.NotApplicable("origin unknown: no legacy Mugirl owner writes it statically and it is no game/dependency literal label (another mod's comp or a validation build); review only");
            case "def":
            case "thingid":
                if (build.HasDef(kind == "thingid" ? "ThingDef" : "*", token))
                {
                    return Verdict.Unchanged();
                }

                List<CorpusRow> defs = corpus.Rows(LegacyCorpus.DefsFile).Where(r => r[1] == token && (kind != "thingid" || r[0] == "ThingDef")).ToList();
                return defs.Count > 0 ? Def(defs[0][0], token, defs[0][2]) : Def("?", token, "-");
            case "def-pattern":
                return IsMooGirlDef(token) ? Verdict.Dropped("MooGirl era", "moogirlEra.defPatterns", null)
                    : build.HasDef("*", token) ? Verdict.Unchanged()
                    : Def("?", token, "-");
            default:
                return Verdict.NotApplicable("unknown observed kind " + kind);
        }
    }

    private string RootOfLegacy(string type)
        => corpus.Rows(LegacyCorpus.TypesFile).Where(r => r[0] == type && r[2] != "-").Select(r => r[2]).FirstOrDefault() ?? "-";

    // ---- 模块归属 ----

    private string ModuleOf(CorpusRow row)
    {
        string? module = row.File switch
        {
            LegacyCorpus.TypesFile or LegacyCorpus.JobsLordsFile => TypeModule(row.File == LegacyCorpus.TypesFile ? row[0] : row[1]),
            LegacyCorpus.LabelsFile or LegacyCorpus.EnumsFile or LegacyCorpus.LoadIdsFile or LegacyCorpus.SettingsFile => TypeModule(row[0]),
            LegacyCorpus.SavedValuesFile => TypeModule(row[0].Split("::")[0]),
            LegacyCorpus.DefsFile => DefModule(row[0], row[1]),
            LegacyCorpus.ToolsFile => DefModule("ThingDef", row[0]),
            LegacyCorpus.ObservedFile => row[0] switch
            {
                "type" or "type?" => TypeModule(row[1]),
                "label" or "label?" => TypeModule(row[2]),
                _ => DefModule("*", row[1]),
            },
            _ => null,
        };
        return module ?? (row.IsMooGirlOnly ? "moogirl" : ModuleMap.Unassigned);
    }

    private string? TypeModule(string legacy)
    {
        if (modules.OfType(legacy) is { } listed)
        {
            return listed;
        }

        string current = MapType(legacy) ?? legacy;
        return build.Types.TryGetValue(current.Split('+')[0], out TypeDefinition? type)
            ? modules.OfPath(sourceOf?.Invoke(type))
            : null;
    }

    private string? DefModule(string root, string defName)
    {
        if (modules.OfDef(root, defName) is { } listed)
        {
            return listed;
        }

        return modules.OfPath(build.DefFile(defName));
    }

    private Func<TypeDefinition, string?>? sourceOf;

    /// <summary>当前类型的源码文件（相对 Source/MugirlRace），用于模块归属；没有 PDB 时不设置。</summary>
    public void UseSources(Func<TypeDefinition, string?> source) => sourceOf = source;

    /// <summary>一行的处置结论。</summary>
    private sealed record Verdict(string Disposition, string Target, string Via, string? Module, string Detail)
    {
        public static Verdict Unchanged(string via = "") => new("unchanged", "", via, null, "");

        public static Verdict Renamed(string target, string via, string? module) => new("renamed", target, via, NonEmpty(module), "");

        public static Verdict Dropped(string policy, string via, string? module) => new("dropped", policy, via, NonEmpty(module), "");

        public static Verdict Consumed(string step, string? module = null) => new("consumed", step, "step " + step, NonEmpty(module), "");

        public static Verdict Undisposed(string detail) => new("undisposed", "", "", null, detail);

        public static Verdict NotApplicable(string detail) => new("n/a", "", "", null, detail);

        private static string? NonEmpty(string? module) => string.IsNullOrWhiteSpace(module) ? null : module;
    }
}
