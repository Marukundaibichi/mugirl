using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>一条 <c>stfld</c>/<c>stsfld</c> 及其写入的抽象字符串值。</summary>
internal sealed record FieldStore(MethodDefinition Method, int Offset, FieldReference Field, StringSet Value)
{
    public string Location => $"{CecilNames.Type(Method.DeclaringType)}::{Method.Name}+IL_{Offset:X4}";
}
