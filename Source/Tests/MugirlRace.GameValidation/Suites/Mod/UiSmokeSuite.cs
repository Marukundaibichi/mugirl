using System.IO;
using System.Reflection;
using HarmonyLib;
using Mugirl.Core;
using Mugirl.Features.Corporate;
using Mugirl.Features.WeaponWheel;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>界面冒烟：打开并关闭 Mod 设置窗口、巨企通讯窗口（逐页切换）与武器轮盘 ITab，确认它们真的被画出来且没有报错。</summary>
/// <remarks>
/// 绘制次数由 <see cref="DrawProbe"/> 记录；界面中的异常会被 RimWorld 记为错误，由会话的日志检查归入本步骤。
/// 隐藏窗口下鼠标位置不确定（E40），这里不模拟点击，只检查打开、绘制、切页与关闭。
/// </remarks>
internal static class UiSmokeSuite
{
    public const string Name = SuiteCatalog.UiSuiteName;

    private const int MinimumDraws = 3;
    private const double DrawTimeoutSeconds = 15;
    private const double CloseTimeoutSeconds = 5;

    public static ValidationSuite Create() => new(
        Name,
        "open, draw and close the mod settings window, the corporate comms window (every page) and the weapon wheel ITab",
        SuiteNeeds.MugirlMod | SuiteNeeds.RenderedFrames,
        [
            new ValidationStep("mod-settings", ModSettings),
            new ValidationStep("corporate-comms", CorporateComms, TimeoutSeconds: 240),
            new ValidationStep("weapon-wheel-tab", WeaponWheelTab),
        ]);

    private static IEnumerable<Wait> ModSettings(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        MugirlMod? mod = LoadedModManager.ModHandles.OfType<MugirlMod>().FirstOrDefault();
        if (mod is null)
        {
            report.Fail("the MugirlMod instance exists", "LoadedModManager has no MugirlMod handle");
            yield break;
        }

        if (DrawProbe.Watch(ctx, typeof(MugirlMod), nameof(MugirlMod.DoSettingsWindowContents)) is null)
        {
            report.Fail("MugirlMod.DoSettingsWindowContents can be probed", "method not found");
            yield break;
        }

        var window = new Dialog_ModSettings(mod);
        ctx.OpenWindow(window);
        yield return Wait.Until(() => DrawProbe.Count(mod) >= MinimumDraws || !Find.WindowStack.IsOpen(window), DrawTimeoutSeconds, "draws of the mod settings page");
        report.Check(DrawProbe.Count(mod) >= MinimumDraws, "the settings page is drawn", () => $"{DrawProbe.Count(mod)} draw(s)");
        report.Check(Find.WindowStack.IsOpen(window), "the settings window stays open", () => "it closed itself");

        // 关闭时 Dialog_ModSettings.PreClose 调用 WriteSettings；设置文件写在临时存档目录的 Config 中。
        ctx.CloseWindow(window);
        yield return Wait.Frames(3);
        report.Check(!Find.WindowStack.IsOpen(window), "the settings window closes", () => "still open");
        string[] files = Directory.Exists(GenFilePaths.ConfigFolderPath)
            ? Directory.GetFiles(GenFilePaths.ConfigFolderPath, "Mod_*_" + nameof(MugirlMod) + ".xml")
            : [];
        report.Check(files.Length > 0, "closing the window writes the settings file", () => "no Mod_*_MugirlMod.xml in " + GenFilePaths.ConfigFolderPath);
    }

    private static IEnumerable<Wait> CorporateComms(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        // 窗口类本身不一定覆写 DoWindowContents（动画基类声明它）；DrawProbe 给实际声明它的类型打补丁，按实例计数。
        MethodInfo? drawn = DrawProbe.Watch(ctx, typeof(Dialog_CorporateTerminal), nameof(Window.DoWindowContents));
        if (drawn is null)
        {
            report.Fail("Window_CorporateComms.DoWindowContents can be probed", "method not found");
            yield break;
        }

        report.Info($"probing {drawn.DeclaringType?.FullName}.{drawn.Name}");

        var window = new Dialog_CorporateTerminal(new CorporateTradeContext(ctx.Map));
        ctx.OpenWindow(window);
        yield return Wait.Until(() => DrawProbe.Count(window) >= MinimumDraws || !Find.WindowStack.IsOpen(window), DrawTimeoutSeconds, "draws of the corporate comms window");
        report.Check(DrawProbe.Count(window) >= MinimumDraws, "the corporate comms window is drawn",
            () => $"{DrawProbe.Count(window)} draw(s) of {drawn.DeclaringType?.Name}.{drawn.Name}");
        report.Check(Find.WindowStack.IsOpen(window), "the corporate comms window stays open", () => "it closed itself");

        // 页面切换是窗口内部的成员；找不到时只跳过逐页检查，不影响打开与关闭的结论。
        MethodInfo? requestPage = AccessTools.Method(typeof(Dialog_CorporateTerminal), "RequestPage", [typeof(int)]);
        FieldInfo? selectedPage = AccessTools.Field(typeof(Dialog_CorporateTerminal), "selectedPage");
        string[]? pages = AccessTools.Field(typeof(Dialog_CorporateTerminal), "PageKeys")?.GetValue(null) as string[];
        if (requestPage is null || selectedPage is null || pages is null)
        {
            report.Skip("pages", "RequestPage, selectedPage or PageKeys not found; pages were not cycled");
        }
        else
        {
            for (int index = 0; index < pages.Length && Find.WindowStack.IsOpen(window); index++)
            {
                int page = index;
                string label = $"page {page} ({pages[page]})";
                requestPage.Invoke(window, [page]);
                yield return Wait.Until(() => selectedPage.GetValue(window) is int current && current == page, 10, label + " to become current");
                int before = DrawProbe.Count(window);
                yield return Wait.Until(() => DrawProbe.Count(window) >= before + MinimumDraws || !Find.WindowStack.IsOpen(window), DrawTimeoutSeconds, "draws of " + label);
                report.Check(selectedPage.GetValue(window) is int shown && shown == page && DrawProbe.Count(window) >= before + MinimumDraws,
                    label + " is drawn", () => $"current page {selectedPage.GetValue(window)}, {DrawProbe.Count(window) - before} draw(s), open {Find.WindowStack.IsOpen(window)}");
            }
        }

        // 巨企窗口先淡出（实时 0.22 秒）再从 WindowStack 移除；按条件等待，不按帧数。
        ctx.CloseWindow(window);
        yield return Wait.Until(() => !Find.WindowStack.IsOpen(window), CloseTimeoutSeconds, "the corporate comms window to finish closing");
        report.Check(!Find.WindowStack.IsOpen(window), "the corporate comms window closes", () => "still open");
    }

    private static IEnumerable<Wait> WeaponWheelTab(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        Pawn pawn = GameFixtures.EnsureMugirlColonists(ctx, 1)[0];
        ITab_Pawn_WeaponWheel? tab = pawn.def.inspectorTabsResolved?.OfType<ITab_Pawn_WeaponWheel>().FirstOrDefault();
        if (tab is null)
        {
            report.Fail("the weapon wheel ITab is on the Mugirl race", "missing from inspectorTabsResolved");
            yield break;
        }

        try
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
            yield return Wait.Frames(3);
            if (!tab.IsVisible)
            {
                report.Skip("weapon wheel tab", $"not reachable: the tab is hidden for {pawn.ThingID} (IsVisible is false)");
                yield break;
            }

            if (DrawProbe.Watch(ctx, typeof(ITab_Pawn_WeaponWheel), "FillTab") is null)
            {
                report.Fail("ITab_Pawn_WeaponWheel.FillTab can be probed", "method not found");
                yield break;
            }

            InspectTabBase? opened = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_WeaponWheel));
            report.Check(ReferenceEquals(opened, tab), "the inspect pane opens the weapon wheel tab", () => opened?.GetType().FullName ?? "no tab opened");
            yield return Wait.Until(() => DrawProbe.Count(tab) >= MinimumDraws, DrawTimeoutSeconds, "draws of the weapon wheel tab");
            report.Check(DrawProbe.Count(tab) >= MinimumDraws, "the weapon wheel tab is drawn", () => $"{DrawProbe.Count(tab)} draw(s)");
        }
        finally
        {
            (MainButtonDefOf.Inspect.TabWindow as MainTabWindow_Inspect)?.CloseOpenTab();
            Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
            Find.Selector.ClearSelection();
        }

        yield return Wait.Frames(2);
    }
}
