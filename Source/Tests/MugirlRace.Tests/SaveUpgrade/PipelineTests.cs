using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>会话识别、流水线的计划/应用失败语义、幂等性、读档后阶段与诊断报告。</summary>
public sealed class PipelineTests
{
    private static UpgradeTables RenameTables() => SaveXml.Tables(m =>
    {
        m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
        m.RenameDef("ThingDef", "Mugirl_OldGun", "Mugirl_Gun_Rifle", "renamed");
        m.DropDef("ThingDef", "Mugirl_OldArmor", DefDropPolicy.RemoveThings, "retired");
        m.RenameLabel(OwnerSelector.Class(typeof(Fakes.NewComponent)), "oldLabel", "newLabel", "renamed");
        m.ResetLordGraph("Mugirl.OldLordJob");
    });

    private const string LegacyBody =
        "<components><li Class=\"Mugirl.OldComponent\"><oldLabel>1</oldLabel></li></components>"
        + "<maps><li><things><thing><def>Mugirl_OldGun</def><id>Mugirl_OldGun1</id></thing><thing><def>Mugirl_OldArmor</def><id>Mugirl_OldArmor2</id></thing></things>"
        + "<lordManager><lords><li><loadID>4</loadID><lordJob Class=\"Mugirl.OldLordJob\" /><lordToilData><keys><li>1</li></keys><values><li Class=\"X\" /></values></lordToilData><curLordToilIdx>2</curLordToilIdx></li></lords></lordManager>"
        + "<refs><li>Thing_Mugirl_OldGun1</li><li>Thing_Mugirl_OldArmor2</li></refs></li></maps>";

    [Fact]
    public void DetectsSchemaFromTheStampLabelAndModList()
    {
        UpgradeTables tables = RenameTables();

        SaveUpgradeSession noMugirl = SaveXml.Upgrade(SaveXml.Save(LegacyBody, mugirl: false), tables);
        Assert.Equal(UpgradeState.NoMugirl, noMugirl.Report.State);
        Assert.False(noMugirl.IsLegacyRun);

        XmlDocument current = SaveXml.Save(LegacyBody, stamp: SaveSchema.Current);
        string before = current.OuterXml;
        SaveUpgradeSession notLegacy = SaveXml.Upgrade(current, tables);
        Assert.Equal(UpgradeState.NotLegacy, notLegacy.Report.State);
        Assert.Equal(before, current.OuterXml);

        XmlDocument newer = SaveXml.Save(LegacyBody, stamp: SaveSchema.Current + 1);
        SaveUpgradeSession newerSession = SaveXml.Upgrade(newer, tables);
        Assert.Equal(UpgradeState.Newer, newerSession.Report.State);
        Assert.Equal(SaveSchema.Current + 1, newerSession.SourceSchema);

        // 版本戳按标签识别，与所在组件的 Class 无关。
        XmlDocument renamedHub = SaveXml.Save("<components><li Class=\"Some.Future.Hub\"><mugirlSaveSchema>" + SaveSchema.Current + "</mugirlSaveSchema></li></components>");
        Assert.Equal(UpgradeState.NotLegacy, SaveXml.Upgrade(renamedHub, tables).Report.State);

        XmlDocument broken = SaveXml.Save("<components><li Class=\"X\"><mugirlSaveSchema>one</mugirlSaveSchema></li></components>");
        SaveUpgradeSession brokenSession = SaveXml.Upgrade(broken, tables);
        Assert.Equal(UpgradeState.Failed, brokenSession.Report.State);
        Assert.Equal(SaveUpgradeSession.DetectStepId, brokenSession.Report.FailedStep);
    }

    [Fact]
    public void UpgradesALegacySaveAndRunningTwiceEqualsRunningOnce()
    {
        UpgradeTables tables = RenameTables();
        XmlDocument document = SaveXml.Save(LegacyBody);
        SaveUpgradeSession first = SaveXml.Upgrade(document, tables);

        Assert.Equal(UpgradeState.Upgraded, first.Report.State);
        string once = document.OuterXml;
        Assert.Equal(
            "<components><li Class=\"" + Fakes.Name<Fakes.NewComponent>() + "\"><newLabel>1</newLabel></li></components>"
            + "<maps><li><things><thing><def>Mugirl_OldGun</def><id>Mugirl_Gun_Rifle1</id></thing></things>"
            + "<lordManager><lords><li><loadID>4</loadID><lordJob Class=\"Mugirl.OldLordJob\" /><curLordToilIdx>0</curLordToilIdx></li></lords></lordManager>"
            + "<refs><li>Thing_Mugirl_OldGun1</li><li>Thing_Mugirl_OldArmor2</li></refs></li></maps>",
            SaveXml.Game(document));

        Assert.Equal("Thing_Mugirl_Gun_Rifle1", first.LoadIds!.Reference("Thing_Mugirl_OldGun1"));
        Assert.Equal("null", first.LoadIds.Reference("Thing_Mugirl_OldArmor2"));

        Assert.Equal(new[] { 4 }, first.Data.ResetLordIds);

        SaveUpgradeSession second = SaveXml.Upgrade(document, tables);
        Assert.Equal(UpgradeState.Upgraded, second.Report.State);
        Assert.Equal(once, document.OuterXml);
        Assert.Equal("Thing_Mugirl_Gun_Rifle1", second.LoadIds!.Reference("Thing_Mugirl_OldGun1"));
        Assert.All(second.Report.Steps, step => Assert.Equal(0, step.Actions));
    }

    [Fact]
    public void ModuleStepRemovalsAreResolvedByTheFinalLoadIdPass()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.Step(new RemoveCrateStep("Test.RemoveCrate")));
        string result = UpgradeWithAliases(
            "<maps><li><things><thing><def>Mugirl_Crate</def><id>Mugirl_Crate1</id></thing><thing><def>Steel</def><id>Steel2</id></thing></things></li></maps>"
            + "<refs><li>Thing_Mugirl_Crate1</li><li>Thing_Steel2</li></refs>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal("<maps><li><things><thing><def>Steel</def><id>Steel2</id></thing></things></li></maps><refs><li>Thing_Mugirl_Crate1</li><li>Thing_Steel2</li></refs>", result);
        Assert.Equal("null", aliases.Reference("Thing_Mugirl_Crate1"));
        Assert.Equal("Thing_Steel2", aliases.Reference("Thing_Steel2"));
    }

    [Fact]
    public void RowsAndStepsApplyOnlyToSavesOlderThanTheirSchema()
    {
        var schema1 = new CountingStep("Test.Schema1");
        var schema2 = new CountingStep("Test.Schema2", schema: 2);
        UpgradeTables tables = SaveXml.Tables(
            m =>
            {
                m.RenameLabel(OwnerSelector.Class("Mugirl.Owner"), "a", "c", "renamed in schema 1, composed with the schema 2 rename");
                m.ForSchema(2).RenameLabel(OwnerSelector.Class("Mugirl.Owner"), "b", "c", "renamed in schema 2");
                m.ResetLordGraph("Mugirl.OldLordJob");
                m.Step(schema1);
                m.Step(schema2);
            },
            currentSchema: 2);

        Assert.Same(tables, tables.ForSource(0));
        UpgradeTables fromSchema1 = tables.ForSource(1);
        Assert.Equal(1, fromSchema1.SourceSchema);
        Assert.Equal(new[] { "b" }, fromSchema1.LabelRows.Select(row => row.Old));
        Assert.Empty(fromSchema1.LordPolicies);
        Assert.Equal(new[] { "Test.Schema2" }, fromSchema1.DomSteps.Select(step => step.Id));
        Assert.Throws<InvalidOperationException>(() => fromSchema1.ForSource(0));

        // 来源版本 1 的存档：版本 1 的标签行与 Lord 重置都不再应用，版本 2 的行与步骤照常运行。
        XmlDocument document = SaveXml.Save(
            "<li Class=\"Mugirl.Owner\"><a>1</a><b>2</b></li>"
            + "<lordManager><lords><li><loadID>4</loadID><lordJob Class=\"Mugirl.OldLordJob\" /><curLordToilIdx>2</curLordToilIdx></li></lords></lordManager>");
        var context = new UpgradeContext(SaveDocument.TryCreate(document)!, fromSchema1, new TestCatalog(), new SessionData(), 1);
        var report = new UpgradeReport(1);
        UpgradePipeline.RunDom(context, report);

        Assert.False(report.Failed, report.Error);
        Assert.Equal(
            "<li Class=\"Mugirl.Owner\"><a>1</a><c>2</c></li>"
            + "<lordManager><lords><li><loadID>4</loadID><lordJob Class=\"Mugirl.OldLordJob\" /><curLordToilIdx>2</curLordToilIdx></li></lords></lordManager>",
            SaveXml.Game(document));
        Assert.Equal((0, 1), (schema1.Runs, schema2.Runs));
    }

    [Fact]
    public void InFlightJobsEndOnlyForExplicitPoliciesEvenOnUnstampedSaves()
    {
        UpgradeTables tables = SaveXml.Tables(
            m =>
            {
                m.ClaimJobDriver("Mugirl.JobDriver_Claimed", "Test.Claim");
                m.PreserveInFlightJobs("Mugirl.JobDriver_Unchanged");
                m.ForSchema(2).EndInFlightJobs("Mugirl.JobDriver_Changed");
                m.Step(new CountingStep("Test.Claim"));
            },
            currentSchema: 2);

        InFlightJobSelection legacy = InFlightJobSelection.For(tables.ForSource(0));
        Assert.Equal(new[] { "Mugirl.JobDriver_Changed" }, legacy.Drivers);
        Assert.Equal(new[] { "Mugirl.JobDriver_Claimed" }, legacy.Claimed);

        InFlightJobSelection fromSchema1 = InFlightJobSelection.For(tables.ForSource(1));
        Assert.Equal(new[] { "Mugirl.JobDriver_Changed" }, fromSchema1.Drivers);
        Assert.Empty(fromSchema1.Claimed);
        Assert.True(InFlightJobSelection.For(SaveXml.Empty().ForSource(0)).IsEmpty);
        Assert.True(InFlightJobSelection.For(SaveXml.Empty().ForSource(1)).IsEmpty);
    }

    [Fact]
    public void PreservingALordKeepsItsActiveToilTimersAndTriggerData()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.PreserveLordGraph("Mugirl.UnchangedLord"));
        const string Body = "<lordManager><lords><li><loadID>7</loadID><lordJob Class=\"Mugirl.UnchangedLord\" /><curLordToilIdx>2</curLordToilIdx><ticksInToil>132</ticksInToil><lordToilData><li><active>True</active></li></lordToilData><triggerData><li><ticksPassed>17</ticksPassed></li></triggerData></li></lords></lordManager>";

        Assert.Equal(Body, SaveXml.Upgraded(Body, tables));
    }

    [Fact]
    public void AFailingPlanAbortsThePipelineAndKeepsEarlierSteps()
    {
        var after = new CountingStep("Test.After");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.Step(new ThrowingStep("Test.Throws"));
            m.Step(after);
        });
        XmlDocument document = SaveXml.Save("<components><li Class=\"Mugirl.OldComponent\" /></components>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);

        Assert.Equal(UpgradeState.Failed, session.Report.State);
        Assert.Equal("Test.Throws", session.Report.FailedStep);
        Assert.Contains("planned failure", session.Report.Error, StringComparison.Ordinal);
        Assert.Contains(Fakes.Name<Fakes.NewComponent>(), SaveXml.Game(document), StringComparison.Ordinal);
        Assert.Equal(0, after.Runs);
        Assert.Equal(-1, SaveXml.Actions(session, "Test.After"));
    }

    [Fact]
    public void ModuleStepsRunInRegistrationOrderBetweenTheTableStepsAndTheLoadIdPass()
    {
        var first = new CountingStep("Test.First");
        var second = new CountingStep("Test.Second");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.Step(first);
            m.Step(second);
        });
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save("<x />"), tables);

        string[] ids = [.. session.Report.Steps.Select(step => step.Id)];
        Assert.Equal(
            new[] { UpgradePipeline.IndexStepId, MooGirlEraStep.StepId, TypeTableStep.StepId, DefDropStep.StepId, LordPolicyStep.StepId, LabelTableStep.StepId, "Test.First", "Test.Second", LoadIdStep.StepId },
            ids);
        Assert.True(first.Order < second.Order);
    }

    [Fact]
    public void PostLoadStepsRunPerPhaseAndCompletionRequiresEveryPhase()
    {
        var phaseA = new PostLoadStep("Test.A", UpgradePhase.A, fail: false);
        var phaseB = new PostLoadStep("Test.B", UpgradePhase.B, fail: true);
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.Step(phaseA);
            m.Step(phaseB);
        });
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save("<x />"), tables);
        session.RunPostLoad(UpgradePhase.A, tables.PostLoadSteps, () => new PostLoadContext(null!, tables, session.Data, session.SourceSchema));
        Assert.Equal(1, phaseA.Runs);
        Assert.Equal(0, phaseB.Runs);

        session.RunPostLoad(UpgradePhase.B, tables.PostLoadSteps, () => new PostLoadContext(null!, tables, session.Data, session.SourceSchema));
        session.Complete();
        Assert.Equal(UpgradeState.Failed, session.Report.State);
        Assert.Equal("Test.B", session.Report.FailedStep);
        Assert.False(session.Verified);

        SaveUpgradeSession skipped = SaveXml.Upgrade(SaveXml.Save("<x />"), SaveXml.Empty());
        skipped.Complete();
        Assert.Equal("Framework.PhaseA", skipped.Report.FailedStep);

        SaveUpgradeSession complete = SaveXml.Upgrade(SaveXml.Save("<x />"), SaveXml.Empty());
        complete.MarkPhase(UpgradePhase.A);
        complete.MarkPhase(UpgradePhase.B);
        complete.Complete();
        Assert.True(complete.Verified);
    }

    [Fact]
    public void ReportJsonHasTheDiagnosticsShape()
    {
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save(LegacyBody), RenameTables());
        session.Report.Backup = "C:\\saves\\A.rws.mugirl-schema0.bak";
        using JsonDocument json = JsonDocument.Parse(session.Report.ToJson());
        JsonElement root = json.RootElement;

        Assert.Equal(new[] { "session", "state", "failedStep", "error", "warnings", "steps", "totalMs", "backup" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
        Assert.Equal("Upgraded", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failedStep").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal("C:\\saves\\A.rws.mugirl-schema0.bak", root.GetProperty("backup").GetString());
        JsonElement step = root.GetProperty("steps")[1];
        Assert.Equal(new[] { "id", "phase", "actions", "ms" }, step.EnumerateObject().Select(p => p.Name));
        Assert.Equal("dom", step.GetProperty("phase").GetString());
    }

    [Fact]
    public void FailedBackupDoesNotLeaveAPartialBackupOrTemporaryFile()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mugirl-backup-failure-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string save = System.IO.Path.Combine(directory, "Colony.rws");
        string backup = SaveBackup.PathFor(save, 0);
        try
        {
            System.IO.File.WriteAllText(save, "original");
            System.IO.Directory.CreateDirectory(backup);
            Assert.ThrowsAny<System.IO.IOException>(() => SaveBackup.Ensure(save, 0));
            Assert.False(System.IO.File.Exists(backup));
            Assert.Empty(System.IO.Directory.GetFiles(directory, "*.tmp-*"));
            Assert.Equal("original", System.IO.File.ReadAllText(save));
        }
        finally
        {
            System.IO.Directory.Delete(backup);
            System.IO.File.Delete(save);
            System.IO.Directory.Delete(directory);
        }
    }

    [Fact]
    public void BackupIsMadeOnceAndNeverOverwritten()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mugirl-a1-backup-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            string save = System.IO.Path.Combine(directory, "Colony.rws");
            System.IO.File.WriteAllText(save, "original");
            string? backup = SaveBackup.Ensure(save, 0);
            Assert.Equal(save + ".mugirl-schema0.bak", backup);

            System.IO.File.WriteAllText(save, "upgraded and saved");
            Assert.Equal(backup, SaveBackup.Ensure(save, 0));
            Assert.Equal("original", System.IO.File.ReadAllText(backup!));
            Assert.Null(SaveBackup.Ensure(System.IO.Path.Combine(directory, "Missing.rws"), 0));
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ThrowingStep(string id) : DomUpgradeStep(id, "test")
    {
        internal override void Plan(UpgradeContext context, EditPlan plan) => throw new InvalidOperationException("planned failure");
    }

    private sealed class CountingStep(string id, int schema = 1) : DomUpgradeStep(id, "test")
    {
        private static int sequence;

        internal int Runs { get; private set; }

        internal int Order { get; private set; }

        internal override int Schema => schema;

        internal override void Plan(UpgradeContext context, EditPlan plan)
        {
            Runs++;
            Order = Interlocked.Increment(ref sequence);
        }
    }

    /// <summary>模块步骤删除一个物品；它的引用由最后的加载 ID 步骤置空。</summary>
    private sealed class RemoveCrateStep(string id) : DomUpgradeStep(id, "test")
    {
        internal override void Plan(UpgradeContext context, EditPlan plan)
        {
            foreach (IndexedThing thing in context.Index.ThingsWithDef("Mugirl_Crate"))
            {
                plan.RemoveItem(thing.Element, NullForm.Value);
            }
        }
    }

    private sealed class PostLoadStep(string id, UpgradePhase phase, bool fail) : PostLoadUpgradeStep(id, phase, "test")
    {
        internal int Runs { get; private set; }

        internal override int Run(PostLoadContext context)
        {
            Runs++;
            return fail ? throw new InvalidOperationException("post-load failure") : 1;
        }
    }

    private static string UpgradeWithAliases(string body, UpgradeTables tables, out LoadIdRemapper aliases)
    {
        var document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);
        Assert.False(session.Report.Failed, session.Report.Error);
        aliases = Assert.IsType<LoadIdRemapper>(session.LoadIds);
        return SaveXml.Game(document);
    }
}
