using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public sealed class Thing_MugirlDunkHead : Thing
    {
        private HeadTypeDef headType;
        private Color skinColor = Color.white;
        private HairDef hairDef;
        private Color hairColor = Color.white;
        private Pawn sourcePawn;
        private Graphic headGraphic;
        private Graphic hairGraphic;
        private Mesh headMesh;
        private Mesh hairMesh;
        private float headWidth = 1f;
        private float headHeight = 1f;
        private float hairWidth;
        private float hairHeight;
        private string headGraphicPath;
        private string headMaskPath;
        private string headShaderName;
        private Color headGraphicColor = Color.white;
        private Color headGraphicColorTwo = Color.white;
        // 每帧绘制解析结果缓存；头/发贴图与材质在 Initialize 后不变，null 不缓存以保留原有重试语义。
        private Graphic headDrawGraphic;
        private Material headDrawMaterial;
        private Graphic hairDrawGraphic;
        private Material hairDrawMaterial;

        internal Vector2 HeadDrawSize => new Vector2(headWidth, headHeight);
        internal Vector2 HairDrawSize => new Vector2(hairWidth, hairHeight);

        internal void Initialize(Pawn victim)
        {
            sourcePawn = victim;
            headType = victim.story.headType;
            skinColor = victim.story.SkinColor;
            hairDef = victim.story.hairDef;
            hairColor = victim.story.HairColor;
            victim.Drawer.renderer.EnsureGraphicsInitialized();
            PawnRenderTree tree = victim.Drawer.renderer.renderTree;
            headGraphic = tree?.HeadGraphic ?? headType?.GetGraphic(victim, skinColor);
            if (headGraphic != null)
            {
                headGraphicPath = headGraphic.path;
                headMaskPath = headGraphic.maskPath;
                headShaderName = headGraphic.Shader?.name;
                headGraphicColor = headGraphic.Color;
                headGraphicColorTwo = headGraphic.ColorTwo;
            }

            PawnRenderNode headNode;
            GraphicMeshSet headMeshSet = tree != null
                && tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Head, out headNode) && headNode != null
                ? headNode.MeshSetFor(victim)
                : null;
            if (headMeshSet == null)
            {
                headMeshSet = HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(victim);
            }
            Vector2 headSize = MeshSize(headMeshSet);
            headWidth = headSize.x;
            headHeight = headSize.y;

            hairGraphic = hairDef != null && !hairDef.noGraphic
                ? hairDef.GraphicFor(victim, hairColor)
                : null;
            Vector2 hairSize = hairGraphic != null
                ? MeshSize(HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(victim))
                : Vector2.zero;
            hairWidth = hairSize.x;
            hairHeight = hairSize.y;
            headMesh = null;
            hairMesh = null;
        }

        internal void DrawHeadAt(Vector3 position, float spin)
        {
            if (headMesh == null)
            {
                headMesh = MeshPool.GetMeshSetForSize(headWidth, headHeight).MeshAt(Rot4.South);
            }
            if (headDrawMaterial == null)
            {
                headDrawGraphic = Graphic?.GetShadowlessGraphic();
                headDrawMaterial = headDrawGraphic?.MatAt(Rot4.South, this);
            }
            DrawPart(headDrawGraphic, headDrawMaterial, headMesh, position, spin);

            if (hairDef == null || hairDef.noGraphic || hairWidth <= 0f || hairHeight <= 0f)
            {
                return;
            }

            if (hairGraphic == null && sourcePawn != null)
            {
                hairGraphic = hairDef.GraphicFor(sourcePawn, hairColor);
            }

            position.y += 0.005f;
            if (hairMesh == null)
            {
                hairMesh = MeshPool.GetMeshSetForSize(hairWidth, hairHeight).MeshAt(Rot4.South);
            }
            if (hairDrawMaterial == null)
            {
                hairDrawGraphic = hairGraphic?.GetShadowlessGraphic();
                hairDrawMaterial = hairDrawGraphic?.MatAt(Rot4.South, this);
            }
            DrawPart(hairDrawGraphic, hairDrawMaterial, hairMesh, position, spin);
        }

        private static Vector2 MeshSize(GraphicMeshSet meshSet)
        {
            Mesh mesh = meshSet?.MeshAt(Rot4.South);
            if (mesh == null)
            {
                return Vector2.one;
            }

            Vector3 size = mesh.bounds.size;
            return new Vector2(size.x, size.z);
        }

        private void DrawPart(Graphic graphic, Material material, Mesh mesh, Vector3 position, float spin)
        {
            if (material == null || mesh == null)
            {
                return;
            }

            Quaternion rotation = graphic.QuatFromRot(Rot4.South) * Quaternion.Euler(Vector3.up * spin);
            Graphics.DrawMesh(mesh, position + graphic.DrawOffset(Rot4.South), rotation, material, 0);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (phase == DrawPhase.Draw)
            {
                DrawHeadAt(drawLoc, 0f);
            }
        }

        public override Graphic Graphic
        {
            get
            {
                if (headGraphic == null && headType != null)
                {
                    if (!headGraphicPath.NullOrEmpty())
                    {
                        Shader shader = headShaderName.NullOrEmpty() ? null : Shader.Find(headShaderName);
                        headGraphic = GraphicDatabase.Get(typeof(Graphic_Multi), headGraphicPath,
                            shader ?? ShaderDatabase.Cutout, Vector2.one,
                            headGraphicColor, headGraphicColorTwo, headMaskPath);
                    }
                    else if (sourcePawn != null && !sourcePawn.Destroyed)
                    {
                        headGraphic = headType.GetGraphic(sourcePawn, skinColor);
                    }
                    else if (!headType.graphicPath.NullOrEmpty())
                    {
                        headGraphic = GraphicDatabase.Get<Graphic_Multi>(
                            headType.graphicPath, ShaderDatabase.Cutout, Vector2.one, skinColor);
                    }
                }

                return headGraphic ?? base.Graphic;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref headType, "headType");
            Scribe_Values.Look(ref skinColor, "skinColor", Color.white);
            Scribe_Defs.Look(ref hairDef, "hairDef");
            Scribe_Values.Look(ref hairColor, "hairColor", Color.white);
            Scribe_References.Look(ref sourcePawn, "sourcePawn");
            Scribe_Values.Look(ref headWidth, "headWidth", 1f);
            Scribe_Values.Look(ref headHeight, "headHeight", 1f);
            Scribe_Values.Look(ref hairWidth, "hairWidth", 0f);
            Scribe_Values.Look(ref hairHeight, "hairHeight", 0f);
            Scribe_Values.Look(ref headGraphicPath, "headGraphicPath");
            Scribe_Values.Look(ref headMaskPath, "headMaskPath");
            Scribe_Values.Look(ref headShaderName, "headShaderName");
            Scribe_Values.Look(ref headGraphicColor, "headGraphicColor", Color.white);
            Scribe_Values.Look(ref headGraphicColorTwo, "headGraphicColorTwo", Color.white);
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
}
