using System.IO;
using System.Reflection;
using System.Text.Json;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>用实际加载的建筑 Def 检验连接限制，保留旧构建对照模式。</summary>
internal static class ThrowConnectionsSuite
{
    public const string Name = "throw-connections";
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static ValidationSuite Create() => new(Name,
        "building pickup: furniture, structural links, conduits, diagonal and multi-cell boundaries",
        SuiteNeeds.MugirlMod, [new ValidationStep("connections", Run)], inDefaultRun: false)
    {
        LogPolicy = SuiteLogPolicy.AnyError,
    };

    private static IEnumerable<Wait> Run(ValidationContext ctx)
    {
        Assembly assembly = ctx.RequireTarget().Assembly!;
        Type utility = assembly.GetType("Mugirl.Features.Abilities.ThrowUtility", throwOnError: true)!;
        Type? connections = assembly.GetType("Mugirl.Features.Abilities.ThrowBuildingConnections");
        MethodInfo connected = connections?.GetMethod("IsConnected", StaticMembers)
            ?? utility.GetMethod("IsConnectedBuilding", StaticMembers)!;
        MethodInfo canPickUp = utility.GetMethod("CanPickUp", StaticMembers)!;
        bool baseline = ctx.Options.SuiteOption(Name + ".mode", "fixed") == "baseline";
        Pawn caster = GameFixtures.EnsureMugirlColonists(ctx, 3).First(p => !p.WorkTagIsDisabled(WorkTags.Violent));
        Map map = ctx.Map;
        IntVec3 origin = map.Center + new IntVec3(12, 0, 0);
        GameFixtures.ClearArea(map, origin, 8);
        var observations = new List<object>();
        var usedDefs = new HashSet<ThingDef>();

        ThingDef Def(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(name);
            usedDefs.Add(def);
            return def;
        }

        Thing Spawn(ThingDef def, IntVec3 cell, Rot4 rotation)
        {
            Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            if (def.CanHaveFaction) { thing.SetFaction(Faction.OfPlayer); }
            return GenSpawn.Spawn(thing, cell, map, rotation);
        }

        // 两种构建都调用自身的 CanPickUp；连接之外的重量等条件保持原样。
        void Measure(string scenario, string targetName, string? otherName, string placement,
            bool expected, bool oldExpected, Rot4 rotation, bool shouldLift = false, bool powerConsumer = false)
        {
            Thing target = Spawn(Def(targetName), origin, rotation);
            Thing? other = null;
            try
            {
                CellRect rect = target.OccupiedRect();
                IntVec3 otherCell = placement switch
                {
                    "same" => origin,
                    "diagonal" => new IntVec3(rect.maxX + 1, 0, rect.maxZ + 1),
                    "gap" => new IntVec3(rect.maxX + 2, 0, rect.maxZ),
                    _ => new IntVec3(rect.maxX + 1, 0, rect.maxZ),
                };
                if (otherName is not null) { other = Spawn(Def(otherName), otherCell, Rot4.North); }
                if (powerConsumer)
                {
                    CompPowerTrader consumer = target.TryGetComp<CompPowerTrader>();
                    CompPower transmitter = other!.TryGetComp<CompPower>();
                    consumer.ConnectToTransmitter(transmitter, reconnectingAfterLoading: false);
                    consumer.PowerOn = true;
                    ctx.Report.Check(consumer.connectParent == transmitter, scenario + " has an actual power connection", () => "not connected");
                }

                bool actual = (bool)connected.Invoke(null, [target])!;
                object?[] args = [caster, target, null];
                bool lift = (bool)canPickUp.Invoke(null, args)!;
                string? reason = args[2]?.ToString();
                bool wantConnected = baseline ? oldExpected : expected;
                ctx.Report.Check(target.Spawned && (other is null || other.Spawned), scenario + " fixtures coexist", () => "spawn wiped a fixture");
                ctx.Report.Check(actual == wantConnected, scenario + " connection", () => $"expected {wantConnected}, got {actual}");
                if (wantConnected)
                {
                    string wantedReason = "Mugirl.Throw.ConnectedTarget".Translate(target.LabelCap).ToString();
                    ctx.Report.Check(!lift && reason == wantedReason, scenario + " rejected by pickup for connection", () => $"lift={lift}, reason={reason}");
                }
                else if (shouldLift)
                {
                    ctx.Report.Check(lift, scenario + " passes actual pickup", () => reason ?? "unknown rejection");
                }
                else
                {
                    ctx.Report.Check(reason != "Mugirl.Throw.ConnectedTarget".Translate(target.LabelCap).ToString(),
                        scenario + " does not report a connection", () => reason ?? "no reason");
                }
                observations.Add(new { scenario, target = targetName, other = otherName, placement, actual, lift, reason });
            }
            finally
            {
                if (target.Spawned) { target.Destroy(DestroyMode.Vanish); }
                if (other?.Spawned == true) { other.Destroy(DestroyMode.Vanish); }
            }
        }

        Measure("chair beside chair", "DiningChair", "DiningChair", "edge", false, true, Rot4.North, shouldLift: true);
        Measure("chair beside wall", "DiningChair", "Wall", "edge", false, true, Rot4.North, shouldLift: true);
        Measure("wall beside chair", "Wall", "DiningChair", "edge", false, true, Rot4.North);
        Measure("table far edge beside wall", "Table2x2c", "Wall", "edge", false, true, Rot4.East, shouldLift: true);
        Measure("isolated wall", "Wall", null, "edge", false, false, Rot4.North);
        Measure("wall beside wall", "Wall", "Wall", "edge", true, true, Rot4.North);
        Measure("wall diagonally beside wall", "Wall", "Wall", "diagonal", false, false, Rot4.North);
        Measure("wall one empty cell from wall", "Wall", "Wall", "gap", false, false, Rot4.North);
        Measure("wall beside rock", "Wall", "Granite", "edge", true, true, Rot4.North);
        Measure("rock beside rock", "Granite", "Granite", "edge", true, true, Rot4.North);
        Measure("fence beside fence", "Fence", "Fence", "edge", true, true, Rot4.North);
        Measure("fence beside gate", "Fence", "FenceGate", "edge", true, true, Rot4.North);
        Measure("gate beside fence", "FenceGate", "Fence", "edge", true, true, Rot4.North);
        Measure("sandbag beside sandbag", "Sandbags", "Sandbags", "edge", true, true, Rot4.North);
        // 电缆没有显式 Mass，仍受最大耐久 × 10 的重量下限约束，不能把连接放行误当成忽略重量。
        Measure("isolated conduit", "PowerConduit", null, "edge", false, false, Rot4.North);
        Measure("conduit beside conduit", "PowerConduit", "PowerConduit", "edge", true, true, Rot4.North);
        Measure("hidden conduit beside hidden conduit", "HiddenConduit", "HiddenConduit", "edge", true, true, Rot4.North);
        Measure("conduit beside hidden conduit", "PowerConduit", "HiddenConduit", "edge", true, true, Rot4.North);
        Measure("conduit beside wall", "PowerConduit", "Wall", "edge", false, true, Rot4.North);
        Measure("conduit beside battery", "PowerConduit", "Battery", "edge", true, true, Rot4.North);
        Measure("battery beside conduit", "Battery", "PowerConduit", "edge", false, true, Rot4.North, shouldLift: true);
        Measure("wall over hidden conduit", "Wall", "HiddenConduit", "same", false, true, Rot4.North);
        Measure("chair over hidden conduit", "DiningChair", "HiddenConduit", "same", false, true, Rot4.North, shouldLift: true);
        Measure("powered stove beside conduit", "ElectricStove", "PowerConduit", "edge", false, true, Rot4.East, shouldLift: true, powerConsumer: true);

        File.WriteAllText(ctx.ArtifactPath("throw-connections.json") ?? throw new InvalidOperationException("No artifact directory."), JsonSerializer.Serialize(new
        {
            baseline,
            observations,
            defs = usedDefs.Select(d => new
            {
                d.defName,
                category = d.category.ToString(),
                size = d.size.ToString(),
                linkType = d.graphicData?.linkType.ToString(),
                linkFlags = d.graphicData?.linkFlags.ToString(),
                asymmetricFlags = d.graphicData?.asymmetricLink?.linkFlags.ToString(),
                linkToDoors = d.graphicData?.asymmetricLink?.linkToDoors,
                powerComp = d.GetCompProperties<CompProperties_Power>()?.compClass.FullName,
                transmitsPower = d.GetCompProperties<CompProperties_Power>()?.transmitsPower,
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        yield return Wait.Frames(1);
    }
}
