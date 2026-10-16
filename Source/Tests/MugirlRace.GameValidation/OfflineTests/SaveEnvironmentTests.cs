using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class SaveEnvironmentTests
{
    [Fact]
    public void MatchingModSetInADifferentOrderIsNotTheSameEnvironment()
    {
        Assert.True(SaveEnvironment.ModListsMatch(["Core", "HAR.MugirlRace"], ["core", "har.mugirlrace"]));
        Assert.False(SaveEnvironment.ModListsMatch(["Core", "HAR.MugirlRace"], ["har.mugirlrace", "core"]));
        Assert.False(SaveEnvironment.ModListsMatch(["Core"], ["Core", "mugirl.test"]));
        Assert.False(SaveEnvironment.ModListsMatch(null, []));
    }
}
