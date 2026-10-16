namespace Mugirl.Features.Milk;

// 榨乳器释放时按 tick 排程的音效、Fleck 与浮动文字（XML 配置）。嵌套类名与字段名由 XML 使用，不可改名。
public class CompProperties_MilkingDeviceReleaseEffect : CompProperties
{
    public int durationTicks = 60;

    public List<SoundWithParams?>? sounds;

    public List<FleckWithParams?>? flecks;

    public List<TextWithParams?>? texts;

    public CompProperties_MilkingDeviceReleaseEffect()
    {
        compClass = typeof(CompMilkingDeviceReleaseEffect);
    }

    public class SoundWithParams
    {
        public SoundDef? sound;
        public int startTick;
        public int loopInterval = 60;
        public int endTick = 60;
    }

    public class FleckWithParams
    {
        public FleckDef? fleck;
        public int startTick;
        public int loopInterval = 30;
        public int endTick = 60;
        public float scale = 0.42f;
        public float speedMin = 0.2f;
        public float speedMax = 0.5f;
    }

    public class TextWithParams
    {
        public string? text;
        public int startTick;
        public Color color = UnityColor.White;
    }
}
