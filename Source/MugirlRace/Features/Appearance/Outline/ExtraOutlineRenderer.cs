using System.Runtime.CompilerServices;
using Mugirl.Features.WeaponWheel;
using Mugirl.Platform.Rendering;
using Mugirl.Rendering;

namespace Mugirl.Features.Appearance;

/// <summary>
/// Mugirl 人物的额外黑色描边：在原版绘制每个轮廓图层之前，用原生着色器 Mugirl_ExtraOutline 把该图层的 alpha 向外膨胀。
/// 全部描边放在全部人物图层之下，衣物重叠处没有接缝。地图上延迟绘制；人物图集与肖像烘焙时立即绘制，因此远景图集与殖民者栏同样有描边。
/// </summary>
internal static class ExtraOutlineRenderer
{
    internal const float DefaultWidth = MugirlSettings.ExtraOutlineDefaultWidth;
    internal const float MinWidth = MugirlSettings.ExtraOutlineMinWidth;
    internal const float MaxWidth = MugirlSettings.ExtraOutlineMaxWidth;
    private const float WidthInCells = 0.012f;
    private const float DepthOffset = 0.0005f;

    // 超过该数量的绘制请求时改用堆数组记录待描边图层（原版人物通常 10–25 个图层）。
    private const int MaxStackRequests = 64;

    // StaticCacheLifecycle: 只存弱引用，设置改变时用来让已有的图集与肖像失效（含已死亡或不在地图上的人物）；
    // 每加入 64 个时清理已回收的项。只在主线程（绘制阶段）读写。
    private static readonly List<WeakReference> seenPawns = [];
    private static readonly ConditionalWeakTable<Pawn, WeakReference> pawnSeen = new();

    /// <summary>
    /// 在原版 <c>PawnRenderTree.Draw</c> 之前为最终的绘制请求画描边（不遍历渲染树、不重算动画、不修改请求）。
    /// </summary>
    internal static void DrawBody(Pawn? pawn, List<PawnGraphicDrawRequest>? requests, in PawnDrawParms parms)
    {
        // PawnDrawParms 不是只读结构：经 in 参数调用其属性（Statue、DrawNow）会复制整个结构，所以直接读 flags 字段（两个属性都只检查 flags）。
        PawnRenderFlags flags = parms.flags;
        MugirlSettings? settings = MugirlMod.Settings;
        if (settings is null || !settings.enableExtraOutline || !MugirlIdentity.IsMugirlPawn(pawn) || requests is null
            || flags.FlagSet(PawnRenderFlags.Invisible) || flags.FlagSet(PawnRenderFlags.Statue))
        {
            return;
        }

        ExtraOutlinePass? outline = ExtraOutlinePass.Instance;
        if (outline is null)
        {
            return;
        }

        Track(pawn);
        int count = requests.Count;
        Span<int> outlined = count <= MaxStackRequests ? stackalloc int[MaxStackRequests] : new int[count];
        int outlinedCount = 0;
        float lowest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            PawnGraphicDrawRequest request = requests[i];
            if (IsWeaponNode(request.node) || !IsSilhouette(request.material))
            {
                continue;
            }

            lowest = UnityMathf.Min(lowest, request.preDrawnComputedMatrix.m13);
            outlined[outlinedCount++] = i;
        }

        float width = UnityMathf.Clamp(settings.extraOutlineWidth, MinWidth, MaxWidth) * WidthInCells;
        bool drawNow = flags.FlagSet(PawnRenderFlags.DrawNow);
        float tintAlpha = parms.tint.a;
        for (int i = 0; i < outlinedCount; i++)
        {
            PawnGraphicDrawRequest request = requests[outlined[i]];
            UMatrix4x4 matrix = request.preDrawnComputedMatrix;
            // 膨胀对并集可分配：把全部轮廓图层的描边放到全部正常图层之下，重叠的衣物之间不产生接缝。
            matrix.m13 = lowest - DepthOffset;
            outline.Draw(request.mesh, matrix, EngineMaterialView.Of(request.material), drawNow, tintAlpha, width);
        }
    }

    private static bool IsWeaponNode(PawnRenderNode? node)
    {
        // 额外描边只处理人物轮廓；背挂与自动装填槽武器仍由原绘制路径显示。
        return node is PawnRenderNode_BackWeapon || node is PawnRenderNode_AutoloadingWeapon;
    }

    private static bool IsSilhouette(Material? material)
        => material is not null && PawnSilhouetteShaders.Contains(EngineMaterialView.Of(material).ShaderName);

    private static void Track(Pawn pawn)
    {
        if (pawnSeen.TryGetValue(pawn, out _))
        {
            return;
        }

        var reference = new WeakReference(pawn);
        pawnSeen.Add(pawn, reference);
        if (seenPawns.Count % 64 == 0)
        {
            for (int i = seenPawns.Count - 1; i >= 0; i--)
            {
                if (!seenPawns[i].IsAlive)
                {
                    seenPawns.RemoveAt(i);
                }
            }
        }

        seenPawns.Add(reference);
    }

    internal static void SettingsChanged()
    {
        // 只在界面上真正改了设置后调用，从不在正常 tick 或绘制中调用。
        for (int i = seenPawns.Count - 1; i >= 0; i--)
        {
            if (seenPawns[i].Target is not Pawn pawn || pawn.Destroyed)
            {
                seenPawns.RemoveAt(i);
                continue;
            }

            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            PortraitsCache.SetDirty(pawn);
        }

        // 功能关闭期间绘制过的人物还没有被记录。
        if (!GameUtility.IsPlaying())
        {
            return;
        }

        foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
        {
            if (!MugirlIdentity.IsMugirlPawn(pawn))
            {
                continue;
            }

            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            PortraitsCache.SetDirty(pawn);
        }
    }
}
