using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RenameApply;

// Resolves Def type names as XML and specs write them (short vanilla names, full names, HAR names, old or new Mugirl names)
// to the root Def type (the class directly derived from Verse.Def), which is what DefDatabase and DefInjected folders key on.
public sealed class DefTypeResolver
{
    private static readonly string[] Prefixes =
    {
        "", "Verse.", "RimWorld.", "Verse.AI.", "Verse.AI.Group.", "Verse.Sound.", "Verse.Grammar.", "RimWorld.Planet.",
        "RimWorld.QuestGen.", "RimWorld.BaseGen.", "RimWorld.SketchGen.", "RimWorld.Utility.", "LudeonTK.", "AlienRace.",
    };

    private readonly CSharpCompilation _c;
    private readonly RenameModel _m;
    private readonly Dictionary<string, INamedTypeSymbol?> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, List<INamedTypeSymbol>>? _byShort;

    public DefTypeResolver(CSharpCompilation c, RenameModel m)
    {
        _c = c;
        _m = m;
    }

    public INamedTypeSymbol? Resolve(string name)
    {
        name = name.Trim();
        if (_cache.TryGetValue(name, out INamedTypeSymbol? hit))
        {
            return hit;
        }
        string lookup = name;
        TypeRename? reverse = _m.Types.FirstOrDefault(t => t.New.Metadata == name || t.New.CSharpForm == name);
        if (reverse != null)
        {
            lookup = reverse.Old.Metadata;
        }
        INamedTypeSymbol? found = null;
        foreach (string p in Prefixes)
        {
            found = _c.GetTypeByMetadataName(p + lookup);
            if (found != null)
            {
                break;
            }
        }
        if (found == null && !lookup.Contains('.'))
        {
            _byShort ??= BuildShortIndex();
            if (_byShort.TryGetValue(lookup, out var list) && list.Count == 1)
            {
                found = list[0];
            }
        }
        _cache[name] = found;
        return found;
    }

    private Dictionary<string, List<INamedTypeSymbol>> BuildShortIndex()
    {
        var d = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        void Walk(INamespaceSymbol ns)
        {
            foreach (INamespaceSymbol n in ns.GetNamespaceMembers())
            {
                Walk(n);
            }
            foreach (INamedTypeSymbol t in ns.GetTypeMembers())
            {
                if (RootOf(t) != null)
                {
                    if (!d.TryGetValue(t.Name, out var list))
                    {
                        d[t.Name] = list = new List<INamedTypeSymbol>();
                    }
                    list.Add(t);
                }
            }
        }
        Walk(_c.Assembly.GlobalNamespace);
        return d;
    }

    public string? RootOf(string name) => Resolve(name) is { } t ? RootOf(t) : null;

    // The DefDatabase type, as Verse.GenDefDatabase.AllDefTypesWithDatabases picks it: the non-abstract ancestor closest to
    // Verse.Def (ThingDef for ThingDef_AlienRace and Mugirl Def subclasses; abstract BuildableDef is skipped).
    public static string? RootOf(ITypeSymbol t)
    {
        t = t.WithNullableAnnotation(NullableAnnotation.None);
        if (t.ToDisplayString() == "Verse.Def")
        {
            return "Verse.Def";
        }
        string? root = null;
        bool derivesFromDef = false;
        for (INamedTypeSymbol? c = t as INamedTypeSymbol; c != null; c = c.BaseType)
        {
            if (c.ToDisplayString() == "Verse.Def")
            {
                derivesFromDef = true;
                break;
            }
            if (!c.IsAbstract)
            {
                root = c.ToDisplayString();
            }
        }
        return derivesFromDef ? root : null;
    }

    // Root type for a name; falls back to the name itself (mods the compilation does not reference, e.g. FA, VEF).
    public string RootOrName(string name) => RootOf(name) ?? name.Trim();
}
