using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 升级清单：把编译后的升级表格写成 JSON，供验证器（C.save-upgrade）对照旧版语料检查完整性。
/// 构建只读取登记的数据，不访问 DefDatabase、Find 或 Current。
/// </summary>
public static class SaveUpgradeManifest
{
    /// <summary>
    /// 清单格式版本 2：保留原有行，并导出值/枚举位置、容器、旧组件关系和逐行 schema，供离线验证准确限定适用范围。
    /// <c>defs</c> 只含登记行；原版隐含 Def（<c>Make_</c>、<c>Corpse_</c> 等）
    /// 由登记行按固定规则派生，不单独列出。表格无效时抛出 <see cref="UpgradeTableException"/>。
    /// </summary>
    public static string ToJson() => ToJson(UpgradeTables.Default);

    internal static string ToJson(UpgradeTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("manifestVersion", 2);
            writer.WriteNumber("schema", SaveSchema.Current);
            writer.WriteString("stampLabel", SaveSchema.StampLabel);
            writer.WriteString("stampType", SaveSchema.HubTypeName);
            WriteTypes(writer, tables);
            WriteDefs(writer, tables);
            WriteLabels(writer, tables);
            WriteEnums(writer, tables);
            WriteEnumSites(writer, tables);
            WriteValueContexts(writer, tables);
            WriteValues(writer, tables);
            WriteDefaults(writer, tables);
            WriteSteps(writer, tables);
            WritePolicies(writer, tables);
            WriteSettings(writer, tables);
            WriteStructure(writer, tables);
            WriteRuntimeProtocols(writer);
            writer.WriteStartObject("moogirlEra");
            WriteStrings(writer, "typePrefixes", tables.MooGirlTypePrefixes);
            WriteStrings(writer, "defPatterns", tables.MooGirlDefPatterns.Select(pattern => pattern.Text));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteTypes(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("types");
        foreach (TypeRow row in tables.TypeRows)
        {
            writer.WriteStartObject();
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.NewSaveName);
            writer.WriteString("disposition", row.New is null ? "drop" : "rename");
            writer.WriteString("policy", row.Policy is { } policy ? Camel(policy.ToString()) : null);
            writer.WriteString("module", row.Module);
            writer.WriteString("reason", row.Reason);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // 非 DOM 的 Scribe 协议也登记在清单；黄金夹具由实际 Job writer 读写，不伪装成 DOM 改名步骤。
    private static void WriteRuntimeProtocols(Utf8JsonWriter writer)
    {
        writer.WriteStartArray("runtimeProtocols");
        writer.WriteStartObject();
        writer.WriteString("id", LegacyThinkTreeKeys.StepId);
        writer.WriteString("owner", "Verse.AI.Job");
        writer.WriteString("keyLabel", "lastJobGiverKey");
        writer.WriteString("treeLabel", "jobGiverThinkTree");
        writer.WriteString("markerLabel", UnresolvedThinkTreeKeys.MarkerLabel);
        writer.WriteBoolean("optional", true);
        writer.WriteString("phase", "Scribe");
        writer.WriteString("goldenFixture", "ScribeGolden/ThinkTreeSources");
        writer.WriteString("reason", "Retain unresolved AI source identities without resetting tasks or blocking the save upgrade.");
        writer.WriteNumber("schema", SaveSchema.Current);
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static void WriteDefs(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("defs");
        foreach (DefRow row in tables.DefRows)
        {
            writer.WriteStartObject();
            writer.WriteString("root", row.Root);
            writer.WriteString("legacyRoot", row.LegacyRoot);
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.New);
            writer.WriteString("disposition", row.New is null ? "drop" : "rename");
            writer.WriteString("policy", row.Policy is { } policy ? Camel(policy.ToString()) : null);
            writer.WriteString("module", row.Module);
            writer.WriteString("reason", row.Reason);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLabels(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("labels");
        foreach (LabelRow row in tables.LabelRows)
        {
            writer.WriteStartObject();
            writer.WriteString("owner", row.Owner.Text);
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.New);
            writer.WriteString("disposition", Camel(row.Disposition.ToString()));
            writer.WriteString("step", row.Step);
            writer.WriteString("module", row.Module);
            writer.WriteString("reason", row.Reason);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEnums(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("enums");
        foreach (EnumRow row in tables.EnumRows)
        {
            writer.WriteStartObject();
            writer.WriteString("enum", row.Enum);
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.New);
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEnumSites(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("enumSites");
        foreach (EnumSite site in tables.EnumSites)
        {
            writer.WriteStartObject();
            writer.WriteString("owner", site.Owner.Text);
            writer.WriteString("label", site.Label);
            writer.WriteString("enum", site.Enum);
            writer.WriteBoolean("flags", site.Flags);
            writer.WriteString("part", Camel(site.Part.ToString()));
            writer.WriteString("module", site.Module);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValueContexts(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("valueContexts");
        foreach (ValueContext context in tables.ValueContexts)
        {
            writer.WriteStartObject();
            writer.WriteString("name", context.Name);
            writer.WriteString("owner", context.Owner.Text);
            writer.WriteString("label", context.Label);
            writer.WriteString("kind", Camel(context.Kind.ToString()));
            writer.WriteString("defRoot", context.DefRoot);
            writer.WriteString("part", Camel(context.Part.ToString()));
            writer.WriteString("module", context.Module);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValues(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("values");
        foreach (ValueRow row in tables.ValueRows)
        {
            writer.WriteStartObject();
            writer.WriteString("context", row.Context);
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.New);
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDefaults(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("defaults");
        foreach (DefaultRow row in tables.DefaultRows)
        {
            writer.WriteStartObject();
            writer.WriteString("owner", row.Owner.Text);
            writer.WriteString("label", row.Label);
            writer.WriteString("oldDefault", row.OldDefault);
            writer.WriteString("evidence", Camel(row.Evidence.ToString()));
            WriteStrings(writer, "evidenceLabels", row.EvidenceLabels);
            writer.WriteString("reason", row.Reason);
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSteps(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("steps");
        foreach (UpgradeStep step in tables.DomSteps.Cast<UpgradeStep>().Concat(tables.PostLoadSteps))
        {
            writer.WriteStartObject();
            writer.WriteString("id", step.Id);
            writer.WriteString("phase", UpgradeReport.PhaseText(step.Phase));
            writer.WriteString("module", step.Module);
            WriteStrings(writer, "consumes", step.Consumes);
            writer.WriteString("reason", step.Reason);
            writer.WriteNumber("schema", step.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePolicies(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("jobPolicies");
        foreach (JobDriverPolicyRow row in tables.JobPolicies)
        {
            writer.WriteStartObject();
            writer.WriteString("type", row.Type);
            writer.WriteString("policy", row.Policy == JobDriverPolicy.Claimed ? "claimed:" + row.Step : Camel(row.Policy.ToString()));
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("lordPolicies");
        foreach (LordJobPolicyRow row in tables.LordPolicies)
        {
            writer.WriteStartObject();
            writer.WriteString("type", row.Type);
            writer.WriteString("policy", row.Policy == LordJobPolicy.Claimed ? "claimed:" + row.Step : Camel(row.Policy.ToString()));
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSettings(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartObject("settings");
        if (tables.SettingsRows.LastOrDefault(row => row.Kind == SettingsRowKind.ClassRename) is { } classRow)
        {
            writer.WriteStartObject("fileClass");
            writer.WriteString("old", classRow.Old);
            writer.WriteString("new", classRow.New);
            writer.WriteNumber("schema", classRow.Schema);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("fileClass");
        }

        writer.WriteStartArray("labels");
        foreach (SettingsRow row in tables.SettingsRows.Where(row => row.Kind != SettingsRowKind.ClassRename))
        {
            writer.WriteStartObject();
            writer.WriteString("old", row.Old);
            writer.WriteString("new", row.New);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("schema", SaveSchema.SettingsCurrent);
        writer.WriteEndObject();
    }

    private static void WriteStructure(Utf8JsonWriter writer, UpgradeTables tables)
    {
        writer.WriteStartArray("removableContainers");
        foreach (RemovableContainer container in tables.RemovableContainers)
        {
            writer.WriteStartObject();
            writer.WriteString("path", container.Text);
            writer.WriteString("module", container.Module);
            writer.WriteString("reason", container.Reason);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("parallelGroups");
        foreach (ParallelGroup group in tables.ParallelGroups)
        {
            writer.WriteStartObject();
            writer.WriteString("owner", group.Owner.Text);
            WriteStrings(writer, "labels", group.Labels);
            writer.WriteString("module", group.Module);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("legacyComps");
        foreach (LegacyCompsRow row in tables.LegacyCompsRows)
        {
            writer.WriteStartObject();
            writer.WriteString("root", row.Root);
            writer.WriteString("def", row.DefName);
            WriteStrings(writer, "classes", row.CompClasses);
            writer.WriteString("module", row.Module);
            writer.WriteNumber("schema", row.Schema);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
