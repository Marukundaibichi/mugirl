using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>证明五类服装的旧 Normal 品质叶子没有当前读取者；只观察自然读取、转换器闸门和真实组件，不改游戏对象。</summary>
internal static class UnconsumedQualityObservation
{
    // 包含回调、所有嵌套helper、构造器和主DOM绑定，保留EH信息；Full/Offline两种实际程序集已校准同一摘要。
    // 修改观察算法后须独立复核并更新，不能在运行时自动接受当前方法体。
    private const string ReviewedObserverContract = "A7D5B0C7E59BE4C694E26EB01B5BFE081D1495916154FC5BCB2BEC884362814F";
    private const string NativeApparelContract = "16A29F33383FE424DB1791C16D33EE78AF7D3677ED63E5CB6FD94C75CD1DB5A6";
    private static readonly Guid EngineMvid = ReviewedEngine.Mvid;
    private static readonly HashSet<string> Defs = new(StringComparer.Ordinal)
    {
        "Mugirl_RA_MechanicBackpack", "Mugirl_RA_MechanicBackpack_B", "Mugirl_RA_MechanicBackpack_C", "Mugirl_RA_MechanicBackpack_D", "Mugirl_AutoloadingSystem",
    };
    private static readonly MethodInfo ApparelReader = (MethodInfo)ReviewedEngine.ResolveMethod(0x0600B1C1)!;
    private static readonly MethodInfo Converter = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1)!;
    private static readonly MethodInfo ConverterGate = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034B1)!;
    private static readonly FieldInfo ConversionChain = AccessTools.Field(typeof(BackCompatibility), "conversionChain");
    private static readonly Type[] NativeConverterTypes =
    [
        typeof(BackCompatibilityConverter_0_17_AndLower), typeof(BackCompatibilityConverter_0_18),
        typeof(BackCompatibilityConverter_0_19), typeof(BackCompatibilityConverter_1_0),
        typeof(BackCompatibilityConverter_1_2), typeof(BackCompatibilityConverter_1_3),
        typeof(BackCompatibilityConverter_1_4), typeof(BackCompatibilityConverter_1_5),
        typeof(BackCompatibilityConverter_Universal),
    ];
    private static readonly int[] ReaderTokens = [0x0600B1C1, 0x06002B7F, 0x06002B7C, 0x06002A98, 0x060027A2, 0x06009D8E, 0x06002769, 0x0600A4BF, 0x060034A1, 0x060034B1];
    // StaticCacheLifecycle: 本次隔离加载到实际保存；Snapshot/Stop释放所有游戏对象和XML。
    private static readonly Dictionary<string, string> candidates = new(StringComparer.Ordinal);
    private static readonly Dictionary<object, Entry> entries = new(ReferenceEqualityComparer.Instance);
    private static readonly List<string> errors = [];
    private static readonly JsonArray readers = [];
    private static readonly List<(MethodInfo Target, MethodInfo Hook, string Role, int Priority)> hooks = [];
    private static Assembly? production;
    private static Harmony? harmony;
    private static XmlDocument? document;
    private static string? inputPath;
    private static bool active;
    private static int controlThread;
    private static int loadThread;
    private static int saveThread;
    [ThreadStatic] private static Frame? current;

    public static void Begin(Assembly? assembly, Harmony patcher, string inputPath)
    {
        if (!GenCommandLine.CommandLineArgPassed("mugirlvalidate") || !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "mugirl-gamevalidation.json")))
        { throw new InvalidOperationException("Quality reader observations require isolated validation."); }
        if (assembly is null) { throw new InvalidOperationException("Missing target assembly for quality observations."); }
        Start(assembly, patcher, SaveXml.Load(inputPath), inputPath);
    }

    internal static void Start(Assembly assembly, Harmony patcher, XDocument input, string? sourcePath = null)
    {
        Stop(); candidates.Clear(); errors.Clear(); readers.Clear();
        production = assembly; harmony = patcher; inputPath = sourcePath; active = true;
        controlThread = Environment.CurrentManagedThreadId; loadThread = 0; saveThread = 0;
        XElement game = SaveXml.Game(input) ?? throw new InvalidOperationException("No actual game input for quality observations.");
        foreach (XElement owner in game.Descendants().Where(e => Defs.Contains(e.Element("def")?.Value ?? string.Empty)))
        {
            XElement[] fields = [.. owner.Elements("quality")];
            if (fields.Length != 1 || !IsNormal(fields[0]) || owner.Element("id")?.Value is not { Length: > 0 } id) { continue; }
            if (!candidates.TryAdd(id, owner.Element("def")!.Value)) { throw new InvalidOperationException("Ambiguous quality source identity: " + id); }
        }
        if (candidates.Count == 0) { return; }
        CheckReaders("before load");
        Patch(ConverterGate, null, nameof(AfterGate));
        Patch(Converter, nameof(BeforeConverter), nameof(AfterConverter));
        Patch(ApparelReader, nameof(BeforeApparel), nameof(AfterApparel));
    }

    private static void Patch(MethodInfo target, string? prefix, string postfix)
    {
        HarmonyMethod? before = prefix is null ? null : new HarmonyMethod(typeof(UnconsumedQualityObservation), prefix) { priority = Priority.First };
        var after = new HarmonyMethod(typeof(UnconsumedQualityObservation), postfix) { priority = Priority.Last };
        var finalizer = new HarmonyMethod(typeof(UnconsumedQualityObservation), target == ApparelReader ? nameof(ApparelFailed) : nameof(Failed)) { priority = Priority.Last };
        harmony!.Patch(target, prefix: before, postfix: after, finalizer: finalizer);
        if (before is not null) { hooks.Add((target, before.method, "prefix", Priority.First)); }
        hooks.Add((target, after.method, "postfix", Priority.Last)); hooks.Add((target, finalizer.method, "finalizer", Priority.Last));
    }

    private static void BeforeApparel(Apparel __instance, out Frame? __state)
    {
        __state = null;
        if (!active || candidates.Count == 0) { return; }
        var frame = new Frame(current, Scribe.mode); current = frame; __state = frame;
        Guard(() =>
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (Scribe.loader.curXmlParent is not XmlElement node || node["id"]?.InnerText is not { } id || !candidates.TryGetValue(id, out string? def)) { return; }
                Require(__instance.GetType() == typeof(Apparel) && ReferenceEquals(Scribe.loader.curParent, __instance), "The candidate is not the actual Apparel source owner.");
                Require(MainSaveDocument.Matches(production!, node.OwnerDocument, inputPath), "The quality source is not the active production main-save DOM/file.");
                Require(node["def"]?.InnerText == def && node.ChildNodes.OfType<XmlElement>().Count(e => e.Name == "quality") == 1
                    && node["quality"] is { } quality && IsNormal(XElement.Parse(quality.OuterXml)), "The actual quality leaf does not match the original source.");
                string path = VanillaReadObservation.PathOf(node);
                Require(path.StartsWith("/savegame[1]/game[1]/", StringComparison.Ordinal) && node.OwnerDocument?.DocumentElement?.Name == "savegame", "Quality owner is outside the main game document.");
                Require(document is null || ReferenceEquals(document, node.OwnerDocument), "Quality readers belong to different documents.");
                BindThread(ref loadThread, "main-document loading");
                document = node.OwnerDocument;
                Require(!entries.ContainsKey(__instance) && entries.Values.All(e => e.Id != id), "Repeated actual quality owner.");
                frame.Entry = new Entry(__instance, id, def, path);
                entries.Add(__instance, frame.Entry);
            }
            else if (entries.TryGetValue(__instance, out Entry? entry))
            {
                if (frame.Mode == LoadSaveMode.Saving)
                {
                    Require(entries.Count == candidates.Count && entries.Values.All(e => e.Loaded && e.Resolved && e.PostLoaded), "Saving began before every candidate completed reading.");
                    Require(!entry.Saved, "A quality owner was saved twice.");
                    BindThread(ref saveThread, "actual saving");
                }
                else
                {
                    Require(Environment.CurrentManagedThreadId == loadThread, "A quality read phase moved away from its actual loading thread.");
                    Require(frame.Mode == LoadSaveMode.ResolvingCrossRefs && entry.Loaded && !entry.Resolved
                        || frame.Mode == LoadSaveMode.PostLoadInit && entry.Resolved && !entry.PostLoaded, "Quality read phases are duplicated or out of order.");
                }
                frame.Entry = entry;
            }
        });
    }

    private static void BeforeConverter(object obj, out Frame? __state)
    {
        __state = current is { Entry: { } entry } frame && ReferenceEquals(entry.Owner, obj) ? frame : null;
        if (__state is not { } call) { return; }
        Guard(() => { CheckFrame(call); call.ConverterCalls++; call.InConverter = true; });
    }

    private static void AfterGate(bool __result, bool __runOriginal)
    {
        if (current is not { Entry: not null, InConverter: true } frame) { return; }
        Guard(() =>
        {
            CheckFrame(frame); Require(__runOriginal, "The actual converter gate was skipped.");
            frame.GateCalls++; frame.GateSkipped = __result;
            if (!__result) { frame.NativeChain = VerifyNativeApparelNoOp(frame); }
        });
    }

    private static void AfterConverter(Frame? __state, bool __runOriginal)
    {
        if (__state is not { } frame) { return; }
        Guard(() =>
        {
            CheckFrame(frame);
            Require(__runOriginal, "The actual BackCompatibility callback was skipped.");
            bool nativeNoOp = frame.NativeChain is { } chain && chain.SequenceEqual(VerifyNativeApparelNoOp(frame));
            Require(frame.Mode == Scribe.mode && (frame.Mode == LoadSaveMode.Saving ? frame.GateCalls == 0 : frame.GateCalls == 1 && (frame.GateSkipped || nativeNoOp)),
                $"The natural converter callback did not take its verified no-conversion branch: {frame.Mode}->{Scribe.mode}, gateCalls={frame.GateCalls}, skipped={frame.GateSkipped}.");
            frame.InConverter = false;
        });
    }

    private static BackCompatibilityConverter[] VerifyNativeApparelNoOp(Frame frame)
    {
        const string version = "1.7.4979 rev109";
        Require(NativeApparelContractFingerprint() == NativeApparelContract
            && frame.Entry is { Owner: { } owner } && owner.GetType() == typeof(Apparel)
            && document?.DocumentElement?["meta"]?["gameVersion"]?.InnerText == version
            && ScribeMetaHeaderUtility.loadedGameVersion == version
            && ScribeMetaHeaderUtility.loadedGameVersionMajor == 1 && ScribeMetaHeaderUtility.loadedGameVersionMinor == 7
            && ScribeMetaHeaderUtility.loadedGameVersionBuild == 4979,
            "The native converter branch does not match the reviewed Apparel read contract.");
        Require(ConversionChain.GetValue(null) is List<BackCompatibilityConverter> chain && chain.Count == NativeConverterTypes.Length,
            "The native converter roster changed.");
        BackCompatibilityConverter[] snapshot = [.. (List<BackCompatibilityConverter>)ConversionChain.GetValue(null)!];
        Require(snapshot.Select(c => c.GetType()).Distinct().Count() == NativeConverterTypes.Length
            && snapshot.All(c => NativeConverterTypes.Contains(c.GetType())), "An unknown or repeated native converter is active.");
        // 1.7 的输入不适用于八个旧版本转换器；Universal 对精确 Apparel 只经过类型不匹配的灯具入口。
        // 只检查实际链和原版入口，不调用任何转换器，也不把该证明扩大到 Pawn、Building 或更早的输入版本。
        MethodInfo applies = AccessTools.Method(typeof(BackCompatibilityConverter), nameof(BackCompatibilityConverter.AppliesToLoadedGameVersion));
        RequireUnpatched(applies, "native converter applicability");
        foreach (MethodInfo call in new CurrentScribeRoots(production!).DirectCalls(applies))
        { RequireUnpatched(call, "native converter applicability helper"); }
        foreach (BackCompatibilityConverter converter in snapshot)
        {
            Type type = converter.GetType();
            RequireUnpatched(AccessTools.Method(type, nameof(BackCompatibilityConverter.AppliesToVersion)), "native converter version");
        }
        // 八个旧版本的谓词均已证明为 false，其 PostExposeData 不会执行；HAR 对其中旧入口的补丁也不会触及本次对象。
        RequireUnpatched(AccessTools.Method(typeof(BackCompatibilityConverter_Universal), nameof(BackCompatibilityConverter.PostExposeData)), "active native converter body");
        RequireUnpatched(AccessTools.Method(typeof(BackCompatibilityConverter_Universal), "SaveLampColor"), "native lamp reader");
        RequireUnpatched(AccessTools.Method(typeof(BackCompatibilityConverter_Universal), "ApplyLampColor"), "native lamp initializer");
        frame.Entry!.NativeSourceVersion = version;
        return snapshot;
    }

    // 校验器只为这条精确服装读取链证明无转换；引擎其余代码的构建变化不影响证明，也不影响生产迁移。
    internal static string NativeApparelContractFingerprint()
    {
        MethodInfo[] methods =
        [
            AccessTools.Method(typeof(BackCompatibilityConverter), nameof(BackCompatibilityConverter.AppliesToLoadedGameVersion)),
            .. NativeConverterTypes.Select(type => AccessTools.Method(type, nameof(BackCompatibilityConverter.AppliesToVersion))),
            AccessTools.Method(typeof(BackCompatibilityConverter_Universal), nameof(BackCompatibilityConverter.PostExposeData)),
            AccessTools.Method(typeof(BackCompatibilityConverter_Universal), "SaveLampColor"),
            AccessTools.Method(typeof(BackCompatibilityConverter_Universal), "ApplyLampColor"),
        ];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', methods.Select(ObservationPatchEvidence.SerializationFingerprint)))));
    }

    private static void AfterApparel(Frame? __state, bool __runOriginal)
    {
        if (__state is not { } frame) { return; }
        try
        {
            if (frame.Entry is not { } entry) { return; }
            Guard(() =>
            {
                CheckFrame(frame);
                Require(__runOriginal && frame.Mode == Scribe.mode && frame.ConverterCalls == 1 && !frame.InConverter, "The full Apparel read/write did not complete its original converter callback.");
                entry.Roster = Roster(entry.Owner, entry.Id, entry.Def);
                if (frame.Mode == LoadSaveMode.LoadingVars) { entry.Loaded = true; }
                if (frame.Mode == LoadSaveMode.ResolvingCrossRefs) { entry.Resolved = true; }
                if (frame.Mode == LoadSaveMode.PostLoadInit) { entry.PostLoaded = true; }
                if (frame.Mode == LoadSaveMode.Saving) { entry.Saved = true; }
            });
        }
        finally { current = frame.Parent; }
    }

    internal static string[] Roster(Apparel owner, string id, string def)
    {
        Require(owner.GetType() == typeof(Apparel) && owner.def?.GetType() == typeof(ThingDef) && owner.def.defName == def && owner.def.thingClass == typeof(Apparel)
            && owner.def.defName + owner.thingIDNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) == id, "Actual Apparel identity/type differs from its source.");
        var actual = (List<ThingComp>?)typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
        List<CompProperties> configured = owner.def.comps;
        bool autoload = def == "Mugirl_AutoloadingSystem";
        Type? autoloadType = autoload ? production?.GetType("Mugirl.Features.WeaponWheel.CompAutoloadingSystem") : null;
        Type[] allowed = autoload ? [typeof(CompForbiddable), typeof(CompColorable), typeof(CompStyleable), autoloadType ?? throw new InvalidOperationException("No actual autoloading comp type.")]
            : [typeof(CompForbiddable), typeof(CompColorable), typeof(CompStyleable)];
        Require(actual is not null && configured is not null && actual.Count == allowed.Length && configured.Count == allowed.Length, "The complete actual and Def component rosters have an unsupported size.");
        Require(allowed.All(type => actual.Count(c => c.GetType() == type) == 1), "An unknown, duplicate, or quality-reading comp is present.");
        for (int i = 0; i < actual.Count; i++)
        {
            Require(actual[i].GetType() == configured[i].compClass && ReferenceEquals(actual[i].props, configured[i]) && ReferenceEquals(actual[i].parent, owner), "Actual component construction does not match the complete Def roster/parent.");
            MethodInfo reader = actual[i].GetType().GetMethod(nameof(ThingComp.PostExposeData))!;
            if (autoload && actual[i].GetType() == autoloadType) { Require(reader.DeclaringType == typeof(ThingComp), "Autoloading introduced an unreviewed saved-data reader."); }
            RequireUnpatched(reader, "actual component");
            VerifyConstruction(actual[i].GetType());
        }
        return [.. actual.Select(c => c.GetType().FullName!)];
    }

    private static void CheckReaders(string stage)
    {
        Require(typeof(Apparel).Module.ModuleVersionId == EngineMvid, "Unreviewed engine for quality reader evidence.");
        var calls = new CurrentScribeRoots(production!);
        foreach (Type type in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Apparel), typeof(ThingComp), typeof(CompForbiddable), typeof(CompColorable), typeof(CompStyleable) })
        { VerifyConstruction(type); }
        RequireUnpatched(typeof(Thing).GetProperty(nameof(Thing.ThingID))!.GetMethod!, stage);
        RequireUnpatched(typeof(ThingWithComps).GetProperty(nameof(ThingWithComps.AllComps))!.GetMethod!, stage);
        foreach (int token in ReaderTokens)
        {
            MethodInfo method = (MethodInfo)ReviewedEngine.ResolveMethod(token)!;
            RequireUnpatched(method, stage);
            foreach (MethodInfo call in calls.DirectCalls(method).Where(m => m.DeclaringType?.Namespace == "Verse" && (m.DeclaringType.Name.StartsWith("Scribe_", StringComparison.Ordinal) || m.DeclaringType == typeof(Scribe))))
            {
                RequireUnpatched(call, stage);
                if (call.IsGenericMethod) { RequireUnpatched(call.GetGenericMethodDefinition(), stage); }
            }
            readers.Add(new JsonObject { ["stage"] = stage, ["type"] = method.DeclaringType?.FullName, ["method"] = method.Name,
                ["mvid"] = EngineMvid.ToString(), ["token"] = method.MetadataToken });
        }
    }

    private static void VerifyConstruction(Type type)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) { RequireUnpatched(constructor, "constructor"); }
        if (type.TypeInitializer is { } initializer) { RequireUnpatched(initializer, "type initializer"); }
        if (typeof(ThingComp).IsAssignableFrom(type))
        {
            MethodInfo initialize = type.GetMethod(nameof(ThingComp.Initialize))!;
            Require(initialize.Module.ModuleVersionId == EngineMvid, "An unreviewed component Initialize can read source data.");
            RequireUnpatched(initialize, "actual Initialize");
        }
        if (type.Assembly == production)
        {
            ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes)!;
            string body = ObservationPatchEvidence.SemanticBody(ctor);
            string expected = "MugirlRace|Mugirl.Features.WeaponWheel.CompAutoloadingSystem::.ctor()\nldarg.0 \ncall Assembly-CSharp|Verse.ThingComp::.ctor()\nret \n";
            Require(type.FullName == "Mugirl.Features.WeaponWheel.CompAutoloadingSystem" && type.TypeInitializer is null && body == expected,
                "The autoloading component constructor is no longer the reviewed empty inherited initialization.");
        }
    }

    private static void RequireUnpatched(MethodBase method, string stage)
    {
        if (Harmony.GetPatchInfo(method) is not { } patches) { return; }
        var seen = new HashSet<(MethodInfo, string)>();
        foreach ((string role, IEnumerable<Patch> list) in new[] { ("prefix", (IEnumerable<Patch>)patches.Prefixes), ("postfix", patches.Postfixes), ("transpiler", patches.Transpilers),
            ("finalizer", patches.Finalizers), ("innerPrefix", patches.InnerPrefixes), ("innerPostfix", patches.InnerPostfixes), ("innerFinalizer", patches.InnerFinalizers) })
        {
            foreach (Patch patch in list)
            {
                bool own = patch.owner == harmony?.Id && hooks.Any(h => h.Target == method && h.Hook == patch.PatchMethod && h.Role == role && h.Priority == patch.priority)
                    && seen.Add((patch.PatchMethod, role));
                if (!own) { throw new InvalidOperationException("An unverified quality reader patch: " + ObservationPatchEvidence.Describe(method, patch, role, stage, null).ToJsonString()); }
                if (Harmony.GetPatchInfo(patch.PatchMethod) is { } nested && nested.Prefixes.Count + nested.Postfixes.Count + nested.Transpilers.Count + nested.Finalizers.Count
                    + nested.InnerPrefixes.Count + nested.InnerPostfixes.Count + nested.InnerFinalizers.Count > 0)
                { throw new InvalidOperationException("The quality observer itself was patched."); }
            }
        }
    }

    /// <summary>供另一个只读模型核验本轮已登记的观察补丁；不信任同owner下的其他补丁。</summary>
    internal static string? ProveRegisteredHook(MethodBase target, Patch patch, string role)
    {
        if (!active || harmony?.Id != ValidationContext.OwnHarmonyId || patch.owner != harmony.Id
            || patch.before.Length != 0 || patch.after.Length != 0) { return null; }
        int matches = 0;
        foreach (var hook in hooks)
        {
            if (hook.Target == target && hook.Hook == patch.PatchMethod && hook.Role == role && hook.Priority == patch.priority) { matches++; }
        }
        if (matches != 1) { return null; }
        foreach (MethodBase method in ObserverContractMethods())
        {
            if (Harmony.GetPatchInfo(method) is { } p && p.Prefixes.Count + p.Postfixes.Count + p.Transpilers.Count + p.Finalizers.Count
                + p.InnerPrefixes.Count + p.InnerPostfixes.Count + p.InnerFinalizers.Count != 0) { return null; }
        }
        if (ObserverContractFingerprint() != ReviewedObserverContract) { return null; }
        return "exact active quality observer registration; reviewed read-only callback/helper IL; no seven-role helper detours";
    }

    internal static string ObserverContractFingerprint()
    {
        var parts = new List<string>();
        string assembly = typeof(UnconsumedQualityObservation).Assembly.GetName().Name!;
        foreach (MethodBase method in ObserverContractMethods())
        {
            if (method.DeclaringType == typeof(UnconsumedQualityObservation) && method.Name == nameof(ProveRegisteredHook)) { continue; }
            MethodBody body = method.GetMethodBody()!;
            var part = new StringBuilder(ObservationPatchEvidence.SemanticBody(method)).Append(method.Attributes).Append('|').Append(method.GetMethodImplementationFlags());
            foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
            {
                part.Append('|').Append(clause.Flags).Append('|').Append(clause.TryOffset).Append('|').Append(clause.TryLength).Append('|').Append(clause.HandlerOffset).Append('|').Append(clause.HandlerLength);
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause) { part.Append('|').Append(clause.CatchType?.FullName); }
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter) { part.Append('|').Append(clause.FilterOffset); }
            }
            parts.Add(part.ToString().Replace(assembly + "|", "ValidationAssembly|", StringComparison.Ordinal)
                .Replace(", " + assembly + ", Version=", ", ValidationAssembly, Version=", StringComparison.Ordinal));
        }
        parts.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));
    }

    private static List<MethodBase> ObserverContractMethods()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var types = new List<Type> { typeof(UnconsumedQualityObservation), typeof(MainSaveDocument), typeof(ReviewedEngine) };
        var result = new List<MethodBase>();
        for (int i = 0; i < types.Count; i++)
        {
            Type type = types[i]; types.AddRange(type.GetNestedTypes(flags));
            result.AddRange(type.GetMethods(flags)); result.AddRange(type.GetConstructors(flags));
            if (type.TypeInitializer is { } initializer && !result.Contains(initializer)) { result.Add(initializer); }
        }
        return result;
    }

    public static string SnapshotJson()
    {
        Require(active, "No active quality observation.");
        Require(Environment.CurrentManagedThreadId == controlThread, "Quality snapshot moved away from its validation control thread.");
        if (candidates.Count > 0) { Guard(() => CheckReaders("after actual save")); }
        if (current is not null) { errors.Add("A quality reader scope did not unwind."); }
        var events = new JsonArray();
        foreach ((string id, string def) in candidates)
        {
            Entry? entry = entries.Values.SingleOrDefault(e => e.Id == id);
            if (entry is null || !entry.Loaded || !entry.Resolved || !entry.PostLoaded || !entry.Saved)
            { errors.Add("The source Apparel did not complete all natural read/save phases: " + id); continue; }
            events.Add(new JsonObject { ["path"] = entry.Path, ["id"] = id, ["def"] = def, ["ownerType"] = typeof(Apparel).FullName,
                ["comps"] = new JsonArray([.. entry.Roster.Select(s => (JsonNode?)JsonValue.Create(s))]),
                ["converterSkipped"] = entry.NativeSourceVersion is null, ["nativeSourceVersion"] = entry.NativeSourceVersion, ["oldValue"] = "Normal" });
        }
        string result = new JsonObject { ["format"] = 1, ["engineMvid"] = EngineMvid.ToString(), ["candidateCount"] = candidates.Count,
            ["events"] = events, ["loadThread"] = loadThread, ["saveThread"] = saveThread, ["controlThread"] = controlThread,
            ["readers"] = readers.DeepClone(), ["errors"] = new JsonArray([.. errors.Select(s => (JsonNode?)JsonValue.Create(s))]) }.ToJsonString();
        Stop();
        return result;
    }

    public static int Apply(XDocument expected, string json)
    {
        JsonObject evidence = JsonNode.Parse(json)!.AsObject();
        Require((int?)evidence["format"] == 1 && (string?)evidence["engineMvid"] == EngineMvid.ToString() && evidence["errors"] is JsonArray { Count: 0 }
            && evidence["events"] is JsonArray events && events.Count == (int?)evidence["candidateCount"], "Quality observation is incomplete or contains errors.");
        var removals = new List<XElement>();
        foreach (JsonNode? raw in evidence["events"]!.AsArray())
        {
            bool skipped = (bool?)raw?["converterSkipped"] == true && raw?["nativeSourceVersion"] is null;
            bool nativeNoOp = (bool?)raw?["converterSkipped"] == false && (string?)raw?["nativeSourceVersion"] == "1.7.4979 rev109"
                && NativeApparelContractFingerprint() == NativeApparelContract
                && expected.Root?.Element("meta")?.Element("gameVersion")?.Value == (string?)raw?["nativeSourceVersion"];
            Require((string?)raw?["ownerType"] == typeof(Apparel).FullName && (skipped || nativeNoOp) && (string?)raw?["oldValue"] == "Normal"
                && Defs.Contains((string?)raw?["def"] ?? string.Empty), "Unknown quality evidence owner.");
            string[] roster = raw?["comps"] is JsonArray comps ? [.. comps.Select(c => c!.GetValue<string>())] : [];
            string[] allowed = (string?)raw?["def"] == "Mugirl_AutoloadingSystem"
                ? [typeof(CompForbiddable).FullName!, typeof(CompColorable).FullName!, typeof(CompStyleable).FullName!, "Mugirl.Features.WeaponWheel.CompAutoloadingSystem"]
                : [typeof(CompForbiddable).FullName!, typeof(CompColorable).FullName!, typeof(CompStyleable).FullName!];
            Require(roster.Length == allowed.Length && allowed.All(type => roster.Count(value => value == type) == 1), "The captured quality roster is unsupported.");
            Require(raw!["path"]!.GetValue<string>().StartsWith("/savegame[1]/game[1]/", StringComparison.Ordinal), "Quality evidence is outside the actual game.");
            XElement owner = expected.XPathSelectElement(raw!["path"]!.GetValue<string>()) ?? throw new InvalidOperationException("The exact observed quality owner path no longer exists.");
            XElement[] quality = [.. owner.Elements("quality")];
            Require(owner.Element("id")?.Value == (string?)raw["id"] && owner.Element("def")?.Value == (string?)raw["def"] && quality.Length == 1 && IsNormal(quality[0])
                && !removals.Contains(quality[0]), "Observed quality evidence does not match the exact independent source leaf.");
            removals.Add(quality[0]);
        }
        foreach (XElement field in removals) { field.Remove(); }
        return removals.Count;
    }

    public static void Stop()
    {
        foreach (var hook in hooks) { harmony?.Unpatch(hook.Target, hook.Hook); }
        hooks.Clear(); entries.Clear(); candidates.Clear(); current = null; document = null; inputPath = null; active = false; production = null; harmony = null;
    }

    private static bool IsNormal(XElement element) => !element.HasAttributes && element.Nodes().ToArray() is [XText text] && text is not XCData && text.Value == "Normal";
    private static void Require([DoesNotReturnIf(false)] bool condition, string text) { if (!condition) { throw new InvalidOperationException(text); } }
    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) { errors.Add(exception.ToString()); }
    }
    private static void BindThread(ref int binding, string phase)
    {
        int actual = Environment.CurrentManagedThreadId;
        int previous = System.Threading.Interlocked.CompareExchange(ref binding, actual, 0);
        Require(previous == 0 || previous == actual, "Quality observation crossed its bound " + phase + " thread.");
    }
    private static void CheckFrame(Frame frame)
        => Require(ReferenceEquals(current, frame) && frame.Thread == Environment.CurrentManagedThreadId && frame.Mode == Scribe.mode,
            "Quality callback left its exact owner/phase/thread scope.");
    private static void Failed(Exception? __exception)
    {
        if (active && __exception is not null) { errors.Add("The natural quality reader threw: " + __exception); }
    }
    private static void ApparelFailed(Exception? __exception, Frame? __state)
    {
        Failed(__exception);
        if (__state is not null && ReferenceEquals(current, __state)) { current = __state.Parent; }
    }
    private sealed class Entry(Apparel owner, string id, string def, string path)
    {
        public Apparel Owner { get; } = owner;
        public string Id { get; } = id;
        public string Def { get; } = def;
        public string Path { get; } = path;
        public string[] Roster { get; set; } = [];
        public bool Loaded { get; set; }
        public bool Resolved { get; set; }
        public bool PostLoaded { get; set; }
        public bool Saved { get; set; }
        public string? NativeSourceVersion { get; set; }
    }
    private sealed class Frame(Frame? parent, LoadSaveMode mode)
    {
        public int Thread { get; } = Environment.CurrentManagedThreadId;
        public Frame? Parent { get; } = parent;
        public LoadSaveMode Mode { get; } = mode;
        public Entry? Entry { get; set; }
        public int ConverterCalls { get; set; }
        public int GateCalls { get; set; }
        public bool GateSkipped { get; set; }
        public bool InConverter { get; set; }
        public BackCompatibilityConverter[]? NativeChain { get; set; }
    }
}
