using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse.AI.Group;

namespace Mugirl.GameValidation.Suites;

/// <summary>
/// 存档夹具的播种套件：把被测构建的每一类内容都放进一局快速测试游戏，运行两天，在本 Mod 的工作进行中存档，
/// 再从写出的存档中读回事实，核对每一类构建支持的内容都确实写进了存档（覆盖）。
/// tools/New-SaveFixture.ps1 用它在 1.7 游戏上重放已发布的旧构建，得到“P 夹具”；tools/Test-InGame.ps1 -Fixture control 用它让被测构建写出对照存档。
/// </summary>
/// <remarks>
/// <para>
/// 与构建无关：只用原版 API，按 <see cref="MugirlTarget.Defs{T}"/>（modContentPack 属于被测 Mod）选 Def，不引用任何 Mod 类型，
/// 所以 -Plumbing 构建（不引用 MugirlRace）能驱动任意旧构建。只能经由 Mod 自己的入口建立的状态（武器轮盘槽位、束缚服装的锁与计时、
/// 绳索登记、巨企开户、订单、任务与支援小队）按类型名与成员名反射（<see cref="LegacyApi"/>），旧构建没有对应成员时记为 "unavailable"，不算失败。
/// </para>
/// <para>
/// 每个动作记录成败（写进 sweep.json，夹具清单引用它）；单个动作失败不算套件失败（旧构建在 1.7 上本来就会有不兼容之处）。
/// 失败的是：没有雪牛娘殖民者、没有写出存档，以及覆盖缺口——构建支持（反射找得到入口、有该类 Def）的内容没有出现在写出的存档中。
/// </para>
/// <para>
/// 参数（<see cref="ValidationOptions.SuiteOptions"/>）：<c>sweep.ticks</c>（默认 120000，即两天）、<c>sweep.save</c>（存档名，默认
/// MugirlFixture-sweep）、<c>sweep.colonists</c>（默认 8）、<c>sweep.inflightTicks</c>（等本 Mod 工作出现的上限，默认 6000）。
/// </para>
/// </remarks>
internal static class ContentSweepSuite
{
    public const string Name = "sweep";

    /// <summary>结果文件（相对 <see cref="ValidationOptions.ArtifactsDir"/>）。</summary>
    public const string ResultFile = "sweep.json";

    private const int DefaultTicks = 120000;
    private const int DefaultColonists = 8;
    private const int DefaultInFlightTicks = 6000;
    private const int InFlightPollTicks = 250;
    private const float IncidentPointsCap = 300f;
    private const float QuestPoints = 300f;
    private const int BuildingAreaRadius = 40;

    /// <summary>武器轮盘至少装填的槽位数（主手之外）。</summary>
    private const int WheelSlotsWanted = 3;

    /// <summary>开户后记入的交易额：达到最高服务等级（每周礼包与支援小队）。</summary>
    private const long CorporateTurnover = 450000;

    /// <summary>武器轮盘的模组武器不够时补充的原版武器。</summary>
    private static readonly string[] VanillaWheelWeapons = ["MeleeWeapon_Knife", "Gun_Autopistol", "MeleeWeapon_Club", "Gun_Revolver"];

    public static ValidationSuite Create()
    {
        var sweep = new Sweep();
        return new ValidationSuite(
            Name,
            "seed every content kind of the target build into a quick-test colony (pawns, items, buildings, apparel incl. restraint locks and timers, weapons and the wheel, "
                + "hediffs, genes, xenotypes, abilities, traits, roping, incidents, quests, corporate account, orders, missions and support lord), run two days, "
                + "settle the map, save while mod jobs run, extract facts and check coverage",
            SuiteNeeds.MugirlMod,
            [
                new ValidationStep("inventory", ctx => Inventory(ctx, sweep)),
                new ValidationStep("colonists", ctx => Colonists(ctx, sweep)),
                new ValidationStep("things", ctx => Things(ctx, sweep), TimeoutSeconds: 300),
                new ValidationStep("apparel", ctx => Apparel(ctx, sweep)),
                new ValidationStep("restraint-states", ctx => RestraintStates(ctx, sweep)),
                new ValidationStep("weapons", ctx => Weapons(ctx, sweep)),
                new ValidationStep("hediffs", ctx => Hediffs(ctx, sweep)),
                new ValidationStep("genes", ctx => GenesAbilitiesTraits(ctx, sweep)),
                new ValidationStep("roping", ctx => Roping(ctx, sweep)),
                new ValidationStep("incidents", ctx => Incidents(ctx, sweep), TimeoutSeconds: 300),
                new ValidationStep("quests", ctx => Quests(ctx, sweep), TimeoutSeconds: 300),
                new ValidationStep("corporate", ctx => Corporate(ctx, sweep), TimeoutSeconds: 300),
                new ValidationStep("ticks", ctx => RunTicks(ctx, sweep), TimeoutSeconds: 3600),
                new ValidationStep("settle", ctx => Settle(ctx, sweep)),
                new ValidationStep("in-flight", ctx => InFlight(ctx, sweep), TimeoutSeconds: 900),
                new ValidationStep("save", ctx => Save(ctx, sweep), TimeoutSeconds: 600),
            ],
            inDefaultRun: false)
        {
            InAllRun = false,
        };
    }

    private static IEnumerable<Wait> Inventory(ValidationContext ctx, Sweep sweep)
    {
        MugirlTarget target = ctx.RequireTarget();
        sweep.Target = target;

        // 无人值守：任务与事件弹出的对话树按第一个选项作答，不只关闭（否则必须作答的对话每个 Tick 重新弹出）。
        ctx.AnswerDialogs = true;
        List<Def> defs = target.AllDefs();
        sweep.DefNames = [.. defs.Select(d => d.defName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        sweep.ThingDefNames = [.. defs.OfType<ThingDef>().Select(d => d.defName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        foreach (IGrouping<string, Def> group in defs.GroupBy(DatabaseRoot, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sweep.DefsByRoot[group.Key] = new JsonArray([.. group.Select(d => (JsonNode?)JsonValue.Create(d.defName))]);
        }

        var byType = new JsonObject();
        foreach (IGrouping<string, Def> group in defs.GroupBy(d => d.GetType().Name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            byType[group.Key] = group.Count();
        }

        // 束缚服装：在本构建中新造一件，读出没有写进存档时的值（锁次数的默认值取决于 Def）。
        foreach (ThingDef def in target.Defs<ThingDef>().Where(d => d.IsApparel && IsRestraint(d)))
        {
            sweep.Try("restraint-defaults", def.defName, () =>
            {
                Thing made = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                Dictionary<string, string> defaults = RestraintValues(made);
                sweep.RestraintDefaults[def.defName] = defaults;
                return string.Join(", ", defaults.Select(p => $"{p.Key}={p.Value}"));
            });
        }

        sweep.Available["restraint-locks"] = sweep.RestraintDefaults.Values.Any(d => d.ContainsKey(SaveFactQueries.IsLockedKey));
        sweep.Available["restraint-timers"] = sweep.RestraintDefaults.Values.Any(d => d.ContainsKey(SaveFactQueries.TimerKey));
        sweep.Available["milk"] = GameFixtures.MugirlRace?.comps.Any(c => c.compClass?.Name.Contains("Milk", StringComparison.Ordinal) == true) == true;
        sweep.Available["weapon-wheel"] = target.Assembly is { } wheelAssembly
            && LegacyApi.LoadableTypes(wheelAssembly).Any(t => typeof(ThingComp).IsAssignableFrom(t) && t.Name.Contains("WeaponWheel", StringComparison.Ordinal));
        sweep.Available["corporate"] = LegacyApi.FindType(target.Assembly, "CorporateIntroduction") is not null;
        sweep.Available["lord"] = target.Assembly is { } assembly && LegacyApi.LoadableTypes(assembly).Any(t => typeof(LordJob).IsAssignableFrom(t) && !t.IsAbstract);
        sweep.Result["inventory"] = byType;
        ctx.Report.Info($"{defs.Count} Defs from {target.PackageId}: " + string.Join(", ", byType.Select(p => $"{p.Key} {p.Value}")));
        ctx.Report.Info($"{sweep.RestraintDefaults.Count} restraint Def(s); features: " + string.Join(", ", sweep.Available.Select(p => $"{p.Key} {(p.Value ? "yes" : "no")}")));
        ctx.Report.Check(defs.Count > 0, "the target build defines Defs", () => "no Def has the target's modContentPack");
        yield break;
    }

    private static IEnumerable<Wait> Colonists(ValidationContext ctx, Sweep sweep)
    {
        ValidationReport report = ctx.Report;
        Map map = ctx.Map;
        int wanted = ctx.Options.SuiteOption("sweep.colonists", DefaultColonists);
        GameFixtures.ClearArea(map, map.Center, 12);
        try
        {
            sweep.Colonists.AddRange(GameFixtures.EnsureMugirlColonists(ctx, wanted));
            sweep.Record("colonists", "Mugirl_Colony x" + sweep.Colonists.Count, ok: true, null);
        }
        catch (Exception ex)
        {
            sweep.Record("colonists", "Mugirl colonists", ok: false, ex.Message);
        }

        if (!report.Check(sweep.Colonists.Count > 0, "Mugirl colonists exist", () => "no Mugirl colonist could be generated; the fixture would be useless"))
        {
            yield break;
        }

        // 每个 PawnKindDef 一个 Pawn：类人与动物种类加入玩家阵营（不产生战斗），其他种类不属于任何阵营。
        foreach (PawnKindDef kind in sweep.Target!.Defs<PawnKindDef>())
        {
            sweep.Try("pawnkind", kind.defName, () =>
            {
                Faction? faction = kind.RaceProps.Humanlike || kind.RaceProps.Animal ? Faction.OfPlayer : null;
                var request = new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false, developmentalStages: DevelopmentalStage.Adult);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                GenSpawn.Spawn(pawn, GameFixtures.FreeCellNear(map, map.Center, 14), map);
                if (pawn.def == GameFixtures.MugirlRace && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike)
                {
                    sweep.Extra.Add(pawn);
                }

                return pawn.ThingID;
            });
        }

        report.Info($"{sweep.Colonists.Count} Mugirl colonists, {sweep.Extra.Count} more player Mugirls from other kinds");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Things(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        MugirlTarget target = sweep.Target ?? ctx.RequireTarget();
        IntVec3 origin = BuildingOrigin(map);
        GameFixtures.ClearArea(map, origin + new IntVec3(BuildingAreaRadius, 0, BuildingAreaRadius), BuildingAreaRadius);
        foreach (IntVec3 cell in CellRect.FromLimits(origin, origin + new IntVec3(2 * BuildingAreaRadius, 0, 2 * BuildingAreaRadius)).ClipInsideMap(map).Cells)
        {
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
        }

        var placer = new BuildingPlacer(map, origin, 2 * BuildingAreaRadius);
        foreach (ThingDef def in target.Defs<ThingDef>())
        {
            if (!IsSweepable(def) || def.IsApparel || def.IsWeapon)
            {
                continue;
            }

            switch (def.category)
            {
                case ThingCategory.Building:
                    sweep.Try("building", def.defName, () =>
                    {
                        Thing building = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                        if (def.CanHaveFaction)
                        {
                            building.SetFactionDirect(Faction.OfPlayer);
                        }

                        IntVec3 cell = placer.Next(def) ?? throw new InvalidOperationException("no free spot for size " + def.size);
                        GenSpawn.Spawn(building, cell, map, Rot4.North, WipeMode.Vanish);
                        return building.ThingID;
                    });
                    break;
                case ThingCategory.Plant:
                    sweep.Try("plant", def.defName, () =>
                    {
                        IntVec3 cell = CellFinder.TryFindRandomCellNear(map.Center, map, 40,
                            c => c.GetPlant(map) is null && c.GetEdifice(map) is null && def.CanEverPlantAt(c, map), out IntVec3 found)
                            ? found
                            : throw new InvalidOperationException("no cell where it can grow");
                        return GenSpawn.Spawn(def, cell, map, WipeMode.Vanish).ThingID;
                    });
                    break;
                case ThingCategory.Filth:
                    sweep.Try("filth", def.defName, () =>
                        FilthMaker.TryMakeFilth(GameFixtures.FreeCellNear(map, map.Center, 10), map, def, 1, FilthSourceFlags.None, shouldPropagate: false)
                            ? "made" : throw new InvalidOperationException("TryMakeFilth returned false"));
                    break;
                case ThingCategory.Item:
                    sweep.Try("item", def.defName, () => PlaceItem(map, def));
                    break;
                default:
                    break;
            }
        }

        sweep.ReportStep(ctx.Report, "building", "plant", "filth", "item");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Apparel(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        List<Pawn> wearers = [.. sweep.Colonists.Concat(sweep.Extra).Where(p => p is { Spawned: true, Dead: false, apparel: not null })];
        // 束缚服装优先：它们的锁与计时是夹具最需要的状态。
        IEnumerable<ThingDef> defs = sweep.Target!.Defs<ThingDef>().Where(d => d.IsApparel && IsSweepable(d))
            .OrderBy(d => IsRestraint(d) ? 0 : 1).ThenBy(d => d.defName, StringComparer.Ordinal);
        foreach (ThingDef def in defs)
        {
            sweep.Try(IsRestraint(def) ? "restraint" : "apparel", def.defName, () =>
            {
                var apparel = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                Pawn? wearer = wearers.FirstOrDefault(p => ApparelUtility.HasPartsToWear(p, def) && p.apparel.CanWearWithoutDroppingAnything(def)
                    && EquipmentUtility.CanEquip(apparel, p));
                if (wearer is null && IsRestraint(def))
                {
                    wearer = wearers.FirstOrDefault(p => ApparelUtility.HasPartsToWear(p, def) && EquipmentUtility.CanEquip(apparel, p));
                }

                if (wearer is null)
                {
                    return "on the ground: " + PlaceThing(map, apparel);
                }

                wearer.apparel.Wear(apparel, dropReplacedApparel: true, locked: false);
                if (IsRestraint(def))
                {
                    sweep.WornRestraints.Add(apparel);
                }

                return $"worn by {wearer.ThingID}";
            });
        }

        sweep.ReportStep(ctx.Report, "restraint", "apparel");
        yield return Wait.Frames(2);
    }

    /// <summary>
    /// 束缚服装的锁状态轮流设置为：默认（上锁、默认锁次数）、已解锁（isLocked=false、lockCount=0，并解除原版的服装锁定）、
    /// 锁次数多一次；带电击项圈组件的设置非零计时。按字段名反射，旧构建没有字段时记为 unavailable。
    /// </summary>
    private static IEnumerable<Wait> RestraintStates(ValidationContext ctx, Sweep sweep)
    {
        int index = 0;
        foreach (Apparel apparel in sweep.WornRestraints.Where(a => a is { Destroyed: false, Wearer: not null }))
        {
            int pattern = index++ % 3;
            sweep.Try("restraint-state", apparel.ThingID, () =>
            {
                var done = new List<string>();
                if (pattern == 1 && LegacyApi.SetAny(apparel, false, "mugirlRestraintLocked", "isLocked") && LegacyApi.SetAny(apparel, 0, "mugirlRestraintLockCount", "lockCount"))
                {
                    apparel.Wearer?.apparel.Unlock(apparel);
                    done.Add("unlocked");
                }
                else if (pattern == 2 && LegacyApi.GetAny(apparel, "mugirlRestraintLockCount", "lockCount") is int count && LegacyApi.SetAny(apparel, count + 1, "mugirlRestraintLockCount", "lockCount"))
                {
                    done.Add("lockCount " + (count + 1).ToString(CultureInfo.InvariantCulture));
                }

                if (apparel.AllComps.FirstOrDefault(c => c.GetType().Name.Contains("ShockCollar", StringComparison.Ordinal)) is { } collar
                    && LegacyApi.SetAny(collar, 1234 + index, "mugirlShockCheckTicks", "ticks"))
                {
                    done.Add("collar timer " + (1234 + index).ToString(CultureInfo.InvariantCulture));
                }

                return done.Count > 0 ? string.Join(", ", done) : pattern == 0 ? "default state" : "unavailable (no lock fields in this build)";
            });
        }

        sweep.ReportStep(ctx.Report, "restraint-state");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Weapons(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        List<Pawn> holders = [.. sweep.Colonists.Where(p => p is { Spawned: true, Dead: false, equipment: not null })];
        Pawn? wheelHolder = holders.FirstOrDefault();
        if (wheelHolder is not null)
        {
            sweep.Try("wheel-unlock", wheelHolder.ThingID, () => UnlockWheel(wheelHolder));
        }

        // 第一名殖民者：主手一件，其余模组武器尽量装进她的武器轮盘；装不下的给其他殖民者做主手，再不行放在地上。
        int nextHolder = 1;
        foreach (ThingDef def in sweep.Target!.Defs<ThingDef>().Where(d => d.IsWeapon && !d.IsApparel && IsSweepable(d)))
        {
            sweep.Try("weapon", def.defName, () =>
            {
                var weapon = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                if (wheelHolder is null)
                {
                    return "on the ground: " + PlaceThing(map, weapon);
                }

                if (wheelHolder.equipment.Primary is null && EquipmentUtility.CanEquip(weapon, wheelHolder))
                {
                    wheelHolder.equipment.AddEquipment(weapon);
                    return $"primary of {wheelHolder.ThingID}";
                }

                string placed = PlaceThing(map, weapon, wheelHolder.Position);
                if (TryAttachToWheel(wheelHolder, weapon, out string wheel))
                {
                    return $"wheel of {wheelHolder.ThingID}: {wheel}";
                }

                for (; nextHolder < holders.Count; nextHolder++)
                {
                    Pawn holder = holders[nextHolder];
                    if (holder.equipment.Primary is null && EquipmentUtility.CanEquip(weapon, holder))
                    {
                        if (weapon.Spawned)
                        {
                            weapon.DeSpawn();
                        }

                        holder.equipment.AddEquipment(weapon);
                        return $"primary of {holder.ThingID} (wheel: {wheel})";
                    }
                }

                return $"on the ground ({placed}); wheel: {wheel}";
            });
        }

        // 轮盘槽位不够时用原版武器补足，保证存档中有多个槽位与备用武器。
        if (wheelHolder is not null)
        {
            foreach (string defName in VanillaWheelWeapons)
            {
                if (WheelSlotsUsed(wheelHolder) >= WheelSlotsWanted || DefDatabase<ThingDef>.GetNamedSilentFail(defName) is not { } def)
                {
                    continue;
                }

                sweep.Try("wheel-fill", defName, () =>
                {
                    var weapon = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                    PlaceThing(map, weapon, wheelHolder.Position);
                    return TryAttachToWheel(wheelHolder, weapon, out string wheel) ? "wheel " + wheel : throw new InvalidOperationException(wheel);
                });
            }
        }

        sweep.ReportStep(ctx.Report, "weapon", "wheel-unlock", "wheel-fill");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Hediffs(ValidationContext ctx, Sweep sweep)
    {
        List<Pawn> patients = [.. sweep.Colonists.Skip(2).Concat(sweep.Extra).Where(p => p is { Spawned: true, Dead: false })];
        if (patients.Count == 0)
        {
            patients = [.. sweep.Colonists.Where(p => p is { Spawned: true, Dead: false })];
        }

        int next = 0;
        foreach (HediffDef def in sweep.Target!.Defs<HediffDef>())
        {
            sweep.Try("hediff", def.defName, () =>
            {
                if (def.lethalSeverity > 0f && def.initialSeverity >= def.lethalSeverity)
                {
                    return "skipped: lethal at its initial severity";
                }

                Pawn pawn = patients[next++ % patients.Count];
                BodyPartRecord? part = NeedsPart(def) ? ChoosePart(pawn) : null;
                Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
                pawn.health.AddHediff(hediff, part);
                return $"{pawn.ThingID}{(part is null ? string.Empty : " on " + part.def.defName)}";
            });
        }

        sweep.ReportStep(ctx.Report, "hediff");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> GenesAbilitiesTraits(ValidationContext ctx, Sweep sweep)
    {
        List<Pawn> pawns = [.. sweep.Colonists.Concat(sweep.Extra).Where(p => p is { Spawned: true, Dead: false })];
        int next = 0;
        if (ModsConfig.BiotechActive)
        {
            foreach (XenotypeDef def in sweep.Target!.Defs<XenotypeDef>())
            {
                sweep.Try("xenotype", def.defName, () =>
                {
                    Pawn pawn = pawns[next++ % pawns.Count];
                    pawn.genes.SetXenotype(def);
                    return pawn.ThingID;
                });
            }

            Pawn geneCarrier = pawns[next++ % pawns.Count];
            foreach (GeneDef def in sweep.Target!.Defs<GeneDef>())
            {
                sweep.Try("gene", def.defName, () =>
                {
                    geneCarrier.genes.AddGene(def, xenogene: true);
                    return geneCarrier.ThingID;
                });
            }
        }
        else
        {
            sweep.Record("gene", "(all)", ok: true, "skipped: Biotech is not active");
        }

        foreach (AbilityDef def in sweep.Target!.Defs<AbilityDef>())
        {
            sweep.Try("ability", def.defName, () =>
            {
                Pawn pawn = pawns[next++ % pawns.Count];
                pawn.abilities.GainAbility(def);
                return pawn.ThingID;
            });
        }

        foreach (TraitDef def in sweep.Target!.Defs<TraitDef>())
        {
            sweep.Try("trait", def.defName, () =>
            {
                Pawn pawn = pawns[next++ % pawns.Count];
                int degree = def.degreeDatas.Count > 0 ? def.degreeDatas[0].degree : 0;
                pawn.story.traits.GainTrait(new Trait(def, degree, forced: true), suppressConflicts: true);
                return pawn.ThingID;
            });
        }

        sweep.ReportStep(ctx.Report, "xenotype", "gene", "ability", "trait");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Roping(ValidationContext ctx, Sweep sweep)
    {
        if (sweep.Colonists.Count < 2)
        {
            ctx.Report.Skip("roping", "fewer than two Mugirl colonists");
            yield break;
        }

        sweep.Roper = sweep.Colonists[0];
        sweep.Ropee = sweep.Colonists[1];
        sweep.Try("roping", $"{sweep.Roper.ThingID} ropes {sweep.Ropee.ThingID}", () => Rope(sweep.Roper, sweep.Ropee));
        sweep.ReportStep(ctx.Report, "roping");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> Incidents(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        foreach (IncidentDef def in sweep.Target!.Defs<IncidentDef>())
        {
            sweep.Try("incident", def.defName, () =>
            {
                IIncidentTarget? target = def.TargetAllowed(map) ? map : def.TargetAllowed(Find.World) ? Find.World : null;
                if (target is null)
                {
                    return "skipped: neither the map nor the world is an allowed target";
                }

                IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, target);
                parms.forced = true;
                if (parms.points > IncidentPointsCap)
                {
                    parms.points = IncidentPointsCap;
                }

                return def.Worker.TryExecute(parms) ? "executed" : throw new InvalidOperationException("TryExecute returned false");
            });
            yield return Wait.Frames(1);
        }

        sweep.ReportStep(ctx.Report, "incident");
    }

    private static IEnumerable<Wait> Quests(ValidationContext ctx, Sweep sweep)
    {
        // 由本 Mod 事件发放的任务已经在 incidents 步骤中按原本的上下文生成，不再脱离上下文重复生成。
        Dictionary<QuestScriptDef, IncidentDef> givenByIncident = sweep.Target!.Defs<IncidentDef>()
            .Where(i => i.questScriptDef is not null)
            .GroupBy(i => i.questScriptDef)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (QuestScriptDef def in sweep.Target!.Defs<QuestScriptDef>())
        {
            sweep.Try("quest", def.defName, () =>
            {
                if (!def.IsRootAny)
                {
                    return "skipped: not a root script";
                }

                if (givenByIncident.TryGetValue(def, out IncidentDef? incident))
                {
                    return "skipped: given by the incident " + incident.defName;
                }

                var slate = new Slate();
                slate.Set("points", QuestPoints);
                slate.Set("map", ctx.Map);
                Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, slate);
                if (quest.State == QuestState.NotYetAccepted)
                {
                    quest.Accept(sweep.Colonists.FirstOrDefault(p => p is { Dead: false }));
                }

                return $"quest {quest.id}, state {quest.State}";
            });
            yield return Wait.Frames(1);
        }

        sweep.ReportStep(ctx.Report, "quest");
    }

    /// <summary>
    /// 巨企线（有 CorporateIntroduction 的构建）：开发者入口让代表到访，按开户对话的“接受”完成开户，记入交易额，领取每周礼包（订单），
    /// 接受一个任务。支援小队（Mugirl 的 LordJob）在存档前呼叫（<see cref="InFlight"/>）。
    /// </summary>
    private static IEnumerable<Wait> Corporate(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        Assembly? assembly = sweep.Target?.Assembly;
        Type? introType = LegacyApi.FindType(assembly, "CorporateIntroduction");
        if (introType is null || LegacyApi.GameComponent(introType) is not { } intro)
        {
            sweep.Record("corporate", "(all)", ok: true, "unavailable: this build has no CorporateIntroduction");
            ctx.Report.Skip("corporate", "this build has no corporate network");
            yield break;
        }

        sweep.Try("corporate", "representative", () =>
        {
            MethodInfo trigger = LegacyApi.Method(introType, "DevTriggerRepresentative", 1) ?? throw new MissingMethodException(introType.Name, "DevTriggerRepresentative");
            return trigger.Invoke(intro, [map]) is string problem ? throw new InvalidOperationException("DevTriggerRepresentative: " + problem) : "arrived";
        });
        yield return Wait.Frames(2);
        sweep.Try("corporate", "account", () =>
        {
            Pawn representative = introType.GetProperty("Representative")?.GetValue(intro) as Pawn ?? throw new InvalidOperationException("no representative");
            Pawn negotiator = sweep.Colonists.Skip(2).Concat(sweep.Colonists).FirstOrDefault(p => p is { Spawned: true, Dead: false, Downed: false }
                && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) ?? throw new InvalidOperationException("no colonist can talk");
            MethodInfo complete = LegacyApi.Method(introType, "Complete", 2) ?? throw new MissingMethodException(introType.Name, "Complete");
            complete.Invoke(intro, [negotiator, representative]);
            return introType.GetProperty("Completed")?.GetValue(intro) is true ? $"opened by {negotiator.ThingID}" : throw new InvalidOperationException("the account is still closed");
        });

        Type? networkType = LegacyApi.FindType(assembly, "CorporateNetwork");
        if (LegacyApi.GameComponent(networkType) is { } network && LegacyApi.FindType(assembly, "CorporateTradeContext") is { } contextType)
        {
            object context = Activator.CreateInstance(contextType, [map, null]) ?? throw new InvalidOperationException("CorporateTradeContext");
            sweep.Try("corporate", "turnover", () =>
            {
                MethodInfo add = LegacyApi.Method(networkType, "AddTradeTurnover", 1) ?? throw new MissingMethodException(networkType!.Name, "AddTradeTurnover");
                add.Invoke(network, [CorporateTurnover]);
                LegacyApi.Method(networkType, "EnsureWeeklyOffers", 0)?.Invoke(network, null);
                return "turnover " + CorporateTurnover.ToString(CultureInfo.InvariantCulture);
            });
            sweep.Try("corporate", "weekly gift order", () => CallWithReason(network, networkType, "TryClaimWeeklyGift", [context]));
            sweep.Try("corporate", "mission", () => AcceptMission(network, networkType, context));
        }
        else
        {
            sweep.Record("corporate", "network", ok: false, "no CorporateNetwork component or CorporateTradeContext type");
        }

        sweep.ReportStep(ctx.Report, "corporate");
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> RunTicks(ValidationContext ctx, Sweep sweep)
    {
        ValidationReport report = ctx.Report;
        using CombatSpeedScope? speed = CombatSpeedScope.Enter(ctx);
        int total = ctx.Options.SuiteOption("sweep.ticks", DefaultTicks);
        int start = Find.TickManager.TicksGame;
        int half = Math.Max(1, total / 2);
        yield return Wait.Ticks(half, timeoutSeconds: 1700);
        report.Info($"after {Find.TickManager.TicksGame - start} ticks: {Alive(sweep)} of {sweep.Colonists.Count + sweep.Extra.Count} seeded Mugirls alive");
        yield return Wait.Ticks(total - half, timeoutSeconds: 1700);
        int advanced = Find.TickManager.TicksGame - start;
        sweep.Result["ticks"] = advanced;
        report.Check(advanced >= total, $"the game ran {total} ticks", () => $"only {advanced} ticks");
        report.Info($"{Alive(sweep)} seeded Mugirls alive after {advanced} ticks");
    }

    /// <summary>
    /// 存档前让地图平静下来：读档后的升级套件要求播种的雪牛娘在 2500 Tick 内存活，存档时还在进行的战斗会让这一检查随机失败。
    /// 敌对的本 Mod 种族 Pawn 麻醉倒地，其他敌对 Pawn 移交世界保留引用；扑灭火焰；结束殖民者的精神崩溃。
    /// </summary>
    private static IEnumerable<Wait> Settle(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        int downed = 0;
        int removed = 0;
        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (pawn.Dead || !pawn.HostileTo(Faction.OfPlayer))
            {
                continue;
            }

            if (sweep.Target!.Owns(pawn.def))
            {
                if (!pawn.Downed)
                {
                    pawn.health.AddHediff(HediffDefOf.Anesthetic);
                    downed++;
                }
            }
            else
            {
                // 其他 Pawn 的关系或记忆仍可能引用它：销毁会产生未深存的 otherPawn 引用。
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
                pawn.DeSpawn();
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                removed++;
            }
        }

        int fires = 0;
        foreach (Thing fire in map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())
        {
            fire.Destroy(DestroyMode.Vanish);
            fires++;
        }

        int recovered = 0;
        foreach (Pawn colonist in sweep.Colonists.Concat(sweep.Extra).Where(p => p is { Dead: false, InMentalState: true }))
        {
            colonist.MentalState.RecoverFromState();
            recovered++;
        }

        sweep.Record("settle", "map", ok: true, $"{downed} hostile mod pawn(s) anaesthetised, {removed} other hostile pawn(s) retained in the world, {fires} fire(s), {recovered} mental state(s) ended");
        ctx.Report.Info($"settled the map: {downed} hostile mod pawn(s) anaesthetised, {removed} other hostile pawn(s) retained in the world, {fires} fire(s), {recovered} mental state(s) ended");
        yield return Wait.Ticks(60);
    }

    private static IEnumerable<Wait> InFlight(ValidationContext ctx, Sweep sweep)
    {
        Map map = ctx.Map;
        ReRope(sweep, "re-rope before the in-flight wait");
        if (Available(sweep, "weapon-wheel"))
        {
            EnsureWheel(map, sweep);
        }

        // 支援小队：本 Mod 唯一的 LordJob，存档时必须在进行中。
        Assembly? assembly = sweep.Target?.Assembly;
        Type? networkType = LegacyApi.FindType(assembly, "CorporateNetwork");
        if (LegacyApi.GameComponent(networkType) is { } network && LegacyApi.FindType(assembly, "CorporateTradeContext") is { } contextType)
        {
            sweep.Try("corporate", "support lord", () =>
            {
                // 两天里的袭击可能让巨企与玩家敌对（暂停新业务）：先把好感恢复到中立，支援小队才能呼叫。
                string relation = string.Empty;
                if (networkType!.GetProperty("CorporateFaction")?.GetValue(network) is Faction corporation && corporation.HostileTo(Faction.OfPlayer))
                {
                    corporation.TryAffectGoodwillWith(Faction.OfPlayer, -corporation.PlayerGoodwill, canSendMessage: false, canSendHostilityLetter: false, reason: null, lookTarget: null);
                    relation = $"goodwill restored to {corporation.PlayerGoodwill}; ";
                }

                object context = Activator.CreateInstance(contextType, [map, null]) ?? throw new InvalidOperationException("CorporateTradeContext");
                IntVec3 cell = CellFinder.TryFindRandomCellNear(map.Center, map, 25, c => c.Standable(map) && !c.Roofed(map) && !c.Fogged(map), out IntVec3 found)
                    ? found
                    : throw new InvalidOperationException("no open cell for the drop pod");
                return relation + CallWithReason(network, networkType, "TryCallSupport", [context, map, cell]);
            });
            sweep.ReportStep(ctx.Report, "corporate");
        }

        sweep.ReportStep(ctx.Report, "roping", "wheel-reseed");
        int limit = ctx.Options.SuiteOption("sweep.inflightTicks", DefaultInFlightTicks);
        int waited = 0;
        while (ModJobs(sweep).Count == 0 && waited < limit)
        {
            yield return Wait.Ticks(InFlightPollTicks);
            waited += InFlightPollTicks;
        }

        List<string> jobs = ModJobs(sweep);
        ctx.Report.Info(jobs.Count == 0
            ? $"no job of the target build was running after {waited} extra ticks"
            : $"{jobs.Count} mod job(s) in flight after {waited} extra ticks: {string.Join("; ", jobs.Take(10))}");
        sweep.Record("in-flight", "mod jobs at save", jobs.Count > 0, string.Join("; ", jobs));
    }

    private static IEnumerable<Wait> Save(ValidationContext ctx, Sweep sweep)
    {
        ValidationReport report = ctx.Report;
        ctx.AnswerDialogs = false;
        RetainRelatedPawns(report);

        // 绳索在等待中可能断开：存档前一帧重新系上（夹具需要读档时仍然存在的绳索）。
        ReRope(sweep, "re-rope just before saving");
        List<string> jobs = ModJobs(sweep);
        var save = new SaveWait(ctx.Options.SuiteOption("sweep.save", "MugirlFixture-sweep"));
        bool canonical = LegacyApi.Field(LegacyApi.FindType(sweep.Target?.Assembly, "CompMilkProduction"), "mugirlMilkFullness") is not null;
        List<(string Id, string Path, string Value)> seeded = [];
        TimeSpeed previousSpeed = Find.TickManager.CurTimeSpeed;
        try
        {
            if (canonical)
            {
                Find.TickManager.Pause();
                if (Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
                {
                    throw new InvalidOperationException("Canonical fixture state must be seeded while paused.");
                }
                seeded = SeedCanonicalState(sweep);
            }
            yield return save;
        }
        finally
        {
            if (canonical) { Find.TickManager.CurTimeSpeed = previousSpeed; }
        }
        if (!report.Check(File.Exists(save.FilePath) && new FileInfo(save.FilePath).Length > 0, "the fixture save is written", () => "no save at " + save.FilePath))
        {
            yield break;
        }

        var file = new FileInfo(save.FilePath);
        string sha256;
        using (FileStream stream = file.OpenRead())
        {
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var restraintDefaults = sweep.RestraintDefaults.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)p.Value, StringComparer.Ordinal);
        XDocument document = SaveXml.Load(save.FilePath);
        VerifySeededState(document, seeded, report);
        SaveFactQueries queries = canonical ? SaveFactQueries.Current(sweep.DefNames, sweep.ThingDefNames, restraintDefaults)
            : SaveFactQueries.Legacy(sweep.DefNames, sweep.ThingDefNames, restraintDefaults);
        SaveFacts facts = SaveFacts.Read(document, queries);
        report.Info($"saved {save.FilePath}: {file.Length / 1024} KiB, {facts.Values.Count} facts, {jobs.Count} mod job(s) in flight");
        report.Check(!File.ReadAllText(save.FilePath).Contains("Mugirl.GameValidation.", StringComparison.Ordinal), "the save holds no test-mod types",
            () => "a class of the test assembly was written into the save");

        JsonObject coverage = Coverage(sweep, facts.Values, out List<string> missing);
        foreach ((string category, JsonNode? entry) in coverage)
        {
            report.Info($"coverage {category}: {entry?["detail"]}");
        }

        ReportCoverage(report, missing);

        sweep.Result["target"] = new JsonObject
        {
            ["packageId"] = sweep.Target?.PackageId,
            ["assembly"] = sweep.Target?.Assembly?.Location,
            ["assemblyVersion"] = sweep.Target?.Assembly?.GetName().Version?.ToString(),
            ["mvid"] = sweep.Target?.Assembly?.ManifestModule.ModuleVersionId.ToString(),
        };
        sweep.Result["save"] = new JsonObject
        {
            ["name"] = save.SaveName,
            ["path"] = save.FilePath,
            ["bytes"] = file.Length,
            ["sha256"] = sha256,
            ["tick"] = Find.TickManager.TicksGame,
            ["gameVersion"] = VersionControl.CurrentVersionStringWithRev,
        };
        sweep.Result["inFlight"] = new JsonArray([.. jobs.Select(j => (JsonNode?)JsonValue.Create(j))]);
        sweep.Result["modDefs"] = new JsonArray([.. sweep.DefNames.Select(d => (JsonNode?)JsonValue.Create(d))]);
        sweep.Result["modThingDefs"] = new JsonArray([.. sweep.ThingDefNames.Select(d => (JsonNode?)JsonValue.Create(d))]);
        sweep.Result["modDefsByRoot"] = sweep.DefsByRoot;
        sweep.Result["restraintDefaults"] = new JsonObject([.. sweep.RestraintDefaults.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, ArtifactJson.Object(p.Value.OrderBy(v => v.Key, StringComparer.Ordinal))))]);
        sweep.Result["coverage"] = coverage;
        sweep.Result["coverageMissing"] = new JsonArray([.. missing.Select(m => (JsonNode?)JsonValue.Create(m))]);
        sweep.Result["facts"] = ArtifactJson.Object(facts.Values);
        sweep.Result["factsFormat"] = canonical ? "current-v1" : "legacy";
        sweep.Result["actions"] = sweep.Actions;
        sweep.Result["actionSummary"] = sweep.Summary();
        if (ctx.ArtifactPath(ResultFile) is { } path)
        {
            ArtifactJson.Write(path, sweep.Result);
            report.Info("results written to " + path);
        }
        else
        {
            report.Info("no artifacts folder was given; " + ResultFile + " was not written");
        }
    }

    /// <summary>暂停后播种非默认的规范状态，随后从实际写出的节点逐值核对，避免深路径写错而测试空状态。</summary>
    private static List<(string Id, string Path, string Value)> SeedCanonicalState(Sweep sweep)
    {
        var expected = new List<(string Id, string Path, string Value)>();
        Pawn pawn = sweep.Colonists.Concat(sweep.Extra).FirstOrDefault(candidate => candidate is { Dead: false, Spawned: true }
            && candidate.AllComps.Any(comp => LegacyApi.Field(comp.GetType(), "mugirlMilkFullness") is not null))
            ?? throw new InvalidOperationException("No canonical milk component remains for the state fixture.");

        void Seed(Thing thing, object owner, string fieldPath, string savePath, object value)
        {
            if (!LegacyApi.SetAny(owner, value, fieldPath)) { throw new MissingFieldException(owner.GetType().FullName, fieldPath); }
            expected.Add((thing.ThingID, savePath, LegacyApi.ScribeText(value)!));
        }

        ThingComp milk = pawn.AllComps.Single(comp => LegacyApi.Field(comp.GetType(), "mugirlMilkFullness") is not null);
        Seed(pawn, milk, "mugirlMilkFullness", "mugirlMilkFullness", 1f);
        Seed(pawn, milk, "mugirlMilkThreshold", "mugirlMilkThreshold", 0.625f);
        Seed(pawn, milk, "mugirlMilkPendingTicks", "mugirlMilkPendingTicks", 17);
        Seed(pawn, milk, "mugirlMilkFullNotified", "mugirlMilkFullNotified", true);
        Seed(pawn, milk, "mugirlMilkLastFullNotifyTick", "mugirlMilkLastFullNotifyTick", Find.TickManager.TicksGame - 137);

        ThingComp mount = pawn.AllComps.Single(comp => LegacyApi.Field(comp.GetType(), "mugirlMountSchedule") is not null);
        foreach ((string field, int value) in new[] { ("physiology", 17), ("safety", 23), ("turret", 31), ("melee", 43) })
        {
            Seed(pawn, mount, "mugirlMountSchedule." + field, "mugirlMountSchedule/" + field, value);
        }
        Seed(pawn, mount, "mugirlMountTurret.burstCooldownTicksLeft", "mugirlMountTurret/burstCooldownTicksLeft", 61);
        ThingComp wheel = WheelOf(pawn) ?? throw new InvalidOperationException("The canonical fixture pawn has no wheel.");
        Seed(pawn, wheel, "swordDance.swordDanceFailureStreak", "mugirlWheelSwordDanceFailureStreak", 3);

        int index = 0;
        foreach (Apparel apparel in sweep.WornRestraints.Where(thing => thing is { Destroyed: false, Wearer: not null }))
        {
            int current = index++;
            if (LegacyApi.GetAny(apparel, "mugirlRestraintLockCount") is int locks)
            {
                Seed(apparel, apparel, "mugirlRestraintLockCount", "mugirlRestraintLockCount", Math.Max(1, locks) + 1);
            }
            foreach (ThingComp comp in apparel.AllComps)
            {
                if (LegacyApi.Field(comp.GetType(), "mugirlShockCheckTicks") is not null)
                {
                    Seed(apparel, comp, "mugirlShockCheckTicks", "mugirlShockCheckTicks", 1234 + current);
                }
                if (LegacyApi.Field(comp.GetType(), "mugirlHelmetTicksToChange") is not null)
                {
                    Seed(apparel, comp, "mugirlHelmetTicksToChange", "mugirlHelmetTicksToChange", 701 + current);
                    Seed(apparel, comp, "mugirlHelmetNonBlockedTicks", "mugirlHelmetNonBlockedTicks", 29 + current);
                }
            }
        }
        return expected;
    }

    private static void VerifySeededState(XDocument document, List<(string Id, string Path, string Value)> seeded, ValidationReport report)
    {
        ILookup<string, XElement> things = document.Descendants().Where(SaveXml.IsThing).ToLookup(element => SaveXml.Id(element)!, StringComparer.Ordinal);
        foreach ((string id, string path, string expected) in seeded)
        {
            XElement[] candidates = [.. things[id]];
            XElement? node = candidates.Length == 1 ? candidates[0] : null;
            foreach (string segment in path.Split('/')) { node = node?.Element(segment); }
            string? actual = node?.Value;
            report.Check(actual == expected, "non-default canonical state is serialized",
                () => id + "/" + path + ": expected " + expected + ", actual " + (actual ?? "<missing or duplicate Thing>"));
        }
    }

    private static void ReportCoverage(ValidationReport report, List<string> missing)
    {
        if (missing.Count == 0)
        {
            report.Pass("the save covers every content kind the build supports");
            return;
        }

        foreach (string category in missing)
        {
            report.Fail("the save covers every content kind the build supports", category);
        }
    }

    /// <summary>
    /// 覆盖：每一类内容是否“可用”（构建有该类 Def，或反射找得到入口）以及是否“存在”（写出的存档中读得到对应的事实）。
    /// 可用而不存在的类别列入 <paramref name="missing"/>。
    /// </summary>
    private static JsonObject Coverage(Sweep sweep, SortedDictionary<string, string> facts, out List<string> missing)
    {
        MugirlTarget target = sweep.Target!;
        int Fact(string key) => facts.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        bool HasFact(Func<KeyValuePair<string, string>, bool> match) => facts.Any(match);
        bool PawnFact(string segment) => HasFact(p => p.Key.StartsWith("pawn/", StringComparison.Ordinal) && p.Key.Contains(segment, StringComparison.Ordinal));
        bool Component(string type, Func<string, string, bool> match)
            => HasFact(p => p.Key.StartsWith("component/", StringComparison.Ordinal) && p.Key.Contains("." + type, StringComparison.Ordinal) && match(p.Key, p.Value));

        var categories = new List<(string Name, bool Available, bool Present, string Detail)>
        {
            ("mugirls", true, HasFact(p => p.Key.EndsWith("/def", StringComparison.Ordinal) && p.Key.StartsWith("pawn/", StringComparison.Ordinal) && p.Value == GameFixtures.MugirlRaceDefName),
                "Mugirl pawns with facts"),
            ("items", target.Defs<ThingDef>().Any(d => d.category is ThingCategory.Item or ThingCategory.Building), HasFact(p => p.Key.StartsWith("thing/", StringComparison.Ordinal)), "mod things by def"),
            ("hediffs", target.Defs<HediffDef>().Count > 0, PawnFact("/hediff/"), "mod hediffs on pawns"),
            ("genes", ModsConfig.BiotechActive && target.Defs<GeneDef>().Count > 0, PawnFact("/gene/"), "mod genes"),
            ("xenotype", ModsConfig.BiotechActive && target.Defs<XenotypeDef>().Count > 0, PawnFact("/xenotype"), "mod xenotype"),
            ("abilities", target.Defs<AbilityDef>().Count > 0, PawnFact("/ability/"), "mod abilities"),
            ("traits", target.Defs<TraitDef>().Count > 0, PawnFact("/trait/"), "mod traits"),
            ("apparel", target.Defs<ThingDef>().Any(d => d.IsApparel && !IsRestraint(d)), PawnFact("/apparel/"), "mod apparel worn"),
            ("quests", target.Defs<QuestScriptDef>().Any(d => d.IsRootAny), HasFact(p => p.Key.StartsWith("quest/", StringComparison.Ordinal)), "quests with a mod root"),
            ("restraint-locked", Available(sweep, "restraint-locks"), Fact("restraints/locked") > 0, $"{Fact("restraints/locked")} locked"),
            ("restraint-unlocked", Available(sweep, "restraint-locks"), Fact("restraints/unlocked") > 0, $"{Fact("restraints/unlocked")} unlocked"),
            ("restraint-lock-count", Available(sweep, "restraint-locks"), Fact("restraints/customLockCount") > 0, $"{Fact("restraints/customLockCount")} with a non-default lock count"),
            ("restraint-timer", Available(sweep, "restraint-timers"), Fact("restraints/runningTimers") > 0, $"{Fact("restraints/runningTimers")} running collar timer(s)"),
            ("milk", Available(sweep, "milk"), PawnFact("/milk/fullness"), "milk fullness on pawns"),
            ("weapon-wheel", Available(sweep, "weapon-wheel"), HasFact(p => p.Key.EndsWith("/wheel/reserve", StringComparison.Ordinal)
                && int.TryParse(p.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 2),
                "at least two reserve weapons on one wheel"),
            ("roping", true, PawnFact("/roping/ropedTo"), "a rope link"),
            ("in-flight-job", true, HasFact(p => p.Key.EndsWith("/driver", StringComparison.Ordinal) && SaveXml.IsModClass(p.Value)), "a pawn running a mod JobDriver"),
            ("corporate-account", Available(sweep, "corporate"), Component("CorporateIntroduction", (k, v) => (k.EndsWith("/completed", StringComparison.Ordinal) || k.EndsWith("/contact/accountOpened", StringComparison.Ordinal)) && v == "True"), "corporate account opened"),
            ("corporate-records", Available(sweep, "corporate"), Component("CorporateNetwork", (k, v) => k.Contains("Record", StringComparison.OrdinalIgnoreCase) && k.EndsWith("/count", StringComparison.Ordinal)),
                "corporate records"),
            ("corporate-orders", Available(sweep, "corporate"), Component("CorporateNetwork", (k, v) => k.Contains("Order", StringComparison.OrdinalIgnoreCase) && k.EndsWith("/count", StringComparison.Ordinal)),
                "corporate orders"),
            ("corporate-missions", Available(sweep, "corporate"), Component("CorporateNetwork", (k, v) => k.Contains("Mission", StringComparison.OrdinalIgnoreCase) && k.EndsWith("/count", StringComparison.Ordinal)),
                "corporate missions"),
            ("lord", Available(sweep, "lord"), HasFact(p => p.Key.StartsWith("lord/", StringComparison.Ordinal)), "a mod LordJob in flight"),
        };

        var result = new JsonObject();
        missing = [];
        foreach ((string name, bool available, bool present, string detail) in categories)
        {
            result[name] = new JsonObject { ["available"] = available, ["present"] = present, ["detail"] = (available ? present ? "present: " : "MISSING: " : "not available in this build: ") + detail };
            if (available && !present)
            {
                missing.Add(name);
            }
        }

        return result;
    }

    private static bool Available(Sweep sweep, string feature) => sweep.Available.GetValueOrDefault(feature);

    /// <summary>
    /// 存档前重新系上绳索：原来的一对还能行动时沿用，否则（两天里死去或倒地）换两名能行动的雪牛娘殖民者。
    /// </summary>
    private static void ReRope(Sweep sweep, string what)
    {
        static bool Usable(Pawn? pawn) => pawn is { Spawned: true, Dead: false, Downed: false, roping: not null };
        if (!Usable(sweep.Roper) || !Usable(sweep.Ropee))
        {
            List<Pawn> able = [.. sweep.Colonists.Concat(sweep.Extra).Where(Usable)];
            // 压力播种可能使所有原角色倒地；追加真实的旧构建 Pawn 作为互动演员，不清除原角色的任何损伤或存档状态。
            PawnKindDef? kind = GameFixtures.MugirlColonistKind();
            for (int attempt = 0; able.Count < 2 && kind is not null && attempt < 4; attempt++)
            {
                Pawn replacement = GameFixtures.GenerateMugirlColonist(kind);
                Map map = Find.CurrentMap;
                GenSpawn.Spawn(replacement, GameFixtures.FreeCellNear(map, map.Center, 10), map);
                sweep.Extra.Add(replacement);
                sweep.Record("roping", "replacement interaction pawn " + replacement.ThingID, Usable(replacement), null);
                if (Usable(replacement))
                {
                    able.Add(replacement);
                }
            }

            if (able.Count < 2)
            {
                sweep.Record("roping", what, ok: false, "fewer than two Mugirl colonists can act");
                return;
            }

            sweep.Roper = able[0];
            sweep.Ropee = able[1];
        }

        Pawn roper = sweep.Roper!;
        Pawn ropee = sweep.Ropee!;
        if (ropee.roping.RopedByPawn != roper)
        {
            sweep.Try("roping", $"{what}: {roper.ThingID} ropes {ropee.ThingID}", () => Rope(roper, ropee));
        }
    }

    /// <summary>生成器可能留下尚未登记到世界的父母或伴侣；保留真实引用对象，避免夹具保存出悬空关系。</summary>
    private static void RetainRelatedPawns(ValidationReport report)
    {
        var queue = new Queue<Pawn>(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead);
        var seen = new HashSet<Pawn>();
        int retained = 0;
        while (queue.TryDequeue(out Pawn? pawn))
        {
            if (!seen.Add(pawn))
            {
                continue;
            }

            foreach (DirectPawnRelation relation in pawn.relations?.DirectRelations ?? [])
            {
                if (relation.otherPawn is not { } other)
                {
                    continue;
                }

                queue.Enqueue(other);
                if (!other.Spawned && other.ParentHolder is null && !Find.WorldPawns.Contains(other))
                {
                    if (other.Discarded)
                    {
                        report.Fail("related pawns remain saveable", other.ThingID + " was already discarded");
                        continue;
                    }

                    Find.WorldPawns.PassToWorld(other, PawnDiscardDecideMode.KeepForever);
                    retained++;
                }
            }
        }

        report.Info($"retained {retained} related pawn(s) in the world before saving; {seen.Count} pawn(s) inspected");
    }

    /// <summary>
    /// 存档前保证武器轮盘装着至少两件备用武器：装填的殖民者可能在两天里死去，换一名能行动的雪牛娘重新解锁并装填原版武器。
    /// </summary>
    private static void EnsureWheel(Map map, Sweep sweep)
    {
        if (sweep.Colonists.Concat(sweep.Extra).Any(p => p is { Dead: false } && WheelSlotsUsed(p) >= 2))
        {
            return;
        }

        if (sweep.Colonists.Concat(sweep.Extra).FirstOrDefault(p => p is { Spawned: true, Dead: false, Downed: false } && WheelOf(p) is not null) is not { } holder)
        {
            sweep.Record("wheel-reseed", "wheel", ok: false, "no living Mugirl colonist with a weapon wheel");
            return;
        }

        sweep.Try("wheel-reseed", holder.ThingID, () => UnlockWheel(holder));
        foreach (string defName in VanillaWheelWeapons)
        {
            if (WheelSlotsUsed(holder) >= WheelSlotsWanted || DefDatabase<ThingDef>.GetNamedSilentFail(defName) is not { } def)
            {
                continue;
            }

            sweep.Try("wheel-reseed", defName, () =>
            {
                var weapon = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                PlaceThing(map, weapon, holder.Position);
                return TryAttachToWheel(holder, weapon, out string wheel) ? $"wheel of {holder.ThingID}: {wheel}" : throw new InvalidOperationException(wheel);
            });
        }
    }

    private static string Rope(Pawn roper, Pawn ropee)
    {
        if (roper.roping is null || ropee.roping is null)
        {
            throw new InvalidOperationException("a pawn has no rope tracker");
        }

        roper.roping.RopePawn(ropee);
        // 旧构建的绳索服务在系绳工作结束时登记绳索；直接系上的绳索按名字补登记（没有该入口的构建由读档时的重建处理）。
        Type? service = ropee.def.modContentPack?.assemblies.loadedAssemblies
            .Select(a => a.GetType("Mugirl.Features.Roping.RopingService", throwOnError: false)).FirstOrDefault(t => t is not null);
        MethodInfo? register = service?.GetMethod("RegisterPawnRope", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Pawn), typeof(Pawn)]);
        register?.Invoke(null, [roper, ropee]);
        return ropee.roping.RopedByPawn == roper
            ? "roped" + (register is null ? " (no RopingService.RegisterPawnRope in this build)" : " and registered")
            : throw new InvalidOperationException("the rope did not hold");
    }

    // 武器轮盘的槽位需要解锁（开发者入口 UnlockSlotDeveloper，要求开发者模式；Test-InGame 打开了它）。
    private static string UnlockWheel(Pawn pawn)
    {
        ThingComp? wheel = WheelOf(pawn);
        MethodInfo? unlock = wheel is null ? null : LegacyApi.Method(wheel.GetType(), "UnlockSlotDeveloper", 1);
        if (wheel is null || unlock is null)
        {
            return "unavailable (no weapon wheel comp with UnlockSlotDeveloper in this build)";
        }

        int unlocked = 0;
        for (int slot = 1; slot < 8; slot++)
        {
            if (unlock.Invoke(wheel, [slot]) is true)
            {
                unlocked++;
            }
        }

        return unlocked.ToString(CultureInfo.InvariantCulture) + " slot(s) unlocked";
    }

    private static bool TryAttachToWheel(Pawn pawn, ThingWithComps weapon, out string outcome)
    {
        ThingComp? wheel = WheelOf(pawn);
        MethodInfo? attach = wheel is null ? null : LegacyApi.Method(wheel.GetType(), "TryAttachGroundWeapon", 3);
        if (wheel is null || attach is null)
        {
            outcome = "unavailable (no weapon wheel comp with TryAttachGroundWeapon in this build)";
            return false;
        }

        outcome = "every slot refused";
        for (int slot = 1; slot < 8; slot++)
        {
            object?[] arguments = [weapon, slot, null];
            if (attach.Invoke(wheel, arguments) is true)
            {
                outcome = "slot " + slot.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            outcome = arguments[2] as string ?? "refused";
        }

        return false;
    }

    private static int WheelSlotsUsed(Pawn pawn)
        => WheelOf(pawn) is { } wheel && LegacyApi.GetAny(wheel, "storage.slots", "slots") is System.Collections.IList slots
            ? slots.Cast<object?>().Skip(1).Count(slot => slot is ThingWithComps
                || (slot is not null && LegacyApi.Get(slot, "Weapon") is ThingWithComps)) : 0;

    private static ThingComp? WheelOf(Pawn pawn) => pawn.AllComps.FirstOrDefault(c => c.GetType().Name.Contains("WeaponWheel", StringComparison.Ordinal));

    // bool Try…(…, out string reason)：成功返回说明，失败抛出带原因的异常（记为该动作失败）。
    private static string CallWithReason(object target, Type? type, string method, object?[] arguments)
    {
        MethodInfo info = LegacyApi.Method(type, method, arguments.Length + 1) ?? throw new MissingMethodException(type?.Name, method);
        object?[] all = [.. arguments, null];
        return info.Invoke(target, all) is true ? method + " succeeded" : throw new InvalidOperationException($"{method}: {all[^1] as string ?? "refused"}");
    }

    private static string AcceptMission(object network, Type? networkType, object context)
    {
        if (LegacyApi.GetAny(network, "missionBoard.missions", "missions") is not System.Collections.IList missions)
        {
            throw new MissingFieldException(networkType?.Name, "missions");
        }

        MethodInfo accept = LegacyApi.Method(networkType, "AcceptMission", 3) ?? throw new MissingMethodException(networkType?.Name, "AcceptMission");
        string last = missions.Count == 0 ? "no mission offered" : "refused";
        foreach (object? mission in missions.Cast<object?>().ToList())
        {
            object?[] arguments = [mission, context, null];
            if (accept.Invoke(network, arguments) is true)
            {
                return $"accepted 1 of {missions.Count} mission(s)";
            }

            last = arguments[2] as string ?? last;
        }

        throw new InvalidOperationException(last);
    }

    /// <summary>新造的束缚服装中各事实的值（Scribe 写法），即没有写进存档时读档得到的值。</summary>
    private static Dictionary<string, string> RestraintValues(Thing made)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string current, string legacy) in new[]
        {
            (SaveFactQueries.IsLockedKey, "mugirlRestraintLocked", "isLocked"),
            (SaveFactQueries.LockCountKey, "mugirlRestraintLockCount", "lockCount"),
            (SaveFactQueries.IsCrackedKey, "mugirlRestraintCracked", "isCracked"),
        })
        {
            if (LegacyApi.ScribeText(LegacyApi.GetAny(made, current, legacy)) is { } text)
            {
                values[key] = text;
            }
        }

        if (made is ThingWithComps withComps && withComps.AllComps.FirstOrDefault(c => c.GetType().Name.Contains("ShockCollar", StringComparison.Ordinal)) is { } collar
            && LegacyApi.ScribeText(LegacyApi.GetAny(collar, "mugirlShockCheckTicks", "ticks")) is { } ticks)
        {
            values[SaveFactQueries.TimerKey] = ticks;
        }

        return values;
    }

    /// <summary>Def 的数据库根类型（向上找到直接继承 Def 的类型）：存档与升级清单都按它区分同名 Def。</summary>
    private static string DatabaseRoot(Def def)
    {
        Type type = def.GetType();
        while (type.BaseType is { } baseType && baseType != typeof(Def))
        {
            type = baseType;
        }

        return type.Name;
    }

    private static List<string> ModJobs(Sweep sweep)
    {
        var jobs = new List<string>();
        if (sweep.Target is not { } target || Find.CurrentMap is not { } map)
        {
            return jobs;
        }

        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
        {
            JobDef? def = pawn.CurJobDef;
            Type? driver = pawn.jobs?.curDriver?.GetType();
            if ((def is not null && target.Owns(def)) || (driver is not null && driver.Assembly == target.Assembly))
            {
                jobs.Add($"{pawn.ThingID}: {def?.defName ?? "?"} ({driver?.FullName ?? "?"})");
            }
        }

        return jobs;
    }

    private static int Alive(Sweep sweep) => sweep.Colonists.Concat(sweep.Extra).Count(p => p is { Dead: false });

    private static bool IsSweepable(ThingDef def)
        => !def.IsBlueprint && !def.IsFrame && !def.isUnfinishedThing && !def.IsCorpse && def.category is not (ThingCategory.Pawn or ThingCategory.Projectile
            or ThingCategory.Mote or ThingCategory.Ethereal or ThingCategory.Gas or ThingCategory.Attachment or ThingCategory.PsychicEmitter or ThingCategory.None)
            && !typeof(Skyfaller).IsAssignableFrom(def.thingClass) && !typeof(PawnFlyer).IsAssignableFrom(def.thingClass)
            && !typeof(MinifiedThing).IsAssignableFrom(def.thingClass);

    /// <summary>束缚服装：MugirlRace 时代的类是 Mugirl.Features.Restraints.RestraintApparel（及其子类），重构后为 Restraint*。</summary>
    private static bool IsRestraint(ThingDef def)
    {
        for (Type? type = def.thingClass; type is not null && type != typeof(Apparel); type = type.BaseType)
        {
            if (type.Name.Contains("SlaveApparel", StringComparison.Ordinal) || type.Name.Contains("Restraint", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsPart(HediffDef def)
        => def.injuryProps is not null || def.addedPartProps is not null
            || typeof(Hediff_MissingPart).IsAssignableFrom(def.hediffClass) || typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass)
            || typeof(Hediff_Implant).IsAssignableFrom(def.hediffClass);

    /// <summary>外层、非要害、有上级的部位（手指、脚趾、四肢）；没有时退回任意未缺失部位。</summary>
    private static BodyPartRecord? ChoosePart(Pawn pawn)
    {
        List<BodyPartRecord> parts = [.. pawn.health.hediffSet.GetNotMissingParts()];
        return parts.FirstOrDefault(p => p.depth == BodyPartDepth.Outside && p.parent is not null && !p.IsCorePart && !p.def.conceptual
                && p.def.tags.Count == 0)
            ?? parts.FirstOrDefault(p => p.parent is not null);
    }

    private static string PlaceItem(Map map, ThingDef def)
    {
        Thing item = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        item.stackCount = Math.Max(1, Math.Min(def.stackLimit, 5));
        return PlaceThing(map, item);
    }

    private static string PlaceThing(Map map, Thing thing, IntVec3? near = null)
        => GenPlace.TryPlaceThing(thing, near ?? map.Center, map, ThingPlaceMode.Near) ? thing.ThingID : throw new InvalidOperationException("TryPlaceThing failed");

    /// <summary>建筑区：地图西南角留出 8 格边距，避开快速测试地图中央的殖民者。</summary>
    private static IntVec3 BuildingOrigin(Map map) => new(8, 0, 8);

    /// <summary>在建筑区内按行排放建筑，相邻建筑之间留一格。</summary>
    private sealed class BuildingPlacer(Map map, IntVec3 origin, int size)
    {
        private int x;
        private int z;
        private int rowDepth;

        public IntVec3? Next(ThingDef def)
        {
            IntVec2 footprint = def.size;
            if (footprint.x > size || footprint.z > size)
            {
                return null;
            }

            if (x + footprint.x > size)
            {
                x = 0;
                z += rowDepth + 1;
                rowDepth = 0;
            }

            if (z + footprint.z > size)
            {
                return null;
            }

            // GenSpawn 以中心格放置：偶数尺寸的中心偏向西南，所以加上半个尺寸（向下取整）。
            IntVec3 cell = origin + new IntVec3(x + (footprint.x - 1) / 2, 0, z + (footprint.z - 1) / 2);
            x += footprint.x + 1;
            rowDepth = Math.Max(rowDepth, footprint.z);
            return cell.InBounds(map) ? cell : null;
        }
    }

    /// <summary>步骤之间共享的播种状态与结果。</summary>
    private sealed class Sweep
    {
        private readonly Dictionary<string, (int Ok, int Failed)> counts = new(StringComparer.Ordinal);

        public MugirlTarget? Target { get; set; }

        public List<string> DefNames { get; set; } = [];

        public List<string> ThingDefNames { get; set; } = [];

        /// <summary>Def 数据库根类型 → defName 列表（升级清单按根类型区分同名 Def）。</summary>
        public JsonObject DefsByRoot { get; } = new();

        /// <summary>束缚服装 Def → 各事实没有写出时的值。</summary>
        public Dictionary<string, Dictionary<string, string>> RestraintDefaults { get; } = new(StringComparer.Ordinal);

        /// <summary>构建支持的内容类别（覆盖检查的“可用”）。</summary>
        public Dictionary<string, bool> Available { get; } = new(StringComparer.Ordinal);

        public List<Pawn> Colonists { get; } = [];

        /// <summary>由其他 PawnKindDef 生成、属于玩家的雪牛娘。</summary>
        public List<Pawn> Extra { get; } = [];

        public List<Apparel> WornRestraints { get; } = [];

        public Pawn? Roper { get; set; }

        public Pawn? Ropee { get; set; }

        public JsonArray Actions { get; } = [];

        public JsonObject Result { get; } = new() { ["format"] = 2 };

        public void Record(string kind, string name, bool ok, string? detail)
        {
            Actions.Add(new JsonObject { ["kind"] = kind, ["name"] = name, ["ok"] = ok, ["detail"] = detail });
            (int good, int bad) = counts.GetValueOrDefault(kind);
            counts[kind] = ok ? (good + 1, bad) : (good, bad + 1);
        }

        /// <summary>执行一个播种动作；返回值是成功时的说明，异常记为该动作失败。</summary>
        public void Try(string kind, string name, Func<string> action)
        {
            try
            {
                Record(kind, name, ok: true, action());
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException { InnerException: { } wrapped } ? wrapped : ex;
                Record(kind, name, ok: false, inner.GetType().Name + ": " + inner.Message);
            }
        }

        public void ReportStep(ValidationReport report, params string[] kinds)
        {
            foreach (string kind in kinds)
            {
                (int ok, int failed) = counts.GetValueOrDefault(kind);
                report.Pass(kind, $"{ok} seeded, {failed} failed");
                foreach (JsonNode? action in Actions.Where(a => (string?)a?["kind"] == kind && (bool?)a?["ok"] == false).Take(5))
                {
                    report.Info($"{kind} {action?["name"]}: {action?["detail"]}");
                }
            }
        }

        public JsonObject Summary()
        {
            var summary = new JsonObject();
            foreach ((string kind, (int ok, int failed)) in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                summary[kind] = new JsonObject { ["ok"] = ok, ["failed"] = failed };
            }

            return summary;
        }
    }
}
