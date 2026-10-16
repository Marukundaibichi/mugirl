using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MugirlRace.Validation.Parity;

/// <summary>
/// 两个构建的 IL 一致性（Wave R 只做保持身份的改名）：A 的每个类型、字段、方法经改名映射后在 B 中有完全对应的定义，方法体逐条指令相同
/// （短/长分支形式归一，分支目标按指令下标，操作数按映射后的规范名），局部变量与异常处理表相同，特性相同，并且决定行为的元数据顺序
/// （组件、FloatMenu 提供者、静态构造、DefOf、补丁类）与每个目标上的 Harmony 补丁顺序相同。
/// </summary>
/// <remarks>
/// 忽略构建噪声：MVID、PDB、<c>AssemblyInformationalVersion</c>、Nullable/RefSafetyRules/CompilerFeatureRequired 特性、<c>.maxstack</c>。
/// 编译器生成的名字按“所属方法 + 序号”规范化（<c>&lt;M&gt;b__N_K</c> 的 N 是方法序号，拆分文件会改变它）。改变的字符串字面量只有在
/// 是映射的投影（类型名、defName、键、抽象 Name）时才算解释过；落到 Scribe 标签的字面量变化永远是存档结构变化。
/// </remarks>
internal sealed partial class IlParity
{
    private static readonly string[] IgnoredAttributes =
    [
        "System.Runtime.CompilerServices.NullableAttribute", "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute", "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute", "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Diagnostics.DebuggableAttribute",
    ];

    private static readonly (string Family, string Base)[] OrderFamilies =
    [
        ("GameComponent", "Verse.GameComponent"), ("MapComponent", "Verse.MapComponent"), ("WorldComponent", "RimWorld.Planet.WorldComponent"),
        ("FloatMenuOptionProvider", "RimWorld.FloatMenuOptionProvider"), ("Alert", "RimWorld.Alert"),
    ];

    private readonly ModuleDefinition a;
    private readonly ModuleDefinition b;
    private readonly RenameMap map;
    private readonly Dictionary<string, TypeDefinition> bTypes;
    private readonly Dictionary<TypeDefinition, string> canonicalA = [];
    private readonly Dictionary<TypeDefinition, string> canonicalB = [];
    private readonly List<(string Subject, IMemberDefinition Member, string Owner, string Name)> movedMembers = [];
    private readonly HashSet<string> moveTargets = new(StringComparer.Ordinal);

    public IlParity(ModuleDefinition a, ModuleDefinition b, RenameMap map)
    {
        this.a = a;
        this.b = b;
        this.map = map;
        Name(a, canonicalA, mapped: true);
        Name(b, canonicalB, mapped: false);

        bTypes = [];
        foreach ((TypeDefinition type, string name) in canonicalB)
        {
            bTypes.TryAdd(name, type);
        }

        foreach ((string owner, string name) in map.MemberTargets)
        {
            moveTargets.Add(owner + "::" + name);
        }
    }

    /// <summary>未解释的差异（类别、主题、说明）。</summary>
    public List<ParityDiff> Diffs { get; } = [];

    /// <summary>读取类型名或成员名的位置（重命名会改变它们读到的值，需要逐个签核）。</summary>
    public List<string> NameSensitiveSites { get; } = [];

    public int MatchedTypes { get; private set; }

    public int IdenticalBodies { get; private set; }

    public void Run()
    {
        var matched = new HashSet<TypeDefinition>();
        foreach ((TypeDefinition typeA, string name) in canonicalA.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            if (typeA.Name == "<Module>")
            {
                continue;
            }

            if (!bTypes.TryGetValue(name, out TypeDefinition? typeB))
            {
                if (map.IsDissolved(CecilNames.Type(typeA)))
                {
                    // 解散的类型：成员按映射移走（未映射的成员就是差异），类型本身与构造函数消失。
                    CollectMoves(name, typeA, report: true);
                    continue;
                }

                Diffs.Add(new ParityDiff("type", name, "A's type has no counterpart in B (an unmapped rename, a deletion or a split)."));
                continue;
            }

            matched.Add(typeB);
            MatchedTypes++;
            CompareType(name, typeA, typeB);
        }

        var receivers = new HashSet<string>(map.MemberTargets.Select(target => target.Owner), StringComparer.Ordinal);
        foreach ((TypeDefinition typeB, string name) in canonicalB.Where(pair => pair.Key.Name != "<Module>" && !matched.Contains(pair.Key)).OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            if (!receivers.Contains(name))
            {
                Diffs.Add(new ParityDiff("type", name, "B's type has no counterpart in A (an addition)."));
                continue;
            }

            // 只因成员移入而新建的类型（DefOf 拆分）：移入的成员单独比较，其余成员是新增。
            foreach (FieldDefinition field in typeB.Fields.Where(f => !moveTargets.Contains(name + "::" + f.Name)))
            {
                Diffs.Add(new ParityDiff("field", $"{name}::{field.Name}", "Field only in B (type created by member moves)."));
            }

            foreach (MethodDefinition method in typeB.Methods.Where(m => !moveTargets.Contains(name + "::" + m.Name)))
            {
                Diffs.Add(new ParityDiff("method", $"{name}::{MethodKey(method, mapped: false)}", "Method only in B (type created by member moves)."));
            }
        }

        CompareMovedMembers();
        CompareOrder();
        CompareHarmonyOrder();
        FindNameSensitiveSites();
    }

    private void CompareType(string name, TypeDefinition typeA, TypeDefinition typeB)
    {
        if ((typeA.Attributes & ~TypeAttributes.BeforeFieldInit) != (typeB.Attributes & ~TypeAttributes.BeforeFieldInit))
        {
            Diffs.Add(new ParityDiff("type-flags", name, $"{typeA.Attributes} vs {typeB.Attributes}"));
        }

        if (TypeA(typeA.BaseType) != TypeB(typeB.BaseType))
        {
            Diffs.Add(new ParityDiff("base-type", name, $"{TypeA(typeA.BaseType)} vs {TypeB(typeB.BaseType)}"));
        }

        string interfacesA = string.Join(",", typeA.Interfaces.Select(i => TypeA(i.InterfaceType)).Order(StringComparer.Ordinal));
        string interfacesB = string.Join(",", typeB.Interfaces.Select(i => TypeB(i.InterfaceType)).Order(StringComparer.Ordinal));
        if (interfacesA != interfacesB)
        {
            Diffs.Add(new ParityDiff("interfaces", name, $"{interfacesA} vs {interfacesB}"));
        }

        CompareAttributes("attributes", name, typeA.CustomAttributes, typeB.CustomAttributes);
        string ownerA = CecilNames.Type(typeA);
        string ownerB = CecilNames.Type(typeB);
        CollectMoves(name, typeA, report: false);
        Dictionary<string, FieldDefinition> fieldsB = Keyed(typeB.Fields.Where(f => !moveTargets.Contains(ownerB + "::" + f.Name)), f => CanonicalMember(f.Name));
        foreach ((string fieldName, FieldDefinition fieldA) in Keyed(typeA.Fields.Where(f => StaysInPlace(ownerA, f.Name, null)), f => CanonicalMember(map.MemberName(ownerA, f.Name))))
        {
            if (!fieldsB.Remove(fieldName, out FieldDefinition? fieldB))
            {
                Diffs.Add(new ParityDiff("field", $"{name}::{fieldName}", "Field missing in B."));
                continue;
            }

            CompareField($"{name}::{fieldName}", fieldA, fieldB);
        }

        foreach (string extra in fieldsB.Keys)
        {
            Diffs.Add(new ParityDiff("field", $"{name}::{extra}", "Field only in B."));
        }

        Dictionary<string, MethodDefinition> methodsB = Keyed(typeB.Methods.Where(m => !moveTargets.Contains(ownerB + "::" + m.Name)), m => MethodKey(m, mapped: false));
        foreach ((string key, MethodDefinition methodA) in Keyed(typeA.Methods.Where(m => StaysInPlace(ownerA, m.Name, Parameters(m))), m => MethodKey(m, mapped: true)))
        {
            if (!methodsB.Remove(key, out MethodDefinition? methodB))
            {
                Diffs.Add(new ParityDiff("method", $"{name}::{key}", "Method missing in B (or its signature changed)."));
                continue;
            }

            CompareMethod($"{name}::{key}", methodA, methodB);
        }

        foreach (string extra in methodsB.Keys)
        {
            Diffs.Add(new ParityDiff("method", $"{name}::{extra}", "Method only in B."));
        }
    }

    private void CompareField(string subject, FieldDefinition fieldA, FieldDefinition fieldB)
    {
        if (TypeA(fieldA.FieldType) != TypeB(fieldB.FieldType) || fieldA.Attributes != fieldB.Attributes || !Equals(fieldA.Constant, fieldB.Constant)
            || !(fieldA.InitialValue ?? []).SequenceEqual(fieldB.InitialValue ?? []))
        {
            Diffs.Add(new ParityDiff("field", subject, $"{TypeA(fieldA.FieldType)} {fieldA.Attributes} {fieldA.Constant} vs {TypeB(fieldB.FieldType)} {fieldB.Attributes} {fieldB.Constant}"));
        }

        bool defOf = fieldA.DeclaringType.CustomAttributes.Any(a => a.AttributeType.FullName == "RimWorld.DefOf")
            && fieldB.DeclaringType.CustomAttributes.Any(a => a.AttributeType.FullName == "RimWorld.DefOf");
        if (defOf)
        {
            string root = CecilNames.TryResolve(fieldA.FieldType) is { } definition ? Legacy.TypeUniverse.DatabaseRoot(definition) ?? fieldA.FieldType.Name : fieldA.FieldType.Name;
            string boundA = map.Def(DefOfBinding(fieldA), root);
            string boundB = DefOfBinding(fieldB);
            if (boundA != boundB) { Diffs.Add(new ParityDiff("def-binding", subject, $"DefOf binding {root}/{boundA} vs {boundB}")); }
        }

        CompareAttributes("field-attributes", subject,
            fieldA.CustomAttributes.Where(a => !defOf || a.AttributeType.FullName != "RimWorld.DefAliasAttribute"),
            fieldB.CustomAttributes.Where(a => !defOf || a.AttributeType.FullName != "RimWorld.DefAliasAttribute"));
    }

    private static string DefOfBinding(FieldDefinition field)
        => field.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "RimWorld.DefAliasAttribute")?.ConstructorArguments.FirstOrDefault().Value as string ?? field.Name;

    // 成员留在（映射后的）原类型中：没有被删除、没有移走（复制的成员原处仍在）。
    private bool StaysInPlace(string owner, string member, string? parameters)
    {
        (string? newOwner, _, bool deleted, bool copied) = map.Member(owner, member, parameters);
        return !deleted && (newOwner is null || copied);
    }

    // 移到其他类型的成员留到最后与 B 中的目标比较；report 为 true 时（解散的类型）没有映射的成员记为差异。
    private void CollectMoves(string name, TypeDefinition typeA, bool report)
    {
        string ownerA = CecilNames.Type(typeA);
        foreach (IMemberDefinition member in typeA.Fields.Cast<IMemberDefinition>().Concat(typeA.Methods))
        {
            string? parameters = member is MethodDefinition method ? Parameters(method) : null;
            (string? newOwner, string newName, bool deleted, _) = map.Member(ownerA, member.Name, parameters);
            IReadOnlyList<(string Owner, string Name)> copiedTargets = map.CopiesOf(ownerA, member.Name);
            foreach ((string owner, string targetName) in copiedTargets)
            {
                movedMembers.Add(($"{name}::{member.Name}", member, owner, targetName));
            }

            if (newOwner is not null)
            {
                movedMembers.Add(($"{name}::{member.Name}", member, newOwner, newName));
            }
            else if (report && copiedTargets.Count == 0 && !deleted && !(member is MethodDefinition { IsConstructor: true }))
            {
                Diffs.Add(new ParityDiff(member is FieldDefinition ? "field" : "method", $"{name}::{member.Name}", "Member of a dissolved type without a move or delete entry."));
            }
        }
    }

    private void CompareMovedMembers()
    {
        foreach ((string subject, IMemberDefinition member, string owner, string newName) in movedMembers)
        {
            if (!bTypes.TryGetValue(owner, out TypeDefinition? typeB))
            {
                Diffs.Add(new ParityDiff("member-move", subject, $"Target type {owner} does not exist in B."));
                continue;
            }

            switch (member)
            {
                case FieldDefinition fieldA:
                    if (typeB.Fields.FirstOrDefault(f => f.Name == newName) is { } fieldB)
                    {
                        CompareField($"{subject} -> {owner}::{newName}", fieldA, fieldB);
                    }
                    else
                    {
                        Diffs.Add(new ParityDiff("member-move", subject, $"Moved field {owner}::{newName} is missing in B."));
                    }

                    break;
                case MethodDefinition methodA:
                    string key = MethodKey(methodA, mapped: true, newName);
                    if (typeB.Methods.FirstOrDefault(m => MethodKey(m, mapped: false) == key) is { } methodB)
                    {
                        CompareMethod($"{subject} -> {owner}::{key}", methodA, methodB);
                    }
                    else
                    {
                        Diffs.Add(new ParityDiff("member-move", subject, $"Moved method {owner}::{key} is missing in B (or its signature changed)."));
                    }

                    break;
            }
        }
    }

    // 方法的文档 id 参数表（映射记录中方法键的写法）；无参数时为 null。
    private static string? Parameters(MethodReference method)
    {
        string id = DocIds.ForMethod(method);
        int open = id.IndexOf('(', StringComparison.Ordinal);
        return open < 0 ? null : id[(open + 1)..id.LastIndexOf(')')];
    }

    // 键重复时（规范化后同名的编译器生成成员）按元数据顺序编号；改名不改变同一类型中成员的声明顺序。
    private static Dictionary<string, T> Keyed<T>(IEnumerable<T> members, Func<T, string> key)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T member in members)
        {
            string name = key(member);
            string unique = name;
            for (int i = 1; result.ContainsKey(unique); i++)
            {
                unique = name + "#" + i.ToString(CultureInfo.InvariantCulture);
            }

            result[unique] = member;
        }

        return result;
    }

    private void CompareMethod(string subject, MethodDefinition methodA, MethodDefinition methodB)
    {
        if (methodA.Attributes != methodB.Attributes || methodA.ImplAttributes != methodB.ImplAttributes)
        {
            Diffs.Add(new ParityDiff("method-flags", subject, $"{methodA.Attributes}/{methodA.ImplAttributes} vs {methodB.Attributes}/{methodB.ImplAttributes}"));
        }

        string parametersA = string.Join(",", methodA.Parameters.Select(p => p.Name));
        string parametersB = string.Join(",", methodB.Parameters.Select(p => p.Name));
        if (parametersA != parametersB)
        {
            // Harmony 按参数名绑定补丁参数（__instance、___field、与原方法同名的参数）。
            Diffs.Add(new ParityDiff("parameter-names", subject, $"({parametersA}) vs ({parametersB})"));
        }

        CompareAttributes("method-attributes", subject, methodA.CustomAttributes, methodB.CustomAttributes);
        if (methodA.HasBody != methodB.HasBody)
        {
            Diffs.Add(new ParityDiff("body", subject, "Only one side has a body."));
            return;
        }

        if (!methodA.HasBody)
        {
            return;
        }

        List<string> bodyA = Body(methodA, mapped: true);
        List<string> bodyB = Body(methodB, mapped: false);
        int count = Math.Min(bodyA.Count, bodyB.Count);
        for (int i = 0; i < count; i++)
        {
            if (bodyA[i] == bodyB[i])
            {
                continue;
            }

            if (StringLiteral(bodyA[i]) is { } literalA && StringLiteral(bodyB[i]) is { } literalB)
            {
                // 落到 Scribe 调用的字面量是存档标签：即使映射解释了（nameof 跟着成员改名），也是存档结构变化。
                if (FeedsScribe(bodyB, i))
                {
                    Diffs.Add(new ParityDiff("scribe-label", subject, $"Scribe label \"{literalA}\" became \"{literalB}\": a save schema change, which needs an upgrade-manifest entry and does not belong in a rename wave."));
                    return;
                }

                if (map.Explains(literalA, literalB))
                {
                    continue;
                }
            }

            Diffs.Add(new ParityDiff(Category(bodyA[i], bodyB[i]), subject, $"IL #{i}: {bodyA[i]}  vs  {bodyB[i]}"));
            return;
        }

        if (bodyA.Count != bodyB.Count)
        {
            Diffs.Add(new ParityDiff("body", subject, $"{bodyA.Count} vs {bodyB.Count} instruction(s)."));
            return;
        }

        IdenticalBodies++;
    }

    // 改变的调用目标（静默重绑定）与字符串字面量单独归类，便于审阅。
    private static string Category(string lineA, string lineB)
        => lineA.StartsWith("ldstr ", StringComparison.Ordinal) ? "string-literal"
            : lineA.StartsWith("call", StringComparison.Ordinal) && lineB.StartsWith("call", StringComparison.Ordinal) ? "call-target"
            : lineA.StartsWith("ldc", StringComparison.Ordinal) || lineB.StartsWith("ldc", StringComparison.Ordinal) ? "constant"
            : "body";

    // 字面量之后的几条指令内有 Verse.Scribe* 调用（标签实参通常紧挨着调用）。
    private static bool FeedsScribe(List<string> body, int index)
        => body.Skip(index + 1).Take(8).Any(line => line.StartsWith("call Verse.Scribe", StringComparison.Ordinal));

    private static string? StringLiteral(string line) => line.StartsWith("ldstr ", StringComparison.Ordinal) ? System.Text.Json.JsonSerializer.Deserialize<string>(line[6..]) : null;

    private List<string> Body(MethodDefinition method, bool mapped)
    {
        MethodBody body = method.Body;
        body.SimplifyMacros();
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < body.Instructions.Count; i++)
        {
            index[body.Instructions[i]] = i;
        }

        var lines = new List<string>(body.Instructions.Count + body.Variables.Count + body.ExceptionHandlers.Count);
        foreach (VariableDefinition variable in body.Variables)
        {
            lines.Add("local " + Type(variable.VariableType, mapped));
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers)
        {
            lines.Add($"handler {handler.HandlerType} {index[handler.TryStart]}-{(handler.TryEnd is null ? -1 : index[handler.TryEnd])} {index[handler.HandlerStart]} "
                + (handler.CatchType is null ? "" : Type(handler.CatchType, mapped)));
        }

        lines.Add("initlocals " + body.InitLocals);
        HashSet<Instruction> callerLines = CallerLineOperands(method);
        foreach (Instruction instruction in body.Instructions)
        {
            lines.Add(callerLines.Contains(instruction) ? "ldc.i4 <caller-line>" : instruction.OpCode.Name + " " + Operand(instruction.Operand, index, mapped));
        }

        return lines;
    }

    // 仅规范化编译器提供的日志源码行号：属性和该侧 PDB 源位置同时证明它是调用位置。
    // 普通游戏常量、显式传入且不同于源位置的行号，以及没有 PDB 的构建仍逐值比较。
    private static HashSet<Instruction> CallerLineOperands(MethodDefinition method)
    {
        var result = new HashSet<Instruction>();
        foreach (Instruction call in method.Body.Instructions)
        {
            if (call.Operand is not MethodReference callee || CecilNames.TryResolve(callee) is not { } definition
                || definition.DeclaringType.FullName != "Mugirl.Core.MugirlLog") { continue; }
            SequencePoint? point = method.DebugInformation.SequencePoints.LastOrDefault(p => !p.IsHidden && p.Offset <= call.Offset);
            if (point is null) { continue; }
            for (int i = 0; i < definition.Parameters.Count; i++)
            {
                ParameterDefinition parameter = definition.Parameters[i];
                if (!parameter.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CallerLineNumberAttribute")) { continue; }
                Instruction? producer = IlArguments.Producer(method, call, i + (callee.HasThis ? 1 : 0));
                if (producer is { OpCode.Code: Code.Ldc_I4, Operand: int line } && line >= point.StartLine && line <= point.EndLine)
                { result.Add(producer); }
            }
        }

        return result;
    }

    private string Operand(object? operand, Dictionary<Instruction, int> index, bool mapped) => operand switch
    {
        null => "",
        Instruction target => "@" + index[target],
        Instruction[] targets => string.Join(",", targets.Select(t => "@" + index[t])),
        string text => IlArguments.Quote(text),
        TypeReference type => Type(type, mapped),
        MethodReference method => MethodName(method, mapped),
        FieldReference field => map.CopiedFieldIdentity(CecilNames.Type(field.DeclaringType), field.Name)
            ?? MemberOwner(field.DeclaringType, field.Name, null, mapped, out string fieldName) + "::" + CanonicalMember(fieldName),
        ParameterDefinition parameter => "arg" + parameter.Index,
        VariableDefinition variable => "loc" + variable.Index,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => operand.ToString() ?? "",
    };

    // 成员引用的所属类型（A 侧按映射：移动的成员指向新类型）与名字。
    private string MemberOwner(TypeReference declaring, string member, string? parameters, bool mapped, out string name)
    {
        name = member;
        if (!mapped || declaring is GenericInstanceType)
        {
            return Type(declaring, mapped);
        }

        (string? newOwner, string newName, _, bool copied) = map.Member(CecilNames.Type(declaring), member, parameters);
        name = newName;
        return newOwner is not null && !copied ? newOwner : Type(declaring, mapped);
    }

    private string MethodName(MethodReference method, bool mapped)
    {
        string owner = MemberOwner(method.DeclaringType, method.Name, mapped ? Parameters(method) : null, mapped, out string memberName);
        string name = CanonicalMember(memberName);
        string generic = method is GenericInstanceMethod instance ? "<" + string.Join(",", instance.GenericArguments.Select(t => Type(t, mapped))) + ">" : "";
        return $"{owner}::{name}{generic}({string.Join(",", method.Parameters.Select(p => Type(p.ParameterType, mapped)))}):{Type(method.ReturnType, mapped)}";
    }

    private string MethodKey(MethodDefinition method, bool mapped, string? rename = null)
    {
        string name = CanonicalMember(rename ?? (mapped ? map.Member(CecilNames.Type(method.DeclaringType), method.Name, Parameters(method)).Name : method.Name));
        return $"{name}{(method.HasGenericParameters ? "`" + method.GenericParameters.Count : "")}({string.Join(",", method.Parameters.Select(p => Type(p.ParameterType, mapped)))}):{Type(method.ReturnType, mapped)}";
    }

    private string TypeA(TypeReference? type) => type is null ? "-" : Type(type, mapped: true);

    private string TypeB(TypeReference? type) => type is null ? "-" : Type(type, mapped: false);

    // 规范类型名：本模块类型按映射与编译器生成名规范化；泛型实参与元素类型递归处理。
    private string Type(TypeReference type, bool mapped)
    {
        switch (type)
        {
            case GenericInstanceType generic:
                return Type(generic.ElementType, mapped) + "<" + string.Join(",", generic.GenericArguments.Select(t => Type(t, mapped))) + ">";
            case ByReferenceType reference:
                return Type(reference.ElementType, mapped) + "&";
            case ArrayType array:
                return Type(array.ElementType, mapped) + "[" + new string(',', array.Rank - 1) + "]";
            case PointerType pointer:
                return Type(pointer.ElementType, mapped) + "*";
            case GenericParameter parameter:
                return (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;
            case RequiredModifierType or OptionalModifierType:
                return Type(((TypeSpecification)type).ElementType, mapped);
        }

        if (type is TypeDefinition definition && (definition.Module == a || definition.Module == b))
        {
            return mapped ? canonicalA.GetValueOrDefault(definition) ?? Canonical(definition, mapped) : canonicalB.GetValueOrDefault(definition) ?? Canonical(definition, mapped);
        }

        if (CecilNames.AssemblyOf(type) == (mapped ? a : b).Assembly.Name.Name && CecilNames.TryResolve(type) is { } own)
        {
            return Type(own, mapped);
        }

        return CecilNames.Type(type);
    }

    // 规范名按元数据顺序编号同名的编译器生成类型（同一类型中的多个 <>c__DisplayClass#_0）。
    private void Name(ModuleDefinition module, Dictionary<TypeDefinition, string> names, bool mapped)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TypeDefinition type in CecilNames.AllTypes(module))
        {
            string name = type.DeclaringType is { } outer && names.TryGetValue(outer, out string? outerName)
                ? outerName + "+" + CanonicalMember(type.Name)
                : Canonical(type, mapped);
            int count = seen.GetValueOrDefault(name);
            seen[name] = count + 1;
            names[type] = count == 0 ? name : name + "#" + count.ToString(CultureInfo.InvariantCulture);
        }
    }

    private string Canonical(TypeDefinition type, bool mapped)
    {
        if (type.DeclaringType is { } outer)
        {
            return Canonical(outer, mapped) + "+" + CanonicalMember(type.Name);
        }

        // 匿名类型按属性名（泛型形参名 <Name>j__TPar）命名：编号取决于编译顺序，拆分或移动文件会改变它。
        if (type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal))
        {
            return "<>f__AnonymousType{" + string.Join(",", type.GenericParameters.Select(p => p.Name)) + "}";
        }

        string name = GeneratorHash().Replace(CecilNames.Type(type), "<RegexGenerator_g>#__");
        return mapped ? map.Type(name) : name;
    }

    // <M>b__12_0 → <M>b__#_0、<>c__DisplayClass12_0 → <>c__DisplayClass#_0、<M>d__12 → <M>d__#：N 是方法序号，拆分文件会改变它。
    // 正则源生成器的类型名含生成文件路径的哈希（不同目录构建不同）。
    private static string CanonicalMember(string name) => GeneratorHash().Replace(GeneratedOrdinal().Replace(name, "${prefix}#"), "<RegexGenerator_g>#__");

    private void CompareAttributes(string category, string subject, IEnumerable<CustomAttribute> attributesA, IEnumerable<CustomAttribute> attributesB)
    {
        string textA = Attributes(attributesA, mapped: true);
        string textB = Attributes(attributesB, mapped: false);
        if (textA != textB)
        {
            Diffs.Add(new ParityDiff(category, subject, $"[{textA}] vs [{textB}]"));
        }
    }

    private string Attributes(IEnumerable<CustomAttribute> attributes, bool mapped)
    {
        var parts = new List<string>();
        foreach (CustomAttribute attribute in attributes.Where(at => !IgnoredAttributes.Contains(at.AttributeType.FullName, StringComparer.Ordinal)))
        {
            var text = new StringBuilder(Type(attribute.AttributeType, mapped)).Append('(');
            text.AppendJoin(",", attribute.ConstructorArguments.Select(argument => Argument(argument, mapped)));
            text.AppendJoin(",", attribute.Properties.Select(p => p.Name + "=" + Argument(p.Argument, mapped)));
            text.AppendJoin(",", attribute.Fields.Select(f => f.Name + "=" + Argument(f.Argument, mapped)));
            parts.Add(text.Append(')').ToString());
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    private string Argument(CustomAttributeArgument argument, bool mapped) => argument.Value switch
    {
        TypeReference type => "typeof(" + Type(type, mapped) + ")",
        string text when mapped => IlArguments.Quote(MapLiteral(text)),
        string text => IlArguments.Quote(text),
        CustomAttributeArgument[] array => "{" + string.Join(",", array.Select(item => Argument(item, mapped))) + "}",
        CustomAttributeArgument nested => Argument(nested, mapped),
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        object value => value.ToString() ?? "",
    };

    // 特性中的字符串（补丁目标方法名、defName 等）按映射投影。
    private string MapLiteral(string text)
    {
        string type = map.Type(text);
        return type != text ? type : map.Def(text);
    }

    // 元数据顺序决定行为的类型族：FillComponents、FloatMenu 提供者、静态构造与 DefOf 初始化、补丁类注册。
    private void CompareOrder()
    {
        foreach ((string family, IEnumerable<string> orderA, IEnumerable<string> orderB) in Families())
        {
            List<string> listA = orderA.ToList();
            List<string> listB = orderB.ToList();
            if (!listA.SequenceEqual(listB, StringComparer.Ordinal))
            {
                Diffs.Add(new ParityDiff("order", family, $"A (mapped): {string.Join(", ", listA)}  B: {string.Join(", ", listB)}"));
            }
        }
    }

    private IEnumerable<(string Family, IEnumerable<string> A, IEnumerable<string> B)> Families()
    {
        foreach ((string family, string baseType) in OrderFamilies)
        {
            yield return (family, Members(a, t => !t.IsAbstract && CecilNames.BaseTypeNames(t).Contains(baseType), mapped: true),
                Members(b, t => !t.IsAbstract && CecilNames.BaseTypeNames(t).Contains(baseType), mapped: false));
        }

        yield return ("StaticConstructorOnStartup", Members(a, HasAttribute("Verse.StaticConstructorOnStartup"), mapped: true), Members(b, HasAttribute("Verse.StaticConstructorOnStartup"), mapped: false));
        yield return ("DefOf", Members(a, HasAttribute("RimWorld.DefOf"), mapped: true), Members(b, HasAttribute("RimWorld.DefOf"), mapped: false));
    }

    private IEnumerable<string> Members(ModuleDefinition module, Func<TypeDefinition, bool> filter, bool mapped)
        => CecilNames.AllTypes(module).Where(t => !CecilNames.IsCompilerGenerated(t) && filter(t)).Select(t => Type(t, mapped));

    private static Func<TypeDefinition, bool> HasAttribute(string name) => type => type.CustomAttributes.Any(at => at.AttributeType.FullName == name);

    // 每个（目标、种类）上 Mugirl 补丁的实际顺序：优先级降序，再按注册顺序（HarmonyBootstrap 按完整类名序数排序）。
    private void CompareHarmonyOrder()
    {
        Dictionary<string, List<string>> orderA = HarmonyOrder(a, mapped: true);
        Dictionary<string, List<string>> orderB = HarmonyOrder(b, mapped: false);
        foreach (string target in orderA.Keys.Union(orderB.Keys).Order(StringComparer.Ordinal))
        {
            List<string> listA = orderA.GetValueOrDefault(target) ?? [];
            List<string> listB = orderB.GetValueOrDefault(target) ?? [];
            if (!listA.SequenceEqual(listB, StringComparer.Ordinal))
            {
                Diffs.Add(new ParityDiff("harmony-order", target, $"A (mapped): {string.Join(" > ", listA)}  B: {string.Join(" > ", listB)}"));
            }
        }
    }

    private Dictionary<string, List<string>> HarmonyOrder(ModuleDefinition module, bool mapped)
    {
        var patches = new List<(string Target, string Kind, string Patch, int Priority, string Registration)>();
        foreach (TypeDefinition type in CecilNames.AllTypes(module).Where(t => t.CustomAttributes.Any(IsHarmonyPatch)))
        {
            string? target = PatchTarget(type.CustomAttributes, mapped);
            int classPriority = Priority(type.CustomAttributes) ?? 400;
            foreach (MethodDefinition method in type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Transpiler" or "Finalizer" || m.CustomAttributes.Any(at => at.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyTranspiler" or "HarmonyFinalizer")))
            {
                string kind = method.CustomAttributes.Select(at => at.AttributeType.Name).FirstOrDefault(n => n is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyTranspiler" or "HarmonyFinalizer")?["Harmony".Length..] ?? method.Name;
                string? methodTarget = PatchTarget(method.CustomAttributes, mapped, target);
                if (methodTarget is not null)
                {
                    patches.Add((methodTarget, kind, Type(type, mapped), Priority(method.CustomAttributes) ?? classPriority, CecilNames.Type(type)));
                }
            }
        }

        return patches.GroupBy(p => p.Target + " " + p.Kind)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Priority).ThenBy(p => p.Registration, StringComparer.Ordinal).Select(p => p.Patch).ToList(), StringComparer.Ordinal);
    }

    private static bool IsHarmonyPatch(CustomAttribute attribute) => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch";

    // 目标键：类型、方法名与参数类型（[HarmonyPatch(typeof(X), "M", new[] { typeof(A) })]，重载不能合并）；没有方法名时为 X::(type)。
    private string? PatchTarget(IEnumerable<CustomAttribute> attributes, bool mapped, string? inherited = null)
    {
        string? type = null;
        string? method = null;
        string? arguments = null;
        foreach (CustomAttribute attribute in attributes.Where(IsHarmonyPatch))
        {
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            {
                switch (argument.Value)
                {
                    case TypeReference target:
                        type = Type(target, mapped);
                        break;
                    case string name:
                        method = name;
                        break;
                    case CustomAttributeArgument[] array when array.All(item => item.Value is TypeReference):
                        arguments = "(" + string.Join(",", array.Select(item => Type((TypeReference)item.Value, mapped))) + ")";
                        break;
                }
            }
        }

        if (type is null)
        {
            return inherited;
        }

        return type + "::" + (method ?? "(type)") + (arguments ?? "");
    }

    private static int? Priority(IEnumerable<CustomAttribute> attributes)
        => attributes.FirstOrDefault(at => at.AttributeType.FullName == "HarmonyLib.HarmonyPriority") is { ConstructorArguments: [{ Value: int priority }] } ? priority : null;

    private void FindNameSensitiveSites()
    {
        foreach (MethodDefinition method in CecilNames.AllTypes(b).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MethodReference { Name: "get_Name" or "get_FullName" } callee
                    && callee.DeclaringType.FullName is "System.Type" or "System.Reflection.MemberInfo")
                {
                    NameSensitiveSites.Add($"{CecilNames.Type(CecilNames.UserType(method.DeclaringType))}::{CecilNames.UserMethodName(method)} reads {callee.DeclaringType.Name}.{callee.Name[4..]}");
                }
            }
        }
    }

    [GeneratedRegex(@"(?<prefix>(__|DisplayClass|d__|AnonymousType|b__|g__[A-Za-z0-9_]+\|))\d+", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedOrdinal();

    [GeneratedRegex(@"<RegexGenerator_g>[0-9A-F]{16,}__", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratorHash();
}
