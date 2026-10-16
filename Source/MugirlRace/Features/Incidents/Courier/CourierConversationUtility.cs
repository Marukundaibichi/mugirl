using System.Diagnostics.CodeAnalysis;
using Mugirl.Features.Corporate;
using Mugirl.Features.Restraints;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 与运货员的对话：交出物品后离开（任务成功），或开战（运货员脱离派系后狂暴，避免影响与巨企的外交）。
public static class CourierConversationUtility
{
    public static bool CanTalkToCourier([NotNullWhen(true)] Pawn? courier)
    {
        return courier != null
            && courier.Spawned
            && !courier.Dead
            && !courier.Downed
            && !courier.InAggroMentalState
            && courier.kindDef == CorporateDefOf.Mugirl_GiantCorp_Courier
            && HasCourierPayload(courier);
    }

    public static void ShowDialog(Pawn? courier, Pawn negotiator)
    {
        if (!CanNegotiateWithCourier(negotiator, courier))
        {
            Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        DiaNode main = new DiaNode("Mugirl.CourierDialogText".Translate(courier.Named("COURIER"), negotiator.Named("NEGOTIATOR")));
        main.options.Add(new DiaOption("Mugirl.CourierDialogLeaveItems".Translate())
        {
            action = () => TryDropItemsAndLeave(courier),
            resolveTree = true
        });
        main.options.Add(new DiaOption("Mugirl.CourierDialogFight".Translate())
        {
            action = () => TryStartFight(courier),
            resolveTree = true
        });
        Dialog_NodeTree dialog = new Dialog_NodeTree(main, delayInteractivity: true, title: "Mugirl.CourierDialogTitle".Translate());
        GameUtility.TryAddWindow(dialog);
    }

    public static bool CanNegotiateWithCourier(Pawn? negotiator, [NotNullWhen(true)] Pawn? courier)
    {
        return negotiator != null
            && PlayerFactionUtility.IsPlayerFaction(negotiator.Faction)
            && negotiator.RaceProps?.Humanlike == true
            && !negotiator.Dead
            && !negotiator.Downed
            && CanTalkToCourier(courier);
    }

    private static bool HasCourierPayload(Pawn courier)
    {
        ThingOwner<Thing>? inventory = courier.inventory?.innerContainer;
        if (inventory == null)
        {
            return false;
        }

        for (int i = 0; i < inventory.Count; i++)
        {
            ThingDef? def = inventory[i]?.def;
            if (def == IncidentsDefOf.Mugirl_CourierDiary
                || def == RestraintsDefOf.Mugirl_SlaveApparelKey_Medieval
                || def == RestraintsDefOf.Mugirl_SlaveApparelKey_Industrial)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDropItemsAndLeave(Pawn courier)
    {
        if (!CanTalkToCourier(courier))
        {
            Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        QuestPart_SpawnCourier.RefreshOpeningCrashKeys(courier);
        courier.inventory.DropAllNearPawn(courier.Position, forbid: false, unforbid: true);
        EventLordUtility.ExitMapJogging(courier);
        CorporateIntroduction.Current?.NotifyCourierChoice(courier, released: true);
        EndRelatedQuest(courier, QuestEndOutcome.Success);
        Messages.Message("Mugirl.CourierItemsDroppedMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.PositiveEvent);
    }

    private static void TryStartFight(Pawn courier)
    {
        if (!CanTalkToCourier(courier) || courier.mindState?.mentalStateHandler == null)
        {
            Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        courier.GetLord()?.RemovePawn(courier);
        if (courier.Faction != null)
        {
            courier.SetFaction(null);
        }

        courier.mindState.mentalStateHandler.TryStartMentalState(
            MentalStateDefOf.Berserk,
            "Mugirl.CourierFightReason".Translate(),
            forced: true,
            forceWake: true,
            transitionSilently: true);
        CorporateIntroduction.Current?.NotifyCourierChoice(courier, released: false);
        EndRelatedQuest(courier, QuestEndOutcome.Unknown);
        Messages.Message("Mugirl.CourierFightMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.ThreatSmall);
        GameUtility.TrySignalForceNormalSpeedShort();
    }

    private static void EndRelatedQuest(Pawn courier, QuestEndOutcome outcome)
    {
        if (!GameUtility.TryGetQuestsListForReading(out List<Quest>? quests))
        {
            return;
        }

        for (int i = 0; i < quests.Count; i++)
        {
            Quest quest = quests[i];
            if (quest == null || quest.root != IncidentsDefOf.Mugirl_CourierContact || quest.State != QuestState.Ongoing)
            {
                continue;
            }

            List<QuestPart> parts = quest.PartsListForReading;
            for (int j = 0; j < parts.Count; j++)
            {
                if (parts[j] is QuestPart_SpawnCourier spawnPart && spawnPart.courier == courier)
                {
                    quest.End(outcome, sendLetter: false, playSound: false);
                    return;
                }
            }
        }
    }
}
