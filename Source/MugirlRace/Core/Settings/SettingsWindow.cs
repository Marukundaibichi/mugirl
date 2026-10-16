using Mugirl.Platform;

namespace Mugirl.Core;

internal static class SettingsWindow
{
    private const float GapHeight = 12f;
    private const float WidthStepsPerUnit = 20f;

    // StaticCacheLifecycle: 设置界面专用的待刷新标记；松开鼠标或设置写盘时消费，不持有游戏对象。
    private static bool pendingOutlineRefresh;

    // StaticCacheLifecycle: 进程级功能绑定；静态启动时登记，不持有 Pawn、Map 或 Game。
    private static readonly List<DeveloperAction> developerActions = [];

    internal static event Action? Changed;

    internal static void RegisterDeveloperAction(string labelKey, Func<MugirlSettings, bool> visible, Action action)
        => developerActions.Add(new DeveloperAction(labelKey, visible, action));

    internal static void Draw(Rect inRect, MugirlSettings settings)
    {
        Listing_Standard listingStandard = new();
        listingStandard.Begin(inRect);

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.StructuralCrashEvent.Label".Translate(), ref settings.enableOpeningStoryline,
            "Mugirl.Settings.StructuralCrashEvent.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.MigrationEvent.Label".Translate(), ref settings.enableMigrationEvent,
            "Mugirl.Settings.MigrationEvent.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.FusionInvestmentEvent.Label".Translate(), ref settings.enableFusionInvestmentEvent,
            "Mugirl.Settings.FusionInvestmentEvent.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.RunawayFarmQuest.Label".Translate(), ref settings.enableRunawayFarmQuest,
            "Mugirl.Settings.RunawayFarmQuest.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.FastMilking.Label".Translate(), ref settings.enableFastMilking,
            "Mugirl.Settings.FastMilking.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.VanillaMilkGauge.Label".Translate(), ref settings.enableVanillaMilkGauge,
            "Mugirl.Settings.VanillaMilkGauge.Desc".Translate());

        listingStandard.Gap(GapHeight);
        DrawOutlineSettings(listingStandard, settings);

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.HideBackWeapons.Label".Translate(), ref settings.hideWeaponWheelBackWeapons,
            "Mugirl.Settings.HideBackWeapons.Desc".Translate());

        listingStandard.Gap(GapHeight);
        listingStandard.CheckboxLabeled("Mugirl.Settings.WeaponWheelDevLog.Label".Translate(), ref settings.enableWeaponWheelDevLog,
            "Mugirl.Settings.WeaponWheelDevLog.Desc".Translate());
        foreach (DeveloperAction action in developerActions)
        {
            if (action.Visible(settings) && listingStandard.ButtonText(action.LabelKey.Translate()))
            {
                action.Run();
            }
        }

        listingStandard.End();
    }

    /// <summary>补上尚未执行的描边刷新；设置写盘时调用（窗口关闭时一定会写盘）。</summary>
    internal static void FlushPendingRefresh()
    {
        if (!pendingOutlineRefresh)
        {
            return;
        }

        pendingOutlineRefresh = false;
        Changed?.Invoke();
    }

    private static void DrawOutlineSettings(Listing_Standard listingStandard, MugirlSettings settings)
    {
        bool outlineEnabled = settings.enableExtraOutline;
        float outlineWidth = settings.extraOutlineWidth;
        listingStandard.CheckboxLabeled("Mugirl.Settings.ExtraOutline.Label".Translate(), ref settings.enableExtraOutline,
            "Mugirl.Settings.ExtraOutline.Desc".Translate());
        if (settings.enableExtraOutline)
        {
            listingStandard.Label("Mugirl.Settings.ExtraOutline.Width".Translate(settings.extraOutlineWidth.ToString("0.00")));
            float sliderValue = listingStandard.Slider(settings.extraOutlineWidth, MugirlSettings.ExtraOutlineMinWidth, MugirlSettings.ExtraOutlineMaxWidth);
            settings.extraOutlineWidth = UnityMathf.Round(sliderValue * WidthStepsPerUnit) / WidthStepsPerUnit;
        }

        if (outlineEnabled != settings.enableExtraOutline || outlineWidth != settings.extraOutlineWidth)
        {
            pendingOutlineRefresh = true;
        }

        // 刷新会遍历全部地图与世界 Pawn 并清空图集，拖动滑条期间只记标记，松开左键后才执行一次。
        // Verse 的滑条用私有的 sliderDraggingID 跟踪拖动，从不设置 GUIUtility.hotControl，所以按原生输入状态判断。
        if (!EngineInput.LeftMouseDown)
        {
            FlushPendingRefresh();
        }
    }

    private sealed record DeveloperAction(string LabelKey, Func<MugirlSettings, bool> Visible, Action Run);
}
