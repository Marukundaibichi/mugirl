using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>把明确由主存档读取的旧 Job 绑定到当前 ThinkNode；新档及其他 Scribe 文档不走旧键空间。</summary>
internal static class LegacyThinkTreeKeys
{
    // StaticCacheLifecycle: Def 级键空间，在原版 ThinkTreeKeyAssigner.Reset 同一时点清空。
    private static readonly LegacyThinkTreeLedger Ledger = new(LegacyShortName);
    private static readonly LegacyThinkTreeLedger BeforeCorporateDuty = new(LegacyShortName);
    private static readonly LegacyThinkTreeLedger FirstPublishedLayout = new(node => LegacyThinkLayouts.WasPawnKindCondition(node)
        ? "ThinkNode_ConditionalPawnKind" : LegacyShortName(node));
    private static string? captureError;

    // StaticCacheLifecycle: 读档会话是弱键，结束后不保留 Job 或整份存档 DOM。
    private static readonly ConditionalWeakTable<SaveUpgradeSession, LegacyThinkJobReadSet> ReadJobs = new();

    [ThreadStatic]
    private static ThinkTreeDef? resolvingTree;

    [ThreadStatic]
    private static SaveUpgradeSession? exposingLegacyJob;

    [ThreadStatic]
    private static XmlNode? exposingLegacyJobXml;

    [ThreadStatic]
    private static Job? exposingJob;

    internal static bool AlgorithmVerified => ReviewedSaveEngine.IsReviewed(typeof(ThinkTreeKeyAssigner).Assembly.ManifestModule.ModuleVersionId);

    internal static string? Problem => !AlgorithmVerified ? "ThinkTree key assignment has not been verified for this game assembly." : captureError;

    internal static void Reset()
    {
        Ledger.Reset();
        BeforeCorporateDuty.Reset();
        FirstPublishedLayout.Reset();
        LegacyThinkLayouts.Reset(resolvingTree);
        captureError = null;
    }

    internal static ThinkTreeDef? EnterTree(ThinkTreeDef tree)
    {
        ThinkTreeDef? previous = resolvingTree;
        resolvingTree = tree;
        LegacyThinkLayouts.EnterTree(tree);
        return previous;
    }

    internal static void LeaveTree(ThinkTreeDef? previous) => resolvingTree = previous;

    internal static void Capture(ThinkNode root, int seed, bool single, int? assignedCount = null)
    {
        if (!AlgorithmVerified)
        {
            return;
        }

        // Reset 很短，可能已被早于 Mod 构造编译的调用方内联。原集合为空是新分配边界，
        // 也必须清除前一轮捕获失败；否则一次失败会永久污染其后的完整 Def 重建。
        if (assignedCount is 0)
        {
            Reset();
        }
        if (captureError is not null)
        {
            return;
        }

        try
        {
            if (assignedCount is { } count && count != Ledger.AssignedCount)
            {
                throw new InvalidOperationException("The ThinkTree assignment stream began before capture or was changed by another patch.");
            }

            if (!single && resolvingTree is { } tree && ReferenceEquals(tree.thinkRoot, root)
                && seed == GenText.StableStringHash(tree.defName))
            {
                string[] oldNames = [.. UpgradeTables.Default.DefRows.Where(r => r.Root == "ThinkTreeDef" && r.New == tree.defName).Select(r => r.Old).Distinct(StringComparer.Ordinal)];
                if (oldNames.Length > 1)
                {
                    throw new InvalidOperationException("ThinkTree " + tree.defName + " has multiple historical seed names; no seed was guessed.");
                }

                if (oldNames.Length == 1)
                {
                    seed = GenText.StableStringHash(oldNames[0]);
                }
            }

            // DeepCopy 的 AssignSingleKey 使用固定种子 0；按原调用顺序记录，不另遍历或重排复制树。
            if (single) { LegacyThinkLayouts.AssigningCopy(root); }
            Ledger.Capture(single ? [root] : root.ThisAndChildrenRecursive, seed);
            if (!LegacyThinkLayouts.InsideOmittedDuty)
            {
                IEnumerable<ThinkNode> historical = (single ? new[] { root } : root.ThisAndChildrenRecursive)
                    .Where(LegacyThinkLayouts.ExistedBeforeCorporateDuty);
                BeforeCorporateDuty.Capture(historical, seed);
                if (resolvingTree?.defName != LegacyThinkLayouts.IntroducedTree)
                {
                    FirstPublishedLayout.Capture(historical, seed);
                }
            }
        }
        catch (Exception exception)
        {
            captureError = "Could not preserve ThinkTree save identities: " + exception.Message;
        }
    }

    internal static LegacyThinkJobScope EnterJob(Job job, SaveUpgradeSession? session, LoadSaveMode mode, XmlNode? currentNode)
    {
        var previous = new LegacyThinkJobScope(exposingLegacyJob, exposingLegacyJobXml, exposingJob);
        exposingLegacyJob = null;
        exposingLegacyJobXml = null;
        exposingJob = null;
        if (session is not { Completed: false, Document.HasMugirl: true } || session.Report.Failed
            || session.SourceSchema > SaveSchema.Current)
        {
            return previous;
        }

        if (mode == LoadSaveMode.LoadingVars && ReferenceEquals(currentNode?.OwnerDocument, session.Document?.Xml)
            && IsInsideGame(currentNode, session.Document!.Game))
        {
            LegacyThinkJobReadSet read = ReadJobs.GetValue(session, static _ => new LegacyThinkJobReadSet());
            if (read.Profiles is not null)
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "A Job was read after the saved ThinkTree layout had already been selected.");
            }
            else if (session.IsLegacyRun && read.Jobs.TryGetValue(job, out XmlNode? existing) && !ReferenceEquals(existing, currentNode))
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "The same Job was read from different saved ThinkTree sources.");
            }
            else { read.Jobs[job] = currentNode!; }
        }
        else if (mode == LoadSaveMode.PostLoadInit && ReadJobs.TryGetValue(session, out LegacyThinkJobReadSet? read)
            && read.Jobs.TryGetValue(job, out XmlNode? source) && IsInsideGame(source, session.Document!.Game))
        {
            exposingLegacyJob = session;
            exposingLegacyJobXml = source;
            exposingJob = job;
        }

        return previous;
    }

    internal static void LeaveJob(LegacyThinkJobScope previous)
    {
        exposingLegacyJob = previous.Session;
        exposingLegacyJobXml = previous.Node;
        exposingJob = previous.Job;
    }

    private static bool IsInsideGame(XmlNode? node, XmlElement game)
    {
        for (; node is not null; node = node.ParentNode)
        {
            if (ReferenceEquals(node, game)) { return true; }
        }

        return false;
    }

    /// <summary>返回 false 表示已识别的旧 Job 无法安全解析；由调用方返回未找到，不猜一个当前节点。</summary>
    internal static bool PrepareLookup(ThinkTreeDef tree, ref int key, out LegacyThinkLookup? lookup)
    {
        lookup = null;
        if (exposingLegacyJob is not { IsLegacyRun: true, Completed: false } session || key == -1)
        {
            return true;
        }

        // Job.ExposeData 中其他补丁的独立查询不属于这个字段的读协议。
        if (!ReferenceEquals(tree, exposingJob?.jobGiverThinkTree))
        {
            return true;
        }

        if (session.Report.Failed)
        {
            return false;
        }

        if (Problem is { } problem)
        {
            session.Report.Fail("Framework.ThinkTreeKeys", problem);
            return false;
        }

        XmlNode? source = exposingLegacyJobXml?["lastJobGiverKey"];
        if (source is null || !int.TryParse(source.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedKey)
            || savedKey != key)
        {
            session.Report.Fail("Framework.ThinkTreeKeys", "The Job ThinkTree lookup does not match its saved key; no identity was guessed.");
            return false;
        }

        int profiles = SelectProfiles(session);
        if (profiles == 0) { return false; }
        if (ReadJobs.TryGetValue(session, out LegacyThinkJobReadSet? read) && read.NativeLookups.Contains(exposingJob!))
        {
            return true;
        }
        ThinkNode? expected = null;
        for (int i = 0; i < 3; i++)
        {
            if ((profiles & (1 << i)) == 0 || !Layout(i).TryNode(key, out ThinkNode? candidate)
                || !tree.thinkRoot.ThisAndChildrenRecursive.Any(node => ReferenceEquals(node, candidate)))
            {
                continue;
            }
            if (expected is not null && !ReferenceEquals(expected, candidate))
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "Published ThinkTree layouts disagree on the node for legacy key " + key.ToString(CultureInfo.InvariantCulture) + ".");
                return false;
            }
            expected = candidate;
        }
        if (expected is null || expected.UniqueSaveKey == -1)
        {
            session.Report.Fail("Framework.ThinkTreeKeys", "No captured ThinkNode proves legacy key " + key.ToString(CultureInfo.InvariantCulture) + ".");
            return false;
        }

        lookup = new LegacyThinkLookup(session, source, tree, expected, expected.UniqueSaveKey);
        key = lookup.CanonicalKey;
        return true;
    }

    private static LegacyThinkTreeLedger Layout(int index) => index switch
    {
        0 => Ledger,
        1 => BeforeCorporateDuty,
        2 => FirstPublishedLayout,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static int SelectProfiles(SaveUpgradeSession session)
    {
        if (!ReadJobs.TryGetValue(session, out LegacyThinkJobReadSet? read))
        {
            session.Report.Fail("Framework.ThinkTreeKeys", "No captured Jobs prove the saved ThinkTree layout.");
            return 0;
        }
        if (read.Profiles is { } selected) { return selected; }

        try { return ComputeProfiles(session, read); }
        catch (Exception exception)
        {
            session.Report.Fail("Framework.ThinkTreeKeys", "Could not verify the captured ThinkTree layout: " + exception.Message);
            return 0;
        }
    }

    private static int ComputeProfiles(SaveUpgradeSession session, LegacyThinkJobReadSet read)
    {
        // Job.ExposeData 每次保存都从实际 jobGiver.UniqueSaveKey 写出整数；同一存档的来源
        // 属于同一全局分配布局。FinalizeLoading 完成全部 LoadingVars/CrossRefs 后才开始
        // PostLoadInit，故首次查询可以一次性求已捕获键的布局交集。
        // 外部树中任何布局都未捕获的外部 Job 来源无法证明本 Mod 的年代，交回原版查找并保留未解析的原键。
        // 不以 JobDef、游戏版本或缺失业务组件猜年代；也不扫描未被 Scribe 读取的 XML。
        read.Profiles = 0; // 冻结来源集合；异常或晚到的 LoadingVars 不能悄悄重新选择。
        int profiles = 7;
        var members = new Dictionary<ThinkTreeDef, HashSet<ThinkNode>>(ReferenceEqualityComparer.Instance);
        foreach ((Job job, XmlNode source) in read.Jobs)
        {
            XmlNode? saved = source["lastJobGiverKey"];
            if (saved is null) { continue; } // 原版缺省是 -1，正常无来源 Job 不参与。
            if (!IsInsideGame(source, session.Document!.Game)
                || !int.TryParse(saved.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "A captured Job has an invalid saved ThinkTree key.");
                return 0;
            }
            if (key == -1) { continue; }
            if (job.jobGiverThinkTree is not { thinkRoot: { } root } tree)
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "A saved ThinkTree key has no actual loaded tree.");
                return 0;
            }
            if (!members.TryGetValue(tree, out HashSet<ThinkNode>? nodes))
            {
                nodes = new HashSet<ThinkNode>(root.ThisAndChildrenRecursive, ReferenceEqualityComparer.Instance);
                members.Add(tree, nodes);
            }
            int available = 0;
            bool captured = false;
            for (int i = 0; i < 3; i++)
            {
                if (Layout(i).TryNode(key, out ThinkNode? candidate))
                {
                    captured = true;
                    if (nodes.Contains(candidate)) { available |= 1 << i; }
                }
            }
            if (!captured && !nodes.Any(node => node.UniqueSaveKey == key) && UnresolvedThinkTreeKeys.IsForeign(job, tree))
            {
                read.NativeLookups.Add(job);
                continue;
            }
            profiles &= available;
            if (profiles == 0)
            {
                session.Report.Fail("Framework.ThinkTreeKeys", "No single captured ThinkTree layout proves all saved Job keys: tree "
                    + tree.defName + ", key " + key.ToString(CultureInfo.InvariantCulture) + ", job " + job.def?.defName + ".");
                return 0;
            }
        }
        read.Profiles = profiles;
        return profiles;
    }

    internal static void CompleteLookup(ThinkTreeDef tree, LegacyThinkLookup? lookup, ref ThinkNode? node, ref bool found)
    {
        if (lookup is null) { return; }
        if (lookup.Session.Report.Failed || !found || node is null || !ReferenceEquals(tree, lookup.Tree)
            || !ReferenceEquals(node, lookup.ExpectedNode) || node.UniqueSaveKey != lookup.CanonicalKey
            || !tree.thinkRoot.ThisAndChildrenRecursive.Any(candidate => ReferenceEquals(candidate, lookup.ExpectedNode)))
        {
            lookup.Session.Report.Fail("Framework.ThinkTreeKeys", "The actual ThinkTree lookup did not return the captured node in its original tree.");
            node = null;
            found = false;
            return;
        }

        lookup.Session.ReadTrace?.Scalar("ThinkKey", lookup.Source,
            lookup.Source.InnerText, lookup.CanonicalKey.ToString(CultureInfo.InvariantCulture));
    }

    internal static void RememberNativeFailure(ThinkTreeDef tree, int key, ThinkNode? node, bool found, LegacyThinkLookup? lookup)
    {
        if (lookup is not null || found || node is not null || key == -1 || !AlgorithmVerified
            || exposingLegacyJob is not { Completed: false } session || session.Report.Failed
            || exposingJob is not { } job || !ReferenceEquals(job.jobGiverThinkTree, tree)
            || exposingLegacyJobXml?["lastJobGiverKey"] is not { } source
            || !int.TryParse(source.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int saved) || saved != key
            || !UnresolvedThinkTreeKeys.IsForeign(job, tree))
        {
            return;
        }
        UnresolvedThinkTreeKeys.Remember(job, tree, key);
    }

    internal static void LookupFailed(LegacyThinkLookup? lookup, Exception? exception)
    {
        if (lookup is not null && exception is not null)
        {
            lookup.Session.Report.Fail("Framework.ThinkTreeKeys", "The ThinkTree lookup threw before its identity could be verified: " + exception.Message);
        }
    }

    private static string LegacyShortName(ThinkNode node) => node.GetType() switch
    {
        Type type when type == typeof(Features.Incidents.JobGiver_RegisterPunisher) => "JobGiver_MugirlPunisher",
        Type type when type == typeof(Features.Roping.JobGiver_MugirlFollowRoper) => "JobGiver_FollowRoper",
        Type type when type == typeof(Features.Social.JobGiver_MugirlNuzzle) => "JobGiver_Nuzzle",
        _ => node.GetType().Name,
    };
}
