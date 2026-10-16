using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MugirlRace.Validation.Metadata;

/// <summary>用 System.Reflection.Metadata 读取的 DLL 身份、目标框架和程序集引用。</summary>
internal sealed record AssemblyFacts(string Name, Version Version, string? TargetFramework, IReadOnlyList<(string Name, Version Version)> References)
{
    public static AssemblyFacts Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        var references = metadata.AssemblyReferences
            .Select(handle => metadata.GetAssemblyReference(handle))
            .Select(reference => (metadata.GetString(reference.Name), reference.Version))
            .ToList();
        return new AssemblyFacts(metadata.GetString(assembly.Name), assembly.Version, TargetFrameworkOf(metadata, assembly), references);
    }

    // [assembly: TargetFramework(".NETCoreApp,Version=v10.0")] 的第一个固定字符串参数。
    private static string? TargetFrameworkOf(MetadataReader metadata, AssemblyDefinition assembly)
    {
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            MemberReference constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Name) != "TargetFrameworkAttribute")
            {
                continue;
            }

            BlobReader blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() == 1)
            {
                return blob.ReadSerializedString();
            }
        }

        return null;
    }
}
