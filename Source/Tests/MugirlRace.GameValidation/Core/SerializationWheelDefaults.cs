using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>仅保存实际属性字段和研究 Def 的原始输入；不调用 IsFinished/GetProgress 等会补写状态的方法。</summary>
internal sealed record SerializationWheelDefaults(string PropsType, int MaxSlots, int BaseUnlockedSlots, string ResearchDef, float BaseCost)
{
    public static SerializationWheelDefaults? Read(CompProperties props, Assembly production)
    {
        Type type = production.GetType("Mugirl.Features.WeaponWheel.CompProperties_WeaponWheel", throwOnError: true)!;
        if (!type.IsInstanceOfType(props)) { return null; }
        FieldInfo maximum = type.GetField("maxSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        FieldInfo unlocked = type.GetField("baseUnlockedSlots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        if (maximum.FieldType != typeof(int) || unlocked.FieldType != typeof(int)) { return null; }
        // 实际 Pawn 已经自然 Initialize/PostLoadInit，DefOf 由游戏启动及组件读档使用；这里只读同一静态字段。
        FieldInfo researchField = production.GetType("Mugirl.Features.WeaponWheel.WeaponWheelDefOf", throwOnError: true)!
            .GetField("Mugirl_Autoloading", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        if (researchField.GetValue(null) is not ResearchProjectDef research || research.GetType() != typeof(ResearchProjectDef)) { return null; }
        ResearchProjectDef[] definitions = [.. DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(d => d.defName == research.defName)];
        if (definitions.Length != 1 || !ReferenceEquals(definitions[0], research)) { return null; }
        return new SerializationWheelDefaults(props.GetType().FullName!, (int)maximum.GetValue(props)!, (int)unlocked.GetValue(props)!, research.defName, research.baseCost);
    }

    public JsonObject ToJson() => new()
    {
        ["propsType"] = PropsType, ["maxSlots"] = MaxSlots, ["baseUnlockedSlots"] = BaseUnlockedSlots,
        ["researchDef"] = ResearchDef, ["baseCost"] = BaseCost.ToString("R", CultureInfo.InvariantCulture), ["baseCostBits"] = BitConverter.SingleToInt32Bits(BaseCost),
    };

    public XElement[] MissingState(XElement game)
    {
        if (!float.IsFinite(BaseCost) || BaseCost <= 0f || string.IsNullOrEmpty(ResearchDef))
        {
            throw new InvalidOperationException("Wheel missing-state proof currently requires the actual positive baseCost research branch.");
        }
        XElement manager = One(game, "researchManager");
        if (manager.HasAttributes && (manager.Attributes().Count() != 1 || manager.Attribute("Class")?.Value != typeof(ResearchManager).FullName))
        {
            throw new InvalidOperationException("Unknown ResearchManager owner cannot authorize Wheel defaults.");
        }
        XElement progress = One(manager, "progress");
        XElement keys = One(progress, "keys");
        XElement values = One(progress, "values");
        RequireElementList(progress);
        RequireElementList(keys);
        RequireElementList(values);
        if (progress.HasAttributes || keys.HasAttributes || values.HasAttributes || progress.Elements().Count() != 2)
        {
            throw new InvalidOperationException("Research progress is not the verified Def/Single dictionary shape.");
        }
        XElement[] keyItems = [.. keys.Elements()];
        XElement[] valueItems = [.. values.Elements()];
        if (keyItems.Length != valueItems.Length) { throw new InvalidOperationException("Research progress dictionary has unpaired keys/values."); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        float amount = 0f;
        for (int i = 0; i < keyItems.Length; i++)
        {
            XElement key = keyItems[i];
            XElement value = valueItems[i];
            if (key.Name != "li" || value.Name != "li" || key.HasAttributes || value.HasAttributes || key.HasElements || value.HasElements
                || key.Value.Length == 0 || key.Value == "null" || key.Value != key.Value.Trim() || !seen.Add(key.Value)
                || !float.TryParse(value.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float number) || !float.IsFinite(number))
            {
                throw new InvalidOperationException("Research progress contains an ambiguous or non-scalar Def/Single pair.");
            }
            if (key.Value == ResearchDef) { amount = number; }
        }
        int count = Math.Max(1, MaxSlots);
        int unlocked = amount >= BaseCost ? count : Math.Clamp(BaseUnlockedSlots, 0, count);
        var slots = new XElement("mugirlWheelSlotRecords");
        for (int i = 0; i < count; i++)
        {
            slots.Add(new XElement("li", new XElement("weapon", "null"), i < unlocked ? new XElement("unlocked", "True") : null));
        }
        return [new XElement("mugirlWheelReserveWeapons", new XElement("innerList")), slots];
    }

    private static XElement One(XElement parent, string name)
    {
        XElement[] elements = [.. parent.Elements(name)];
        return elements.Length == 1 ? elements[0] : throw new InvalidOperationException("Missing or repeated research field: " + name);
    }

    private static void RequireElementList(XElement list)
    {
        foreach (XNode node in list.Nodes())
        {
            if (node is XElement || node is XText text && node is not XCData && text.Value.All(c => c is ' ' or '\t' or '\r' or '\n')) { continue; }
            throw new InvalidOperationException("Research dictionary contains a non-element child that the actual ChildNodes reader would encounter.");
        }
    }
}
