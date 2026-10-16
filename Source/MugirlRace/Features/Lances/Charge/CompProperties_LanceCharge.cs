namespace Mugirl.Features.Lances;

public sealed class CompProperties_LanceCharge : CompProperties
{
    public bool hasPointCharge = true;
    public bool hasLineCharge = true;
    public float minimumPointRange;
    public float minimumLineRange = 4f;
    public float maximumRange = 19.9f;
    public int pointCooldownTicks = 1800;
    public int lineCooldownTicks = 600;
    public int pointStunTicks = 300;
    public int lineStunTicks = 180;
    public int pointDurabilityCost = 20;
    public int lineDurabilityCost;
    public float maximumPointDamage = 220f;
    public bool steamTrail;
    public string? pointIconPath;
    public string? lineIconPath;

    // 指令图标在主线程（Gizmo 与瞄准界面）首次需要时解析一次。语言切换与 Mod 重载会重建 Def，连同本对象一起刷新。
    [Unsaved]
    private Texture2D? pointIcon;

    [Unsaved]
    private Texture2D? lineIcon;

    public CompProperties_LanceCharge()
    {
        compClass = typeof(CompLanceCharge);
    }

    internal Texture2D IconFor(bool point)
    {
        return point
            ? pointIcon ??= LoadIcon(pointIconPath)
            : lineIcon ??= LoadIcon(lineIconPath);
    }

    internal float MinimumRangeFor(bool point) => point ? minimumPointRange : minimumLineRange;

    internal int CooldownTicksFor(bool point) => point ? pointCooldownTicks : lineCooldownTicks;

    private static Texture2D LoadIcon(string? path)
    {
        // 路径来自 XML（UI/Commands/Lances/*），缺图时退回原版 BadTex，与 1.6 相同。
        return path.NullOrEmpty()
            ? BaseContent.BadTex
            : ContentFinder<Texture2D>.Get(path, reportFailure: false) ?? BaseContent.BadTex;
    }
}
