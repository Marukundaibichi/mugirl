using System.Text.Json;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Infrastructure;

/// <summary><c>Baselines/</c> 中的 JSONC 文件（允许注释与尾逗号）。</summary>
internal static class JsonBaseline
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>读取并解析；文件缺失或格式错误时记失败并返回 <see langword="null"/>。</summary>
    public static JsonDocument? Load(CheckRecorder recorder, string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            recorder.Fail("Baselines/" + fileName, "Missing baseline file.");
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            recorder.Fail("Baselines/" + fileName, "Malformed JSON: " + exception.Message);
            return null;
        }
    }

    /// <summary>对象 <c>{ "key": "reason" }</c>；理由为空时记失败。</summary>
    public static Dictionary<string, string> ReasonMap(CheckRecorder recorder, string fileName, JsonElement element, string context)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty entry in element.EnumerateObject())
        {
            string reason = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()!.Trim() : "";
            if (reason.Length == 0)
            {
                recorder.Fail($"Baselines/{fileName}: {context}{entry.Name}", "Entry without a reason.");
            }

            result[entry.Name] = reason;
        }

        return result;
    }

    /// <summary>字符串数组；缺失时为空。</summary>
    public static List<string> Strings(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
            : [];

    /// <summary>字符串属性；缺失时为 <see langword="null"/>。</summary>
    public static string? String(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
