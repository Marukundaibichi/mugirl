using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Verse;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 找出 Mod 把字符串传给按名称查找的游戏 API（Def、翻译键、贴图、Shader）的每一处，并给出字符串流分析能证明的字面量。
/// 用 Mono.Cecil 从 IL 读取：什么都不运行。
/// </summary>
internal static class CodeLookups
{
    /// <param name="flow">模块的字符串流分析。</param>
    /// <param name="types">运行时类型解析。</param>
    /// <param name="sourceOf">方法所在源码文件（相对 Source/MugirlRace），未知时为 null。</param>
    public static IReadOnlyList<LookupSite> Collect(ModuleStringFlow flow, TypeResolver types, Func<MethodDefinition, string?> sourceOf)
    {
        var sites = new List<LookupSite>();
        foreach (MethodStringFlow method in flow.Methods)
        {
            foreach (StringCall call in method.Calls)
            {
                if (Classify(call, types) is { } classified && call.FirstStringArgument is { } value)
                {
                    bool reports = !PassesFalse(call);
                    foreach ((string location, MethodDefinition caller, StringSet expanded) in flow.Expand(call.Location, method.Method, value))
                    {
                        sites.Add(new LookupSite(classified.Kind, expanded, location, Owner(caller), classified.DefType, classified.GraphicClass, reports, sourceOf(caller)));
                    }
                }
            }

            foreach (FieldStore store in method.Stores)
            {
                if (store.Field.Name == "texPath" && store.Field.DeclaringType.FullName == "Verse.GraphicData")
                {
                    foreach ((string location, MethodDefinition caller, StringSet expanded) in flow.Expand(store.Location, method.Method, store.Value))
                    {
                        sites.Add(new LookupSite(LookupKind.TexturePath, expanded, location, Owner(caller), null, null, ReportsFailure: true, sourceOf(caller)));
                    }
                }
            }
        }

        return sites;
    }

    /// <summary>包含 <paramref name="method"/> 的手写方法的 <c>Type::Method</c>（例外清单的键）。</summary>
    public static string Owner(MethodDefinition method) => CecilNames.Type(CecilNames.UserType(method.DeclaringType)) + "::" + MethodName(method);

    private static (LookupKind Kind, Type? DefType, Type? GraphicClass)? Classify(StringCall call, TypeResolver types)
    {
        MethodReference callee = call.Callee;
        TypeReference owner = callee.DeclaringType;
        string ownerName = owner is GenericInstanceType generic ? generic.ElementType.FullName : owner.FullName;
        switch (ownerName)
        {
            case "Verse.DefDatabase`1" when callee.Name is "GetNamed" or "GetNamedSilentFail":
                Type? defType = owner is GenericInstanceType instance ? Runtime(instance.GenericArguments[0], types) : null;
                bool silent = callee.Name == "GetNamedSilentFail" || PassesFalse(call);
                return (silent ? LookupKind.DefNameSilent : LookupKind.DefName, defType, null);
            case "Verse.GenDefDatabase" when callee.Name is "GetDef" or "GetDefSilentFail":
                return (callee.Name == "GetDefSilentFail" || PassesFalse(call) ? LookupKind.DefNameSilent : LookupKind.DefName, null, null);
            case "Verse.Translator" when callee.Name is "Translate" or "TranslateSimple":
            case "Verse.TranslatorFormattedStringExtensions" when callee.Name == "Translate":
                return (LookupKind.TranslationKey, null, null);
            case "Verse.Translator" when callee.Name is "CanTranslate" or "TryTranslate" or "TranslateWithBackup":
                return (LookupKind.TranslationProbe, null, null);
            case "Verse.ContentFinder`1" when owner is GenericInstanceType { GenericArguments: [{ FullName: "UnityEngine.Texture2D" }] }:
                return callee.Name switch
                {
                    "Get" => (LookupKind.TexturePath, null, typeof(Graphic_Single)),
                    "GetAllInFolder" => (LookupKind.TextureFolder, null, null),
                    _ => null,
                };
            case "Verse.GraphicDatabase" when callee.Name == "Get" && FirstParameterIsString(callee):
                Type? graphic = callee is GenericInstanceMethod getT ? Runtime(getT.GenericArguments[0], types) : null;
                return (LookupKind.TexturePath, null, graphic);
            case "Verse.MaterialPool" when callee.Name == "MatFrom" && FirstParameterIsString(callee):
                return (LookupKind.TexturePath, null, typeof(Graphic_Single));
            case "Verse.ShaderDatabase" when callee.Name == "LoadShader" && FirstParameterIsString(callee):
                return (LookupKind.ShaderPath, null, null);
        }

        // XDef.Named(string)：Def 子类上的静态查找（ThingDef.Named、SoundDef.Named 等）。
        if (callee.Name == "Named" && !callee.HasThis && callee.Parameters.Count == 1 && FirstParameterIsString(callee)
            && Runtime(owner, types) is { } named && typeof(Def).IsAssignableFrom(named))
        {
            return (LookupKind.DefName, named, null);
        }

        return null;
    }

    // 调用末尾 bool 参数（errorOnFail / reportFailure）的字面量 false 就是调用前一条指令。
    private static bool PassesFalse(StringCall call)
    {
        if (call.Callee.Parameters.Count == 0 || call.Callee.Parameters[^1].ParameterType.FullName != "System.Boolean")
        {
            return false;
        }

        Instruction? previous = call.Caller.Body.Instructions.FirstOrDefault(i => i.Offset == call.Offset)?.Previous;
        return previous?.OpCode.Code == Code.Ldc_I4_0;
    }

    private static bool FirstParameterIsString(MethodReference method)
        => method.Parameters.Count > 0 && method.Parameters[0].ParameterType.FullName == "System.String";

    private static Type? Runtime(TypeReference type, TypeResolver types)
        => type is GenericParameter ? null : types.GetTypeInAnyAssembly(CecilNames.Type(type));

    // lambda 与迭代器体归到包含它们的方法（"<GetGizmos>d__5" -> GetGizmos）。
    private static string MethodName(MethodDefinition method)
    {
        foreach (string generated in new[] { method.Name, method.DeclaringType.Name })
        {
            int close = generated.IndexOf('>', StringComparison.Ordinal);
            if (generated.StartsWith('<') && close > 1)
            {
                return generated[1..close];
            }
        }

        return method.Name;
    }
}
