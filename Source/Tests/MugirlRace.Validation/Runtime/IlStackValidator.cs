using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace MugirlRace.Validation.Runtime;

/// <summary>
/// Transpiler 输出的结构检查：每个分支目标都是存在的标签，异常块保留，求值栈在每条路径上每条指令只有一个深度、从不下溢，
/// 且在 <c>ret</c>/<c>throw</c> 处为空（从入口和每个处理器做数据流：catch/filter 起始深度 1，finally/fault 为 0）。
/// </summary>
internal static class IlStackValidator
{
    /// <summary>发现的第一个问题；IL 结构正确时为 <see langword="null"/>。</summary>
    public static string? Validate(IReadOnlyList<CodeInstruction> code, MethodBase original)
    {
        var labels = new Dictionary<Label, int>();
        for (int i = 0; i < code.Count; i++)
        {
            foreach (Label label in code[i].labels)
            {
                if (!labels.TryAdd(label, i))
                {
                    return $"Label marks two instructions ({labels[label]} and {i}).";
                }
            }
        }

        foreach (CodeInstruction instruction in code)
        {
            IEnumerable<Label> targets = instruction.operand switch
            {
                Label label => [label],
                Label[] many => many,
                _ => [],
            };
            if (targets.Any(label => !labels.ContainsKey(label)))
            {
                return $"{instruction.opcode} branches to a label that marks no instruction.";
            }
        }

        var depths = new int?[code.Count];
        var pending = new Queue<int>();
        string? problem = null;
        void Enqueue(int index, int depth)
        {
            if (index >= code.Count)
            {
                problem ??= "Control flow falls off the end of the method.";
                return;
            }

            if (depths[index] is { } known)
            {
                if (known != depth)
                {
                    problem ??= $"Stack depth differs between paths at instruction {index} ({known} vs {depth}).";
                }

                return;
            }

            depths[index] = depth;
            pending.Enqueue(index);
        }

        Enqueue(0, 0);
        for (int i = 0; i < code.Count; i++)
        {
            foreach (ExceptionBlock block in code[i].blocks)
            {
                switch (block.blockType)
                {
                    case ExceptionBlockType.BeginCatchBlock:
                    case ExceptionBlockType.BeginExceptFilterBlock:
                        Enqueue(i, 1);
                        break;
                    case ExceptionBlockType.BeginFinallyBlock:
                    case ExceptionBlockType.BeginFaultBlock:
                        Enqueue(i, 0);
                        break;
                }
            }
        }

        Type returnType = original is MethodInfo method ? method.ReturnType : typeof(void);
        while (pending.Count > 0 && problem is null)
        {
            int index = pending.Dequeue();
            CodeInstruction instruction = code[index];
            OpCode opcode = instruction.opcode;
            int pop = StackCount(opcode.StackBehaviourPop);
            int push = StackCount(opcode.StackBehaviourPush);
            if (instruction.operand is MethodBase called && (opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj))
            {
                pop = called.GetParameters().Length + (opcode != OpCodes.Newobj && !called.IsStatic ? 1 : 0);
                push = opcode == OpCodes.Newobj || (called is MethodInfo info && info.ReturnType != typeof(void)) ? 1 : 0;
            }
            else if (opcode == OpCodes.Ret)
            {
                pop = returnType == typeof(void) ? 0 : 1;
            }

            if (pop < 0 || push < 0)
            {
                return $"Unsupported stack behaviour of {opcode} at {index}.";
            }

            int depth = depths[index]!.Value;
            if (depth < pop)
            {
                return $"Stack underflow at instruction {index} ({opcode}).";
            }

            depth = depth - pop + push;
            if (opcode == OpCodes.Leave || opcode == OpCodes.Leave_S)
            {
                depth = 0;
            }

            if (opcode.FlowControl is FlowControl.Return or FlowControl.Throw)
            {
                if (depth != 0 && opcode != OpCodes.Endfinally && opcode != OpCodes.Endfilter)
                {
                    return $"Stack not empty at {opcode} (instruction {index}).";
                }

                continue;
            }

            if (instruction.operand is Label target)
            {
                Enqueue(labels[target], depth);
            }
            else if (instruction.operand is Label[] targets)
            {
                foreach (Label branch in targets)
                {
                    Enqueue(labels[branch], depth);
                }
            }

            if (opcode.FlowControl != FlowControl.Branch)
            {
                Enqueue(index + 1, depth);
            }
        }

        return problem;
    }

    private static int StackCount(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Pop0 or StackBehaviour.Push0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref or StackBehaviour.Push1 or StackBehaviour.Pushi
            or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi
            or StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or StackBehaviour.Popref_popi_popi
            or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8
            or StackBehaviour.Popref_popi_popref => 3,
        _ => -1,
    };
}
