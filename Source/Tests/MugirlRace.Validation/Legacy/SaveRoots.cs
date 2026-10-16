using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 以 <c>Class="完整类型名"</c> 写进存档的基类（“存档根”），从游戏自己的 IL 推出而不是手写：<c>Scribe_Deep.Look&lt;T&gt;</c> 的 T，
/// <c>Scribe_Collections.Look</c> 以 <c>LookMode.Deep</c> 保存的元素类型（字典按键与值各自的 LookMode），加上被分析模块自己这样
/// 保存的非密封类型。运行时类型与声明类型不同的对象都带 Class 特性，所以任何根的子类都在存档中以类名出现。
/// </summary>
/// <remarks>
/// 声明类型为 <c>object</c> 或没有约束的类型形参时（例如原版 <c>CompStatue</c> 的 <c>Dictionary&lt;string, object&gt;</c>），根记为
/// <c>Verse.IExposable</c>：<c>ScribeExtractor.SaveableFromNode</c> 只创建实现 IExposable 的非抽象类型。静态、抽象、接口、值类型与委托
/// 永远不会以类名出现在存档中，没有存档根。
/// </remarks>
internal sealed class SaveRoots
{
    /// <summary>声明类型为 <c>object</c> 或无约束形参时的根：游戏只实例化实现它的类型。</summary>
    public const string ExposableRoot = "Verse.IExposable";

    // Verse.LookMode.Deep（1.6 与 1.7 相同：Undefined 0, Value 1, Deep 2, Reference 3, Def 4, ...）。
    private const int LookModeDeep = 2;

    private readonly HashSet<string> roots;

    private SaveRoots(HashSet<string> roots) => this.roots = roots;

    /// <summary>根类型的完整名（序数排序）。</summary>
    public IEnumerable<string> Names => roots.Order(StringComparer.Ordinal);

    public int Count => roots.Count;

    /// <summary>扫描 <paramref name="modules"/>（游戏程序集与被分析模块）。</summary>
    public static SaveRoots Derive(IEnumerable<ModuleDefinition> modules)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModuleDefinition module in modules)
        {
            foreach (MethodDefinition method in CecilNames.AllTypes(module).SelectMany(type => type.Methods).Where(method => method.HasBody))
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code is Code.Call or Code.Callvirt && instruction.Operand is GenericInstanceMethod generic)
                    {
                        foreach (TypeReference element in DeepElements(method, instruction, generic))
                        {
                            AddRoot(found, element);
                        }
                    }
                }
            }
        }

        // 设置文件：ReadModSettings 以抽象 ModSettings 为声明类型（同样由扫描得到；这里保证游戏程序集缺失时也成立）。
        found.Add("Verse.ModSettings");
        return new SaveRoots(found);
    }

    public bool Contains(string typeName) => roots.Contains(typeName);

    /// <summary>这些根加上 <paramref name="module"/> 自己以 Deep 保存的类型（游戏程序集只需扫描一次）。</summary>
    public SaveRoots With(ModuleDefinition module)
    {
        SaveRoots own = Derive([module]);
        var union = new HashSet<string>(roots, StringComparer.Ordinal);
        union.UnionWith(own.roots);
        return new SaveRoots(union);
    }

    /// <summary>
    /// <paramref name="type"/> 的存档根：自身或最近的祖先中第一个是根的类型；不以类名存档时为 <see langword="null"/>。
    /// 对象只有在运行时类型不同于声明类型时才写 Class，所以类型本身是根且没有更上层的根时也算（它的子类以类名出现）。
    /// </summary>
    public string? RootOf(TypeDefinition type)
    {
        if (type.IsInterface || type.IsValueType || type.IsAbstract || type.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate")
        {
            return null;
        }

        string? result = null;
        foreach (string baseName in CecilNames.BaseTypeNames(type))
        {
            if (roots.Contains(baseName))
            {
                result = baseName;
                break;
            }
        }

        if (result is null)
        {
            foreach (string iface in Interfaces(type))
            {
                if (roots.Contains(iface))
                {
                    return iface;
                }
            }
        }

        return result;
    }

    // Deep 保存的元素类型。Scribe_Deep 的 T；Scribe_Collections 的元素类型（按其 LookMode 参数为 Deep 的位置）。
    private static IEnumerable<TypeReference> DeepElements(MethodDefinition method, Instruction call, GenericInstanceMethod generic)
    {
        TypeReference owner = generic.DeclaringType;
        if (owner.Namespace != "Verse" || generic.Name != "Look")
        {
            yield break;
        }

        if (owner.Name == "Scribe_Deep")
        {
            yield return generic.GenericArguments[0];
            yield break;
        }

        if (owner.Name != "Scribe_Collections")
        {
            yield break;
        }

        MethodReference element = generic.ElementMethod;
        int offset = element.HasThis ? 1 : 0;
        int lookModeIndex = 0;
        for (int i = 0; i < element.Parameters.Count; i++)
        {
            if (element.Parameters[i].ParameterType.FullName != "Verse.LookMode")
            {
                continue;
            }

            // 第 n 个 LookMode 参数对应第 n 个泛型实参（列表一个，字典的键与值两个）。
            int argument = Math.Min(lookModeIndex, generic.GenericArguments.Count - 1);
            lookModeIndex++;
            if (IlArguments.Producer(method, call, offset + i) is { } producer && IlArguments.IntConstant(producer) == LookModeDeep)
            {
                yield return generic.GenericArguments[argument];
            }
        }
    }

    private static void AddRoot(HashSet<string> found, TypeReference type)
    {
        if (type is GenericParameter parameter)
        {
            // where T : Thing 之类的约束就是实际的根；没有类约束时只能是 IExposable 的实现。
            bool constrained = false;
            foreach (GenericParameterConstraint constraint in parameter.Constraints)
            {
                if (constraint.ConstraintType.FullName is not ("System.Object" or "System.ValueType"))
                {
                    constrained = true;
                    AddRoot(found, constraint.ConstraintType);
                }
            }

            if (!constrained)
            {
                found.Add(ExposableRoot);
            }

            return;
        }

        if (type.FullName == "System.Object")
        {
            found.Add(ExposableRoot);
            return;
        }

        TypeReference element = type is GenericInstanceType instance ? instance.ElementType : type;
        if (CecilNames.TryResolve(element) is not { } definition || definition.IsSealed || definition.IsValueType)
        {
            return;
        }

        // IExposable 本身作声明类型时，所有实现都可能以类名出现；保留它，RootOf 按接口匹配。
        found.Add(CecilNames.Type(element));
    }

    private static IEnumerable<string> Interfaces(TypeDefinition type)
    {
        for (TypeDefinition? current = type; current is not null;)
        {
            foreach (InterfaceImplementation implementation in current.Interfaces)
            {
                yield return CecilNames.Type(implementation.InterfaceType is GenericInstanceType generic ? generic.ElementType : implementation.InterfaceType);
            }

            current = CecilNames.TryResolve(current.BaseType);
        }
    }
}
