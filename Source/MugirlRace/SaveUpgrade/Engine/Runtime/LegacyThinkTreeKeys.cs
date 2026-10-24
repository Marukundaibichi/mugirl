using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>把明确由主存档读取的旧 Job 绑定到当前 ThinkNode；新档及其他 Scribe 文档不走旧键空间。</summary>
internal static class LegacyThinkTreeKeys
{
    internal const string StepId = "Framework.ThinkTreeKeys";
    private const int CurrentLayoutIndex = 3;
    private const int LayoutCount = 4;
    // StaticCacheLifecycle: Def 级键空间，在原版 ThinkTreeKeyAssigner.Reset 同一时点清空。
    private static readonly LegacyThinkTreeLedger Ledger = new(LegacyShortName);
    private static readonly LegacyThinkTreeLedger BeforeCorporateDuty = new(LegacyShortName);
    private static readonly LegacyThinkTreeLedger FirstPublishedLayout = new(node => LegacyThinkLayouts.WasPawnKindCondition(node)
        ? "ThinkNode_ConditionalPawnKind" : LegacyShortName(node));
    private static readonly LegacyThinkTreeLedger CurrentLayout = new(node => node.GetType().Name);
    private static string? captureError;
    // StaticCacheLifecycle: 安装状态由启动检查更新；Def 的 Reset 不能使缺失挂钩重新变为可用。
    private static string? captureHookProblem;

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

    internal static bool AlgorithmVerified => ReviewedSaveEngine.ThinkTreeApiAvailable;

    internal static string? Problem => !AlgorithmVerified ? "The ThinkTree assignment API is unavailable; source identities will be retained." : captureHookProblem ?? captureError;

    internal static void SetCaptureHookProblem(string? problem) => captureHookProblem = problem;

    internal static void Reset()
    {
        Ledger.Reset();
        BeforeCorporateDuty.Reset();
        FirstPublishedLayout.Reset();
        CurrentLayout.Reset();
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
        if (!AlgorithmVerified || captureHookProblem is not null)
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
            if (assignedCount is { } count && (count != Ledger.AssignedCount || count != CurrentLayout.AssignedCount))
            {
                throw new InvalidOperationException("The ThinkTree assignment stream began before capture or was changed by another patch.");
            }

            // 升级失败后的正常保存仍写来源 schema，却会写当前 Job 节点键。
            // 当前布局也必须从完整分配流证明；在任何历史树名种子替换之前独立捕获。
            CurrentLayout.Capture(single ? [root] : root.ThisAndChildrenRecursive, seed);

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

    /// <summary>按真实分配结果验证影子账本。算法发生变化时仅停用来源映射，不按构建号拒绝整个存档。</summary>
    internal static void VerifyCapture(ThinkNode root, bool single)
    {
        if (captureError is not null || !AlgorithmVerified) { return; }
        try
        {
            foreach (ThinkNode node in single ? new[] { root } : root.ThisAndChildrenRecursive)
            {
                if (!CurrentLayout.TryNode(node.UniqueSaveKey, out ThinkNode? captured) || !ReferenceEquals(node, captured))
                {
                    captureError = "The actual ThinkTree assignment differs from the captured layout; original source keys will be retained.";
                    return;
                }
            }
        }
        catch (Exception exception) { captureError = "The assigned ThinkTree could not be checked: " + exception.Message; }
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
            if (read.Profiles is not null) { read.DeferredSources[job] = "late-source"; }
            else if (read.Jobs.TryGetValue(job, out XmlNode? existing) && !ReferenceEquals(existing, currentNode))
            { read.DeferredSources[job] = "repeated-source"; }
            read.Jobs[job] = currentNode!;
            ReadRetainedMarker(session, read, job, currentNode!);
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

    private static void ReadRetainedMarker(SaveUpgradeSession session, LegacyThinkJobReadSet read, Job job, XmlNode source)
    {
        XmlElement[] markers = [.. source.ChildNodes.OfType<XmlElement>().Where(node => node.Name == UnresolvedThinkTreeKeys.MarkerLabel)];
        read.RetainedSources.Remove(job);
        if (markers.Length == 0) { return; }
        if (markers.Length != 1 || DomEditor.HasElementChild(markers[0]) || !bool.TryParse(markers[0].InnerText, out bool retained))
        {
            session.Report.Fail(StepId, "The saved unresolved ThinkTree source marker is malformed.");
            return;
        }
        if (!retained) { return; }
        if (source["lastJobGiverKey"] is not { } saved
            || !int.TryParse(saved.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key) || key == -1)
        {
            session.Report.Fail(StepId, "The saved unresolved ThinkTree source marker has no valid original key.");
            return;
        }
        read.RetainedSources.Add(job);
    }

    /// <summary>返回 false 表示已识别的旧 Job 无法安全解析；由调用方返回未找到，不猜一个当前节点。</summary>
    internal static bool PrepareLookup(ThinkTreeDef tree, ref int key, out LegacyThinkLookup? lookup)
    {
        lookup = null;
        if (exposingLegacyJob is not { Completed: false } session || exposingJob is not { } job || key == -1)
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

        bool retained = ReadJobs.TryGetValue(session, out LegacyThinkJobReadSet? read) && read.RetainedSources.Contains(job);
        if (!session.IsLegacyRun && !retained) { return true; }

        XmlNode? source = exposingLegacyJobXml?["lastJobGiverKey"];
        if (source is null || !int.TryParse(source.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedKey))
        {
            session.Report.Fail("Framework.ThinkTreeKeys", "The Job ThinkTree lookup does not match its saved key; no identity was guessed.");
            return false;
        }
        if (savedKey != key) { return Defer(session, job, tree, savedKey, "source-key-changed"); }

        if (retained) { return Defer(session, job, tree, key, "retained-source"); }
        if (!AlgorithmVerified) { return Defer(session, job, tree, key, "assignment-api-unavailable"); }
        if ((captureHookProblem ?? captureError) is { } problem)
        {
            if (read is not null) { read.Profiles ??= 0; }
            return Defer(session, job, tree, key, "capture-unavailable: " + problem);
        }

        int profiles = SelectProfiles(session);
        if (session.Report.Failed) { return false; }
        if (read is not null && read.DeferredSources.TryGetValue(job, out string? reason))
        {
            return Defer(session, job, tree, key, reason);
        }
        if (profiles == 0) { return false; }
        ThinkNode? expected = null;
        for (int i = 0; i < LayoutCount; i++)
        {
            if ((profiles & (1 << i)) == 0 || !Layout(i).TryNode(key, out ThinkNode? candidate)
                || !MatchesSavedLayout(i, candidate, tree, exposingLegacyJobXml!, key)
                || !tree.thinkRoot.ThisAndChildrenRecursive.Any(node => ReferenceEquals(node, candidate)))
            {
                continue;
            }
            if (expected is not null && !ReferenceEquals(expected, candidate))
            {
                return Defer(session, job, tree, key, "ambiguous-layouts");
            }
            expected = candidate;
        }
        if (expected is null || expected.UniqueSaveKey == -1)
        {
            return Defer(session, job, tree, key, "unavailable-node");
        }

        lookup = new LegacyThinkLookup(session, source, tree, expected, expected.UniqueSaveKey, job, savedKey);
        key = lookup.CanonicalKey;
        return true;
    }

    private static LegacyThinkTreeLedger Layout(int index) => index switch
    {
        0 => Ledger,
        1 => BeforeCorporateDuty,
        2 => FirstPublishedLayout,
        CurrentLayoutIndex => CurrentLayout,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    // 当前 writer 只会写实际树名和实际节点键；旧树名搭配当前键不能证明来源。
    private static bool MatchesSavedLayout(int index, ThinkNode node, ThinkTreeDef tree, XmlNode source, int key)
        => index != CurrentLayoutIndex || (source["jobGiverThinkTree"]?.InnerText == tree.defName && node.UniqueSaveKey == key);

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
            foreach (Job job in read.Jobs.Keys) { read.DeferredSources[job] = "layout-unavailable: " + exception.Message; }
            return 0;
        }
    }

    private static int ComputeProfiles(SaveUpgradeSession session, LegacyThinkJobReadSet read)
    {
        // 只由能够匹配真实树的来源约束布局；未知、错树与既有待解析来源各自保留，不能否决别的任务。
        // 有效来源彼此冲突时也不选一个年代：整组保留原键并警告，不将猜测绑定成成功迁移。
        read.Profiles = 0; // 冻结来源集合；异常或晚到的 LoadingVars 不能悄悄重新选择。
        int profiles = (1 << LayoutCount) - 1;
        var members = new Dictionary<ThinkTreeDef, HashSet<ThinkNode>>(ReferenceEqualityComparer.Instance);
        var constrained = new List<Job>();
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
            if (read.RetainedSources.Contains(job)) { continue; }
            if (read.DeferredSources.ContainsKey(job)) { continue; }
            if (job.jobGiverThinkTree is not { thinkRoot: { } root } tree)
            {
                read.DeferredSources[job] = "missing-tree";
                continue;
            }
            if (!members.TryGetValue(tree, out HashSet<ThinkNode>? nodes))
            {
                nodes = new HashSet<ThinkNode>(root.ThisAndChildrenRecursive, ReferenceEqualityComparer.Instance);
                members.Add(tree, nodes);
            }
            int available = 0;
            int captured = 0;
            for (int i = 0; i < LayoutCount; i++)
            {
                if (Layout(i).TryNode(key, out ThinkNode? candidate))
                {
                    captured |= 1 << i;
                    if (nodes.Contains(candidate) && MatchesSavedLayout(i, candidate, tree, source, key)) { available |= 1 << i; }
                }
            }
            if (available == 0)
            {
                read.DeferredSources[job] = (captured == 0 ? "uncaptured-key" : "wrong-tree-or-writer")
                    + "; captured=" + ProfileNames(captured) + "; nativeMatches="
                    + nodes.Count(node => node.UniqueSaveKey == key).ToString(CultureInfo.InvariantCulture);
                continue;
            }
            constrained.Add(job);
            profiles &= available;
        }
        if (profiles == 0)
        {
            foreach (Job job in constrained) { read.DeferredSources[job] = "incompatible-layouts"; }
        }
        read.Profiles = profiles;
        return profiles;
    }

    /// <summary>仅保留来源，不碰 JobDef、目标、计数或 JobDriver。false 让原版发出缺失来源警告。</summary>
    private static bool Defer(SaveUpgradeSession session, Job job, ThinkTreeDef? tree, int key, string reason)
    {
        if (!UnresolvedThinkTreeKeys.ProtocolVerified)
        {
            session.Report.Fail(StepId, "The Job source writer no longer has the required omitted -1 field contract; the original key cannot be safely written.");
            return false;
        }
        if (!ReferenceEquals(job.jobGiverThinkTree, tree))
        {
            session.Report.Fail(StepId, "The Job changed its saved ThinkTree while its source was being read.");
            return false;
        }
        LegacyThinkJobReadSet read = ReadJobs.GetValue(session, static _ => new LegacyThinkJobReadSet());
        string? treeName = tree?.defName;
        if (treeName is null && read.Jobs.TryGetValue(job, out XmlNode? source)) { treeName = source["jobGiverThinkTree"]?.InnerText; }
        UnresolvedThinkTreeKeys.Remember(job, tree, key, treeName);
        if (read.WarnedJobs.Add(job))
        {
            session.Report.Warn(StepId, "Unresolved AI source retained: tree " + (treeName ?? "<none>") + ", key "
                + key.ToString(CultureInfo.InvariantCulture) + ", job " + job.def?.defName + ", loadID "
                + job.loadID.ToString(CultureInfo.InvariantCulture) + ". Reason=" + reason + ". Task state was not reset.");
        }
        return false;
    }

    /// <summary>原版有键时会直接访问 tree。缺树只跳过这次来源查询，随后仍执行 Job 的其他 PostLoadInit。</summary>
    internal static bool RetainMissingTree(Job job, int key)
    {
        if (key == -1 || job.jobGiverThinkTree?.thinkRoot is not null
            || exposingLegacyJob is not { Completed: false } session || session.Report.Failed
            || !ReferenceEquals(exposingJob, job)) { return false; }
        Defer(session, job, job.jobGiverThinkTree, key, "missing-tree");
        return !session.Report.Failed;
    }

    private static string ProfileNames(int profiles) => profiles == 0 ? "none" : string.Join(",",
        Enumerable.Range(0, LayoutCount).Where(index => (profiles & (1 << index)) != 0).Select(index => index switch
        {
            0 => "renamed", 1 => "before-duty", 2 => "first-published", CurrentLayoutIndex => "current",
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        }));

    /// <summary>只说明环境差异，不用模组列表推断节点或放宽升级条件。</summary>
    internal static string DescribeModDifference(IReadOnlyList<string> savedIds, IEnumerable<string> activeIds)
    {
        string[] saved = [.. savedIds.Select(id => id.Trim())];
        string[] active = [.. activeIds.Select(id => id.Trim())];
        var savedSet = new HashSet<string>(saved, StringComparer.OrdinalIgnoreCase);
        var activeSet = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
        string[] added = [.. active.Where(id => !savedSet.Contains(id))];
        string[] removed = [.. saved.Where(id => !activeSet.Contains(id))];
        bool orderChanged = !saved.Where(activeSet.Contains).SequenceEqual(active.Where(savedSet.Contains), StringComparer.OrdinalIgnoreCase);
        return FormattableString.Invariant($"Mod environment: saved={saved.Length}, active={active.Length}, added={added.Length} [{string.Join(",", added.Take(8))}], removed={removed.Length} [{string.Join(",", removed.Take(8))}], commonOrderChanged={orderChanged}. Differences may change keys; this does not prove a node mapping.");
    }

    internal static void CompleteLookup(ThinkTreeDef tree, LegacyThinkLookup? lookup, ref ThinkNode? node, ref bool found)
    {
        if (lookup is null) { return; }
        if (lookup.Session.Report.Failed || !found || node is null || !ReferenceEquals(tree, lookup.Tree)
            || !ReferenceEquals(node, lookup.ExpectedNode) || node.UniqueSaveKey != lookup.CanonicalKey
            || !tree.thinkRoot.ThisAndChildrenRecursive.Any(candidate => ReferenceEquals(candidate, lookup.ExpectedNode)))
        {
            node = null;
            found = false;
            if (!lookup.Session.Report.Failed)
            {
                Defer(lookup.Session, lookup.Job, lookup.Tree, lookup.SavedKey, "lookup-mismatch");
            }
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
            || !int.TryParse(source.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int saved) || saved != key)
        {
            return;
        }
        Defer(session, job, tree, key, "native-source-missing");
    }

    internal static bool LookupFailed(LegacyThinkLookup? lookup, Exception? exception)
    {
        if (lookup is not null && exception is not null and not OutOfMemoryException && !lookup.Session.Report.Failed)
        {
            Defer(lookup.Session, lookup.Job, lookup.Tree, lookup.SavedKey, "lookup-exception: " + exception.Message);
            return !lookup.Session.Report.Failed;
        }
        return false;
    }

    private static string LegacyShortName(ThinkNode node) => node.GetType() switch
    {
        Type type when type == typeof(Features.Incidents.JobGiver_RegisterPunisher) => "JobGiver_MugirlPunisher",
        Type type when type == typeof(Features.Roping.JobGiver_MugirlFollowRoper) => "JobGiver_FollowRoper",
        Type type when type == typeof(Features.Social.JobGiver_MugirlNuzzle) => "JobGiver_Nuzzle",
        _ => node.GetType().Name,
    };
}
