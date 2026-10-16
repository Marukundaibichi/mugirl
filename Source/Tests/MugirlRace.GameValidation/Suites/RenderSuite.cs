using System.IO;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>四个朝向的雪牛娘截图：身体、头部、HAR 附件、服装与 Mod 的渲染节点在真实渲染器中各画一遍。</summary>
/// <remarks>
/// 截图在帧渲染后写出，由 <see cref="ScreenshotWait"/> 轮询；不用 ReadPixels（E36）。tools/Test-InGame.ps1 比较四张截图中央区域
/// 是否互不相同、是否有大片品红（引擎的回退 Shader），并把截图复制到 TMP/InGameValidation。
/// </remarks>
internal static class RenderSuite
{
    public const string Name = "render";

    /// <summary>截图文件名前缀；脚本按它识别本套件的截图。</summary>
    public const string ShotPrefix = "render-";

    private const int ClearRadius = 5;

    // StaticCacheLifecycle: 不可变常量表，与 Rot4.AsInt 对应。不用 Rot4.ToStringHuman()：它随游戏语言翻译，不能做文件名。
    private static readonly string[] DirectionNames = ["north", "east", "south", "west"];

    public static ValidationSuite Create() => new(
        Name,
        "screenshots of a Mugirl colonist facing north, east, south and west on a cleared, fog-free area",
        SuiteNeeds.MugirlMod | SuiteNeeds.RenderedFrames,
        [new ValidationStep("rotations", Rotations, TimeoutSeconds: 180)]);

    private static IEnumerable<Wait> Rotations(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        if (!ctx.Options.Screenshots)
        {
            report.Skip("screenshots", "screenshots are turned off");
            yield break;
        }

        Pawn pawn = GameFixtures.EnsureMugirlColonists(ctx, 1)[0];
        Map map = ctx.Map;
        IntVec3 cell = map.Center;
        int removed = GameFixtures.ClearArea(map, cell, ClearRadius);
        foreach (Pawn other in map.mapPawns.AllPawnsSpawned.Where(p => p != pawn && p.Position.InHorDistOf(cell, ClearRadius)).ToList())
        {
            GameFixtures.Teleport(other, GameFixtures.FreeCellNear(map, cell + new IntVec3(ClearRadius + 4, 0, 0), 6));
        }

        GameFixtures.Teleport(pawn, cell);
        pawn.jobs?.StopAll();
        report.Info($"framing {pawn.ThingID} at {cell} ({removed} things cleared)");
        int written = 0;
        try
        {
            GameFixtures.FrameForScreenshot(map, cell, GameFixtures.CloseUpCameraSize);
            yield return Wait.Frames(10);
            for (int index = 0; index < 4; index++)
            {
                // 游戏暂停时朝向不会被寻路或 Job 改回。
                var rotation = new Rot4(index);
                string direction = DirectionNames[index];
                pawn.Rotation = rotation;
                yield return Wait.Frames(3);
                report.Check(pawn.Rotation == rotation, $"{pawn.ThingID} faces {direction}", () => "rotation is " + pawn.Rotation.AsInt);
                var shot = new ScreenshotWait(ValidationContext.ScreenshotName(ShotPrefix + direction));
                yield return shot;
                long size = File.Exists(shot.FilePath) ? new FileInfo(shot.FilePath).Length : 0;
                if (report.Check(size > 1024, $"screenshot {direction} is written", () => $"{shot.FilePath}: {size} bytes"))
                {
                    written++;
                    report.Info("screenshot " + shot.FilePath);
                }
            }
        }
        finally
        {
            // 步骤中途被放弃时也要退出截图模式，否则之后的界面步骤画不出窗口。
            GameFixtures.EndScreenshots();
        }

        report.Info($"{written} of 4 screenshots written; the script compares their centres");
    }
}
