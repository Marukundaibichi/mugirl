using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RenameApply.Xml;

// What a Defs XML element holds, derived from the C# field it loads into (Verse.DirectXmlToObject rules): the def type of
// a reference, a plain string, or unknown (type not in the compilation, dictionaries, custom loaders). This makes def
// references typed: a backstory category or an apparel tag that happens to equal a defName is not renamed, and a name
// shared by several def types resolves to the right one without explicitRefs.
public sealed class XmlSchema
{
    public enum Kind { Unknown, DefRef, String, Other }

    public sealed record Slot(Kind Kind, string? DefRoot, ITypeSymbol? Type, INamedTypeSymbol? Owner, string? Field)
    {
        public bool OwnerIsMod { get; init; }
    }

    private readonly CSharpCompilation _c;
    private readonly DefTypeResolver _types;
    private readonly string _modAssembly;
    private readonly Dictionary<(INamedTypeSymbol, string), IFieldSymbol?> _fieldCache = new();

    // String fields of engine types that the engine resolves to defs by name.
    private static readonly Dictionary<(string Owner, string Field), string> StringDefFields = new()
    {
        [("Verse.ThingFilter", "categories")] = "Verse.ThingCategoryDef",
        [("Verse.ThingFilter", "disallowedCategories")] = "Verse.ThingCategoryDef",
        [("Verse.ThingFilter", "specialFiltersToAllow")] = "Verse.SpecialThingFilterDef",
        [("Verse.ThingFilter", "specialFiltersToDisallow")] = "Verse.SpecialThingFilterDef",
    };

    public XmlSchema(CSharpCompilation c, DefTypeResolver types, string modAssembly)
    {
        _c = c;
        _types = types;
        _modAssembly = modAssembly;
    }

    public INamedTypeSymbol? ResolveType(string name) => _types.Resolve(name) ?? ResolveAnyType(name);

    private INamedTypeSymbol? ResolveAnyType(string name)
    {
        INamedTypeSymbol? t = _c.GetTypeByMetadataName(name);
        if (t != null)
        {
            return t;
        }
        foreach (string p in new[] { "Verse.", "RimWorld.", "Verse.AI.", "RimWorld.Planet.", "AlienRace.", "Verse.Sound.", "RimWorld.QuestGen." })
        {
            t = _c.GetTypeByMetadataName(p + name);
            if (t != null)
            {
                return t;
            }
        }
        return null;
    }

    // Element types of a Defs file, keyed by element (null entry = unknown).
    public Dictionary<XElem, Slot> Analyze(XDoc doc)
    {
        var result = new Dictionary<XElem, Slot>();
        if (doc.Root == null || doc.Root.Name != "Defs")
        {
            return result;
        }
        foreach (XElem def in doc.Root.Children)
        {
            INamedTypeSymbol? t = ResolveType(def.Attr("Class")?.Value ?? def.Name);
            if (t == null)
            {
                continue;
            }
            result[def] = new Slot(Kind.Other, null, t, null, null);
            Walk(def, t, result);
        }
        return result;
    }

    private void Walk(XElem parent, ITypeSymbol parentType, Dictionary<XElem, Slot> result)
    {
        foreach (XElem e in parent.Children)
        {
            Slot slot = SlotOf(e, parentType, parent, result);
            result[e] = slot;
            if (slot.Type != null && slot.Kind is Kind.Other)
            {
                Walk(e, slot.Type, result);
            }
        }
    }

    private Slot SlotOf(XElem e, ITypeSymbol parentType, XElem parent, Dictionary<XElem, Slot> result)
    {
        ITypeSymbol? type;
        INamedTypeSymbol? owner = null;
        string? field = null;
        if (ElementTypeOfCollection(parentType) is { } elementType)
        {
            if (e.Name != "li")
            {
                return new Slot(Kind.Unknown, null, null, null, null);
            }
            type = elementType;
            // a list element inherits the owning field (for string-def fields like ThingFilter.categories)
            if (result.TryGetValue(parent, out Slot? ps))
            {
                owner = ps.Owner;
                field = ps.Field;
            }
        }
        else if (parentType is INamedTypeSymbol nt && IsDictionary(nt))
        {
            return new Slot(Kind.Unknown, null, null, null, null);
        }
        else if (parentType is INamedTypeSymbol pt)
        {
            IFieldSymbol? f = FindField(pt, e.Name);
            if (f == null)
            {
                return new Slot(Kind.Unknown, null, null, null, null);
            }
            type = f.Type;
            owner = f.ContainingType;
            field = f.Name;
        }
        else
        {
            return new Slot(Kind.Unknown, null, null, null, null);
        }
        if (e.Attr("Class")?.Value is string cls && ResolveType(cls) is { } ct)
        {
            type = ct;
        }
        type = Unwrap(type);
        bool ownerIsMod = owner?.ContainingAssembly?.Name == _modAssembly;
        if (type is INamedTypeSymbol named && DefTypeResolver.RootOf(named) is { } root)
        {
            return new Slot(Kind.DefRef, root, type, owner, field) { OwnerIsMod = ownerIsMod };
        }
        if (type?.SpecialType == SpecialType.System_String)
        {
            if (owner != null && field != null && StringDefFields.TryGetValue((owner.ToDisplayString(), field), out string? defRoot))
            {
                return new Slot(Kind.DefRef, defRoot, type, owner, field) { OwnerIsMod = ownerIsMod };
            }
            return new Slot(Kind.String, null, type, owner, field) { OwnerIsMod = ownerIsMod };
        }
        return new Slot(Kind.Other, null, type, owner, field) { OwnerIsMod = ownerIsMod };
    }

    private static ITypeSymbol? Unwrap(ITypeSymbol? t) =>
        t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n ? n.TypeArguments[0] : t;

    private static bool IsDictionary(INamedTypeSymbol t) =>
        t.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>")
        || t.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal);

    private static ITypeSymbol? ElementTypeOfCollection(ITypeSymbol t)
    {
        if (t is IArrayTypeSymbol a)
        {
            return a.ElementType;
        }
        if (t is not INamedTypeSymbol n || n.SpecialType == SpecialType.System_String || IsDictionary(n))
        {
            return null;
        }
        string def = n.OriginalDefinition.ToDisplayString();
        if (def is "System.Collections.Generic.List<T>" or "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IList<T>")
        {
            return n.TypeArguments[0];
        }
        return null;
    }

    private IFieldSymbol? FindField(INamedTypeSymbol t, string name)
    {
        if (_fieldCache.TryGetValue((t, name), out IFieldSymbol? hit))
        {
            return hit;
        }
        IFieldSymbol? found = null;
        for (INamedTypeSymbol? c = t; c != null && found == null; c = c.BaseType)
        {
            foreach (IFieldSymbol f in c.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsConst))
            {
                if (f.Name == name || f.GetAttributes().Any(a => a.AttributeClass?.Name == "LoadAliasAttribute" && a.ConstructorArguments.FirstOrDefault().Value as string == name))
                {
                    found = f;
                    break;
                }
            }
        }
        _fieldCache[(t, name)] = found;
        return found;
    }
}
