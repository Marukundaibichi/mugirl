using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>只核对补写来源所依赖的字段写出约定；无关方法体、编译器改写和程序集版本不参与判断。</summary>
internal static class JobSourceWriteProtocol
{
    // StaticCacheLifecycle: 原版方法元数据在进程内不变，首次使用时读取一次；不持有 Job 或存档。
    private static readonly Lazy<bool> Available = new(Check);

    internal static bool Supported => Available.Value;

    private static bool Check()
    {
        try
        {
            FieldInfo? field = AccessTools.Field(typeof(Job), "jobGiverKey");
            if (field?.FieldType != typeof(int)) { return false; }
            return HasDefaultKeyWrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Job), nameof(Job.ExposeData))), field);
        }
        catch (Exception) { return false; }
    }

    internal static bool HasDefaultKeyWrite(IEnumerable<CodeInstruction> instructions, FieldInfo field)
    {
        CodeInstruction[] code = [.. instructions.Where(instruction => instruction.opcode != OpCodes.Nop)];
        for (int i = 1; i + 3 < code.Length; i++)
        {
            if (code[i].opcode != OpCodes.Ldstr || !Equals(code[i].operand, "lastJobGiverKey")
                || code[i - 1].opcode != OpCodes.Ldflda || !Equals(code[i - 1].operand, field)
                || !Integer(code[i + 1], -1) || !Integer(code[i + 2], 0)
                || code[i + 3].opcode != OpCodes.Call || code[i + 3].operand is not MethodInfo method
                || method.DeclaringType != typeof(Scribe_Values) || method.Name != nameof(Scribe_Values.Look)
                || !method.IsGenericMethod || !method.GetGenericArguments().SequenceEqual([typeof(int)])) { continue; }
            Type[] parameters = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];
            if (parameters.SequenceEqual([typeof(int).MakeByRefType(), typeof(string), typeof(int), typeof(bool)])) { return true; }
        }
        return false;
    }

    private static bool Integer(CodeInstruction instruction, int value)
        => value == -1 && instruction.opcode == OpCodes.Ldc_I4_M1
            || value == 0 && instruction.opcode == OpCodes.Ldc_I4_0
            || (instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S)
                && Convert.ToInt32(instruction.operand, System.Globalization.CultureInfo.InvariantCulture) == value;
}
