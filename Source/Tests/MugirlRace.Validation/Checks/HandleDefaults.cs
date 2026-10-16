using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// 列表项句柄字段的构造函数常量（<c>compClass = typeof(X)</c> 取 X 的短名与完整名，<c>field = "text"</c> 取文本），读自 IL，不运行代码。
/// 沿基类向上找；Mod 类型读被测程序集，游戏类型经元数据解析器读游戏程序集。
/// </summary>
internal sealed class HandleDefaults
{
    private readonly MetadataWorld world;
    private readonly Dictionary<(Type, string), IReadOnlyList<string>> cache = [];

    public HandleDefaults(MetadataWorld world) => this.world = world;

    public IReadOnlyList<string> Of(Type type, string field)
    {
        if (!cache.TryGetValue((type, field), out IReadOnlyList<string>? values))
        {
            values = Read(type, field);
            cache[(type, field)] = values;
        }

        return values;
    }

    private List<string> Read(Type type, string field)
    {
        var values = new List<string>();
        for (TypeDefinition? current = Definition(type); current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            foreach (MethodDefinition ctor in current.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
            {
                foreach (Instruction instruction in ctor.Body.Instructions)
                {
                    if (instruction.OpCode.Code != Code.Stfld || instruction.Operand is not FieldReference stored || !stored.Name.Equals(field, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    switch (instruction.Previous)
                    {
                        case { OpCode.Code: Code.Ldstr, Operand: string text }:
                            values.Add(text);
                            break;
                        case { OpCode.Code: Code.Call, Operand: MethodReference { Name: "GetTypeFromHandle" }, Previous: { OpCode.Code: Code.Ldtoken, Operand: TypeReference token } }:
                            values.Add(token.Name);
                            values.Add(CecilNames.Type(token));
                            break;
                    }

                    if (instruction.Previous is { } previous && IlArguments.IntConstant(previous) is { } number)
                    {
                        values.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }

            if (values.Count > 0)
            {
                break;
            }
        }

        return values;
    }

    private TypeDefinition? Definition(Type type)
    {
        string name = (type.FullName ?? type.Name).Replace('+', '/');
        if (type.Assembly.GetName().Name == world.Mod.Assembly.Name.Name)
        {
            return world.Mod.GetType(name);
        }

        return world.TryResolve(type.Assembly.GetName().Name ?? "")?.MainModule.GetType(name);
    }
}
