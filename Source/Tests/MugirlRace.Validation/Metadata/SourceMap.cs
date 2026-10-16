using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 方法到源码文件的映射，读自被测程序集的便携式 PDB（MethodDebugInformation 的文档）。架构规则按源码目录放行
/// （例如 <c>Compatibility/</c>），而 Mod 的根命名空间不随目录变化（ADR-004），只能靠 PDB 知道代码写在哪个文件。
/// </summary>
internal sealed class SourceMap
{
    // StaticCacheLifecycle: 进程级缓存（目录是否含工程文件），验证程序一次运行内磁盘不变。
    private static readonly Dictionary<string, bool> ProjectDirectories = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, string> documentByMethodToken;
    private readonly Dictionary<int, string[]> documentsByTypeToken;

    private SourceMap(Dictionary<int, string> documentByMethodToken, Dictionary<int, string[]> documentsByTypeToken, bool available)
    {
        this.documentByMethodToken = documentByMethodToken;
        this.documentsByTypeToken = documentsByTypeToken;
        Available = available;
    }

    /// <summary>读到了 PDB。</summary>
    public bool Available { get; }

    public static SourceMap Read(string? pdbPath)
    {
        var map = new Dictionary<int, string>();
        var typeMap = new Dictionary<int, string[]>();
        if (pdbPath is null || !File.Exists(pdbPath))
        {
            return new SourceMap(map, typeMap, available: false);
        }

        try
        {
            using FileStream stream = File.OpenRead(pdbPath);
            using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            MetadataReader reader = provider.GetMetadataReader();
            foreach (MethodDebugInformationHandle handle in reader.MethodDebugInformation)
            {
                MethodDebugInformation info = reader.GetMethodDebugInformation(handle);
                DocumentHandle document = info.Document;
                if (document.IsNil)
                {
                    // 跨多个文档的方法（如部分类）：取第一个序列点的文档。
                    foreach (SequencePoint point in info.GetSequencePoints())
                    {
                        document = point.Document;
                        break;
                    }
                }

                if (!document.IsNil)
                {
                    map[MetadataTokens.GetToken(handle.ToDefinitionHandle())] = Normalize(reader.GetString(reader.GetDocument(document).Name));
                }
            }
            // Portable PDB TypeDefinitionDocument: 无序列点的薄别名、枚举和空类型也必须有可验证的归属。
            // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#type-definition-document-c-and-vb-compilers
            Guid typeDocuments = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");
            foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
            {
                CustomDebugInformation info = reader.GetCustomDebugInformation(handle);
                if (info.Parent.Kind != HandleKind.TypeDefinition || reader.GetGuid(info.Kind) != typeDocuments) { continue; }
                BlobReader blob = reader.GetBlobReader(info.Value);
                var documents = new List<string>();
                while (blob.RemainingBytes > 0)
                {
                    DocumentHandle document = MetadataTokens.DocumentHandle(blob.ReadCompressedInteger());
                    documents.Add(Normalize(reader.GetString(reader.GetDocument(document).Name)));
                }

                typeMap[MetadataTokens.GetToken(info.Parent)] = documents.ToArray();
            }
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException)
        {
            return new SourceMap(map, typeMap, available: false);
        }

        return new SourceMap(map, typeMap, available: true);
    }

    /// <summary>方法所在源码文件（正斜杠路径），未知时为 <see langword="null"/>。</summary>
    public string? DocumentOf(MethodDefinition method)
        => documentByMethodToken.GetValueOrDefault(method.MetadataToken.ToInt32());

    /// <summary>类型的源码文件：其（及嵌套类型）方法出现过的全部文档。</summary>
    public IEnumerable<string> DocumentsOf(TypeDefinition type)
        => SelfAndNested(type).SelectMany(t => t.Methods.Select(DocumentOf).OfType<string>()
            .Concat(documentsByTypeToken.GetValueOrDefault(t.MetadataToken.ToInt32()) ?? [])).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// 源码文件相对 C# 工程目录的路径（正斜杠），例如 <c>Compatibility/OtherMods/X.cs</c>。PDB 记录的是编译时的绝对路径，构建可能在
    /// 临时副本中进行，所以依次按 <paramref name="sourceDir"/>、文件所在的最近一个含 <c>*.csproj</c> 的目录（构建目录仍在时）、最后一个
    /// <c>/Source/MugirlRace/</c> 截取；都不匹配时为 <see langword="null"/>。
    /// </summary>
    public static string? ProjectRelative(string document, string sourceDir)
    {
        string path = Normalize(document);
        string root = Normalize(sourceDir).TrimEnd('/') + "/";
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path[root.Length..];
        }

        if (ProjectDirectoryOf(path) is { } project)
        {
            return path[(project.Length + 1)..];
        }

        const string Marker = "/Source/MugirlRace/";
        int index = path.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? path[(index + Marker.Length)..] : null;
    }

    // 文件所在的最近一个含工程文件的目录（正斜杠，无结尾斜杠）；文件不在磁盘上时为 null。
    private static string? ProjectDirectoryOf(string path)
    {
        lock (ProjectDirectories)
        {
            for (string? directory = Parent(path); !string.IsNullOrEmpty(directory); directory = Parent(directory))
            {
                if (!ProjectDirectories.TryGetValue(directory, out bool isProject))
                {
                    isProject = Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.csproj").Any();
                    ProjectDirectories[directory] = isProject;
                }

                if (isProject)
                {
                    return directory;
                }
            }
        }

        return null;
    }

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
        {
            foreach (TypeDefinition inner in SelfAndNested(nested))
            {
                yield return inner;
            }
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string? Parent(string path) => Path.GetDirectoryName(path) is { } parent ? Normalize(parent) : null;
}
