using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using MugirlRace.Validation.Legacy;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>金样中的物理标签按同一 owner 语法求值；组件关系取旧语料，别名取被测清单。</summary>
internal sealed class GoldenOwnerContext : ISaveOwnerContext
{
    private readonly Dictionary<string, string?> types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Root, string Def), HashSet<string>> comps = [];

    internal GoldenOwnerContext(LegacyCorpus corpus, UpgradeManifest manifest)
    {
        foreach (ManifestEntry row in manifest.Types) { types.TryAdd(row.Old, row.New); }
        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.DefCompsFile))
        {
            if (row[3] == "-") { continue; }
            string comp = Map(row[3]);
            Add(row[0], row[1], comp);
            foreach (ManifestEntry renamed in manifest.Defs.Where(d => d.Root == row[0] && d.Old == row[1] && d.New is not null))
            {
                Add(row[0], renamed.New!, comp);
            }
        }
    }

    public string? MappedClassOf(XElement element) => element.Attribute("Class")?.Value is { } name ? Map(name) : null;

    public bool DefHasComp(string root, string defName, string compClass)
        => comps.TryGetValue((root, defName), out HashSet<string>? values) && values.Contains(compClass);

    private string Map(string name) => types.TryGetValue(name, out string? mapped) ? mapped ?? name : name;

    private void Add(string root, string defName, string comp)
    {
        if (!comps.TryGetValue((root, defName), out HashSet<string>? values))
        {
            comps.Add((root, defName), values = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        values.Add(comp);
    }
}
