using System.Diagnostics.CodeAnalysis;
using System.IO;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>步骤共享的运行状态：参数、报告、被测 Mod、角色登记、本次运行捕获的日志与自己打开的窗口。</summary>
/// <remarks>
/// 只在主线程使用。角色按 thingIDNumber 登记，读档后仍能找到同一个 Pawn；读档会换掉全部游戏对象，
/// 所以步骤不能跨 <see cref="LoadWait"/> 持有 Thing 引用，读档后要用 <see cref="TryGetPawn"/> 重新取。
/// </remarks>
internal sealed class ValidationContext
{
    /// <summary><see cref="TickTarget"/> 的空值：没有等待中的 Tick，会话保持游戏暂停。</summary>
    public const int NoTickTarget = -1;

    /// <summary>测试模组自己的 Harmony ID（探针补丁）；运行结束时只撤销这个 ID 的补丁。</summary>
    public const string OwnHarmonyId = "MugirlRace.GameValidation";

    /// <summary>本测试写出的存档与截图的文件名前缀。</summary>
    public const string FilePrefix = "MugirlValidation-";

    private readonly Dictionary<string, int> roles = new(StringComparer.Ordinal);
    private readonly HashSet<Window> ownWindows = [];
    private readonly List<Func<CapturedLog, bool>> expectedLogs = [];
    private readonly HashSet<CapturedLog> reviewedLogs = new(ReferenceEqualityComparer.Instance);
    private Harmony? harmony;

    public ValidationContext(ValidationOptions options, ValidationReport report, MugirlTarget? target, bool headless)
    {
        Options = options;
        Report = report;
        Target = target;
        Headless = headless;
        LogPolicy = new LogPolicy(options.LogAllowlist, options.ForeignTokens);
    }

    public ValidationOptions Options { get; }

    public ValidationReport Report { get; }

    /// <summary>实际加载的被测构建（packageId 见 <see cref="ValidationOptions.TargetPackageId"/>）；-Plumbing 运行时为 null。</summary>
    public MugirlTarget? Target { get; }

    /// <summary>日志评判规则；套件可以加入被测 Mod 的记号。</summary>
    public LogPolicy LogPolicy { get; }

    /// <summary>宿主不渲染画面帧、不运行界面（-Headless，E48）；截图与界面步骤因此跳过。</summary>
    public bool Headless { get; }

    /// <summary>会话处理过的引擎帧数。</summary>
    public int Frame { get; internal set; }

    /// <summary>
    /// 推进 Tick 期间遇到会暂停游戏的对话树时，先选第一个可用选项作答再关闭（<see cref="DialogAnswers"/>）；
    /// 默认只关闭。无人值守的长时间运行（夹具播种）打开它，避免必须作答的任务对话每个 Tick 重新弹出。
    /// </summary>
    public bool AnswerDialogs { get; set; }

    /// <summary>大于当前 Tick 时会话以最高速推进游戏，到达后恢复暂停；由 <see cref="Wait.Ticks"/> 设置。</summary>
    public int TickTarget { get; set; } = NoTickTarget;

    /// <summary>当前地图。只在会话判定游戏可玩之后（步骤体内）调用。</summary>
    public Map Map => Find.CurrentMap ?? throw new InvalidOperationException("there is no current map");

    /// <summary>本次运行捕获的全部日志，按到达顺序。</summary>
    public List<CapturedLog> Logs { get; } = [];

    /// <summary>当前步骤开始时 <see cref="Logs"/> 的长度。</summary>
    public int StepLogStart { get; internal set; }

    /// <summary>当前步骤开始以来捕获的日志。</summary>
    public IEnumerable<CapturedLog> StepLogs => Logs.Skip(StepLogStart);

    /// <summary>探针补丁用的 Harmony 实例。</summary>
    public Harmony Harmony => harmony ??= new Harmony(OwnHarmonyId);

    /// <summary>运行中是否创建过探针 Harmony 实例（结束时据此撤销补丁）。</summary>
    public bool HasHarmony => harmony is not null;

    public MugirlTarget RequireTarget()
        => Target ?? throw new InvalidOperationException(Options.TargetPackageId + " is not loaded; the suite should have been skipped");

    /// <summary>
    /// 结果目录中文件 <paramref name="fileName"/> 的路径（脚本把 <see cref="ValidationOptions.ArtifactsDir"/> 复制到结果目录）；
    /// 参数没有给出目录时为 null。
    /// </summary>
    public string? ArtifactPath(string fileName)
    {
        if (Options.ArtifactsDir is not { Length: > 0 } folder)
        {
            return null;
        }

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }

    public static string SaveName(string name) => FilePrefix + name;

    public static string ScreenshotName(string name) => FilePrefix + name;

    public void SetRole(string role, Thing thing) => roles[role] = thing.thingIDNumber;

    /// <summary>登记为 <paramref name="role"/> 的 Pawn，在任何地图（含容器内）或世界 Pawn 中查找。</summary>
    public bool TryGetPawn(string role, [NotNullWhen(true)] out Pawn? pawn)
    {
        pawn = roles.TryGetValue(role, out int id) ? FindPawn(id) : null;
        return pawn is not null;
    }

    /// <summary>按 thingIDNumber 查找 Pawn。</summary>
    public static Pawn? FindPawn(int thingId)
    {
        foreach (Map map in Find.Maps)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawns)
            {
                if (pawn.thingIDNumber == thingId)
                {
                    return pawn;
                }
            }
        }

        return Find.WorldPawns.AllPawnsAliveOrDead.FirstOrDefault(p => p.thingIDNumber == thingId);
    }

    /// <summary>打开测试自己的窗口；会话推进 Tick 时不会关闭它。</summary>
    public void OpenWindow(Window window)
    {
        ownWindows.Add(window);
        Find.WindowStack.Add(window);
    }

    public void CloseWindow(Window window)
    {
        Find.WindowStack.TryRemove(window, doCloseSound: false);
        ownWindows.Remove(window);
    }

    public bool IsOwnWindow(Window window) => ownWindows.Contains(window);

    /// <summary>本步骤会故意产生的日志；匹配的条目不按问题报告。步骤结束时清空。</summary>
    public void ExpectLog(Func<CapturedLog, bool> match) => expectedLogs.Add(match);

    public bool IsExpected(CapturedLog entry) => expectedLogs.Any(match => match(entry));

    public void ClearExpectedLogs() => expectedLogs.Clear();

    /// <summary>标记已经由某个检查报告过的日志，运行结束的汇总不再重复报告。</summary>
    public void MarkReviewed(CapturedLog entry) => reviewedLogs.Add(entry);

    public bool IsReviewed(CapturedLog entry) => reviewedLogs.Contains(entry);
}
