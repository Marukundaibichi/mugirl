using System.Xml;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>支持嵌套 Job.ExposeData 的线程上下文恢复。</summary>
internal readonly record struct LegacyThinkJobScope(SaveUpgradeSession? Session, XmlNode? Node, Job? Job);
