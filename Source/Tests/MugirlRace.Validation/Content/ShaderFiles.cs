namespace MugirlRace.Validation.Content;

/// <summary>
/// RimWorld 1.7 可见的 GLSL 源，搜索方式同 <c>ShaderDefRegistry</c>/<c>ShaderSourceLoader</c>：运行中各 Mod（含 Core 与 DLC）
/// 每个加载目录的 <c>Shaders</c> 目录，后加载的在前，然后是引擎的 <c>Rendering/Shaders</c>；每个根目录先按相对路径、再按文件名
/// 在其下任意位置查找（忽略大小写）。
/// </summary>
internal sealed class ShaderFiles
{
    private readonly List<string> roots;
    private readonly List<string> gameFiles;

    private ShaderFiles(List<string> roots, List<string> gameFiles, List<string> ours, List<string> ourRoots)
    {
        this.roots = roots;
        this.gameFiles = gameFiles;
        Ours = ours;
        OurRoots = ourRoots;
    }

    /// <summary>Mod 自己的 GLSL 文件。</summary>
    public IReadOnlyList<string> Ours { get; }

    /// <summary>Mod 各加载目录中的 <c>Shaders</c> 目录。</summary>
    public IReadOnlyList<string> OurRoots { get; }

    public static ShaderFiles Collect(string gameDir, ModCatalog catalog)
    {
        var roots = new List<string>();
        var gameFiles = new List<string>();
        var ours = new List<string>();
        var ourRoots = new List<string>();
        foreach (ContentPack pack in catalog.LoadOrder.Reverse())
        {
            foreach (string folder in catalog.FoldersFor(pack, catalog.Full))
            {
                string shaders = Path.Combine(folder, "Shaders");
                if (!Directory.Exists(shaders))
                {
                    continue;
                }

                roots.Add(shaders);
                List<string> glsl = Directory.EnumerateFiles(shaders, "*.glsl", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
                if (pack.IsUnderTest)
                {
                    ours.AddRange(glsl);
                    ourRoots.Add(shaders);
                }
                else if (pack.IsOfficial)
                {
                    gameFiles.AddRange(glsl);
                }
            }
        }

        string engine = Path.Combine(gameDir, "Rendering", "Shaders");
        if (Directory.Exists(engine))
        {
            roots.Add(engine);
            gameFiles.AddRange(Directory.EnumerateFiles(engine, "*.glsl", SearchOption.AllDirectories));
        }

        return new ShaderFiles(roots, gameFiles, ours, ourRoots);
    }

    /// <summary>Shader 源名称解析到的文件，或 <see langword="null"/>。</summary>
    public string? Resolve(string relativeOrName)
    {
        string relative = relativeOrName.Replace('/', Path.DirectorySeparatorChar);
        string name = Path.GetFileName(relative);
        foreach (string root in roots)
        {
            string direct = Path.Combine(root, relative);
            if (File.Exists(direct))
            {
                return direct;
            }

            string? byName = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        return null;
    }

    /// <summary><paramref name="path"/> 是 Mod 自己的 GLSL 文件。</summary>
    public bool IsOurs(string path) => Ours.Contains(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase)
        || OurRoots.Any(root => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    /// <summary>同名（忽略大小写）的游戏（Core、DLC 或引擎）Shader 文件，或 <see langword="null"/>。</summary>
    public string? GameFileNamed(string name)
        => gameFiles.FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase));
}
