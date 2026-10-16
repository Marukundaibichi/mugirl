using Mugirl.Platform.Ui;

namespace Mugirl.Features.Milk;

// 奶槽阈值线的拖动：在奶槽或阈值线附近按下左键即设置阈值并捕获鼠标，拖动中持续更新，松开时结束。
// 原始 GUI 事件与鼠标捕获只经 Mugirl.Platform.Ui（UiEvent、UiHotControl）访问。
internal static class MilkGaugeThresholdDrag
{
    // 本事件改变了阈值时返回 true（Gizmo 据此报告 Interacted）；newThreshold 为钳制到 [0.1, 1] 的新阈值。
    // 每个 GUI 事件都要调用一次，保证控件 id 的分配顺序在各事件中一致。
    internal static bool TryHandle(Rect milkSlotRect, float threshold, int controlHint, out float newThreshold)
    {
        newThreshold = threshold;
        int controlId = UiHotControl.NewControlId(controlHint, milkSlotRect);
        Vector2 mouse = UiEvent.MousePosition;

        if (UiEvent.IsMouseDown(0)
            && (UnityRect.Contains(MilkGaugeLayout.ThresholdGrabRect(milkSlotRect, threshold), mouse)
                || UnityRect.Contains(MilkGaugeLayout.SlotGrabRect(milkSlotRect), mouse)))
        {
            UiHotControl.Capture(controlId);
            newThreshold = MilkGaugeLayout.ThresholdFromMouse(milkSlotRect, mouse.X);
            UiEvent.Use();
            return true;
        }

        if (!UiHotControl.IsCaptured(controlId))
        {
            return false;
        }

        if (UiEvent.IsMouseDrag(UiEvent.Button))
        {
            newThreshold = MilkGaugeLayout.ThresholdFromMouse(milkSlotRect, mouse.X);
            UiEvent.Use();
            return true;
        }

        if (UiEvent.IsMouseUp(0))
        {
            newThreshold = MilkGaugeLayout.ThresholdFromMouse(milkSlotRect, mouse.X);
            UiHotControl.Release(controlId);
            UiEvent.Use();
            return true;
        }

        return false;
    }
}
