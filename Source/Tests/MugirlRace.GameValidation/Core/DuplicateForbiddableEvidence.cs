using System.Reflection;
using System.Text.Json.Nodes;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>仅核实已出现的长袜服装的两个原版Forbiddable；不去重Def，不读取重存XML作为运行时证据。</summary>
internal static class DuplicateForbiddableEvidence
{
    internal const string Definition = "Mugirl_Bikini_Stocking";
    private static readonly Guid EngineMvid = ReviewedEngine.Mvid;
    // StaticCacheLifecycle: 每次隔离读档前重建；只保存方法证据，不持有游戏对象。
    private static bool checkedBeforeLoad;
    private static readonly JsonArray readers = [];
    private static readonly FieldInfo Comps = typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Forbidden = typeof(CompForbiddable).GetField("forbiddenInt", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Begin()
    {
        checkedBeforeLoad = false; readers.Clear();
        VerifyReaders("before load"); checkedBeforeLoad = true;
    }

    public static IReadOnlySet<string> Capture(IEnumerable<Thing> mapThings, out JsonObject evidence)
    {
        if (!checkedBeforeLoad) { throw new InvalidOperationException("Forbiddable readers were not checked before loading."); }
        VerifyReaders("after actual save");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rows = new JsonArray();
        foreach (IGrouping<string, Thing> group in mapThings.Where(t => t.def?.defName == Definition)
            .GroupBy(t => t.def.defName + t.thingIDNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal))
        {
            if (group.Count() != 1 || group.Single() is not Apparel apparel || !TryRoster(apparel, out string[] roster)) { continue; }
            ThingDef[] definitions = [.. DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.defName == Definition)];
            if (definitions.Length != 1 || !ReferenceEquals(definitions[0], apparel.def)) { continue; }
            ids.Add(group.Key);
            rows.Add(new JsonObject { ["id"] = group.Key, ["def"] = Definition, ["ownerType"] = apparel.GetType().FullName,
                ["actualAndDefCompTypes"] = new JsonArray([.. roster.Select(t => (JsonNode?)JsonValue.Create(t))]),
                ["forbiddenValues"] = new JsonArray(true, true) });
        }
        evidence = new JsonObject { ["engineMvid"] = EngineMvid.ToString(), ["readers"] = readers.DeepClone(), ["things"] = rows };
        return ids;
    }

    internal static bool TryRoster(Apparel apparel, out string[] roster)
    {
        roster = [];
        if (apparel.GetType() != typeof(Apparel) || apparel.def?.GetType() != typeof(ThingDef) || apparel.def.defName != Definition
            || apparel.def.thingClass != typeof(Apparel) || Comps.GetValue(apparel) is not List<ThingComp> actual
            || apparel.def.comps is not { } configured || actual.Count != configured.Count) { return false; }
        Type[] declared = [typeof(CompForbiddable), typeof(CompColorable), typeof(CompQuality), typeof(CompStyleable), typeof(CompForbiddable), typeof(CompColorable)];
        if (actual.Count != declared.Length || !actual.Select(c => c.GetType()).SequenceEqual(declared)) { return false; }
        for (int i = 0; i < actual.Count; i++)
        {
            ThingComp comp = actual[i];
            if (comp.GetType() != configured[i].compClass || !ReferenceEquals(comp.props, configured[i]) || !ReferenceEquals(comp.parent, apparel)) { return false; }
            if (comp is CompForbiddable && Forbidden.GetValue(comp) is not true) { return false; }
            Verify(comp.GetType().GetMethod(nameof(ThingComp.PostExposeData))!, "actual component reader");
            Verify(comp.GetType().GetMethod(nameof(ThingComp.Initialize))!, "actual component initializer");
            foreach (ConstructorInfo ctor in comp.GetType().GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) { Verify(ctor, "actual component constructor"); }
        }
        roster = [.. actual.Select(c => c.GetType().FullName!)];
        return true;
    }

    private static void VerifyReaders(string stage)
    {
        // 原版值读取每次都从同一curXmlParent[label]取首叶；ThingWithComps按实际comps顺序逐个调用。
        int[] tokens = [0x06009D8E, 0x06002742, 0x060026E6, 0x06002B7F, 0x06002B7C, 0x06002B79, 0x06002B7D, 0x06009D9F, 0x0600B1C1, 0x06002A98, 0x060034A1];
        foreach (int token in tokens) { Verify(ReviewedEngine.ResolveMethod(token)!, stage); }
        MethodInfo values = (MethodInfo)ReviewedEngine.ResolveMethod(0x06002742)!;
        Verify(values.MakeGenericMethod(typeof(bool)), stage);
        MethodInfo extractor = (MethodInfo)ReviewedEngine.ResolveMethod(0x060026E6)!;
        Verify(extractor.MakeGenericMethod(typeof(bool)), stage);
        Verify(((MethodInfo)ReviewedEngine.ResolveMethod(0x06002B79)!).MakeGenericMethod(typeof(CompForbiddable)), stage);
        MethodInfo lookup = typeof(System.Xml.XmlNode).GetProperty("Item", [typeof(string)])!.GetMethod!;
        // 游戏自带XML与离线宿主MVID不同，但已独立对比这一方法的IL、locals和EH完全一致。
        if (ObservationPatchEvidence.SerializationFingerprint(lookup) != "8C129BF82C084D21B963CF93809640D7970045F9D9744077EC00D7FCBA9E0845")
        { throw new InvalidOperationException("Unreviewed XML first-child lookup implementation."); }
        Verify(lookup, stage, requireEngine: false);
        Verify(typeof(Find).GetProperty(nameof(Find.Maps))!.GetMethod!, stage);
        Verify(typeof(Current).GetProperty(nameof(Current.Game))!.GetMethod!, stage);
        Verify(typeof(Game).GetProperty(nameof(Game.Maps))!.GetMethod!, stage);
        Verify(typeof(ScribeSaver).GetMethod(nameof(ScribeSaver.WriteElement))!, stage);
        Verify(typeof(ListerThings).GetProperty(nameof(ListerThings.AllThings))!.GetMethod!, stage);
        Verify(typeof(DefDatabase<ThingDef>).GetProperty(nameof(DefDatabase<ThingDef>.AllDefsListForReading))!.GetMethod!, stage);
    }

    private static void Verify(MethodBase method, string stage, bool requireEngine = true)
    {
        if (requireEngine && method.Module.ModuleVersionId != EngineMvid) { throw new InvalidOperationException("Unreviewed Forbiddable reader implementation."); }
        if (Harmony.GetPatchInfo(method) is { } p)
        {
            MethodInfo verifier = typeof(UnconsumedQualityObservation).GetMethod(nameof(UnconsumedQualityObservation.ProveRegisteredHook), BindingFlags.Static | BindingFlags.NonPublic)!;
            if (HasPatches(verifier)) { throw new InvalidOperationException("The exact observer proof method was patched."); }
            var seen = new HashSet<(string, MethodInfo)>();
            foreach ((string role, IEnumerable<Patch> group) in new[] { ("prefix", (IEnumerable<Patch>)p.Prefixes), ("postfix", p.Postfixes), ("transpiler", p.Transpilers),
                ("finalizer", p.Finalizers), ("innerPrefix", p.InnerPrefixes), ("innerPostfix", p.InnerPostfixes), ("innerFinalizer", p.InnerFinalizers) })
            {
                foreach (Patch patch in group)
                {
                    string? proof = UnconsumedQualityObservation.ProveRegisteredHook(method, patch, role);
                    if (proof is null || !seen.Add((role, patch.PatchMethod))) { throw new InvalidOperationException("An extra patch invalidates duplicate Forbiddable evidence: " + method); }
                    readers.Add(ObservationPatchEvidence.Describe(method, patch, role, stage, proof));
                }
            }
        }
        readers.Add(new JsonObject { ["stage"] = stage, ["method"] = method.DeclaringType?.FullName + "." + method.Name,
            ["mvid"] = method.Module.ModuleVersionId.ToString(), ["token"] = method.MetadataToken.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static bool HasPatches(MethodBase method) => Harmony.GetPatchInfo(method) is { } p
        && p.Prefixes.Count + p.Postfixes.Count + p.Transpilers.Count + p.Finalizers.Count + p.InnerPrefixes.Count + p.InnerPostfixes.Count + p.InnerFinalizers.Count != 0;
}
