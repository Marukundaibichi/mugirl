using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

internal static partial class CorporateUI
{
    /// <summary>
    /// 装饰背景：缓慢漂移的网格、呼吸的方框、旋转的轨道环与扫描线。只在 Repaint 中绘制，暂停时也继续动画。
    /// </summary>
    internal static void DrawGeometry(Rect rect, float opacity, bool light = false)
    {
        // 分组在每个事件中都开始与结束，各事件的 GUI 裁剪栈保持一致；图形只在 Repaint 中绘制。
        Widgets.BeginGroup(rect);
        try
        {
            if (!GuiPhase.IsRepaint || opacity <= 0f)
            {
                return;
            }

            float tone = light ? 0f : 1f;
            Color line = Gray(tone, opacity * 0.15f);
            float step = UnityMathf.Max(36f, UnityMathf.Min(64f, rect.width / 12f));
            float drift = CorporateClock.Phase(3.0, step);
            for (float x = -step + drift; x < rect.width; x += step)
            {
                Fill(new Rect(x, 0f, 1f, rect.height), line);
            }

            for (float y = -step + drift * 0.5f; y < rect.height; y += step)
            {
                Fill(new Rect(0f, y, rect.width, 1f), line);
            }

            float pulsePhase = CorporateClock.Phase(0.65, Math.Tau);
            float driftXPhase = CorporateClock.Phase(0.14, Math.Tau);
            float driftYPhase = CorporateClock.Phase(0.18, Math.Tau);
            for (int i = 0; i < 5; i++)
            {
                float pulse = 0.5f + 0.5f * UnityMathf.Sin(pulsePhase + i * 1.4f);
                float size = UnityMathf.Lerp(28f, 110f, (i + 1) / 5f) + pulse * 12f;
                float x = rect.width * (0.17f + i * 0.17f) + UnityMathf.Sin(driftXPhase + i) * 12f;
                float y = rect.height * (0.25f + (i % 3) * 0.22f) + UnityMathf.Cos(driftYPhase + i) * 15f;
                Outline(new Rect(x - size / 2f, y - size / 2f, size, size), Gray(tone, opacity * (0.12f + pulse * 0.18f)));
                Fill(new Rect(x - 2f, y - 2f, 4f, 4f), Gray(tone, opacity * 0.4f));
            }

            DrawOrbit(new Vector2(rect.width * 0.79f, rect.height * 0.50f),
                UnityMathf.Clamp(UnityMathf.Min(rect.width, rect.height) * 0.27f, 18f, 145f), opacity, tone);
            float sweep = CorporateClock.Phase(24.0, rect.width + 120.0) - 60f;
            Fill(new Rect(sweep, 0f, 1f, rect.height), Gray(tone, opacity * 0.40f));
        }
        finally
        {
            Widgets.EndGroup();
        }
    }

    // 三个反向旋转的方环。UiTransform 经 UI.RotateAroundPivot 按界面缩放换算支点，修正了 1.6 直接调用
    // GUIUtility.RotateAroundPivot 时在界面缩放不为 1 时支点偏移、环飘离主视觉区的问题。
    private static void DrawOrbit(Vector2 center, float radius, float opacity, float tone)
    {
        for (int ring = 0; ring < 3; ring++)
        {
            float size = radius * (1f - ring * 0.24f);
            float angle = CorporateClock.Phase(ring % 2 == 0 ? 3.0 : -4.2, 360.0) + ring * 30f;
            using (UiTransform.RotateAroundPivot(angle, center))
            {
                Outline(new Rect(center.X - size, center.Y - size, size * 2f, size * 2f), Gray(tone, opacity * (0.20f - ring * 0.035f)));
                Fill(new Rect(center.X + size - 2f, center.Y - size - 2f, 4f, 4f), Gray(tone, opacity * 0.38f));
            }
        }
    }
}
