using System.Runtime.CompilerServices;
using HarmonyLib;
using Mugirl.Rendering;

namespace Mugirl.Features.Lances;

/// <summary>
/// 骑枪冲锋的方向运动模糊：飞行中每帧画一次，落地后在 <see cref="RecoveryTicks"/> 个 tick 内收回。复用渲染树本帧计算好的图层与矩阵，
/// 每个轮廓图层提交一次原生方向卷积（<see cref="LanceMotionBlurPass"/>），不额外绘制人物副本。
/// </summary>
internal static class LanceMotionBlur
{
    internal const int RecoveryTicks = 18;

    private const int NoMap = -1;

    private const int NoRecoveryWindow = int.MinValue;

    // StaticCacheLifecycle: 固定的原版字段访问器，进程内不变。
    private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> Requests =
        AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");

    // StaticCacheLifecycle: 落地视觉状态弱关联 Pawn，收回结束或换图后在下次绘制时删除，全局收回窗口结束时一并清空；
    // 只记地图 id，不持有地图；不写入存档、不影响玩法。
    private static readonly ConditionalWeakTable<Pawn, Recovery> recoveries = new();

    // StaticCacheLifecycle: 进程级，已登记收回中最晚的结束 tick；只在主线程的落地与绘制阶段读写。当前 tick 到达它时
    // 每条记录都已过期，由 AnyRecoveryActive 清空弱表并复位。读档后 tick 变小时窗口只会多开一段（照常逐人物查表），无需随读档清理。
    private static int recoveryWindowEndTick = NoRecoveryWindow;

    /// <summary>落地时记录本帧位移，开始收回拖尾。</summary>
    internal static void BeginRecovery(Pawn? pawn, UVector3 travel) => BeginRecovery(pawn, travel.ToNumerics());

    /// <summary>落地时记录本帧位移（世界空间），开始收回拖尾。</summary>
    internal static void BeginRecovery(Pawn? pawn, Vector3 travel)
    {
        if (pawn is null || UnityVector.SqrMagnitude(travel) < 0.0001f || !TickUtility.TryGetCurrentGameTick(out int tick))
        {
            return;
        }

        recoveries.AddOrUpdate(pawn, new Recovery(MapIdOf(pawn), tick, travel));
        recoveryWindowEndTick = Math.Max(recoveryWindowEndTick, tick + RecoveryTicks);
    }

    /// <summary>
    /// 绘制补丁的全局快速门：没有仍可能在收回的拖尾时返回 false，补丁不必逐人物查弱表。
    /// 当前 tick 到达窗口结束时，每条记录的 <see cref="RecoveryScale"/> 都只会返回 0，在此一次清空。
    /// </summary>
    internal static bool AnyRecoveryActive()
    {
        if (recoveryWindowEndTick == NoRecoveryWindow)
        {
            return false;
        }

        // 不在游戏中时 RecoveryScale 一律返回 0。
        if (!TickUtility.TryGetCurrentGameTick(out int tick))
        {
            return false;
        }

        if (tick < recoveryWindowEndTick)
        {
            return true;
        }

        recoveries.Clear();
        recoveryWindowEndTick = NoRecoveryWindow;
        return false;
    }

    /// <summary>收回进度：1 为刚落地，0 为已结束（结束或换图时删除状态）。</summary>
    internal static float RecoveryScale(Pawn? pawn)
    {
        if (pawn is null || !recoveries.TryGetValue(pawn, out Recovery? recovery))
        {
            return 0f;
        }

        int elapsed = TickUtility.CurrentGameTickOrFallback(recovery.StartTick + RecoveryTicks) - recovery.StartTick;
        if (recovery.MapId != MapIdOf(pawn) || elapsed < 0 || elapsed >= RecoveryTicks)
        {
            recoveries.Remove(pawn);
            return 0f;
        }

        float remaining = 1f - (float)elapsed / RecoveryTicks;
        return remaining * remaining * (3f - 2f * remaining);
    }

    /// <summary>在人物的绘制阶段画收回中的拖尾。</summary>
    internal static void DrawRecovery(Pawn pawn)
    {
        float scale = RecoveryScale(pawn);
        if (scale <= 0f || !pawn.Spawned || pawn.ParentHolder is PawnFlyer_LanceCharge)
        {
            return;
        }

        if (recoveries.TryGetValue(pawn, out Recovery? recovery))
        {
            Draw(pawn, recovery.Travel * scale, UnityMathf.Sqrt(scale));
        }
    }

    /// <summary>
    /// 画一帧拖尾，返回提交的图层数。<paramref name="travel"/> 是本帧世界位移；<paramref name="drawPosition"/> 缺省时用人物的绘制位置。
    /// </summary>
    internal static int Draw(Pawn? pawn, UVector3 travel, float opacity = 1f, UVector3? drawPosition = null)
        => Draw(pawn, travel.ToNumerics(), opacity, drawPosition?.ToNumerics());

    /// <summary>同上，参数为 System.Numerics 向量。</summary>
    internal static int Draw(Pawn? pawn, Vector3 travel, float opacity = 1f, Vector3? drawPosition = null)
    {
        PawnRenderer? renderer = pawn?.Drawer?.renderer;
        PawnRenderTree? tree = renderer?.renderTree;
        if (pawn is null || renderer is null || tree is null || UnityVector.SqrMagnitude(travel) < 0.0001f)
        {
            return 0;
        }

        LanceMotionBlurPass? blur = LanceMotionBlurPass.Instance;
        if (blur is null)
        {
            return 0;
        }

        renderer.EnsureGraphicsInitialized();
        // 原版可能只画人物图集，渲染树里仍是图集原点的矩阵。每帧只按飞行位置准备一次矩阵，随后每层一次 GPU 卷积；
        // 不调用 renderTree.Draw，也不生成额外人物副本。
        PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
        parms.facing = pawn.Rotation;
        parms.posture = PawnPosture.Standing;
        parms.flags |= PawnRenderFlags.NeverAimWeapon;
        parms.rotDrawMode = renderer.CurRotDrawMode;
        Vector3 position = (drawPosition ?? pawn.DrawPos.ToNumerics()) + pawn.ageTracker.CurLifeStage.bodyDrawOffset.ToNumerics();
        parms.matrix = UnityMatrix.Translate(position);
        tree.ParallelPreDraw(parms);

        List<PawnGraphicDrawRequest> requests = Requests(tree);
        float alpha = 0.8f * opacity * InvisibilityUtility.GetAlpha(pawn);
        Vector4 flash = renderer.flasher.CurColor.ToNumerics();
        int layers = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            PawnGraphicDrawRequest request = requests[i];
            if (blur.Draw(request.mesh, request.preDrawnComputedMatrix, request.material, travel, flash, alpha))
            {
                layers++;
            }
        }

        return layers;
    }

    private static int MapIdOf(Pawn pawn) => pawn.MapHeld?.uniqueID ?? NoMap;

    // 只记录地图 id：1.6 记录 Map 引用，地图移除后若人物不再绘制，状态会一直持有整张地图。
    private sealed record Recovery(int MapId, int StartTick, Vector3 Travel);
}
