using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>
/// 存档夹具（DevData/Evidence/SaveFixtures，不在 git 中，存在时才检查）：归档与清单的 SHA-256 一致，清单中的事实可由归档重现
/// （同一个 <see cref="SaveFacts"/> 读取器、清单中的旧名查询），meta 已改写为 har.mugirlrace。
/// </summary>
public sealed class FixtureArchiveTests
{
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        if (FixtureRoot() is { } root)
        {
            foreach (string folder in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
            {
                string manifest = Path.Combine(folder, "manifest.json");
                if (File.Exists(manifest) && JsonNode.Parse(File.ReadAllText(manifest)) is JsonObject json && (string?)json["kind"] == "P" && json["save"] is not null)
                {
                    data.Add(Path.GetFileName(folder));
                }
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FactsAreReproducibleFromTheArchive(string id)
    {
        Assert.SkipWhen(id.Length == 0, "no P fixture in DevData/Evidence/SaveFixtures");
        string folder = Path.Combine(FixtureRoot()!, id);
        var manifest = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")))!;
        string archive = Path.Combine(folder, (string)manifest["save"]!["file"]!);
        Assert.Equal((string?)manifest["save"]!["sha256Gz"], Sha256(archive));

        XDocument save = SaveXml.Load(archive);
        Assert.Contains("har.mugirlrace", SaveXml.ModIds(save));
        Assert.DoesNotContain(SaveXml.ModIds(save), modId => modId.StartsWith("mugirl.legacy.", StringComparison.Ordinal));

        var restraints = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (manifest["restraintDefaults"] is JsonObject defaults)
        {
            foreach ((string def, JsonNode? values) in defaults)
            {
                restraints[def] = ArtifactJson.StringMap(values);
            }
        }

        SaveFactQueries queries = SaveFactQueries.Legacy(Strings(manifest["modDefs"]), Strings(manifest["modThingDefs"]), restraints);
        SortedDictionary<string, string> expected = ArtifactJson.StringMap(manifest["facts"]);
        SortedDictionary<string, string> actual = SaveFacts.Read(save, queries).Values;

        Assert.Equal(expected, actual);
    }

    private static List<string> Strings(JsonNode? node) => node is JsonArray array ? [.. array.Select(n => (string?)n).OfType<string>()] : [];

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // 从测试程序所在目录向上找 Mod 根目录（含 DevData/Evidence/SaveFixtures）。
    private static string? FixtureRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "DevData", "Evidence", "SaveFixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string? configured = Environment.GetEnvironmentVariable("MUGIRL_FIXTURE_ROOT");
        return configured is not null && Directory.Exists(configured) ? configured : null;
    }
}
