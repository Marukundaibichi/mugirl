using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>只读迁移指定存档的完整 DOM，再把其中两份奶量放进隔离测试 Pawn，执行真实游戏存读档。</summary>
internal static class MilkMigrationSuite
{
    public const string Name = "milk-migration";

    public static ValidationSuite Create() => new(Name,
        "read-only supplied-save migration and independent milk component values through a real game save/load",
        SuiteNeeds.MugirlMod, [new ValidationStep("roundtrip", Roundtrip, TimeoutSeconds: 600)], inDefaultRun: false)
    {
        InAllRun = false,
        LogPolicy = SuiteLogPolicy.AnyError,
    };

    private static IEnumerable<Wait> Roundtrip(ValidationContext ctx)
    {
        string input = ctx.Options.SuiteOption(Name + ".source", "");
        if (!File.Exists(input))
        {
            ctx.Report.Fail("source save copy exists", "provide milk-migration.source=<path to a test copy>");
            yield break;
        }

        byte[] hash = SHA256.HashData(File.ReadAllBytes(input));
        var document = new XmlDocument { XmlResolver = null };
        document.Load(input);
        var pairs = document.SelectNodes("//*[def='Mugirl' and milkFullness]")!.Cast<XmlElement>().Select(pawn =>
        {
            XmlElement[] milk = pawn.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "milkFullness").ToArray();
            if (milk.Length != 2 || milk[0].NextSibling?.Name.StartsWith("milkFullness_", StringComparison.Ordinal) != true)
            {
                throw new InvalidOperationException("The supplied fixture must have two milk values and an independently identifiable first production block.");
            }
            return (Source: pawn, Id: pawn["id"]!.InnerText, Own: milk[0].InnerText, Foreign: milk[1].InnerText, ForeignNode: milk[1]);
        }).ToArray();
        if (!ctx.Report.Check(pairs.Length > 0, "source contains paired milk fields", () => "no paired Mugirl found"))
        {
            yield break;
        }

        Assembly assembly = ctx.RequireTarget().Assembly ?? throw new InvalidOperationException("MugirlRace is not loaded.");
        Type ownType = assembly.GetType("Mugirl.Features.Milk.CompMilkProduction", throwOnError: true)!;
        PropertyInfo ownFullness = ownType.GetProperty("Fullness")!;
        Type catalogType = assembly.GetType("Mugirl.SaveUpgrade.DefDatabaseCatalog", throwOnError: true)!;
        object catalog = catalogType.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        MethodInfo upgrade = assembly.GetType("Mugirl.SaveUpgrade.SaveUpgradeOffline", throwOnError: true)!.GetMethod("Run")!;
        string first = (string)upgrade.Invoke(null, [document, catalog])!;
        if (ctx.ArtifactPath("milk-migration-first.json") is { } firstPath)
        {
            File.WriteAllText(firstPath, first);
        }
        using (JsonDocument report = JsonDocument.Parse(first))
        {
            if (!ctx.Report.Check(report.RootElement.GetProperty("state").GetString() == "Upgraded", "complete supplied DOM upgrades", () => first))
            {
                yield break;
            }
        }
        string upgraded = document.OuterXml;
        string repeated = (string)upgrade.Invoke(null, [document, catalog])!;
        using (JsonDocument report = JsonDocument.Parse(repeated))
        {
            ctx.Report.Check(report.RootElement.GetProperty("failedStep").ValueKind == JsonValueKind.Null
                && report.RootElement.GetProperty("steps").EnumerateArray().All(step => step.GetProperty("actions").GetInt32() == 0)
                && upgraded == document.OuterXml, "repeated migration changes nothing", () => repeated);
        }

        // 使用原版同名字段的真实组件作为第二个读写者；不声称加载了玩家的 Equal Milking DLL。
        ThingDef race = GameFixtures.MugirlRace ?? throw new InvalidOperationException("Mugirl race is missing.");
        var foreignProps = new CompProperties_Milkable { milkDef = DefDatabase<ThingDef>.GetNamed("Milk"), milkAmount = 1, milkIntervalDays = 1 };
        race.comps.Add(foreignProps);
        try
        {
            List<Pawn> pawns = GameFixtures.EnsureMugirlColonists(ctx, pairs.Length);
            int[] identities = pawns.Select(pawn => pawn.thingIDNumber).ToArray();
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                ctx.Report.Check(pair.Source["mugirlMilkFullness"]?.InnerText == pair.Own
                    && ReferenceEquals(pair.Source["milkFullness"], pair.ForeignNode) && pair.ForeignNode.InnerText == pair.Foreign,
                    pair.Id + " keeps both XML values", () => "own or foreign milk changed");
                Pawn pawn = pawns[i];
                ThingComp own = pawn.AllComps.Single(comp => comp.GetType() == ownType);
                CompMilkable foreign = pawn.GetComp<CompMilkable>() ?? throw new InvalidOperationException("Second component is missing.");
                ReadComponents(pair.Source, own, foreign);
                CheckValues(ctx, pair.Id + " initial read", own, foreign, ownFullness, pair.Own, pair.Foreign);
            }

            var save = new SaveWait(ValidationContext.SaveName(Name));
            yield return save;
            if (!ctx.Report.Check(File.Exists(save.FilePath), "isolated paired-component game saves", () => save.FilePath))
            {
                yield break;
            }
            yield return new LoadWait(save.SaveName);
            for (int i = 0; i < identities.Length; i++)
            {
                Pawn pawn = ValidationContext.FindPawn(identities[i]) ?? throw new InvalidOperationException("Reloaded pawn is missing.");
                ThingComp own = pawn.AllComps.Single(comp => comp.GetType() == ownType);
                CompMilkable foreign = pawn.GetComp<CompMilkable>() ?? throw new InvalidOperationException("Reloaded second component is missing.");
                CheckValues(ctx, pairs[i].Id + " game reload", own, foreign, ownFullness, pairs[i].Own, pairs[i].Foreign);
            }
            ctx.Report.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "supplied file remains byte-identical", () => input);
            if (ctx.ArtifactPath("milk-migration-values.json") is { } valuesPath)
            {
                File.WriteAllText(valuesPath, JsonSerializer.Serialize(new
                {
                    sourceSha256 = Convert.ToHexString(hash),
                    modSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
                    pairs = pairs.Select(pair => new { pair.Id, pair.Own, pair.Foreign }),
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally
        {
            race.comps.Remove(foreignProps);
        }
    }

    private static void ReadComponents(XmlElement source, ThingComp own, ThingComp foreign)
    {
        ScribeLoader previousLoader = Scribe.loader;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            Scribe.loader = new ScribeLoader { curXmlParent = source, curParent = own.parent };
            Scribe.mode = LoadSaveMode.LoadingVars;
            own.PostExposeData();
            foreign.PostExposeData();
        }
        finally
        {
            Scribe.loader = previousLoader;
            Scribe.mode = previousMode;
        }
    }

    private static void CheckValues(ValidationContext ctx, string label, ThingComp own, CompMilkable foreign,
        PropertyInfo ownFullness, string expectedOwn, string expectedForeign)
    {
        float ownValue = (float)ownFullness.GetValue(own)!;
        float foreignValue = foreign.Fullness;
        ctx.Report.Check(BitConverter.SingleToInt32Bits(ownValue) == BitConverter.SingleToInt32Bits(float.Parse(expectedOwn, CultureInfo.InvariantCulture))
            && BitConverter.SingleToInt32Bits(foreignValue) == BitConverter.SingleToInt32Bits(float.Parse(expectedForeign, CultureInfo.InvariantCulture)),
            label + " preserves both float values", () => $"expected {expectedOwn}/{expectedForeign}, actual {ownValue:R}/{foreignValue:R}");
    }
}
