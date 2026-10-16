namespace Mugirl;

/// <summary>A.assembly-refs、B.unity-boundary、B.banned-symbols、B.waylith-namespace 与 B.jit 违规。</summary>
public static class UnityLayerViolations
{
    // UnityStubs 逻辑（Mathf）：不在边界白名单中，也被 BannedSymbols.txt 禁用。
    public static float ClampWithMathf(float value) => UnityEngine.Mathf.Clamp(value, 0f, 1f);

    // 例外清单列出了这个调用点，但它不在允许抑制 RS0030 的命名空间中。
    public static float ClampWithMathfListed(float value) => UnityEngine.Mathf.Clamp01(value);

    // 编译期针对 "UnityEngine.CoreModule"，运行时被游戏重定向到 UnityStubs：JIT 绑定不到。
    public static int CallRemovedLegacyApi() => UnityEngine.ControlOnlyLegacyApi.Removed();

    // Mugirl.Platform 之外的原生引擎访问。
    public static float EngineTimeOutsidePlatform() => Waylith.Time.RealtimeSinceStartup;

    // 引用 Unity 兼容层。
    public static string CompatibilityLayerName() => typeof(Waylith.UnityCompat.UnityFrame).Name;

    // 白名单中的 GUI 服务成员在 Mugirl.Platform 之外使用。
    public static UnityEngine.Color GuiColorOutsidePlatform() => UnityEngine.GUI.color;

    // 正对照：当前 HAR 1.7 的合法引用。
    public static string AlienRaceTypeName() => typeof(AlienRace.ThingDef_AlienRace).Name;
}

/// <summary>B.static-assets：不带 [StaticConstructorOnStartup] 的类型持有静态贴图。</summary>
public static class UnmarkedTextureHolder
{
    // StaticCacheLifecycle: 负对照；进程级。
    public static UnityEngine.Texture2D Icon;
}

/// <summary>B.architecture 违规：直接 Verse.Log、Find.TickManager、Current.Game、Faction.OfPlayer、Game.components、PatchAll。</summary>
public static class ArchitectureViolations
{
    public static void LogDirectly() => Log.Warning("[Mugirl] control: direct Verse.Log call");

    public static int TicksDirectly() => Find.TickManager.TicksGame;

    public static Game GameDirectly() => Current.Game;

    public static Faction PlayerFactionDirectly() => Faction.OfPlayer;

    public static int ComponentCount() => Current.Game.components.Count;

    public static void PatchEverything(HarmonyLib.Harmony harmony) => harmony.PatchAll();
}

/// <summary>B.base-calls：重写 ExposeData 而不调用 base。</summary>
public class Thought_ControlNoBase : Thought_Memory
{
    public override void ExposeData()
    {
    }
}

/// <summary>B.empty-types：没有任何成员、也不在例外清单中的类型。</summary>
public class EmptyControlType
{
}

/// <summary>B.static-caches 与 B.scribe-defaults：没有生命周期注释的静态缓存；Scribe 默认值（省略即 0）与字段初始值 3 不同。</summary>
public class ControlScribeHolder : IExposable
{
    private static readonly Dictionary<int, int> UncommentedCache = new();

    private int lockCount = 3;

    public void ExposeData()
    {
        Scribe_Values.Look(ref lockCount, "lockCount");
    }

    public static int CacheSize() => UncommentedCache.Count;
}
