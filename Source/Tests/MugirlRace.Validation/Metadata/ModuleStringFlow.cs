using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 模块内每个方法的字符串流分析，加上模块自身的调用图。辅助方法的参数值可以追溯到调用方传入的字面量（例如把参数转交给
/// <c>Scribe_Values.Look</c> 的存档辅助方法，或 <c>Required&lt;T&gt;(defName)</c> 这类 Def 查找辅助方法）。
/// </summary>
internal sealed class ModuleStringFlow
{
    private const int MaxDepth = 6;

    private readonly Dictionary<MethodDefinition, List<StringCall>> callers = [];

    public ModuleStringFlow(ModuleDefinition module)
    {
        Module = module;
        var analysis = new StringFlowAnalysis(module);
        Methods = CecilNames.AllTypes(module).SelectMany(type => type.Methods).Where(method => method.HasBody).Select(analysis.Analyze).ToList();
        string self = module.Assembly.Name.Name;
        foreach (StringCall call in Methods.SelectMany(flow => flow.Calls))
        {
            // 只能追溯进本模块的调用（解析它们不需要其他程序集）。
            if (CecilNames.AssemblyOf(call.Callee.DeclaringType) == self && CecilNames.TryResolve(call.Callee) is { } target && target.Module == module)
            {
                if (!callers.TryGetValue(target, out List<StringCall>? list))
                {
                    callers[target] = list = [];
                }

                list.Add(call);
            }
        }
    }

    public ModuleDefinition Module { get; }

    public IReadOnlyList<MethodStringFlow> Methods { get; }

    /// <summary>本模块中调用 <paramref name="method"/> 的调用点（游戏、委托与反射调用不在其中）。</summary>
    public IReadOnlyList<StringCall> CallersOf(MethodDefinition method) => callers.TryGetValue(method, out List<StringCall>? list) ? list : [];

    /// <summary>
    /// 到达某个汇点（<paramref name="container"/> 中 <paramref name="location"/> 处的调用实参或字段写入）的值。字面量或未知值
    /// 原样返回；若值是 <paramref name="container"/> 的参数，则替换为本模块每个调用方为该参数传入的值（递归，位置为调用方的
    /// 调用点）。没有任何模块内调用方的方法（游戏调用的重写方法、委托目标）的参数视为未知。
    /// </summary>
    public IEnumerable<(string Location, MethodDefinition Method, StringSet Value)> Expand(string location, MethodDefinition container, StringSet value)
        => Expand(location, container, value, 0, []);

    private IEnumerable<(string Location, MethodDefinition Method, StringSet Value)> Expand(
        string location, MethodDefinition container, StringSet value, int depth, HashSet<(MethodDefinition, int)> visited)
    {
        if (value.Parameter is not { } position || depth >= MaxDepth)
        {
            yield return (location, container, value.Parameter is null ? value : StringSet.Unknown);
            yield break;
        }

        if (!callers.TryGetValue(container, out List<StringCall>? calls) || calls.Count == 0 || !visited.Add((container, position)))
        {
            yield return (location, container, StringSet.Unknown);
            yield break;
        }

        foreach (StringCall call in calls)
        {
            int offset = call.Callee.HasThis && !call.IsNewObject ? 1 : 0;
            StringSet argument = offset + position < call.Arguments.Count ? call.Arguments[offset + position] : StringSet.Unknown;
            foreach ((string Location, MethodDefinition Method, StringSet Value) expanded in Expand(call.Location, call.Caller, argument, depth + 1, visited))
            {
                yield return expanded;
            }
        }

        visited.Remove((container, position));
    }
}
