using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 债务敌对是叠加层：底层好感仍独立记录其他敌对行为；还清后自动恢复原关系。
// 三个补丁都只读 CorporateNetwork.DefaultedLoanFaction，没有违约时第一步就返回，不扫描 GameComponent。
// 目标都是小方法，补丁在 Mod 构造函数中施加，调用方尚未被 JIT 编译，不受内联影响（迁移指南“补丁与 JIT 内联”）。
[HarmonyPatch(typeof(FactionUtility), nameof(FactionUtility.HostileTo))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.High, "Debt collection cannot rely on enforced combat hostility.")]
internal static class Harmony_FactionUtility_HostileTo_CorporateDebt
{
    public static void Postfix(Faction fac, Faction other, ref bool __result)
    {
        if (__result || CorporateNetwork.DefaultedLoanFaction is not { } debtor)
        {
            return;
        }

        if (CorporateNetwork.IsDebtPair(debtor, fac, other))
        {
            __result = true;
        }
    }
}
