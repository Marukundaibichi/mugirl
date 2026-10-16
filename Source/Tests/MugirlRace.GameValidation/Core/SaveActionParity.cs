using System.Text.Json.Nodes;

namespace Mugirl.GameValidation.Core;

/// <summary>纯 DOM 与实际读取诊断的动作核对；只把精确字典配对轨迹认作读取阶段额外发生的动作。</summary>
internal static class SaveActionParity
{
    public const string DictionaryStep = "Framework.LoadIds.DictionaryReferences";

    public static List<string> Compare(JsonObject? runtime, JsonObject offline)
    {
        Dictionary<(string Id, string Phase), long> actual = Counts(runtime);
        Dictionary<(string Id, string Phase), long> expected = Counts(offline);
        var problems = new List<string>();
        foreach (((string id, string phase) key, long count) in expected)
        {
            if (key.id == DictionaryStep)
            {
                if (count != 0) { problems.Add("The pure DOM report unexpectedly includes runtime dictionary edits."); }
                continue;
            }
            long found = actual.GetValueOrDefault(key, -1);
            if (found != count) { problems.Add($"step {key.id} ({key.phase}): offline {count}, in game {found}"); }
        }
        foreach (((string id, string phase) key, long count) in actual.Where(p => p.Key.Phase == "dom" && p.Key.Id != DictionaryStep && !expected.ContainsKey(p.Key)))
        {
            problems.Add($"step {key.id} ({key.phase}): in game {count}, not in the offline report");
        }
        return problems;
    }

    public static void VerifyDictionaryActions(JsonObject? runtime, string traceJson)
    {
        JsonObject trace = JsonNode.Parse(traceJson) as JsonObject ?? throw new InvalidOperationException("No read trace object.");
        if ((int?)trace["format"] != 1 || trace["errors"] is not JsonArray { Count: 0 } || trace["events"] is not JsonArray events)
        {
            throw new InvalidOperationException("Read trace is incomplete or contains source-location errors.");
        }
        long traced = 0;
        foreach (JsonNode? item in events.Where(e => (string?)e?["kind"] == "DictionaryPairs"))
        {
            if (item?["indices"] is not JsonArray indices || item["oldKeys"] is not JsonArray keys || item["oldValues"] is not JsonArray values
                || indices.Count != keys.Count || indices.Count != values.Count)
            {
                throw new InvalidOperationException("Dictionary trace lacks complete pair evidence.");
            }
            traced += 2L * indices.Count;
        }
        Dictionary<(string Id, string Phase), long> counts = Counts(runtime);
        if (counts.Keys.Any(k => k.Id == DictionaryStep && k.Phase != "dom")
            || counts.GetValueOrDefault((DictionaryStep, "dom")) != traced)
        {
            throw new InvalidOperationException("Dictionary action diagnostics do not equal twice the exact traced pair removals: " + traced);
        }
    }

    private static Dictionary<(string Id, string Phase), long> Counts(JsonObject? report)
        => report?["steps"] is JsonArray rows
            ? rows.OfType<JsonObject>().GroupBy(r => ((string?)r["id"] ?? "?", (string?)r["phase"] ?? "?"))
                .ToDictionary(g => g.Key, g => g.Sum(r => (long?)r["actions"] ?? 0))
            : [];
}
