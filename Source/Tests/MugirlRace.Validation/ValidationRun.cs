using System.Runtime.InteropServices;
using MugirlRace.Validation.Checks;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation;

/// <summary>
/// 按固定顺序运行全部检查，顺序本身是正确性的一部分：先是只读文件与元数据的检查，再是数据检查（只检查类型，不运行任何静态构造
/// 函数），最后才是执行 Mod 代码的检查（Prepare/TargetMethod、Transpiler、JIT、Harmony）。加载游戏程序集的那一刻起就捕获日志。
/// </summary>
internal static class ValidationRun
{
    public static int Execute(ValidationOptions options, ModCatalog catalog)
    {
        var report = new Report();
        Console.WriteLine("MugirlRace offline validation");
        report.Environment("runtime", RuntimeInformation.FrameworkDescription
            + (AppContext.GetData("WaylithClr.JitPatchableAssemblies") is string patchable ? $" (WaylithClr options active, JitPatchableAssemblies={patchable})" : " (standard .NET)"));
        report.Environment("game", $"RimWorld {catalog.GameVersion} at {options.GameDir}");
        report.Environment("mod root", options.ModRoot);
        report.Environment("source", options.SourceDir);
        report.Environment("assumed active", options.AssumeActive.Count == 0 ? "(none)" : string.Join(", ", options.AssumeActive));
        if (!File.Exists(options.ModAssembly))
        {
            throw new EnvironmentException($"The assembly to validate does not exist: {options.ModAssembly}");
        }

        report.Environment("assembly under test", $"{options.ModAssembly} (sha256 {ArtifactChecks.Sha256(options.ModAssembly)})"
            + (options.ModPdb is null ? " (no PDB: source-folder rules cannot locate code)" : ""));
        report.Environment("baselines", options.BaselineDir + (options.UpdateBaselines ? " (updating golden lists)" : ""));

        // 第 1 阶段：只读文件与元数据。
        ArtifactChecks.Run(report, options, catalog);
        IEnumerable<string> searchDirectories = new[] { options.GameDir, options.HarmonyDir, Path.GetDirectoryName(typeof(object).Assembly.Location)! }
            .Concat(catalog.DependencyAssemblies().Values.Select(path => Path.GetDirectoryName(path)!));
        using var world = new MetadataWorld(options.ModAssembly, options.ModPdb, searchDirectories);
        UnityBoundaryCheck.Run(report, options, world);
        WaylithNamespaceCheck.Run(report, world);
        BannedSymbolsCheck.Run(report, options, world);
        ArchitectureCheck.Run(report, options, world);
        SourceConventionChecks.Run(report, options, world);
        var modFlow = new ModuleStringFlow(world.Mod);
        StructureChecks.Run(report, options, world);
        ScribeDefaultsCheck.Run(report, options, modFlow);

        // 第 2 阶段：加载游戏、Harmony、依赖与 Mod（日志捕获随游戏程序集开始）。
        Console.WriteLine();
        Console.WriteLine("== Loading RimWorld 1.7, dependencies and the mod");
        GameRuntime runtime = GameRuntime.Load(options, catalog, report);
        LogCapture.AttributeTo(runtime.Dependencies.Skip(1).Append(runtime.Mod));
        report.PhaseChanged = phase => LogCapture.Phase = phase;

        // 第 3 阶段：数据（只检查类型，不运行游戏或 Mod 代码）。
        ContentModel? content = null;
        report.Run("C.content", "Load the Defs, patches, languages, textures and sounds of every active pack", recorder =>
        {
            content = ContentModel.Build(catalog, runtime, options.GameDir);
            recorder.Summary = $"{content.Defs.Count} defs, {content.Patches.Results.Count} patch operations, {content.Textures.Count} textures, {content.Sounds.Count} sounds";
        });
        if (content is not null)
        {
            Func<Mono.Cecil.MethodDefinition, string?> sourceOf = method => world.Sources.DocumentOf(method) is { } document ? SourceMap.ProjectRelative(document, options.SourceDir) : null;
            IReadOnlyList<LookupSite> sites = CodeLookups.Collect(modFlow, content.Types, sourceOf);
            AllowList dynamic = AllowList.Load(options.BaselineDir, LookupChecks.DynamicFile);
            dynamic.AddsMissingForReview = options.UpdateBaselines;
            XmlChecks.Run(report, content);
            DefReferenceCheck.Run(report, content);
            DefOfCheck.Run(report, world, content);
            IReadOnlySet<string> legacyNames = Legacy.LegacyCorpus.Load(SaveUpgradeCheck.CorpusDirectory(options)) is { } corpus
                ? corpus.LegacyNames()
                : new HashSet<string>(StringComparer.Ordinal);
            LookupChecks.Run(report, content, sites, dynamic, modFlow.Module, legacyNames);
            KeyedParityCheck.Run(report, content);
            LanguageFileChecks.Run(report, content);
            ContentShadowingCheck.Run(report, content);
            DefInjectedPathCheck.Run(report, world, content);
            TextureCheck.Run(report, content, sites, dynamic);
            SoundCheck.Run(report, content);
            ShaderCheck.Run(report, options, content, sites, dynamic, runtime.Mod);
            ContentContractChecks.Run(report, options, content);
            ReflectionNameCheck.Run(report, modFlow, runtime, content.Types, dynamic, sourceOf);
            PublicApiCheck.Run(report, options, world, content);
            if (dynamic.WritePendingReview() is var added and > 0)
            {
                Console.WriteLine($"   Baselines/{LookupChecks.DynamicFile}: {added} entr(y/ies) appended with a \"{AllowList.ReviewPrefix}\" reason; replace each after review.");
            }
        }

        // 第 4 阶段：运行 Mod 代码（升级清单 ToJson、补丁解析、Transpiler）、JIT 绑定、Harmony 打补丁再撤销。
        if (content is not null)
        {
            SaveUpgradeCheck.Run(report, options, world, modFlow, runtime, content);
        }

        StaticAssetFieldCheck.Run(report, runtime);
        PatchPlan plan = PatchTargetCheck.Run(report, options, runtime);
        PatchMetadataCheck.Run(report, runtime);
        TranspilerCheck.Run(report, options, runtime, plan);
        JitCheck.Run(report, runtime);
        HarmonyApplyCheck.Run(report, options, runtime, plan);
        LogCheck.Run(report, runtime, options.BaselineDir);

        report.PrintSummary();
        if (options.ReportPath is not null)
        {
            report.WriteJson(options.ReportPath);
            Console.WriteLine("Report: " + options.ReportPath);
        }

        return report.AnyFailure ? ExitCodes.Failed : ExitCodes.Passed;
    }
}
