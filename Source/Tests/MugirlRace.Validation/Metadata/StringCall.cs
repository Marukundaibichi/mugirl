using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>一条调用指令及其实参的抽象字符串值。</summary>
/// <param name="Caller">包含调用的方法。</param>
/// <param name="Offset">调用的 IL 偏移。</param>
/// <param name="Callee">被调用方法（按引用）。</param>
/// <param name="Arguments">按顺序的实参值；实例调用（非 <c>newobj</c>）的第 0 项是 <c>this</c>。</param>
/// <param name="IsNewObject">指令为 <c>newobj</c>。</param>
internal sealed record StringCall(MethodDefinition Caller, int Offset, MethodReference Callee, IReadOnlyList<StringSet> Arguments, bool IsNewObject)
{
    /// <summary>第一个 <see cref="string"/> 参数的值（本程序检查的所有查找 API 的键、标签、路径或 defName）。</summary>
    public StringSet? FirstStringArgument
    {
        get
        {
            int offset = Callee.HasThis && !IsNewObject ? 1 : 0;
            for (int i = 0; i < Callee.Parameters.Count; i++)
            {
                if (Callee.Parameters[i].ParameterType.FullName == "System.String")
                {
                    return Arguments[offset + i];
                }
            }

            return null;
        }
    }

    /// <summary><c>Type::Method+IL_xxxx</c>，用于消息与例外清单。</summary>
    public string Location => $"{CecilNames.Type(Caller.DeclaringType)}::{Caller.Name}+IL_{Offset:X4}";
}
