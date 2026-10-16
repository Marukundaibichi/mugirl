namespace Mugirl.Features.Milk;

// 喂奶双人动画：开始时测量双方的渲染尺寸与头部锚点并求解姿态（MilkingPoseMath.SolvePairPose），之后每 tick 只续期。
public static partial class MilkingAnimation
{
    public static void StartFeeding(Pawn? milkSource, Pawn? recipient)
    {
        StartPairedInteraction(milkSource, recipient);
    }

    public static void TickFeeding(Pawn? milkSource, Pawn? recipient)
    {
        if (!Valid(milkSource) || !Valid(recipient) || milkSource == recipient)
        {
            return;
        }

        if (!TickUtility.TryGetCurrentGameTick(out int now))
        {
            ResetTransientState();
            return;
        }

        SweepStaleStates(now);
        ApplyMilkInteractionFacings(milkSource, recipient);
        if (!TryGetLiveState(milkSource, out MilkingVisualState? sourceState)
            || sourceState.partner != recipient
            || sourceState.role != MilkingVisualRole.FeedingSource
            || !TryGetLiveState(recipient, out MilkingVisualState? recipientState)
            || recipientState.partner != milkSource
            || recipientState.role != MilkingVisualRole.FeedingRecipient)
        {
            StartPairedInteraction(milkSource, recipient);
            return;
        }

        sourceState.lastTick = now;
        recipientState.lastTick = now;
        EnsureNativeAnimation(sourceState);
        EnsureNativeAnimation(recipientState);
    }

    public static void EndFeeding(Pawn? milkSource, Pawn? recipient)
    {
        RemoveIfMatches(milkSource, recipient);
        RemoveIfMatches(recipient, milkSource);
    }

    // 与 Root 倾角配对：右侧用 East、左侧用 West，旋转后侧脸朝画面上方。
    internal static Rot4 FeedingRecipientFacing(Pawn source, Pawn recipient)
    {
        float sideSign = MilkingPoseMath.InteractionSideSign(source.DrawPos.ToNumerics(), recipient.DrawPos.ToNumerics(), recipient.thingIDNumber);
        return sideSign > 0f ? Rot4.East : Rot4.West;
    }

    private static void StartPairedInteraction(Pawn? source, Pawn? recipient)
    {
        if (!Valid(source) || !Valid(recipient) || source == recipient || !TickUtility.TryGetCurrentGameTick(out int now))
        {
            return;
        }

        ApplyMilkInteractionFacings(source, recipient);
        Vector3 sourcePos = source.DrawPos.ToNumerics();
        Vector3 recipientPos = recipient.DrawPos.ToNumerics();
        InteractionPairPose pairPose = MilkingPoseMath.SolvePairPose(
            MeasurePawnVisuals(source),
            MeasurePawnVisuals(recipient),
            sourcePos,
            recipientPos,
            MilkingPoseMath.InteractionSideSign(sourcePos, recipientPos, recipient.thingIDNumber),
            MilkFeedingLayout.Current);
        EnsureState(source, recipient, MilkingVisualRole.FeedingSource, now, pairPose.Source);
        EnsureState(recipient, source, MilkingVisualRole.FeedingRecipient, now, pairPose.Recipient);
    }

    // 喂奶构图固定：雪牛娘正面，被喂者使用旋转后脸朝上的侧面贴图。
    private static void ApplyMilkInteractionFacings(Pawn source, Pawn recipient)
    {
        source.Rotation = Rot4.South;
        recipient.Rotation = FeedingRecipientFacing(source, recipient);
    }

    // 测量 Pawn 当前朝向下身体与头部的绘制尺寸、头部锚点。只在主线程的喂奶开始时调用一次。
    private static PawnVisualMetrics MeasurePawnVisuals(Pawn pawn)
    {
        PawnRenderer? renderer = pawn.Drawer?.renderer;
        PawnRenderTree? tree = renderer?.renderTree;
        if (renderer == null || tree == null)
        {
            return MilkingPoseMath.FallbackMetrics();
        }

        renderer.EnsureGraphicsInitialized();
        Rot4 facing = pawn.Rotation;
        Vector2 bodySize = new(MilkingPoseMath.FallbackBodySize, MilkingPoseMath.FallbackBodySize);
        Vector2 measuredBody = MeasureNodeSize(pawn, renderer, tree, PawnRenderNodeTagDefOf.Body, facing);
        if (measuredBody.X > 0.01f && measuredBody.Y > 0.01f)
        {
            bodySize = measuredBody;
        }

        if (tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Head, out PawnRenderNode? headNode)
            && headNode != null
            && renderer.HeadGraphic != null)
        {
            return new PawnVisualMetrics(
                bodySize,
                MeasureNodeSize(pawn, renderer, tree, PawnRenderNodeTagDefOf.Head, facing),
                MeasureHeadAnchor(pawn, renderer, headNode, facing),
                HasExplicitHead: true);
        }

        return MilkingPoseMath.BodyOnlyMetrics(bodySize);
    }

    // 节点网格尺寸乘以节点 Worker 的实际缩放，覆盖原版年龄、体型及自定义渲染树的贴图尺寸。
    // 网格尺寸取自 MeshPool 的网格元数据（MeshMakerPlanes 生成的平面网格都会登记，HAR 与原版人形网格均来自 MeshPool）；
    // 1.6 读取的 Mesh.bounds 属于 UnityStubs 兼容层。未登记的网格按没有网格处理，改用 Graphic.drawSize。
    private static Vector2 MeasureNodeSize(Pawn pawn, PawnRenderer renderer, PawnRenderTree tree, PawnRenderNodeTagDef tag, Rot4 facing)
    {
        if (!tree.TryGetNodeByTag(tag, out PawnRenderNode? node) || node == null)
        {
            return Vector2.Zero;
        }

        Mesh? mesh = node.MeshSetFor(pawn)?.MeshAt(facing);
        if (mesh is null || !TryGetPlaneMeshSize(mesh, out Vector2 meshSize))
        {
            Graphic? graphic = node.PrimaryGraphic;
            return graphic != null ? graphic.drawSize.ToNumerics() : Vector2.Zero;
        }

        Vector3 scale;
        if (!IsMugirlMilkingAnimation(renderer.CurAnimation))
        {
            PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
            parms.facing = facing;
            scale = node.Worker.ScaleFor(node, parms).ToNumerics();
        }
        else
        {
            // 自己的动画正在播放时 Worker 缩放含动画分量，改用静态配置。
            float drawScale = node.Props.drawData?.ScaleFor(pawn) ?? 1f;
            scale = new Vector3(node.Props.drawSize.x * drawScale, 1f, node.Props.drawSize.y * drawScale);
        }

        return new Vector2(UnityMathf.Abs(meshSize.X * scale.X), UnityMathf.Abs(meshSize.Y * scale.Z));
    }

    // MeshPool 元数据中平面网格的宽（X）与深（Z），与该网格包围盒的 X/Z 尺寸相同。
    private static bool TryGetPlaneMeshSize(Mesh mesh, out Vector2 size)
    {
        try
        {
            size = MeshPool.GetMetaData(mesh).size.ToNumerics();
            return true;
        }
        catch (KeyNotFoundException)
        {
            // MeshPool 没有提供 TryGet；未登记的网格（模组自建）很少见，且只在喂奶开始时测量一次。
            size = Vector2.Zero;
            return false;
        }
    }

    private static Vector3 MeasureHeadAnchor(Pawn pawn, PawnRenderer renderer, PawnRenderNode headNode, Rot4 facing)
    {
        if (!IsMugirlMilkingAnimation(renderer.CurAnimation))
        {
            PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
            parms.facing = facing;
            return headNode.Worker.OffsetFor(headNode, parms, out _).ToNumerics();
        }

        // 自己的动画正在播放时 Worker 偏移含动画分量，改由静态 DrawData 与原版头部基准偏移重建。
        Vector3 anchor = Vector3.Zero;
        DrawData? drawData = headNode.Props.drawData;
        if (drawData != null)
        {
            anchor = drawData.OffsetForRot(facing).ToNumerics();
            if (drawData.scaleOffsetByBodySize && pawn.story?.bodyType != null)
            {
                UVector2 bodyGraphicScale = pawn.story.bodyType.bodyGraphicScale;
                anchor *= (bodyGraphicScale.x + bodyGraphicScale.y) * 0.5f;
            }
        }

        if (headNode.Worker is PawnRenderNodeWorker_Head
            && pawn.story?.bodyType != null
            && pawn.ageTracker?.CurLifeStage != null)
        {
            anchor += renderer.BaseHeadOffsetAt(facing).ToNumerics();
        }

        return anchor;
    }
}
