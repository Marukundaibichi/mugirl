using System.Xml;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>一个主存档中实际读入的 Job 来源；首次 PostLoad 查询后冻结，布局交集只计算一次。</summary>
internal sealed class LegacyThinkJobReadSet
{
    internal Dictionary<Job, XmlNode> Jobs { get; } = new(ReferenceEqualityComparer.Instance);
    internal Dictionary<Job, string> DeferredSources { get; } = new(ReferenceEqualityComparer.Instance);
    internal HashSet<Job> RetainedSources { get; } = new(ReferenceEqualityComparer.Instance);
    internal HashSet<Job> WarnedJobs { get; } = new(ReferenceEqualityComparer.Instance);
    internal int? Profiles { get; set; }
}
