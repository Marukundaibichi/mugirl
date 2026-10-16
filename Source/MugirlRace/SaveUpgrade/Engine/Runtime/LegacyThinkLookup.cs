using System.Xml;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>仅保留当前原版查找的已证明输入；成功结果必须仍指向同一个树内节点。</summary>
internal sealed record LegacyThinkLookup(SaveUpgradeSession Session, XmlNode Source, ThinkTreeDef Tree,
    ThinkNode ExpectedNode, int CanonicalKey, Job Job, int SavedKey);
