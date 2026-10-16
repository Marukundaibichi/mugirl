using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>
/// 性能预算：一次完整遍历加一次加载 ID 遍历，约 60 MB 的合成存档在 2 秒内完成（不含原版读入 XML 的时间）。
/// 生成存档本身要几秒，所以只在显式请求时运行：<c>MugirlRace.Tests.exe -explicit only -class Mugirl.Tests.SaveUpgrade.PerformanceBenchmarks</c>。
/// </summary>
public sealed class PerformanceBenchmarks(ITestOutputHelper output)
{
    private const int TargetBytes = 60 * 1024 * 1024;

    [Fact(Explicit = true)]
    public void SixtyMegabyteSaveUpgradesWithinTheBudget()
    {
        Stopwatch generate = Stopwatch.StartNew();
        string xml = SyntheticSave(TargetBytes, out int things);
        var document = new XmlDocument();
        document.LoadXml(xml);
        generate.Stop();

        UpgradeTables tables = BenchmarkTables();
        var catalog = new TestCatalog().With("ThingDef", "Mugirl", Fakes.Name<Fakes.CompMilk>());
        Stopwatch upgrade = Stopwatch.StartNew();
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables, catalog);
        upgrade.Stop();

        Assert.False(session.Report.Failed, session.Report.Error);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"document: {xml.Length / (1024.0 * 1024.0):F1} MB, {things} things; generate+load {generate.ElapsedMilliseconds} ms"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"upgrade: {upgrade.ElapsedMilliseconds} ms wall, {session.Report.TotalMilliseconds} ms in steps, {session.Report.TotalActions} actions"));
        output.WriteLine(session.Report.Details());
        Assert.True(upgrade.ElapsedMilliseconds < 2000, "The DOM pass took " + upgrade.ElapsedMilliseconds + " ms.");
        Assert.True(SaveXml.Actions(session, LoadIdStep.StepId) > 0);
    }

    private static UpgradeTables BenchmarkTables() => SaveXml.Tables(m =>
    {
        m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
        m.RenameType("Mugirl.CompOldMilk", typeof(Fakes.CompMilk), "renamed");
        for (int i = 0; i < 300; i++)
        {
            m.RenameDef("ThingDef", "Mugirl_Unused" + Letters(i), "Mugirl_Renamed" + Letters(i), "volume");
            m.RenameLabel(OwnerSelector.Class("Mugirl.Unused" + Letters(i)), "label" + Letters(i), "renamed" + Letters(i), "volume");
        }

        m.RenameDef("ThingDef", "Mugirl_OldGun", "Mugirl_Gun_Rifle", "renamed");
        m.DropDef("ThingDef", "Mugirl_OldArmor", DefDropPolicy.RemoveThings, "retired");
        m.DeclareLegacyComps("ThingDef", "Mugirl", "Mugirl.CompOldMilk");
        m.RenameLabel(OwnerSelector.ThingComp(typeof(Fakes.CompMilk)), "milkFullness", "mugirlMilkFullness", "prefix");
        m.RenameLabel(OwnerSelector.Class(typeof(Fakes.NewComponent)), "oldLabel", "newLabel", "renamed");
        m.ResetLordGraph("Mugirl.OldLordJob");
    });

    private static string Letters(int value)
    {
        var builder = new StringBuilder();
        do
        {
            builder.Append((char)('A' + (value % 26)));
            value /= 26;
        }
        while (value > 0);
        return builder.ToString();
    }

    private static string SyntheticSave(int targetBytes, out int things)
    {
        string[] defs = ["Steel", "Mugirl", "Human", "Mugirl_OldGun", "Mugirl_Milk", "Apparel_Parka", "Mugirl_OldArmor", "Plant_Grass"];
        var builder = new StringBuilder(targetBytes + 4096);
        builder.Append("<savegame><meta><gameVersion>1.6.4871 rev591</gameVersion><modIds><li>ludeon.rimworld</li><li>har.mugirlrace</li></modIds></meta><game>")
            .Append("<components><li Class=\"Mugirl.OldComponent\"><oldLabel>1</oldLabel></li><li Class=\"Vanilla.Component\" /></components><maps><li><things>");
        int n = 0;
        while (builder.Length < targetBytes)
        {
            string def = defs[n % defs.Length];
            string reference = defs[(n * 7 + 3) % defs.Length] + (n / 2).ToString(CultureInfo.InvariantCulture);
            string id = def + n.ToString(CultureInfo.InvariantCulture);
            if (def is "Mugirl" or "Human")
            {
                builder.Append("<thing Class=\"Pawn\"><def>").Append(def).Append("</def><id>").Append(id).Append("</id><map>0</map><pos>(12, 0, 34)</pos>")
                    .Append("<milkFullness>0.5</milkFullness><kindDef>Colonist</kindDef><faction>Faction_9</faction>")
                    .Append("<healthTracker><hediffSet><hediffs>")
                    .Append("<li><loadID>").Append(n).Append("</loadID><ageTicks>1000</ageTicks><severity>0.3</severity><def>Flu</def></li>")
                    .Append("<li Class=\"Hediff_Injury\"><loadID>").Append(n + 1).Append("</loadID><ageTicks>99</ageTicks><def>Cut</def><part><body>Human</body><index>3</index></part></li>")
                    .Append("</hediffs></hediffSet></healthTracker>")
                    .Append("<jobs><curJob><def>Wait</def><loadID>").Append(n).Append("</loadID><targetA>Thing_").Append(reference).Append("</targetA></curJob></jobs>")
                    .Append("<equipment><equipment><innerList><li><def>Mugirl_OldGun</def><id>Mugirl_OldGun").Append(n + 1_000_000).Append("</id>")
                    .Append("<verbTracker><verbs><li Class=\"Verb_Shoot\"><loadID>CompEquippable_Mugirl_OldGun").Append(n + 1_000_000).Append("_0</loadID></li></verbs></verbTracker></li></innerList></equipment></equipment>")
                    .Append("<relations><directRelations><li><def>Lover</def><otherPawn>Thing_").Append(reference).Append("</otherPawn><startTicks>12345</startTicks></li></directRelations></relations>")
                    .Append("</thing>");
            }
            else
            {
                builder.Append("<thing Class=\"ThingWithComps\"><def>").Append(def).Append("</def><id>").Append(id).Append("</id><map>0</map><pos>(")
                    .Append(n % 250).Append(", 0, ").Append(n % 211).Append(")</pos><health>100</health><stackCount>").Append(n % 75 + 1)
                    .Append("</stackCount><stuff>Steel</stuff><questTags IsNull=\"True\" /><forbidden>False</forbidden><quality>Normal</quality><lastHolder>Thing_")
                    .Append(reference).Append("</lastHolder></thing>");
            }

            n++;
        }

        things = n;
        builder.Append("</things><lordManager><lords><li><loadID>1</loadID><lordJob Class=\"Mugirl.OldLordJob\" /><curLordToilIdx>2</curLordToilIdx></li></lords></lordManager>")
            .Append("<zoneManager><allZones><li Class=\"Zone_Stockpile\"><settings><filter Class=\"ThingFilter\"><allowedDefs>")
            .Append(string.Concat(defs.Select(d => "<li>" + d + "</li>")))
            .Append("</allowedDefs></filter></settings></li></allZones></zoneManager></li></maps></game></savegame>");
        return builder.ToString();
    }
}
