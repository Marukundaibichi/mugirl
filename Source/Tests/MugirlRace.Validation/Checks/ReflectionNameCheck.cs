using System.Reflection;
using System.Text.RegularExpressions;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using HarmonyLib;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 代码按名字查找的成员（<c>AccessTools.Field/Method/Property…</c>、<c>FieldRefAccess</c>、<c>TypeByName</c>、
/// <c>Type.GetField/GetMethod/GetProperty</c>）在 1.7 游戏中存在。JIT 绑定发现不了拼错或被删的名字：查找返回 null，或
/// <c>static readonly</c> 绑定在游戏中第一次使用其类时抛 <c>TypeInitializationException</c>。名字以字面量、<c>nameof</c>
/// 或辅助方法参数（追溯到调用方）到达调用点。
/// </summary>
/// <remarks>重载只按名字匹配：不跟踪传给 <c>AccessTools.Method</c> 的参数类型数组。</remarks>
internal static partial class ReflectionNameCheck
{
    private const string Category = "reflection";

    private static readonly HashSet<string> FieldLookups = new(StringComparer.Ordinal) { "Field", "DeclaredField", "FieldRefAccess", "StaticFieldRefAccess" };
    private static readonly HashSet<string> PropertyLookups = new(StringComparer.Ordinal)
    {
        "Property", "DeclaredProperty", "PropertyGetter", "PropertySetter", "DeclaredPropertyGetter", "DeclaredPropertySetter",
    };

    private static readonly HashSet<string> MethodLookups = new(StringComparer.Ordinal) { "Method", "DeclaredMethod" };

    /// <param name="report">报告。</param>
    /// <param name="flow">模块的字符串流分析。</param>
    /// <param name="runtime">已加载的游戏与 Mod。</param>
    /// <param name="types">运行时类型解析。</param>
    /// <param name="dynamic">动态查找例外清单。</param>
    /// <param name="sourceOf">方法所在源码文件（相对 Source/MugirlRace），未知时为 null。</param>
    public static void Run(Report report, ModuleStringFlow flow, GameRuntime runtime, TypeResolver types, AllowList dynamic, Func<MethodDefinition, string?> sourceOf)
        => report.Run("B.reflection-names", "Members looked up by name (AccessTools, Type.GetX) exist in RimWorld 1.7", recorder => Check(recorder, flow, runtime, types, dynamic, sourceOf));

    private static void Check(CheckRecorder recorder, ModuleStringFlow flow, GameRuntime runtime, TypeResolver types, AllowList dynamic, Func<MethodDefinition, string?> sourceOf)
    {
        int checkedLookups = 0;
        var reportedDynamic = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodStringFlow method in flow.Methods)
        {
            foreach (StringCall call in method.Calls)
            {
                if (Classify(call) is not { } lookup || call.FirstStringArgument is not { } name)
                {
                    continue;
                }

                StringSet type = lookup.TypeName is { } fixedType ? StringSet.OfType(fixedType) : TypeArgument(call) ?? StringSet.Unknown;
                List<(string Location, MethodDefinition Method, StringSet Value)> names = flow.Expand(call.Location, method.Method, name).ToList();
                List<(string Location, MethodDefinition Method, StringSet Value)> typeValues = lookup.Kind == "type"
                    ? [(call.Location, method.Method, StringSet.OfType(""))]
                    : flow.Expand(call.Location, method.Method, type).ToList();
                StringSet? resolvedType = typeValues.Count == 1 && typeValues[0].Value is { IsKnown: true, IsType: true } single ? single : null;
                foreach ((string location, MethodDefinition caller, StringSet value) in names)
                {
                    if (!value.IsKnown || resolvedType is null)
                    {
                        string key = Category + "\t" + CodeLookups.Owner(caller);
                        if (!dynamic.AllowsOrReview(key, "computed member or type name; explain why the lookup cannot fail") && reportedDynamic.Add(key))
                        {
                            string what = value.IsKnown ? $"The member name \"{string.Join("\" | \"", value.Values!)}\" is a literal, but the type is computed at run time"
                                : "The member name is computed at run time";
                            recorder.Fail($"dynamic {Category} in {CodeLookups.Owner(caller)}", what + ", so the lookup cannot be verified here. "
                                + $"Use typeof/nameof, or add \"{Category}<TAB>{CodeLookups.Owner(caller)}<TAB>reason\" to Baselines/{LookupChecks.DynamicFile}.", location);
                        }

                        continue;
                    }

                    foreach (string typeName in resolvedType.Values!)
                    {
                        foreach (string member in value.Values!)
                        {
                            checkedLookups++;
                            if (Verify(lookup, typeName, member, types, runtime) is not { } problem)
                            {
                                continue;
                            }

                            // 兼容层按名字找其他 Mod 的类型是设计如此（ADR-003）：那个 Mod 不在本机的完整配置中时找不到是正常的。
                            if (lookup.Kind == "type" && sourceOf(caller)?.StartsWith("Compatibility/", StringComparison.Ordinal) == true)
                            {
                                recorder.Info($"{lookup.Api}(\"{member}\")", "Optional third-party type looked up in the Compatibility layer; its mod is not loaded here.", location);
                            }
                            else
                            {
                                recorder.Fail($"{lookup.Api}({(lookup.Kind == "type" ? "" : typeName + ", ")}\"{member}\")", problem, location);
                            }
                        }
                    }
                }
            }
        }

        dynamic.ReportProblems(recorder, key => key.StartsWith(Category + "\t", StringComparison.Ordinal));
        recorder.Summary = $"{checkedLookups} by-name lookup(s) checked";
    }

    private static string? Verify(Lookup lookup, string typeName, string name, TypeResolver types, GameRuntime runtime)
    {
        if (lookup.Kind == "type")
        {
            return AccessTools.TypeByName(name) is null ? "No loaded type has this name." : null;
        }

        string definitionName = typeName.Split('<', 2)[0];
        Type? type = types.GetTypeInAnyAssembly(definitionName) ?? runtime.Mod.GetType(definitionName, throwOnError: false);
        if (type is null)
        {
            return $"Type {typeName} is not loaded.";
        }

        bool declared = lookup.Api.StartsWith("Declared", StringComparison.Ordinal);
        switch (lookup.Kind)
        {
            case "field":
                FieldInfo? field = declared ? AccessTools.DeclaredField(type, name) : AccessTools.Field(type, name);
                if (field is null)
                {
                    return $"{type.FullName} has no field '{name}'" + (declared ? " declared on it." : ".");
                }

                if (lookup.Public && !field.IsPublic)
                {
                    return $"Field '{name}' is not public; Type.GetField(string) finds public members only.";
                }

                if (lookup.FieldType is { } wanted && !FieldTypeMatches(field.FieldType, wanted, types))
                {
                    return $"FieldRefAccess type {wanted} does not match the field type {RuntimeNames.Type(field.FieldType)}; Harmony throws ArgumentException when binding it (Tools.ValidateFieldType).";
                }

                return null;
            case "property":
                PropertyInfo? property = declared ? AccessTools.DeclaredProperty(type, name) : AccessTools.Property(type, name);
                if (property is null)
                {
                    return $"{type.FullName} has no property '{name}'.";
                }

                if (lookup.Api.EndsWith("Getter", StringComparison.Ordinal) && property.GetGetMethod(nonPublic: true) is null)
                {
                    return $"Property '{name}' has no getter.";
                }

                if (lookup.Api.EndsWith("Setter", StringComparison.Ordinal) && property.GetSetMethod(nonPublic: true) is null)
                {
                    return $"Property '{name}' has no setter.";
                }

                return lookup.Public && property.GetGetMethod(nonPublic: false) is null && property.GetSetMethod(nonPublic: false) is null
                    ? $"Property '{name}' is not public; Type.GetProperty(string) finds public members only." : null;
            case "method":
                IEnumerable<MethodInfo> methods = declared
                    ? type.GetMethods(AccessTools.allDeclared)
                    : HierarchyOf(type).SelectMany(t => t.GetMethods(AccessTools.allDeclared));
                List<MethodInfo> named = methods.Where(m => m.Name == name).ToList();
                if (named.Count == 0)
                {
                    return $"{type.FullName} has no method '{name}'" + (declared ? " declared on it." : ".");
                }

                return lookup.Public && !named.Any(m => m.IsPublic) ? $"Method '{name}' is not public; Type.GetMethod(string) finds public members only." : null;
            case "inner":
                return type.GetNestedType(name, AccessTools.all) is null ? $"{type.FullName} has no nested type '{name}'." : null;
            default:
                return null;
        }
    }

    // Harmony 3 的 Tools.ValidateFieldType<F> 的复刻：类型相同；枚举字段也可以是其底层整数类型；引用类型字段可以是字段类型能赋给的任何类型。
    // 泛型实例（List`1<Verse.X>）按名字比较：用名字解析只能得到泛型定义，与构造类型永远不相等。
    private static bool FieldTypeMatches(Type fieldType, string wantedName, TypeResolver types)
    {
        if (wantedName.Contains('<', StringComparison.Ordinal))
        {
            string normalized = GenericArity().Replace(wantedName, "");
            return Hierarchy(fieldType).Any(type => RuntimeNames.Type(type) == normalized);
        }

        Type? wanted = types.GetTypeInAnyAssembly(wantedName);
        return wanted is null || wanted == fieldType
            || (fieldType.IsEnum ? wanted == Enum.GetUnderlyingType(fieldType) : !fieldType.IsValueType && wanted.IsAssignableFrom(fieldType));
    }

    // 字段类型本身、其基类与接口（引用类型字段可以用任一可赋值类型访问）。
    private static IEnumerable<Type> Hierarchy(Type type)
    {
        if (type.IsValueType)
        {
            return [type];
        }

        return HierarchyOf(type).Concat(type.GetInterfaces());
    }

    [GeneratedRegex(@"`\d+", RegexOptions.CultureInvariant)]
    private static partial Regex GenericArity();

    private static IEnumerable<Type> HierarchyOf(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static Lookup? Classify(StringCall call)
    {
        MethodReference callee = call.Callee;
        string owner = callee.DeclaringType.FullName;
        if (owner == "HarmonyLib.AccessTools")
        {
            if (callee.Name == "TypeByName" && callee.Parameters.Count == 1)
            {
                return new Lookup("AccessTools.TypeByName", "type", null, null, Public: false);
            }

            string? kind = FieldLookups.Contains(callee.Name) ? "field"
                : PropertyLookups.Contains(callee.Name) ? "property"
                : MethodLookups.Contains(callee.Name) ? "method"
                : callee.Name == "Inner" ? "inner"
                : null;
            if (kind is null || !callee.Parameters.Any(p => p.ParameterType.FullName == "System.String"))
            {
                return null;
            }

            // FieldRefAccess<T, F>(string) / StaticFieldRefAccess<T, F>(string)：所属类型是第一个类型实参。
            if (callee is GenericInstanceMethod { GenericArguments.Count: 2 } generic && callee.Parameters.Count == 1)
            {
                return new Lookup("AccessTools." + callee.Name, kind, CecilNames.Type(generic.GenericArguments[0]), CecilNames.Type(generic.GenericArguments[1]), Public: false);
            }

            string? fieldType = callee is GenericInstanceMethod { GenericArguments.Count: 1 } single && kind == "field" ? CecilNames.Type(single.GenericArguments[0]) : null;
            return callee.Parameters[0].ParameterType.FullName == "System.Type"
                ? new Lookup("AccessTools." + callee.Name, kind, null, fieldType, Public: false)
                : null;
        }

        if (owner == "System.Type" && callee.HasThis && callee.Name is "GetField" or "GetProperty" or "GetMethod"
            && callee.Parameters.Count > 0 && callee.Parameters[0].ParameterType.FullName == "System.String")
        {
            string kind = callee.Name switch
            {
                "GetField" => "field",
                "GetProperty" => "property",
                _ => "method",
            };

            // 不带 BindingFlags 参数的这些重载只找公开成员。
            bool publicOnly = !callee.Parameters.Any(p => p.ParameterType.FullName == "System.Reflection.BindingFlags");
            return new Lookup("Type." + callee.Name, kind, null, null, publicOnly);
        }

        return null;
    }

    // Type 实参：System.Type 的方法取 this，否则取第一个 System.Type 参数。
    private static StringSet? TypeArgument(StringCall call)
    {
        if (call.Callee.DeclaringType.FullName == "System.Type")
        {
            return call.Arguments.Count > 0 ? call.Arguments[0] : null;
        }

        int offset = call.Callee.HasThis && !call.IsNewObject ? 1 : 0;
        for (int i = 0; i < call.Callee.Parameters.Count; i++)
        {
            if (call.Callee.Parameters[i].ParameterType.FullName == "System.Type")
            {
                return call.Arguments[offset + i];
            }
        }

        return null;
    }

    private sealed record Lookup(string Api, string Kind, string? TypeName, string? FieldType, bool Public);
}
