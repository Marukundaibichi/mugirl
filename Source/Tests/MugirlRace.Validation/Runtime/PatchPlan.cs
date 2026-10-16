using System.Reflection;

namespace MugirlRace.Validation.Runtime;

/// <summary>Harmony 3 将为 Mod 补丁类应用的补丁，由 <c>B.patch-targets</c> 解析得出。</summary>
internal sealed class PatchPlan
{
    public List<PlannedPatch> Patches { get; } = [];

    /// <summary><c>Prepare()</c> 返回 false 的补丁类。</summary>
    public List<Type> Skipped { get; } = [];

    /// <summary>解析运行过后为 true（Harmony 相关检查只在此时与它比较）。</summary>
    public bool Resolved { get; set; }
}

/// <summary>应用到一个原方法上的一个补丁方法。</summary>
/// <param name="PatchClass">补丁类。</param>
/// <param name="Role">Prefix、Postfix、Transpiler、Finalizer、ReversePatch 或中缀角色。</param>
/// <param name="PatchMethod">补丁方法。</param>
/// <param name="Target">原方法。</param>
internal sealed record PlannedPatch(Type PatchClass, string Role, MethodInfo PatchMethod, MethodBase Target)
{
    /// <summary>与 Harmony 实际应用结果比较时的身份。</summary>
    public string Key => KeyOf(PatchClass, Role, PatchMethod.Name, Target);

    /// <summary><c>PatchClass&lt;TAB&gt;Role[ (method)]&lt;TAB&gt;Target</c>；方法名与角色不同时才显示方法名。</summary>
    public static string KeyOf(Type patchClass, string role, string methodName, MethodBase target)
        => $"{RuntimeNames.Type(patchClass)}\t{role}{(methodName == role ? "" : " (" + methodName + ")")}\t{RuntimeNames.Method(target)}";
}
