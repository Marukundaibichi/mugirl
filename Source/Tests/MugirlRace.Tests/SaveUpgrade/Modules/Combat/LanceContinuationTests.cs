using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Features.Lances;
using Mugirl.SaveUpgrade;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[Collection("Reference aliases")]
public sealed class LanceContinuationTests
{
    private static UpgradeTables Tables()
    {
        var builder = new UpgradeTableBuilder();
        UpgradeModuleBuilder module = builder.Module("Combat", 600);
        module.RenameDef("JobDef", "Job_MugirlLancePointCharge", "Mugirl_LancePointCharge", "test");
        module.RenameDef("JobDef", "Job_MugirlLanceLineCharge", "Mugirl_LanceLineCharge", "test");
        module.Step(new LanceJobContinuationStep());
        module.ClaimJobDriver("Mugirl.Features.Lances.JobDriver_LanceCharge", LanceJobContinuationStep.StepId);
        return builder.Build();
    }

    [Theory]
    [InlineData(-1, -1, 1, false, false)]
    [InlineData(-1, 0, 1, false, false)]
    [InlineData(0, -1, 1, false, false)]
    [InlineData(0, 1, 1, true, false)]
    [InlineData(0, -1, -1, true, true)]
    [InlineData(0, 1, -1, true, true)]
    [InlineData(1, 2, -1, true, true)]
    public void DistinguishesPublishedLaunchMarkersWithoutUsingGameVersion(int current, int next, int count, bool thingTarget, bool resumesWalking)
    {
        Assert.Equal(resumesWalking, LanceJobContinuation.Resolve(new(current, next, count, false, thingTarget)) == LanceJobContinuation.Action.ResumeWalking);
    }

    [Theory]
    [InlineData(0, -1, 0, false, true)]
    [InlineData(2, -1, -1, false, true)]
    [InlineData(1, -1, 1, false, true)]
    [InlineData(0, -1, -1, false, false)]
    [InlineData(0, -1, -1, true, true)]
    [InlineData(-1, 1, -1, false, true)]
    [InlineData(-1, -1, -1, false, true)]
    [InlineData(-1, 0, -1, false, true)]
    [InlineData(0, -1, 2, false, true)]
    [InlineData(-1, 0, 2, false, true)]
    public void RejectsUnprovenOrInvalidStateWithoutEndingAJob(int current, int next, int count, bool ended, bool target)
    {
        Assert.Throws<InvalidOperationException>(() => LanceJobContinuation.Resolve(new(current, next, count, ended, target)));
    }

    [Fact]
    public void DomChangesOnlyTheLegacyDriverClassAndPreservesAllBaseState()
    {
        const string Driver = "<curToilIndex>0</curToilIndex><nextToilIndex>1</nextToilIndex><ticksLeftThisToil>-37</ticksLeftThisToil>"
            + "<wantBeginNextToil>True</wantBeginNextToil><startTick>91</startTick><locomotionUrgencySameAs>Thing_Human3</locomotionUrgencySameAs>";
        const string Job = "<def>Job_MugirlLanceLineCharge</def><loadID>87</loadID><targetA>Thing_Human2</targetA><playerForced>True</playerForced>";
        XmlDocument document = SaveXml.Save("<pawn><jobs><curJob>" + Job + "</curJob><curDriver Class=\"Mugirl.Features.Lances.JobDriver_LanceCharge\">" + Driver + "</curDriver></jobs></pawn>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        Assert.False(session.Report.Failed, session.Report.Error);
        XmlElement jobs = document.DocumentElement!["game"]!["pawn"]!["jobs"]!;
        Assert.Equal(Job, jobs["curJob"]!.InnerXml);
        Assert.Equal(Driver, jobs["curDriver"]!.InnerXml);
        Assert.Equal(typeof(JobDriver_ResumeLanceCharge).FullName, jobs["curDriver"]!.GetAttribute("Class"));
        string once = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, Tables()).Report.Failed);
        Assert.Equal(once, document.OuterXml);
    }

    [Fact]
    public void LaunchedFlyerJobAndUnstartedJobRemainByteEquivalent()
    {
        const string Body = "<pawn><jobs><curJob><def>Mugirl_LanceLineCharge</def><count>1</count><targetA>(7, 0, 8)</targetA></curJob>"
            + "<curDriver Class=\"Mugirl.Features.Lances.JobDriver_LanceCharge\"><curToilIndex>0</curToilIndex><wantBeginNextToil>True</wantBeginNextToil></curDriver></jobs></pawn>"
            + "<pawn><jobs><curJob><def>Mugirl_LanceLineCharge</def><targetA>(7, 0, 8)</targetA></curJob>"
            + "<curDriver Class=\"Mugirl.Features.Lances.JobDriver_LanceCharge\"><curToilIndex>-1</curToilIndex></curDriver></jobs></pawn>";
        Assert.Equal(Body, SaveXml.Upgraded(Body, Tables()));
    }

    [Theory]
    [InlineData("Mugirl_LancePointCharge", "Thing_Human2")]
    [InlineData("Mugirl_LanceLineCharge", "Thing_Human2")]
    [InlineData("Mugirl_LancePointCharge", "(7, 0, 8)")]
    [InlineData("Mugirl_LanceLineCharge", "(7, 0, invalid)")]
    [InlineData("OtherJob", "(7, 0, 8)")]
    public void UnstartedAmbiguousOrUnsupportedJobFailsWithoutChangingSavedData(string job, string target)
    {
        XmlDocument document = SaveXml.Save("<pawn><jobs><curJob><def>" + job + "</def><targetA>" + target + "</targetA></curJob>"
            + "<curDriver Class=\"Mugirl.Features.Lances.JobDriver_LanceCharge\"><curToilIndex>-1</curToilIndex></curDriver></jobs></pawn>");
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        Assert.True(session.Report.Failed);
        Assert.Equal(LanceJobContinuationStep.StepId, session.Report.FailedStep);
        Assert.Equal(before, document.OuterXml);
    }

    [Theory]
    [InlineData(0, -1, false)]
    [InlineData(1, 2, true)]
    public void RealScribePostLoadBuildsBothToilsWithoutReplayingAndResavesExactly(int current, int next, bool wantNext)
    {
        string xml = "<root><target><number>7</number></target><driver><curToilIndex>" + current
            + "</curToilIndex><nextToilIndex>" + next + "</nextToilIndex><ticksLeftThisToil>-17</ticksLeftThisToil><wantBeginNextToil>"
            + wantNext + "</wantBeginNextToil><startTick>93</startTick><mugirlLanceResumeSmokeTicks>5</mugirlLanceResumeSmokeTicks>"
            + "<mugirlLanceResumeHitTargets><li>Thing_TestTarget7</li></mugirlLanceResumeHitTargets></driver></root>";
        var first = Read(xml);
        Assert.Equal(current, Field<int>(first.Driver, "curToilIndex", typeof(JobDriver)));
        Assert.Equal(next, Field<int>(first.Driver, "nextToilIndex", typeof(JobDriver)));
        Assert.Equal(-17, first.Driver.ticksLeftThisToil);
        Assert.Equal(wantNext, Field<bool>(first.Driver, "wantBeginNextToil", typeof(JobDriver)));
        Assert.Equal(5, Field<int>(first.Driver, "smokeTick", typeof(JobDriver_ResumeLanceCharge)));
        var hit = Field<HashSet<Thing>>(first.Driver, "hitThings", typeof(JobDriver_ResumeLanceCharge));
        Assert.Same(first.Target, Assert.Single(hit));
        List<Toil> toils = Field<List<Toil>>(first.Driver, "toils", typeof(JobDriver));
        Assert.Equal(2, toils.Count);
        Assert.Equal(ToilCompleteMode.Instant, toils[1].defaultCompleteMode);
        Assert.Null(first.Driver.pawn.pather); // Goto.initAction would dereference it; loading did not run that action.
        string saved = Write(first.Target, first.Driver);
        var second = Read(saved);
        Assert.Equal(saved, Write(second.Target, second.Driver));
        Assert.Same(second.Target, Assert.Single(Field<HashSet<Thing>>(second.Driver, "hitThings", typeof(JobDriver_ResumeLanceCharge))));
    }

    private static T Field<T>(object value, string name, Type declaring) =>
        (T)declaring.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;

    private static (FixtureTarget Target, JobDriver_ResumeLanceCharge Driver) Read(string text)
    {
        using var errors = new ScribeErrorCapture();
        ScribeLoader previous = Scribe.loader;
        LoadSaveMode mode = Scribe.mode;
        var mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        mod.assemblies = new ModAssemblyHandler(mod);
        mod.assemblies.loadedAssemblies.Add(typeof(JobDriver_ResumeLanceCharge).Assembly);
        mod.assemblies.loadedAssemblies.Add(typeof(LanceContinuationTests).Assembly);
        LoadedModManager.RunningModsListForReading.Add(mod);
        GenTypes.ClearCache();
        try
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml(text);
            var root = new FixtureRoot();
            Scribe.loader = new ScribeLoader { curXmlParent = xml.DocumentElement, curParent = root };
            Scribe.mode = LoadSaveMode.LoadingVars;
            root.ExposeData();
            Assert.NotNull(root.Target);
            Assert.NotNull(root.Driver);
            var pawn = new Pawn();
            var job = new Job { def = new JobDef { defName = "Mugirl_LanceLineCharge" }, targetA = root.Target, count = -1 };
            pawn.jobs = new Pawn_JobTracker(pawn) { curJob = job, curDriver = root.Driver };
            root.Driver.pawn = pawn;
            root.Driver.job = job;
            Scribe.loader.curXmlParent = null;
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
            return (root.Target, root.Driver);
        }
        finally
        {
            LoadedModManager.RunningModsListForReading.Remove(mod);
            GenTypes.ClearCache();
            Scribe.loader = previous;
            Scribe.mode = mode;
        }
    }

    private static string Write(FixtureTarget target, JobDriver_ResumeLanceCharge driver)
    {
        string file = Path.Combine(Path.GetTempPath(), "mugirl-lance-continuation-" + Guid.NewGuid().ToString("N") + ".xml");
        ScribeSaver previous = Scribe.saver;
        LoadSaveMode mode = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(file, "root");
            new FixtureRoot { Target = target, Driver = driver }.ExposeData();
            Scribe.saver.FinalizeSaving();
            var xml = new XmlDocument { XmlResolver = null };
            xml.Load(file);
            return xml.DocumentElement!.OuterXml;
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe.saver.ForceStop();
            }
            Scribe.saver = previous;
            Scribe.mode = mode;
            File.Delete(file);
        }
    }

    private sealed class FixtureRoot : IExposable
    {
        internal FixtureTarget? Target;
        internal JobDriver_ResumeLanceCharge? Driver;
        public void ExposeData()
        {
            Scribe_Deep.Look(ref Target, "target");
            Scribe_Deep.Look(ref Driver, "driver");
        }
    }

    private sealed class FixtureTarget : ThingWithComps
    {
        public FixtureTarget()
        {
            def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.defName = "TestTarget";
            def.stackLimit = 1;
        }
        public override void ExposeData() => Scribe_Values.Look(ref thingIDNumber, "number", 0);
    }
}
