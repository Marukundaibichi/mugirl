using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    public sealed class PawnRenderNode_BackWeapon : PawnRenderNode
    {
        private readonly Comp_WeaponWheel wheelComp;
        private readonly int displayIndex;
        private Rot4 lastMeshFacing = Rot4.Invalid;
        private int lastMirrorFlags;

        internal ThingWithComps Weapon => wheelComp?.GetBackWeapon(displayIndex);
        internal int DisplayIndex => displayIndex;
        internal bool HasAutoloadingSystem => wheelComp?.HasAutoloadingSystem == true;
        internal bool PreviewActive => wheelComp?.BackLayoutPreviewActive == true;
        internal bool VisibleInStorage => wheelComp?.IsBackWeaponVisible(displayIndex) == true;
        public override Graphic PrimaryGraphic => GraphicFor(wheelComp?.Pawn);
        public override bool RecacheRequested => base.RecacheRequested
            || (lastMeshFacing.IsValid && MirrorFlags(lastMeshFacing) != lastMirrorFlags);

        private int MirrorFlags(Rot4 facing)
        {
            WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(displayIndex, facing);
            return (entry.FlipHorizontal ? 1 : 0) | (entry.FlipVertical ? 2 : 0);
        }

        public PawnRenderNode_BackWeapon(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree,
            Comp_WeaponWheel wheelComp,
            int displayIndex)
            : base(pawn, props, tree)
        {
            this.wheelComp = wheelComp;
            this.displayIndex = displayIndex;
            wheelComp?.RegisterBackWeaponNode(this);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            ThingWithComps weapon = Weapon;
            Graphic graphic = weapon?.Graphic;
            if (graphic == null)
            {
                return null;
            }
            // NodeGetMat 用 parms.pawn 而不是武器实例解析 Graphic_Random 等集合贴图，
            // 会选中与手持时不同的文化风格差分；这里先按武器本体解析到具体子贴图。
            if (graphic is Graphic_StackCount stackCountGraphic)
            {
                return stackCountGraphic.SubGraphicForStackCount(1, weapon.def);
            }
            return graphic.ExtractInnerGraphicFor(weapon);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            Graphic graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            // 人物网格的 backLift 相当于 5 层；枪械旋转后会穿过身体/装弹架。
            // 南向收纳武器使用原版缓存的平面网格，保留尺寸、UV 和镜像规则。
            Mesh mesh;
            if (parms.facing == Rot4.South)
            {
                mesh = MeshPool.GridPlane(Vector2.one);
                if (FlipGraphic(parms)) mesh = MeshPool.GridPlaneFlip(mesh);
            }
            else mesh = base.GetMesh(parms);
            lastMeshFacing = parms.facing;
            lastMirrorFlags = MirrorFlags(parms.facing);
            return WeaponWheelBackMirrorMeshes.Get(mesh, lastMirrorFlags);
        }
    }

    [StaticConstructorOnStartup]
    internal static class WeaponWheelBackMirrorMeshes
    {
        // StaticCacheLifecycle: 进程级只读网格，启动主线程上预建；仅含单位网格的有限镜像组合，
        // 不引用 Pawn，不修改 MeshPool 的共享顶点，游戏退出时由 Unity 回收。
        private static readonly Dictionary<Mesh, Mesh[]> Variants = new Dictionary<Mesh, Mesh[]>();

        static WeaponWheelBackMirrorMeshes()
        {
            GraphicMeshSet source = MeshPool.GetMeshSetForSize(1f, 1f);
            for (int facing = 0; facing < 4; facing++)
            {
                Mesh mesh = source.MeshAt(new Rot4(facing));
                Add(mesh);
                Add(MeshPool.GridPlaneFlip(mesh));
            }
            Mesh flat = MeshPool.GridPlane(Vector2.one);
            Add(flat);
            Add(MeshPool.GridPlaneFlip(flat));
        }

        private static void Add(Mesh source)
        {
            if (Variants.ContainsKey(source)) return;
            Mesh[] variants = new Mesh[4];
            variants[0] = source;
            for (int flags = 1; flags < 4; flags++)
            {
                Mesh mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = "Mugirl back weapon mirror " + flags;
                Vector2[] uv = source.uv;
                for (int i = 0; i < uv.Length; i++)
                {
                    if ((flags & 1) != 0) uv[i].x = 1f - uv[i].x;
                    if ((flags & 2) != 0) uv[i].y = 1f - uv[i].y;
                }
                mesh.uv = uv;
                variants[flags] = mesh;
            }
            Variants.Add(source, variants);
        }

        internal static Mesh Get(Mesh source, int flags)
        {
            return source != null && Variants.TryGetValue(source, out Mesh[] variants)
                ? variants[flags & 3] : source;
        }
    }

    public sealed class PawnRenderNodeWorker_BackWeapon : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = node as PawnRenderNode_BackWeapon;
            if ((parms.Portrait && backNode?.PreviewActive != true) || parms.pawn.Dead || parms.pawn.GetPosture().InBed())
            {
                return false;
            }
            // 设置项：隐藏普通背负武器（仅背上的两把；自驱装弹凹槽武器不受影响），调试窗口预览不受此开关影响。
            if (MugirlMod.Settings?.hideWeaponWheelBackWeapons == true && backNode?.PreviewActive != true)
            {
                return false;
            }
            return backNode?.VisibleInStorage == true && base.CanDrawNow(node, parms);
        }

        // 位置、角度与图层全部来自 WeaponWheelBackLayout（每帧读取），
        // 开发者模式的布局调试窗口改动后立即生效，无需重建渲染树。
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(
                ((PawnRenderNode_BackWeapon)node).DisplayIndex, parms.facing);
            offset.x += entry.OffsetX;
            offset.z += entry.OffsetZ;
            return offset;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
            if (parms.facing == Rot4.South && backNode.HasAutoloadingSystem)
            {
                // 穿戴装弹系统时，普通背负枪位于南向装弹架（-3 层）后方。
                return WeaponWheelBackLayout.SouthLayerWithAutoloading;
            }
            return WeaponWheelBackLayout.GetEntry(backNode.DisplayIndex, parms.facing).Layer;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            float angle = WeaponWheelBackLayout.GetEntry(
                ((PawnRenderNode_BackWeapon)node).DisplayIndex, parms.facing).Angle;
            return Quaternion.AngleAxis(angle, Vector3.up);
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            ThingWithComps weapon = ((PawnRenderNode_BackWeapon)node).Weapon;
            Vector2 drawSize = weapon?.Graphic?.drawSize ?? Vector2.one;
            return new Vector3(drawSize.x, 1f, drawSize.y);
        }
    }

    public sealed class PawnRenderNode_AutoloadingWeapon : PawnRenderNode
    {
        private readonly Comp_WeaponWheel wheelComp;
        private readonly int displayIndex;

        internal ThingWithComps Weapon => wheelComp?.GetAutoloadingWeapon(displayIndex);
        internal int DisplayIndex => displayIndex;
        internal bool PreviewActive => wheelComp?.BackLayoutPreviewActive == true;
        public override Graphic PrimaryGraphic => GraphicFor(wheelComp?.Pawn);

        public PawnRenderNode_AutoloadingWeapon(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree,
            Comp_WeaponWheel wheelComp,
            int displayIndex)
            : base(pawn, props, tree)
        {
            this.wheelComp = wheelComp;
            this.displayIndex = displayIndex;
            wheelComp?.RegisterAutoloadingWeaponNode(this);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            ThingWithComps weapon = Weapon;
            Graphic graphic = weapon?.Graphic;
            if (graphic == null)
            {
                return null;
            }
            if (graphic is Graphic_StackCount stackCountGraphic)
            {
                return stackCountGraphic.SubGraphicForStackCount(1, weapon.def);
            }
            return graphic.ExtractInnerGraphicFor(weapon);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            Graphic graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }
        public override Mesh GetMesh(PawnDrawParms parms)
        {
            // 南向槽枪旋转 90° 后也必须保持等高，不能使用人物网格的 backLift。
            if (parms.facing == Rot4.South)
            {
                Mesh mesh = MeshPool.GridPlane(Vector2.one);
                return FlipGraphic(parms) ? MeshPool.GridPlaneFlip(mesh) : mesh;
            }
            return base.GetMesh(parms);
        }
    }

    public sealed class PawnRenderNodeWorker_AutoloadingWeapon : PawnRenderNodeWorker
    {
        // 512px 穿戴贴图在 Mugirl 的 1.5 倍身体画布上绘制；这些值对应四个槽口中心，
        // 不能按原版 1.0 倍人体画布折算，否则四把武器会一起挤到腰带中央。
        private static readonly float[] FrontX = { -0.54f, -0.35f, 0.35f, 0.54f };
        private static readonly Vector2[] SideOffsets =
        {
            new Vector2(-0.71f, -0.17f),
            new Vector2(-0.51f, -0.17f),
            new Vector2(-0.71f, -0.38f),
            new Vector2(-0.51f, -0.38f)
        };

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_AutoloadingWeapon grooveNode = node as PawnRenderNode_AutoloadingWeapon;
            if ((parms.Portrait && grooveNode?.PreviewActive != true) || parms.pawn.Dead || parms.pawn.GetPosture().InBed())
            {
                return false;
            }
            return grooveNode?.Weapon != null && base.CanDrawNow(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            int index = ((PawnRenderNode_AutoloadingWeapon)node).DisplayIndex;
            if (parms.facing == Rot4.South || parms.facing == Rot4.North)
            {
                offset.x += FrontX[index];
                offset.z += parms.facing == Rot4.South ? -0.36f : -0.30f;
                return offset;
            }

            Vector2 side = SideOffsets[index];
            offset.x += parms.facing == Rot4.East ? side.x : -side.x;
            offset.z += side.y;
            return offset;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.facing == Rot4.South)
            {
                // 身体前、装弹架（-3）中、凹槽枪（-4..-7）后。
                // 南向使用无 backLift 的网格，顶点不会再越过架体平面；层值均在原版 -10..100 范围内。
                return -4f - ((PawnRenderNode_AutoloadingWeapon)node).DisplayIndex;
            }
            if (parms.facing == Rot4.North)
            {
                // 原版 utility pack 北向层为 93；武器覆盖凹槽但不被底图吞掉。
                return 94f;
            }
            return 1f;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            return Quaternion.AngleAxis(parms.facing == Rot4.West ? -90f : 90f, Vector3.up);
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            ThingWithComps weapon = ((PawnRenderNode_AutoloadingWeapon)node).Weapon;
            Vector2 drawSize = weapon?.Graphic?.drawSize ?? Vector2.one;
            // 凹槽只提供收纳锚点，不改变武器本身的显示尺寸。
            // 与普通背负节点保持一致，直接沿用武器 Graphic 的完整 drawSize。
            return new Vector3(drawSize.x, 1f, drawSize.y);
        }
    }
}
