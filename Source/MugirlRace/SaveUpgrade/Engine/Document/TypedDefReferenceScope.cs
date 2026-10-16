using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>证明原版 ThingFilter 的两个 Def 集合来自已知 writer；字段同名不构成类型证据。</summary>
internal static class TypedDefReferenceScope
{
    // Writer evidence: Assembly-CSharp 1.7.4979, MVID 425a5939-54d7-432a-8f44-e3076b25fce9.
    // ThingFilter.ExposeData 06003A0A, StorageSettings.ExposeData 0600AB68, Bill.ExposeData 060049E5.
    // Policies 0600897C/06008990/060089D1; Game roots 06000DD2; database lists 060067DE/060067D5/060067E6.
    internal static bool IsThingFilterList(XmlElement list, UpgradeContext context)
    {
        if (list.Name is not ("allowedDefs" or "disallowedSpecialFilters") || list.ParentNode is not XmlElement filter)
        {
            return false;
        }
        if (filter.HasAttribute("Class"))
        {
            return IsEngineInstance<ThingFilter>(filter, context);
        }
        if (filter.ParentNode is not XmlElement owner)
        {
            return false;
        }

        if (filter.Name == "ingredientFilter")
        {
            return IsEngineInstance<Bill>(owner, context);
        }
        if (filter.Name == "filter")
        {
            if (IsEngineInstance<FoodPolicy>(owner, context) || IsEngineInstance<ApparelPolicy>(owner, context)
                || IsPolicyPath<FoodPolicy, FoodRestrictionDatabase>(owner, context, "foodRestrictionDatabase", "foodRestrictions")
                || IsPolicyPath<ApparelPolicy, OutfitDatabase>(owner, context, "outfitDatabase", "outfits"))
            {
                return true;
            }
            return IsStorageSettings(owner, context);
        }
        if (filter.Name is "defFilter" or "effectFilter")
        {
            return IsEngineInstance<ReadingPolicy>(owner, context)
                || IsPolicyPath<ReadingPolicy, ReadingPolicyDatabase>(owner, context, "readingPolicyDatabase", "readingPolicies");
        }

        // ThingComp writes into its parent's shared element. Require the actual def-to-comp relation.
        return filter.Name switch
        {
            "animalFilter" => HasComp<CompAnimalPenMarker>(owner, context), // 060099A4
            "autoCutFilter" => HasComp<CompAutoCut>(owner, context), // 060098E6
            _ => false,
        };
    }

    private static bool IsStorageSettings(XmlElement settings, UpgradeContext context)
    {
        if (settings.HasAttribute("Class"))
        {
            return IsEngineInstance<StorageSettings>(settings, context);
        }
        if (settings.ParentNode is not XmlElement owner)
        {
            return false;
        }
        return settings.Name switch
        {
            "settings" => IsEngineInstance<Building_Storage>(owner, context) // 0600AAE8
                || IsEngineInstance<Blueprint_Storage>(owner, context) // 0600A902
                || IsEngineInstance<Building_Bookcase>(owner, context) // 0600AA80
                || IsEngineInstance<Building_OutfitStand>(owner, context) // 0600AABF
                || IsEngineInstance<Zone_Stockpile>(owner, context) // 06007D76
                || IsEngineInstance<StorageGroup>(owner, context) || IsStorageGroupPath(owner, context), // 0600AB54/0600182D
            "storageSettings" => IsEngineInstance<Building_CorpseCasket>(owner, context) // 0600AC32
                || IsEngineInstance<Frame>(owner, context), // 0600A92B
            "allowedNutritionSettings" => IsEngineInstance<Building_GrowthVat>(owner, context) // 0600AD91
                || HasComp<CompBiosculpterPod>(owner, context), // 06009ABB
            "allowedShellsSettings" => HasComp<CompChangeableProjectile>(owner, context), // 06009BA3
            _ => false,
        };
    }

    private static bool IsPolicyPath<TPolicy, TDatabase>(XmlElement policy, UpgradeContext context, string databaseName, string listName)
    {
        return policy.Name == "li" && UnspecifiedOrEngineInstance<TPolicy>(policy, context)
            && policy.ParentNode is XmlElement list && list.Name == listName && !list.HasAttribute("Class")
            && list.ParentNode is XmlElement database && database.Name == databaseName
            && UnspecifiedOrEngineInstance<TDatabase>(database, context)
            && ReferenceEquals(database.ParentNode, context.Document.Game);
    }

    private static bool IsStorageGroupPath(XmlElement group, UpgradeContext context)
    {
        return group.Name == "li" && UnspecifiedOrEngineInstance<StorageGroup>(group, context)
            && group.ParentNode is XmlElement { Name: "groups" } groups && !groups.HasAttribute("Class")
            && groups.ParentNode is XmlElement { Name: "storageGroups" } manager
            && UnspecifiedOrEngineInstance<StorageGroupManager>(manager, context)
            && manager.ParentNode is XmlElement { Name: "li" } map && UnspecifiedOrEngineInstance<Map>(map, context)
            && map.ParentNode is XmlElement { Name: "maps" } maps && !maps.HasAttribute("Class")
            && ReferenceEquals(maps.ParentNode, context.Document.Game);
    }

    private static bool HasComp<TComp>(XmlElement owner, UpgradeContext context) where TComp : ThingComp
        => context.Index.TryGetThing(owner, out IndexedThing thing) && context.DefHasComp("ThingDef", thing.Def, SaveTypeNames.Of(typeof(TComp)));

    private static bool UnspecifiedOrEngineInstance<T>(XmlElement owner, UpgradeContext context)
        => !owner.HasAttribute("Class") || IsEngineInstance<T>(owner, context);

    private static bool IsEngineInstance<T>(XmlElement owner, UpgradeContext context)
    {
        string? name = context.MappedClassOf(owner);
        if (name is null || name.IndexOfAny(['[', ']', ',', '+', '&', '*']) >= 0)
        {
            return false;
        }
        // Only trusted engine types qualify here. An unknown third-party subtype may replace ExposeData,
        // so it needs its own registered ValueContext rather than inference from a similar field name.
        var assembly = typeof(ThingFilter).Assembly;
        Type? type = assembly.GetType(name, throwOnError: false, ignoreCase: true)
            ?? assembly.GetType("RimWorld." + name, throwOnError: false, ignoreCase: true)
            ?? assembly.GetType("Verse." + name, throwOnError: false, ignoreCase: true);
        return type is not null && typeof(T).IsAssignableFrom(type);
    }
}
