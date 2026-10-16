using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 装备页中被锁定的束具，把原版“已锁定”提示换成“需要对应钥匙”。
// 锚点：ldstr "DropThingLocked" 紧接 call Translator.Translate(string)，后跟 TipSignal.op_Implicit（1.7 IL_0147/IL_014C，已核对）。
// 替换为 ldarg.3（thing 参数）+ call DropThingTooltip，返回值同为 TaggedString，栈类型不变。
[HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
[MugirlPatch(PatchModule.Restraints, PatchRisk.Critical, "Advanced slave-apparel drop tooltip injection is skipped and a once-warning is emitted.")]
public static class Harmony_ITab_Pawn_Gear_DrawThingRow
{
    private const string LockedTooltipKey = "DropThingLocked";

    // StaticCacheLifecycle: 进程级反射结果，补丁期间解析一次；不持有游戏对象，无需清理。
    private static readonly MethodInfo TranslateMethod =
        AccessTools.Method(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) });

    private static readonly MethodInfo TooltipMethod =
        AccessTools.Method(typeof(Harmony_ITab_Pawn_Gear_DrawThingRow), nameof(DropThingTooltip));

    // BondageToys 有自己的提示处理；1.6 在 Transpiler 内原样返回指令，现在直接跳过整个补丁类。
    public static bool Prepare()
    {
        return ModLister.GetActiveModWithIdentifier("rimworld.bondagetoys") == null;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
        bool patched = false;

        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction code = codes[i];
            if (code.opcode == OpCodes.Ldstr
                && code.OperandIs(LockedTooltipKey)
                && i + 1 < codes.Count
                && codes[i + 1].Calls(TranslateMethod))
            {
                // 原版 DrawThingRow 的 thing 参数稳定可用，不能依赖编译器生成的局部变量编号。
                // 被替换指令上的标签与异常块转移到新指令，避免跳转目标丢失。
                yield return new CodeInstruction(OpCodes.Ldarg_3).MoveLabelsFrom(code).MoveBlocksFrom(code);
                yield return new CodeInstruction(OpCodes.Call, TooltipMethod).MoveLabelsFrom(codes[i + 1]).MoveBlocksFrom(codes[i + 1]);
                i++;
                patched = true;
                continue;
            }

            yield return code;
        }

        if (!patched)
        {
            // 补丁在 Mod 构造函数中执行，语言尚未加载：先用英文 fallback，语言就绪后才求值翻译。
            MugirlLog.StartupWarningOnce(
                "DropThingTooltip.TranspilerNotApplied",
                () => "Mugirl.DropThingTooltip.TranspilerNotApplied".Translate().ToString(),
                "Gear drop tooltip patch was skipped because the expected vanilla IL pattern was not found.");
        }
    }

    public static TaggedString DropThingTooltip(Thing thing)
    {
        if (thing is RestraintApparel)
        {
            return "Mugirl.SlaveApparelCannotBeRemoved".Translate();
        }

        return LockedTooltipKey.Translate();
    }
}
