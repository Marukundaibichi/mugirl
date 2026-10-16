using System.Diagnostics;
using System.IO;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>步骤要等待的条件。步骤是产出 <see cref="Wait"/> 的迭代器，会话每帧轮询一次，条件满足后继续执行步骤。</summary>
/// <remarks>
/// 每个等待都有实时超时。超时由会话记为该步骤的失败（写明在等什么），然后照常继续执行步骤，
/// 步骤自己的检查再说明后果。游戏默认保持暂停，只有 <see cref="Ticks"/> 等待期间才推进时间。
/// </remarks>
internal abstract class Wait
{
    private readonly Stopwatch clock = new();

    protected Wait(double timeoutSeconds)
    {
        TimeoutSeconds = timeoutSeconds;
    }

    public double TimeoutSeconds { get; }

    public bool TimedOut { get; private set; }

    /// <summary>为 true 时，在读档长事件期间（没有可玩的游戏）也轮询。</summary>
    public virtual bool PollsWhileLoading => false;

    public abstract string Description { get; }

    public static Wait Frames(int frames) => new FrameWait(frames);

    public static Wait Until(Func<bool> condition, double timeoutSeconds, string description)
        => new ConditionWait(condition, timeoutSeconds, description);

    /// <summary>以最高速推进 <paramref name="ticks"/> 个游戏 Tick，然后恢复暂停。</summary>
    public static Wait Ticks(int ticks, double timeoutSeconds = 240) => new TickWait(ticks, timeoutSeconds);

    public void Start(ValidationContext context)
    {
        clock.Restart();
        OnStart(context);
    }

    /// <summary>条件满足或超时时返回 true。</summary>
    public bool Poll(ValidationContext context)
    {
        if (IsSatisfied(context))
        {
            return true;
        }

        if (clock.Elapsed.TotalSeconds >= TimeoutSeconds)
        {
            TimedOut = true;
            return true;
        }

        return false;
    }

    protected virtual void OnStart(ValidationContext context)
    {
    }

    protected abstract bool IsSatisfied(ValidationContext context);

    private sealed class FrameWait(int frames) : Wait(600)
    {
        private readonly int total = frames;
        private int remaining = frames;

        public override string Description => $"{total} frame(s)";

        protected override bool IsSatisfied(ValidationContext context) => remaining-- <= 0;
    }

    private sealed class ConditionWait(Func<bool> condition, double timeoutSeconds, string description) : Wait(timeoutSeconds)
    {
        public override string Description => description;

        protected override bool IsSatisfied(ValidationContext context) => condition();
    }

    private sealed class TickWait(int ticks, double timeoutSeconds) : Wait(timeoutSeconds)
    {
        private int target;
        private int started;
        private readonly Stopwatch progressClock = new();

        public override string Description => $"{ticks} game ticks (until tick {target}, now {Find.TickManager?.TicksGame})";

        protected override void OnStart(ValidationContext context)
        {
            started = Find.TickManager.TicksGame;
            target = started + ticks;
            context.TickTarget = target;
            progressClock.Restart();
        }

        protected override bool IsSatisfied(ValidationContext context)
        {
            int current = Find.TickManager.TicksGame;
            if (progressClock.Elapsed.TotalSeconds >= 30)
            {
                context.Report.Info($"tick progress: {current - started}/{ticks}, current {current}, target {target}");
                progressClock.Restart();
            }

            return current >= target;
        }
    }
}

/// <summary>同步存档，等文件写完。</summary>
internal sealed class SaveWait : Wait
{
    public SaveWait(string saveName)
        : base(120)
    {
        SaveName = saveName;
        FilePath = GenFilePaths.FilePathForSavedGame(saveName);
    }

    public string SaveName { get; }

    public string FilePath { get; }

    public override string Description => "the save " + FilePath;

    protected override void OnStart(ValidationContext context)
    {
        // 先删旧文件，等到的一定是这次写出的存档。
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        GameDataSaveLoader.SaveGame(SaveName);
    }

    protected override bool IsSatisfied(ValidationContext context)
        => !LongEventHandler.AnyEventNowOrWaiting && File.Exists(FilePath) && new FileInfo(FilePath).Length > 0;
}

/// <summary>在本进程中读回存档，等新游戏的第一帧。</summary>
/// <remarks>
/// GameDataSaveLoader.LoadGame 立即释放当前 Game 并排入读档长事件，之后不能再访问旧游戏的对象。
/// TickManager 不保存时间速度，读档后的游戏以正常速度开始，会在会话第一次看到它之前走几个 Tick（Prefs.PauseOnLoad 则固定先走一个）。
/// 所以给 Game.LoadGame 挂一个只属于测试的后缀，在读档长事件内就把新游戏暂停：读回的状态与存档时完全相同。
/// </remarks>
internal sealed class LoadWait : Wait
{
    // StaticCacheLifecycle: 进程级；后缀只需安装一次，随运行结束的 UnpatchAll 撤销。
    private static bool pausePatched;

    // StaticCacheLifecycle: 进程级反射句柄。直接写字段：屏幕淡入期间 CurTimeSpeed 的 setter 会忽略请求。
    private static readonly AccessTools.FieldRef<TickManager, TimeSpeed> CurTimeSpeedField =
        AccessTools.FieldRefAccess<TickManager, TimeSpeed>("curTimeSpeed");

    private Game? previous;

    public LoadWait(string saveName)
        : base(300)
    {
        SaveName = saveName;
    }

    public string SaveName { get; }

    public override bool PollsWhileLoading => true;

    public override string Description => "loading the save " + SaveName;

    /// <summary>安装只属于测试的 Game.LoadGame 后缀（只装一次）：之后读入的每个游戏在读档长事件内即被暂停。</summary>
    public static void EnsurePausedAfterLoad(ValidationContext context)
    {
        if (!pausePatched)
        {
            context.Harmony.Patch(
                AccessTools.Method(typeof(Game), nameof(Game.LoadGame)),
                postfix: new HarmonyMethod(typeof(LoadWait), nameof(PauseLoadedGame)));
            pausePatched = true;
        }
    }

    protected override void OnStart(ValidationContext context)
    {
        EnsurePausedAfterLoad(context);
        if (context.Options.Suites.Contains(SuiteCatalog.UpgradeSuiteName))
        {
            VanillaReadObservation.Begin(context, GenFilePaths.FilePathForSavedGame(SaveName));
            CorporatePriceObservation.Begin(context.Target?.Assembly, context.Harmony, GenFilePaths.FilePathForSavedGame(SaveName));
            UnconsumedQualityObservation.Begin(context.Target?.Assembly, context.Harmony, GenFilePaths.FilePathForSavedGame(SaveName));
            DuplicateForbiddableEvidence.Begin();
            CanonicalSavePreview.EnableReadTrace(context.Target?.Assembly);
        }
        previous = Current.Game;
        GameDataSaveLoader.LoadGame(SaveName);
    }

    private static void PauseLoadedGame(Game __instance)
    {
        if (__instance.tickManager is { } ticks)
        {
            CurTimeSpeedField(ticks) = TimeSpeed.Paused;
        }
    }

    protected override bool IsSatisfied(ValidationContext context)
        => Current.Game is { } game && !ReferenceEquals(game, previous) && Current.ProgramState == ProgramState.Playing
            && game.CurrentMap is not null && !LongEventHandler.AnyEventNowOrWaiting && game.PlayerHasControl;
}

/// <summary>截图并等文件写完。</summary>
/// <remarks>
/// ScreenshotTaker.TakeNonSteamShot 只登记请求，文件在帧渲染后才写出（E36），所以轮询：文件存在、长度连续两帧不变、可以独占打开。
/// 不用 ReadPixels 或 RenderTexture 读回：1.7 上它们同步执行且可能只写一个像素。
/// </remarks>
internal sealed class ScreenshotWait : Wait
{
    private long lastLength = -1;
    private int stableFrames;

    public ScreenshotWait(string name)
        : base(30)
    {
        Name = name;
        FilePath = Path.Combine(GenFilePaths.ScreenshotFolderPath, name + ".png");
    }

    public string Name { get; }

    public string FilePath { get; }

    public override string Description => "the screenshot " + FilePath;

    protected override void OnStart(ValidationContext context)
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        ScreenshotTaker.TakeNonSteamShot(Name);
    }

    protected override bool IsSatisfied(ValidationContext context)
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length == 0)
        {
            return false;
        }

        if (info.Length != lastLength)
        {
            lastLength = info.Length;
            stableFrames = 0;
            return false;
        }

        return ++stableFrames >= 2 && CanOpenExclusively(FilePath);
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
