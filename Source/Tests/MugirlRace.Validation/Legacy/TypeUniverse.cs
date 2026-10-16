using MugirlRace.Validation.Content;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 一个游戏版本的只读类型世界（Cecil）：Assembly-CSharp 与参考目录中的其他程序集（HAR、Facial Animation 等），供旧构建解析基类、
/// Def 数据库根与 XML 中的类名。1.6 构建用 1.6 的完整 Assembly-CSharp（需要方法体才能推出存档根），1.7 构建用游戏目录。
/// </summary>
internal sealed class TypeUniverse : IDisposable
{
    private static readonly string[] SkippedPrefixes = ["UnityEngine", "Unity.", "System.", "Mono.", "netstandard", "mscorlib", "Microsoft.", "Waylith"];

    private readonly UniverseResolver resolver;
    private readonly List<ModuleDefinition> owned = [];
    private readonly Dictionary<string, TypeDefinition> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeDefinition> shortNames = new(StringComparer.Ordinal);
    private SaveRoots? gameRoots;
    private HashSet<string>? scribeLabels;

    private TypeUniverse(string label, UniverseResolver resolver, ModuleDefinition game, IReadOnlyList<(string Path, string Sha256)> inputs)
    {
        Label = label;
        this.resolver = resolver;
        Game = game;
        Inputs = inputs;
    }

    /// <summary><c>1.6</c> 或 <c>1.7</c>。</summary>
    public string Label { get; }

    /// <summary>该版本的 Assembly-CSharp。</summary>
    public ModuleDefinition Game { get; }

    /// <summary>建立索引的参考程序集文件及其 sha256（记入 builds.tsv）。</summary>
    public IReadOnlyList<(string Path, string Sha256)> Inputs { get; }

    /// <summary>从游戏 IL 推出的存档根（只算一次）。</summary>
    public SaveRoots GameRoots => gameRoots ??= SaveRoots.Derive([Game]);

    /// <summary>游戏与参考程序集在 Scribe 调用中写成字面量的全部标签（见 <see cref="ScribeLabelLiterals"/>；只算一次）。</summary>
    public IReadOnlySet<string> ScribeLabels => scribeLabels ??= owned.SelectMany(ScribeLabelLiterals.Of).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="gameAssembly"/> 为 Assembly-CSharp；<paramref name="directories"/> 中的其他托管程序集（Unity、System 等除外）
    /// 一并索引。<paramref name="redirectUnity"/> 为 true 时按 1.7 游戏运行时把 Unity 模块名解析为 UnityStubs。
    /// </summary>
    public static TypeUniverse Create(string label, string gameAssembly, IReadOnlyList<string> directories, bool redirectUnity)
    {
        var dirs = new List<string> { Path.GetDirectoryName(gameAssembly)! };
        dirs.AddRange(directories.Where(Directory.Exists));
        var resolver = new UniverseResolver(dirs, redirectUnity);
        var inputs = new List<(string, string)>();
        ModuleDefinition game = Read(resolver, gameAssembly);
        resolver.Register(game.Assembly);
        inputs.Add((gameAssembly, LegacyHash.Sha256File(gameAssembly)));
        var universe = new TypeUniverse(label, resolver, game, inputs);
        universe.owned.Add(game);
        universe.Index(game);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Assembly-CSharp" };
        foreach (string directory in directories.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (SkippedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) || !seen.Add(name))
                {
                    continue;
                }

                try
                {
                    ModuleDefinition module = Read(resolver, file);
                    resolver.Register(module.Assembly);
                    universe.owned.Add(module);
                    universe.Index(module);
                    inputs.Add((file, LegacyHash.Sha256File(file)));
                }
                catch (BadImageFormatException)
                {
                    // 非托管 DLL。
                }
            }
        }

        return universe;
    }

    /// <summary>把被分析的 Mod 程序集读入内存（引用经本世界解析）。调用方负责释放。</summary>
    public ModuleDefinition ReadMod(string path) => Read(resolver, path);

    /// <summary>
    /// XML 中的类名按 <c>GenTypes.GetTypeInAnyAssembly</c> 的规则解析：先 <paramref name="mod"/>，再完整名，再忽略命名空间下的短名。
    /// </summary>
    public TypeDefinition? Find(string name, ModuleDefinition? mod)
    {
        if (mod is not null && mod.GetType(name.Replace('+', '/')) is { } own)
        {
            return own;
        }

        if (types.TryGetValue(name, out TypeDefinition? found) || shortNames.TryGetValue(name, out found))
        {
            return found;
        }

        foreach (string ns in TypeResolver.IgnoredNamespaces)
        {
            if (types.TryGetValue(ns + "." + name, out found))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Def 类所在 DefDatabase 的类型短名：<c>Verse.Def</c> 之下最上层的非抽象祖先（<c>GenDefDatabase.AllDefTypesWithDatabases</c>
    /// 的规则：ThingDef 的子类与 ThingDef 共用 DefDatabase&lt;ThingDef&gt;，抽象的 BuildableDef 没有数据库）。不是 Def 时为 <see langword="null"/>。
    /// </summary>
    public static string? DatabaseRoot(TypeDefinition type)
    {
        string? root = null;
        for (TypeDefinition? current = type; current is not null;)
        {
            if (CecilNames.Type(current) == "Verse.Def")
            {
                return root;
            }

            if (!current.IsAbstract)
            {
                root = current.Name;
            }

            current = current.BaseType is { } baseType ? CecilNames.TryResolve(baseType) : null;
        }

        return null;
    }

    public void Dispose()
    {
        foreach (ModuleDefinition module in owned)
        {
            module.Dispose();
        }

        resolver.Dispose();
    }

    private static ModuleDefinition Read(UniverseResolver resolver, string path)
        => ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadingMode = ReadingMode.Deferred });

    private void Index(ModuleDefinition module)
    {
        foreach (TypeDefinition type in CecilNames.AllTypes(module))
        {
            string name = CecilNames.Type(type);
            types.TryAdd(name, type);
            if (type.DeclaringType is null && (type.Namespace.Length == 0 || Array.IndexOf(TypeResolver.IgnoredNamespaces, type.Namespace) >= 0))
            {
                shortNames[type.Name] = type;
            }
        }
    }

    /// <summary>按目录解析的 Cecil 解析器；1.7 世界按游戏运行时规则重定向 Unity 模块名。</summary>
    private sealed class UniverseResolver : DefaultAssemblyResolver
    {
        private readonly bool redirectUnity;

        public UniverseResolver(IEnumerable<string> directories, bool redirectUnity)
        {
            this.redirectUnity = redirectUnity;
            foreach (string directory in GetSearchDirectories())
            {
                RemoveSearchDirectory(directory);
            }

            foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                AddSearchDirectory(directory);
            }

            AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        }

        /// <summary>已读入的程序集直接用于解析，避免同一程序集被读第二份。</summary>
        public void Register(AssemblyDefinition assembly) => RegisterAssembly(assembly);

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (redirectUnity && (name.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
                || (name.Name.StartsWith("Unity.", StringComparison.Ordinal) && name.Name != "Unity.Newtonsoft.Json")
                || name.Name == "Assembly-CSharp-firstpass"))
            {
                return base.Resolve(new AssemblyNameReference("UnityStubs", new Version(0, 0)));
            }

            return base.Resolve(name);
        }
    }
}
