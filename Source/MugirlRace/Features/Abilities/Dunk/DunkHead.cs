using Mugirl.Rendering;

namespace Mugirl.Features.Abilities;

// 扣篮用的头部物件（XML thingClass，存档按类名保存）：记录受害者头部与头发的贴图、颜色和网格尺寸，读档后据此重建。
public sealed class DunkHead : Thing
{
    private HeadTypeDef? headType;
    private Color skinColor = UnityColor.White;
    private HairDef? hairDef;
    private Color hairColor = UnityColor.White;
    private Pawn? sourcePawn;
    private Graphic? headGraphic;
    private Graphic? hairGraphic;
    private Mesh? headMesh;
    private Mesh? hairMesh;
    private float headWidth = 1f;
    private float headHeight = 1f;
    private float hairWidth;
    private float hairHeight;
    private string? headGraphicPath;
    private string? headMaskPath;
    private string? headShaderName;
    private Color headGraphicColor = UnityColor.White;
    private Color headGraphicColorTwo = UnityColor.White;
    // 每帧绘制解析结果缓存；头/发贴图与材质在 Initialize 后不变，null 不缓存以保留原有重试语义。
    private Graphic? headDrawGraphic;
    private Material? headDrawMaterial;
    private Graphic? hairDrawGraphic;
    private Material? hairDrawMaterial;

    public override Graphic Graphic
    {
        get
        {
            if (headGraphic is null && headType is not null)
            {
                headGraphic = RebuildHeadGraphic(headType);
            }

            return headGraphic ?? base.Graphic;
        }
    }

    internal void Initialize(Pawn victim)
    {
        sourcePawn = victim;
        headType = victim.story.headType;
        skinColor = victim.story.SkinColor;
        hairDef = victim.story.hairDef;
        hairColor = victim.story.HairColor;
        victim.Drawer.renderer.EnsureGraphicsInitialized();
        PawnRenderTree? tree = victim.Drawer.renderer.renderTree;
        headGraphic = tree?.HeadGraphic ?? headType?.GetGraphic(victim, skinColor);
        if (headGraphic is not null)
        {
            headGraphicPath = headGraphic.path;
            headMaskPath = headGraphic.maskPath;
            headShaderName = headGraphic.Shader?.name;
            headGraphicColor = headGraphic.Color;
            headGraphicColorTwo = headGraphic.ColorTwo;
        }

        GraphicMeshSet? headMeshSet = null;
        if (tree is not null && tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Head, out PawnRenderNode? headNode) && headNode is not null)
        {
            headMeshSet = headNode.MeshSetFor(victim);
        }

        headMeshSet ??= HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(victim);
        Vector2 headSize = MeshSize(headMeshSet);
        headWidth = headSize.X;
        headHeight = headSize.Y;

        hairGraphic = hairDef is not null && !hairDef.noGraphic
            ? hairDef.GraphicFor(victim, hairColor)
            : null;
        Vector2 hairSize = hairGraphic is not null
            ? MeshSize(HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(victim))
            : Vector2.Zero;
        hairWidth = hairSize.X;
        hairHeight = hairSize.Y;
        headMesh = null;
        hairMesh = null;
    }

    internal void DrawHeadAt(Vector3 position, float spin)
    {
        headMesh ??= MeshPool.GetMeshSetForSize(headWidth, headHeight).MeshAt(Rot4.South);
        if (headDrawMaterial is null)
        {
            headDrawGraphic = Graphic?.GetShadowlessGraphic();
            headDrawMaterial = headDrawGraphic?.MatAt(Rot4.South, this);
        }

        DrawPart(headDrawGraphic, headDrawMaterial, headMesh, position, spin);

        if (hairDef is null || hairDef.noGraphic || hairWidth <= 0f || hairHeight <= 0f)
        {
            return;
        }

        if (hairGraphic is null && sourcePawn is not null)
        {
            hairGraphic = hairDef.GraphicFor(sourcePawn, hairColor);
        }

        position.Y += 0.005f;
        hairMesh ??= MeshPool.GetMeshSetForSize(hairWidth, hairHeight).MeshAt(Rot4.South);
        if (hairDrawMaterial is null)
        {
            hairDrawGraphic = hairGraphic?.GetShadowlessGraphic();
            hairDrawMaterial = hairDrawGraphic?.MatAt(Rot4.South, this);
        }

        DrawPart(hairDrawGraphic, hairDrawMaterial, hairMesh, position, spin);
    }

    // 网格的平面尺寸（宽, 纵深）。Verse 的平面网格都由 MeshMakerPlanes 生成并在 MeshPool 登记尺寸；
    // 1.6 读取 Mesh.bounds，在 1.7 中那是对引擎网格的兼容层桥接，这里改读 MeshPool 的登记数据，结果相同。
    private static Vector2 MeshSize(GraphicMeshSet? meshSet)
    {
        Mesh? mesh = meshSet?.MeshAt(Rot4.South);
        if (mesh is null)
        {
            return Vector2.One;
        }

        try
        {
            return MeshPool.GetMetaData(mesh).size.ToNumerics();
        }
        catch (KeyNotFoundException)
        {
            // 非 MeshPool 生成的网格（其他模组自建）没有登记尺寸，按 1×1 绘制。
            return Vector2.One;
        }
    }

    private static void DrawPart(Graphic? graphic, Material? material, Mesh? mesh, Vector3 position, float spin)
    {
        if (graphic is null || material is null || mesh is null)
        {
            return;
        }

        Quaternion rotation = UnityQuaternion.Multiply(graphic.QuatFromRot(Rot4.South).ToNumerics(), UnityQuaternion.Euler(0f, spin, 0f));
        VfxDraw.Mesh(mesh, position + graphic.DrawOffset(Rot4.South).ToNumerics(), rotation, material);
    }

    public override void DynamicDrawPhaseAt(DrawPhase phase, UVector3 drawLoc, bool flip = false)
    {
        if (phase == DrawPhase.Draw)
        {
            DrawHeadAt(drawLoc.ToNumerics(), 0f);
        }
    }

    private Graphic? RebuildHeadGraphic(HeadTypeDef type)
    {
        if (!headGraphicPath.NullOrEmpty())
        {
            return GraphicDatabase.Get(typeof(Graphic_Multi), headGraphicPath,
                ResolveSavedShader(headShaderName) ?? ShaderDatabase.Cutout, new UVector2(1f, 1f),
                headGraphicColor, headGraphicColorTwo, headMaskPath);
        }

        if (sourcePawn is not null && !sourcePawn.Destroyed)
        {
            return type.GetGraphic(sourcePawn, skinColor);
        }

        if (!type.graphicPath.NullOrEmpty())
        {
            return GraphicDatabase.Get<Graphic_Multi>(type.graphicPath, ShaderDatabase.Cutout, new UVector2(1f, 1f), skinColor);
        }

        return null;
    }

    // 存档中的着色器名：1.6 存档为带路径前缀的 Unity 着色器名，1.7 为 ShaderDef 名（late-bound 着色器的 name）。
    // 1.7 的 Shader.Find 走资源加载、找不到 ShaderDef 着色器，改用 ShaderDatabase：与原版一样取最后一段作为 ShaderDef 名；
    // 没有对应 ShaderDef 时与 1.6 的 Shader.Find 失败一样退回 Cutout。按存档名查找，无法用 DefOf。
    private static Shader? ResolveSavedShader(string? savedName)
    {
        if (string.IsNullOrEmpty(savedName))
        {
            return null;
        }

        int slash = savedName.LastIndexOf('/');
        string defName = slash < 0 ? savedName : savedName[(slash + 1)..];
        return DefDatabase<ShaderDef>.GetNamedSilentFail(defName) is not null ? ShaderDatabase.LoadShader(savedName) : null;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref headType, "headType");
        Scribe_Values.Look(ref skinColor, "skinColor", UnityColor.White);
        Scribe_Defs.Look(ref hairDef, "hairDef");
        Scribe_Values.Look(ref hairColor, "hairColor", UnityColor.White);
        Scribe_References.Look(ref sourcePawn, "sourcePawn");
        Scribe_Values.Look(ref headWidth, "headWidth", 1f);
        Scribe_Values.Look(ref headHeight, "headHeight", 1f);
        Scribe_Values.Look(ref hairWidth, "hairWidth", 0f);
        Scribe_Values.Look(ref hairHeight, "hairHeight", 0f);
        Scribe_Values.Look(ref headGraphicPath, "headGraphicPath");
        Scribe_Values.Look(ref headMaskPath, "headMaskPath");
        Scribe_Values.Look(ref headShaderName, "headShaderName");
        Scribe_Values.Look(ref headGraphicColor, "headGraphicColor", UnityColor.White);
        Scribe_Values.Look(ref headGraphicColorTwo, "headGraphicColorTwo", UnityColor.White);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            headGraphic = null;
            hairGraphic = null;
            headMesh = null;
            hairMesh = null;
            headDrawGraphic = null;
            headDrawMaterial = null;
            hairDrawGraphic = null;
            hairDrawMaterial = null;
        }
    }
}
