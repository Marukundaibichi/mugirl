using HarmonyLib;

namespace Mugirl;

/// <summary>B.patch-targets / B.harmony-apply：目标不存在；B.patch-metadata：没有 [MugirlPatch]。</summary>
[HarmonyPatch(typeof(Pawn), "NoSuchMethodInRimWorld")]
public static class Pawn_NoSuchMethod_Patch
{
    public static void Postfix()
    {
    }
}

/// <summary>B.patch-targets：辅助方法用了 Harmony 保留名 Prepare 且返回类型错误。</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
[MugirlPatch(PatchModule.Core, PatchRisk.Low, "control")]
public static class Pawn_ExposeData_MisnamedHelperPatch
{
    public static string Prepare() => "not a bool";

    public static void Postfix()
    {
    }
}

/// <summary>B.patch-targets：有效补丁，但（空的）黄金清单没有它；B.patch-metadata：failureBehavior 为空。</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
[MugirlPatch(PatchModule.Core, (PatchRisk)999, "")]
public static class Pawn_SpawnSetup_ControlPatch
{
    public static void Postfix()
    {
    }
}

/// <summary>B.transpilers / B.log：锚点“找不到”的 Transpiler，写日志且什么都不改。</summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonist), MethodType.Getter)]
[MugirlPatch(PatchModule.Core, PatchRisk.Low, "control")]
public static class Pawn_IsColonist_NoOpTranspilerPatch
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Log.Warning("[Mugirl] control transpiler: anchor not found");
        return instructions;
    }
}

/// <summary>B.patch-metadata：[MugirlPatch] 标在不是补丁类的类型上。</summary>
[MugirlPatch(PatchModule.Core, PatchRisk.Low, "stale metadata")]
public static class NotAPatchClass
{
    public static int Value() => 1;
}
