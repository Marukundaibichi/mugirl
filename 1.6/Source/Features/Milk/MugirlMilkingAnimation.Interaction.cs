using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public static partial class MugirlMilkingAnimation
    {
        private const float FallbackBodySize = 1.5f;
        private const float FallbackHeadRegionFraction = 0.3f;

        // 交互动画的接触点不是 Pawn 原点。喂奶时，被喂者的身体位于其地图格
        // 一侧，但头部需要跨过雪牛娘中心线落到另一侧的胸前；喝奶则保持在饮奶者
        // 所在的一侧。两个接触点都低于身体中心，避免头部遮住雪牛娘的脸。
        private const float HeldChestLateralScale = 0.16f;
        private const float HeldChestHeightScale = -0.1f;
        private const float DrinkingChestLateralScale = 0.12f;
        private const float DrinkingChestHeightScale = -0.06f;
        // 生物科技原版给小孩喂奶时，孩子的身体会明显横向倾倒；雪牛娘喂奶沿用
        // 同样的横向构图，按体型在约 62° 到 82° 之间变化。
        private const float HeldMinTilt = 62f;
        private const float HeldMaxTilt = 82f;
        private const float DrinkingMinTilt = 36f;
        private const float DrinkingMaxTilt = 52f;

        private struct InteractionPose
        {
            public Vector3 rootWorldOffset;
            public float rootWorldAngle;
            public bool hasExplicitHead;
            public float bodyHeight;
        }

        private struct InteractionPairPose
        {
            public InteractionPose source;
            public InteractionPose recipient;
        }

        private struct PawnVisualMetrics
        {
            public Vector2 bodySize;
            public Vector2 headSize;
            public Vector3 headAnchor;
            public bool hasExplicitHead;
        }

        public static void StartFeeding(Pawn milkSource, Pawn recipient)
        {
            StartPairedInteraction(milkSource, recipient, MugirlMilkingVisualRole.FeedingSource, MugirlMilkingVisualRole.FeedingRecipient, heldPose: true);
        }

        public static void TickFeeding(Pawn milkSource, Pawn recipient)
        {
            TickPairedInteraction(milkSource, recipient, MugirlMilkingVisualRole.FeedingSource, MugirlMilkingVisualRole.FeedingRecipient, heldPose: true);
        }

        public static void EndFeeding(Pawn milkSource, Pawn recipient)
        {
            EndPairedInteraction(milkSource, recipient);
        }

        public static void StartDrinking(Pawn drinker, Pawn milkSource)
        {
            StartPairedInteraction(milkSource, drinker, MugirlMilkingVisualRole.DrinkingSource, MugirlMilkingVisualRole.Drinker, heldPose: false);
        }

        public static void TickDrinking(Pawn drinker, Pawn milkSource)
        {
            TickPairedInteraction(milkSource, drinker, MugirlMilkingVisualRole.DrinkingSource, MugirlMilkingVisualRole.Drinker, heldPose: false);
        }

        public static void EndDrinking(Pawn drinker, Pawn milkSource)
        {
            EndPairedInteraction(milkSource, drinker);
        }

        private static void StartPairedInteraction(Pawn source, Pawn recipient, MugirlMilkingVisualRole sourceRole, MugirlMilkingVisualRole recipientRole, bool heldPose)
        {
            if (!Valid(source) || !Valid(recipient) || source == recipient)
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                return;
            }

            ApplyMilkInteractionFacings(source, recipient, heldPose);
            InteractionPairPose pairPose = BuildInteractionPairPose(source, recipient, heldPose);
            EnsureState(source, recipient, sourceRole, now, pairPose.source);
            EnsureState(recipient, source, recipientRole, now, pairPose.recipient);
        }

        private static void TickPairedInteraction(Pawn source, Pawn recipient, MugirlMilkingVisualRole sourceRole, MugirlMilkingVisualRole recipientRole, bool heldPose)
        {
            if (!Valid(source) || !Valid(recipient) || source == recipient)
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int now))
            {
                ResetTransientState();
                return;
            }

            ApplyMilkInteractionFacings(source, recipient, heldPose);
            if (!TryGetState(source, out MilkingVisualState sourceState)
                || sourceState.partner != recipient
                || sourceState.role != sourceRole
                || !TryGetState(recipient, out MilkingVisualState recipientState)
                || recipientState.partner != source
                || recipientState.role != recipientRole)
            {
                StartPairedInteraction(source, recipient, sourceRole, recipientRole, heldPose);
                return;
            }

            sourceState.lastTick = now;
            recipientState.lastTick = now;
            EnsureNativeAnimation(sourceState);
            EnsureNativeAnimation(recipientState);
        }

        private static void EndPairedInteraction(Pawn source, Pawn recipient)
        {
            RemoveIfMatches(source, recipient);
            RemoveIfMatches(recipient, source);
        }

        internal static Rot4 FeedingRecipientFacing(Pawn source, Pawn recipient)
        {
            // 与 Root 倾角配对：右侧用 East、左侧用 West，旋转后侧脸朝画面上方。
            return InteractionSideSign(source, recipient) > 0f ? Rot4.East : Rot4.West;
        }

        private static void ApplyMilkInteractionFacings(Pawn source, Pawn recipient, bool heldPose)
        {
            if (!Valid(source) || !Valid(recipient))
            {
                return;
            }

            if (heldPose)
            {
                // 喂奶构图固定：雪牛娘正面，被喂者使用旋转后脸朝上的侧面贴图。
                source.Rotation = Rot4.South;
                recipient.Rotation = FeedingRecipientFacing(source, recipient);
                return;
            }

            // 喝奶仍保持双方相向。
            source.rotationTracker?.FaceTarget(recipient);
            recipient.rotationTracker?.FaceTarget(source);
        }

        private static InteractionPairPose BuildInteractionPairPose(Pawn source, Pawn recipient, bool heldPose)
        {
            PawnVisualMetrics sourceMetrics = MeasurePawnVisuals(source);
            PawnVisualMetrics recipientMetrics = MeasurePawnVisuals(recipient);
            float sourceHeight = Mathf.Max(0.5f, sourceMetrics.bodySize.y);
            float sourceWidth = Mathf.Max(0.5f, sourceMetrics.bodySize.x);
            float recipientHeight = Mathf.Max(0.35f, recipientMetrics.bodySize.y);
            float recipientWidth = Mathf.Max(0.35f, recipientMetrics.bodySize.x);
            float recipientVisualHeight = Mathf.Max(recipientHeight, Mathf.Abs(recipientMetrics.headAnchor.z) + recipientMetrics.headSize.y * 0.5f);
            float recipientVisualWidth = Mathf.Max(recipientWidth, Mathf.Abs(recipientMetrics.headAnchor.x) * 2f + recipientMetrics.headSize.x);
            float sideSign = InteractionSideSign(source, recipient);

            Vector3 sourceToRecipient = recipient.DrawPos - source.DrawPos;
            sourceToRecipient.y = 0f;
            if (sourceToRecipient.sqrMagnitude > 0.0001f)
            {
                sourceToRecipient.Normalize();
            }

            float sourceAngle = heldPose ? 0f : sideSign * 2.5f;
            Vector3 sourceOffset = sourceToRecipient * (heldPose ? 0.11f : 0.075f);
            float contactSideSign = heldPose ? 0f - sideSign : sideSign;
            float contactHeight = sourceHeight * (heldPose ? HeldChestHeightScale : DrinkingChestHeightScale);
            Vector3 chestLocal = new Vector3(
                contactSideSign * Mathf.Clamp(sourceWidth * (heldPose ? HeldChestLateralScale : DrinkingChestLateralScale), 0.09f, 0.26f),
                0f,
                Mathf.Clamp(contactHeight, -0.18f, -0.04f));
            Vector3 chestWorld = source.DrawPos + sourceOffset + RotateOnGround(chestLocal, sourceAngle);

            float heightRatio = recipientVisualHeight / sourceHeight;
            float widthRatio = recipientVisualWidth / sourceWidth;
            float sizeFactor = Mathf.Clamp01((heightRatio - 0.45f) / 1.1f);
            float widthFactor = Mathf.Clamp01((widthRatio - 0.45f) / 1.1f);
            float tiltMagnitude = heldPose
                ? Mathf.Lerp(HeldMinTilt, HeldMaxTilt, sizeFactor * 0.78f + widthFactor * 0.22f)
                : Mathf.Lerp(DrinkingMinTilt, DrinkingMaxTilt, sizeFactor * 0.82f + widthFactor * 0.18f);
            float recipientAngle = 0f - sideSign * tiltMagnitude;
            // 反解 Root 位移：旋转后的头部锚点必须落在雪牛娘的胸部锚点上。
            Vector3 rotatedHeadAnchor = RotateOnGround(recipientMetrics.headAnchor, recipientAngle);
            Vector3 recipientOffset = chestWorld - recipient.DrawPos - rotatedHeadAnchor;
            recipientOffset.y += heldPose ? 0.012f : 0.008f;

            return new InteractionPairPose
            {
                source = new InteractionPose
                {
                    rootWorldOffset = sourceOffset,
                    rootWorldAngle = sourceAngle,
                    hasExplicitHead = sourceMetrics.hasExplicitHead,
                    bodyHeight = sourceHeight
                },
                recipient = new InteractionPose
                {
                    rootWorldOffset = recipientOffset,
                    rootWorldAngle = recipientAngle,
                    hasExplicitHead = recipientMetrics.hasExplicitHead,
                    bodyHeight = recipientHeight
                }
            };
        }

        private static PawnVisualMetrics MeasurePawnVisuals(Pawn pawn)
        {
            PawnVisualMetrics metrics = new PawnVisualMetrics
            {
                bodySize = new Vector2(FallbackBodySize, FallbackBodySize),
                headSize = new Vector2(FallbackBodySize * FallbackHeadRegionFraction, FallbackBodySize * FallbackHeadRegionFraction),
                headAnchor = new Vector3(0f, 0f, FallbackBodySize * (0.5f - FallbackHeadRegionFraction * 0.5f)),
                hasExplicitHead = false
            };

            PawnRenderer renderer = pawn?.Drawer?.renderer;
            PawnRenderTree tree = renderer?.renderTree;
            if (renderer == null || tree == null)
            {
                return metrics;
            }

            renderer.EnsureGraphicsInitialized();
            Rot4 facing = pawn.Rotation;
            Vector2 bodySize = MeasureNodeSize(pawn, tree, PawnRenderNodeTagDefOf.Body, facing);
            if (bodySize.x > 0.01f && bodySize.y > 0.01f)
            {
                metrics.bodySize = bodySize;
            }

            if (tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Head, out PawnRenderNode headNode)
                && headNode != null
                && renderer.HeadGraphic != null)
            {
                metrics.hasExplicitHead = true;
                metrics.headSize = MeasureNodeSize(pawn, tree, PawnRenderNodeTagDefOf.Head, facing);
                metrics.headAnchor = MeasureHeadAnchor(pawn, renderer, headNode, facing);
            }
            else
            {
                // 无独立头节点时，将身体贴图顶部 30% 区域的中心视为头部中心。
                metrics.headSize = new Vector2(metrics.bodySize.x, metrics.bodySize.y * FallbackHeadRegionFraction);
                metrics.headAnchor = new Vector3(0f, 0f, metrics.bodySize.y * (0.5f - FallbackHeadRegionFraction * 0.5f));
            }

            return metrics;
        }

        private static Vector2 MeasureNodeSize(Pawn pawn, PawnRenderTree tree, PawnRenderNodeTagDef tag, Rot4 facing)
        {
            if (tree == null || !tree.TryGetNodeByTag(tag, out PawnRenderNode node) || node == null)
            {
                return Vector2.zero;
            }

            GraphicMeshSet meshSet = node.MeshSetFor(pawn);
            Mesh mesh = meshSet?.MeshAt(facing);
            if (mesh == null)
            {
                Graphic graphic = node.PrimaryGraphic;
                return graphic != null ? graphic.drawSize : Vector2.zero;
            }

            Vector3 scale;
            PawnRenderer renderer = pawn?.Drawer?.renderer;
            // 网格尺寸与节点 Worker 的实际缩放共同覆盖原版年龄、体型及自定义渲染树的贴图尺寸。
            if (renderer != null && !IsMugirlMilkingAnimation(renderer.CurAnimation))
            {
                PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
                parms.facing = facing;
                scale = node.Worker.ScaleFor(node, parms);
            }
            else
            {
                float drawScale = node.Props.drawData?.ScaleFor(pawn) ?? 1f;
                scale = new Vector3(node.Props.drawSize.x * drawScale, 1f, node.Props.drawSize.y * drawScale);
            }

            Vector3 size = mesh.bounds.size;
            return new Vector2(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.z * scale.z));
        }

        private static Vector3 MeasureHeadAnchor(Pawn pawn, PawnRenderer renderer, PawnRenderNode headNode, Rot4 facing)
        {
            if (!IsMugirlMilkingAnimation(renderer.CurAnimation))
            {
                PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
                parms.facing = facing;
                return headNode.Worker.OffsetFor(headNode, parms, out _);
            }

            Vector3 anchor = Vector3.zero;
            DrawData drawData = headNode.Props.drawData;
            if (drawData != null)
            {
                anchor = drawData.OffsetForRot(facing);
                if (drawData.scaleOffsetByBodySize && pawn.story?.bodyType != null)
                {
                    Vector2 bodyGraphicScale = pawn.story.bodyType.bodyGraphicScale;
                    anchor *= (bodyGraphicScale.x + bodyGraphicScale.y) * 0.5f;
                }
            }

            if (headNode.Worker is PawnRenderNodeWorker_Head
                && pawn.story?.bodyType != null
                && pawn.ageTracker?.CurLifeStage != null)
            {
                anchor += renderer.BaseHeadOffsetAt(facing);
            }

            return anchor;
        }

        private static void ApplyMilkInteractionBodyMotion(MilkingVisualState state, PawnDrawParms parms, int age, ref Vector3 offset, ref float angle, ref Vector3 scale)
        {
            float baseAngle = parms.pawn?.Drawer?.renderer?.BodyAngle(parms.flags) ?? 0f;
            Vector3 worldOffset = state.rootWorldOffset;
            float wave = Mathf.Sin(age * 0.075f + state.pawn.thingIDNumber * 0.11f);
            float motionScale = Mathf.Clamp(state.bodyHeight / FallbackBodySize, 0.65f, 1.35f);
            float angleWave = 0f;

            switch (state.role)
            {
                case MugirlMilkingVisualRole.FeedingSource:
                    worldOffset.z += wave * 0.008f * motionScale;
                    scale = new Vector3(scale.x * (1f - wave * 0.004f), scale.y, scale.z * (1f + wave * 0.006f));
                    break;
                case MugirlMilkingVisualRole.DrinkingSource:
                    worldOffset.z += wave * 0.005f * motionScale;
                    angleWave = wave * 0.7f;
                    break;
                case MugirlMilkingVisualRole.FeedingRecipient:
                    worldOffset += DirectionToPartner(state) * (wave * 0.006f * motionScale);
                    angleWave = wave * 1.5f;
                    break;
                case MugirlMilkingVisualRole.Drinker:
                    float sip = Mathf.Sin(age * 0.11f);
                    worldOffset += DirectionToPartner(state) * (sip * 0.012f * motionScale);
                    angleWave = sip * 2.1f;
                    break;
            }

            offset += RotateOnGround(worldOffset, 0f - baseAngle);
            angle += Mathf.DeltaAngle(baseAngle, state.rootWorldAngle) + angleWave;
        }

        private static void ApplyMilkSourceHeadMotion(MilkingVisualState state, ref Vector3 offset, ref float angle)
        {
            int age = Mathf.Max(0, MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.startTick);
            float wave = Mathf.Sin(age * 0.075f + 0.8f);
            offset += new Vector3(0f, 0f, -0.026f + wave * 0.008f);
            if (state.role == MugirlMilkingVisualRole.FeedingSource)
            {
                return;
            }

            Vector3 toPartner = DirectionToPartner(state);
            angle += LeanAngleForDirection(toPartner, 4f) + wave * 1.1f;
        }

        private static void ApplyMilkRecipientHeadMotion(MilkingVisualState state, ref Vector3 offset, ref float angle)
        {
            int age = Mathf.Max(0, MugirlTickUtility.CurrentGameTickOrFallback(state.lastTick) - state.startTick);
            float wave = Mathf.Sin(age * (state.role == MugirlMilkingVisualRole.Drinker ? 0.11f : 0.075f));
            offset += new Vector3(0f, 0f, wave * 0.005f);
            angle += wave * (state.role == MugirlMilkingVisualRole.Drinker ? 1.6f : 0.8f);
        }

        private static float InteractionSideSign(Pawn source, Pawn recipient)
        {
            float deltaX = recipient.DrawPos.x - source.DrawPos.x;
            if (Mathf.Abs(deltaX) > 0.05f)
            {
                return deltaX > 0f ? 1f : -1f;
            }

            float deltaZ = recipient.DrawPos.z - source.DrawPos.z;
            if (Mathf.Abs(deltaZ) > 0.05f)
            {
                return deltaZ > 0f ? 1f : -1f;
            }

            return recipient.thingIDNumber % 2 == 0 ? 1f : -1f;
        }

        private static Vector3 RotateOnGround(Vector3 vector, float angle)
        {
            return Quaternion.AngleAxis(angle, Vector3.up) * vector;
        }
    }
}
