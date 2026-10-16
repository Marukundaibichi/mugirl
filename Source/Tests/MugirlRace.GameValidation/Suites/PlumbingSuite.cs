using System.IO;
using System.Reflection;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>框架自检：只用原版内容检查脚本与测试模组的管线（帧、Tick、日志捕获、存读档、截图、窗口探针）。</summary>
/// <remarks>
/// tools/Test-InGame.ps1 -Plumbing 只加载 Harmony 与测试模组运行本套件，用来调试脚本，而不加载正在迁移中的雪牛娘。
/// 不属于 "default" 与 "all" 选择。
/// </remarks>
internal static class PlumbingSuite
{
    public const string Name = "plumbing";

    private const string ProbeText = "GameValidation log-capture probe ";

    public static ValidationSuite Create() => new(
        Name,
        "framework self-test on vanilla content: environment, frames and ticks, log capture, save/load round trip, screenshot, window draw probe",
        SuiteNeeds.None,
        [
            new ValidationStep("environment", EnvironmentStep),
            new ValidationStep("time", TimeControl),
            new ValidationStep("log-capture", LogCaptureProbe),
            new ValidationStep("save-load", SaveLoad, TimeoutSeconds: 360),
            new ValidationStep("screenshot", Screenshot),
            new ValidationStep("window", WindowProbe),
        ],
        inDefaultRun: false)
    {
        InAllRun = false,
    };

    private static IEnumerable<Wait> EnvironmentStep(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        Assembly own = typeof(PlumbingSuite).Assembly;
        Assembly[] copies = [.. AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == own.GetName().Name)];
        report.Check(copies.Length == 1, "the test assembly is loaded once", () => string.Join(", ", copies.Select(a => a.Location)));
        string folder = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(own.Location)) ?? string.Empty);
        report.Check(folder.StartsWith("__MugirlRace_GameValidation_", StringComparison.Ordinal),
            "the test assembly comes from the temporary test mod folder", () => own.Location);
        report.Info($"screen {UI.screenWidth}x{UI.screenHeight}, dev mode {Prefs.DevMode}, language {LanguageDatabase.activeLanguage?.folderName}");
        report.Check(ctx.Map.mapPawns.FreeColonistsSpawnedCount > 0, "the quick-test map has colonists",
            () => "no free colonist is spawned on " + ctx.Map);
        report.Check(Find.TickManager.CurTimeSpeed == TimeSpeed.Paused, "the session keeps the game paused",
            () => "time speed " + Find.TickManager.CurTimeSpeed);
        yield break;
    }

    private static IEnumerable<Wait> TimeControl(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        int start = Find.TickManager.TicksGame;
        yield return Wait.Frames(5);
        report.Check(Find.TickManager.TicksGame == start, "no ticks pass while paused",
            () => $"{Find.TickManager.TicksGame - start} ticks passed during 5 paused frames");

        yield return Wait.Ticks(600);
        int advanced = Find.TickManager.TicksGame - start;
        report.Check(advanced >= 600, "Wait.Ticks advances the game", () => $"only {advanced} of 600 ticks");

        yield return Wait.Frames(1);
        int paused = Find.TickManager.TicksGame;
        yield return Wait.Frames(3);
        report.Check(Find.TickManager.TicksGame == paused && Find.TickManager.CurTimeSpeed == TimeSpeed.Paused,
            "the game is paused again after the tick wait",
            () => $"speed {Find.TickManager.CurTimeSpeed}, {Find.TickManager.TicksGame - paused} ticks in 3 frames");
    }

    private static IEnumerable<Wait> LogCaptureProbe(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        string marker = ProbeText + Guid.NewGuid().ToString("N");
        ctx.ExpectLog(entry => entry.Message.Contains(marker, StringComparison.Ordinal));
        Log.Warning(marker);
        yield return Wait.Until(() => ctx.StepLogs.Any(e => e.Message.Contains(marker, StringComparison.Ordinal)), 10, "the probe warning in LogStream");

        CapturedLog? seen = ctx.StepLogs.FirstOrDefault(e => e.Message.Contains(marker, StringComparison.Ordinal));
        if (seen is null)
        {
            report.Fail("a Verse warning arrives through Waylith's LogStream", "not captured within 10 s");
        }
        else
        {
            report.Pass("a Verse warning arrives through Waylith's LogStream", $"category {seen.Category}");
            report.Check(seen.IsWarningOrWorse && seen.DuringStep && seen.Scope == report.Scope,
                "it is captured as a warning of this step", () => $"{seen.Level} [{seen.Category}] in {seen.Scope}");
        }

        report.Check(Log.Messages.Any(m => m.text is not null && m.text.Contains(marker, StringComparison.Ordinal)),
            "the same warning is in Verse's message list", () => "missing from Log.Messages");

        // 归因规则：测试模组自己的名字不算提到被测 Mod；雪牛娘的 ThingID（defName + 数字）算。
        report.Check(!LogCapture.MentionsMod("at Mugirl.GameValidation.Core.ValidationSession.Update () in __MugirlRace_GameValidation_1a2b3c4d"),
            "the test mod's own names are not attributed to the mod", () => "attributed");
        report.Check(LogCapture.MentionsMod("Exception ticking Mugirl12345: System.NullReferenceException"),
            "a Mugirl ThingID in a message is attributed to the mod", () => "not attributed");
    }

    private static IEnumerable<Wait> SaveLoad(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        HashSet<int> colonists = [.. ctx.Map.mapPawns.FreeColonistsSpawned.Select(p => p.thingIDNumber)];
        int savedTick = Find.TickManager.TicksGame;
        var save = new SaveWait(ValidationContext.SaveName(Name));
        yield return save;
        if (!report.Check(File.Exists(save.FilePath) && new FileInfo(save.FilePath).Length > 0, "the game is saved", () => "no save at " + save.FilePath))
        {
            yield break;
        }

        report.Info($"saved {save.FilePath} ({new FileInfo(save.FilePath).Length / 1024} KiB) at tick {savedTick}");
        string text = File.ReadAllText(save.FilePath);
        report.Check(!text.Contains("Mugirl.GameValidation.", StringComparison.Ordinal), "the save holds no test-mod types",
            () => "a class of the test assembly was written into the save");

        yield return new LoadWait(save.SaveName);
        HashSet<int> loaded = [.. ctx.Map.mapPawns.FreeColonistsSpawned.Select(p => p.thingIDNumber)];
        report.Check(loaded.SetEquals(colonists), "the loaded game has the same colonists",
            () => $"saved [{string.Join(", ", colonists)}], loaded [{string.Join(", ", loaded)}]");
        report.Check(Find.TickManager.TicksGame == savedTick, "the loaded game resumes at the saved tick",
            () => $"saved {savedTick}, loaded {Find.TickManager.TicksGame}");
    }

    private static IEnumerable<Wait> Screenshot(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        if (ctx.Headless || !ctx.Options.Screenshots)
        {
            report.Skip("screenshot", ctx.Headless ? "the headless host renders no frames" : "screenshots are turned off");
            yield break;
        }

        Pawn? colonist = ctx.Map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
        if (colonist is null)
        {
            report.Fail("a colonist to frame", "no colonist on the map");
            yield break;
        }

        GameFixtures.FrameForScreenshot(ctx.Map, colonist.Position, GameFixtures.CloseUpCameraSize);
        yield return Wait.Frames(5);
        var shot = new ScreenshotWait(ValidationContext.ScreenshotName(Name));
        yield return shot;
        GameFixtures.EndScreenshots();
        long size = File.Exists(shot.FilePath) ? new FileInfo(shot.FilePath).Length : 0;
        report.Check(size > 1024, "the screenshot is written after the frame is rendered", () => $"{shot.FilePath}: {size} bytes");
        report.Info("screenshot " + shot.FilePath);
    }

    private static IEnumerable<Wait> WindowProbe(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        if (ctx.Headless)
        {
            report.Skip("window", "the headless host runs no UI");
            yield break;
        }

        if (!report.Check(DrawProbe.Watch(ctx, typeof(Dialog_MessageBox), nameof(Window.DoWindowContents)) is not null,
            "Dialog_MessageBox.DoWindowContents can be probed", () => "method not found"))
        {
            yield break;
        }

        var window = new Dialog_MessageBox("Mugirl game validation: plumbing window probe");
        ctx.OpenWindow(window);
        yield return Wait.Until(() => DrawProbe.Count(window) >= 3 || !Find.WindowStack.IsOpen(window), 15, "three draws of the probe window");
        report.Check(DrawProbe.Count(window) >= 3, "the window is drawn by the game's OnGUI", () => $"{DrawProbe.Count(window)} draw(s)");
        report.Check(Find.WindowStack.IsOpen(window), "the window stays open until closed", () => "it closed itself");
        ctx.CloseWindow(window);
        yield return Wait.Frames(3);
        report.Check(!Find.WindowStack.IsOpen(window), "the window closes", () => "still open");
    }
}
