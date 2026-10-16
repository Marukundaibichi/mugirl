namespace Mugirl.Features.Milk;

// 多选合并时名字条弹窗的会话状态。Gizmo 实例每帧重建，跨帧状态只能放在静态字段；不进存档。
// StaticCacheLifecycle: 鼠标离开 Gizmo 与弹窗区域、或弹窗连续两帧未被请求时结束会话（End）；
// 每局重置（MilkingAnimation.ResetTransientState，由 GameComponent_Storyline 调用）时全部清空（Reset），
// 不让上一局的组件（及其 Pawn、地图）被静态字段持有。名字与宽度列表每次绘制弹窗时重新填充。
internal static class MilkGaugePopupSession
{
    // 当前查看的成员。会话结束后保留，鼠标离开后量杯仍显示最后选中的成员（与 1.6 相同），下次打开弹窗时归位。
    internal static CompMilkProduction? DisplayComp;

    internal static bool ApplyToAll;

    internal static float NameScroll;

    internal static bool IsOpen;

    internal static int LastRequestFrame = -1;

    internal static readonly List<string> MemberNames = [];

    internal static readonly List<float> MemberCellWidths = [];

    internal static void Begin()
    {
        IsOpen = true;
        ApplyToAll = false;
        NameScroll = 0f;
        DisplayComp = null;
    }

    internal static void End()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        ApplyToAll = false;
        NameScroll = 0f;
    }

    internal static void Reset()
    {
        End();
        DisplayComp = null;
        LastRequestFrame = -1;
        MemberNames.Clear();
        MemberCellWidths.Clear();
    }
}
