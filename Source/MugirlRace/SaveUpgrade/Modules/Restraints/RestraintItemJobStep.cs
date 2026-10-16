using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Xml;
using Mugirl.Features.Restraints;

namespace Mugirl.SaveUpgrade;

/// <summary>仅提交有唯一语义证据的在途取物工作映射；保留所有目标、持有物、时长及已发生的效果。</summary>
internal sealed class RestraintItemJobStep : DomUpgradeStep
{
    internal const string StepId = "Restraints.ItemJobContinuations";
    private static readonly OwnerSelector Owner = OwnerSelector.Class(typeof(JobDriver_CrackRestraint), typeof(JobDriver_UnlockRestraint), typeof(JobDriver_UseItemOn));

    internal RestraintItemJobStep()
        : base(StepId, "Infer a unique saved movement/wait phase across historical and current layouts; fail without ending or restarting ambiguous work.",
            "type:Mugirl.JobDriver_CrackBondageGear", "type:Mugirl.JobDriver_UnlockSlaveApparelGear", "type:Mugirl.JobDriver_UseItemOn")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        Dictionary<string, string> identities = new(StringComparer.Ordinal);
        Dictionary<string, XmlElement> owners = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);
        foreach (var thing in context.Index.Things)
        {
            if (!context.IsLive(thing.Element))
            {
                continue;
            }

            string reference = "Thing_" + thing.Id;
            string? renamedDef = context.Tables.RenamedDef("ThingDef", thing.Def);
            string canonical = renamedDef is null ? reference : "Thing_" + LoadIdTokens.RenamedThingId(thing.Id, renamedDef);
            if (owners.TryGetValue(canonical, out XmlElement? previous) && !ReferenceEquals(previous, thing.Element))
            {
                ambiguous.Add(canonical);
            }

            owners[canonical] = thing.Element;
            identities[reference] = canonical;
            identities[canonical] = canonical;
            if (renamedDef is not null && LoadIdTokens.OriginalThingId(thing.Def, renamedDef, thing.Id) is { } original)
            {
                identities["Thing_" + original] = canonical;
            }
        }

        foreach (XmlElement driver in Owner.Candidates(context))
        {
            if (!context.IsLive(driver) || driver.Name != "curDriver" || driver.ParentNode is not XmlElement { Name: "jobs" } jobs
                || jobs.ParentNode is not XmlElement pawn || jobs["curJob"] is not { } job)
            {
                continue;
            }

            if (driver["itemFlowVersion"] is { } version)
            {
                if (version.InnerText != "1")
                {
                    throw new InvalidOperationException("Unknown restraint item-flow version.");
                }

                continue;
            }

            string? type = context.MappedClassOf(driver);
            string? item = job["targetA"]?.InnerText;
            string? target = job["targetB"]?.InnerText;
            if (type == typeof(JobDriver_UseItemOn).FullName && !IsThingReference(target))
            {
                // 该分支完整继承原版 UseItem，已核实两版本的完整类型相同。
                plan.Append(driver, "itemFlowVersion", "1");
                continue;
            }

            if (!IsThingReference(item) || !IsThingReference(target)
                || !identities.TryGetValue(item, out string? itemId) || !identities.TryGetValue(target, out string? targetId)
                || pawn["id"] is not { } pawnId)
            {
                throw new InvalidOperationException("Restraint item-job targets cannot be identified without guessing.");
            }

            string actor = identities.TryGetValue("Thing_" + pawnId.InnerText, out string? actorId) ? actorId : "Thing_" + pawnId.InnerText;
            if (ambiguous.Contains(itemId) || ambiguous.Contains(targetId) || ambiguous.Contains(actor))
            {
                throw new InvalidOperationException("Duplicate identity in a restraint item-job continuation.");
            }
            RestraintItemFlow.Kind kind = type == typeof(JobDriver_CrackRestraint).FullName ? RestraintItemFlow.Kind.Crack
                : type == typeof(JobDriver_UseItemOn).FullName ? RestraintItemFlow.Kind.UseOnTarget
                : targetId == actor ? RestraintItemFlow.Kind.UnlockSelf : RestraintItemFlow.Kind.UnlockOther;
            XmlElement? pather = pawn["pather"];
            bool moving = pather is not null && ReadBool(pather, "moving", true);
            string? destination = pather?["destination"]?.InnerText;
            if (destination is not null && identities.TryGetValue(destination, out string? mapped))
            {
                destination = mapped;
            }

            bool toItem = destination == itemId;
            bool toTarget = destination == targetId;
            RestraintItemFlow.Destination pathTarget = toItem && toTarget ? RestraintItemFlow.Destination.Both
                : toItem ? RestraintItemFlow.Destination.Item : toTarget ? RestraintItemFlow.Destination.Target : RestraintItemFlow.Destination.Unknown;
            var snapshot = new RestraintItemFlow.Snapshot(ReadInt(driver, "curToilIndex", 0), ReadInt(driver, "nextToilIndex", -1),
                ReadInt(driver, "ticksLeftThisToil", 0), ReadBool(driver, "wantBeginNextToil", false), moving, pathTarget);
            RestraintItemFlow.Result continuation = RestraintItemFlow.Resolve(kind, snapshot);
            SetIndex(driver, "curToilIndex", snapshot.Current, continuation.Current, plan);
            SetIndex(driver, "nextToilIndex", snapshot.Next, continuation.Next, plan);
            plan.Append(driver, "itemFlowVersion", "1");
        }
    }

    private static bool IsThingReference([NotNullWhen(true)] string? text) => text?.StartsWith("Thing_", StringComparison.Ordinal) == true;

    private static int ReadInt(XmlElement owner, string label, int fallback)
        => owner[label] is not { } value ? fallback
            : int.TryParse(value.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result
            : throw new InvalidOperationException("Invalid item-job integer " + label + ".");

    private static bool ReadBool(XmlElement owner, string label, bool fallback)
        => owner[label] is not { } value ? fallback
            : bool.TryParse(value.InnerText, out bool result) ? result
            : throw new InvalidOperationException("Invalid item-job boolean " + label + ".");

    private static void SetIndex(XmlElement owner, string label, int previous, int current, EditPlan plan)
    {
        if (previous == current)
        {
            return;
        }

        string text = current.ToString(CultureInfo.InvariantCulture);
        if (owner[label] is { } value)
        {
            plan.SetText(value, text);
        }
        else
        {
            plan.Append(owner, label, text);
        }
    }
}
