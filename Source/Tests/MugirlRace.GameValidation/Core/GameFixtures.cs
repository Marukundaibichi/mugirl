namespace Mugirl.GameValidation.Core;

/// <summary>套件共用的前置条件：雪牛娘殖民者、空旷的测试区域、截图用的相机与界面状态。</summary>
/// <remarks>
/// 只用原版 API 与 defName，不引用被测 Mod 的类型，-Plumbing 运行也能加载本类。
/// 测试代码按名查找 Def 是有意的：不能在测试程序集里写 DefOf，未加载雪牛娘时 DefOf 初始化会报错。
/// </remarks>
internal static class GameFixtures
{
    /// <summary>雪牛娘种族的 ThingDef（XML 与存档引用的 defName，自 1.6 起不变）。</summary>
    public const string MugirlRaceDefName = "Mugirl";

    /// <summary>玩家殖民者用的雪牛娘 PawnKind；不存在时退回任一种族为雪牛娘的 PawnKind。</summary>
    public const string PreferredKindDefName = "Mugirl_Colony";

    /// <summary>雪牛娘殖民者的角色名前缀：mugirl-1、mugirl-2 …</summary>
    public const string MugirlRolePrefix = "mugirl-";

    /// <summary>截图时相机的视野大小（格）。原版缩放下限约 11，再小会被 CameraDriver 夹回。</summary>
    public const float CloseUpCameraSize = 12f;

    public static ThingDef? MugirlRace => DefDatabase<ThingDef>.GetNamedSilentFail(MugirlRaceDefName);

    public static PawnKindDef? MugirlColonistKind()
    {
        ThingDef? race = MugirlRace;
        if (race is null)
        {
            return null;
        }

        PawnKindDef? preferred = DefDatabase<PawnKindDef>.GetNamedSilentFail(PreferredKindDefName);
        if (preferred?.race == race)
        {
            return preferred;
        }

        return DefDatabase<PawnKindDef>.AllDefsListForReading
            .Where(kind => kind.race == race)
            .OrderBy(kind => kind.defName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string RoleName(int index) => MugirlRolePrefix + (index + 1);

    /// <summary>
    /// 保证当前地图上有 <paramref name="count"/> 个已登记的雪牛娘殖民者（角色 mugirl-1 …），缺少的就地生成。
    /// 已登记且仍然在图、存活的沿用，所以多个套件依次运行时使用同一批 Pawn。
    /// </summary>
    public static List<Pawn> EnsureMugirlColonists(ValidationContext context, int count)
    {
        PawnKindDef kind = MugirlColonistKind()
            ?? throw new InvalidOperationException("no PawnKindDef has race " + MugirlRaceDefName);
        Map map = context.Map;
        var pawns = new List<Pawn>(count);
        for (int i = 0; i < count; i++)
        {
            string role = RoleName(i);
            if (context.TryGetPawn(role, out Pawn? existing) && existing.Spawned && existing.Map == map && !existing.Dead)
            {
                pawns.Add(existing);
                continue;
            }

            Pawn pawn = GenerateMugirlColonist(kind);
            GenSpawn.Spawn(pawn, FreeCellNear(map, map.Center, 8), map);
            context.SetRole(role, pawn);
            context.Report.Info($"generated {pawn.LabelShort} ({pawn.ThingID}, kind {kind.defName}, age {pawn.ageTracker.AgeBiologicalYears}) as {role}");
            pawns.Add(pawn);
        }

        return pawns;
    }

    public static Pawn GenerateMugirlColonist(PawnKindDef kind)
    {
        var request = new PawnGenerationRequest(
            kind,
            Faction.OfPlayer,
            PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true,
            canGeneratePawnRelations: false,
            developmentalStages: DevelopmentalStage.Adult);
        return PawnGenerator.GeneratePawn(request);
    }

    /// <summary>站得住、无人无物的格子；<paramref name="near"/> 本身符合时返回它。</summary>
    public static IntVec3 FreeCellNear(Map map, IntVec3 near, int radius)
    {
        if (IsFree(map, near))
        {
            return near;
        }

        if (CellFinder.TryFindRandomCellNear(near, map, radius, cell => IsFree(map, cell), out IntVec3 found))
        {
            return found;
        }

        return CellFinder.RandomClosewalkCellNear(near, map, radius + 6);
    }

    /// <summary>
    /// 清出以 <paramref name="center"/> 为中心的方形区域：移除可摧毁的非 Pawn 物体、屋顶、积雪与迷雾，不可站立的地面换成混凝土。
    /// 截图因此只拍到被测 Pawn，不会被树木、岩石或屋顶遮住。
    /// </summary>
    public static int ClearArea(Map map, IntVec3 center, int radius)
    {
        int removed = 0;
        foreach (IntVec3 cell in CellRect.CenteredOn(center, radius).ClipInsideMap(map).Cells)
        {
            foreach (Thing thing in cell.GetThingList(map).ToList())
            {
                if (thing is Pawn || thing.Destroyed || !thing.def.destroyable)
                {
                    continue;
                }

                thing.Destroy(DestroyMode.Vanish);
                removed++;
            }

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain.passability == Traversability.Impassable || terrain.IsWater)
            {
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }

            map.roofGrid.SetRoof(cell, null);
            map.snowGrid.SetDepth(cell, 0f);
        }

        map.fogGrid.ClearAllFog();
        return removed;
    }

    /// <summary>瞬移已生成的 Pawn（与开发者工具的传送相同）。</summary>
    public static void Teleport(Pawn pawn, IntVec3 cell)
    {
        if (!pawn.Spawned)
        {
            return;
        }

        pawn.pather?.StopDead();
        pawn.Position = cell;
        pawn.Notify_Teleported();
    }

    /// <summary>
    /// 截图前的准备：清除迷雾，相机对准 <paramref name="cell"/>（quicktest 每次地图不同），打开截图模式隐藏界面。
    /// 截图模式不影响 UIRoot.HideMotes（E36）。用完调用 <see cref="EndScreenshots"/>。
    /// </summary>
    public static void FrameForScreenshot(Map map, IntVec3 cell, float cameraSize)
    {
        map.fogGrid.ClearAllFog();
        Find.CameraDriver.SetRootPosAndSize(cell.ToVector3Shifted(), cameraSize);
        Find.ScreenshotModeHandler.Active = true;
    }

    public static void EndScreenshots()
    {
        if (Find.UIRoot is not null)
        {
            Find.ScreenshotModeHandler.Active = false;
        }
    }

    private static bool IsFree(Map map, IntVec3 cell)
        => cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map)
            && cell.GetFirstPawn(map) is null && cell.GetFirstItem(map) is null && cell.GetEdifice(map) is null;
}
