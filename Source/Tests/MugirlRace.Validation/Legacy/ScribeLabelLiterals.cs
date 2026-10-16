using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 一个程序集（游戏 Assembly-CSharp 与依赖）在 Scribe 调用中直接写成字面量的标签。存档观测扫描用它排除原版与依赖写进本 Mod 元素的
/// 标签：<c>Mugirl.Features.Restraints.RestraintApparel</c> 元素下的 <c>def</c>、<c>id</c>、<c>health</c> 由 <c>Thing.ExposeData</c> 写，原版组件的标签
/// 由它们的 <c>PostExposeData</c> 写，都不属于本 Mod。
/// </summary>
internal static class ScribeLabelLiterals
{
    /// <summary><paramref name="module"/> 中每个 Scribe 调用的第一个字符串实参（标签）为 <c>ldstr</c> 时的字面量。</summary>
    public static IEnumerable<string> Of(ModuleDefinition module)
    {
        foreach (MethodDefinition method in CecilNames.AllTypes(module).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) || instruction.Operand is not MethodReference callee || !ScribeSchema.IsScribeCall(callee))
                {
                    continue;
                }

                int label = -1;
                for (int i = 0; i < callee.Parameters.Count; i++)
                {
                    if (callee.Parameters[i].ParameterType.FullName == "System.String")
                    {
                        label = i;
                        break;
                    }
                }

                if (label >= 0 && IlArguments.Producer(method, instruction, label + (callee.HasThis ? 1 : 0)) is { OpCode.Code: Code.Ldstr, Operand: string literal })
                {
                    yield return literal;
                }
            }
        }
    }
}
