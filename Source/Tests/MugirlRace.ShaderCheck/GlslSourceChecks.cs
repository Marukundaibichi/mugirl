using System.Text;
using System.Text.RegularExpressions;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 本模组 GLSL 的文件级规则。引擎按文件名在所有模组、Core 与引擎的 Shaders 目录间解析源文件与 include，所以与原版同名的模组文件会
/// 静默替换全游戏的那个文件，反之亦然。
/// </summary>
internal static partial class GlslSourceChecks
{
    /// <summary>本模组所有着色器文件、ShaderDef 与 MaterialDef 的前缀（同 Mugirl.Platform.Rendering.ShaderSelfTest.ShaderNamePrefix）。</summary>
    public const string Prefix = "Mugirl_";

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly string[] InstancingTokens = ["INSTANCED", "gl_InstanceIndex"];

    /// <summary>本模组全部加载目录下存在的 Shaders 目录。</summary>
    public static IReadOnlyList<string> OwnShaderRoots(CheckPaths paths)
        => [.. paths.LoadFolders().Select(folder => Path.Combine(folder.Path, "Shaders")).Where(Directory.Exists)];

    /// <summary>运行文件检查。<paramref name="ownShaders"/> 提供 ShaderDef 引用的阶段文件。</summary>
    public static void Run(CheckPaths paths, IReadOnlyList<ShaderDefEntry> ownShaders, CheckReport report)
    {
        IReadOnlyList<string> roots = OwnShaderRoots(paths);
        CheckReport.Section("GLSL sources: " + string.Join(", ", roots));
        if (roots.Count == 0)
        {
            report.Fail("The mod has no Shaders folder in its 1.7 load folders.");
            return;
        }

        HashSet<string> vanillaLeaves = VanillaLeafNames(paths.GameDir);
        Dictionary<string, string> ownFiles = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> includesByFile = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).OrderBy(f => f, StringComparer.Ordinal))
        {
            string leaf = Path.GetFileName(file);
            if (!leaf.EndsWith(".glsl", StringComparison.Ordinal))
            {
                report.Fail($"{file}: unexpected file in the Shaders folder (only .glsl sources belong there).");
                continue;
            }

            if (!ownFiles.TryAdd(leaf, file))
            {
                report.Fail($"{file}: leaf name also used by {ownFiles[leaf]}; sources resolve by leaf name, one would shadow the other.");
                continue;
            }

            if (!leaf.StartsWith(Prefix, StringComparison.Ordinal))
            {
                report.Fail($"{file}: file name must start with {Prefix} (sources resolve by leaf name across all mods).");
            }

            if (vanillaLeaves.Contains(leaf))
            {
                report.Fail($"{file}: leaf name collides with a vanilla shader source and would replace it game-wide.");
            }

            includesByFile[leaf] = CheckContent(file, leaf, report);
        }

        // include 只能指向本模组的文件（原版 include 可能被任何其他模组替换）。
        foreach ((string leaf, List<string> includes) in includesByFile)
        {
            foreach (string include in includes.Where(include => !ownFiles.ContainsKey(Path.GetFileName(include))))
            {
                report.Fail($"{ownFiles[leaf]}: #include \"{include}\" does not name a file of this mod.");
            }
        }

        // 每个文件都必须能从某个 ShaderDef 的阶段文件或其 include 到达。
        HashSet<string> reachable = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> pending = new();
        foreach (ShaderDefEntry shader in ownShaders)
        {
            if (shader.Program is { } program)
            {
                pending.Enqueue(Path.GetFileName(program.vertexShaderFileName));
                pending.Enqueue(Path.GetFileName(program.fragmentShaderFileName));
            }
        }

        while (pending.TryDequeue(out string? leaf))
        {
            if (ownFiles.ContainsKey(leaf) && reachable.Add(leaf))
            {
                foreach (string include in includesByFile.GetValueOrDefault(leaf, []))
                {
                    pending.Enqueue(Path.GetFileName(include));
                }
            }
        }

        foreach ((string leaf, string file) in ownFiles.Where(pair => !reachable.Contains(pair.Key)))
        {
            report.Fail($"{file}: not used by any ShaderDef (directly or through #include).");
        }

        CheckReport.Pass($"{ownFiles.Count} files checked against {vanillaLeaves.Count} vanilla shader file names.");
    }

    private static List<string> CheckContent(string file, string leaf, CheckReport report)
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            report.Fail($"{file}: has a UTF-8 BOM; the engine injects its #define block after the first line only when the source starts with #version.");
        }

        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        bool isStage = leaf.EndsWith(".vert.glsl", StringComparison.Ordinal) || leaf.EndsWith(".frag.glsl", StringComparison.Ordinal);
        if (isStage)
        {
            string firstLine = text.Split('\n')[0].TrimEnd('\r');
            if (firstLine != "#version 450")
            {
                report.Fail($"{file}: a stage source must start with the line '#version 450' (found '{firstLine}').");
            }
        }
        else if (VersionDirective().IsMatch(text))
        {
            report.Fail($"{file}: an include file must not contain #version (it is pasted into the including stage).");
        }

        // Waylith 的 ShaderVariantEnumerator.VertexSupportsInstancing：顶点源（展开 include 后）只要含这些记号（注释中也算）就被当作
        // 支持实例化，多编译一个变体。Mugirl 的着色器都不支持实例化。
        foreach (string token in InstancingTokens.Where(token => text.Contains(token, StringComparison.Ordinal)))
        {
            report.Fail($"{file}: contains '{token}'; the engine would treat a vertex stage including it as instancing-capable.");
        }

        string code = LineComment().Replace(BlockComment().Replace(text, string.Empty), string.Empty);
        if (code.Contains('[', StringComparison.Ordinal))
        {
            report.Fail($"{file}: uses an array; the Mugirl shaders use none (the material parser rejects arrays in the material block).");
        }

        foreach (Match block in MaterialBlock().Matches(BlockComment().Replace(text, string.Empty)))
        {
            CheckMaterialBlock(file, block.Groups["body"].Value, report);
        }

        return [.. IncludeDirective().Matches(text).Select(match => match.Groups[1].Value)];
    }

    // 材质块的解析器保留行注释并在其中查找声明，且成员后的注解可以跨行匹配到下一行的注释（ShaderMetadataParser.UniformMemberRegex）。
    // 所以块内只允许“成员 + 同行注解”，不允许独立注释行或注释里出现分号。
    private static void CheckMaterialBlock(string file, string body, CheckReport report)
    {
        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                report.Fail($"{file}: comment-only line inside the material block ('{line}'); the parser would attach it to the previous member or read it as a slot.");
                continue;
            }

            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0 && line[(comment + 2)..].Contains(';', StringComparison.Ordinal))
            {
                report.Fail($"{file}: a material-block comment contains ';' ('{line}'); the parser would read a declaration inside it.");
            }

            string declaration = comment >= 0 ? line[..comment] : line;
            if (MemberName().Match(declaration) is { Success: true } member && member.Groups[1].Value.EndsWith("_ST", StringComparison.Ordinal))
            {
                report.Fail($"{file}: material member {member.Groups[1].Value} would become an engine TextureST slot (reordered and V-flipped); Mugirl shaders pass their own scale/offset.");
            }
        }
    }

    private static HashSet<string> VanillaLeafNames(string gameDir)
    {
        HashSet<string> leaves = new(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> roots = Directory.EnumerateDirectories(Path.Combine(gameDir, "Data"))
            .Select(package => Path.Combine(package, "Shaders"))
            .Append(Path.Combine(gameDir, "Rendering", "Shaders"))
            .Where(Directory.Exists);
        foreach (string root in roots)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                leaves.Add(Path.GetFileName(file));
            }
        }

        return leaves;
    }

    [GeneratedRegex(@"^\s*#\s*include\s+""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex IncludeDirective();

    [GeneratedRegex(@"^\s*#\s*version\b", RegexOptions.Multiline)]
    private static partial Regex VersionDirective();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"layout\s*\(\s*set\s*=\s*2\s*,\s*binding\s*=\s*0\s*\)\s*uniform\s+\w+\s*\{(?<body>.*?)\}", RegexOptions.Singleline)]
    private static partial Regex MaterialBlock();

    [GeneratedRegex(@"\b(?:float|int|vec2|vec3|vec4|mat3|mat4)\s+([A-Za-z_]\w*)\s*;")]
    private static partial Regex MemberName();
}
