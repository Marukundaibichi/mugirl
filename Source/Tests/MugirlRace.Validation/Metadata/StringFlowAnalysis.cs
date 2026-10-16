using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 方法 IL 上字符串字面量的前向数据流分析（栈、局部变量、分支、异常处理）。回答“这个调用实参可能是哪些字面量”：
/// <c>cond ? "A" : "B"</c> 得到两个字面量；先存进局部变量的值仍然已知；<c>"Prefix" + "Suffix"</c> 会被折叠；在静态构造函数
/// 中以字面量初始化的 <c>static readonly string</c> 字段算作该字面量，另一程序集声明的也一样（只读元数据，构造函数从不运行）。
/// 其他一切（参数、实例字段、运行时拼接）都是 <see cref="StringSet.Unknown"/>。
/// </summary>
internal sealed class StringFlowAnalysis
{
    private const int MaxPasses = 64;
    private readonly ModuleDefinition module;
    private readonly Dictionary<string, StringSet> staticStrings;
    private readonly HashSet<string> scannedExternalTypes = new(StringComparer.Ordinal);

    // 被分析方法中被重新赋值或取地址的参数：对它们的 ldarg 不一定是调用方传入的值。
    private HashSet<int> reassignedParameters = [];

    public StringFlowAnalysis(ModuleDefinition module)
    {
        this.module = module;
        staticStrings = new Dictionary<string, StringSet>(StringComparer.Ordinal);
        foreach (TypeDefinition type in CecilNames.AllTypes(module))
        {
            AddStaticStringConstants(type, staticStrings);
        }
    }

    /// <summary><paramref name="method"/> 的调用与字段写入，以及其字符串操作数的抽象值。</summary>
    public MethodStringFlow Analyze(MethodDefinition method)
    {
        var calls = new List<StringCall>();
        var stores = new List<FieldStore>();
        if (!method.HasBody)
        {
            return new MethodStringFlow(method, calls, stores, Traceable: true);
        }

        IList<Instruction> code = method.Body.Instructions;
        reassignedParameters = code
            .Where(i => i.OpCode.Code is Code.Starg or Code.Starg_S or Code.Ldarga or Code.Ldarga_S && i.Operand is ParameterDefinition)
            .Select(i => ((ParameterDefinition)i.Operand).Index)
            .ToHashSet();
        var index = new Dictionary<Instruction, int>(code.Count);
        for (int i = 0; i < code.Count; i++)
        {
            index[code[i]] = i;
        }

        var states = new Frame?[code.Count];
        var work = new Queue<int>();
        bool traceable = true;

        void Merge(int target, Frame frame)
        {
            Frame? existing = states[target];
            if (existing is null)
            {
                states[target] = frame;
                work.Enqueue(target);
                return;
            }

            if (existing.Stack.Length != frame.Stack.Length)
            {
                traceable = false;
                return;
            }

            if (existing.MergeFrom(frame))
            {
                work.Enqueue(target);
            }
        }

        Merge(0, Frame.Empty(method.Body.Variables.Count));
        int budget = MaxPasses * code.Count;
        while (work.Count > 0 && traceable && budget-- > 0)
        {
            int i = work.Dequeue();
            Frame state = states[i]!;
            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            {
                if (Contains(handler, code[i], index))
                {
                    StringSet[] handlerStack = handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter ? [StringSet.Unknown] : [];
                    Merge(index[handler.HandlerStart], new Frame(handlerStack, state.CopyLocals()));
                    if (handler.FilterStart is not null)
                    {
                        Merge(index[handler.FilterStart], new Frame([StringSet.Unknown], state.CopyLocals()));
                    }
                }
            }

            Frame? after = Step(method, code[i], state, null, null);
            if (after is null)
            {
                traceable = false;
                break;
            }

            foreach (int successor in Successors(code, i, index))
            {
                Merge(successor, code[i].OpCode.Code is Code.Leave or Code.Leave_S ? new Frame([], after.CopyLocals()) : after.Clone());
            }
        }

        if (!traceable || work.Count > 0)
        {
            // 栈不平衡或不收敛的 IL（Roslyn 不会生成）：全部报告为未知。
            return new MethodStringFlow(method, AllCallsUnknown(method), [], Traceable: false);
        }

        for (int i = 0; i < code.Count; i++)
        {
            if (states[i] is { } state)
            {
                Step(method, code[i], state.Clone(), calls, stores);
            }
        }

        return new MethodStringFlow(method, calls, stores, Traceable: true);
    }

    // 把一条指令应用到帧的副本上；传入列表时记录调用与写入。
    private Frame? Step(MethodDefinition method, Instruction instruction, Frame frame, List<StringCall>? calls, List<FieldStore>? stores)
    {
        var stack = new List<StringSet>(frame.Stack);
        StringSet[] locals = frame.CopyLocals();
        OpCode opCode = instruction.OpCode;

        StringSet Pop()
        {
            StringSet value = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        switch (opCode.Code)
        {
            case Code.Ldtoken:
                // typeof(T) 是 "ldtoken T; call Type.GetTypeFromHandle"，值原样传递。
                stack.Add(instruction.Operand is TypeReference token ? StringSet.OfType(CecilNames.Type(token)) : StringSet.Unknown);
                return new Frame([.. stack], locals);
            case Code.Ldstr:
                stack.Add(StringSet.Of((string)instruction.Operand));
                return new Frame([.. stack], locals);
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            case Code.Ldarg_S:
            case Code.Ldarg:
                stack.Add(ParameterPosition(method, instruction) is int position
                    ? (reassignedParameters.Contains(position) ? StringSet.Unknown : StringSet.FromParameter(position))
                    : IsThis(method, instruction) ? StringSet.This : StringSet.Unknown);
                return new Frame([.. stack], locals);
            case Code.Ldfld:
                // this 的字符串字段记为模板洞（存档标签可能来自它）；其他实例字段是未知值。
                if (stack.Count == 0)
                {
                    return null;
                }

                FieldReference loaded = (FieldReference)instruction.Operand;
                StringSet owner = Pop();
                stack.Add(owner.IsThis && loaded.FieldType.FullName == "System.String" ? StringSet.ThisField(CecilNames.MemberShort(loaded)) : StringSet.Unknown);
                return new Frame([.. stack], locals);
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            case Code.Ldloc_S:
            case Code.Ldloc:
                stack.Add(locals[LocalIndex(instruction)]);
                return new Frame([.. stack], locals);
            case Code.Stloc_0:
            case Code.Stloc_1:
            case Code.Stloc_2:
            case Code.Stloc_3:
            case Code.Stloc_S:
            case Code.Stloc:
                if (stack.Count == 0)
                {
                    return null;
                }

                locals[LocalIndex(instruction)] = Pop();
                return new Frame([.. stack], locals);
            case Code.Ldloca_S:
            case Code.Ldloca:
                // 地址逃逸：局部变量可能经由它被写入。
                locals[LocalIndex(instruction)] = StringSet.Unknown;
                stack.Add(StringSet.Unknown);
                return new Frame([.. stack], locals);
            case Code.Dup:
                if (stack.Count == 0)
                {
                    return null;
                }

                stack.Add(stack[^1]);
                return new Frame([.. stack], locals);
            case Code.Ldsfld:
                stack.Add(StaticString((FieldReference)instruction.Operand));
                return new Frame([.. stack], locals);
            case Code.Castclass:
            case Code.Isinst:
                // 字符串经过类型转换仍是同一个值。
                if (stack.Count == 0)
                {
                    return null;
                }

                return ((TypeReference)instruction.Operand).FullName == "System.String"
                    ? new Frame([.. stack], locals)
                    : Replace(stack, 1, 1, locals);
        }

        if (instruction.Operand is MethodReference callee && opCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        {
            bool isNew = opCode.Code == Code.Newobj;
            int argumentCount = callee.Parameters.Count + (callee.HasThis && !isNew ? 1 : 0);
            if (stack.Count < argumentCount)
            {
                return null;
            }

            var arguments = stack.GetRange(stack.Count - argumentCount, argumentCount);
            stack.RemoveRange(stack.Count - argumentCount, argumentCount);
            calls?.Add(new StringCall(method, instruction.Offset, callee, arguments, isNew));
            if (isNew)
            {
                stack.Add(StringSet.Unknown);
            }
            else if (callee.ReturnType.FullName != "System.Void")
            {
                stack.Add(IsStringConcat(callee) ? StringSet.Concat(arguments)
                    : IsGetTypeFromHandle(callee) ? arguments[0]
                    : IsThisGetter(callee, arguments) ? StringSet.ThisCall(CecilNames.MemberShort(callee))
                    : StringSet.Unknown);
            }

            return new Frame([.. stack], locals);
        }

        if (opCode.Code is Code.Stfld or Code.Stsfld && instruction.Operand is FieldReference field)
        {
            if (stack.Count == 0)
            {
                return null;
            }

            stores?.Add(new FieldStore(method, instruction.Offset, field, stack[^1]));
        }

        int pops = Pops(instruction, method, stack.Count);
        int pushes = Pushes(instruction);
        return pops < 0 || pushes < 0 || stack.Count < pops ? null : Replace(stack, pops, pushes, locals);
    }

    private static Frame Replace(List<StringSet> stack, int pops, int pushes, StringSet[] locals)
    {
        stack.RemoveRange(stack.Count - pops, pops);
        for (int i = 0; i < pushes; i++)
        {
            stack.Add(StringSet.Unknown);
        }

        return new Frame([.. stack], locals);
    }

    // this 上的无参字符串实例方法（属性 getter 等），例如 HediffComp 的抽象标签属性。
    private static bool IsThisGetter(MethodReference callee, List<StringSet> arguments)
        => callee.HasThis && callee.Parameters.Count == 0 && callee.ReturnType.FullName == "System.String" && arguments.Count == 1 && arguments[0].IsThis;

    // 实例方法中的 ldarg.0（未被重新赋值时）就是 this。
    private bool IsThis(MethodDefinition method, Instruction instruction)
        => method.HasThis && !reassignedParameters.Contains(-1) && instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => true,
            Code.Ldarg_S or Code.Ldarg => instruction.Operand is ParameterDefinition { Index: -1 },
            _ => false,
        };

    private static bool IsGetTypeFromHandle(MethodReference callee)
        => callee.DeclaringType.FullName == "System.Type" && callee.Name == "GetTypeFromHandle";

    private static bool IsStringConcat(MethodReference callee)
        => callee.DeclaringType.FullName == "System.String" && callee.Name == "Concat"
            && callee.Parameters.Count is >= 2 and <= 4 && callee.Parameters.All(p => p.ParameterType.FullName == "System.String");

    // ldarg 加载的实参在 method.Parameters 中的位置；"this" 为 null。
    private static int? ParameterPosition(MethodDefinition method, Instruction instruction)
    {
        if (instruction.Operand is ParameterDefinition parameter)
        {
            return parameter.Index >= 0 ? parameter.Index : null;
        }

        int index = instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => 0,
            Code.Ldarg_1 => 1,
            Code.Ldarg_2 => 2,
            _ => 3,
        };
        int position = method.HasThis ? index - 1 : index;
        return position >= 0 ? position : null;
    }

    private static int LocalIndex(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldloc_0 or Code.Stloc_0 => 0,
        Code.Ldloc_1 or Code.Stloc_1 => 1,
        Code.Ldloc_2 or Code.Stloc_2 => 2,
        Code.Ldloc_3 or Code.Stloc_3 => 3,
        _ => ((VariableDefinition)instruction.Operand).Index,
    };

    private static int Pops(Instruction instruction, MethodDefinition method, int depth) => instruction.OpCode.StackBehaviourPop switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
            or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
        StackBehaviour.PopAll => depth,
        StackBehaviour.Varpop when instruction.OpCode.Code == Code.Ret => method.ReturnType.FullName == "System.Void" ? 0 : 1,
        StackBehaviour.Varpop when instruction.Operand is CallSite site => site.Parameters.Count + (site.HasThis ? 1 : 0) + 1,
        _ => -1,
    };

    private static int Pushes(Instruction instruction) => instruction.OpCode.StackBehaviourPush switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
            or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
        StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Varpush when instruction.Operand is CallSite site => site.ReturnType.FullName == "System.Void" ? 0 : 1,
        _ => -1,
    };

    private static IEnumerable<int> Successors(IList<Instruction> code, int i, Dictionary<Instruction, int> index)
    {
        Instruction instruction = code[i];
        switch (instruction.OpCode.FlowControl)
        {
            case FlowControl.Branch:
                yield return index[(Instruction)instruction.Operand];
                yield break;
            case FlowControl.Cond_Branch:
                if (instruction.Operand is Instruction[] targets)
                {
                    foreach (Instruction target in targets)
                    {
                        yield return index[target];
                    }
                }
                else
                {
                    yield return index[(Instruction)instruction.Operand];
                }

                break;
            case FlowControl.Return:
            case FlowControl.Throw:
                yield break;
        }

        if (i + 1 < code.Count)
        {
            yield return i + 1;
        }
    }

    private static bool Contains(ExceptionHandler handler, Instruction instruction, Dictionary<Instruction, int> index)
    {
        int position = index[instruction];
        int end = handler.TryEnd is null ? int.MaxValue : index[handler.TryEnd];
        return position >= index[handler.TryStart] && position < end;
    }

    private static List<StringCall> AllCallsUnknown(MethodDefinition method)
    {
        var calls = new List<StringCall>();
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.Operand is MethodReference callee && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                bool isNew = instruction.OpCode.Code == Code.Newobj;
                int count = callee.Parameters.Count + (callee.HasThis && !isNew ? 1 : 0);
                calls.Add(new StringCall(method, instruction.Offset, callee, Enumerable.Repeat(StringSet.Unknown, count).ToList(), isNew));
            }
        }

        return calls;
    }

    private StringSet StaticString(FieldReference field)
    {
        if (field.DeclaringType.FullName == "System.String" && field.Name == "Empty")
        {
            return StringSet.Of("");
        }

        string key = CecilNames.Member(field);
        if (staticStrings.TryGetValue(key, out StringSet? value))
        {
            return value;
        }

        // 另一程序集的常量：只扫描其声明类型一次。
        TypeDefinition? declaring;
        try
        {
            declaring = field.DeclaringType.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            declaring = null;
        }

        if (declaring is null || declaring.Module == module || !scannedExternalTypes.Add(CecilNames.Type(declaring)))
        {
            return StringSet.Unknown;
        }

        AddStaticStringConstants(declaring, staticStrings);
        return staticStrings.GetValueOrDefault(key, StringSet.Unknown);
    }

    // 类型的 static readonly string 字段：静态构造函数恰好以字面量赋值一次的那些。
    private static void AddStaticStringConstants(TypeDefinition type, Dictionary<string, StringSet> result)
    {
        MethodDefinition? cctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody);
        if (cctor is null)
        {
            return;
        }

        var stores = new Dictionary<string, List<Instruction>>(StringComparer.Ordinal);
        foreach (Instruction instruction in cctor.Body.Instructions)
        {
            if (instruction.OpCode.Code == Code.Stsfld && instruction.Operand is FieldReference field)
            {
                string key = CecilNames.Member(field);
                if (!stores.TryGetValue(key, out List<Instruction>? list))
                {
                    stores[key] = list = [];
                }

                list.Add(instruction);
            }
        }

        foreach (FieldDefinition field in type.Fields.Where(f => f.IsStatic && f.IsInitOnly && f.FieldType.FullName == "System.String"))
        {
            if (stores.TryGetValue(CecilNames.Member(field), out List<Instruction>? list) && list is [{ Previous.OpCode.Code: Code.Ldstr } single])
            {
                result[CecilNames.Member(field)] = StringSet.Of((string)single.Previous.Operand);
            }
        }
    }

    // 一条指令开始处的抽象机状态。
    private sealed class Frame
    {
        public Frame(StringSet[] stack, StringSet[] locals)
        {
            Stack = stack;
            Locals = locals;
        }

        public StringSet[] Stack { get; }

        public StringSet[] Locals { get; }

        public static Frame Empty(int locals) => new([], Enumerable.Repeat(StringSet.Unknown, locals).ToArray());

        public Frame Clone() => new([.. Stack], CopyLocals());

        public StringSet[] CopyLocals() => [.. Locals];

        // 把另一帧合并进来；有变化时返回 true。
        public bool MergeFrom(Frame other)
        {
            bool changed = false;
            for (int i = 0; i < Stack.Length; i++)
            {
                StringSet merged = StringSet.Merge(Stack[i], other.Stack[i]);
                if (!merged.Equals(Stack[i]))
                {
                    Stack[i] = merged;
                    changed = true;
                }
            }

            for (int i = 0; i < Locals.Length; i++)
            {
                StringSet merged = StringSet.Merge(Locals[i], other.Locals[i]);
                if (!merged.Equals(Locals[i]))
                {
                    Locals[i] = merged;
                    changed = true;
                }
            }

            return changed;
        }
    }
}
