using System.Reflection;
using System.Reflection.Emit;

namespace Mugirl.GameValidation.Core;

/// <summary>从实际组件的序列化调用图读取 Scribe 标签；只读 IL，不构造组件、不运行其代码。</summary>
internal sealed class CurrentScribeRoots
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());
    private readonly Assembly assembly;
    private readonly Dictionary<MethodBase, List<Instruction>> bodies = [];

    public CurrentScribeRoots(Assembly assembly) => this.assembly = assembly;

    public Dictionary<string, HashSet<string>> Labels { get; } = new(StringComparer.Ordinal);
    public List<string> Problems { get; } = [];

    public IEnumerable<MethodInfo> DirectCalls(MethodBase method)
        => Body(method).Select(i => i.Operand).OfType<MethodInfo>();

    public IEnumerable<MethodBase> DirectInvocations(MethodBase method)
        => Body(method).Select(i => i.Operand).OfType<MethodBase>();

    public void Discover(IEnumerable<Type> componentTypes)
    {
        foreach (Type type in componentTypes.Distinct())
        {
            MethodInfo? root = type.GetMethod("PostExposeData", Members, Type.EmptyTypes);
            if (root is null)
            {
                Problems.Add(type.FullName + " has no PostExposeData entry point.");
                continue;
            }

            var labels = new HashSet<string>(StringComparer.Ordinal);
            Labels[type.FullName!] = labels;
            Scan(root, type, labels, new Dictionary<int, string>(), new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private void Scan(MethodInfo method, Type concrete, HashSet<string> labels, Dictionary<int, string> arguments, HashSet<string> visited)
    {
        string key = method.Module.ModuleVersionId + ":" + method.MetadataToken + ":" + string.Join("|", arguments.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
        if (!visited.Add(key))
        {
            return;
        }

        if (visited.Count > 4096)
        {
            Problems.Add(concrete.FullName + " serialization call graph exceeded the checked bound.");
            return;
        }

        List<Instruction> body = Body(method);
        for (int i = 0; i < body.Count; i++)
        {
            Instruction instruction = body[i];
            if (instruction.Operand is not MethodInfo callee)
            {
                continue;
            }

            ParameterInfo[] parameters = callee.GetParameters();
            bool scribe = callee.DeclaringType?.Namespace == "Verse" && callee.DeclaringType.Name.StartsWith("Scribe_", StringComparison.Ordinal) && callee.Name == "Look";
            bool enter = callee.DeclaringType?.FullName == "Verse.Scribe" && callee.Name == "EnterNode";
            if (scribe || enter)
            {
                int label = enter ? 0 : Array.FindIndex(parameters, p => p.ParameterType == typeof(string) && p.Name == "label");
                int producer = Producer(method, body, i, label + (callee.IsStatic ? 0 : 1));
                string? text = StringValue(method, body, producer, concrete, arguments, 0);
                if (text is null || text.Length == 0)
                {
                    Problems.Add(concrete.FullName + ": unresolved Scribe root label at " + method.DeclaringType?.FullName + "." + method.Name + "+IL_" + instruction.Offset.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    labels.Add(text);
                }

                continue;
            }

            // 深层对象由 Scribe 自己调用，不沿其泛型类型展开；普通委托/辅助方法仍在当前 XML 层。
            if (callee.DeclaringType?.Assembly != assembly
                && !(callee.Name == "PostExposeData" && callee.DeclaringType?.IsAssignableFrom(concrete) == true))
            {
                continue;
            }

            Dictionary<int, string> passed = [];
            for (int parameter = 0; parameter < parameters.Length; parameter++)
            {
                if (parameters[parameter].ParameterType == typeof(string)
                    && StringValue(method, body, Producer(method, body, i, parameter + (callee.IsStatic ? 0 : 1)), concrete, arguments, 0) is { } text)
                {
                    passed[parameter + (callee.IsStatic ? 0 : 1)] = text;
                }
            }

            Scan(callee, concrete, labels, passed, visited);
        }
    }

    private string? StringValue(MethodInfo method, List<Instruction> body, int position, Type concrete, Dictionary<int, string> arguments, int depth)
    {
        if (position < 0 || depth > 12)
        {
            return null;
        }

        Instruction instruction = body[position];
        if (instruction.Code == OpCodes.Ldstr)
        {
            return instruction.Operand as string;
        }

        if (ArgumentIndex(instruction) is { } argument)
        {
            return arguments.GetValueOrDefault(argument);
        }

        if (LocalIndex(instruction, store: false) is { } local)
        {
            int[] stores = [.. body.Select((item, index) => (item, index)).Where(p => LocalIndex(p.item, store: true) == local).Select(p => p.index)];
            return stores.Length == 1 && stores[0] < position
                ? StringValue(method, body, StackProducer(method, body, stores[0], 0), concrete, arguments, depth + 1)
                : null;
        }

        if (instruction.Operand is MethodInfo getter && getter.ReturnType == typeof(string) && getter.GetParameters().Length == 0)
        {
            if (getter.IsVirtual && getter.DeclaringType?.IsAssignableFrom(concrete) == true
                && ArgumentIndex(body.ElementAtOrDefault(Producer(method, body, position, 0))!) == 0)
            {
                getter = concrete.GetMethod(getter.Name, Members, Type.EmptyTypes) ?? getter;
            }

            List<Instruction> code = Body(getter);
            Instruction[] meaningful = [.. code.Where(i => i.Code != OpCodes.Nop)];
            if (meaningful.Length == 2 && meaningful[0].Code == OpCodes.Ldstr && meaningful[1].Code == OpCodes.Ret)
            {
                return (string)meaningful[0].Operand!;
            }
        }

        return null;
    }

    private List<Instruction> Body(MethodBase method)
    {
        if (bodies.TryGetValue(method, out List<Instruction>? existing))
        {
            return existing;
        }

        List<Instruction> result = [];
        bodies[method] = result;
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int start = offset;
            short value = bytes[offset++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | bytes[offset++]));
            }

            OpCode code = Opcodes[value];
            object? operand = null;
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
            if (code.OperandType == OperandType.InlineString)
            {
                operand = method.Module.ResolveString(BitConverter.ToInt32(bytes, offset));
            }
            else if (code.OperandType == OperandType.InlineMethod)
            {
                operand = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            else if (code.OperandType is OperandType.InlineVar or OperandType.ShortInlineVar)
            {
                operand = size == 1 ? bytes[offset] : BitConverter.ToUInt16(bytes, offset);
            }
            else if (code.OperandType is OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget)
            {
                operand = offset + size + (size == 1 ? (sbyte)bytes[offset] : BitConverter.ToInt32(bytes, offset));
            }
            else if (code.OperandType == OperandType.InlineSwitch)
            {
                int end = offset + size;
                operand = Enumerable.Range(0, BitConverter.ToInt32(bytes, offset)).Select(i => end + BitConverter.ToInt32(bytes, offset + 4 + i * 4)).ToArray();
            }

            offset += size;
            result.Add(new Instruction(start, code, operand));
        }

        return result;
    }

    private static int Producer(MethodInfo method, List<Instruction> body, int callIndex, int argument)
    {
        if (callIndex < 0 || body[callIndex].Operand is not MethodBase callee || argument < 0)
        {
            return -1;
        }

        int count = callee.GetParameters().Length + (callee.IsStatic || body[callIndex].Code == OpCodes.Newobj ? 0 : 1);
        return argument >= count ? -1 : StackProducer(method, body, callIndex, count - 1 - argument);
    }

    private static int StackProducer(MethodInfo method, List<Instruction> body, int before, int depth)
    {
        var incoming = new Dictionary<int, List<int>>();
        Dictionary<int, int> positions = body.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(x => x.Offset, x => x.index);
        for (int index = 0; index < body.Count; index++)
        {
            Instruction instruction = body[index];
            if (index + 1 < body.Count && instruction.Code.FlowControl is not FlowControl.Branch and not FlowControl.Return and not FlowControl.Throw)
            {
                AddEdge(index + 1, index);
            }
            if (instruction.Code.OperandType is OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget && instruction.Operand is int target)
            {
                AddEdge(positions[target], index);
            }
            else if (instruction.Operand is int[] targets)
            {
                foreach (int offset in targets)
                {
                    AddEdge(positions[offset], index);
                }
            }
        }

        var pending = new Stack<(int Position, int Depth)>();
        var seen = new HashSet<(int Position, int Depth)>();
        pending.Push((before, depth));
        int producer = -1;
        while (pending.TryPop(out (int Position, int Depth) state))
        {
            if (!seen.Add(state))
            {
                continue;
            }
            if (seen.Count > 4096 || !incoming.TryGetValue(state.Position, out List<int>? predecessors))
            {
                return -1;
            }
            foreach (int index in predecessors)
            {
                Instruction instruction = body[index];
                int pushes = Pushes(instruction);
                int pops = Pops(instruction, method);
                if (pushes < 0 || pops < 0 || instruction.Code == OpCodes.Leave || instruction.Code == OpCodes.Leave_S)
                {
                    return -1;
                }
                if (state.Depth < pushes)
                {
                    if (producer >= 0 && producer != index)
                    {
                        return -1;
                    }
                    producer = index;
                }
                else
                {
                    pending.Push((index, state.Depth - pushes + pops));
                }
            }
        }
        return producer;

        void AddEdge(int destination, int source)
        {
            if (!incoming.TryGetValue(destination, out List<int>? list))
            {
                incoming[destination] = list = [];
            }
            list.Add(source);
        }
    }

    private static int Pops(Instruction instruction, MethodInfo method)
        => instruction.Operand is MethodBase callee && instruction.Code is var code && (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
            ? callee.GetParameters().Length + (callee.IsStatic || code == OpCodes.Newobj ? 0 : 1)
            : instruction.Code.StackBehaviourPop switch
            {
                StackBehaviour.Pop0 => 0,
                StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
                StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
                StackBehaviour.Varpop when instruction.Code == OpCodes.Ret => method.ReturnType == typeof(void) ? 0 : 1,
                _ => -1,
            };

    private static int Pushes(Instruction instruction)
        => instruction.Code == OpCodes.Newobj ? 1 : instruction.Operand is MethodInfo callee && (instruction.Code == OpCodes.Call || instruction.Code == OpCodes.Callvirt)
            ? callee.ReturnType == typeof(void) ? 0 : 1
            : instruction.Code.StackBehaviourPush switch
            {
                StackBehaviour.Push0 => 0,
                StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
                StackBehaviour.Push1_push1 => 2,
                _ => -1,
            };

    private static int? ArgumentIndex(Instruction? i) => i?.Code.Name switch
    {
        "ldarg.0" => 0, "ldarg.1" => 1, "ldarg.2" => 2, "ldarg.3" => 3,
        "ldarg" or "ldarg.s" => Convert.ToInt32(i.Operand, System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static int? LocalIndex(Instruction i, bool store)
    {
        string prefix = store ? "stloc" : "ldloc";
        string? name = i.Code.Name;
        if (name == prefix || name == prefix + ".s")
        {
            return Convert.ToInt32(i.Operand, System.Globalization.CultureInfo.InvariantCulture);
        }

        return name is not null && name.StartsWith(prefix + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(prefix.Length + 1), out int index) ? index : null;
    }

    private sealed record Instruction(int Offset, OpCode Code, object? Operand);
}
