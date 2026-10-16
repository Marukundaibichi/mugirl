using System.Text;
using System.Text.RegularExpressions;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// Mod 自己的 GLSL 文件规则（C.shaders 的一部分）。引擎按文件名在所有 Mod、Core 与引擎的 Shaders 目录中解析源文件与 include，
/// 所以 Mod 文件与别的文件同名时会静默替换对方，反之亦然：
/// <list type="bullet">
/// <item>Mod 的 Shaders 目录只放 <c>.glsl</c>，每个文件名只用一次、带前缀、不与游戏着色器同名；</item>
/// <item>没有 UTF-8 BOM（只有源文件以 <c>#version</c> 开头时引擎才会把 <c>#define</c> 块插在第一行之后），阶段源文件以
/// <c>#version 450</c> 开头，include 文件不含 <c>#version</c>；</item>
/// <item>任何位置都没有 <c>INSTANCED</c>/<c>gl_InstanceIndex</c>（含有它们的顶点阶段会被当成支持实例化，Mod 着色器都不支持）；</item>
/// <item>每个 include 都解析到 Mod 自己的文件（原版 include 可能被任何其他 Mod 替换），每个文件都直接或经 include 被 Mod 的 ShaderDef 使用。</item>
/// </list>
/// 移植自 hk 的 GlslSourceRules。
/// </summary>
internal static partial class GlslSourceRules
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly string[] InstancingTokens = ["INSTANCED", "gl_InstanceIndex"];

    /// <summary>运行规则。<paramref name="stageNames"/> 是 Mod 的 ShaderDef 用作阶段的文件名。</summary>
    public static void Check(CheckRecorder recorder, ShaderFiles files, string modRoot, string prefix, IReadOnlySet<string> stageNames)
    {
        foreach (string root in files.OurRoots)
        {
            foreach (string other in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(file => !file.EndsWith(".glsl", StringComparison.Ordinal))
                         .Order(StringComparer.Ordinal))
            {
                recorder.Fail(XmlPaths.File(modRoot, other), "Unexpected file in a Shaders folder: only .glsl sources belong there.");
            }
        }

        var byLeaf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var includes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.Ours)
        {
            string leaf = Path.GetFileName(file);
            string subject = XmlPaths.File(modRoot, file);
            if (!byLeaf.TryAdd(leaf, file))
            {
                recorder.Fail(subject, $"Same leaf name as {XmlPaths.File(modRoot, byLeaf[leaf])}: sources resolve by leaf name, one shadows the other.");
                continue;
            }

            if (!leaf.StartsWith(prefix, StringComparison.Ordinal))
            {
                recorder.Fail(subject, $"GLSL sources resolve by file name across every mod: prefix the file name with {prefix}.");
            }

            if (files.GameFileNamed(leaf) is { } shadowed)
            {
                recorder.Fail(subject, $"Same file name as a game shader ({shadowed}): it would replace the game's program for every ShaderDef that uses it.");
            }

            includes[leaf] = CheckContent(recorder, file, leaf, subject);
        }

        foreach ((string leaf, List<string> targets) in includes)
        {
            string file = byLeaf[leaf];
            foreach (string target in targets)
            {
                string local = Path.Combine(Path.GetDirectoryName(file)!, target);
                string? resolved = File.Exists(local) ? local : files.Resolve(Path.GetFileName(target));
                if (resolved is null)
                {
                    recorder.Fail($"{XmlPaths.File(modRoot, file)} #include \"{target}\"", "Included file not found (relative to the includer or by name).");
                }
                else if (!files.IsOurs(resolved))
                {
                    recorder.Fail($"{XmlPaths.File(modRoot, file)} #include \"{target}\"", $"Resolves outside the mod ({resolved}); a vanilla include could be replaced by any other mod.");
                }
            }
        }

        // 每个文件都必须能从某个 ShaderDef 的阶段或其 include 到达。
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(stageNames);
        while (pending.TryDequeue(out string? leaf))
        {
            if (byLeaf.ContainsKey(leaf) && reachable.Add(leaf))
            {
                foreach (string include in includes.GetValueOrDefault(leaf, []))
                {
                    pending.Enqueue(Path.GetFileName(include));
                }
            }
        }

        foreach ((string leaf, string file) in byLeaf.Where(pair => !reachable.Contains(pair.Key)))
        {
            recorder.Fail(XmlPaths.File(modRoot, file), "Not used by any ShaderDef of the mod (directly or through #include).");
        }
    }

    // 返回文件的 #include 目标。
    private static List<string> CheckContent(CheckRecorder recorder, string file, string leaf, string subject)
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            recorder.Fail(subject, "Has a UTF-8 BOM: the engine injects its #define block after the first line only when the source starts with #version.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            recorder.Fail(subject, "Not valid UTF-8.");
            return [];
        }

        bool isStage = leaf.EndsWith(".vert.glsl", StringComparison.Ordinal) || leaf.EndsWith(".frag.glsl", StringComparison.Ordinal);
        if (isStage)
        {
            string firstLine = text.TrimStart('﻿').Split('\n')[0].TrimEnd('\r');
            if (firstLine != "#version 450")
            {
                recorder.Fail(subject, $"A stage source must start with the line '#version 450' (found '{firstLine}').");
            }
        }
        else if (VersionDirective().IsMatch(text))
        {
            recorder.Fail(subject, "An include file must not contain #version (it is pasted into the including stage).");
        }

        foreach (string token in InstancingTokens.Where(token => text.Contains(token, StringComparison.Ordinal)))
        {
            recorder.Fail(subject, $"Contains '{token}': the engine would treat a vertex stage including it as instancing-capable.");
        }

        return [.. IncludeDirective().Matches(text).Select(match => match.Groups["file"].Value)];
    }

    [GeneratedRegex("^\\s*#\\s*include\\s+\"(?<file>[^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex IncludeDirective();

    [GeneratedRegex(@"^\s*#\s*version\b", RegexOptions.Multiline)]
    private static partial Regex VersionDirective();
}
