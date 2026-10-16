using System.IO;

namespace Mugirl.GameValidation.Core;

/// <summary>夹具播种的临时时间驱动：使用原版调试开关解除战斗常速限制，仍逐个执行所有 tick。</summary>
internal sealed class CombatSpeedScope : IDisposable
{
    public const string Marker = ".mugirl-gamevalidation-savedata";
    private readonly bool previous;
    private bool disposed;

    private CombatSpeedScope()
    {
        previous = DebugViewSettings.neverForceNormalSpeed;
        DebugViewSettings.neverForceNormalSpeed = true;
    }

    public static CombatSpeedScope? Enter(ValidationContext context)
    {
        if (!string.Equals(context.Options.SuiteOption("sweep.ignoreCombatSlowdown", "false"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!GenCommandLine.CommandLineArgPassed(ValidationOptions.CommandLineSwitch)
            || !context.Options.Suites.Contains("sweep", StringComparer.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, Marker)))
        {
            throw new InvalidOperationException("combat-speed override requires -mugirlvalidate, the explicit sweep suite, and its temporary savedata marker");
        }

        context.Report.Info("fixture simulation uses the vanilla neverForceNormalSpeed debug switch; every requested tick still runs");
        return new CombatSpeedScope();
    }

    public void Dispose()
    {
        if (!disposed)
        {
            DebugViewSettings.neverForceNormalSpeed = previous;
            disposed = true;
        }
    }
}
