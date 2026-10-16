using System.Runtime.CompilerServices;

namespace Mugirl;

/// <summary>
/// 正对照：根命名空间中、PublicApi/ 下、列在 public-api.json 的公开类型（B.namespaces 不报告）。C.public-api 负对照：
/// <see cref="Patchable"/> 缺少 NoInlining，<see cref="PatchableUncalled"/> 没有任何调用方；<see cref="PatchableCalled"/> 是正对照。
/// </summary>
public static class ControlPublicAlias
{
    public static bool Patchable() => true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PatchableUncalled() => true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PatchableCalled() => true;
}
