using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using HarmonyLib;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>
/// 旧构建在 1.7 游戏上的可行性闸门（tools/New-SaveFixture.ps1 的第一次启动）：被测构建的程序集是否加载、哪些类型加载失败、
/// 哪些 Harmony 补丁类没有生效、加载期有哪些提到它的警告与错误。结论写进 gate.json，进入夹具清单。
/// </summary>
/// <remarks>
/// 只记录，不评判：旧构建为 1.6 编译，1.7 上出现补丁失败是预期中的事实，夹具清单如实保留。是否可以继续播种由脚本根据
/// 游戏能否开局、存读档能否往返（plumbing 套件）决定。与构建无关，只用反射与原版 API。
/// </remarks>
internal static class FixtureGateSuite
{
    public const string Name = "fixture-gate";

    /// <summary>结果文件（相对 <see cref="ValidationOptions.ArtifactsDir"/>）。</summary>
    public const string ResultFile = "gate.json";

    private const int MaxListed = 200;

    public static ValidationSuite Create()
    {
        var gate = new JsonObject { ["format"] = 1 };
        return new ValidationSuite(
            Name,
            "feasibility gate of a temporary legacy build: assembly identity, types that fail to load, Harmony patch classes not applied, load-time problems naming the build",
            SuiteNeeds.MugirlMod,
            [
                new ValidationStep("identity", ctx => Identity(ctx, gate)),
                new ValidationStep("types", ctx => Types(ctx, gate)),
                new ValidationStep("harmony", ctx => HarmonyPatches(ctx, gate)),
                new ValidationStep("load-log", ctx => LoadLog(ctx, gate)),
            ],
            inDefaultRun: false)
        {
            InAllRun = false,
        };
    }

    private static IEnumerable<Wait> Identity(ValidationContext ctx, JsonObject gate)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        var identity = new JsonObject
        {
            ["packageId"] = target.PackageId,
            ["root"] = target.Mod.RootDir,
            ["gameVersion"] = VersionControl.CurrentVersionStringWithRev,
            ["mods"] = new JsonArray([.. LoadedModManager.RunningModsListForReading.Select(m => (JsonNode?)JsonValue.Create(m.PackageIdPlayerFacing))]),
        };
        gate["identity"] = identity;
        if (!report.Check(target.Assembly is not null, "the build's MugirlRace assembly is loaded", () => "RimWorld loaded no MugirlRace assembly for " + target.Mod.RootDir))
        {
            Write(ctx, gate);
            yield break;
        }

        Assembly assembly = target.Assembly!;
        identity["assembly"] = assembly.Location;
        identity["assemblyVersion"] = assembly.GetName().Version?.ToString();
        identity["mvid"] = assembly.ManifestModule.ModuleVersionId.ToString();
        if (File.Exists(assembly.Location))
        {
            using FileStream stream = File.OpenRead(assembly.Location);
            identity["sha256"] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        identity["defs"] = target.AllDefs().Count;
        report.Info($"{assembly.GetName().Name} {assembly.GetName().Version}, MVID {assembly.ManifestModule.ModuleVersionId}, {identity["defs"]} Defs");
        Write(ctx, gate);
    }

    private static IEnumerable<Wait> Types(ValidationContext ctx, JsonObject gate)
    {
        ValidationReport report = ctx.Report;
        if (ctx.RequireTarget().Assembly is not { } assembly)
        {
            report.Skip("types", "the assembly is not loaded");
            yield break;
        }

        var failures = new JsonArray();
        int loaded;
        try
        {
            loaded = assembly.GetTypes().Length;
        }
        catch (ReflectionTypeLoadException ex)
        {
            loaded = ex.Types.Count(t => t is not null);
            foreach (string message in ex.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct(StringComparer.Ordinal).Take(MaxListed))
            {
                failures.Add(message);
            }
        }

        gate["types"] = new JsonObject { ["loaded"] = loaded, ["failures"] = failures };
        report.Info($"{loaded} types load; {failures.Count} distinct loader failure(s)");
        foreach (JsonNode? failure in failures.Take(10))
        {
            report.Info("type load failure: " + failure);
        }

        report.Pass("type loading recorded");
        Write(ctx, gate);
    }

    private static IEnumerable<Wait> HarmonyPatches(ValidationContext ctx, JsonObject gate)
    {
        ValidationReport report = ctx.Report;
        if (ctx.RequireTarget().Assembly is not { } assembly)
        {
            report.Skip("harmony", "the assembly is not loaded");
            yield break;
        }

        var applied = new HashSet<Type>();
        var owners = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int patchedMethods = 0;
        foreach (MethodBase original in Harmony.GetAllPatchedMethods())
        {
            if (Harmony.GetPatchInfo(original) is not { } info)
            {
                continue;
            }

            bool fromBuild = false;
            foreach (HarmonyLib.Patch patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
            {
                if (patch.PatchMethod?.DeclaringType is { } declaring && declaring.Assembly == assembly)
                {
                    fromBuild = true;
                    applied.Add(declaring);
                    owners[patch.owner] = owners.GetValueOrDefault(patch.owner) + 1;
                }
            }

            if (fromBuild)
            {
                patchedMethods++;
            }
        }

        List<Type> declared = [.. LoadableTypes(assembly).Where(IsHarmonyPatchClass).OrderBy(t => t.FullName, StringComparer.Ordinal)];
        List<string> notApplied = [.. declared.Where(t => !applied.Contains(t)).Select(t => t.FullName ?? t.Name)];
        gate["harmony"] = new JsonObject
        {
            ["patchedMethods"] = patchedMethods,
            ["appliedClasses"] = applied.Count,
            ["declaredPatchClasses"] = declared.Count,
            ["owners"] = new JsonObject([.. owners.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value))]),
            ["notApplied"] = new JsonArray([.. notApplied.Take(MaxListed).Select(n => (JsonNode?)JsonValue.Create(n))]),
        };
        report.Info($"{patchedMethods} methods patched from {applied.Count} classes of the build (owners {string.Join(", ", owners.Select(p => $"{p.Key} {p.Value}"))}); "
            + $"{notApplied.Count} of {declared.Count} [HarmonyPatch] classes not applied");
        foreach (string name in notApplied.Take(10))
        {
            report.Info("not applied: " + name);
        }

        report.Pass("Harmony patching recorded");
        Write(ctx, gate);
    }

    private static IEnumerable<Wait> LoadLog(ValidationContext ctx, JsonObject gate)
    {
        ValidationReport report = ctx.Report;
        var problems = new JsonArray();
        int errors = 0;
        int warnings = 0;
        foreach (CapturedLog entry in ctx.Logs.Where(e => e.Scope == "load" && e.IsWarningOrWorse && !LogCapture.IsHostNoise(e)))
        {
            if (!entry.MentionsMod)
            {
                continue;
            }

            // 已记录进闸门结果：会话结束时不再按“提到本 Mod”判为失败。
            ctx.MarkReviewed(entry);
            if (entry.IsErrorOrWorse)
            {
                errors++;
            }
            else
            {
                warnings++;
            }

            if (problems.Count < MaxListed)
            {
                problems.Add(entry.Describe());
            }
        }

        gate["loadLog"] = new JsonObject { ["errors"] = errors, ["warnings"] = warnings, ["entries"] = problems };
        report.Info($"{errors} error(s) and {warnings} warning(s) at load time name the build");
        foreach (JsonNode? problem in problems.Take(10))
        {
            report.Info("load: " + problem);
        }

        report.Pass("load-time problems recorded");
        Write(ctx, gate);
        yield break;
    }

    private static bool IsHarmonyPatchClass(Type type)
    {
        try
        {
            return type.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            // 特性引用的类型在 1.7 上无法解析时，该类本来就不可能被打补丁，不计入。
            return false;
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static void Write(ValidationContext ctx, JsonObject gate)
    {
        if (ctx.ArtifactPath(ResultFile) is { } path)
        {
            ArtifactJson.Write(path, gate);
        }
    }
}
