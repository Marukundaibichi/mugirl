using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>旧行为校准：同一原档在指定构建加载后立即重存，不播种、不 tick、不转换字段。</summary>
internal static class ReferenceResaveSuite
{
    public const string Name = "reference-resave";
    private const string InputFile = "reference-input.rws";
    private const string EntryFile = "reference-entry.json";

    public static ValidationSuite Create() => new(Name,
        "load the supplied source and immediately resave without advancing ticks; retain exact input/output and loaded assembly/method identities",
        SuiteNeeds.MugirlMod, [new ValidationStep("resave", Resave, TimeoutSeconds: 180)], inDefaultRun: false)
        { InAllRun = false, LogPolicy = SuiteLogPolicy.AnyError };

    // 在真实 LoadGame 之前保存输入副本与身份，加载过程中任何源文件改写都不能成为新的‘原本’。
    public static void PrepareEntry(ValidationContext context, string source)
    {
        if (context.Options.Entry != EntryMode.LoadSave || context.Options.Suites.Count != 1 || !string.Equals(context.Options.Suites[0], Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reference calibration requires load entry and reference-resave as the sole suite.");
        }
        string destination = context.ArtifactPath(InputFile) ?? throw new InvalidOperationException("Reference calibration requires an artifacts directory.");
        string resave = GenFilePaths.FilePathForSavedGame(context.Options.ResaveName ?? ValidationContext.SaveName("reference-boot" + context.Options.Boot));
        ReferenceResaveEvidence.RequireDifferentFiles(source, destination);
        ReferenceResaveEvidence.RequireDifferentFiles(source, resave);
        File.Copy(source, destination, overwrite: false);
        var identity = ReferenceResaveEvidence.FileIdentity(source);
        identity["capturedBeforeLoad"] = true;
        identity["beforeLoadFrame"] = context.Frame;
        identity["ticksGame"] = ReferenceResaveEvidence.Tick(SaveXml.Load(destination));
        identity["inputCopySha256"] = ReferenceResaveEvidence.Sha256(destination);
        identity["resavePath"] = resave;
        ArtifactJson.Write(context.ArtifactPath(EntryFile)!, identity);
    }

    private static IEnumerable<Wait> Resave(ValidationContext context)
    {
        string entryPath = context.ArtifactPath(EntryFile) ?? throw new InvalidOperationException("No reference entry artifact directory.");
        JsonObject entry = JsonNode.Parse(File.ReadAllText(entryPath))!.AsObject();
        if (entry["capturedBeforeLoad"]?.GetValue<bool>() != true) { throw new InvalidOperationException("Reference source was not captured before loading."); }
        string source = entry["path"]!.GetValue<string>();
        string sourceHash = entry["sha256"]!.GetValue<string>();
        int inputTick = entry["ticksGame"]!.GetValue<int>();
        var result = new JsonObject { ["format"] = 1, ["input"] = entry.DeepClone(), ["boot"] = context.Options.Boot, ["completed"] = false };
        string reportPath = context.ArtifactPath("reference-resave.json")!;
        ArtifactJson.Write(reportPath, result);

        Assembly assembly = context.RequireTarget().Assembly ?? throw new InvalidOperationException("Reference target assembly was not loaded.");
        result["environment"] = Environment(context, assembly);
        ArtifactJson.Write(reportPath, result);
        if (context.Options.ExpectedModDllSha256 is { } hash && !string.Equals(hash, ReferenceResaveEvidence.Sha256(assembly.Location), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The loaded reference DLL does not match the selected hash.");
        }
        if (context.Options.ExpectedModRoot is { } root && !string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(context.RequireTarget().Mod.RootDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The loaded reference Mod is not the selected source root.");
        }
        if (ReferenceResaveEvidence.Sha256(source) != sourceHash || ReferenceResaveEvidence.Sha256(context.ArtifactPath(InputFile)!) != sourceHash)
        {
            throw new InvalidOperationException("The reference source or preserved input copy changed during loading.");
        }
        int before = Find.TickManager.TicksGame;
        result["beforeSaveTick"] = before;
        result["beforeSaveFrame"] = context.Frame;
        if (before != inputTick || Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
        {
            ArtifactJson.Write(reportPath, result);
            throw new InvalidOperationException($"Reference input tick {inputTick} differs from the paused game tick {before}.");
        }

        var save = new SaveWait(context.Options.ResaveName ?? ValidationContext.SaveName("reference-boot" + context.Options.Boot));
        ReferenceResaveEvidence.RequireDifferentFiles(source, save.FilePath);
        yield return save;
        if (!File.Exists(save.FilePath)) { throw new InvalidOperationException("Reference resave did not produce a file."); }
        XDocument resaved = SaveXml.Load(save.FilePath);
        int written = ReferenceResaveEvidence.Tick(resaved);
        int after = Find.TickManager.TicksGame;
        bool zeroTicks = before == inputTick && after == inputTick && written == inputTick;
        bool inputUnchanged = ReferenceResaveEvidence.Sha256(source) == sourceHash;
        bool containsValidationTypes = resaved.Descendants().Attributes("Class").Any(a => a.Value.StartsWith("Mugirl.GameValidation.", StringComparison.Ordinal));
        File.Copy(save.FilePath, context.ArtifactPath("reference-resaved.rws")!, overwrite: false);
        result["output"] = ReferenceResaveEvidence.FileIdentity(save.FilePath);
        result["afterSaveTick"] = after;
        result["afterSaveFrame"] = context.Frame;
        result["writtenTick"] = written;
        result["zeroTicks"] = zeroTicks;
        result["inputUnchanged"] = inputUnchanged;
        result["thinkTreesArtifact"] = "think-trees.json";
        result["containsValidationTypes"] = containsValidationTypes;
        result["completed"] = zeroTicks && inputUnchanged && !containsValidationTypes;
        ArtifactJson.Write(reportPath, result);
        context.Report.Check(zeroTicks, "reference load and immediate resave advance zero ticks", () => $"input {inputTick}, before {before}, after {after}, written {written}");
        context.Report.Check(inputUnchanged, "reference input bytes remain unchanged", () => source);
        context.Report.Check(!containsValidationTypes,
            "reference output contains no validation types", () => save.FilePath);
        context.Report.Info("reference resaved: " + save.FilePath + "; SHA-256 " + result["output"]!["sha256"]!.GetValue<string>());
    }

    private static JsonObject Environment(ValidationContext context, Assembly target)
    {
        var methods = new JsonArray();
        IEnumerable<Type> types = target.GetTypes().Where(t => typeof(GameComponent).IsAssignableFrom(t) || typeof(MapComponent).IsAssignableFrom(t));
        IEnumerable<MethodInfo> relevant = types.SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "ExposeData" or "LoadedGame" or "FinalizeInit"));
        relevant = relevant.Concat([typeof(Game).GetMethod(nameof(Game.LoadGame))!, typeof(GameDataSaveLoader).GetMethod(nameof(GameDataSaveLoader.SaveGame))!]);
        foreach (MethodInfo method in relevant)
        {
            var patches = new JsonArray();
            if (Harmony.GetPatchInfo(method) is { } info)
            {
                foreach ((string role, IEnumerable<Patch> entries) in new[]
                    {
                        ("prefix", (IEnumerable<Patch>)info.Prefixes), ("postfix", info.Postfixes), ("transpiler", info.Transpilers), ("finalizer", info.Finalizers),
                        ("innerPrefix", info.InnerPrefixes), ("innerPostfix", info.InnerPostfixes), ("innerFinalizer", info.InnerFinalizers),
                    })
                {
                    foreach (Patch patch in entries)
                    {
                        patches.Add(new JsonObject { ["owner"] = patch.owner, ["role"] = role,
                            ["method"] = patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name,
                            ["mvid"] = patch.PatchMethod.Module.ModuleVersionId.ToString(), ["priority"] = patch.priority,
                            ["index"] = patch.index, ["before"] = new JsonArray([.. patch.before.Select(s => (JsonNode?)JsonValue.Create(s))]),
                            ["after"] = new JsonArray([.. patch.after.Select(s => (JsonNode?)JsonValue.Create(s))]) });
                    }
                }
            }
            byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
            methods.Add(new JsonObject { ["type"] = method.DeclaringType?.FullName, ["method"] = method.Name,
                ["mvid"] = method.Module.ModuleVersionId.ToString(), ["token"] = method.MetadataToken,
                ["rawIlSha256"] = il is null ? null : Convert.ToHexString(SHA256.HashData(il)), ["patches"] = patches });
        }
        return new JsonObject { ["gameVersion"] = VersionControl.CurrentVersionStringWithRev, ["gameMvid"] = typeof(Game).Module.ModuleVersionId.ToString(),
            ["modMvid"] = target.ManifestModule.ModuleVersionId.ToString(), ["modAssembly"] = ReferenceResaveEvidence.FileIdentity(target.Location),
            ["targetRoot"] = context.RequireTarget().Mod.RootDir,
            ["modIds"] = new JsonArray([.. LoadedModManager.RunningModsListForReading.Select(m => (JsonNode?)JsonValue.Create(m.PackageIdPlayerFacing))]),
            ["methods"] = methods };
    }
}
