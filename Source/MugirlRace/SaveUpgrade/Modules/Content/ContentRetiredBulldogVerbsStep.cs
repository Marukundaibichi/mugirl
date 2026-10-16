using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>退役机械斗牛犬的独有动词不再有生成来源；只删除真实 VerbTracker 条目并注销其引用身份。</summary>
internal sealed class ContentRetiredBulldogVerbsStep() : DomUpgradeStep(
    "Content.RetiredBulldogVerbs", "Remove saved verbs belonging to the already retired bulldog maneuver.",
    "type:Mugirl.Verb_MeleeAttack_SegmentedTailAssist", "def:ManeuverDef/Mugirl_BulldogTailAssistedCutManeuver")
{
    internal const string Maneuver = "Mugirl_BulldogTailAssistedCutManeuver";
    private const string LegacyVerb = "Mugirl.Verb_MeleeAttack_SegmentedTailAssist";

    internal override IEnumerable<string> IndexNames => ["loadID"];

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        var handled = new HashSet<XmlElement>();
        foreach (XmlElement id in context.Index.ElementsNamed("loadID"))
        {
            if (!context.IsLive(id) || id.ParentNode is not XmlElement { Name: "li" } verb
                || verb.ParentNode is not XmlElement { Name: "verbs", ParentNode: XmlElement { Name: "verbTracker" } })
            {
                continue;
            }

            if (context.OriginalClassOf(verb) != LegacyVerb && !id.InnerText.EndsWith("_" + Maneuver, StringComparison.Ordinal))
            {
                continue;
            }

            plan.RemoveItem(verb, NullForm.Value);
            plan.RemovedLoadIds.Add("Verb_" + id.InnerText);
            handled.Add(verb);
        }

        foreach (XmlElement element in context.Index.ElementsWithMappedClass(LegacyVerb))
        {
            if (context.IsLive(element) && !handled.Contains(element))
            {
                throw new InvalidOperationException("Retired bulldog verb has no recognized VerbTracker identity; its data was not discarded.");
            }
        }
    }
}
