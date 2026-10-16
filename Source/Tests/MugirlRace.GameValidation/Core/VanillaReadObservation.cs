using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>隔离验证中观察两个已核实的原版初始化；不赋值、不调用初始化方法、不消耗随机数。</summary>
internal static class VanillaReadObservation
{
    private static readonly Guid EngineMvid = ReviewedEngine.Mvid;
    private static readonly FieldInfo Psyfocus = AccessTools.Field(typeof(Pawn_PsychicEntropyTracker), "currentPsyfocus");
    private static readonly FieldInfo Locked = AccessTools.Field(typeof(Pawn_ApparelTracker), "lockedApparel");
    private static readonly MethodInfo PsyExpose = AccessTools.Method(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.ExposeData));
    private static readonly MethodInfo ApparelExpose = AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.ExposeData));
    private static readonly MethodInfo Initial = AccessTools.Method(typeof(Pawn_PsychicEntropyTracker), nameof(Pawn_PsychicEntropyTracker.SetInitialPsyfocusLevel));
    // StaticCacheLifecycle: 每次受控 load 清空，保存快照后/验证结束释放对象；正常游戏不会安装这些观察补丁。
    private static readonly Dictionary<object, Observation> observations = new(ReferenceEqualityComparer.Instance);
    private static readonly List<string> errors = [];
    private static readonly JsonArray patchEvidence = [];
    private static XmlDocument? document;
    private static Assembly? sourceAssembly;
    private static string? inputPath;
    private static bool active;
    private static bool installed;

    public static void Begin(ValidationContext context, string? sourcePath = null)
    {
        if (!GenCommandLine.CommandLineArgPassed("mugirlvalidate") || !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "mugirl-gamevalidation.json"))
            || !context.Options.Suites.Contains(SuiteCatalog.UpgradeSuiteName))
        {
            throw new InvalidOperationException("Vanilla observations require the isolated upgrade validation session.");
        }
        Start(context.Harmony, context.Target?.Assembly ?? throw new InvalidOperationException("No production main-save identity provider."), sourcePath);
    }

    private static void Start(Harmony harmony, Assembly assembly, string? sourcePath)
    {
        observations.Clear();
        errors.Clear();
        patchEvidence.Clear();
        document = null;
        sourceAssembly = assembly;
        inputPath = sourcePath;
        active = true;
        if (PsyExpose.Module.ModuleVersionId != EngineMvid || PsyExpose.MetadataToken != ReviewedEngine.Token(0x06008889)
            || ApparelExpose.MetadataToken != ReviewedEngine.Token(0x06008731) || Initial.MetadataToken != ReviewedEngine.Token(0x06008881))
        {
            throw new InvalidOperationException("The vanilla tracker implementation has not been verified for this engine assembly.");
        }
        CheckPatchChains("before load");
        if (!installed)
        {
            Patch(PsyExpose, nameof(BeforePsychic), nameof(AfterExpose));
            Patch(ApparelExpose, nameof(BeforeApparel), nameof(AfterExpose));
            Patch(Initial, nameof(BeforeInitial), nameof(AfterInitial));
            installed = true;
        }

        void Patch(MethodInfo method, string prefix, string postfix)
            => harmony.Patch(method, prefix: new HarmonyMethod(typeof(VanillaReadObservation), prefix) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(VanillaReadObservation), postfix) { priority = Priority.Last },
                finalizer: new HarmonyMethod(typeof(VanillaReadObservation), nameof(ObserveFailure)));
    }

    public static string SnapshotJson()
    {
        CheckPatchChains("after actual save");
        var events = new JsonArray();
        foreach (Observation entry in observations.Values.Where(o => !o.LoadingComplete || o.InPostLoad
            || (o.Psychic ? !o.PostLoadComplete : !o.CrossRefsComplete)))
        {
            Error("A source-bound tracker callback did not complete: " + entry.Path);
        }
        foreach (Observation entry in observations.Values.Where(o => o.Normalized))
        {
            if (!entry.Saved) { Error("An initialized tracker was not observed in the actual save: " + entry.Path); }
            events.Add(new JsonObject
            {
                ["kind"] = entry.Psychic ? "PsyfocusInitialization" : "LockedApparelEmptyReferences",
                ["parentPath"] = entry.Path, ["pawnId"] = entry.PawnId, ["ownerType"] = entry.Owner.GetType().FullName,
                ["oldMissing"] = entry.OldXml is null, ["oldXml"] = entry.OldXml,
                ["newValue"] = entry.SavedText, ["newFloatBits"] = entry.Psychic ? entry.InitialBits : null,
                ["saveCulture"] = entry.SaveCulture,
            });
        }
        string json = new JsonObject { ["format"] = 1, ["gameAssemblyMvid"] = EngineMvid.ToString(), ["events"] = events,
            ["patches"] = patchEvidence.DeepClone(),
            ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode?)JsonValue.Create(e))]) }.ToJsonString();
        active = false;
        observations.Clear();
        document = null;
        sourceAssembly = null;
        inputPath = null;
        return json;
    }

    public static void Stop()
    {
        active = false;
        installed = false;
        document = null;
        sourceAssembly = null;
        inputPath = null;
        observations.Clear();
    }

    private static void BeforePsychic(Pawn_PsychicEntropyTracker __instance, out Call? __state)
        => __state = Before(__instance, __instance.Pawn, psychic: true);

    private static void BeforeApparel(Pawn_ApparelTracker __instance, out Call? __state)
        => __state = Before(__instance, __instance.pawn, psychic: false);

    private static Call? Before(object instance, Pawn pawn, bool psychic)
    {
        if (!active) { return null; }
        try
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                string label = psychic ? "psychicEntropy" : "apparel";
                if (instance.GetType() != (psychic ? typeof(Pawn_PsychicEntropyTracker) : typeof(Pawn_ApparelTracker))
                    || !ReferenceEquals(Scribe.loader.curParent, instance) || Scribe.loader.curXmlParent is not XmlElement node
                    || node.Name != label || node.ParentNode is not XmlElement owner || owner["id"]?.InnerText != pawn.ThingID
                    || owner["def"] is null || owner["kindDef"] is null || node.OwnerDocument?.DocumentElement?.Name != "savegame"
                    || !MainSaveDocument.Matches(sourceAssembly!, node.OwnerDocument, inputPath)
                    || !PathOf(node).StartsWith("/savegame[1]/game[1]/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Tracker reader did not identify its exact source owner.");
                }
                if (document is not null && !ReferenceEquals(document, node.OwnerDocument))
                {
                    throw new InvalidOperationException("A tracker source belongs to a different document in this load.");
                }
                document = node.OwnerDocument;
                string field = psychic ? "currentPsyfocus" : "lockedApparel";
                XmlElement[] leaves = [.. node.ChildNodes.OfType<XmlElement>().Where(e => e.Name == field)];
                if (leaves.Length > 1 || observations.ContainsKey(instance))
                {
                    throw new InvalidOperationException("Ambiguous or repeated tracker source.");
                }
                var observation = new Observation(instance, psychic, PathOf(node), pawn.ThingID, leaves.SingleOrDefault()?.OuterXml);
                observations.Add(instance, observation);
                return new Call(observation, Scribe.mode, 0, null, false);
            }
            if (!observations.TryGetValue(instance, out Observation? entry))
            {
                if (Scribe.mode is LoadSaveMode.ResolvingCrossRefs or LoadSaveMode.PostLoadInit)
                {
                    Error("A tracker load callback has no source binding: " + pawn.ThingID);
                }
                return null;
            }
            float number = psychic ? (float)Psyfocus.GetValue(instance)! : 0f;
            if (Scribe.mode == LoadSaveMode.PostLoadInit && psychic)
            {
                entry.InPostLoad = true;
                entry.InitialCalls = 0;
                if (BitConverter.SingleToInt32Bits(number) != entry.LoadedBits)
                {
                    throw new InvalidOperationException("Psyfocus changed between LoadingVars and PostLoadInit: " + entry.Path);
                }
            }
            string? saved = Scribe.mode == LoadSaveMode.Saving && psychic ? number.ToString("G9", CultureInfo.CurrentCulture) : null;
            return new Call(entry, Scribe.mode, number, saved, !psychic && Locked.GetValue(instance) is null);
        }
        catch (Exception ex)
        {
            Error(ex.ToString());
            return null;
        }
    }

    private static void AfterExpose(Call? __state, bool __runOriginal)
    {
        if (!active || __state is not { } call) { return; }
        try
        {
            Observation entry = call.Entry;
            if (!__runOriginal || Scribe.mode != call.Mode) { throw new InvalidOperationException("The original tracker callback did not complete in its original Scribe phase."); }
            if (call.Mode == LoadSaveMode.LoadingVars) { entry.LoadingComplete = true; }
            if (call.Mode == LoadSaveMode.ResolvingCrossRefs) { entry.CrossRefsComplete = true; }
            if (call.Mode == LoadSaveMode.PostLoadInit) { entry.PostLoadComplete = true; }
            float number = entry.Psychic ? (float)Psyfocus.GetValue(entry.Owner)! : 0f;
            if (call.Mode == LoadSaveMode.LoadingVars && entry.Psychic)
            {
                entry.LoadedBits = BitConverter.SingleToInt32Bits(number);
                XmlElement? old = Parse(entry.OldXml);
                float expected = -1f;
                if (old is not null)
                {
                    if (string.Equals(old.GetAttribute("IsNull"), "true", StringComparison.OrdinalIgnoreCase)) { expected = 0f; }
                    else if (old.ChildNodes.OfType<XmlElement>().Any() || !float.TryParse(old.InnerText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out expected))
                    {
                        throw new InvalidOperationException("Psyfocus source is not a valid scalar; it cannot authorize initialization.");
                    }
                }
                if (entry.LoadedBits != BitConverter.SingleToInt32Bits(expected)) { throw new InvalidOperationException("The actual loaded psyfocus differs from the exact source/default."); }
            }
            else if (call.Mode == LoadSaveMode.ResolvingCrossRefs && !entry.Psychic && IsExactNullList(entry.OldXml))
            {
                if (!call.WasNull || Locked.GetValue(entry.Owner) is not System.Collections.IList { Count: 0 }) { throw new InvalidOperationException("Null lockedApparel did not resolve to an empty reference list."); }
                entry.Normalized = true;
            }
            else if (call.Mode == LoadSaveMode.PostLoadInit && entry.Psychic)
            {
                entry.InPostLoad = false;
                if (call.Number < 0f)
                {
                    if (entry.InitialCalls != 1 || !entry.InitialOriginal || entry.InitialBits != BitConverter.SingleToInt32Bits(number))
                    {
                        throw new InvalidOperationException("Psyfocus initialization did not match one actual original SetInitial call.");
                    }
                    entry.Normalized = true;
                }
                else if (entry.InitialCalls != 0 || BitConverter.SingleToInt32Bits(number) != entry.LoadedBits)
                {
                    throw new InvalidOperationException("A non-negative/NaN psyfocus changed during PostLoadInit.");
                }
            }
            else if (call.Mode == LoadSaveMode.Saving && entry.Normalized)
            {
                if (entry.Psychic)
                {
                    if (BitConverter.SingleToInt32Bits(call.Number) != entry.InitialBits || BitConverter.SingleToInt32Bits(number) != entry.InitialBits
                        || call.Text != number.ToString("G9", CultureInfo.CurrentCulture)
                        || call.Text != number.ToString("G9", CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.Name)))
                    {
                        throw new InvalidOperationException("Initialized psyfocus or its save formatting changed before/during the actual write.");
                    }
                    entry.SavedText = call.Text;
                    entry.SaveCulture = CultureInfo.CurrentCulture.Name;
                }
                else if (Locked.GetValue(entry.Owner) is not System.Collections.IList { Count: 0 })
                {
                    throw new InvalidOperationException("The initialized lockedApparel list is no longer empty when saved.");
                }
                entry.Saved = true;
            }
        }
        catch (Exception ex) { Error(ex.ToString()); }
    }

    private static void BeforeInitial(Pawn_PsychicEntropyTracker __instance, out Observation? __state)
    {
        __state = null;
        if (!active || Scribe.mode != LoadSaveMode.PostLoadInit) { return; }
        if (!observations.TryGetValue(__instance, out Observation? entry) || !entry.InPostLoad || !((float)Psyfocus.GetValue(__instance)! < 0f))
        {
            Error("SetInitialPsyfocusLevel did not belong to the matching source-bound PostLoadInit call.");
            return;
        }
        entry.InitialCalls++;
        __state = entry;
    }

    private static void AfterInitial(Observation? __state, bool __runOriginal)
    {
        if (__state is not { } entry) { return; }
        entry.InitialOriginal = __runOriginal;
        entry.InitialBits = BitConverter.SingleToInt32Bits((float)Psyfocus.GetValue(entry.Owner)!);
    }

    // void finalizer 不返回/替换异常；记录后由 Harmony 按原异常继续传播，重试不能覆盖这条失败证据。
    private static void ObserveFailure(Exception? __exception, MethodBase __originalMethod)
    {
        if (active && __exception is not null) { Error("The observed original method threw: " + __originalMethod.Name + ": " + __exception); }
    }

    private static void CheckPatchChains(string check)
    {
        foreach (MethodBase method in new MethodBase[] { PsyExpose, ApparelExpose, Initial,
            ReviewedEngine.ResolveMethod(0x06002742), ((MethodInfo)ReviewedEngine.ResolveMethod(0x06002742)).MakeGenericMethod(typeof(float)),
            ReviewedEngine.ResolveMethod(0x06002705)! })
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            if (patches is null) { continue; }
            foreach ((string role, IEnumerable<Patch> entries) in new[]
                {
                    ("prefix", (IEnumerable<Patch>)patches.Prefixes), ("postfix", patches.Postfixes),
                    ("transpiler", patches.Transpilers), ("finalizer", patches.Finalizers),
                    ("innerPrefix", patches.InnerPrefixes), ("innerPostfix", patches.InnerPostfixes), ("innerFinalizer", patches.InnerFinalizers),
                })
            {
                foreach (Patch patch in entries)
                {
                    string? proof = patch.owner == ValidationContext.OwnHarmonyId && patch.PatchMethod.DeclaringType == typeof(VanillaReadObservation)
                        ? "this validation observer" : ObservationPatchEvidence.GuardedProductionProof(method, patch, role);
                    JsonObject evidence = ObservationPatchEvidence.Describe(method, patch, role, check, proof);
                    patchEvidence.Add(evidence);
                    if (proof is null) { Error("An unverified patch affects the observed vanilla implementation: " + evidence.ToJsonString()); }
                }
            }
        }
    }

    private static XmlElement? Parse(string? xml)
    {
        if (xml is null) { return null; }
        var parsed = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
        parsed.LoadXml(xml);
        return parsed.DocumentElement;
    }

    private static bool IsExactNullList(string? xml)
        => Parse(xml) is { } leaf && leaf.Attributes.Count == 1 && leaf.GetAttribute("IsNull").Equals("true", StringComparison.OrdinalIgnoreCase)
            && leaf.ChildNodes.Count == 0;

    internal static string PathOf(XmlElement element)
    {
        List<string> parts = [];
        for (XmlNode? node = element; node is XmlElement current; node = current.ParentNode)
        {
            int index = 1;
            // XmlNode.PreviousSibling 会从父节点重新遍历；反向逐项读取在宽列表上是平方复杂度。
            for (XmlNode? sibling = current.ParentNode?.FirstChild; sibling is not null && !ReferenceEquals(sibling, current); sibling = sibling.NextSibling)
            {
                if (sibling is XmlElement other && other.Name == current.Name) { index++; }
            }
            parts.Add(current.Name + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
        }
        parts.Reverse();
        return "/" + string.Join('/', parts);
    }

    private static void Error(string text) => errors.Add(text);

    private sealed record Call(Observation Entry, LoadSaveMode Mode, float Number, string? Text, bool WasNull);

    private sealed class Observation(object owner, bool psychic, string path, string pawnId, string? oldXml)
    {
        public object Owner { get; } = owner;
        public bool Psychic { get; } = psychic;
        public string Path { get; } = path;
        public string PawnId { get; } = pawnId;
        public string? OldXml { get; } = oldXml;
        public int LoadedBits { get; set; }
        public bool InPostLoad { get; set; }
        public bool LoadingComplete { get; set; }
        public bool CrossRefsComplete { get; set; }
        public bool PostLoadComplete { get; set; }
        public int InitialCalls { get; set; }
        public int InitialBits { get; set; }
        public bool InitialOriginal { get; set; }
        public bool Normalized { get; set; }
        public bool Saved { get; set; }
        public string? SavedText { get; set; }
        public string? SaveCulture { get; set; }
    }
}
