using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal enum SegmentedMechTailMeshPart
    {
        Segments,
        Tip
    }

    /// <summary>
    /// 无 tick、无物理、无逐帧对象分配的分节机械尾。
    /// 同材质尾节合并为一个动态 Mesh，尾尖使用第二个 Mesh。
    /// </summary>
    public sealed class Comp_SegmentedMechTail : ThingComp
    {
        private Graphic segmentGraphic;
        private Graphic tipGraphic;
        private Graphic activeTipGraphic;

        private Mesh segmentMesh;
        private Mesh tipMesh;
        private Vector3[] segmentVertices;
        private Vector3[] tipVertices;
        private Vector2[] joints;
        private Vector2[] segmentUvs;
        private Vector2[] tipUvs;
        private int[] segmentTriangles;
        private int[] tipTriangles;
        private Bounds meshBounds;

        private BodyPartRecord requiredTailPartRecord;
        private bool requiredTailPartResolved;
        private bool resourcesReady;
        private bool runtimeResourcesQueued;

        private int cachedTick = int.MinValue;
        private int cachedFacing = -1;
        private bool cachedDead;
        private int cachedPoseRevision = int.MinValue;
        private int cachedOperationalTick = int.MinValue;
        private bool cachedOperational;

        private int attackStartTick = int.MinValue;
        private Vector2 attackTargetOffset;
        private int poseRevision;
        private int lastAssistTick = -999999;

        public CompProperties_SegmentedMechTail Props =>
            (CompProperties_SegmentedMechTail)props;

        private Pawn Pawn => parent as Pawn;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (runtimeResourcesQueued)
            {
                return;
            }
            runtimeResourcesQueued = true;
            // 建图可能在长事件工作线程生成此 Pawn；Unity 网格和图形资源
            // 必须等事件结束、回到主线程后再创建。
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                runtimeResourcesQueued = false;
                if (parent == null || parent.Destroyed || !parent.Spawned)
                {
                    return;
                }
                EnsureRuntimeResources();
                Pawn pawn = Pawn;
                pawn?.Drawer?.renderer?.renderTree?.SetDirty();
            });
        }

        public override List<PawnRenderNode> CompRenderNodes()
        {
            Pawn pawn = Pawn;
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null || !resourcesReady)
            {
                return null;
            }

            List<PawnRenderNode> nodes = new List<PawnRenderNode>(2)
            {
                CreateNode(pawn, tree, SegmentedMechTailMeshPart.Segments),
                CreateNode(pawn, tree, SegmentedMechTailMeshPart.Tip)
            };
            return nodes;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ReleaseRuntimeResources();
        }

        internal Graphic GraphicFor(SegmentedMechTailMeshPart part, bool active)
        {
            if (part == SegmentedMechTailMeshPart.Segments)
            {
                return segmentGraphic;
            }
            return active && activeTipGraphic != null ? activeTipGraphic : tipGraphic;
        }

        internal Mesh MeshFor(SegmentedMechTailMeshPart part)
        {
            return part == SegmentedMechTailMeshPart.Segments ? segmentMesh : tipMesh;
        }

        internal bool ShouldUseActiveTip(PawnDrawParms parms)
        {
            if (parms.Portrait || parms.pawn == null || parms.pawn.Dead)
            {
                return false;
            }
            int tick = CurrentTick;
            bool inAttackWindow = attackStartTick != int.MinValue
                && tick >= attackStartTick
                && tick - attackStartTick < Props.attackAnimationTicks;
            return inAttackWindow && TailOperational();
        }

        internal void PrepareMeshes(PawnDrawParms parms)
        {
            if (!resourcesReady || parms.Portrait)
            {
                return;
            }
            int tick = CurrentTick;
            int facing = parms.facing.AsInt;
            bool dead = parms.pawn?.Dead == true;
            bool attackActive = !dead
                && attackStartTick != int.MinValue
                && tick >= attackStartTick
                && tick - attackStartTick < Props.attackAnimationTicks;
            int poseTick = dead
                ? 0
                : attackActive
                    ? tick
                    : tick - tick % Mathf.Max(1, Props.idleAnimationIntervalTicks);
            if (cachedTick == poseTick
                && cachedFacing == facing
                && cachedDead == dead
                && cachedPoseRevision == poseRevision)
            {
                return;
            }

            cachedTick = poseTick;
            cachedFacing = facing;
            cachedDead = dead;
            cachedPoseRevision = poseRevision;

            if (!TailOperational())
            {
                CollapseMeshes();
                return;
            }

            BuildRestPose(parms.facing, poseTick, dead);
            float attackWeight = AttackWeight(tick, dead);
            if (attackWeight > 0.0001f)
            {
                PullPoseTowardAttackTarget(attackWeight);
            }

            UpdateSegmentMesh();
            UpdateTipMesh();
        }

        internal void NotifyMeleeAttack(Thing target)
        {
            Pawn pawn = Pawn;
            if (pawn == null || target == null || !TailOperational())
            {
                return;
            }

            Vector3 delta = target.DrawPos - pawn.DrawPos;
            attackTargetOffset = new Vector2(delta.x, delta.z);
            if (attackTargetOffset.sqrMagnitude < 0.0001f)
            {
                attackTargetOffset = FacingVector(pawn.Rotation);
            }
            attackStartTick = CurrentTick;
            poseRevision++;
        }

        internal bool TryApplyAssistDamage(Thing target, out DamageWorker.DamageResult result)
        {
            result = null;
            Pawn pawn = Pawn;
            if (pawn == null
                || target == null
                || target.Destroyed
                || !pawn.Spawned
                || target.Map != pawn.Map
                || !TailOperational())
            {
                return false;
            }
            Pawn targetPawn = target as Pawn;
            if (targetPawn != null && targetPawn.Dead)
            {
                return false;
            }

            int tick = CurrentTick;
            if (tick - lastAssistTick < Props.assistCooldownTicks)
            {
                return false;
            }
            lastAssistTick = tick;

            float damage = Rand.Range(Props.assistDamage * 0.8f, Props.assistDamage * 1.2f);
            Vector3 direction = (target.Position - pawn.Position).ToVector3();
            bool instigatorGuilty = !pawn.Drafted;
            DamageInfo dinfo = new DamageInfo(
                DamageDefOf.Cut,
                damage,
                Props.assistArmorPenetration,
                -1f,
                pawn,
                null,
                pawn.def,
                DamageInfo.SourceCategory.ThingOrUnknown,
                target,
                instigatorGuilty);
            dinfo.SetBodyRegion(BodyPartHeight.Undefined, BodyPartDepth.Outside);
            dinfo.SetAngle(direction);
            if (Props.tailBodyPartGroup != null)
            {
                dinfo.SetWeaponBodyPartGroup(Props.tailBodyPartGroup);
            }
            result = target.TakeDamage(dinfo);
            return result != null;
        }

        private PawnRenderNode CreateNode(
            Pawn pawn,
            PawnRenderTree tree,
            SegmentedMechTailMeshPart part)
        {
            PawnRenderNodeProperties nodeProps = new PawnRenderNodeProperties
            {
                debugLabel = part == SegmentedMechTailMeshPart.Segments
                    ? "Mugirl segmented mech tail"
                    : "Mugirl segmented mech tail tip",
                nodeClass = typeof(PawnRenderNode_SegmentedMechTail),
                workerClass = typeof(PawnRenderNodeWorker_SegmentedMechTail),
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.NonHumanlikeOnly,
                parentTagDef = PawnRenderNodeTagDefOf.Body,
                useGraphic = true,
                baseLayer = part == SegmentedMechTailMeshPart.Segments
                    ? Props.segmentLayer
                    : Props.tipLayer,
                drawSize = Vector2.one
            };
            return new PawnRenderNode_SegmentedMechTail(pawn, nodeProps, tree, this, part);
        }

        private void EnsureRuntimeResources()
        {
            if (resourcesReady)
            {
                return;
            }

            ResolveRequiredTailPart();

            int segmentCount = Mathf.Clamp(Props.segmentCount, 2, 16);
            joints = new Vector2[segmentCount + 1];
            segmentVertices = new Vector3[segmentCount * 4];
            tipVertices = new Vector3[4];
            segmentUvs = CreateUvs(segmentCount);
            tipUvs = CreateUvs(1);
            segmentTriangles = CreateTriangles(segmentCount);
            tipTriangles = CreateTriangles(1);
            meshBounds = CalculateMeshBounds(segmentCount);

            segmentMesh = CreateMesh(
                "Mugirl segmented mech tail segments",
                segmentVertices,
                segmentUvs,
                segmentTriangles,
                meshBounds);
            tipMesh = CreateMesh(
                "Mugirl segmented mech tail tip",
                tipVertices,
                tipUvs,
                tipTriangles,
                meshBounds);

            segmentGraphic = GraphicDatabase.Get<Graphic_Single>(
                Props.segmentTexPath,
                ShaderDatabase.Cutout,
                Vector2.one,
                Color.white);
            tipGraphic = GraphicDatabase.Get<Graphic_Single>(
                Props.tipTexPath,
                ShaderDatabase.Cutout,
                Vector2.one,
                Color.white);
            if (!Props.activeTipTexPath.NullOrEmpty())
            {
                activeTipGraphic = GraphicDatabase.Get<Graphic_Single>(
                    Props.activeTipTexPath,
                    ShaderDatabase.Cutout,
                    Vector2.one,
                    Color.white);
            }

            resourcesReady = true;
        }

        private void BuildRestPose(Rot4 facing, int tick, bool staticPose)
        {
            Vector2 anchor;
            float startAngle;
            float bend;
            GetFacingPose(facing, out anchor, out startAngle, out bend);

            float wag = 0f;
            Pawn pawn = Pawn;
            if (!staticPose && pawn != null)
            {
                float phase = (tick + pawn.thingIDNumber * 17) * Props.idleWagSpeed;
                wag = Mathf.Sin(phase) * Props.idleWagDegrees;
                if (facing == Rot4.West)
                {
                    wag = -wag;
                }
            }

            joints[0] = anchor;
            int segmentCount = joints.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                float along = (i + 1f) / segmentCount;
                float angle = startAngle + bend * i + wag * along * along;
                float radians = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                joints[i + 1] = joints[i] + direction * Props.segmentLength;
            }
        }

        private void PullPoseTowardAttackTarget(float attackWeight)
        {
            Vector2 direction = attackTargetOffset;
            float rawDistance = direction.magnitude;
            if (rawDistance < 0.0001f)
            {
                return;
            }
            direction /= rawDistance;

            float totalLength = Props.segmentLength * (joints.Length - 1);
            float hingeDistance = Mathf.Clamp(
                rawDistance - Props.attackTipBackoff,
                Props.segmentLength * 1.25f,
                totalLength * 0.95f);
            Vector2 desiredHinge = direction * hingeDistance;
            Vector2 target = Vector2.Lerp(joints[joints.Length - 1], desiredHinge, attackWeight);
            SolveFixedLengthChain(target, 3);
        }

        private void SolveFixedLengthChain(Vector2 target, int iterations)
        {
            Vector2 root = joints[0];
            float totalLength = Props.segmentLength * (joints.Length - 1);
            Vector2 rootToTarget = target - root;
            float targetDistance = rootToTarget.magnitude;
            if (targetDistance >= totalLength - 0.0001f)
            {
                Vector2 direction = targetDistance > 0.0001f
                    ? rootToTarget / targetDistance
                    : Vector2.right;
                for (int i = 1; i < joints.Length; i++)
                {
                    joints[i] = joints[i - 1] + direction * Props.segmentLength;
                }
                return;
            }

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                joints[joints.Length - 1] = target;
                for (int i = joints.Length - 2; i >= 0; i--)
                {
                    Vector2 delta = joints[i] - joints[i + 1];
                    float distance = delta.magnitude;
                    Vector2 direction = distance > 0.0001f ? delta / distance : Vector2.left;
                    joints[i] = joints[i + 1] + direction * Props.segmentLength;
                }

                joints[0] = root;
                for (int i = 0; i < joints.Length - 1; i++)
                {
                    Vector2 delta = joints[i + 1] - joints[i];
                    float distance = delta.magnitude;
                    Vector2 direction = distance > 0.0001f ? delta / distance : Vector2.right;
                    joints[i + 1] = joints[i] + direction * Props.segmentLength;
                }
            }
        }

        private void UpdateSegmentMesh()
        {
            float canvasLength = Props.segmentLength
                * Props.segmentOverlap
                * Props.texturePixelSize
                / Props.segmentVisibleWidthPixels;
            Vector2 canvasSize = new Vector2(canvasLength, canvasLength);
            for (int i = 0; i < joints.Length - 1; i++)
            {
                Vector2 from = joints[i];
                Vector2 to = joints[i + 1];
                Vector2 center = (from + to) * 0.5f;
                Vector2 delta = to - from;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                WriteQuad(
                    segmentVertices,
                    i * 4,
                    center,
                    angle,
                    canvasSize,
                    Props.segmentVisibleCenterPixels,
                    i * 0.0001f);
            }
            segmentMesh.vertices = segmentVertices;
            segmentMesh.bounds = meshBounds;
        }

        private void UpdateTipMesh()
        {
            // The saw texture points along local +X from its hinge. Deriving its angle from
            // the terminal joint makes it exactly collinear with the final tail segment in
            // idle, attack and mirrored west-facing poses.
            Vector2 terminalDirection = joints[joints.Length - 1] - joints[joints.Length - 2];
            float angle = terminalDirection.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(terminalDirection.y, terminalDirection.x) * Mathf.Rad2Deg
                : 0f;

            WriteQuad(
                tipVertices,
                0,
                joints[joints.Length - 1],
                angle,
                Props.tipCanvasDrawSize,
                Props.tipHingePixels,
                0f);
            tipMesh.vertices = tipVertices;
            tipMesh.bounds = meshBounds;
        }

        private void WriteQuad(
            Vector3[] vertices,
            int offset,
            Vector2 pivot,
            float angle,
            Vector2 canvasSize,
            Vector2 anchorPixels,
            float lift)
        {
            float textureSize = Mathf.Max(1f, Props.texturePixelSize);
            float left = -anchorPixels.x / textureSize * canvasSize.x;
            float right = (textureSize - anchorPixels.x) / textureSize * canvasSize.x;
            float top = anchorPixels.y / textureSize * canvasSize.y;
            float bottom = -(textureSize - anchorPixels.y) / textureSize * canvasSize.y;
            float radians = angle * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);

            vertices[offset] = TransformPoint(left, top, pivot, cosine, sine, lift);
            vertices[offset + 1] = TransformPoint(right, top, pivot, cosine, sine, lift);
            vertices[offset + 2] = TransformPoint(right, bottom, pivot, cosine, sine, lift);
            vertices[offset + 3] = TransformPoint(left, bottom, pivot, cosine, sine, lift);
        }

        private static Vector3 TransformPoint(
            float localX,
            float localZ,
            Vector2 pivot,
            float cosine,
            float sine,
            float lift)
        {
            float x = pivot.x + localX * cosine - localZ * sine;
            float z = pivot.y + localX * sine + localZ * cosine;
            return new Vector3(x, lift, z);
        }

        private void GetFacingPose(
            Rot4 facing,
            out Vector2 anchor,
            out float startAngle,
            out float bend)
        {
            if (facing == Rot4.North)
            {
                anchor = Props.northAnchor;
                startAngle = Props.northStartAngle;
                bend = Props.northBendPerSegment;
                return;
            }
            if (facing == Rot4.South)
            {
                anchor = Props.southAnchor;
                startAngle = Props.southStartAngle;
                bend = Props.southBendPerSegment;
                return;
            }
            if (facing == Rot4.West)
            {
                anchor = new Vector2(-Props.eastAnchor.x, Props.eastAnchor.y);
                startAngle = 180f - Props.eastStartAngle;
                bend = -Props.eastBendPerSegment;
                return;
            }

            anchor = Props.eastAnchor;
            startAngle = Props.eastStartAngle;
            bend = Props.eastBendPerSegment;
        }

        private float AttackWeight(int tick, bool staticPose)
        {
            if (staticPose || attackStartTick == int.MinValue)
            {
                return 0f;
            }
            int elapsed = tick - attackStartTick;
            if (elapsed < 0 || elapsed >= Props.attackAnimationTicks)
            {
                return 0f;
            }
            float progress = elapsed / (float)Mathf.Max(1, Props.attackAnimationTicks);
            // The melee Verb resolves damage immediately. Treat that tick as chainsaw impact,
            // then ease the tail back to its idle curve so visuals and damage stay synchronized.
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        private bool TailOperational()
        {
            Pawn pawn = Pawn;
            if (pawn == null || Props.requiredTailPart == null)
            {
                return pawn != null;
            }
            int tick = CurrentTick;
            if (cachedOperationalTick == tick)
            {
                return cachedOperational;
            }
            ResolveRequiredTailPart();
            cachedOperationalTick = tick;
            cachedOperational = requiredTailPartRecord != null
                && pawn.health?.hediffSet != null
                && !pawn.health.hediffSet.PartIsMissing(requiredTailPartRecord);
            return cachedOperational;
        }

        private void ResolveRequiredTailPart()
        {
            if (requiredTailPartResolved)
            {
                return;
            }
            Pawn pawn = Pawn;
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            if (parts == null)
            {
                return;
            }
            requiredTailPartResolved = true;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].def == Props.requiredTailPart)
                {
                    requiredTailPartRecord = parts[i];
                    return;
                }
            }
        }

        private void CollapseMeshes()
        {
            for (int i = 0; i < segmentVertices.Length; i++)
            {
                segmentVertices[i] = Vector3.zero;
            }
            for (int i = 0; i < tipVertices.Length; i++)
            {
                tipVertices[i] = Vector3.zero;
            }
            segmentMesh.vertices = segmentVertices;
            tipMesh.vertices = tipVertices;
            segmentMesh.bounds = meshBounds;
            tipMesh.bounds = meshBounds;
        }

        private static Mesh CreateMesh(
            string name,
            Vector3[] vertices,
            Vector2[] uvs,
            int[] triangles,
            Bounds bounds)
        {
            Mesh mesh = new Mesh
            {
                name = name,
                vertices = vertices,
                uv = uvs,
                triangles = triangles,
                bounds = bounds
            };
            mesh.MarkDynamic();
            return mesh;
        }

        private Bounds CalculateMeshBounds(int segmentCount)
        {
            float anchorRadius = Mathf.Max(
                Props.northAnchor.magnitude,
                Mathf.Max(Props.eastAnchor.magnitude, Props.southAnchor.magnitude));
            float tailReach = segmentCount * Props.segmentLength;
            float segmentCanvasLength = Props.segmentLength
                * Props.segmentOverlap
                * Props.texturePixelSize
                / Mathf.Max(1f, Props.segmentVisibleWidthPixels);
            float spriteReach = Mathf.Max(segmentCanvasLength, Props.tipCanvasDrawSize.magnitude);
            float radius = Mathf.Max(1f, anchorRadius + tailReach + spriteReach);
            return new Bounds(Vector3.zero, new Vector3(radius * 2f, 1f, radius * 2f));
        }

        private static Vector2[] CreateUvs(int quadCount)
        {
            Vector2[] uvs = new Vector2[quadCount * 4];
            for (int i = 0; i < quadCount; i++)
            {
                int offset = i * 4;
                uvs[offset] = new Vector2(0f, 1f);
                uvs[offset + 1] = new Vector2(1f, 1f);
                uvs[offset + 2] = new Vector2(1f, 0f);
                uvs[offset + 3] = new Vector2(0f, 0f);
            }
            return uvs;
        }

        private static int[] CreateTriangles(int quadCount)
        {
            int[] triangles = new int[quadCount * 6];
            for (int i = 0; i < quadCount; i++)
            {
                int vertexOffset = i * 4;
                int triangleOffset = i * 6;
                triangles[triangleOffset] = vertexOffset;
                triangles[triangleOffset + 1] = vertexOffset + 1;
                triangles[triangleOffset + 2] = vertexOffset + 2;
                triangles[triangleOffset + 3] = vertexOffset;
                triangles[triangleOffset + 4] = vertexOffset + 2;
                triangles[triangleOffset + 5] = vertexOffset + 3;
            }
            return triangles;
        }

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            Object.Destroy(mesh);
            mesh = null;
        }

        private void ReleaseRuntimeResources()
        {
            DestroyMesh(ref segmentMesh);
            DestroyMesh(ref tipMesh);
            segmentVertices = null;
            tipVertices = null;
            joints = null;
            segmentUvs = null;
            tipUvs = null;
            segmentTriangles = null;
            tipTriangles = null;
            resourcesReady = false;
            cachedTick = int.MinValue;
            cachedFacing = -1;
            cachedPoseRevision = int.MinValue;
            cachedOperationalTick = int.MinValue;
            cachedOperational = false;
        }

        private static Vector2 FacingVector(Rot4 facing)
        {
            if (facing == Rot4.North)
            {
                return Vector2.up;
            }
            if (facing == Rot4.South)
            {
                return Vector2.down;
            }
            if (facing == Rot4.West)
            {
                return Vector2.left;
            }
            return Vector2.right;
        }

        private static int CurrentTick => MugirlTickUtility.CurrentGameTickOrFallback(0);
    }
}
