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

        internal ThingWithComps Weapon => wheelComp?.GetBackWeapon(displayIndex);
        internal int DisplayIndex => displayIndex;

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
    }

    public sealed class PawnRenderNodeWorker_BackWeapon : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = node as PawnRenderNode_BackWeapon;
            return backNode?.Weapon != null
                && !parms.flags.FlagSet(PawnRenderFlags.Portrait)
                && !parms.pawn.Dead
                && !parms.pawn.GetPosture().InBed()
                && base.CanDrawNow(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
            float side = backNode.DisplayIndex == 0 ? -0.11f : 0.11f;

            if (parms.facing == Rot4.East)
            {
                offset.x += side * 0.55f;
                offset.z += 0.03f;
            }
            else if (parms.facing == Rot4.West)
            {
                offset.x -= side * 0.55f;
                offset.z += 0.03f;
            }
            else
            {
                if (parms.facing == Rot4.North)
                {
                    side = -side;
                }
                offset.x += side;
                offset.z -= 0.04f;
            }
            return offset;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.facing == Rot4.South)
            {
                return -2f;
            }
            if (parms.facing == Rot4.North)
            {
                return 22f;
            }
            return ((PawnRenderNode_BackWeapon)node).DisplayIndex == 0 ? 18f : -1f;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
            float angle = backNode.DisplayIndex == 0 ? 43f : -43f;
            if (parms.facing == Rot4.East)
            {
                angle += 8f;
            }
            else if (parms.facing == Rot4.West)
            {
                angle -= 8f;
            }
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
            return grooveNode?.Weapon != null
                && !parms.flags.FlagSet(PawnRenderFlags.Portrait)
                && !parms.pawn.Dead
                && !parms.pawn.GetPosture().InBed()
                && base.CanDrawNow(node, parms);
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
                // 原版 utility pack 南向层为 -3；武器略高一层，仍保持在身体之后。
                return -2f;
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
