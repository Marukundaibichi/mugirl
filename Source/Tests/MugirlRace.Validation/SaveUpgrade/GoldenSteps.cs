using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using MugirlRace.Validation.Legacy;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// 步骤的黄金样例 <c>Source/Tests/MugirlRace.Tests/SaveUpgrade/Golden/&lt;StepId&gt;/{before,after}.xml</c>。“Consumed”不能自称：
/// 步骤声明消费的每个旧名必须出现在 before.xml 中，并且不出现在 after.xml 中。
/// </summary>
internal sealed class GoldenSteps
{
    private readonly string directory;
    private readonly Dictionary<string, (string Before, string After)?> cache = new(StringComparer.Ordinal);
    private string? testSources;
    private GoldenOwnerContext? owners;

    public GoldenSteps(string directory) => this.directory = directory;

    public string Directory => directory;

    internal void ConfigureOwners(LegacyCorpus corpus, UpgradeManifest manifest) => owners = new GoldenOwnerContext(corpus, manifest);

    /// <summary>
    /// 核对步骤 <paramref name="stepId"/> 对令牌 <paramref name="token"/>（<c>type:</c>、<c>def:</c>、<c>label:</c>、<c>enum:</c>、<c>value:</c>）
    /// 的消费；成功时返回 <see langword="null"/>，否则返回原因。
    /// </summary>
    public string? Verify(string stepId, string token)
    {
        if (Load(stepId) is not { } files)
        {
            return $"no golden fixture {Path.Combine("Golden", stepId)}/before.xml and after.xml";
        }

        Regex pattern = PatternOf(token);
        if (!Contains(files.Before, token, pattern))
        {
            return $"before.xml of {stepId} does not contain the consumed name ({token})";
        }

        if (Contains(files.After, token, pattern))
        {
            return $"after.xml of {stepId} still contains the consumed name ({token})";
        }

        return null;
    }

    private bool Contains(string xml, string token, Regex pattern)
    {
        if (token.StartsWith("label:", StringComparison.Ordinal))
        {
            int slash = token.LastIndexOf('/');
            if (slash <= "label:".Length || owners is null) { return false; }
            SaveOwnerSelector selector = SaveOwnerSelector.Parse(token["label:".Length..slash]);
            if (!selector.IsValid) { return false; }
            string label = token[(slash + 1)..];
            XDocument scopedDocument = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            return scopedDocument.Descendants(label).Any(element => element.Parent is { } owner && selector.Matches(owner, owners));
        }

        if (pattern.IsMatch(xml))
        {
            return true;
        }

        const string ManeuverToken = "def:ManeuverDef/";
        if (!token.StartsWith(ManeuverToken, StringComparison.Ordinal))
        {
            return false;
        }

        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        // 真正的动词身份是 <owner>_<tool.id>_<maneuver.defName>；下划线不能按一般单词边界匹配。
        // 只在已核实的保存位置接受该证明，不把玩家文字中碰巧出现的名字算作升级消费。
        return document.SelectNodes("//verbTracker/verbs/li/loadID")!.OfType<XmlElement>()
            .Any(id => id.InnerText.EndsWith("_" + token[ManeuverToken.Length..], StringComparison.Ordinal));
    }

    /// <summary>步骤 <paramref name="stepId"/> 有 before.xml 与 after.xml。</summary>
    public bool Has(string stepId) => Load(stepId) is not null;

    /// <summary>测试工程（黄金样例目录的上两级，Source/Tests/MugirlRace.Tests）中有源文件以字符串字面量引用 <paramref name="stepId"/>。</summary>
    public bool TestsMention(string stepId)
    {
        if (testSources is null)
        {
            string root = Path.GetFullPath(Path.Combine(directory, "..", ".."));
            testSources = System.IO.Directory.Exists(root)
                ? string.Join('\n', System.IO.Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal).Select(File.ReadAllText))
                : "";
        }

        return testSources.Contains("\"" + stepId + "\"", StringComparison.Ordinal);
    }

    private (string Before, string After)? Load(string stepId)
    {
        if (!cache.TryGetValue(stepId, out (string, string)? files))
        {
            string before = Path.Combine(directory, stepId, "before.xml");
            string after = Path.Combine(directory, stepId, "after.xml");
            files = stepId.Length > 0 && stepId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(before) && File.Exists(after)
                ? (File.ReadAllText(before), File.ReadAllText(after))
                : null;
            cache[stepId] = files;
        }

        return files;
    }

    // 令牌在 XML 中的形态：标签是元素名，其他是完整的名字（两侧不是标识符字符）。标签在选择器（可含 '/'）之后的最后一段；
    // 值与 defName 在第一个 '/'（上下文名、数据库根）之后，值本身可以含 '/'。
    private static Regex PatternOf(string token)
    {
        int colon = token.IndexOf(':', StringComparison.Ordinal);
        string kind = colon < 0 ? "" : token[..colon];
        string body = colon < 0 ? token : token[(colon + 1)..];
        string name = kind switch
        {
            "label" => body[(body.LastIndexOf('/') + 1)..],
            "def" or "value" => body[(body.IndexOf('/', StringComparison.Ordinal) + 1)..],
            "enum" => body[(body.LastIndexOf('.') + 1)..],
            _ => body,
        };
        string escaped = Regex.Escape(name);
        return kind == "label"
            ? new Regex("<" + escaped + @"[\s/>]", RegexOptions.CultureInvariant)
            : new Regex(@"(?<![\w.+])" + escaped + @"(?![\w])", RegexOptions.CultureInvariant);
    }
}
