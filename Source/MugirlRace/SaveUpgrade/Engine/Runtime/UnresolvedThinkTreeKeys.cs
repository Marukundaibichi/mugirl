using System.Runtime.CompilerServices;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>保留无法证明的 Job 来源及待解析标记；不改任务或驱动，也不在再次读档时猜测节点。</summary>
internal static class UnresolvedThinkTreeKeys
{
    internal const string MarkerLabel = "mugirlUnresolvedThinkSource";
    // StaticCacheLifecycle: 弱键只跟随实际读入的 Job；池回收时移除，复制时显式续接，不持有存档 DOM。
    private static readonly ConditionalWeakTable<Job, SavedKey> Keys = new();

    internal static bool ProtocolVerified => JobSourceWriteProtocol.Supported;

    internal static void Remember(Job job, ThinkTreeDef? tree, int key, string? treeName = null)
    {
        Keys.Remove(job);
        Keys.Add(job, new SavedKey(key, tree, treeName ?? tree?.defName, job.def, job.loadID));
    }

    internal static void Save(Job job, ref int jobGiverKey)
    {
        if (Scribe.mode != LoadSaveMode.Saving || !ProtocolVerified || !TryKey(job, out SavedKey? saved))
        {
            return;
        }
        if (jobGiverKey != -1) { Keys.Remove(job); return; }

        // 已核验的 Job.ExposeData 在来源为空时写 -1 并省略该标签；因此这里只补写一次原值。
        // 标记防止同一原整数在下一次读取时被误绑到碰巧同号的新节点。
        int key = saved.Key;
        Scribe_Values.Look(ref key, "lastJobGiverKey", -1);
        jobGiverKey = key;
        bool unresolved = true;
        Scribe_Values.Look(ref unresolved, MarkerLabel, false);
        // Scribe_Defs 对空 Def 省略标签；缺失树的原始名字也必须随来源保留，不能写成另一个有效树。
        if (job.jobGiverThinkTree is null && saved.TreeName is { Length: > 0 } originalName && originalName != "null")
        {
            Scribe_Values.Look(ref originalName, "jobGiverThinkTree", "null");
        }
    }

    internal static void Copied(Job original, Job copy)
    {
        if (TryKey(original, out SavedKey? saved) && saved.Matches(copy))
        {
            Remember(copy, saved.Tree, saved.Key, saved.TreeName);
        }
    }

    internal static void Forget(Job job) => Keys.Remove(job);

    private static bool TryKey(Job job, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SavedKey? saved)
    {
        if (Keys.TryGetValue(job, out saved) && saved.Matches(job)) { return true; }
        Keys.Remove(job);
        saved = null;
        return false;
    }

    private sealed record SavedKey(int Key, ThinkTreeDef? Tree, string? TreeName, JobDef? Definition, int LoadId)
    {
        internal bool Matches(Job job) => job.jobGiver is null && ReferenceEquals(job.jobGiverThinkTree, Tree)
            && ReferenceEquals(job.def, Definition) && job.loadID == LoadId;
    }
}
