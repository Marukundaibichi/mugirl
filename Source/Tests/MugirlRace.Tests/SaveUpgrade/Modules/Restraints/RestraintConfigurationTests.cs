using System.Collections.Generic;
using Mugirl.Features.Restraints;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Restraints;

public sealed class RestraintConfigurationTests
{
    [Fact]
    public void UninitializedApparelKeepsTheOriginalNullConfigurationBoundary()
    {
        var apparel = new RestraintApparel();
        Assert.Null(apparel.Restraint);
        Assert.True(apparel.IsLocked);
        Assert.Equal(0, apparel.LockCount);
    }

    [Fact]
    public void PublicXmlAliasKeepsAllScalarValuesAndListInstances()
    {
        var hediff = new HediffDef();
        var key = new ThingDef();
        var next = new List<ThingDef> { new() };
        var parts = new List<BodyPartDef> { new() };
        var groups = new List<BodyPartGroupDef> { new() };
        var definition = new SlaveApparelDef
        {
            defName = "Mugirl_TestConfiguration",
            equipped_hediff = hediff,
            blocks_hands = true,
            unlockTick = -17,
            needkeynumber = 7,
            keytype = key,
            NextSlaveApparelDefs = next,
            HediffTargetBodyPartDefs = parts,
            BoundBodyPartGroupDefs = groups,
        };
        definition.ResolveReferences();
        RestraintExtension extension = definition.GetModExtension<RestraintExtension>();
        Assert.NotNull(extension);
        Assert.Same(hediff, extension.equippedHediff);
        Assert.True(extension.blocksHands);
        Assert.Equal(-17, extension.unlockTicks);
        Assert.Equal(7, extension.requiredKeys);
        Assert.Same(key, extension.key);
        Assert.Same(next, extension.nextApparelDefs);
        Assert.Same(parts, extension.targetBodyParts);
        Assert.Same(groups, extension.boundBodyPartGroups);
        definition.needkeynumber = 11;
        definition.unlockTick = 7;
        definition.ResolveReferences();
        Assert.Same(extension, definition.GetModExtension<RestraintExtension>());
        Assert.Equal(11, extension.requiredKeys);
        Assert.Equal(7, extension.unlockTicks);
        Assert.Single(definition.modExtensions);
    }

    [Fact]
    public void ExplicitExtensionKeepsPriorityOverTheAliasGeneratedBridge()
    {
        var extension = new RestraintExtension { requiredKeys = 19, unlockTicks = 77 };
        var definition = new SlaveApparelDef { defName = "Mugirl_TestExplicitConfiguration", needkeynumber = 2, modExtensions = [extension] };
        definition.ResolveReferences();
        definition.needkeynumber = 11;
        definition.ResolveReferences();
        Assert.Same(extension, definition.GetModExtension<RestraintExtension>());
        Assert.Equal(19, extension.requiredKeys);
        Assert.Equal(77, extension.unlockTicks);
    }
}
