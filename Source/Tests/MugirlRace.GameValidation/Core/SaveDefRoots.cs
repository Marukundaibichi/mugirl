using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 存档中 Def 引用所在位置对应的 Def 数据库根类型（短名，例如 <c>ThingDef</c>）。同一个 defName 可以分属不同的根类型
/// （<c>Mugirl_Blindfold</c> 既是 HediffDef 又是 ThingDef），翻译旧名时必须先知道根类型。
/// </summary>
/// <remarks>只认原版 Scribe 写出的位置（标签名与外层列表名）；认不出时返回 null，由调用方按“各根类型的映射一致”处理。</remarks>
internal static class SaveDefRoots
{
    /// <summary>标签名本身决定根类型的 Def 引用。</summary>
    private static readonly Dictionary<string, string> ByLabel = new(StringComparer.Ordinal)
    {
        ["kindDef"] = "PawnKindDef",
        ["xenotype"] = "XenotypeDef",
        ["stuff"] = "ThingDef",
        ["plantDefToSow"] = "ThingDef",
        ["thingDefToCarry"] = "ThingDef",
        ["recipe"] = "RecipeDef",
        ["workGiverDef"] = "WorkGiverDef",
        ["jobGiverThinkTree"] = "ThinkTreeDef",
        ["interaction"] = "InteractionDef",
        ["bodyType"] = "BodyTypeDef",
        ["headType"] = "HeadTypeDef",
        ["hairDef"] = "HairDef",
        ["beardDef"] = "BeardDef",
        ["sourceHediffDef"] = "HediffDef",
    };

    /// <summary>列表项 <c>li/def</c> 的根类型，按列表名。</summary>
    private static readonly Dictionary<string, string> ByListName = new(StringComparer.Ordinal)
    {
        ["hediffs"] = "HediffDef",
        ["xenogenes"] = "GeneDef",
        ["endogenes"] = "GeneDef",
        ["abilities"] = "AbilityDef",
        ["allTraits"] = "TraitDef",
        ["memories"] = "ThoughtDef",
        ["allFactions"] = "FactionDef",
    };

    /// <summary>叶子 <paramref name="leaf"/> 中 defName 的根类型；不是可识别的 Def 引用位置时为 null。</summary>
    public static string? RootOf(XElement leaf)
    {
        string label = leaf.Name.LocalName;
        if (ByLabel.TryGetValue(label, out string? root))
        {
            return root;
        }

        XElement? owner = leaf.Parent;
        if (label == "root" && owner?.Name.LocalName == "li" && owner.Parent?.Name.LocalName == "quests")
        {
            return "QuestScriptDef";
        }

        if (label != "def" || owner is null)
        {
            return null;
        }

        // Thing 节点：def 与 id 并列（原版 Thing.ExposeData）。
        if (owner.Element("id") is not null)
        {
            return "ThingDef";
        }

        if (owner.Name.LocalName is "curJob" or "job")
        {
            return "JobDef";
        }

        return owner.Name.LocalName == "li" && owner.Parent is { } list && ByListName.TryGetValue(list.Name.LocalName, out string? listRoot) ? listRoot : null;
    }
}
