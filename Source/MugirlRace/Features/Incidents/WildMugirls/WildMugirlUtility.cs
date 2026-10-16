using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Incidents;

// 野生雪牛娘（逃亡野生奴隶、野生雪牛娘）的种类判断与加入玩家后的清理。
// 玩家派系判断（PlayerFaction/IsPlayerFaction/IsHostileToPlayer/TrySetPlayerFaction）被许多模块共用，签名保持不变。
internal static class WildMugirlUtility
{

    public static bool IsEscapeWildSlave([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null && pawn.kindDef == IncidentsDefOf.Mugirl_EscapeWildSlave;
    }

    public static bool IsWildMugirl([NotNullWhen(true)] Pawn? pawn)
    {
        return IsEscapeWildSlave(pawn) || (pawn != null && pawn.kindDef == IncidentsDefOf.Mugirl_WildMugirl);
    }

    public static bool HasPlayerConvertibleEventKind([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null
            && (pawn.kindDef == IncidentsDefOf.Mugirl_EscapeWildSlave
                || pawn.kindDef == IncidentsDefOf.Mugirl_WildMugirl
                || pawn.kindDef == IncidentsDefOf.Mugirl_PreEscapeWildSlave);
    }

    // 热路径（野人判断、ThinkTree 条件）：只做 kindDef 引用比较与一次派系比较。
    public static bool IsNonPlayerWildMugirl([NotNullWhen(true)] Pawn? pawn)
    {
        return IsWildMugirl(pawn) && !PlayerFactionUtility.IsPlayerFaction(pawn.Faction);
    }

    public static bool IsMugirlPawn([NotNullWhen(true)] Pawn? pawn)
    {
        if (pawn == null)
        {
            return false;
        }

        return MugirlIdentity.IsMugirlPawn(pawn)
            || pawn.kindDef == IncidentsDefOf.Mugirl_EscapeWildSlave
            || pawn.kindDef == IncidentsDefOf.Mugirl_WildMugirl
            || pawn.kindDef == IncidentsDefOf.Mugirl_PreEscapeWildSlave;
    }

    // 加入玩家后清除事件标记、野人与动物式状态（驯服指定、主人、训练等）。重复调用无副作用。
    public static bool CleanupAfterJoiningPlayer(Pawn? pawn)
    {
        if (pawn == null || pawn.Destroyed || !PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
        {
            return false;
        }

        bool changed = false;
        if (NormalizePlayerKindToFactionDefault(pawn))
        {
            changed = true;
        }

        if (EventPawnTags.ClearMigrationPawn(pawn))
        {
            changed = true;
        }

        if (pawn.mindState != null)
        {
            if (pawn.mindState.WillJoinColonyIfRescued)
            {
                pawn.mindState.WillJoinColonyIfRescued = false;
                changed = true;
            }

            if (pawn.mindState.WildManEverReachedOutside)
            {
                pawn.mindState.WildManEverReachedOutside = false;
                changed = true;
            }
        }

        Map? map = pawn.MapHeld;
        if (map != null)
        {
            Designation? tameDesignation = map.designationManager.DesignationOn(pawn, DesignationDefOf.Tame);
            if (tameDesignation != null)
            {
                map.designationManager.RemoveDesignation(tameDesignation);
                changed = true;
            }
        }

        if (IsMugirlPawn(pawn) && pawn.RaceProps?.Humanlike == true && pawn.training != null)
        {
            if (pawn.playerSettings != null)
            {
                pawn.playerSettings.Master = null;
                pawn.playerSettings.followDrafted = false;
                pawn.playerSettings.followFieldwork = false;
                pawn.playerSettings.animalsReleased = false;
                pawn.playerSettings.animalForage = false;
                pawn.playerSettings.animalDig = false;
            }

            pawn.training = null;
            changed = true;
        }

        if (changed)
        {
            RefreshPawn(pawn);
        }

        return changed;
    }

    // 每次加载重新核对事件 Pawn 的派系种类，覆盖其他功能在运行中改变派系的情况。
    public static int NormalizeLoadedPlayerPawnKinds()
    {
        int changed = 0;
        foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
        {
            if (NormalizePlayerKindToFactionDefault(pawn))
            {
                RefreshPawn(pawn);
                changed++;
            }
        }

        return changed;
    }

    private static bool NormalizePlayerKindToFactionDefault(Pawn? pawn)
    {
        if (!HasPlayerConvertibleEventKind(pawn)
            || !PlayerFactionUtility.IsPlayerFaction(pawn.Faction)
            || pawn.RaceProps?.Humanlike != true
            || pawn.IsQuestLodger())
        {
            return false;
        }

        PawnKindDef? targetKind = pawn.Faction.def?.basicMemberKind;
        if (targetKind == null || pawn.kindDef == targetKind)
        {
            return false;
        }

        pawn.ChangeKind(targetKind);
        return true;
    }

    public static void RefreshPawn(Pawn? pawn)
    {
        if (pawn == null || pawn.Destroyed)
        {
            return;
        }

        PortraitsCache.SetDirty(pawn);
        pawn.Drawer?.renderer?.SetAllGraphicsDirty();

        GameUtility.TryMarkColonistsDirty();
    }
}
