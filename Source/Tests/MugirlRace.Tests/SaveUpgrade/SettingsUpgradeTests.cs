using System.IO;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>Mod 设置文件升级：按 settingsSchema 版本、XmlDocument 改写、临时文件替换、只写一次。</summary>
public sealed class SettingsUpgradeTests
{
    private const string LegacyFile =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><SettingsBlock><ModSettings Class=\"Mugirl.MugirlSettings\">"
        + "<enableStructuralCrashEvent>False</enableStructuralCrashEvent><enableExtraOutline>True</enableExtraOutline><obsolete>1</obsolete>"
        + "</ModSettings></SettingsBlock>";

    private static UpgradeTables Tables() => SaveXml.Tables(m =>
    {
        m.RenameSettingsClass("Mugirl.MugirlSettings", "Mugirl.Core.MugirlSettings", 1);
        m.RenameSetting("enableStructuralCrashEvent", "enableOpeningStoryline", 1);
        m.DropSetting("obsolete", 1);
    });

    [Fact]
    public void UpgradesClassAndLabelsOnceAndKeepsUnknownLabels()
    {
        var document = new XmlDocument();
        document.LoadXml(LegacyFile);
        UpgradeTables tables = Tables();

        Assert.True(SettingsUpgrade.Upgrade(document, tables));
        Assert.Equal(
            "<ModSettings Class=\"Mugirl.Core.MugirlSettings\"><enableOpeningStoryline>False</enableOpeningStoryline><enableExtraOutline>True</enableExtraOutline><settingsSchema>1</settingsSchema></ModSettings>",
            document.DocumentElement!.InnerXml);
        Assert.False(SettingsUpgrade.Upgrade(document, tables));
    }

    [Fact]
    public void AFileWrittenByTheFrozenBuildIsUpgradedAgainWithoutLosingNewValues()
    {
        // 1.6 构建写回时丢掉 settingsSchema，并可能同时留下新旧两个标签：保留新值，删除旧标签。
        var document = new XmlDocument();
        document.LoadXml("<SettingsBlock><ModSettings Class=\"Mugirl.MugirlSettings\"><enableStructuralCrashEvent>True</enableStructuralCrashEvent><enableOpeningStoryline>False</enableOpeningStoryline></ModSettings></SettingsBlock>");

        Assert.True(SettingsUpgrade.Upgrade(document, Tables()));
        Assert.Equal(
            "<ModSettings Class=\"Mugirl.Core.MugirlSettings\"><enableOpeningStoryline>False</enableOpeningStoryline><settingsSchema>1</settingsSchema></ModSettings>",
            document.DocumentElement!.InnerXml);
    }

    [Fact]
    public void AFileThatNeedsNoChangeIsLeftAloneEvenWithoutAStamp()
    {
        // 设置类自己保存时写版本号；升级只在有行改动文件时才写，避免每次启动重写设置文件。
        var document = new XmlDocument();
        document.LoadXml("<SettingsBlock><ModSettings Class=\"Mugirl.Core.MugirlSettings\"><enableOpeningStoryline>True</enableOpeningStoryline></ModSettings></SettingsBlock>");
        string before = document.OuterXml;

        Assert.False(SettingsUpgrade.Upgrade(document, Tables()));
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void RunWritesTheFileOnceThroughATemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mugirl-a1-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Mod_mugirl_MugirlMod.xml");
            File.WriteAllText(path, LegacyFile);
            UpgradeTables tables = Tables();

            Assert.Equal(SettingsUpgradeResult.Upgraded, SettingsUpgrade.Run(path, tables));
            string written = File.ReadAllText(path);
            Assert.Contains("<enableOpeningStoryline>False</enableOpeningStoryline>", written, StringComparison.Ordinal);
            Assert.Contains("Class=\"Mugirl.Core.MugirlSettings\"", written, StringComparison.Ordinal);
            DateTime stamp = File.GetLastWriteTimeUtc(path);

            Assert.Equal(SettingsUpgradeResult.UpToDate, SettingsUpgrade.Run(path, tables));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            Assert.Single(Directory.GetFiles(directory));

            Assert.Equal(SettingsUpgradeResult.NoFile, SettingsUpgrade.Run(Path.Combine(directory, "Missing.xml"), tables));
            string broken = Path.Combine(directory, "Broken.xml");
            File.WriteAllText(broken, "<SettingsBlock><ModSettings>");
            Assert.Equal(SettingsUpgradeResult.Unreadable, SettingsUpgrade.Run(broken, tables));
            Assert.Equal(SettingsUpgradeResult.NoTarget, SettingsUpgrade.Run(path, SaveXml.Empty()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
