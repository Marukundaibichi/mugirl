using System.Text.RegularExpressions;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 只能在源码文本上验证的约定（根 README“代码与内容约束”）：<c>B.static-caches</c>，每个静态缓存或可变静态状态的声明前有
/// <c>StaticCacheLifecycle:</c> 注释，写明生命周期与清理点。字段清单取自编译结果（不依赖写法），再按 PDB 找到声明它的源码文件与行。
/// </summary>
internal static class SourceConventionChecks
{
    private const string LifecycleMarker = "StaticCacheLifecycle:";

    // 只读字段中算作“缓存”的类型：可变集合、反射句柄与缓存的 Def 查找结果（与 1.6 静态检查的范围一致并推广到全部访问级别）。
    private static readonly string[] CacheTypePrefixes =
    [
        "System.Collections.Generic.", "System.Collections.Concurrent.", "System.Runtime.CompilerServices.ConditionalWeakTable`2",
        "System.Reflection.FieldInfo", "System.Reflection.MethodInfo", "System.Reflection.PropertyInfo", "System.Reflection.MethodBase",
        "System.Reflection.ConstructorInfo", "HarmonyLib.AccessTools/FieldRef`2", "HarmonyLib.AccessTools+FieldRef`2",
    ];

    private static readonly string[] ImmutableCollectionPrefixes =
    [
        "System.Collections.Generic.IReadOnly", "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.KeyValuePair`2",
        "System.Collections.Generic.EqualityComparer`1", "System.Collections.Generic.Comparer`1",
    ];

    public static void Run(Report report, ValidationOptions options, MetadataWorld world)
    {
        report.Run("B.static-caches", "Static caches and mutable static state carry a StaticCacheLifecycle comment", recorder => CheckStaticCaches(recorder, options, world));
    }

    private static void CheckStaticCaches(CheckRecorder recorder, ValidationOptions options, MetadataWorld world)
    {
        if (!Directory.Exists(options.SourceDir))
        {
            recorder.Skip($"source folder not found: {options.SourceDir} (pass --source)");
            return;
        }

        var files = new SourceFiles(options.SourceDir);
        int fields = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(world.Mod).Where(t => !CecilNames.IsCompilerGenerated(t) && !t.IsEnum && !IsDefOf(t)))
        {
            foreach (FieldDefinition field in type.Fields.Where(IsStaticState))
            {
                fields++;
                string declared = DeclaredName(field);
                string subject = CecilNames.Type(type) + "." + declared;
                // PDB 文档先换算到 --source 目录（构建可能在临时副本中进行），找不到时用 PDB 中的绝对路径。
                List<string> candidates = world.Sources.DocumentsOf(type)
                    .Select(document => SourceMap.ProjectRelative(document, options.SourceDir) is { } relative && File.Exists(Path.Combine(options.SourceDir, relative))
                        ? relative
                        : File.Exists(document) ? document : null)
                    .OfType<string>()
                    .ToList();
                // 分部类的字段可能写在没有方法的文件里（PDB 不记录），所以再找所有声明该类型的文件。
                candidates.AddRange(files.Declaring(type.Name).Where(file => !candidates.Contains(file, StringComparer.OrdinalIgnoreCase)));

                (string File, int Line)? declaration = null;
                foreach (string candidate in candidates)
                {
                    declaration ??= files.FindDeclaration(candidate, declared, field.Name.StartsWith('<'));
                }

                if (declaration is not { } found)
                {
                    recorder.Warn(subject, "Static field found in the assembly but its declaration was not found in the source folder; the lifecycle comment cannot be verified.");
                    continue;
                }

                if (!files.HasLifecycleComment(found.File, found.Line, type.Name))
                {
                    recorder.Fail(subject, $"Static {(field.IsInitOnly ? "cache" : "mutable state")} ({CecilNames.Type(field.FieldType)}) without a \"{LifecycleMarker}\" comment above "
                        + "the declaration: state when its lifetime (process, game, map) and where it is cleared (new game, load, FinalizeInit).", $"{found.File}:{found.Line}");
                }
            }
        }

        recorder.Summary = $"{fields} static cache/state field(s) checked";
    }

    // 需要生命周期说明的静态字段：可变的（非 readonly），或 readonly 但类型是缓存（集合、反射句柄、Def）。
    private static bool IsStaticState(FieldDefinition field)
    {
        if (!field.IsStatic || field.IsLiteral)
        {
            return false;
        }

        // 编译器生成的字段（缓存的 lambda 等）不是手写状态；自动属性的支持字段代表属性本身，照常检查。
        bool backingField = field.Name.EndsWith("k__BackingField", StringComparison.Ordinal);
        if (!backingField && (field.Name.StartsWith('<') || field.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute")))
        {
            return false;
        }

        if (!field.IsInitOnly)
        {
            return true;
        }

        string typeName = (field.FieldType is GenericInstanceType generic ? generic.ElementType : field.FieldType).FullName;
        if (ImmutableCollectionPrefixes.Any(prefix => typeName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        return CacheTypePrefixes.Any(prefix => typeName.StartsWith(prefix, StringComparison.Ordinal)) || IsDefType(field.FieldType);
    }

    private static bool IsDefType(TypeReference type)
        => CecilNames.TryResolve(type) is { } definition && (CecilNames.Type(definition) == "Verse.Def" || CecilNames.BaseTypeNames(definition).Contains("Verse.Def", StringComparer.Ordinal));

    private static bool IsDefOf(TypeDefinition type)
        => type.CustomAttributes.Any(a => a.AttributeType.FullName is "RimWorld.DefOf" or "RimWorld.DefOfAttribute");

    // 自动属性的支持字段 <Name>k__BackingField 按属性名查找。
    private static string DeclaredName(FieldDefinition field)
        => field.Name.StartsWith('<') && field.Name.IndexOf('>', StringComparison.Ordinal) is var close and > 1 ? field.Name[1..close] : field.Name;

    /// <summary>C# 工程目录下的源码文件（不含 bin/obj），按相对路径缓存文本。</summary>
    private sealed class SourceFiles
    {
        private readonly string root;
        private readonly Dictionary<string, string[]> cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> declaring = new(StringComparer.Ordinal);
        private List<string>? all;

        public SourceFiles(string root) => this.root = root;

        private IReadOnlyList<string> All => all ??= Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("bin/", StringComparison.Ordinal) && !path.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        public string[] Lines(string relative)
        {
            if (!cache.TryGetValue(relative, out string[]? lines))
            {
                string path = Path.IsPathRooted(relative) ? relative : Path.Combine(root, relative);
                lines = File.Exists(path) ? File.ReadAllLines(path) : [];
                cache[relative] = lines;
            }

            return lines;
        }

        /// <summary>没有 PDB 映射时的退路：声明了名为 <paramref name="typeName"/> 的类型的文件。</summary>
        public IReadOnlyList<string> Declaring(string typeName)
        {
            if (!declaring.TryGetValue(typeName, out List<string>? result))
            {
                var pattern = new Regex(@"\b(class|struct|record|interface)\s+" + Regex.Escape(typeName.Split('`')[0]) + @"\b", RegexOptions.CultureInvariant);
                result = All.Where(file => Lines(file).Any(pattern.IsMatch)).ToList();
                declaring[typeName] = result;
            }

            return result;
        }

        /// <summary>字段（或自动属性）声明所在的行号（从 1 开始）。</summary>
        public (string File, int Line)? FindDeclaration(string relative, string name, bool property)
        {
            var pattern = new Regex(@"\bstatic\b[^;={}]*?(?<![\w.])" + Regex.Escape(name) + (property ? @"\s*\{" : @"\s*(=|;|,)"), RegexOptions.CultureInvariant);
            string[] lines = Lines(relative);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//", 2)[0];
                if (pattern.IsMatch(code))
                {
                    return (relative, i + 1);
                }
            }

            return null;
        }

        /// <summary>
        /// 生命周期标记出现在：声明行本身；其上方 20 个非空行之内（不越过成员或类型的边界，同组字段与多行初始化式算在内）；或声明它的
        /// 类型上方的注释中（整个类型的静态字段共用一条说明，例如可选 Def 缓存类）。
        /// </summary>
        public bool HasLifecycleComment(string relative, int line, string typeName)
        {
            string[] lines = Lines(relative);
            if (lines[line - 1].Contains(LifecycleMarker, StringComparison.Ordinal))
            {
                return true;
            }

            // 同 1.6 静态检查：向上看有限的几行（多行初始化式与同组字段算在内）。块的开始（类型或方法体）或单独的块结束
            // （上一个成员的方法体）结束这一段；"};" 之类属于初始化式。
            int nonBlank = 0;
            for (int i = line - 2; i >= 0 && nonBlank < 20; i--)
            {
                string text = lines[i].Trim();
                if (text.Contains(LifecycleMarker, StringComparison.Ordinal))
                {
                    return true;
                }

                if (text.EndsWith('{') || text == "}")
                {
                    break;
                }

                if (text.Length > 0)
                {
                    nonBlank++;
                }
            }

            return TypeCommentHasMarker(lines, line, typeName);
        }

        // 字段之上最近的同名类型声明，及其上方连续的注释与特性行。
        private static bool TypeCommentHasMarker(string[] lines, int line, string typeName)
        {
            var declaration = new Regex(@"\b(class|struct|record)\s+" + Regex.Escape(typeName.Split('`')[0]) + @"\b", RegexOptions.CultureInvariant);
            for (int i = line - 2; i >= 0; i--)
            {
                if (!declaration.IsMatch(lines[i].Split("//", 2)[0]))
                {
                    continue;
                }

                for (int j = i - 1; j >= 0; j--)
                {
                    string text = lines[j].Trim();
                    if (text.Contains(LifecycleMarker, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (!(text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith('[') || text.StartsWith('*') || text.StartsWith("/*", StringComparison.Ordinal)))
                    {
                        return false;
                    }
                }

                return false;
            }

            return false;
        }
    }
}
