using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CecilDocument = Mono.Cecil.Cil.Document;
using CecilModule = Mono.Cecil.ModuleDefinition;
using CecilToken = Mono.Cecil.MetadataToken;
using CecilType = Mono.Cecil.TypeDefinition;
using PdbReader = System.Reflection.Metadata.MetadataReader;

namespace Mugirl.Build.MetadataOrder;

/// <summary>固定 Cecil 0.11.6 的文档发射顺序，并独立验证类型到源文件的实际对应关系。</summary>
internal sealed class PortablePdbPreservation
{
    private static readonly Guid TypeDocumentsKind = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");
    private readonly DocumentData[] documents;
    private readonly Dictionary<string, int[]> typeDocuments;

    private sealed record CustomData(Guid Kind, byte[] Value);
    private sealed record DocumentData(string Url, Guid Language, Guid HashAlgorithm, byte[] Hash, CustomData[] Custom)
    {
        public CecilDocument Create()
        {
            var document = new CecilDocument(Url) { LanguageGuid = Language, HashAlgorithmGuid = HashAlgorithm, Hash = Hash };
            foreach (CustomData custom in Custom) { document.CustomDebugInformations.Add(new BinaryCustomDebugInformation(custom.Kind, custom.Value)); }
            return document;
        }

        public bool SameAs(DocumentData other)
            => Url == other.Url && Language == other.Language && HashAlgorithm == other.HashAlgorithm && Hash.SequenceEqual(other.Hash)
                && Custom.Length == other.Custom.Length && Custom.Zip(other.Custom).All(p => p.First.Kind == p.Second.Kind && p.First.Value.SequenceEqual(p.Second.Value));
    }

    private PortablePdbPreservation(DocumentData[] documents, Dictionary<string, int[]> typeDocuments)
    {
        this.documents = documents;
        this.typeDocuments = typeDocuments;
    }

    public static PortablePdbPreservation Capture(CecilModule module, byte[] symbols)
    {
        using var stream = new MemoryStream(symbols, writable: false);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        PdbReader reader = provider.GetMetadataReader();
        DocumentData[] documents = reader.Documents.Select(handle =>
        {
            System.Reflection.Metadata.Document document = reader.GetDocument(handle);
            CustomData[] custom = reader.GetCustomDebugInformation(handle).Select(customHandle =>
            {
                System.Reflection.Metadata.CustomDebugInformation information = reader.GetCustomDebugInformation(customHandle);
                return new CustomData(reader.GetGuid(information.Kind), reader.GetBlobBytes(information.Value));
            }).ToArray();
            return new DocumentData(reader.GetString(document.Name), reader.GetGuid(document.Language), reader.GetGuid(document.HashAlgorithm), reader.GetBlobBytes(document.Hash), custom);
        }).ToArray();
        Dictionary<int, CecilType> types = module.GetTypes().ToDictionary(t => t.MetadataToken.ToInt32());
        var typeDocuments = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
        {
            System.Reflection.Metadata.CustomDebugInformation information = reader.GetCustomDebugInformation(handle);
            if (information.Parent.Kind != HandleKind.TypeDefinition || reader.GetGuid(information.Kind) != TypeDocumentsKind) { continue; }
            if (!types.TryGetValue(MetadataTokens.GetToken(information.Parent), out CecilType? type))
            {
                throw new InvalidDataException("Portable PDB document record references a missing type.");
            }
            BlobReader blob = reader.GetBlobReader(information.Value);
            var rows = new List<int>();
            while (blob.RemainingBytes != 0)
            {
                int row = blob.ReadCompressedInteger();
                if (row <= 0 || row > documents.Length) { throw new InvalidDataException("Portable PDB type document is outside the Document table: " + type.FullName); }
                rows.Add(row);
            }
            if (!typeDocuments.TryAdd(type.FullName, rows.ToArray()))
            {
                throw new InvalidDataException("Duplicate portable PDB type document record: " + type.FullName);
            }
        }
        return new PortablePdbPreservation(documents, typeDocuments);
    }

    public ISymbolWriterProvider CreateWriterProvider() => new PreservingWriterProvider(documents);

    public void Verify(CecilModule module, byte[] symbols)
    {
        PortablePdbPreservation actual = Capture(module, symbols);
        if (documents.Length != actual.documents.Length || documents.Where((document, i) => !document.SameAs(actual.documents[i])).Any())
        {
            throw new InvalidDataException("Portable PDB Document rows changed or disappeared.");
        }
        if (typeDocuments.Count != actual.typeDocuments.Count)
        {
            throw new InvalidDataException("Portable PDB type document records changed or disappeared.");
        }
        foreach ((string name, int[] rows) in typeDocuments)
        {
            if (!actual.typeDocuments.TryGetValue(name, out int[]? actualRows) || rows.Length != actualRows.Length
                || rows.Where((row, i) => !documents[row - 1].SameAs(actual.documents[actualRows[i] - 1])).Any())
            {
                throw new InvalidDataException("Portable PDB type points to different source documents: " + name);
            }
        }
    }

    private sealed class PreservingWriterProvider(DocumentData[] documents) : ISymbolWriterProvider
    {
        public ISymbolWriter GetSymbolWriter(CecilModule module, string fileName)
            => throw new NotSupportedException("Metadata ordering requires an in-memory portable PDB output.");

        public ISymbolWriter GetSymbolWriter(CecilModule module, Stream symbolStream)
        {
            // Cecil 对 TypeDefinitionDocuments 只搬运原始 blob，其中的 Document RID 不会重写。
            // 预登记所有文档还保留了只有枚举、空类型等引用、没有任何序列点的源文件。
            Type writerType = typeof(PortablePdbWriter);
            if (writerType.Assembly.GetName().Version != new Version(0, 11, 6, 0))
            {
                throw new NotSupportedException("Portable PDB preservation requires Mono.Cecil 0.11.6.0.");
            }
            FieldInfo? field = writerType.GetField("pdb_metadata", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? register = field?.FieldType.GetMethod("GetDocumentToken", BindingFlags.Instance | BindingFlags.Public, null, [typeof(CecilDocument)], null);
            if (field?.FieldType.FullName != "Mono.Cecil.MetadataBuilder" || register?.ReturnType != typeof(CecilToken))
            {
                throw new NotSupportedException("Mono.Cecil portable PDB internals do not match the verified document registration contract.");
            }
            ISymbolWriter writer = new PortablePdbWriterProvider().GetSymbolWriter(module, symbolStream);
            try
            {
                object metadata = field.GetValue(writer) ?? throw new InvalidDataException("Cecil did not create portable PDB metadata.");
                for (int i = 0; i < documents.Length; i++)
                {
                    var token = (CecilToken)(register.Invoke(metadata, [documents[i].Create()]) ?? throw new InvalidDataException("Cecil returned no document token."));
                    if (token.TokenType != TokenType.Document || token.RID != (uint)i + 1)
                    {
                        throw new InvalidDataException("Cecil cannot retain original Document rows; duplicate source URLs are unsupported.");
                    }
                }
                return writer;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }
    }
}