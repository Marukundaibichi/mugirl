using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mugirl.SaveUpgrade;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade;

public sealed class JobSourceWriteProtocolTests
{
    [Fact]
    public void ActualJobWriterHasTheRequiredFieldContractRegardlessOfItsBuildId()
    {
        Assert.True(JobSourceWriteProtocol.Supported);
        Assert.True(UnresolvedThinkTreeKeys.ProtocolVerified);
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("noops", true)]
    [InlineData("wide-constants", true)]
    [InlineData("unrelated-code", true)]
    [InlineData("force-save", false)]
    [InlineData("different-default", false)]
    [InlineData("different-label", false)]
    [InlineData("wrong-field", false)]
    public void OnlyTheRelevantWriteContractControlsRetention(string variation, bool expected)
    {
        FieldInfo field = AccessTools.Field(typeof(Job), "jobGiverKey");
        MethodInfo look = typeof(Scribe_Values).GetMethod(nameof(Scribe_Values.Look))!.MakeGenericMethod(typeof(int));
        List<CodeInstruction> instructions =
        [
            new(OpCodes.Ldarg_0), new(OpCodes.Ldflda, field), new(OpCodes.Ldstr, "lastJobGiverKey"),
            new(OpCodes.Ldc_I4_M1), new(OpCodes.Ldc_I4_0), new(OpCodes.Call, look),
        ];
        if (variation == "force-save") { instructions[4] = new(OpCodes.Ldc_I4_1); }
        if (variation == "different-default") { instructions[3] = new(OpCodes.Ldc_I4_0); }
        if (variation == "different-label") { instructions[2].operand = "otherValue"; }
        if (variation == "wrong-field") { instructions[1].operand = AccessTools.Field(typeof(Job), nameof(Job.count)); }
        if (variation == "wide-constants") { instructions[3] = new(OpCodes.Ldc_I4, -1); instructions[4] = new(OpCodes.Ldc_I4_S, (sbyte)0); }
        if (variation == "noops") { instructions = [.. instructions.SelectMany(instruction => new[] { instruction, new CodeInstruction(OpCodes.Nop) })]; }
        if (variation == "unrelated-code") { instructions.InsertRange(0, [new(OpCodes.Ldstr, "new unrelated field"), new(OpCodes.Pop)]); }
        Assert.Equal(expected, JobSourceWriteProtocol.HasDefaultKeyWrite(instructions, field));
    }

    [Fact]
    public void IncompleteStepNoticeIncludesTheReasonWithoutDumpingAStackTrace()
    {
        Assert.Equal("specific cause", SaveUpgradeNotices.NoticeReason("specific cause\r\n  at Internal.Reader()"));
        Assert.Equal(321, SaveUpgradeNotices.NoticeReason(new string('x', 400)).Length);
    }
}
