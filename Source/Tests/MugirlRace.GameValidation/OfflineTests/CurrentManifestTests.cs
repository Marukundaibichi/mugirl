using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>当前构建的升级清单（K1：<c>Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson()</c>）能被游戏内验证的读取器完整解析。</summary>
public sealed class CurrentManifestTests
{
    [Fact]
    public void TheCurrentUpgradeManifestParses()
    {
        SaveNameMap map = SaveNameMap.Parse(Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson());

        Assert.True(map.Schema >= 1, "schema " + map.Schema);
        Assert.Equal("mugirlSaveSchema", map.StampLabel);
        Assert.All(map.LabelRows, row => Assert.True(row.Owner.IsValid, "owner selector the reader cannot parse: " + row.Owner.Text));
    }
}
