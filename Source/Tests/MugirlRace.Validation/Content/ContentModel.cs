using System.Reflection;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation.Content;

/// <summary>数据检查读取的全部内容：应用补丁后的统一 Def 文档、各类索引与类型解析器。</summary>
internal sealed class ContentModel
{
    private ContentModel(ModCatalog catalog, TypeResolver types, XmlCorpus corpus, PatchSimulator patches, XmlInheritanceResolver inheritance, DefIndex defs,
        KeyedIndex keyed, TextureIndex textures, SoundIndex sounds)
    {
        Catalog = catalog;
        Types = types;
        Corpus = corpus;
        Patches = patches;
        Inheritance = inheritance;
        Defs = defs;
        Keyed = keyed;
        Textures = textures;
        Sounds = sounds;
    }

    public ModCatalog Catalog { get; }

    public TypeResolver Types { get; }

    public XmlCorpus Corpus { get; }

    public PatchSimulator Patches { get; }

    public XmlInheritanceResolver Inheritance { get; }

    public DefIndex Defs { get; }

    public KeyedIndex Keyed { get; }

    public TextureIndex Textures { get; }

    public SoundIndex Sounds { get; }

    public static ContentModel Build(ModCatalog catalog, GameRuntime runtime, string gameDir)
    {
        IReadOnlyList<Assembly> assemblies = runtime.ActiveAssemblies.ToList();
        var types = new TypeResolver(assemblies, assembly => assembly == runtime.Mod ? runtime.ModTypes : GameRuntime.LoadableTypes(assembly));
        XmlCorpus corpus = XmlCorpus.Load(catalog);
        PatchSimulator patches = PatchSimulator.ApplyAll(corpus, types, catalog.Full);
        var inheritance = new XmlInheritanceResolver(corpus, catalog, catalog.Full);
        DefIndex defs = DefIndex.Build(corpus, catalog, types, inheritance);
        return new ContentModel(catalog, types, corpus, patches, inheritance, defs, KeyedIndex.Build(catalog), TextureIndex.Build(catalog, gameDir),
            SoundIndex.Build(catalog, gameDir));
    }

    /// <summary><paramref name="pack"/> 是被测 Mod 时为 true。</summary>
    public static bool IsOurs(ContentPack? pack) => pack is not null && pack.IsUnderTest;
}
