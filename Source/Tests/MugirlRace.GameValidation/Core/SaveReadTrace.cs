using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>按真实 Scribe 读取轨迹修改规范期望值；只接受准确节点、准确原值和完整字典配对证据。</summary>
internal static class SaveReadTrace
{
    private static readonly HashSet<string> ScalarKinds = new(StringComparer.Ordinal)
        { "Reference", "ReferenceList", "VerbIdentity", "ThinkKey", "DefReference" };

    public static int Apply(XDocument expected, string json)
    {
        JsonObject trace = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("The read trace is not an object.");
        if ((int?)trace["format"] != 1 || trace["events"] is not JsonArray events || trace["errors"] is not JsonArray errors)
        {
            throw new InvalidOperationException("Unsupported or incomplete read trace.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("The actual reader could not locate all changed values: " + errors.ToJsonString());
        }

        HashSet<(XElement Element, string Old, string New)> applied = [];
        HashSet<XElement> omittedThinkKeys = [];
        int edits = 0;
        foreach (JsonNode? item in events)
        {
            string kind = (string?)item?["kind"] ?? throw new InvalidOperationException("Read trace event without kind.");
            string path = (string?)item?["path"] ?? throw new InvalidOperationException("Read trace event without path.");
            XElement element = Select(expected, path) ?? throw new InvalidOperationException("Read trace path is absent from the canonical document: " + path);
            if (kind == "DictionaryPairs")
            {
                edits += RemovePairs(element, item!);
                continue;
            }

            if (!ScalarKinds.Contains(kind) || element.HasElements
                || item?["oldValue"] is not JsonValue oldNode || item["newValue"] is not JsonValue newNode)
            {
                throw new InvalidOperationException("Unknown or non-scalar read event at " + path);
            }

            string oldValue = oldNode.GetValue<string>();
            string newValue = newNode.GetValue<string>();
            var key = (element, oldValue, newValue);
            if (element.Value == newValue && applied.Contains(key))
            {
                continue;
            }

            if (element.Value != oldValue)
            {
                throw new InvalidOperationException("Read trace source differs at " + path + ": expected exact value " + oldValue);
            }

            element.Value = newValue;
            if (kind == "ThinkKey" && newValue == "-1")
            {
                if (element.Name.LocalName != "lastJobGiverKey" || element.Parent?.Name.LocalName is not ("curJob" or "job" or "li"))
                {
                    throw new InvalidOperationException("A ThinkKey event did not identify a Job key.");
                }
                omittedThinkKeys.Add(element);
            }
            applied.Add(key);
            edits++;
        }

        // Job.ExposeData 的实际 default 是 -1；只省略本次真实读取已准确定位并归一化的那个叶子。
        foreach (XElement key in omittedThinkKeys.Where(e => e.Value == "-1"))
        {
            key.Remove();
        }

        return edits;
    }

    /// <summary>只解析同名兄弟索引组成的绝对路径，不执行任意 XPath 表达式。</summary>
    public static XElement? Select(XDocument document, string path)
    {
        if (!path.StartsWith('/'))
        {
            throw new InvalidOperationException("The read trace path is not absolute.");
        }

        IEnumerable<XElement> candidates = document.Elements();
        XElement? selected = null;
        foreach (string segment in path[1..].Split('/'))
        {
            int bracket = segment.LastIndexOf('[');
            if (bracket <= 0 || !segment.EndsWith(']')
                || !int.TryParse(segment.AsSpan(bracket + 1, segment.Length - bracket - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1)
            {
                throw new InvalidOperationException("Invalid read trace path segment: " + segment);
            }

            string name = XmlConvert.VerifyNCName(segment[..bracket]);
            selected = candidates.Where(e => e.Name.NamespaceName.Length == 0 && e.Name.LocalName == name).Skip(index - 1).FirstOrDefault();
            if (selected is null)
            {
                return null;
            }

            candidates = selected.Elements();
        }

        return selected;
    }

    private static int RemovePairs(XElement dictionary, JsonNode item)
    {
        if (dictionary.Element("keys") is not { } keyList || dictionary.Element("values") is not { } valueList
            || item["indices"] is not JsonArray indices || item["oldKeys"] is not JsonArray oldKeys || item["oldValues"] is not JsonArray oldValues
            || indices.Count != oldKeys.Count || indices.Count != oldValues.Count)
        {
            throw new InvalidOperationException("Dictionary read event has incomplete pair evidence.");
        }

        XElement[] keys = [.. keyList.Elements()];
        XElement[] values = [.. valueList.Elements()];
        if (keys.Length != values.Length || keys.Any(e => e.Name.LocalName != "li" || e.HasElements)
            || values.Any(e => e.Name.LocalName != "li" || e.HasElements))
        {
            throw new InvalidOperationException("Dictionary pair evidence is restricted to parallel scalar/reference lists.");
        }

        HashSet<int> removed = [];
        for (int i = 0; i < indices.Count; i++)
        {
            int position = indices[i]!.GetValue<int>();
            if (position < 0 || position >= keys.Length || !removed.Add(position)
                || keys[position].Value != oldKeys[i]!.GetValue<string>() || values[position].Value != oldValues[i]!.GetValue<string>())
            {
                throw new InvalidOperationException("Dictionary removal evidence does not match its exact pre-removal pair.");
            }
        }

        foreach (int position in removed.OrderDescending())
        {
            keys[position].Remove();
            values[position].Remove();
        }

        return 2 * removed.Count;
    }
}
