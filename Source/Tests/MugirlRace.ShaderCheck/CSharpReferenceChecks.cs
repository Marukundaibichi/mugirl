using System.Text.RegularExpressions;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// C# 与 Def 的交叉检查：
/// <list type="bullet">
/// <item><c>Mugirl.Rendering.RenderingDefOf</c> 的每个字段都必须是本模组的 Def（否则 DefOf 绑定在启动时失败）。</item>
/// <item>参数为字符串字面量（或同文件 <c>const string</c>）的 <c>ShaderIds.For(shaderDef, "_Property")</c> 必须是该着色器声明的属性
/// （引擎静默忽略未知属性）。其余调用只计数，交给 <c>ShaderIds.For</c> 自己的开发者模式检查。</item>
/// <item>本模组着色器的每个材质参数与纹理槽都必须被这样的调用引用：没被 C# 设置的参数永远停在默认值，通常说明 C# 与 GLSL 不同步。</item>
/// </list>
/// </summary>
internal static partial class CSharpReferenceChecks
{
    /// <summary>运行检查。</summary>
    public static void Run(
        CheckPaths paths,
        IReadOnlyDictionary<string, Shader> shaders,
        IReadOnlySet<string> ownShaderDefs,
        IReadOnlySet<string> ownMaterialDefs,
        CheckReport report)
    {
        CheckReport.Section("C# references: " + paths.SourceDir);
        CheckDefOf(paths.RenderDefOfFile, ownShaderDefs, ownMaterialDefs, report);

        Dictionary<string, HashSet<string>> referenced = new(StringComparer.Ordinal);
        int checkedCalls = 0;
        int otherCalls = 0;
        foreach (string file in Directory.EnumerateFiles(paths.SourceDir, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !IsBuildOutput(paths.SourceDir, file)))
        {
            string text = File.ReadAllText(file);
            Dictionary<string, string> constants = ConstString().Matches(text)
                .GroupBy(match => match.Groups[1].Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Groups[2].Value, StringComparer.Ordinal);
            foreach (Match match in ShaderIdsFor().Matches(text))
            {
                string? shaderDef = Resolve(match.Groups[1].Value, constants);
                string? property = Resolve(match.Groups[2].Value, constants);
                if (shaderDef is null || property is null)
                {
                    otherCalls++;
                    continue;
                }

                checkedCalls++;
                if (!ownShaderDefs.Contains(shaderDef) || !shaders.TryGetValue(shaderDef, out Shader? shader))
                {
                    report.Fail($"{file}: ShaderIds.For(\"{shaderDef}\", \"{property}\"): no ShaderDef {shaderDef} in the mod.");
                }
                else if (!Declares(shader, property))
                {
                    report.Fail($"{file}: ShaderIds.For(\"{shaderDef}\", \"{property}\"): {shaderDef} does not declare {property}.");
                }
                else
                {
                    if (!referenced.TryGetValue(shaderDef, out HashSet<string>? names))
                    {
                        referenced[shaderDef] = names = new HashSet<string>(StringComparer.Ordinal);
                    }

                    names.Add(property);
                }
            }
        }

        CheckReport.Pass($"ShaderIds.For: {checkedCalls} constant references checked ({otherCalls} with computed arguments left to the developer-mode check).");

        foreach (string shaderDef in ownShaderDefs.Order(StringComparer.Ordinal))
        {
            if (!shaders.TryGetValue(shaderDef, out Shader? shader))
            {
                continue;
            }

            HashSet<string> names = referenced.GetValueOrDefault(shaderDef) ?? [];
            List<string> unset = [.. shader.MaterialLayout.UniformSlots.Select(slot => slot.PropertyName)
                .Concat(shader.MaterialLayout.TextureSlots.Select(slot => slot.PropertyName))
                .Where(name => !names.Contains(name))];
            if (unset.Count > 0)
            {
                report.Fail($"{shaderDef}: never set from C# through ShaderIds.For: {string.Join(", ", unset)} (they would always keep their defaults).");
            }
            else
            {
                CheckReport.Pass($"{shaderDef}: every material parameter and texture is set from C# ({names.Count} properties).");
            }
        }
    }

    private static void CheckDefOf(string file, IReadOnlySet<string> ownShaderDefs, IReadOnlySet<string> ownMaterialDefs, CheckReport report)
    {
        if (!File.Exists(file))
        {
            report.Fail($"{file} not found.");
            return;
        }

        int fields = 0;
        foreach (Match match in DefOfField().Matches(File.ReadAllText(file)))
        {
            fields++;
            string type = match.Groups[1].Value;
            string name = match.Groups[2].Value;
            IReadOnlySet<string> defs = type == "ShaderDef" ? ownShaderDefs : ownMaterialDefs;
            if (!defs.Contains(name))
            {
                report.Fail($"{file}: RenderingDefOf.{name} has no {type} of that name in the mod.");
            }
        }

        if (fields == 0)
        {
            report.Fail($"{file}: no ShaderDef or MaterialDef fields found.");
        }
        else
        {
            CheckReport.Pass($"RenderingDefOf: {fields} fields checked.");
        }
    }

    private static bool Declares(Shader shader, string property)
        => shader.MaterialLayout.UniformSlots.Any(slot => slot.PropertyName == property)
           || shader.MaterialLayout.TextureSlots.Any(slot => slot.PropertyName == property);

    // 字符串字面量参数的值、同文件 const string 的值，表达式时为 null。
    private static string? Resolve(string argument, Dictionary<string, string> constants)
    {
        argument = argument.Trim();
        if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"' && argument.IndexOf('"', 1) == argument.Length - 1)
        {
            return argument[1..^1];
        }

        return constants.GetValueOrDefault(argument);
    }

    private static bool IsBuildOutput(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first is "obj" or "bin";
    }

    [GeneratedRegex(@"public\s+static\s+(ShaderDef|MaterialDef)\??\s+(\w+)")]
    private static partial Regex DefOfField();

    // ShaderIds.For(<参数>, <参数>)，参数为字符串字面量或标识符。
    [GeneratedRegex(@"ShaderIds\.For\(\s*(""[^""]*""|[A-Za-z_][\w.]*)\s*,\s*(""[^""]*""|[A-Za-z_][\w.]*)\s*\)")]
    private static partial Regex ShaderIdsFor();

    [GeneratedRegex(@"const\s+string\s+(\w+)\s*=\s*""([^""]*)""\s*;")]
    private static partial Regex ConstString();
}
