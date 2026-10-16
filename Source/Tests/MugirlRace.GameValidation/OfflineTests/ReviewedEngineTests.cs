using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class ReviewedEngineTests
{
    [Theory]
    [InlineData(0x06000595, "Verse.GenTypes", "AllSubclassesNonAbstract")]
    [InlineData(0x06001458, "Verse.Map", "FillComponents")]
    [InlineData(0x060026DD, "Verse.LoadIDsWantedBank", "RegisterLoadIDListReadFromXml")]
    [InlineData(0x06002705, "Verse.ScribeSaver", "WriteElement")]
    [InlineData(0x060068A7, "RimWorld.Quest", "ExposeData")]
    [InlineData(0x06008881, "RimWorld.Pawn_PsychicEntropyTracker", "SetInitialPsyfocusLevel")]
    [InlineData(0x06009D8E, "RimWorld.CompForbiddable", "PostExposeData")]
    [InlineData(0x0600E52D, "RimWorld.Planet.WorldPawns", "ExposeData")]
    public void KnownTokensStillIdentifyTheActualReader(int token, string type, string name)
    {
        var method = ReviewedEngine.ResolveMethod(token);
        Assert.Equal(type, method.DeclaringType!.FullName);
        Assert.Equal(name, method.Name);
        Assert.Equal(ReviewedEngine.Mvid, method.Module.ModuleVersionId);
    }

    [Fact]
    public void UnknownBuildOrMethodCannotBorrowAnObservationContract()
    {
        Guid unknown = new("00000000-0000-0000-0000-000000004980");
        Assert.False(ReviewedEngine.IsReviewed(unknown));
        Assert.Throws<InvalidOperationException>(() => ReviewedEngine.Token(unknown, 0x06001458));
        Assert.Throws<InvalidOperationException>(() => ReviewedEngine.Token(ReviewedEngine.Mvid, 0x06000001));
    }
}
