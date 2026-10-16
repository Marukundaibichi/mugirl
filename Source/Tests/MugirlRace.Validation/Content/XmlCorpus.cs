using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 完整配置的全部 Def XML 合并成一个文档，同 <c>LoadedModManager.CombineIntoUnifiedXML</c>（根 <c>&lt;Defs&gt;</c>，内容包按加载
/// 顺序，文件按游戏顺序），另记录每个内容包的补丁文件。<see cref="PatchSimulator"/> 把补丁应用到这个文档上。
/// </summary>
internal sealed class XmlCorpus
{
    private readonly Dictionary<XmlNode, NodeSource> sources = [];
    private readonly List<(ContentPack Pack, LoadedFile File)> patchFiles = [];
    private readonly List<(ContentPack Pack, string File, string Problem)> problems = [];

    private XmlCorpus(XmlDocument document) => Document = document;

    public XmlDocument Document { get; }

    public XmlElement Root => Document.DocumentElement!;

    /// <summary>按加载顺序列出各内容包的补丁文件。</summary>
    public IReadOnlyList<(ContentPack Pack, LoadedFile File)> PatchFiles => patchFiles;

    /// <summary>无法读取或根元素错误的文件。</summary>
    public IReadOnlyList<(ContentPack Pack, string File, string Problem)> Problems => problems;

    public static XmlCorpus Load(ModCatalog catalog)
    {
        var document = new XmlDocument();
        document.AppendChild(document.CreateElement("Defs"));
        var corpus = new XmlCorpus(document);
        foreach (ContentPack pack in catalog.LoadOrder)
        {
            IReadOnlyList<string> folders = catalog.FoldersFor(pack, catalog.Full);
            foreach (LoadedFile file in LoadFolderResolver.Files(folders, "Defs", IsXml))
            {
                XmlDocument? source = corpus.TryParse(pack, file.FullPath);
                if (source?.DocumentElement is not { } root)
                {
                    continue;
                }

                if (root.Name != "Defs")
                {
                    corpus.problems.Add((pack, file.FullPath, $"Root element is <{root.Name}>, expected <Defs>; the game skips the file."));
                    continue;
                }

                foreach (XmlElement def in root.ChildNodes.OfType<XmlElement>())
                {
                    XmlNode imported = document.ImportNode(def, deep: true);
                    document.DocumentElement!.AppendChild(imported);
                    corpus.sources[imported] = new NodeSource(pack, file.FullPath, file.LoadFolder, InsertedByPatch: false);
                }
            }

            foreach (LoadedFile file in LoadFolderResolver.Files(folders, "Patches", IsXml))
            {
                corpus.patchFiles.Add((pack, file));
            }
        }

        return corpus;
    }

    public static bool IsXml(string extension) => string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>节点的来源文件：Def 文件，或插入它的补丁文件。</summary>
    public NodeSource? SourceOf(XmlNode node)
    {
        for (XmlNode? current = node; current is not null; current = current.ParentNode)
        {
            if (sources.TryGetValue(current, out NodeSource? source))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>记录 <paramref name="node"/> 由补丁插入。</summary>
    public void MarkInserted(XmlNode node, ContentPack pack, string patchFile)
        => sources[node] = new NodeSource(pack, patchFile, LoadFolder: null, InsertedByPatch: true);

    /// <summary>文档中当前的顶层 Def 元素。</summary>
    public IEnumerable<XmlElement> Defs => Root.ChildNodes.OfType<XmlElement>();

    /// <summary><paramref name="pack"/> 的补丁插入且仍在文档中的节点。</summary>
    public IEnumerable<XmlElement> InsertedBy(ContentPack pack)
        => sources.Where(pair => pair.Value.InsertedByPatch && pair.Value.Pack == pack && pair.Key is XmlElement && IsAttached(pair.Key))
            .Select(pair => (XmlElement)pair.Key);

    public XmlDocument? TryParse(ContentPack pack, string path)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(path);
            return document;
        }
        catch (XmlException exception)
        {
            problems.Add((pack, path, "Malformed XML: " + exception.Message));
            return null;
        }
    }

    private bool IsAttached(XmlNode node)
    {
        for (XmlNode? current = node; current is not null; current = current.ParentNode)
        {
            if (current == Root)
            {
                return true;
            }
        }

        return false;
    }
}
