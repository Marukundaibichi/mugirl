using System.Xml.Linq;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>测试用的存档片段与 owner 目录。</summary>
internal static class Saves
{
    /// <summary>把 <paramref name="game"/> 包成 <c>savegame/game</c> 文档（<paramref name="tick"/> 写在 tickManager 中）。</summary>
    public static XDocument Game(string game, int tick = 1000)
        => XDocument.Parse($"<savegame><meta><modIds><li>har.mugirlrace</li></modIds></meta><game><tickManager><ticksGame>{tick}</ticksGame></tickManager>{game}</game></savegame>");

    /// <summary>旧版语料：(根类型, defName, 组件类) 与 (类型, 标签)。</summary>
    public static LegacyCorpus Corpus((string Root, string Def, string Comp)[] comps, (string Owner, string Label)[] labels, (string Type, string ModBase)[]? bases = null)
        => LegacyCorpus.Parse(
            "# header\n" + string.Join("\n", comps.Select(c => $"{c.Root}\t{c.Def}\tProps\t{c.Comp}\tdef\tpublished\tMugirl\tp\tp")),
            "# header\n" + string.Join("\n", labels.Select(l => $"{l.Owner}\t{l.Label}\tValues\tSystem.Int32\tValue\t0\tFalse\tpublished\tMugirl\tp\tp")),
            "# header\n" + string.Join("\n", (bases ?? []).Select(b => $"{b.Type}\tclass\t-\t{b.ModBase}\tVerse.ThingComp\tpublic\tpublished\tMugirl\tp\tp")));

    public static SaveOwnerCatalog Catalog(SaveNameMap map, LegacyCorpus corpus, params (string Root, string Def, string Comp)[] current)
        => new(map, corpus, current);

    /// <summary>只有类型映射、没有任何组件关系的目录。</summary>
    public static SaveOwnerCatalog Catalog(SaveNameMap map) => new(map, LegacyCorpus.Empty, []);

    public static XElement Element(XDocument document, string name) => document.Descendants(name).First();
}
