using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Incidents;

// 事件标签的解析、标记和临时状态共用一个入口，始终按序数比较。
internal static class EventPawnTags
{
    internal const string MigrationTag = "Mugirl_Migration";
    internal const string RunawayFarmTag = "Mugirl_RunawayFarm";
    internal const string FusionInvestorTag = "Mugirl_FusionInvestor";

    /// <summary>单次遍历标签列表；与 <c>List.Contains</c> 一样按序数比较字符串。</summary>
    internal static EventPawnFlags Parse(List<string>? questTags)
    {
        if (questTags is null)
        {
            return EventPawnFlags.None;
        }

        EventPawnFlags flags = EventPawnFlags.None;
        for (int i = 0; i < questTags.Count; i++)
        {
            flags |= FlagOf(questTags[i]);
        }

        return flags;
    }

    internal static EventPawnFlags FlagOf(string? tag)
    {
        if (string.Equals(tag, MigrationTag, StringComparison.Ordinal))
        {
            return EventPawnFlags.Migration;
        }

        if (string.Equals(tag, RunawayFarmTag, StringComparison.Ordinal))
        {
            return EventPawnFlags.RunawayFarm;
        }

        return string.Equals(tag, FusionInvestorTag, StringComparison.Ordinal)
            ? EventPawnFlags.FusionInvestor
            : EventPawnFlags.None;
    }

    internal static EventPawnFlags GetEventFlags(Pawn? pawn)
    {
        return Parse(pawn?.questTags);
    }

    internal static bool IsMigrationPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return HasTag(pawn, MigrationTag);
    }

    internal static bool IsRunawayFarmPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return HasTag(pawn, RunawayFarmTag);
    }

    internal static bool IsFusionInvestor([NotNullWhen(true)] Pawn? pawn)
    {
        return HasTag(pawn, FusionInvestorTag);
    }

    internal static bool IsPassiveEventPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn is not null && (GetEventFlags(pawn) & EventPawnFlags.Passive) != 0;
    }

    internal static void MarkMigrationPawn(Pawn? pawn)
    {
        AddTag(pawn, MigrationTag);
    }

    internal static void MarkRunawayFarmPawn(Pawn? pawn)
    {
        AddTag(pawn, RunawayFarmTag);
    }

    internal static void MarkFusionInvestor(Pawn? pawn)
    {
        AddTag(pawn, FusionInvestorTag);
    }

    internal static void ClearFusionInvestor(Pawn? pawn)
    {
        RemoveTag(pawn, FusionInvestorTag);
    }

    internal static bool ClearMigrationPawn(Pawn? pawn)
    {
        bool hadMigrationTag = IsMigrationPawn(pawn);
        RemoveTag(pawn, MigrationTag);
        return hadMigrationTag;
    }

    // 每次加载维护派系变化后的标记不变量；已加入玩家的迁徙者不再带迁徙标记。
    internal static int ClearPlayerMigrationPawns()
    {
        int cleared = 0;
        foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
        {
            if (pawn != null && PlayerFactionUtility.IsPlayerFaction(pawn.Faction) && ClearMigrationPawn(pawn))
            {
                cleared++;
            }
        }

        return cleared;
    }

    internal static void ClearTemporaryEventTags(Pawn? pawn)
    {
        RemoveTag(pawn, MigrationTag);
        RemoveTag(pawn, RunawayFarmTag);
    }

    internal static void ClearRunawayPanic(Pawn? pawn)
    {
        if (pawn?.health?.hediffSet == null)
        {
            return;
        }

        Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(IncidentsDefOf.Mugirl_RunawayPanic);
        if (hediff != null)
        {
            pawn.health.RemoveHediff(hediff);
        }
    }

    internal static void PreparePassiveWildPawn(Pawn? pawn)
    {
        if (pawn?.mindState == null)
        {
            return;
        }

        pawn.mindState.canFleeIndividual = false;
        pawn.mindState.Active = true;
    }

    private static bool HasTag([NotNullWhen(true)] Pawn? pawn, string tag)
    {
        return pawn?.questTags != null && pawn.questTags.Contains(tag);
    }

    private static void AddTag(Pawn? pawn, string tag)
    {
        if (pawn == null)
        {
            return;
        }

        pawn.questTags ??= new List<string>();
        if (!pawn.questTags.Contains(tag))
        {
            pawn.questTags.Add(tag);
        }
    }

    private static void RemoveTag(Pawn? pawn, string tag)
    {
        pawn?.questTags?.Remove(tag);
    }
}
