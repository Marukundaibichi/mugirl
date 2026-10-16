using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mugirl.GameValidation.Core;

/// <summary>套件写给脚本的机器可读结果（UTF-8 无 BOM、缩进、键按插入顺序），以及读回这些文件。</summary>
internal static class ArtifactJson
{
    // StaticCacheLifecycle: 进程级不可变的序列化选项。
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>把 <paramref name="node"/> 写到 <paramref name="path"/>；先写临时文件再替换，脚本读不到半个文件。</summary>
    public static void Write(string path, JsonNode node)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, node.ToJsonString(Options) + "\n", Utf8);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>读 JSON 文件；文件不存在或无法解析时返回 null，并在 <paramref name="problem"/> 中说明。</summary>
    public static JsonNode? TryRead(string path, out string problem)
    {
        try
        {
            if (!File.Exists(path))
            {
                problem = "missing: " + path;
                return null;
            }

            problem = string.Empty;
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            problem = $"cannot read {path}: {ex.Message}";
            return null;
        }
    }

    /// <summary>键值表写成 JSON 对象（顺序与输入相同）。</summary>
    public static JsonObject Object(IEnumerable<KeyValuePair<string, string>> values)
    {
        var result = new JsonObject();
        foreach ((string key, string value) in values)
        {
            result[key] = value;
        }

        return result;
    }

    /// <summary>JSON 对象中的字符串键值（非字符串值取其 JSON 文本）。</summary>
    public static SortedDictionary<string, string> StringMap(JsonNode? node)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject map)
        {
            foreach ((string key, JsonNode? value) in map)
            {
                result[key] = value is JsonValue scalar && scalar.TryGetValue(out string? text) ? text : value?.ToJsonString() ?? "null";
            }
        }

        return result;
    }
}
