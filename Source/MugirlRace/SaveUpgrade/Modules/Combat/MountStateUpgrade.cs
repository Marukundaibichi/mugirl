using System.Linq;
using System.Xml;
using Mugirl.Features.Mounting;

namespace Mugirl.SaveUpgrade;

internal sealed class MountStateUpgrade : DomUpgradeStep
{
    internal const string StepId = "Combat.MountState";
    internal static OwnerSelector Owner => OwnerSelector.ThingComp(typeof(CompMount));
    // StaticCacheLifecycle: 不可变的旧标签与新状态位置，仅用于一次性升级。
    internal static readonly (string Old, string Group, string New)[] Fields =
    [
        ("innerContainer", "", "mugirlMountRiders"),
        ("fireAtWill", "", "mugirlMountFireAtWill"),
        ("physiologicalTickCounter", "mugirlMountSchedule", "physiology"),
        ("safetyTickCounter", "mugirlMountSchedule", "safety"),
        ("turretTickCounter", "mugirlMountSchedule", "turret"),
        ("riderMeleeTickCounter", "mugirlMountSchedule", "melee"),
        ("turretBurstCooldownTicksLeft", "mugirlMountTurret", "burstCooldownTicksLeft"),
        ("turretAimTarget", "mugirlMountTurret", "aimTarget"),
        ("turretAimTicksLeft", "mugirlMountTurret", "aimTicksLeft"),
        ("turretAimTicksTotal", "mugirlMountTurret", "aimTicksTotal"),
        ("turretCastStartTick", "mugirlMountTurret", "castStartTick"),
        ("turretLastAttackedTarget", "mugirlMountTurret", "lastAttackedTarget"),
        ("turretLastAttackTargetTick", "mugirlMountTurret", "lastAttackTargetTick"),
    ];

    internal MountStateUpgrade() : base(StepId, "Regroup all thirteen mount labels without changing riders, targets, defaults or elapsed values.",
        [.. Fields.Select(row => "label:" + Owner.Text + "/" + row.Old)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            if (owner["mugirlMountRiders"] is not null || owner["mugirlMountFireAtWill"] is not null
                || owner["mugirlMountSchedule"] is not null || owner["mugirlMountTurret"] is not null)
            {
                if (Fields.Any(row => owner[row.Old] is not null))
                {
                    throw new InvalidOperationException("Mixed flat and grouped mount state cannot be merged without losing data.");
                }

                continue;
            }

            if (!Fields.Any(row => owner[row.Old] is not null))
            {
                // Thing 保存后才新增此 comp 时，无旧数据需要搬移；领域初始化提供同一组默认值。
                continue;
            }

            XmlElement schedule = plan.AppendElement(owner, "mugirlMountSchedule");
            XmlElement turret = plan.AppendElement(owner, "mugirlMountTurret");
            foreach ((string oldName, string group, string newName) in Fields)
            {
                if (owner[oldName] is not { } node)
                {
                    continue;
                }

                if (group.Length == 0)
                {
                    plan.Rename(node, newName);
                }
                else
                {
                    plan.Move(node, group == "mugirlMountSchedule" ? schedule : turret, newName);
                }
            }

            // 缺失开火标志仍由同一个 CompProperties 的默认值在实际 Scribe 读取时解释，不能猜成 true。
        }
    }
}
