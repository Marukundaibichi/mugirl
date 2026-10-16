using System.Globalization;
using System.Linq;
using System.Xml;
using Mugirl.Features.Incidents;

namespace Mugirl.SaveUpgrade;

internal sealed class MigrationRecordsUpgrade : DomUpgradeStep
{
    internal const string StepId = "Incidents.MigrationRecords";
    internal static OwnerSelector Owner => OwnerSelector.MapComponent(typeof(MapComponent_Migration));
    // StaticCacheLifecycle: 不可变的批次事实标签映射。
    internal static readonly (string Old, string New)[] Fields =
    [
        ("playerKilledMigrationPawnIds", "playerKillIds"),
        ("exitCell", "exit"),
        ("ticksUntilDeparture", "departureRemaining"),
        ("killWindowTicksRemaining", "killWindowRemaining"),
        ("originalMigrationCount", "initialCount"),
        ("killedMigrationPawns", "playerKillCount"),
        ("migrationOutcomeFailed", "outcomeFailed"),
        ("departureStarted", "departing"),
    ];
    internal static IEnumerable<string> OldLabels => Fields.Select(row => row.Old).Concat(["migrationPawns", "nextGrazeTicks", "dataVersion"]);

    internal MigrationRecordsUpgrade() : base(StepId, "Zip records without dropping null references or changing countdowns and random scheduling.",
        [.. OldLabels.Select(label => "label:" + Owner.Text + "/" + label)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            if (owner["migration"] is not null)
            {
                if (OldLabels.Any(label => owner[label] is not null))
                {
                    throw new InvalidOperationException("Mixed migration schemas.");
                }

                continue;
            }

            XmlElement state = plan.AppendElement(owner, "migration");
            XmlElement members = plan.AppendElement(state, "members");
            XmlElement[] pawns = Items(owner["migrationPawns"]);
            XmlElement[] ticks = Items(owner["nextGrazeTicks"]);
            for (int i = 0; i < pawns.Length; i++)
            {
                XmlElement member = plan.AppendElement(members, "li");
                plan.Move(pawns[i], member, "pawn");
                if (i < ticks.Length)
                {
                    plan.Move(ticks[i], member, "grazeAtTick");
                }
            }

            foreach ((string oldName, string newName) in Fields)
            {
                if (owner[oldName] is { } node)
                {
                    plan.Move(node, state, newName);
                }
            }

            bool releaseLocks = ReadInt(owner, "dataVersion", 0) < 2;
            int missingIndex = ticks.Length < pawns.Length ? ticks.Length : -1;
            if (releaseLocks || missingIndex >= 0)
            {
                XmlElement initialization = plan.AppendElement(owner, "initialization");
                plan.SetAttribute(initialization, "Class", SaveTypeNames.Of(typeof(MigrationInitialization)));
                if (missingIndex >= 0)
                {
                    plan.Append(initialization, "firstUnscheduledIndex", missingIndex.ToString(CultureInfo.InvariantCulture));
                }

                if (releaseLocks)
                {
                    plan.Append(initialization, "releaseBikiniLocks", "True");
                }
            }

            foreach (string label in new[] { "migrationPawns", "nextGrazeTicks", "dataVersion" })
            {
                if (owner[label] is { } node)
                {
                    plan.Remove(node);
                }
            }
        }
    }

    internal static XmlElement[] Items(XmlElement? list)
    {
        if (list is null) { return []; }
        List<XmlElement> items = [];
        bool isNull = list.GetAttribute("IsNull").Equals("true", StringComparison.OrdinalIgnoreCase);
        foreach (XmlNode node in list.ChildNodes)
        {
            // 只跳过列表项之间的 XML 排版；项内纯空白字符串、CDATA 和属性随原元素搬迁。
            if (node.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.Text
                && (node.Value ?? string.Empty).All(character => character is ' ' or '\t' or '\r' or '\n'))
            {
                continue;
            }
            if (isNull || node is not XmlElement { Name: "li" } item || DomEditor.HasElementChild(item))
            {
                throw new InvalidOperationException("Legacy incident list " + list.Name + " contains unsupported data; its container cannot be discarded.");
            }
            items.Add(item);
        }
        return [.. items];
    }
    internal static int ReadInt(XmlElement owner, string label, int fallback) => owner[label] is { } node ? int.Parse(node.InnerText, CultureInfo.InvariantCulture) : fallback;
}
