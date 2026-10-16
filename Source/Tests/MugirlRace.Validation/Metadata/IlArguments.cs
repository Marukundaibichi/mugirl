using System.Globalization;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 调用实参的来源指令：从调用点向前逐条回溯栈深度，找到压入某个实参的那条指令（只在同一基本块内；遇到分支目标即放弃）。
/// 用来读取 <c>Scribe_Values.Look</c> 的默认值与 <c>forceSave</c>、<c>Scribe_Collections.Look</c> 的 <c>LookMode</c>，
/// 以及被存档的字段（<c>ldflda</c>）。
/// </summary>
internal static class IlArguments
{
    /// <summary>
    /// 压入第 <paramref name="argument"/> 个实参的指令（0 = 第一个实参，实例调用的 <c>this</c> 也算一个），找不到时为 <see langword="null"/>。
    /// </summary>
    public static Instruction? Producer(MethodDefinition method, Instruction call, int argument)
    {
        if (call.Operand is not IMethodSignature callee)
        {
            return null;
        }

        int count = callee.Parameters.Count + (callee.HasThis && call.OpCode.Code != Code.Newobj ? 1 : 0);
        if (argument < 0 || argument >= count)
        {
            return null;
        }

        HashSet<Instruction> targets = BranchTargets(method);
        // depth：目标槽位之上还有多少个栈槽。最后一个实参位于栈顶（depth 0）。
        int depth = count - 1 - argument;
        for (Instruction? current = call.Previous; current is not null; current = current.Previous)
        {
            // 跨过的指令边界若是分支目标，栈上的值可能来自另一条路径（例如三元表达式）。
            int pushes = Pushes(current);
            int pops = Pops(current, method);
            if (targets.Contains(current.Next) || pushes < 0 || pops < 0
                || current.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw)
            {
                return null;
            }

            if (depth < pushes)
            {
                return current;
            }

            depth = depth - pushes + pops;
        }

        return null;
    }

    /// <summary>
    /// 来源指令代表的编译期常量，按 Scribe 写出的文本格式（不变区域性、<c>True/False</c>、枚举成员名）；不是常量时为 <see langword="null"/>。
    /// <paramref name="valueType"/> 决定整数常量的解释（bool、枚举、float 等）。
    /// </summary>
    public static string? Constant(MethodDefinition method, Instruction? producer, TypeReference? valueType)
    {
        if (producer is null)
        {
            return null;
        }

        string? typeName = valueType?.FullName;
        switch (producer.OpCode.Code)
        {
            case Code.Ldnull:
                return "null";
            case Code.Ldstr:
                return Quote((string)producer.Operand);
            case Code.Ldc_R4:
                return Float((float)producer.Operand);
            case Code.Ldc_R8:
                return ((double)producer.Operand).ToString("R", CultureInfo.InvariantCulture);
            case Code.Ldc_I8:
                return ((long)producer.Operand).ToString(CultureInfo.InvariantCulture);
            case Code.Conv_I8 or Code.Conv_U8 or Code.Conv_R4 or Code.Conv_R8:
                // 64 位或浮点常量可能由较短的整数常量加 conv 构成。
                return producer.Previous is { } inner && IntConstant(inner) is int widened
                    ? (producer.OpCode.Code is Code.Conv_R4 or Code.Conv_R8 ? Float(widened) : widened.ToString(CultureInfo.InvariantCulture))
                    : null;
        }

        if (IntConstant(producer) is int value)
        {
            if (typeName == "System.Boolean")
            {
                return value != 0 ? "True" : "False";
            }

            if (typeName is "System.Single" or "System.Double")
            {
                return Float(value);
            }

            if (CecilNames.TryResolve(valueType) is { IsEnum: true } enumType)
            {
                return EnumMember(enumType, value) ?? value.ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        // 结构体默认值：ldloca x; initobj T; ldloc x。
        if (producer.OpCode.Code is Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3)
        {
            for (Instruction? previous = producer.Previous; previous is not null && previous.OpCode.FlowControl == FlowControl.Next; previous = previous.Previous)
            {
                if (previous.OpCode.Code == Code.Initobj)
                {
                    return "default";
                }
            }
        }

        if (producer.OpCode.Code is Code.Ldsfld && producer.Operand is FieldReference field && CecilNames.TryResolve(field.DeclaringType) is { } owner
            && owner.Fields.FirstOrDefault(f => f.Name == field.Name) is { IsLiteral: true, Constant: { } literal })
        {
            return literal is string text ? Quote(text) : Convert.ToString(literal, CultureInfo.InvariantCulture);
        }

        // 静态只读值（IntVec3.Invalid、Color.white 之类）：不是编译期常量，但每次取到同一个值，按符号比较。
        if (producer.OpCode.Code == Code.Ldsfld && producer.Operand is FieldReference staticField)
        {
            return "static " + CecilNames.MemberShort(staticField);
        }

        if (producer.OpCode.Code == Code.Call && producer.Operand is MethodReference getter && !getter.HasThis && getter.Parameters.Count == 0
            && getter.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            return "static " + CecilNames.MemberShort(getter);
        }

        return null;
    }

    /// <summary>整数常量指令（<c>ldc.i4*</c>）的值。</summary>
    public static int? IntConstant(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1,
        Code.Ldc_I4_0 => 0,
        Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5,
        Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4_S => (sbyte)instruction.Operand,
        Code.Ldc_I4 => (int)instruction.Operand,
        _ => null,
    };

    /// <summary>值为 <paramref name="value"/> 的枚举成员名；没有或有多个时为 <see langword="null"/>。</summary>
    public static string? EnumMember(TypeDefinition enumType, long value)
    {
        List<string> names = enumType.Fields
            .Where(f => f.IsStatic && f.IsLiteral && f.Constant is not null && Convert.ToInt64(f.Constant, CultureInfo.InvariantCulture) == value)
            .Select(f => f.Name)
            .ToList();
        return names.Count == 1 ? names[0] : null;
    }

    /// <summary>JSON 风格的带引号字符串（制表符、换行转义，TSV 安全）。</summary>
    public static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    /// <summary>浮点常量的 Scribe 文本（不变区域性、往返格式）。</summary>
    public static string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>指令弹出的栈槽数；无法确定时为 -1。</summary>
    public static int Pops(Instruction instruction, MethodDefinition method)
    {
        if (instruction.Operand is IMethodSignature callee && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        {
            return callee.Parameters.Count + (callee.HasThis && instruction.OpCode.Code != Code.Newobj ? 1 : 0);
        }

        if (instruction.OpCode.Code == Code.Calli && instruction.Operand is CallSite site)
        {
            return site.Parameters.Count + (site.HasThis ? 1 : 0) + 1;
        }

        return instruction.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.Varpop when instruction.OpCode.Code == Code.Ret => method.ReturnType.FullName == "System.Void" ? 0 : 1,
            _ => -1,
        };
    }

    /// <summary>指令压入的栈槽数；无法确定时为 -1。</summary>
    public static int Pushes(Instruction instruction)
    {
        if (instruction.Operand is IMethodSignature callee && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj or Code.Calli)
        {
            return instruction.OpCode.Code == Code.Newobj || callee.ReturnType.FullName != "System.Void" ? 1 : 0;
        }

        return instruction.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
                or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => -1,
        };
    }

    private static HashSet<Instruction> BranchTargets(MethodDefinition method)
    {
        var targets = new HashSet<Instruction>();
        foreach (Instruction instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case Instruction target:
                    targets.Add(target);
                    break;
                case Instruction[] many:
                    targets.UnionWith(many);
                    break;
            }
        }

        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
        {
            targets.Add(handler.HandlerStart);
            if (handler.FilterStart is not null)
            {
                targets.Add(handler.FilterStart);
            }
        }

        return targets;
    }
}
