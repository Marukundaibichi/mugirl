using System.Reflection;
using Mugirl.Features.Trade;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class FrozenXmlAliasEvidenceTests
{
    private static readonly Assembly Production = typeof(StockGenerator_MugirlSlaves).Assembly;

    [Fact]
    public void FrozenAliasUsesTheInheritedCanonicalField()
        => Assert.True(FrozenXmlAliasEvidence.SlaveRange(Production, [Alias(1, 2)], out _));

    [Fact]
    public void CanonicalImplementationIsNotTheLegacyXmlAlias()
        => Assert.False(FrozenXmlAliasEvidence.SlaveRange(Production, [new StockGenerator_MugirlSlaves { slaveCountRange = new IntRange(1, 2) }], out _));

    [Fact]
    public void FurtherDerivedAliasIsNotTheExactFrozenXmlEntry()
        => Assert.False(FrozenXmlAliasEvidence.SlaveRange(Production, [new DerivedAlias { slaveCountRange = new IntRange(1, 2) }], out _));

    [Fact]
    public void DuplicateAliasesDoNotBecomeAmbiguousSuccessfulCoverage()
        => Assert.False(FrozenXmlAliasEvidence.SlaveRange(Production, [Alias(1, 2), Alias(1, 2)], out _));

    [Theory]
    [InlineData(1, 8)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    public void IgnoredOrChangedLegacyXmlFieldFails(int minimum, int maximum)
        => Assert.False(FrozenXmlAliasEvidence.SlaveRange(Production, [Alias(minimum, maximum)], out _));

    [Fact]
    public void ATypeFromAnotherAssemblyCannotStandInForTheTarget()
        => Assert.False(FrozenXmlAliasEvidence.SlaveRange(typeof(FrozenXmlAliasEvidenceTests).Assembly, [Alias(1, 2)], out _));

    private static global::Mugirl.StockGenerator_Mugirl_Slaves Alias(int minimum, int maximum)
        => new() { slaveCountRange = new IntRange(minimum, maximum) };

    private sealed class DerivedAlias : global::Mugirl.StockGenerator_Mugirl_Slaves;
}
