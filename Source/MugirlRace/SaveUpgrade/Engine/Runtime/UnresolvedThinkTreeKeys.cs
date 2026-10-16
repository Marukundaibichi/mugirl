using System.Runtime.CompilerServices;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>保留原版实际查找失败的外部 Job 来源，不猜节点，不清空任务，也不让下次保存抹掉原键。</summary>
internal static class UnresolvedThinkTreeKeys
{
    // StaticCacheLifecycle: 弱键只跟随实际读入的 Job；池回收时移除，复制时显式续接，不持有存档 DOM。
    private static readonly ConditionalWeakTable<Job, SavedKey> Keys = new();

    private static bool ProtocolVerified => ReviewedSaveEngine.CanPreserveUnresolvedThinkKeys(typeof(Job).Assembly.ManifestModule.ModuleVersionId);

    internal static bool IsForeign(Job job, ThinkTreeDef tree) =>
        ProtocolVerified && ForeignDef(tree) && job.def is { } definition && ForeignDef(definition)
        && definition.driverClass is { } driver && driver.Assembly != typeof(UnresolvedThinkTreeKeys).Assembly;

    private static bool ForeignDef(Def definition) => definition.modContentPack?.assemblies is { } owner
        && !owner.loadedAssemblies.Contains(typeof(UnresolvedThinkTreeKeys).Assembly)
        && !definition.defName.StartsWith("Mugirl", StringComparison.OrdinalIgnoreCase);

    internal static void Remember(Job job, ThinkTreeDef tree, int key)
    {
        Keys.Remove(job);
        Keys.Add(job, new SavedKey(key, tree, job.def, job.loadID));
    }

    internal static void Save(Job job, ref int jobGiverKey)
    {
        if (Scribe.mode != LoadSaveMode.Saving || !ProtocolVerified || !TryKey(job, out SavedKey? saved))
        {
            return;
        }
        if (jobGiverKey != -1) { Keys.Remove(job); return; }

        // 已核验的 Job.ExposeData 在来源为空时写 -1 并省略该标签；因此这里只补写一次原值。
        // 不屏蔽原版的未知节点警告，后续读取仍由真实树判断该键是否重新可用。
        int key = saved.Key;
        Scribe_Values.Look(ref key, "lastJobGiverKey", -1);
        jobGiverKey = key;
    }

    internal static void Copied(Job original, Job copy)
    {
        if (TryKey(original, out SavedKey? saved) && saved.Matches(copy))
        {
            Remember(copy, saved.Tree, saved.Key);
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

    private sealed record SavedKey(int Key, ThinkTreeDef Tree, JobDef Definition, int LoadId)
    {
        internal bool Matches(Job job) => job.jobGiver is null && ReferenceEquals(job.jobGiverThinkTree, Tree)
            && ReferenceEquals(job.def, Definition) && job.loadID == LoadId;
    }
}
