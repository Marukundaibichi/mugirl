namespace Mugirl.Features.Roping;

// 绳索材质：与原版 Pawn_RopeTracker.RopeLineMat 的参数完全相同（1.7 已核对），MaterialPool 按请求去重，
// 得到的就是原版使用的同一个材质。放在 [StaticConstructorOnStartup] 类中，内容加载完成后才创建；
// 1.6 版在补丁类的静态构造函数里反射读取原版私有字段，会在 Mod 构造期间提前触发原版的静态构造。
[StaticConstructorOnStartup]
internal static class RopeGraphics
{
    private const string RopeTexPath = "UI/Overlays/Rope";

    // 原版 GenDraw.DrawLineBetween 的默认线宽。
    internal const float RopeLineWidth = 0.2f;

    // StaticCacheLifecycle: 进程级共享材质，归 MaterialPool 所有，不需要清理。
    internal static readonly Material RopeLineMat = MaterialPool.MatFrom(RopeTexPath, ShaderDatabase.Transparent, GenColor.FromBytes(99, 70, 41));
}
