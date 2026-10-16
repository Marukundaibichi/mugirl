using System.IO;
using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class ReferenceResaveTests
{
    [Theory]
    [InlineData("<game><tickManager><ticksGame>15</ticksGame></tickManager></game><game />")]
    [InlineData("<game><tickManager><ticksGame>15</ticksGame><ticksGame>16</ticksGame></tickManager></game>")]
    [InlineData("<game><tickManager><ticksGame IsNull='True' /></tickManager></game>")]
    [InlineData("<game><tickManager><ticksGame><number>15</number></ticksGame></tickManager></game>")]
    [InlineData("<game><tickManager /><tickManager><ticksGame>15</ticksGame></tickManager></game>")]
    [InlineData("<game><tickManager><ticksGame>2147483648</ticksGame></tickManager></game>")]
    public void AmbiguousOrMalformedTimeCannotCertifyZeroTicks(string game)
        => Assert.Throws<InvalidOperationException>(() => ReferenceResaveEvidence.Tick(XDocument.Parse("<savegame>" + game + "</savegame>")));

    [Fact]
    public void PlayerTextWithTheSameLabelDoesNotAlterTheSourceTick()
        => Assert.Equal(1500, ReferenceResaveEvidence.Tick(XDocument.Parse("<savegame><game><tickManager><ticksGame>1500</ticksGame></tickManager><text><ticksGame>999</ticksGame></text></game></savegame>")));

    [Fact]
    public void ResaveRejectsSourcePathAliasesBeforeSaveWaitCouldDeleteTheInput()
    {
        string path = Path.Combine(Path.GetTempPath(), "ReferenceInput.rws");
        Assert.Throws<InvalidOperationException>(() => ReferenceResaveEvidence.RequireDifferentFiles(path, path.ToUpperInvariant()));
        Assert.Throws<InvalidOperationException>(() => ReferenceResaveEvidence.RequireDifferentFiles(path, Path.Combine(Path.GetTempPath(), "subfolder", "..", "ReferenceInput.rws")));
        ReferenceResaveEvidence.RequireDifferentFiles(path, Path.Combine(Path.GetTempPath(), "ReferenceOutput.rws"));
    }
}
