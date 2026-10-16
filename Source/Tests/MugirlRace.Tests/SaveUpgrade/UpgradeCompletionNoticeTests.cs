using System.Xml;
using Mugirl.Core;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade;

[Collection("Reference aliases")]
public sealed class UpgradeCompletionNoticeTests
{
    [Fact]
    public void CompletedBackedUpUpgradeClaimsOneNoticeAndTheSavedStampSuppressesTheNextLoad()
    {
        SaveUpgradeSession session = Legacy();
        Assert.False(session.TryClaimCompletionNotice(stampVerified: true));
        session.MarkPhase(UpgradePhase.A);
        session.MarkPhase(UpgradePhase.B);
        session.Complete();
        Assert.False(session.TryClaimCompletionNotice(stampVerified: false));
        Assert.True(session.TryClaimCompletionNotice(stampVerified: true));
        Assert.False(session.TryClaimCompletionNotice(stampVerified: true));

        var hub = new GameComponent_Mugirl(null!);
        hub.AcceptLoad(0, verified: true, session.Report.ToJson(), record: null, problem: null);
        ScribeSaver previousSaver = Scribe.saver;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            var serialized = new XmlDocument();
            serialized.LoadXml(Scribe.saver.DebugOutputFor(hub));
            Assert.Equal("1", serialized.DocumentElement![SaveSchema.StampLabel]!.InnerText);
            XmlDocument saved = SaveXml.Save("<components><li Class=\"Mugirl.Core.GameComponent_Mugirl\">"
                + serialized.DocumentElement.InnerXml + "</li></components>");
            SaveUpgradeSession reloaded = SaveUpgradeSession.Start(2, saved, "updated.rws");
            reloaded.Complete();
            Assert.True(reloaded.Verified);
            Assert.Equal(UpgradeState.NotLegacy, reloaded.Report.State);
            Assert.False(reloaded.TryClaimCompletionNotice(stampVerified: true));
        }
        finally { Scribe.saver = previousSaver; Scribe.mode = previousMode; }
    }

    [Theory]
    [InlineData("missing-backup")]
    [InlineData("failed")]
    [InlineData("incomplete")]
    [InlineData("unsupported-era")]
    public void UnsuccessfulOrUnbackedUpgradeCannotClaimASuccessLetter(string fault)
    {
        SaveUpgradeSession session = Legacy();
        session.MarkPhase(UpgradePhase.A);
        if (fault != "incomplete") { session.MarkPhase(UpgradePhase.B); }
        if (fault == "missing-backup") { session.Report.Backup = null; }
        if (fault == "failed") { session.Report.Fail("Test.Step", "Expected failure"); }
        if (fault == "unsupported-era") { session.Report.State = UpgradeState.MooGirlEra; }
        session.Complete();
        Assert.False(session.TryClaimCompletionNotice(stampVerified: true));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void CurrentNewerAndNonMugirlSavesNeverClaimAnUpgradeLetter(int stamp, bool hasMugirl)
    {
        SaveUpgradeSession session = SaveUpgradeSession.Start(1, SaveXml.Save("", stamp, hasMugirl), "current.rws");
        session.Report.Backup = "unrelated.bak";
        session.Complete();
        Assert.False(session.TryClaimCompletionNotice(stampVerified: true));
    }

    private static SaveUpgradeSession Legacy()
    {
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save(""), SaveXml.Empty());
        session.Report.Backup = "original.rws.mugirl-schema0.bak";
        return session;
    }
}
