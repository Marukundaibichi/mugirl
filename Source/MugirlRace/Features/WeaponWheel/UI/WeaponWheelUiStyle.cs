namespace Mugirl.Features.WeaponWheel;

// 武器轮盘检查面板的配色（只读数据）。
internal static class WeaponWheelUiStyle
{
    internal static readonly Color PanelTint = new(0.065f, 0.072f, 0.082f, 0.72f);
    internal static readonly Color PanelTopLine = new(0.72f, 0.78f, 0.82f, 0.08f);
    internal static readonly Color SummaryDivider = new(0.48f, 0.53f, 0.57f, 0.18f);
    internal static readonly Color TrayBackground = new(0.025f, 0.03f, 0.036f, 0.54f);
    internal static readonly Color TrayTopLine = new(0.48f, 0.56f, 0.60f, 0.10f);

    internal static readonly Color SlotBackgroundEquipped = new(0.145f, 0.16f, 0.18f, 0.96f);
    internal static readonly Color SlotBackgroundEmpty = new(0.09f, 0.10f, 0.115f, 0.92f);
    internal static readonly Color SlotBackgroundLocked = new(0.065f, 0.073f, 0.084f, 0.96f);
    internal static readonly Color SlotTopLine = new(1f, 1f, 1f, 0.07f);
    internal static readonly Color SlotHoverFill = new(0.34f, 0.38f, 0.42f, 0.18f);
    internal static readonly Color SlotDragSourceFill = new(0.08f, 0.12f, 0.15f, 0.42f);
    internal static readonly Color SlotBorderEquipped = new(0.31f, 0.35f, 0.38f, 0.74f);
    internal static readonly Color SlotBorderEmpty = new(0.23f, 0.25f, 0.28f, 0.58f);
    internal static readonly Color SlotBorderLocked = new(0.25f, 0.28f, 0.31f, 0.76f);
    internal static readonly Color SlotBorderHover = new(0.76f, 0.70f, 0.38f, 0.94f);
    internal static readonly Color SlotBorderLockedHover = new(0.48f, 0.44f, 0.27f, 0.90f);
    internal static readonly Color SlotBorderDropTarget = new(0.48f, 0.88f, 0.52f, 1f);
    internal static readonly Color SlotBorderActive = new(0.32f, 0.76f, 0.94f, 1f);
    internal static readonly Color PrimaryAccent = new(0.94f, 0.68f, 0.20f, 0.96f);

    internal static readonly Color HealthBarBackground = new(0.035f, 0.04f, 0.05f, 0.92f);
    internal static readonly Color HealthLow = new(0.78f, 0.22f, 0.18f);
    internal static readonly Color HealthHigh = new(0.32f, 0.82f, 0.38f);

    internal static readonly Color AccentMounted = new(0.40f, 0.60f, 0.72f, 0.92f);
    internal static readonly Color AccentBusy = new(0.90f, 0.62f, 0.24f, 0.95f);
    internal static readonly Color AccentEligible = new(0.38f, 0.82f, 0.46f, 0.95f);
    internal static readonly Color AccentIdle = new(0.42f, 0.48f, 0.52f, 0.78f);
    internal static readonly Color StatusText = new(0.84f, 0.87f, 0.85f, 0.94f);
    internal static readonly Color SlotCountText = new(0.70f, 0.73f, 0.72f, 0.88f);

    internal static readonly Color BadgeLockedBackground = new(0.075f, 0.086f, 0.10f, 0.94f);
    internal static readonly Color BadgePrimaryBackground = new(0.34f, 0.24f, 0.07f, 0.92f);
    internal static readonly Color BadgeBackground = new(0.065f, 0.075f, 0.09f, 0.86f);
    internal static readonly Color BadgeLockedText = new(0.52f, 0.56f, 0.60f, 0.82f);
    internal static readonly Color BadgeText = new(0.82f, 0.84f, 0.80f, 0.88f);

    internal static readonly Color LockedText = new(0.57f, 0.60f, 0.62f, 0.82f);
    internal static readonly Color LockedTextHover = new(0.82f, 0.79f, 0.62f, 0.94f);

    internal static readonly Color DevButtonBackground = new(0.12f, 0.14f, 0.16f, 0.92f);
    internal static readonly Color DevButtonBackgroundHover = new(0.42f, 0.37f, 0.18f, 0.94f);
    internal static readonly Color DevButtonBorder = new(0.37f, 0.40f, 0.42f, 0.82f);
    internal static readonly Color DevButtonBorderHover = new(0.88f, 0.79f, 0.42f, 0.98f);
    internal static readonly Color DevButtonText = new(0.62f, 0.65f, 0.66f, 0.88f);
    internal static readonly Color DevButtonTextHover = new(1f, 0.91f, 0.58f);

    internal static readonly Color GhostShadow = new(0f, 0f, 0f, 0.30f);
    internal static readonly Color GhostBackground = new(0.08f, 0.10f, 0.12f, 0.88f);

    // 空槽中央的“+”号，悬停时更亮。
    internal static Color AddMark(bool mouseOver) => new(0.58f, 0.62f, 0.66f, mouseOver ? 0.86f : 0.42f);
}
