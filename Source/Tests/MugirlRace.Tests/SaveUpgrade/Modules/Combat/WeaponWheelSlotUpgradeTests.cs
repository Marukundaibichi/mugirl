using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Features.WeaponWheel;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[Collection("Reference aliases")]
public sealed class WeaponWheelSlotUpgradeTests
{
    private static UpgradeTables Tables()
    {
        var builder = new UpgradeTableBuilder();
        builder.Module("Combat", 600).Step(new CombatWheelSlotRecordsStep());
        return builder.Build();
    }
    private static TestCatalog Catalog() => new TestCatalog().With("ThingDef", "Mugirl", SaveTypeNames.Of(typeof(CompWeaponWheel))).With("ThingDef", "Human");

    [Fact]
    public void PairsUnequalListsByOriginalIndexAndKeepsExactReferenceText()
    {
        XmlDocument document = SaveXml.Save("<thing><def>Mugirl</def><id>Mugirl1</id>"
            + "<weaponWheelSlots><li>Thing_Gun7</li><li>null</li><li>Thing_Gun8</li></weaponWheelSlots>"
            + "<weaponWheelUnlockedSlots><li>True</li><li>False</li></weaponWheelUnlockedSlots></thing>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables(), Catalog());

        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal("<thing><def>Mugirl</def><id>Mugirl1</id><mugirlWheelSlotRecords>"
            + "<li><weapon>Thing_Gun7</weapon><unlocked>True</unlocked></li>"
            + "<li><weapon>null</weapon><unlocked>False</unlocked></li><li><weapon>Thing_Gun8</weapon></li>"
            + "</mugirlWheelSlotRecords></thing>", SaveXml.Game(document));
        string once = document.OuterXml;
        SaveUpgradeSession second = SaveXml.Upgrade(document, Tables(), Catalog());
        Assert.Equal(once, document.OuterXml);
        Assert.Equal(0, SaveXml.Actions(second, CombatWheelSlotRecordsStep.StepId));
    }

    [Fact]
    public void HandlesMissingNullAndEmptyListsWithoutInventingUnlockValues()
    {
        const string Body = "<thing><def>Mugirl</def><id>Mugirl1</id><weaponWheelSlots IsNull=\"True\" /><weaponWheelUnlockedSlots><li>True</li><li>False</li></weaponWheelUnlockedSlots></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl2</id><weaponWheelSlots /></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl3</id></thing>";
        string result = SaveXml.Upgraded(Body, Tables(), Catalog());
        Assert.Equal("<thing><def>Mugirl</def><id>Mugirl1</id><mugirlWheelSlotRecords><li><unlocked>True</unlocked></li><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl2</id><mugirlWheelSlotRecords /></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl3</id></thing>", result);
    }

    [Fact]
    public void FindsNestedOwnersAndLeavesOtherRacesAndPlayerTextUntouched()
    {
        const string Body = "<vault><innerList><li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><weaponWheelSlots><li>null</li></weaponWheelSlots>"
            + "<weaponWheelUnlockedSlots><li>True</li></weaponWheelUnlockedSlots><nick>weaponWheelSlots</nick></li></innerList></vault>"
            + "<thing><def>Human</def><id>Human2</id><weaponWheelSlots><li>Player text</li></weaponWheelSlots></thing>";
        string result = SaveXml.Upgraded(Body, Tables(), Catalog());
        Assert.Contains("<mugirlWheelSlotRecords><li><weapon>null</weapon><unlocked>True</unlocked></li></mugirlWheelSlotRecords>", result, StringComparison.Ordinal);
        Assert.Contains("<nick>weaponWheelSlots</nick>", result, StringComparison.Ordinal);
        Assert.Contains("<thing><def>Human</def><id>Human2</id><weaponWheelSlots><li>Player text</li></weaponWheelSlots></thing>", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<weaponWheelSlots><li><weapon>Thing_Gun7</weapon></li></weaponWheelSlots>")]
    [InlineData("<weaponWheelSlots><li>Thing_Gun7</li></weaponWheelSlots><mugirlWheelSlotRecords />")]
    public void RejectsAmbiguousShapesBeforeEditingAnyOwner(string invalid)
    {
        XmlDocument document = SaveXml.Save("<thing><def>Mugirl</def><id>Mugirl1</id><weaponWheelSlots><li>null</li></weaponWheelSlots></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl2</id>" + invalid + "</thing>");
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables(), Catalog());
        Assert.True(session.Report.Failed);
        Assert.Equal(CombatWheelSlotRecordsStep.StepId, session.Report.FailedStep);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void RecordViewMatchesTheOriginalPureAlgorithmsAndWeaponIdentities()
    {
        ThingWithComps[] pool = [new(), new(), new(), new()];
        for (int occupied = 0; occupied < 16; occupied++)
        {
            for (int unlocked = 0; unlocked < 16; unlocked++)
            {
                List<ThingWithComps?> weapons = [];
                List<bool> flags = [];
                List<WeaponWheelSlot> records = [];
                for (int i = 0; i < 4; i++)
                {
                    ThingWithComps? weapon = (occupied & (1 << i)) == 0 ? null : pool[i];
                    bool flag = (unlocked & (1 << i)) != 0;
                    weapons.Add(weapon);
                    flags.Add(flag);
                    records.Add(new WeaponWheelSlot { Weapon = weapon, Unlocked = flag });
                }
                Assert.Equal(WeaponWheelSlotMath.CountOccupiedUnlocked(weapons, flags), WeaponWheelSlotMath.CountOccupiedUnlocked(records));
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(WeaponWheelSlotMath.FirstUnlockedEmpty(weapons, flags, i), WeaponWheelSlotMath.FirstUnlockedEmpty(records, i));
                    Assert.Equal(WeaponWheelSlotMath.FindNextCyclic(weapons, flags, i, _ => true), WeaponWheelSlotMath.FindNextCyclic(records, i, _ => true));
                    List<int> oldIndices = [], newIndices = [];
                    List<ThingWithComps?> oldWeapons = [], newWeapons = [];
                    Assert.Equal(WeaponWheelSlotMath.TryRotateToFront(weapons, flags, i, oldIndices, oldWeapons), WeaponWheelSlotMath.TryRotateToFront(records, i, newIndices, newWeapons));
                    Assert.Equal(oldIndices, newIndices);
                    Assert.Equal(oldWeapons, newWeapons);
                    Assert.True(oldWeapons.Zip(newWeapons).All(pair => ReferenceEquals(pair.First, pair.Second)));
                }
            }
        }
    }

    [Fact]
    public void ReserveOwnerAndParentChainRemainOnTheThingCompFacade()
    {
        var pawn = new Pawn();
        var wheel = new CompWeaponWheel { parent = pawn };
        Assert.Same(wheel, wheel.ReserveWeapons.Owner);
        Assert.Same(pawn, ((IThingHolder)wheel).ParentHolder);
    }

    [Fact]
    public void ActualDeepSlotRecordsResolveTheOriginalWeaponInstance()
    {
        using var errors = new ScribeErrorCapture();
        using var assemblies = new WeaponWheelScribeTests.ModAssemblyScope();
        ScribeLoader oldLoader = Scribe.loader;
        LoadSaveMode oldMode = Scribe.mode;
        try
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml("<root><slots><li><weapon>Thing_TestGun42</weapon><unlocked>True</unlocked></li><li><weapon>null</weapon></li></slots></root>");
            var holder = new SlotHolder();
            Scribe.loader = new ScribeLoader { curXmlParent = xml.DocumentElement, curParent = holder };
            Scribe.mode = LoadSaveMode.LoadingVars;
            holder.ExposeData();
            var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            definition.defName = "TestGun";
            var weapon = new ThingWithComps { def = definition, thingIDNumber = 42 };
            FieldInfo directoryField = typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var directory = (LoadedObjectDirectory)directoryField.GetValue(Scribe.loader.crossRefs)!;
            directory.RegisterLoaded(weapon);
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(holder);
            Scribe.loader.crossRefs.ResolveAllCrossReferences();

            Assert.Equal(2, holder.Slots!.Count);
            Assert.Same(weapon, holder.Slots[0].Weapon);
            Assert.True(holder.Slots[0].Unlocked);
            Assert.Null(holder.Slots[1].Weapon);
            Assert.False(holder.Slots[1].Unlocked);
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
        }
        finally
        {
            Scribe.loader = oldLoader;
            Scribe.mode = oldMode;
        }
    }

    private sealed class SlotHolder : IExposable
    {
        internal List<WeaponWheelSlot>? Slots;
        public void ExposeData() => Scribe_Collections.Look(ref Slots, "slots", LookMode.Deep);
    }
}
