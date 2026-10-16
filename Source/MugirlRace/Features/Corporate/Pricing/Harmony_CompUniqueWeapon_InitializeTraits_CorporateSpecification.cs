using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 原版 InitializeTraits 以 MapGenerator.mapBeingGenerated?.NextGenSeed ?? parent.HashOffset() 为种子生成独特武器词条。
// 巨企订购样品要求同规格（Def、材料、品质）的预览与交付词条相同，因此只在生成期间把这个种子换成规格种子
// （CorporateNetwork.ResolveProductSeed）；词条数量、互斥校验、权重与 Setup 全部沿用原版代码。
// 1.6 用返回 false 的 Prefix 复制整段原版逻辑，会跳过其他模组在此方法上的 Transpiler 与 Harmony 3 infix；改为只替换种子。
[HarmonyPatch(typeof(CompUniqueWeapon), "InitializeTraits")]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "巨企特化武器的预览与交付词条可能不同；巨企生成范围外始终执行原版。")]
internal static class Harmony_CompUniqueWeapon_InitializeTraits_CorporateSpecification
{
    // StaticCacheLifecycle: 进程级反射句柄，补丁类型初始化时解析一次，之后不变。
    private static readonly ConstructorInfo? RandBlockConstructor = AccessTools.DeclaredConstructor(typeof(RandBlock), [typeof(int)]);

    // StaticCacheLifecycle: 进程级反射句柄，指向本模组的种子钩子（经委托取得，不按名字查找），之后不变。
    private static readonly MethodInfo SeedHook = new Func<int, int>(CorporateNetwork.ResolveProductSeed).Method;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        int anchor = -1;
        int matches = 0;
        for (int i = 0; i < codes.Count; i++)
        {
            if (RandBlockConstructor is not null && codes[i].opcode == OpCodes.Call && Equals(codes[i].operand, RandBlockConstructor))
            {
                anchor = i;
                matches++;
            }
        }

        // 锚点必须唯一：原版改动或其他补丁改写了种子结构时整体保持原版，不做半套替换。
        if (matches != 1)
        {
            MugirlLog.Warning("Corporate weapon specification: expected exactly one RandBlock(int) constructor call in "
                + "CompUniqueWeapon.InitializeTraits but found " + matches + "; corporate unique-weapon previews keep vanilla traits.");
            return codes;
        }

        // 栈上已是 (ref RandBlock, int seed)；在构造前把 seed 交给钩子。原先跳到构造指令的分支改跳到钩子。
        var hook = new CodeInstruction(OpCodes.Call, SeedHook);
        codes[anchor].MoveLabelsTo(hook);
        codes.Insert(anchor, hook);
        return codes;
    }
}
