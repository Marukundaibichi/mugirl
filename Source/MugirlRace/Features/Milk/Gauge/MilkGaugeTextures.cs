namespace Mugirl.Features.Milk;

// 奶量量杯的美术贴图。1.7 使用 1.7/Textures/UI/Mugirl_MilkGauge_Uncompressed.png（不压缩，避免细描边被块压缩模糊）；
// 1.6 的根目录 Textures/UI/Mugirl_MilkGauge.png 保持不变。
[StaticConstructorOnStartup]
internal static class MilkGaugeTextures
{
    private const string GaugeTexturePath = "UI/Mugirl_MilkGauge_Uncompressed";

    // StaticCacheLifecycle: 进程级贴图引用，启动阶段在主线程加载一次；缺图时为 null，Gizmo 改画边框。
    internal static readonly Texture2D? Gauge = ContentFinder<Texture2D>.Get(GaugeTexturePath, false);
}
