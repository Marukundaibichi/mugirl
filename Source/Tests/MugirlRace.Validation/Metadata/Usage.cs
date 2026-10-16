using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>被测模块对某个类型或成员的一次引用。</summary>
/// <param name="Container">元数据或代码含有这次引用的类型。</param>
/// <param name="Method">引用位于方法签名或方法体时，该方法。</param>
/// <param name="Referenced"><see cref="TypeReference"/>、<see cref="MethodReference"/> 或 <see cref="FieldReference"/>。</param>
internal sealed record Usage(TypeDefinition Container, MethodDefinition? Method, MemberReference Referenced)
{
    /// <summary><c>Type::Method</c> 或类型名，用于消息。</summary>
    public string Where => Method is null ? CecilNames.Type(Container) : CecilNames.Type(Container) + "::" + Method.Name;
}
