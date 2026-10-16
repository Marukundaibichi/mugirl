using HarmonyLib;

namespace Mugirl.GameValidation.Core;

// 仅提供被链接观察器的启动入口类型形状；离线测试通过私有 Start 运行真实 Harmony/Scribe，不启动游戏会话。
internal sealed class ValidationContext
{
    public const string OwnHarmonyId = "MugirlRace.GameValidation";
    public Harmony Harmony { get; } = new(OwnHarmonyId);
    public ObservationOptions Options { get; } = new();
    public ObservationTarget Target { get; } = new();
}

internal sealed class ObservationTarget
{
    public System.Reflection.Assembly Assembly => typeof(Mugirl.Features.Milk.CompMilkProduction).Assembly;
}

internal sealed class ObservationOptions
{
    public List<string> Suites { get; } = [];
}

internal static class SuiteCatalog
{
    public const string UpgradeSuiteName = "upgrade";
}
