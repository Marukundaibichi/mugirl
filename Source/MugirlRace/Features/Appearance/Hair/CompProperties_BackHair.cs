using System.Threading;

namespace Mugirl.Features.Appearance;

/// <summary>
/// 发型后发层条目：目标发型与各朝向后发贴图路径，可在 XML 中为每个带后发的发型配置一条。
/// 未配置 hairDef/southPath 时回退到 Mugirl_LongCascade 的默认后发（XNN_Hairs8F）。
/// </summary>
public sealed class CompProperties_BackHair : CompProperties
{
    private const string DefaultSouthPath = "Mugirl/Hair/Mugirl_LongCascadeBack_south";
    private const string DefaultSidePath = "Mugirl/Hair/Mugirl_LongCascadeBack_east";

    public HairDef? hairDef;

    public string? southPath;

    public string? sidePath;

    // 渲染节点属性只由本条目决定，像原版 Def 层节点一样由所有 Pawn 共用；首次构建渲染树时创建。
    [Unsaved]
    private PawnRenderNodeProperties_BackHair? nodeProperties;

    public CompProperties_BackHair()
    {
        compClass = typeof(CompBackHair);
    }

    /// <summary>共用的节点属性。渲染树可能在工作线程上构建，用比较交换保证只发布一个实例。</summary>
    internal PawnRenderNodeProperties_BackHair NodeProperties
    {
        get
        {
            PawnRenderNodeProperties_BackHair? cached = Volatile.Read(ref nodeProperties);
            if (cached is not null)
            {
                return cached;
            }

            PawnRenderNodeProperties_BackHair created = PawnRenderNodeProperties_BackHair.Create(this);
            return Interlocked.CompareExchange(ref nodeProperties, created, null) ?? created;
        }
    }

    public override void ResolveReferences(ThingDef parentDef)
    {
        base.ResolveReferences(parentDef);
        bool isDefaultEntry = southPath.NullOrEmpty();
        hairDef ??= AppearanceDefOf.Mugirl_LongCascade;
        if (southPath.NullOrEmpty())
        {
            southPath = DefaultSouthPath;
        }

        if (sidePath.NullOrEmpty())
        {
            // 默认条目沿用 LongCascade 的东西向后发；显式留空 sidePath 表示该发型没有侧向后发（仅南向绘制）。
            sidePath = isDefaultEntry ? DefaultSidePath : null;
        }
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }

        if (hairDef is null)
        {
            yield return $"{parentDef.defName}: CompProperties_LongCascadeBackHair 条目的 hairDef 未解析（DefOf 缺失或被移除），后发层不会绘制。";
        }
    }
}
