using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class SaveVerbTargetTests
{
    [Theory]
    [InlineData("currentTarget", "null", "Verb_MeleeAttackDamage", true, true)]
    [InlineData("currentTarget", "null", "Verb_MeleeAttackDamage", false, false)]
    [InlineData("currentTarget", " null ", "Verb_MeleeAttackDamage", true, false)]
    [InlineData("currentTarget", "Thing_Human1", "Verb_MeleeAttackDamage", true, false)]
    [InlineData("playerLabel", "null", "Verb_MeleeAttackDamage", true, false)]
    [InlineData("currentTarget", "null", "ThirdParty.CustomVerb", true, false)]
    public void OnlyVerifiedVanillaTargetDefaultCanBeOmitted(string label, string value, string type, bool evidence, bool expected)
    {
        XDocument Before() => XDocument.Parse($"<savegame><game><things><thing><def>Mugirl_Test</def><id>Mugirl_Test1</id><verbTracker><verbs><li Class=\"{type}\"><loadID>CompEquippable_Mugirl_Test1_0_Smash</loadID><{label}>{value}</{label}></li></verbs></verbTracker></thing></things></game></savegame>");
        XDocument before = Before(), after = Before();
        after.Descendants(label).Single().Remove();
        var diff = new SaveDiff(new SaveDiffScope(new HashSet<string> { "Mugirl_Test" }, new HashSet<string> { "Mugirl_Test" }, (_, _) => false, new HashSet<string>(),
            DefaultTargetVerbClasses: evidence ? new HashSet<string> { "Verb_MeleeAttackDamage" } : null));
        SaveDifference change = Assert.Single(diff.Compare(before, after));
        Assert.Equal(expected, diff.ExplainVerbTargetDefault(change) is not null);
        Assert.Null(diff.ExplainVerbTargetDefault(Assert.Single(diff.Compare(after, before))));
    }
}
