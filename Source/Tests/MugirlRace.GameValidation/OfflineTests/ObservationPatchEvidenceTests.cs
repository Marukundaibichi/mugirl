using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class ObservationPatchEvidenceTests : IDisposable
{
    private static readonly MethodInfo Target = typeof(Pawn_ApparelTracker).GetMethod(nameof(Pawn_ApparelTracker.ExposeData))!;
    private static readonly MethodInfo Production = typeof(Mugirl.Features.Restraints.Harmony_Pawn_ApparelTracker_ExposeData).GetMethod("Postfix")!;
    private readonly Harmony harmony = new("MugirlMod.Mod");
    private readonly Harmony foreign = new("A5.UnknownPatch");

    public void Dispose()
    {
        harmony.Unpatch(Target, Production);
        foreign.UnpatchAll(foreign.Id);
    }

    [Fact]
    public void OnlyTheExactPhaseGuardedProductionPostfixHasAProof()
    {
        harmony.CreateClassProcessor(Production.DeclaringType!).Patch();
        Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.PatchMethod == Production);
        string? proof = ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix");
        Assert.Contains("PostLoadInit-only", proof, StringComparison.Ordinal);
        Assert.Contains("ResolvingCrossRefs/Saving", proof, StringComparison.Ordinal);
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "prefix"));
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(typeof(Pawn_PsychicEntropyTracker).GetMethod("ExposeData")!, patch, "postfix"));
        var evidence = ObservationPatchEvidence.Describe(Target, patch, "postfix", "offline exact production class", proof);
        Assert.Equal("MugirlMod.Mod", (string?)evidence["owner"]);
        Assert.Equal(400, (int?)evidence["priority"]);
        Assert.Equal("Mugirl.Features.Restraints.Harmony_Pawn_ApparelTracker_ExposeData", (string?)evidence["declaringType"]);
        Assert.Equal(64, ((string)evidence["semanticIlSha256"]!).Length);
        Assert.NotEmpty((string)evidence["mvid"]!);
        Assert.NotEmpty((string)evidence["token"]!);
    }

    [Fact]
    public void AnAdditionalPatchOnTheApprovedPostfixRevokesItsProof()
    {
        harmony.CreateClassProcessor(Production.DeclaringType!).Patch();
        Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.PatchMethod == Production);
        Assert.NotNull(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
        foreign.Patch(Production, prefix: new HarmonyMethod(typeof(ObservationPatchEvidenceTests), nameof(Noop)));
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
        Assert.Single(ObservationPatchEvidence.Describe(Target, patch, "postfix", "negative control", null)["patchMethodHooks"]!.AsArray());
    }

    [Fact]
    public void CorrectCodeUnderAnotherOwnerStillFailsTheExactContract()
    {
        foreign.Patch(Target, postfix: new HarmonyMethod(Production));
        Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.owner == foreign.Id);
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
    }

    [Fact]
    public void AChangedPriorityIsNotAcceptedByTheReviewedContract()
    {
        harmony.Patch(Target, postfix: new HarmonyMethod(Production) { priority = Priority.First });
        Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.PatchMethod == Production);
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
    }

    [Fact]
    public void TheSameGuardedBodyWithATypeInitializerHasNoProof()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MugirlRace"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule("negative-control").DefineType(Production.DeclaringType!.FullName!, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        type.DefineTypeInitializer().GetILGenerator().Emit(OpCodes.Ret);
        MethodBuilder method = type.DefineMethod("Postfix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(Pawn_ApparelTracker)]);
        method.DefineParameter(1, ParameterAttributes.None, "__instance");
        ILGenerator body = method.GetILGenerator();
        Label done = body.DefineLabel();
        body.Emit(OpCodes.Ldsfld, typeof(Scribe).GetField(nameof(Scribe.mode))!);
        body.Emit(OpCodes.Ldc_I4_4);
        body.Emit(OpCodes.Bne_Un_S, done);
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Brfalse_S, done);
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Ldfld, typeof(Pawn_ApparelTracker).GetField(nameof(Pawn_ApparelTracker.pawn))!);
        body.Emit(OpCodes.Call, Production.Module.Assembly.GetType("Mugirl.Features.Restraints.RestraintApparelUtility")!.GetMethod("EnsureWornRestraintLocks")!);
        body.MarkLabel(done);
        body.Emit(OpCodes.Ret);
        MethodInfo imitation = type.CreateType()!.GetMethod("Postfix")!;
        Assert.Equal(ObservationPatchEvidence.Fingerprint(Production), ObservationPatchEvidence.Fingerprint(imitation));
        harmony.Patch(Target, postfix: new HarmonyMethod(imitation));
        try
        {
            Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.PatchMethod == imitation);
            Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
        }
        finally { harmony.Unpatch(Target, imitation); }
    }

    private static void Noop() { }

    [Theory]
    [InlineData("prefix", "innerPrefix")]
    [InlineData("postfix", "innerPostfix")]
    [InlineData("finalizer", "innerFinalizer")]
    public void NestedInnerHooksRevokeTheReviewedPostfixAndAppearInEvidence(string role, string evidenceRole)
    {
        harmony.CreateClassProcessor(Production.DeclaringType!).Patch();
        Patch patch = Harmony.GetPatchInfo(Target)!.Postfixes.Single(p => p.PatchMethod == Production);
        Assert.NotNull(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
        MethodInfo inner = new CurrentScribeRoots(Production.Module.Assembly).DirectCalls(Production).Single(m => m.Name == "EnsureWornRestraintLocks");
        var hook = new HarmonyMethod(typeof(ObservationPatchEvidenceTests), nameof(Noop)) { innerMethod = new InnerMethod(inner, []) };
        PatchProcessor processor = foreign.CreateProcessor(Production);
        if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
        processor.Patch();
        Assert.Null(ObservationPatchEvidence.GuardedProductionProof(Target, patch, "postfix"));
        var hooks = ObservationPatchEvidence.Describe(Target, patch, "postfix", "negative inner control", null)["patchMethodHooks"]!.AsArray();
        Assert.Equal(evidenceRole, (string?)Assert.Single(hooks)!["role"]);
    }
}
