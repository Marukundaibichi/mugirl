using System.Linq;
using RimWorld.Planet;
using Verse.AI;

namespace Mugirl.Features.Corporate;

/// <summary>巨企终端的入口条件与打开方式：殖民地通讯台，或停在巨企据点的商队。</summary>
internal static class CorporateCommsAccess
{
    /// <summary>通讯台不能使用的原因；<see cref="None"/> 表示可以使用。</summary>
    internal enum ConsoleBlock
    {
        None,
        Unavailable,
        NoPower,
        CannotTalk,
        NoPath,
        Locked,
    }

    /// <summary>
    /// 检查 <paramref name="pawn"/> 能否用 <paramref name="console"/> 联系巨企（含寻路判断），不生成任何文字。
    /// 每 tick 的失败检查与每个 GUI 事件的连接检查只需要这个结果。
    /// </summary>
    internal static ConsoleBlock CheckConsole(Building_CommsConsole? console, Pawn? pawn)
    {
        if (console?.Spawned != true || pawn?.Spawned != true || pawn.Map != console.Map
            || !IsPlayerFaction(console.Faction) || !IsPlayerFaction(pawn.Faction)
            || pawn.Dead || pawn.Downed || pawn.InMentalState || !pawn.RaceProps.Humanlike)
        {
            return ConsoleBlock.Unavailable;
        }

        if (!console.CanUseCommsNow)
        {
            return ConsoleBlock.NoPower;
        }

        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
        {
            return ConsoleBlock.CannotTalk;
        }

        if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some))
        {
            return ConsoleBlock.NoPath;
        }

        if (CorporateIntroduction.Current?.Completed != true)
        {
            return ConsoleBlock.Locked;
        }

        return ConsoleBlock.None;
    }

    /// <summary>通讯台不能使用时给玩家看的原因；可以使用时为 null。</summary>
    internal static string? ConsoleFailure(Building_CommsConsole? console, Pawn? pawn) => Describe(CheckConsole(console, pawn));

    internal static string? Describe(ConsoleBlock block) => block switch
    {
        ConsoleBlock.None => null,
        ConsoleBlock.NoPower => "Mugirl.CorporateUI.NoPower".Translate(),
        ConsoleBlock.CannotTalk => "Mugirl.CorporateUI.CannotTalk".Translate(),
        ConsoleBlock.NoPath => "Mugirl.CorporateUI.NoPath".Translate(),
        ConsoleBlock.Locked => "Mugirl.CorporateUI.Locked".Translate(),
        _ => "Mugirl.CorporateUI.Unavailable".Translate(),
    };

    /// <summary>玩家商队停在（未移动）巨企据点所在的地块。</summary>
    internal static bool AtSettlement(Caravan? caravan, Settlement? settlement)
    {
        return caravan?.Spawned == true && caravan.IsPlayerControlled && !caravan.pather.Moving
            && settlement?.Spawned == true && caravan.Tile == settlement.Tile
            && settlement.Faction?.def == CorporateDefOf.Mugirl_GiantCorporation;
    }

    /// <summary>由站在通讯台交互格上的 Pawn 打开终端；条件不满足时显示原因。</summary>
    internal static void OpenConsole(Building_CommsConsole? console, Pawn pawn)
    {
        string? failure = ConsoleFailure(console, pawn);
        if (failure is not null || console is null)
        {
            if (failure is not null)
            {
                Messages.Message(failure, MessageTypeDefOf.RejectInput, historical: false);
            }

            return;
        }

        CorporateTradeContext context = new(console.Map,
            () => CheckConsole(console, pawn) == ConsoleBlock.None && pawn.Position == console.InteractionCell)
        {
            Negotiator = pawn,
        };
        GameUtility.TryAddWindow(new Dialog_CorporateTerminal(context));
    }

    /// <summary>由停在巨企据点的商队打开终端；谈判者取社交技能最高的可交谈殖民者。</summary>
    internal static void OpenSettlement(Caravan caravan, Settlement settlement)
    {
        if (!AtSettlement(caravan, settlement) || CorporateIntroduction.Current?.Completed != true)
        {
            return;
        }

        CorporateTradeContext context = new(caravan,
            () => AtSettlement(caravan, settlement) && CorporateIntroduction.Current?.Completed == true)
        {
            Negotiator = caravan.PawnsListForReading.Where(p => p.IsColonist && !p.IsSlave && !p.Dead && !p.Downed
                    && !p.InMentalState && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).FirstOrDefault(),
        };
        GameUtility.TryAddWindow(new Dialog_CorporateTerminal(context));
    }

    // 玩家派系判断（无玩家派系或派系为空时为 false），与 WildMugirlUtility.IsPlayerFaction 相同。
    internal static bool IsPlayerFaction(Faction? faction) => faction is not null && PlayerFactionUtility.IsPlayerFaction(faction);
}
