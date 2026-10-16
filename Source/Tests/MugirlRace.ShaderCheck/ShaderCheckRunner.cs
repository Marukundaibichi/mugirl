using System.Runtime.CompilerServices;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>按依赖顺序运行全部检查并返回退出码。</summary>
internal static class ShaderCheckRunner
{
    /// <summary>运行检查。不内联：它是第一个接触引擎类型的方法。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(CheckPaths paths)
    {
        var report = new CheckReport();
        Console.WriteLine("Mugirl Race ShaderCheck");
        Console.WriteLine($"  mod:     {paths.ModDir}");
        Console.WriteLine($"  game:    {paths.GameDir}");
        Console.WriteLine($"  Waylith: {GameAssemblyResolver.VersionOf(typeof(Shader).Assembly)}");

        EngineLogCapture log = EngineLogCapture.Install();
        DefXmlIndex index = DefXmlIndex.Load(paths, report);
        List<ShaderDefEntry> shaderDefs = [.. index.OfType("ShaderDef").Select(ShaderDefEntry.Read)];
        List<ShaderDefEntry> ownShaderDefs = [.. shaderDefs.Where(def => def.IsOwn)];
        List<MaterialDefEntry> ownMaterialDefs = [.. index.OfType("MaterialDef").Where(def => def.IsOwn).Select(MaterialDefEntry.Read)];
        if (ownShaderDefs.Count == 0)
        {
            report.Fail("No ShaderDefs found in the mod's 1.7 Defs.");
        }

        GlslSourceChecks.Run(paths, ownShaderDefs, report);

        var programs = new ShaderProgramChecks(paths, log, report);
        programs.ConfigureSourceRoots(index.GamePackages);
        programs.RegisterVanilla(shaderDefs.Where(def => !def.IsOwn));
        programs.CheckOwn(ownShaderDefs);

        ShaderLabParityChecks.Run(programs.Own, report);
        SpirvInterfaceChecks.Run(programs.Own, report);
        NegativeControlChecks.Run(programs, report);

        // 负对照故意编译失败，引擎可能为它记录日志；这些日志不属于本模组的着色器。
        log.Drain();
        MaterialDefChecks.Run(paths, ownMaterialDefs, programs.Shaders, report);
        ShaderTypeChecks.Run(paths, index, programs.Shaders, report);
        CSharpReferenceChecks.Run(
            paths,
            programs.Shaders,
            ownShaderDefs.Select(def => def.DefName).ToHashSet(StringComparer.Ordinal),
            ownMaterialDefs.Select(def => def.DefName).ToHashSet(StringComparer.Ordinal),
            report);

        foreach (CapturedLogEntry problem in log.Drain().Where(entry => entry.IsProblem))
        {
            report.Fail("engine reported " + problem);
        }

        return report.Finish();
    }
}
