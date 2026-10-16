using HarmonyLib;

namespace Mugirl.Control.UI
{
    /// <summary>B.namespaces：命名空间段 UI 遮蔽 Verse.UI。</summary>
    public static class ShadowingSegmentControl
    {
        public static int Value() => 1;
    }
}

namespace Mugirl.Control.Apparel
{
    /// <summary>B.namespaces：命名空间段 Apparel 与 Mod 代码使用的 RimWorld.Apparel 同名。</summary>
    public static class GameTypeSegmentControl
    {
        public static bool IsApparel(Thing thing) => thing is RimWorld.Apparel;
    }
}

namespace Mugirl
{
    /// <summary>B.component-ctors：没有游戏使用的 public (Game) 构造函数。</summary>
    public class ControlComponentWithoutCtor : GameComponent
    {
        public ControlComponentWithoutCtor()
        {
        }
    }

    /// <summary>B.component-ctors：构造函数读 Find（经本模块方法）并写静态字段。</summary>
    public class ControlComponentWithSideEffects : GameComponent
    {
        // StaticCacheLifecycle: 负对照；进程级。
        private static int constructed;

        public ControlComponentWithSideEffects(Game game)
        {
            constructed++;
            Touch();
        }

        private static int Touch() => Find.TickManager.TicksGame;
    }

    /// <summary>B.save-confinement：存档读取内部的补丁写在 SaveUpgrade/ 之外；B.architecture save-internals：直接读 curXmlParent。</summary>
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName))]
    public static class ControlConfinedPatch
    {
        public static void Postfix()
        {
        }

        public static string CurrentNode() => Scribe.loader.curXmlParent?.Name;
    }

    /// <summary>B.patch-order：同一目标上两个同种补丁优先级相同（顺序取决于类名）。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    [MugirlPatch(PatchModule.Core, PatchRisk.Low, "control")]
    public static class Pawn_Kill_ControlFirst
    {
        public static void Prefix()
        {
        }
    }

    /// <summary>B.patch-order：见 <see cref="Pawn_Kill_ControlFirst"/>。</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    [MugirlPatch(PatchModule.Core, PatchRisk.Low, "control")]
    public static class Pawn_Kill_ControlSecond
    {
        public static void Prefix()
        {
        }
    }

    /// <summary>B.scribe-defaults：默认值与字段初始值不同；字段在构造函数中由参数赋值却没有 forceSave。</summary>
    public class ControlScribeDefaults : IExposable
    {
        private int countdown = 60;
        private float computed;

        public ControlScribeDefaults()
        {
        }

        public ControlScribeDefaults(float start)
        {
            computed = start;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref countdown, "countdown", 30);
            Scribe_Values.Look(ref computed, "computed");
        }
    }
}

namespace Mugirl.Control.Structure
{
    /// <summary>B.save-confinement：手写的 Harmony.Patch 调用给存档读取内部打补丁（不经 [HarmonyPatch] 特性）。</summary>
    public static class ControlManualPatcher
    {
        public static void Apply(Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(ScribeLoader), "FinalizeLoading"), postfix: new HarmonyMethod(typeof(ControlManualPatcher), nameof(Postfix)));

        public static void Postfix()
        {
        }
    }

    /// <summary>B.component-ctors：构造函数 new 一个本模块类型，后者的构造函数读 Current.Game。</summary>
    public class ControlComponentNewobj : GameComponent
    {
        private readonly ControlCtorHelper helper;

        public ControlComponentNewobj(Game game) => helper = new ControlCtorHelper();
    }

    /// <summary>见 <see cref="ControlComponentNewobj"/>。</summary>
    public class ControlCtorHelper
    {
        public ControlCtorHelper() => Seen = Current.Game is not null;

        public bool Seen { get; }
    }

    /// <summary>B.component-ctors：构造函数读静态字段，触发静态构造函数（它读 Find.TickManager）。</summary>
    public class ControlComponentStatic : GameComponent
    {
        private readonly int start;

        public ControlComponentStatic(Game game) => start = ControlStaticState.Start;
    }

    /// <summary>见 <see cref="ControlComponentStatic"/>。</summary>
    public static class ControlStaticState
    {
        // StaticCacheLifecycle: 负对照；进程级。
        public static readonly int Start = Find.TickManager.TicksGame;
    }
}
