using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>只回放已由实际原版调用和实际保存线程证明的两个初始化，不接受数值范围或同名字段推测。</summary>
internal static class VanillaReadEvidence
{
    public static int Apply(XDocument expected, string json)
    {
        JsonObject evidence = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("No vanilla read evidence.");
        if ((int?)evidence["format"] != 1 || (string?)evidence["gameAssemblyMvid"] != ReviewedEngine.Mvid.ToString()
            || evidence["events"] is not JsonArray events || evidence["errors"] is not JsonArray { Count: 0 })
        {
            throw new InvalidOperationException("Vanilla initialization observations are unverified or contain errors: " + evidence["errors"]);
        }
        HashSet<XElement> changed = [];
        foreach (JsonObject item in events.OfType<JsonObject>())
        {
            string kind = (string?)item["kind"] ?? string.Empty;
            string path = (string?)item["parentPath"] ?? string.Empty;
            XElement parent = SaveReadTrace.Select(expected, path) ?? throw new InvalidOperationException("Vanilla initialization owner is absent: " + path);
            if (parent.Parent is not { } pawn || !SaveXml.IsPawn(pawn) || SaveXml.Id(pawn) != (string?)item["pawnId"] || !changed.Add(parent))
            {
                throw new InvalidOperationException("Vanilla initialization does not identify a unique source Pawn tracker.");
            }
            bool psychic = kind == "PsyfocusInitialization";
            string tracker = psychic ? "psychicEntropy" : "apparel";
            string label = psychic ? "currentPsyfocus" : "lockedApparel";
            string type = psychic ? "RimWorld.Pawn_PsychicEntropyTracker" : "RimWorld.Pawn_ApparelTracker";
            if ((!psychic && kind != "LockedApparelEmptyReferences") || parent.Name.LocalName != tracker || (string?)item["ownerType"] != type)
            {
                throw new InvalidOperationException("Unknown vanilla initialization reader.");
            }
            XElement[] fields = [.. parent.Elements(label)];
            bool missing = item["oldMissing"]?.GetValue<bool>() ?? throw new InvalidOperationException("Missing source-state evidence.");
            string? oldXml = (string?)item["oldXml"];
            if (missing ? fields.Length != 0 || oldXml is not null : fields.Length != 1 || oldXml is null || !XNode.DeepEquals(fields[0], XElement.Parse(oldXml, LoadOptions.PreserveWhitespace)))
            {
                throw new InvalidOperationException("Vanilla initialization source does not equal its exact captured node.");
            }
            if (psychic)
            {
                if (!missing && (fields[0].HasAttributes || fields[0].HasElements
                    || !float.TryParse(fields[0].Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float old) || !(old < 0f)))
                {
                    throw new InvalidOperationException("Only missing or explicitly negative psyfocus can initialize.");
                }
                int bits = item["newFloatBits"]?.GetValue<int>() ?? throw new InvalidOperationException("Missing captured float bits.");
                float value = BitConverter.Int32BitsToSingle(bits);
                string text = (string?)item["newValue"] ?? throw new InvalidOperationException("Missing exact G9 write observation.");
                CultureInfo culture = CultureInfo.GetCultureInfo((string?)item["saveCulture"] ?? throw new InvalidOperationException("Missing save culture observation."));
                if (!float.IsFinite(value) || value < 0f || value.ToString("G9", culture) != text)
                {
                    throw new InvalidOperationException("Captured psyfocus bits do not reproduce the actual G9 write.");
                }
                parent.SetElementValue(label, text);
            }
            else
            {
                if (missing || fields[0].Attributes().Count() != 1 || !SaveXml.HasNullAttribute(fields[0]) || fields[0].HasElements || fields[0].Value.Length != 0
                    || item["newValue"] is not null || item["newFloatBits"] is not null)
                {
                    throw new InvalidOperationException("Only the observed exact null reference list can become empty.");
                }
                fields[0].Attribute("IsNull")!.Remove();
            }
        }
        if (changed.Count != events.Count) { throw new InvalidOperationException("Invalid vanilla event entry."); }
        return changed.Count;
    }
}
