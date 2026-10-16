using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>
/// 步骤金样：<c>SaveUpgrade/Golden/&lt;StepId&gt;/before.xml</c> 经生产表格的完整 DOM 流水线升级后必须等于 <c>after.xml</c>，
/// 再升级一次保持不变。金样只含合成或匿名化的 Mugirl 节点（不放玩家存档），根元素是 <c>savegame</c>（含 <c>meta/modIds</c>）。
/// 验证器用同一目录核对步骤声明消耗的旧名称。
/// </summary>
public sealed class GoldenFixtureTests
{
    [Fact]
    public void EveryModuleDomStepHasAGoldenFixture()
    {
        string root = GoldenRoot();
        string[] missing =
        [
            .. UpgradeTables.Default.DomSteps
                .Select(step => step.Id)
                .Where(id => !File.Exists(Path.Combine(root, id, "before.xml")) || !File.Exists(Path.Combine(root, id, "after.xml"))),
        ];

        Assert.True(missing.Length == 0, "DOM steps without Golden/<StepId>/{before,after}.xml: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryGoldenFixtureMatchesAndIsIdempotent()
    {
        string root = GoldenRoot();
        string[] directories = Directory.Exists(root) ? [.. Directory.GetDirectories(root).Order(StringComparer.Ordinal)] : [];
        UpgradeTables tables = UpgradeTables.Default;
        string[] registered =
        [
            .. tables.DomSteps.Select(s => s.Id).Concat(tables.PostLoadSteps.Select(s => s.Id)).Concat(UpgradePipeline.AllFrameworkSteps.Select(s => s.Id)),
        ];
        foreach (string directory in directories)
        {
            string stepId = Path.GetFileName(directory);
            Assert.Contains(stepId, registered);
            string before = Path.Combine(directory, "before.xml");
            string after = Path.Combine(directory, "after.xml");
            Assert.True(File.Exists(before) && File.Exists(after), stepId + " needs before.xml and after.xml.");

            XmlDocument actual = Load(before);
            SaveUpgradeSession session = SaveXml.Upgrade(actual, tables);
            Assert.False(session.Report.Failed, stepId + ": " + session.Report.Error);
            Assert.Equal(SaveXml.Game(Load(after)), SaveXml.Game(actual));

            SaveXml.Upgrade(actual, tables);
            Assert.Equal(SaveXml.Game(Load(after)), SaveXml.Game(actual));
        }
    }

    private static XmlDocument Load(string path)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        return document;
    }

    // 金样随源码存放；测试从源码目录读取，不复制到输出目录。
    private static string GoldenRoot([CallerFilePath] string sourceFile = "") => Path.Combine(Path.GetDirectoryName(sourceFile)!, "Golden");
}
