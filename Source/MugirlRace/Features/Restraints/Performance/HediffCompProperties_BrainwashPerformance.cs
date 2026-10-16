namespace Mugirl.Features.Restraints;

// 洗脑表演效果配置，控制延迟、持续时间和播放的声音/粒子/文字。
// DefInjected 路径使用 compClass 短名 HediffComp_BrainwashPerformance 与嵌套类型的字段名，类名与字段名都不能改。
public class HediffCompProperties_BrainwashPerformance : HediffCompProperties
{
    public int triggerTicks = 60;
    public int stunDelayTicks = 10;
    public int stunDurationTicks = 120;

    public List<SoundWithParams>? sounds;
    public List<FleckWithParams>? flecks;
    public List<TextWithParams>? texts;

    // 头顶 Mote 的显示窗口。
    public ThingDef? moteToShowOnHead;
    public int moteShowStartTick;
    public int moteShowEndTick = 999999;

    public HediffCompProperties_BrainwashPerformance()
    {
        compClass = typeof(HediffComp_BrainwashPerformance);
    }

    public override void ResolveReferences(HediffDef parent)
    {
        base.ResolveReferences(parent);
        ClampTextColors();
    }

    // XML 文字颜色写成 (239, 70, 274) 时，ParseHelper 按 0–255 换算得 b≈1.07，超出 [0,1]。
    // 1.6 由 Unity 的 LDR 输出截断到 1；Waylith 对 >1 的 GUI 颜色如何显示未经核实，因此加载时截断，结果与 1.6 一致。
    private void ClampTextColors()
    {
        if (texts == null)
        {
            return;
        }

        for (int i = 0; i < texts.Count; i++)
        {
            TextWithParams? text = texts[i];
            if (text == null)
            {
                continue;
            }

            Color c = text.color;
            text.color = new Color(UnityMathf.Clamp01(c.r), UnityMathf.Clamp01(c.g), UnityMathf.Clamp01(c.b), UnityMathf.Clamp01(c.a));
        }
    }

    // 可循环播放的声音片段配置。
    public class SoundWithParams
    {
        public SoundDef? sound;
        public int startTick;
        public int loopInterval = 60;
        public int endTick = 99999;
    }

    // 可循环生成的 Fleck 配置。
    public class FleckWithParams
    {
        public FleckDef? fleck;
        public int startTick;
        public int loopInterval = 120;
        public int endTick = 99999;
    }

    // 在指定 tick 显示一次的文字配置。
    public class TextWithParams
    {
        public string? text;
        public int startTick;
        public Color color = UnityColor.White;
    }
}
