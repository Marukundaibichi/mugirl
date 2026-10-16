using System.Reflection;
using System.Xml.Linq;
using Mugirl.Features.Milk;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class WheelMissingDefaultsTests
{
    private static readonly Assembly Production = typeof(CompMilkProduction).Assembly;

    [Theory]
    [InlineData(6, 3, "", 6, 3)]
    [InlineData(4, 2, "99.99999", 4, 2)]
    [InlineData(4, 2, "100", 4, 4)]
    [InlineData(0, 9, "0", 1, 1)]
    [InlineData(4, -2, "0", 4, 0)]
    public void ActualConfigurationAndExactResearchBoundaryDetermineOnlyEmptySlots(int maximum, int initial, string progress, int count, int unlocked)
    {
        XDocument original = Input(progress);
        XDocument expected = new(original);
        var projection = Projection(maximum, initial);
        projection.Apply(expected, original);
        Assert.Equal(2, projection.Actions.Count);
        XElement records = expected.Descendants("mugirlWheelSlotRecords").Single();
        Assert.Equal(count, records.Elements("li").Count());
        Assert.Equal(unlocked, records.Descendants("unlocked").Count());
        Assert.All(records.Descendants("weapon"), weapon => Assert.Equal("null", weapon.Value));
        Assert.All(records.Descendants("unlocked"), flag => Assert.Equal("True", flag.Value));
        Assert.Empty(expected.Descendants("mugirlWheelReserveWeapons").Single().Element("innerList")!.Elements());
    }

    [Theory]
    [InlineData("weaponWheelReserveWeapons")]
    [InlineData("weaponWheelSlots")]
    [InlineData("weaponWheelUnlockedSlots")]
    [InlineData("weaponWheelSwordDanceFailureStreak")]
    [InlineData("mugirlWheelReserveWeapons")]
    [InlineData("mugirlWheelSlotRecords")]
    [InlineData("mugirlWheelSwordDanceFailureStreak")]
    public void AnyOriginalOrCurrentFieldPreventsWholeStateInitialization(string label)
    {
        XDocument original = Input("");
        XDocument expected = new(original);
        original.Descendants("thing").Single().Add(new XElement(label));
        var projection = Projection();
        projection.Apply(expected, original);
        Assert.Empty(projection.Actions);
        original = Input("");
        expected.Descendants("thing").Single().Add(new XElement(label));
        projection = Projection();
        projection.Apply(expected, original);
        Assert.Empty(projection.Actions);
    }

    [Fact]
    public void PrimaryWeaponIsNeverInventedInTheNewSlotList()
    {
        XDocument original = Input("");
        original.Descendants("thing").Single().Add(new XElement("equipment", new XElement("primary", "Thing_Gun1")));
        XDocument expected = new(original);
        Projection().Apply(expected, original);
        Assert.All(expected.Descendants("mugirlWheelSlotRecords").Descendants("weapon"), weapon => Assert.Equal("null", weapon.Value));
    }

    [Fact]
    public void SourceIdentityAndTypeMustBeUnambiguous()
    {
        XDocument original = Input("");
        XDocument expected = new(original);
        original.Descendants("thing").Single().SetAttributeValue("Class", "ThirdParty.Pawn");
        var projection = Projection();
        projection.Apply(expected, original);
        Assert.Empty(projection.Actions);
        original = Input("");
        original.Descendants("things").Single().Add(new XElement(original.Descendants("thing").Single()));
        Assert.Throws<InvalidOperationException>(() => Projection().Apply(expected, original));
    }

    [Theory]
    [InlineData("<progress><keys><li>Research</li><li>Research</li></keys><values><li>0</li><li>100</li></values></progress>")]
    [InlineData("<progress><keys><li>Research</li></keys><values/></progress>")]
    [InlineData("<progress><keys><li>Research</li></keys><values><li>NaN</li></values></progress>")]
    [InlineData("<progress><keys><li>Research</li></keys><values><li IsNull='True'/></values></progress>")]
    [InlineData("<progress><keys/><values/><custom/></progress>")]
    [InlineData("<progress IsNull='True'/>")]
    public void UnknownResearchShapeCannotGuessBaseUnlocks(string progress)
    {
        XDocument original = Input("");
        original.Descendants("researchManager").Single().ReplaceNodes(XElement.Parse(progress));
        XDocument expected = new(original);
        Assert.Throws<InvalidOperationException>(() => Projection().Apply(expected, original));
        Assert.Empty(expected.Descendants("mugirlWheelSlotRecords"));
    }

    [Fact]
    public void NoOriginalOrNoConfigurationNeverFallsBackToSixAndThree()
    {
        XDocument original = Input("");
        var projection = Projection();
        projection.Apply(new XDocument(original));
        Assert.Empty(projection.Actions);
        Assert.Throws<InvalidOperationException>(() => Projection(configured: false).Apply(new XDocument(original), original));
        Assert.Throws<InvalidOperationException>(() => new SerializationWheelDefaults("Props", 6, 3, "Research", 0f).MissingState(SaveXml.Game(original)!));
    }

    [Theory]
    [InlineData("progress", "text")]
    [InlineData("keys", "text")]
    [InlineData("values", "text")]
    [InlineData("progress", "cdata")]
    [InlineData("keys", "cdata")]
    [InlineData("values", "cdata")]
    [InlineData("keys", "comment")]
    [InlineData("values", "pi")]
    public void DictionaryChildNodesNeverSilentlyDiscardTextOrOtherNodes(string label, string kind)
    {
        XDocument original = Input("2000");
        XNode node = kind switch { "cdata" => new XCData(" "), "comment" => new XComment("note"), "pi" => new XProcessingInstruction("probe", "x"), _ => new XText("garbage") };
        original.Descendants(label).Single().Add(node);
        Assert.Throws<InvalidOperationException>(() => Projection().Apply(new XDocument(original), original));
    }

    private static CurrentSerializationDefaults Projection(int maximum = 6, int initial = 3, bool configured = true)
        => new(Production, new Dictionary<string, SerializationPawnDefaults>
        {
            ["Mugirl"] = new(["Pawn", "Verse.Pawn"], new Dictionary<string, int> { [CurrentSerializationDefaults.WheelType] = 1 }, null, null)
            {
                Wheel = configured ? new SerializationWheelDefaults("Mugirl.Features.WeaponWheel.CompProperties_WeaponWheel", maximum, initial, "Research", 100f) : null,
            },
        });

    private static XDocument Input(string progress)
        => Saves.Game("<researchManager><progress><keys>" + (progress.Length == 0 ? "" : "<li>Research</li>") + "</keys><values>"
            + (progress.Length == 0 ? "" : "<li>" + progress + "</li>") + "</values></progress></researchManager>"
            + "<things><thing Class='Pawn'><def>Mugirl</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef></thing></things>");
}
