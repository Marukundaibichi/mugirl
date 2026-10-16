using System.Globalization;
using System.Linq;
using System.Xml;
using Mugirl.Features.Incidents;

namespace Mugirl.SaveUpgrade;

internal sealed class PunisherRecordsUpgrade : DomUpgradeStep
{
    internal const string StepId = "Incidents.PunisherRecords";
    internal static OwnerSelector Owner => OwnerSelector.MapComponent(typeof(MapComponent_Punisher));
    // StaticCacheLifecycle: 不可变的历史平行列表及容器标签。
    internal static readonly string[] OldLabels = ["punishers", "punisherSpawnedAtTicks", "punisherLeavingFlags", "punisherHeldLoot", "punisherLootBursts"];

    internal PunisherRecordsUpgrade() : base(StepId, "Preserve punisher identity, explicit leaving flags, null slots and loot ownership.",
        [.. OldLabels.Select(label => "label:" + Owner.Text + "/" + label)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            if (owner["activePunishers"] is not null)
            {
                if (OldLabels.Any(label => owner[label] is not null))
                {
                    throw new InvalidOperationException("Mixed punisher schemas.");
                }

                continue;
            }

            if ((owner["punisherHeldLoot"] is not null && owner["heldLoot"] is not null)
                || (owner["punisherLootBursts"] is not null && owner["lootBursts"] is not null))
            {
                throw new InvalidOperationException("Mixed legacy and canonical punisher loot containers.");
            }
            bool legacyEvidence = OldLabels.Any(label => owner[label] is not null)
                || (context.OriginalClassOf(owner) is { } original && context.Tables.FindType(original) is not null);

            XmlElement records = plan.AppendElement(owner, "activePunishers");
            XmlElement[] pawns = MigrationRecordsUpgrade.Items(owner["punishers"]);
            XmlElement[] ticks = MigrationRecordsUpgrade.Items(owner["punisherSpawnedAtTicks"]);
            XmlElement[] leaving = MigrationRecordsUpgrade.Items(owner["punisherLeavingFlags"]);
            for (int i = 0; i < pawns.Length; i++)
            {
                XmlElement record = plan.AppendElement(records, "li");
                plan.Move(pawns[i], record, "pawn");
                if (i < ticks.Length)
                {
                    plan.Move(ticks[i], record, "spawnTick");
                }

                if (i < leaving.Length)
                {
                    plan.Move(leaving[i], record, "leaving");
                }
            }

            if (ticks.Length < pawns.Length)
            {
                XmlElement initialization = plan.AppendElement(owner, "initialization");
                plan.SetAttribute(initialization, "Class", SaveTypeNames.Of(typeof(PunisherInitialization)));
                plan.Append(initialization, "firstUnscheduledIndex", ticks.Length.ToString(CultureInfo.InvariantCulture));
            }

            if (owner["punisherHeldLoot"] is { } held)
            {
                plan.Rename(held, "heldLoot");
            }
            else if (legacyEvidence && owner["heldLoot"] is null)
            {
                // 旧 PostLoadInit 对缺失字段创建同一 owner 的空 ThingOwner<Thing>；没有物品、ID 或随机动作。
                XmlElement empty = plan.AppendElement(owner, "heldLoot");
                plan.AppendElement(empty, "innerList");
            }

            if (owner["punisherLootBursts"] is { } bursts)
            {
                plan.Rename(bursts, "lootBursts");
            }
            else if (legacyEvidence && owner["lootBursts"] is null)
            {
                plan.AppendElement(owner, "lootBursts");
            }

            foreach (string label in OldLabels.Take(3))
            {
                if (owner[label] is { } node)
                {
                    plan.Remove(node);
                }
            }
        }
    }
}
