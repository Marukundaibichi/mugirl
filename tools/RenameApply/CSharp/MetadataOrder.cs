using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mugirl.Build.MetadataOrder;

namespace RenameApply.CSharp;

public sealed partial class CSharpPass
{
    public string BuildTypeOrderManifest(string? previous)
    {
        (byte[] oldAssembly, _) = Emit(_originalMain);
        (byte[] finalAssembly, byte[] finalSymbols) = Emit(_final[Main]);
        string[] original = TypeNames(oldAssembly);
        string[] current = TypeNames(finalAssembly);
        HashSet<string> currentSet = current.ToHashSet(StringComparer.Ordinal);
        string[] prior = previous == null ? original : MetadataOrderer.ReadOrder(previous).Concat(original).Distinct(StringComparer.Ordinal).ToArray();
        string Map(string name)
        {
            if (Tables.TypeFull.TryGetValue(name, out string? mapped)) { return mapped; }
            // 编译器生成的嵌套类型跟随外部类型移动；最终不存在的旧生成物不会进入名单。
            for (int at = name.LastIndexOf('+'); at > 0; at = name.LastIndexOf('+', at - 1))
            {
                if (Tables.TypeFull.TryGetValue(name[..at], out mapped)) { return mapped + name[at..]; }
            }
            return name;
        }
        string[] order = prior.Select(Map).Where(currentSet.Contains).Concat(current).Distinct(StringComparer.Ordinal).ToArray();
        MetadataOrderer.Result verified = MetadataOrderer.Rewrite(finalAssembly, finalSymbols, order, Main.ResolverDirectories);
        string[] emitted = TypeNames(verified.Assembly);
        foreach (string family in new[] { "Verse.GameComponent", "Verse.MapComponent", "RimWorld.Planet.WorldComponent", "RimWorld.FloatMenuOptionProvider", "RimWorld.Alert", "Verse.StaticConstructorOnStartup", "RimWorld.DefOf" })
        {
            string[] expected = prior.Where(name => InFamily(_originalMain.GetTypeByMetadataName(name), family))
                .Select(Map).Where(currentSet.Contains).Distinct(StringComparer.Ordinal).ToArray();
            HashSet<string> members = expected.ToHashSet(StringComparer.Ordinal);
            if (!expected.SequenceEqual(emitted.Where(members.Contains), StringComparer.Ordinal))
            {
                _report.Error("metadata-order-family", $"{family}: emitted DLL does not preserve the relative order of existing family members.");
            }
        }
        _report.Info("metadata-order-verified", $"Emitted DLL/PDB round trip: {verified.Types} types, {verified.SequencePoints} sequence points; reflection family order checked.");
        return MetadataOrderer.ManifestHeader + "\n# Existing types retain metadata order; new types are appended automatically.\n" + string.Join("\n", order) + "\n";
    }

    private static bool InFamily(INamedTypeSymbol? type, string family)
    {
        if (type == null) { return false; }
        if (family is "Verse.StaticConstructorOnStartup" or "RimWorld.DefOf")
        {
            return type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == family);
        }
        if (type.IsAbstract) { return false; }
        for (INamedTypeSymbol? parent = type.BaseType; parent != null; parent = parent.BaseType)
        {
            if (parent.ToDisplayString() == family) { return true; }
        }
        return false;
    }

    private static (byte[] Assembly, byte[] Symbols) Emit(CSharpCompilation compilation)
    {
        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        EmitResult result = compilation.Emit(assembly, pdbStream: symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: compilation.AssemblyName + ".pdb"));
        if (!result.Success)
        {
            throw new ToolException("Cannot verify emitted metadata order: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)));
        }
        return (assembly.ToArray(), symbols.ToArray());
    }

    private static string[] TypeNames(byte[] assembly)
    {
        using var stream = new MemoryStream(assembly, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        string Name(TypeDefinitionHandle handle)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            string name = metadata.GetString(type.Name);
            TypeDefinitionHandle parent = type.GetDeclaringType();
            if (!parent.IsNil) { return Name(parent) + "+" + name; }
            string ns = metadata.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        return metadata.TypeDefinitions.Select(Name).ToArray();
    }
}
