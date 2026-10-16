using System.Reflection;
using System.Xml;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Milk;

/// <summary>升级失败后仍保存来源戳时，一次性状态的当前写法也必须可再次读取，不能被误判为混合结构。</summary>
[Collection("Reference aliases")]
public sealed class MilkXenotypeScribeTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 300)]
    [InlineData(false, 0)]
    [InlineData(false, 300)]
    [InlineData(true, -1)]
    [InlineData(true, 17)]
    public void CurrentWriterAlwaysCarriesBothScheduleFieldsWhenTheSourceStampIsRetained(bool pending, int ticks)
    {
        ScribeSaver oldSaver = Scribe.saver;
        LoadSaveMode oldMode = Scribe.mode;
        using var errors = new ScribeErrorCapture();
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            var component = new GameComponent_MilkUpgrade();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(GameComponent_MilkUpgrade).GetField("xenotypeRepairPending", fields)!.SetValue(component, pending);
            typeof(GameComponent_MilkUpgrade).GetField("xenotypeRepairTicks", fields)!.SetValue(component, ticks);
            var saved = new XmlDocument { XmlResolver = null };
            saved.LoadXml(Scribe.saver.DebugOutputFor(component));
            Assert.Equal(pending.ToString(), saved.DocumentElement!["xenotypeRepairPending"]?.InnerText);
            Assert.Equal(ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), saved.DocumentElement["xenotypeRepairTicks"]?.InnerText);
            XmlDocument retainedSource = SaveXml.Save("<components><li Class=\"Mugirl.SaveUpgrade.GameComponent_MilkUpgrade\">"
                + saved.DocumentElement.InnerXml + "</li></components>");
            string before = retainedSource.OuterXml;
            SaveUpgradeSession retry = SaveXml.Upgrade(retainedSource, UpgradeTables.Default);
            Assert.False(retry.Report.Failed, retry.Report.Error);
            Assert.Equal(0, retry.Report.TotalActions);
            Assert.Equal(before, retainedSource.OuterXml);
            Assert.Empty(errors.Messages);
        }
        finally
        {
            Scribe.saver = oldSaver;
            Scribe.mode = oldMode;
        }
    }
}
