using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Mugirl.Core;
using Mugirl.Features.WeaponWheel;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>启动检查：加载的是开发目录中的 Mod 与指定的 DLL，全部 Harmony 补丁已安装，加载日志中没有提到本 Mod 的问题。</summary>
/// <remarks>
/// 本机同时装有创意工坊发布的副本（packageId 也是 HAR.MugirlRace，RimWorld 给它加 _steam 后缀），
/// 所以必须核对 RootDir，不能只看 packageId。
/// </remarks>
internal static class StartupSuite
{
    public const string Name = SuiteCatalog.StartupSuiteName;

    private const string StylingStationType = "AlienRace.StylingStation";
    private const string StylingStationMethod = "DoRaceTabs";
    private const string SettingsCategoryKey = "Mugirl.Settings.Category";

    public static ValidationSuite Create() => new(
        Name,
        "mod loaded from the development folder, one MugirlRace assembly, every Harmony patch class applied (incl. the deferred HAR StylingStation patch), no load-time warning or error mentioning the mod, core Defs present",
        SuiteNeeds.MugirlMod,
        [
            new ValidationStep("mod-root", ModRoot),
            new ValidationStep("assembly", AssemblyIdentity),
            new ValidationStep("harmony", HarmonyPatches),
            new ValidationStep("load-log", LoadLog),
            new ValidationStep("defs", Defs),
        ]);

    private static IEnumerable<Wait> ModRoot(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        string root = NormalizeFolder(target.Mod.RootDir);
        report.Info("HAR.MugirlRace root: " + root);
        if (ctx.Options.ExpectedModRoot is { } expected)
        {
            report.Check(string.Equals(root, NormalizeFolder(expected), StringComparison.OrdinalIgnoreCase),
                "HAR.MugirlRace is loaded from the development folder",
                () => $"loaded from {root}, expected {expected}; the published Workshop copy has the same packageId");
        }
        else
        {
            report.Skip("development folder", "the script passed no expected mod folder");
        }

        report.Check(!target.Mod.ModMetaData.OnSteamWorkshop, "the active copy is not the Workshop item", () => "OnSteamWorkshop is true");
        report.Check(string.Equals(target.Mod.PackageId, MugirlTarget.DefaultPackageId, StringComparison.OrdinalIgnoreCase),
            "the active copy has the plain packageId", () => target.Mod.PackageId);
        yield break;
    }

    private static IEnumerable<Wait> AssemblyIdentity(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        if (target.Assembly is not { } assembly)
        {
            report.Fail("MugirlRace.dll is loaded", $"RimWorld loaded no MugirlRace assembly for {target.Mod.RootDir} (missing from 1.7/Assemblies, or rejected; see the load-log step)");
            yield break;
        }

        Assembly[] copies = [.. AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == MugirlTarget.AssemblyName)];
        report.Check(copies.Length == 1, "exactly one MugirlRace assembly is loaded",
            () => string.Join(", ", copies.Select(a => a.Location.Length > 0 ? a.Location : "(no location)")));

        string expectedFolder = Path.Combine(NormalizeFolder(target.Mod.RootDir), "1.7", "Assemblies");
        string actualFolder = NormalizeFolder(Path.GetDirectoryName(assembly.Location) ?? string.Empty);
        report.Check(string.Equals(actualFolder, expectedFolder, StringComparison.OrdinalIgnoreCase),
            "MugirlRace.dll comes from the mod's 1.7/Assemblies", () => assembly.Location);

        if (ctx.Options.ExpectedModDllSha256 is { } expectedHash && File.Exists(assembly.Location))
        {
            string actualHash = Sha256(assembly.Location);
            report.Check(string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase),
                "it is the DLL the script selected", () => $"SHA-256 {actualHash}, expected {expectedHash}");
        }

        report.Check(ReferenceEquals(typeof(MugirlMod).Assembly, assembly), "the test mod binds to the loaded assembly",
            () => typeof(MugirlMod).Assembly.Location);
        MugirlMod[] handles = [.. LoadedModManager.ModHandles.OfType<MugirlMod>()];
        report.Check(handles.Length == 1 && handles[0].Content == target.Mod, "one MugirlMod instance owns HAR.MugirlRace",
            () => $"{handles.Length} instance(s)");

        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        report.Info($"MugirlRace {assembly.GetName().Version}, {informational}, MVID {assembly.ManifestModule.ModuleVersionId}, {new FileInfo(assembly.Location).Length} bytes");
    }

    private static IEnumerable<Wait> HarmonyPatches(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        IReadOnlyList<Type> classes = HarmonyBootstrap.PatchClasses;
        var applied = new HashSet<Type>();
        int patchedMethods = 0;
        bool stylingStationPatched = false;
        foreach (MethodBase original in Harmony.GetAllPatchedMethods())
        {
            if (Harmony.GetPatchInfo(original) is not { } info)
            {
                continue;
            }

            bool owned = false;
            foreach (HarmonyLib.Patch patch in AllPatches(info))
            {
                if (patch.owner != HarmonyBootstrap.HarmonyId)
                {
                    continue;
                }

                owned = true;
                if (patch.PatchMethod?.DeclaringType is { } declaring)
                {
                    applied.Add(declaring);
                }
            }

            if (owned)
            {
                patchedMethods++;
                stylingStationPatched |= original.Name == StylingStationMethod && original.DeclaringType?.FullName == StylingStationType;
            }
        }

        report.Info($"{classes.Count} patch classes; {HarmonyBootstrap.HarmonyId} patches {patchedMethods} methods from {applied.Count} classes");
        report.Check(classes.Count > 0, "the bootstrap found patch classes", () => "HarmonyBootstrap.PatchClasses is empty");

        int missing = 0;
        foreach (Type patchClass in classes)
        {
            if (applied.Contains(patchClass))
            {
                continue;
            }

            string name = patchClass.FullName ?? patchClass.Name;
            string? declined = PrepareDeclines(patchClass, out string? prepareFailure);
            if (declined is not null)
            {
                report.Skip(name, declined);
            }
            else
            {
                missing++;
                report.Fail(name, prepareFailure ?? "no patch owned by " + HarmonyBootstrap.HarmonyId + " comes from this class");
            }
        }

        if (missing == 0)
        {
            report.Pass("every patch class whose Prepare() allows it is applied");
        }

        foreach (Type patchClass in classes.Where(c => c.GetCustomAttribute<MugirlPatchAttribute>(inherit: false)?.Deferred == true))
        {
            report.Check(applied.Contains(patchClass), "deferred patch class applied after loading: " + patchClass.Name, () => "not applied");
        }

        report.Check(stylingStationPatched, "the deferred HAR StylingStation.DoRaceTabs patch is installed",
            () => $"{StylingStationType}.{StylingStationMethod} has no patch owned by {HarmonyBootstrap.HarmonyId}");
        yield break;
    }

    private static IEnumerable<Wait> LoadLog(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        List<CapturedLog> load = [.. ctx.Logs.Where(e => e.Scope == "load" && !ctx.IsReviewed(e))];
        int other = 0;
        int mod = 0;
        foreach (CapturedLog entry in load)
        {
            ctx.MarkReviewed(entry);
            if (!entry.IsWarningOrWorse || LogCapture.IsHostNoise(entry))
            {
                continue;
            }

            if (entry.MentionsMod)
            {
                mod++;
                report.Fail("load log", entry.Describe());
            }
            else
            {
                other++;
            }
        }

        if (mod == 0)
        {
            report.Pass("no load-time warning or error mentions the mod", $"{other} warning/error line(s) from other sources");
        }

        yield break;
    }

    private static IEnumerable<Wait> Defs(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        ThingDef? race = GameFixtures.MugirlRace;
        if (race is null)
        {
            report.Fail("the Mugirl race ThingDef is loaded", "no ThingDef named " + GameFixtures.MugirlRaceDefName);
            yield break;
        }

        report.Check(race.race is { Humanlike: true } && typeof(Pawn).IsAssignableFrom(race.thingClass),
            "Mugirl is a humanlike pawn race", () => $"race {race.race}, thingClass {race.thingClass}");
        report.Check(race.GetType().FullName == "AlienRace.ThingDef_AlienRace", "Mugirl is a HAR alien race", () => race.GetType().FullName ?? "?");
        report.Check(GameFixtures.MugirlColonistKind() is not null, "a PawnKindDef generates Mugirl pawns", () => "none found");
        report.Check(race.inspectorTabsResolved?.Any(tab => tab is ITab_Pawn_WeaponWheel) == true,
            "the weapon wheel ITab is resolved on the race", () => "missing from inspectorTabsResolved");
        report.Check(SettingsCategoryKey.CanTranslate(), "the settings category is translated in the active language",
            () => $"{SettingsCategoryKey} missing in {LanguageDatabase.activeLanguage?.folderName}");
        report.Info($"{ctx.RequireTarget().Mod.AllDefs.Count()} Defs come from HAR.MugirlRace");
    }

    /// <summary>
    /// 按 Harmony 的约定调用补丁类的 Prepare()（类级调用，original 为 null）。返回 false 时给出跳过原因；
    /// 没有 Prepare、返回 true 或抛异常时返回 null，异常写入 <paramref name="failure"/>。
    /// </summary>
    private static string? PrepareDeclines(Type patchClass, out string? failure)
    {
        failure = null;
        MethodInfo? prepare = patchClass
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == "Prepare" || m.IsDefined(typeof(HarmonyPrepare), inherit: false));
        if (prepare is null || prepare.ReturnType != typeof(bool))
        {
            return null;
        }

        object?[] arguments = [.. prepare.GetParameters().Select(p => p.ParameterType == typeof(Harmony) ? new Harmony(HarmonyBootstrap.HarmonyId) : (object?)null)];
        try
        {
            return prepare.Invoke(null, arguments) is false ? "Prepare() returns false (optional integration not active)" : null;
        }
        catch (TargetInvocationException ex)
        {
            failure = "Prepare() threw: " + (ex.InnerException ?? ex);
            return null;
        }
    }

    private static IEnumerable<HarmonyLib.Patch> AllPatches(Patches info)
        => info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
            .Concat(info.InnerPrefixes).Concat(info.InnerPostfixes).Concat(info.InnerFinalizers);

    private static string NormalizeFolder(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
