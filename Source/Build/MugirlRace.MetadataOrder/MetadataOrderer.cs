using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mugirl.Build.MetadataOrder;

/// <summary>只重排类型定义集合；程序集身份、成员、IL 和调试符号保持原义。</summary>
public static class MetadataOrderer
{
    public const string ManifestHeader = "# Mugirl metadata order v1";

    public sealed record Result(byte[] Assembly, byte[] Symbols, bool Changed, int Types, int SequencePoints);

    public static string[] ReadOrder(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.FirstOrDefault()?.TrimEnd('\r') != ManifestHeader)
        {
            throw new InvalidDataException("Unsupported type-order manifest header.");
        }
        string[] names = lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new InvalidDataException("Duplicate type in the type-order manifest.");
        }
        return names;
    }

    public static Result Rewrite(byte[] assembly, byte[] symbols, IReadOnlyList<string> order, IEnumerable<string>? searchDirectories = null)
    {
        if (symbols.Length == 0)
        {
            throw new InvalidDataException("Portable PDB is required; refusing to drop debugging information.");
        }
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        foreach (string directory in (searchDirectories ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(directory))
            {
                resolver.AddSearchDirectory(directory);
            }
        }
        using var input = new MemoryStream(assembly, writable: false);
        using var inputSymbols = new MemoryStream(symbols, writable: false);
        using ModuleDefinition module = Read(input, inputSymbols, resolver);
        PortablePdbPreservation preservation = PortablePdbPreservation.Capture(module, symbols);
        string before = Fingerprint(module);
        int sequencePoints = AllTypes(module.Types).SelectMany(t => t.Methods).Sum(m => m.DebugInformation.SequencePoints.Count);
        Dictionary<string, int> ranks = order.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.Ordinal);
        bool changed = Reorder(module.Types, ranks);
        if (!changed)
        {
            return new Result(assembly, symbols, false, AllTypes(module.Types).Count(), sequencePoints);
        }
        using var output = new MemoryStream();
        using var outputSymbols = new MemoryStream();
        module.Write(output, new WriterParameters
        {
            WriteSymbols = true, SymbolWriterProvider = preservation.CreateWriterProvider(), SymbolStream = outputSymbols,
            DeterministicMvid = true, Timestamp = 0,
        });
        byte[] rewrittenAssembly = output.ToArray();
        byte[] rewrittenSymbols = outputSymbols.ToArray();
        using var checkAssembly = new MemoryStream(rewrittenAssembly, writable: false);
        using var checkSymbols = new MemoryStream(rewrittenSymbols, writable: false);
        using ModuleDefinition verified = Read(checkAssembly, checkSymbols, resolver);
        preservation.Verify(verified, rewrittenSymbols);
        if (Fingerprint(verified) != before)
        {
            throw new InvalidDataException("Metadata ordering changed members, IL or portable PDB information.");
        }
        if (Reorder(verified.Types, ranks))
        {
            throw new InvalidDataException("Written metadata does not preserve the requested type order.");
        }
        return new Result(rewrittenAssembly, rewrittenSymbols, true, AllTypes(verified.Types).Count(), sequencePoints);
    }

    private static ModuleDefinition Read(Stream assembly, Stream symbols, IAssemblyResolver resolver)
    {
        ModuleDefinition module = ModuleDefinition.ReadModule(assembly, new ReaderParameters
        {
            ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = resolver,
            ReadSymbols = true, SymbolReaderProvider = new PortablePdbReaderProvider(), SymbolStream = symbols,
            ThrowIfSymbolsAreNotMatching = true,
        });
        if (!module.HasSymbols)
        {
            module.Dispose();
            throw new InvalidDataException("The assembly has no matching portable PDB.");
        }
        return module;
    }

    private static bool Reorder(Mono.Collections.Generic.Collection<TypeDefinition> types, IReadOnlyDictionary<string, int> ranks)
    {
        // 未登记的新类型保持编译器当前相对顺序，追加到已登记类型之后；嵌套类型只在所属类型内重排。
        TypeDefinition[] ordered = types.OrderBy(t => t.Name == "<Module>" ? -1 : ranks.GetValueOrDefault(Name(t), int.MaxValue)).ToArray();
        bool changed = !types.SequenceEqual(ordered);
        if (changed)
        {
            types.Clear();
            foreach (TypeDefinition type in ordered)
            {
                types.Add(type);
            }
        }
        foreach (TypeDefinition type in types)
        {
            changed |= Reorder(type.NestedTypes, ranks);
        }
        return changed;
    }

    private static string Name(TypeReference type) => type.FullName.Replace('/', '+');

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (TypeDefinition type in roots)
        {
            yield return type;
            foreach (TypeDefinition child in AllTypes(type.NestedTypes))
            {
                yield return child;
            }
        }
    }

    // 对序列化前后作独立读回。忽略 metadata token/类型顺序/MVID，仅这些值由重排合法改变。
    private static string Fingerprint(ModuleDefinition module)
    {
        var text = new StringBuilder();
        void Add(object? value) => text.Append(value?.ToString() ?? "<null>").Append('\n');
        void Attributes(ICustomAttributeProvider provider)
        {
            foreach (CustomAttribute attribute in provider.CustomAttributes)
            {
                Add(attribute.Constructor.FullName);
                Add(Convert.ToHexString(attribute.GetBlob()));
            }
        }
        void Debug(CustomDebugInformation information)
        {
            Add(information.Identifier);
            Add(information.Kind);
            switch (information)
            {
                case BinaryCustomDebugInformation binary: Add(Convert.ToHexString(binary.Data)); break;
                case SourceLinkDebugInformation sourceLink: Add(sourceLink.Content); break;
                case EmbeddedSourceDebugInformation source: Add(Convert.ToHexString(source.Content)); Add(source.Compress); break;
                case AsyncMethodBodyDebugInformation asyncBody:
                    AddOffset(asyncBody.CatchHandler);
                    Add(asyncBody.Yields.Count);
                    foreach (InstructionOffset offset in asyncBody.Yields) { AddOffset(offset); }
                    Add(asyncBody.Resumes.Count);
                    foreach (InstructionOffset offset in asyncBody.Resumes) { AddOffset(offset); }
                    Add(asyncBody.ResumeMethods.Count);
                    foreach (MethodDefinition method in asyncBody.ResumeMethods) { Add(method.FullName); }
                    break;
                case StateMachineScopeDebugInformation stateMachine:
                    Add(stateMachine.Scopes.Count);
                    foreach (StateMachineScope scope in stateMachine.Scopes) { AddOffset(scope.Start); AddOffset(scope.End); }
                    break;
                default:
                    throw new InvalidDataException("Unsupported custom debugging information: " + information.GetType().FullName);
            }
        }
        void AddOffset(InstructionOffset offset) => Add(offset.IsEndOfMethod ? "end" : offset.Offset);
        void CustomDebug(ICustomDebugInformationProvider provider)
        {
            Add(provider.CustomDebugInformations.Count);
            foreach (CustomDebugInformation information in provider.CustomDebugInformations) { Debug(information); }
        }
        Add(module.Assembly.Name.FullName); Add(module.Kind); Add(module.RuntimeVersion); Add(module.Architecture); Add(module.Attributes);
        Attributes(module); Attributes(module.Assembly);
        foreach (CustomDebugInformation debug in module.CustomDebugInformations) { Debug(debug); }
        foreach (AssemblyNameReference reference in module.AssemblyReferences) { Add(reference.FullName); }
        foreach (Resource resource in module.Resources)
        {
            Add(resource.Name); Add(resource.Attributes); Add(resource.ResourceType);
            if (resource is EmbeddedResource embedded) { Add(Convert.ToHexString(embedded.GetResourceData())); }
        }
        foreach (TypeDefinition type in AllTypes(module.Types).OrderBy(Name, StringComparer.Ordinal))
        {
            Add(Name(type)); Add(type.Attributes); Add(type.BaseType?.FullName); Add(type.PackingSize); Add(type.ClassSize); Attributes(type); CustomDebug(type);
            foreach (InterfaceImplementation implementation in type.Interfaces) { Add(implementation.InterfaceType.FullName); Attributes(implementation); }
            foreach (FieldDefinition field in type.Fields)
            {
                Add(field.FullName); Add(field.Attributes); Add(field.Constant); Add(field.Offset); Add(Convert.ToHexString(field.InitialValue)); Attributes(field);
            }
            foreach (PropertyDefinition property in type.Properties)
            {
                Add(property.FullName); Add(property.Attributes); Add(property.Constant); Add(property.GetMethod?.FullName); Add(property.SetMethod?.FullName); Attributes(property);
            }
            foreach (EventDefinition @event in type.Events)
            {
                Add(@event.FullName); Add(@event.Attributes); Add(@event.AddMethod?.FullName); Add(@event.RemoveMethod?.FullName); Add(@event.InvokeMethod?.FullName); Attributes(@event);
            }
            foreach (MethodDefinition method in type.Methods)
            {
                Add(method.FullName); Add(method.Attributes); Add(method.ImplAttributes); Add(method.CallingConvention); Attributes(method);
                foreach (ParameterDefinition parameter in method.Parameters) { Add(parameter.Name); Add(parameter.ParameterType.FullName); Add(parameter.Attributes); Add(parameter.Constant); Attributes(parameter); }
                foreach (MethodReference @override in method.Overrides) { Add(@override.FullName); }
                if (method.HasBody)
                {
                    MethodBody body = method.Body;
                    Add(body.InitLocals); Add(body.MaxStackSize);
                    var positions = body.Instructions.Select((instruction, index) => (instruction, index)).ToDictionary(p => p.instruction, p => p.index);
                    string Position(Instruction? instruction) => instruction == null ? "end" : positions[instruction].ToString(CultureInfo.InvariantCulture);
                    foreach (VariableDefinition variable in body.Variables) { Add(variable.VariableType.FullName); }
                    foreach (Instruction instruction in body.Instructions)
                    {
                        Add(instruction.OpCode.Code);
                        Add(instruction.Operand switch
                        {
                            Instruction target => Position(target),
                            Instruction[] targets => string.Join(",", targets.Select(Position)),
                            MemberReference member => member.FullName,
                            ParameterDefinition parameter => "arg:" + parameter.Index,
                            VariableDefinition variable => "local:" + variable.Index,
                            float value => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture),
                            double value => BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture),
                            _ => instruction.Operand,
                        });
                    }
                    foreach (ExceptionHandler handler in body.ExceptionHandlers)
                    {
                        Add(handler.HandlerType); Add(handler.CatchType?.FullName); Add(Position(handler.TryStart)); Add(Position(handler.TryEnd));
                        Add(Position(handler.HandlerStart)); Add(Position(handler.HandlerEnd)); Add(Position(handler.FilterStart));
                    }
                }
                foreach (SequencePoint point in method.DebugInformation.SequencePoints)
                {
                    Add(point.Offset); Add(point.StartLine); Add(point.StartColumn); Add(point.EndLine); Add(point.EndColumn);
                    Add(point.Document.Url); Add(point.Document.Language); Add(point.Document.HashAlgorithm); Add(Convert.ToHexString(point.Document.Hash));
                    foreach (CustomDebugInformation debug in point.Document.CustomDebugInformations) { Debug(debug); }
                }
                Add(method.DebugInformation.StateMachineKickOffMethod?.FullName);
                AddScope(method.DebugInformation.Scope);
                foreach (CustomDebugInformation debug in method.CustomDebugInformations) { Debug(debug); }
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        void AddScope(ScopeDebugInformation? scope)
        {
            Add(scope == null ? "no-scope" : "scope");
            if (scope == null) { return; }
            AddOffset(scope.Start); AddOffset(scope.End);
            CustomDebug(scope);
            AddImports(scope.Import);
            Add(scope.Variables.Count);
            foreach (VariableDebugInformation variable in scope.Variables)
            {
                Add(variable.Index); Add(variable.Name); Add(variable.Attributes); CustomDebug(variable);
            }
            Add(scope.Constants.Count);
            foreach (ConstantDebugInformation constant in scope.Constants)
            {
                Add(constant.Name); Add(constant.ConstantType.FullName); Add(constant.Value); CustomDebug(constant);
            }
            Add(scope.Scopes.Count);
            foreach (ScopeDebugInformation child in scope.Scopes) { AddScope(child); }
        }

        void AddImports(ImportDebugInformation? import)
        {
            // 导入作用域可被多个方法共用；每条父链按内容比较，不把行号或对象共享方式当作语义。
            var visited = new HashSet<ImportDebugInformation>();
            while (import != null)
            {
                if (!visited.Add(import)) { throw new InvalidDataException("Cyclic portable PDB import scope."); }
                Add("import");
                CustomDebug(import);
                Add(import.Targets.Count);
                foreach (ImportTarget target in import.Targets)
                {
                    Add(target.Kind); Add(target.Namespace); Add(target.Type?.FullName);
                    Add(target.AssemblyReference?.FullName); Add(target.Alias);
                }
                import = import.Parent;
            }
            Add("end-imports");
        }
    }
}
