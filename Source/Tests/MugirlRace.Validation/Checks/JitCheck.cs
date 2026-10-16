using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 与真实的 1.7 游戏、引擎和 Harmony 绑定：每个类型都能加载（否则 RimWorld 拒绝整个程序集）；每个非泛型方法都能 JIT 编译
/// （<c>RuntimeHelpers.PrepareMethod</c> 绑定方法体中的每个成员引用，签名变化在这里失败，而不是在游戏中第一次调用时）；每个
/// TypeRef/MemberRef/MethodSpec 元数据令牌都能解析，覆盖 JIT 无法编译的泛型代码。收集全部失败。
/// </summary>
internal static class JitCheck
{
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run(Report report, GameRuntime runtime)
        => report.Run("B.jit", "All types load, all methods JIT-compile, all metadata tokens resolve", recorder => Check(recorder, runtime));

    private static void Check(CheckRecorder recorder, GameRuntime runtime)
    {
        foreach (string error in runtime.ModTypeErrors)
        {
            recorder.Fail("type load: " + error, "Assembly.GetTypes() fails, so RimWorld rejects the entire assembly (ModAssemblyHandler.AssemblyIsUsable).");
        }

        int compiled = 0;
        foreach (Type type in runtime.ModTypes.Where(t => !t.ContainsGenericParameters).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            MethodBase[] methods;
            try
            {
                methods = [.. type.GetMethods(Declared), .. type.GetConstructors(Declared)];
            }
            catch (TypeLoadException exception)
            {
                recorder.Fail(RuntimeNames.Type(type), "Members cannot be enumerated: " + exception.Message);
                continue;
            }

            foreach (MethodBase method in methods)
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                {
                    continue;
                }

                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    compiled++;
                }
                catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or BadImageFormatException
                    or FileNotFoundException or FileLoadException or MemberAccessException or InvalidProgramException or VerificationException)
                {
                    recorder.Fail(RuntimeNames.Method(method), $"JIT binding failed: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        (int resolved, int contextual) = ResolveTokens(recorder, runtime.Mod);
        recorder.Summary = $"{runtime.ModTypes.Count} types, {compiled} methods compiled, {resolved} metadata tokens resolved";
        if (contextual > 0)
        {
            recorder.Info("(generic tokens)", $"{contextual} token(s) need a generic context and were checked through their compiled users only.");
        }
    }

    private static (int Resolved, int Contextual) ResolveTokens(CheckRecorder recorder, Assembly mod)
    {
        Module module = mod.ManifestModule;
        int resolved = 0;
        int contextual = 0;
        using FileStream stream = File.OpenRead(mod.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        IEnumerable<(EntityHandle Handle, string Kind)> handles = metadata.TypeReferences.Select(h => ((EntityHandle)h, "type"))
            .Concat(metadata.MemberReferences.Select(h => ((EntityHandle)h, "member")))
            .Concat(Enumerable.Range(1, metadata.GetTableRowCount(TableIndex.MethodSpec)).Select(row => ((EntityHandle)MetadataTokens.MethodSpecificationHandle(row), "method instantiation")));
        foreach ((EntityHandle handle, string kind) in handles)
        {
            int token = MetadataTokens.GetToken(handle);
            try
            {
                _ = kind == "type" ? module.ResolveType(token) : (object)module.ResolveMember(token)!;
                resolved++;
            }
            catch (ArgumentException)
            {
                contextual++;
            }
            catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or BadImageFormatException or FileNotFoundException or FileLoadException)
            {
                recorder.Fail($"{kind} token 0x{token:X8}", $"Does not resolve against the 1.7 assemblies: {exception.GetType().Name}: {exception.Message}");
            }
        }

        return (resolved, contextual);
    }
}
