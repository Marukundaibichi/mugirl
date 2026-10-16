using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Waylith;

namespace Mugirl.GameValidation.Core;

/// <summary>在真实游戏的主循环中逐帧运行选中的套件，结束后关闭游戏。</summary>
/// <remarks>
/// <para>
/// 驱动源是 <c>EngineApplication.Instance.OnUpdate</c>，不是 GameComponent：
/// 引擎每帧都在主线程调用它（宿主的游戏更新之后，E36），与程序状态无关，所以入口场景、读档长事件期间和读档后的新游戏
/// 都由同一个会话继续驱动；GameComponent 会被写进存档（按类名保存，测试模组不在时读档出错，1.6 的验证驱动出过这个问题），
/// 而且只在有 Game 时才运行，无法发现“开局失败”。
/// </para>
/// <para>
/// 每个步骤单独捕获异常并有实时上限，一处失败不影响其余步骤。游戏在步骤之间保持暂停，只有 <see cref="Wait.Ticks"/>
/// 等待期间以最高速推进。步骤结束时检查该步骤期间捕获的日志：提到被测 Mod 的警告与错误记为失败。
/// </para>
/// </remarks>
internal sealed class ValidationSession
{
    // DONE 之后再跑几帧，让报告行写进标准输出。
    private const int FramesBeforeShutdown = 10;

    // 每帧最多处理的步骤切换次数；等待会结束本帧。
    private const int MaxTransitionsPerFrame = 16;

    // 每个步骤最多逐条列出的“未归因错误”。
    private const int MaxUnattributedListed = 10;

    // 每个步骤中每种窗口最多逐条列出的处理记录（关闭或作答）；其余只计数，步骤结束时汇总。
    private const int MaxWindowEventsListed = 3;

    // 从加载完成到游戏可玩（quicktest 开局或从主菜单读档）的上限。
    private static readonly TimeSpan EntryLimit = TimeSpan.FromMinutes(5);

    /// <summary>从主菜单读档期间捕获的日志的范围名；<see cref="SuiteLogPolicy.AnyError"/> 套件的第一个步骤评判它们。</summary>
    public const string EntryScope = "entry";

    // StaticCacheLifecycle: 进程级；整次运行唯一的会话，读档换掉 Game 后仍由它驱动，结束后关闭游戏。
    private static ValidationSession? current;

    private readonly ValidationContext context;
    private readonly List<PlannedStep> plan;
    private readonly string planProblem;
    private readonly string targetProblem;
    private readonly Stopwatch runClock = Stopwatch.StartNew();
    private readonly Stopwatch entryClock = Stopwatch.StartNew();
    private readonly Stopwatch stepClock = new();
    private int stepIndex = -1;
    private string? currentSuite;
    private bool skipSuite;
    private IEnumerator<Wait>? running;
    private Wait? waiting;
    private int stepStartTick;
    private bool gameReady;
    private bool finished;
    private bool shutdownRequested;
    private int framesSinceDone;
    private Game? lastGame;
    private bool entryLoadRequested;
    private bool entryLogsReviewed;
    private readonly Dictionary<string, int> handledWindows = new(StringComparer.Ordinal);

    private ValidationSession(ValidationContext context, List<ValidationSuite> suites, string planProblem, string targetProblem)
    {
        this.context = context;
        this.planProblem = planProblem;
        this.targetProblem = targetProblem;
        plan = [];
        foreach (ValidationSuite suite in suites)
        {
            if (suite.Steps.Count == 0)
            {
                plan.Add(new PlannedStep(suite, null));
                continue;
            }

            foreach (ValidationStep step in suite.Steps)
            {
                plan.Add(new PlannedStep(suite, step));
            }
        }
    }

    /// <summary>读取参数并开始会话；由 <see cref="ValidationEntry"/> 在启用开关时调用一次。</summary>
    public static void Start()
    {
        if (current is not null)
        {
            return;
        }

        LogCapture.Install();
        ValidationOptions? options = ValidationOptions.TryLoad(out string problem);
        if (options is null)
        {
            // 没有参数文件就无法写报告文件；结果行仍写入标准输出，脚本据此报错。
            var fallback = new ValidationReport(null);
            fallback.Fail("options", problem);
            fallback.Done(TimeSpan.Zero);
            LogCapture.Uninstall();
            ShutdownAfterFrames();
            return;
        }

        var report = new ValidationReport(options.ReportPath);
        report.Info($"run {options.RunId}: {VersionControl.CurrentVersionStringWithRev}, {RuntimeInformation.FrameworkDescription}, {options}");
        bool headless = EngineApplication.Instance.Headless;
        MugirlTarget? target = MugirlTarget.Detect(options.TargetPackageId, out string targetProblem);
        var context = new ValidationContext(options, report, target, headless);
        SuiteCatalog.TryResolve(options.Suites, out List<ValidationSuite> suites, out string planProblem);
        current = new ValidationSession(context, suites, planProblem, targetProblem);
        EngineApplication.Instance.OnUpdate += current.OnUpdate;
        report.Info($"plan: {string.Join(", ", suites.Select(s => s.Name))}; {current.plan.Count} step(s); host {(headless ? "headless" : "rendering")}");
    }

    private static void ShutdownAfterFrames()
    {
        int frames = 0;
        Action? handler = null;
        handler = () =>
        {
            if (++frames == FramesBeforeShutdown)
            {
                EngineApplication.Instance.OnUpdate -= handler;
                Root.Shutdown();
            }
        };
        EngineApplication.Instance.OnUpdate += handler;
    }

    private ValidationReport Report => context.Report;

    private void OnUpdate()
    {
        if (shutdownRequested)
        {
            return;
        }

        try
        {
            if (finished)
            {
                if (++framesSinceDone >= FramesBeforeShutdown)
                {
                    shutdownRequested = true;
                    EngineApplication.Instance.OnUpdate -= OnUpdate;
                    Root.Shutdown();
                }

                return;
            }

            Update();
        }
        catch (Exception ex)
        {
            // 回调中的异常会回到引擎主循环；记录后结束运行，不再继续步骤。
            try
            {
                Report.Fail("session", "unexpected exception in the frame callback: " + ex);
                Finish();
            }
            catch (Exception inner)
            {
                Log.Error("[Mugirl.GameValidation] session could not finish: " + inner);
                finished = true;
            }
        }
    }

    private void Update()
    {
        context.Frame++;
        if (context.Headless)
        {
            HeadlessLongEvents.Emulate();
        }

        LogCapture.DrainInto(context.Logs);
        if (runClock.Elapsed.TotalSeconds > context.Options.TimeoutSeconds)
        {
            if (running is not null)
            {
                AbortStep($"the run exceeded its limit of {context.Options.TimeoutSeconds} s while waiting for {waiting?.Description ?? "the step"}");
            }
            else
            {
                Report.Fail("session", $"the run exceeded its limit of {context.Options.TimeoutSeconds} s");
            }

            Finish();
            return;
        }

        if (!gameReady && context.Options.Entry == EntryMode.LoadSave && !entryLoadRequested
            && Current.ProgramState == ProgramState.Entry && !LongEventHandler.AnyEventNowOrWaiting)
        {
            RequestEntryLoad();
            if (finished)
            {
                return;
            }
        }

        // 开局与读档后有一段屏幕淡入，期间 PlayerHasControl 为 false，TickManager 忽略改变速度的请求（游戏照常以正常速度运行）；
        // 等玩家能控制后再运行步骤，暂停才会生效。
        bool playable = Current.ProgramState == ProgramState.Playing && Find.CurrentMap is not null && !LongEventHandler.AnyEventNowOrWaiting
            && Current.Game is { PlayerHasControl: true };
        if (!gameReady)
        {
            if (playable)
            {
                gameReady = true;
                OnGameReady();
            }
            else if (entryClock.Elapsed > EntryLimit)
            {
                Report.BeginScope("setup", isStep: false);
                string what = context.Options.Entry == EntryMode.LoadSave
                    ? $"loading {context.Options.EntrySave} from the main menu did not finish"
                    : "the quick test did not start";
                Report.Fail("game", $"no playable game within {EntryLimit.TotalMinutes:0} min ({what}; see the errors above)");
                Finish();
                return;
            }
            else
            {
                return;
            }
        }

        if (playable)
        {
            if (!ReferenceEquals(Current.Game, lastGame) && Find.CurrentMap is { } map)
            {
                lastGame = Current.Game;
                Report.Info($"game ready: tick {Find.TickManager.TicksGame}, map {map.uniqueID} {map.Size}, {map.mapPawns.FreeColonistsSpawnedCount} free colonist(s)");
            }

            ControlTime();
        }

        Advance(playable);
    }

    /// <summary>
    /// 主菜单就绪后读取 <see cref="ValidationOptions.EntrySave"/>（与读档对话框相同的 GameDataSaveLoader.LoadGame，不经过版本检查对话框）。
    /// 读入的游戏在 Game.LoadGame 结束时即被暂停（<see cref="LoadWait.EnsurePausedAfterLoad"/>），套件看到的状态与存档相同；
    /// 读档期间的日志标为范围 <see cref="EntryScope"/>。
    /// </summary>
    private void RequestEntryLoad()
    {
        entryLoadRequested = true;
        entryClock.Restart();
        string name = context.Options.EntrySave ?? string.Empty;
        string path = GenFilePaths.FilePathForSavedGame(name);
        Report.BeginScope(EntryScope, isStep: false);
        if (!File.Exists(path))
        {
            Report.Fail("entry", $"the save to load does not exist: {path}");
            Finish();
            return;
        }

        LoadWait.EnsurePausedAfterLoad(context);
        if (context.Options.Suites.Any(s => string.Equals(s, Mugirl.GameValidation.Suites.ReferenceResaveSuite.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Mugirl.GameValidation.Suites.ReferenceResaveSuite.PrepareEntry(context, path);
        }
        if (context.Options.Suites.Contains(SuiteCatalog.UpgradeSuiteName))
        {
            VanillaReadObservation.Begin(context, path);
            CorporatePriceObservation.Begin(context.Target?.Assembly, context.Harmony, path);
            UnconsumedQualityObservation.Begin(context.Target?.Assembly, context.Harmony, path);
            DuplicateForbiddableEvidence.Begin();
            Report.Check(CanonicalSavePreview.EnableReadTrace(context.Target?.Assembly), "precise Scribe read tracing is enabled before loading",
                () => "the loaded build does not expose ValidationReadTrace.EnableForValidation");
        }
        Report.Info($"loading {name} from the main menu ({new FileInfo(path).Length / 1024} KiB)");
        GameDataSaveLoader.LoadGame(name);
    }

    private void OnGameReady()
    {
        // 读档期间的日志保留范围 "entry"；之后的会话日志属于 setup。
        Report.BeginScope("setup", isStep: false);
        AddVerseLoadMessages();
        if (planProblem.Length > 0)
        {
            Report.Fail("plan", planProblem);
        }

        if (targetProblem.Length > 0)
        {
            Report.Fail("target", targetProblem);
        }

        if (context.Headless && !HeadlessLongEvents.Available)
        {
            Report.Info("LongEventHandler.currentEvent was not found: synchronous long events (loading a save) may hang on the headless host");
        }

        Report.Info("mods: " + string.Join(", ", LoadedModManager.RunningModsListForReading.Select(m => m.PackageIdPlayerFacing)));
        Report.Info(context.Target is null ? context.Options.TargetPackageId + " is not loaded" : "target: " + context.Target);
        if (context.ArtifactPath("think-trees.json") is { } thinkTreePath)
        {
            var snapshot = ThinkTreeSnapshot.Capture(DefDatabase<ThinkTreeDef>.AllDefsListForReading, context.Target?.Assembly);
            ArtifactJson.Write(thinkTreePath, snapshot);
            Report.Check(snapshot["errors"]!.AsArray().Count == 0, "resolved ThinkTree snapshot captured without graph errors",
                () => snapshot["errors"]!.ToJsonString());
        }
    }

    /// <summary>
    /// 把测试模组订阅 LogStream 之前的 Verse 警告与错误补进日志列表（场景为 "load"）。
    /// 已经由 LogStream 收到的同文消息不重复加入。
    /// </summary>
    private void AddVerseLoadMessages()
    {
        var seen = new HashSet<string>(context.Logs.Where(l => l.Category == "Verse").Select(l => l.Message), StringComparer.Ordinal);
        var earlier = new List<CapturedLog>();
        foreach (LogMessage message in Log.Messages)
        {
            if (message.type == LogMessageType.Message || message.text is null || seen.Contains(message.text))
            {
                continue;
            }

            LogLevel level = message.type == LogMessageType.Warning ? LogLevel.Warning : LogLevel.Error;
            earlier.Add(new CapturedLog(level, "Verse", message.text, message.StackTrace, "load", DuringStep: false));
        }

        context.Logs.InsertRange(0, earlier);
    }

    /// <summary>
    /// 步骤之间保持暂停；有 Tick 目标时以最高速推进，并处理会暂停游戏的其他窗口（自动打开的对话框等）：
    /// <see cref="ValidationContext.AnswerDialogs"/> 打开时先对对话树作答（<see cref="DialogAnswers"/>），仍然打开的窗口关闭。
    /// </summary>
    private void ControlTime()
    {
        TickManager ticks = Find.TickManager;
        if (context.TickTarget > ticks.TicksGame)
        {
            if (ticks.CurTimeSpeed != TimeSpeed.Ultrafast)
            {
                ticks.CurTimeSpeed = TimeSpeed.Ultrafast;
            }

            if (Find.WindowStack.WindowsForcePause)
            {
                foreach (Window window in Find.WindowStack.Windows.ToList())
                {
                    if (window.forcePause && !context.IsOwnWindow(window))
                    {
                        HandlePausingWindow(window);
                    }
                }
            }
        }
        else if (ticks.CurTimeSpeed != TimeSpeed.Paused)
        {
            ticks.CurTimeSpeed = TimeSpeed.Paused;
        }
    }

    private void HandlePausingWindow(Window window)
    {
        string type = window.GetType().FullName ?? window.GetType().Name;
        string what = "closed";
        if (context.AnswerDialogs && window is Dialog_NodeTree dialog && DialogAnswers.TryAnswer(dialog, out string choices))
        {
            what = $"answered \"{choices}\"";
        }

        if (Find.WindowStack.IsOpen(window))
        {
            Find.WindowStack.TryRemove(window, doCloseSound: false);
        }

        int count = handledWindows.GetValueOrDefault(type) + 1;
        handledWindows[type] = count;
        if (count <= MaxWindowEventsListed)
        {
            Report.Info($"{type} paused the game: {what}");
        }
    }

    private void ReportHandledWindows()
    {
        foreach ((string type, int count) in handledWindows.Where(p => p.Value > MaxWindowEventsListed).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Report.Info($"{count - MaxWindowEventsListed} more {type} window(s) paused the game and were handled ({count} in this step)");
        }

        handledWindows.Clear();
    }

    private void Advance(bool playable)
    {
        for (int transition = 0; transition < MaxTransitionsPerFrame; transition++)
        {
            if (waiting is not null)
            {
                if (!playable && !waiting.PollsWhileLoading)
                {
                    CheckStepLimit();
                    return;
                }

                bool over;
                try
                {
                    over = waiting.Poll(context);
                }
                catch (Exception ex)
                {
                    AbortStep($"waiting for {waiting.Description} threw: {ex}");
                    continue;
                }

                if (!over)
                {
                    CheckStepLimit();
                    return;
                }

                if (waiting.TimedOut)
                {
                    Report.Fail("wait", $"gave up after {waiting.TimeoutSeconds:0} s waiting for {waiting.Description}");
                }

                waiting = null;
                context.TickTarget = ValidationContext.NoTickTarget;
            }

            // 步骤体只在游戏可玩时运行；读档期间只轮询声明了 PollsWhileLoading 的等待。
            if (!playable)
            {
                return;
            }

            if (running is null && !StartNextStep())
            {
                Finish();
                return;
            }

            bool hasNext;
            try
            {
                hasNext = running!.MoveNext();
            }
            catch (Exception ex)
            {
                AbortStep(ex.ToString());
                continue;
            }

            if (!hasNext)
            {
                EndStep();
                continue;
            }

            Wait next = running.Current ?? Wait.Frames(1);
            try
            {
                next.Start(context);
            }
            catch (Exception ex)
            {
                AbortStep($"starting the wait for {next.Description} threw: {ex}");
                continue;
            }

            waiting = next;
            return;
        }
    }

    private bool StartNextStep()
    {
        while (++stepIndex < plan.Count)
        {
            PlannedStep next = plan[stepIndex];
            if (!string.Equals(next.Suite.Name, currentSuite, StringComparison.Ordinal))
            {
                BeginSuite(next.Suite);
            }

            if (skipSuite || next.Step is null)
            {
                continue;
            }

            Report.BeginScope(next.Suite.Name + "/" + next.Step.Name, isStep: true);
            context.StepLogStart = context.Logs.Count;
            stepStartTick = CurrentTick();
            stepClock.Restart();
            try
            {
                running = next.Step.Body(context).GetEnumerator();
                return true;
            }
            catch (Exception ex)
            {
                Report.Fail("step", ex.ToString());
                EndStep();
            }
        }

        return false;
    }

    private void BeginSuite(ValidationSuite suite)
    {
        currentSuite = suite.Name;
        skipSuite = true;
        Report.BeginScope(suite.Name, isStep: false);
        if (suite.SkipReason is { } reason)
        {
            Report.Skip("suite", reason);
            return;
        }

        if ((suite.Needs & SuiteNeeds.MugirlMod) != 0 && context.Target is null)
        {
            // 正式运行时脚本写入了期望的 Mod 目录；这时 Mod 没有加载是失败，-Plumbing 运行才是跳过。
            if (context.Options.ExpectedModRoot is { } root)
            {
                Report.Fail("suite", $"{context.Options.TargetPackageId} was expected from {root} but is not loaded");
            }
            else
            {
                Report.Skip("suite", $"{context.Options.TargetPackageId} is not loaded (plumbing run)");
            }

            return;
        }

        if ((suite.Needs & SuiteNeeds.RenderedFrames) != 0 && context.Headless)
        {
            Report.Skip("suite", "the headless host renders no frames and runs no UI; run with -NoWindow");
            return;
        }

        skipSuite = false;
        Report.Info("begin: " + suite.Description);
    }

    private void EndStep()
    {
        DisposeRunning();
        LogCapture.DrainInto(context.Logs);
        SuiteLogPolicy policy = stepIndex >= 0 && stepIndex < plan.Count ? plan[stepIndex].Suite.LogPolicy : SuiteLogPolicy.ModMentions;
        if (policy == SuiteLogPolicy.AnyError && !entryLogsReviewed)
        {
            // 从主菜单读档期间的日志由第一个“任何错误都失败”的套件评判（存档升级套件），归入它的第一个步骤。
            entryLogsReviewed = true;
            ReviewLogs(context.Logs.Where(l => l.Scope == EntryScope), honourExpectations: true, policy);
        }

        ReviewLogs(context.Logs.Skip(context.StepLogStart), honourExpectations: true, policy);
        ReportHandledWindows();
        context.ClearExpectedLogs();
        context.TickTarget = ValidationContext.NoTickTarget;
        string timing = $"{stepClock.Elapsed.TotalSeconds:0.0} s, {Math.Max(0, CurrentTick() - stepStartTick)} ticks";
        if (Report.ScopeFailures == 0)
        {
            Report.Pass("step", timing);
        }
        else
        {
            Report.Info($"step finished with {Report.ScopeFailures} failure(s) after {timing}");
        }

        Report.BeginScope(currentSuite ?? "session", isStep: false);
    }

    private void AbortStep(string detail)
    {
        Report.Fail("step", detail);
        EndStep();
    }

    private void CheckStepLimit()
    {
        if (running is null || stepIndex < 0 || stepIndex >= plan.Count || plan[stepIndex].Step is not { } step)
        {
            return;
        }

        if (stepClock.Elapsed.TotalSeconds > step.TimeoutSeconds)
        {
            AbortStep($"abandoned after {stepClock.Elapsed.TotalSeconds:0} s (limit {step.TimeoutSeconds:0} s) while waiting for {waiting?.Description ?? "the step"}");
        }
    }

    private void DisposeRunning()
    {
        try
        {
            running?.Dispose();
        }
        catch (Exception ex)
        {
            Report.Fail("step cleanup", ex.ToString());
        }

        running = null;
        waiting = null;
    }

    /// <summary>
    /// 按 <paramref name="policy"/> 评判尚未检查过的日志（<see cref="LogPolicy.Classify"/>）：失败逐条报告；
    /// 未归因、foreign 与白名单命中只列出或计数，不算失败。
    /// </summary>
    private void ReviewLogs(IEnumerable<CapturedLog> entries, bool honourExpectations, SuiteLogPolicy policy)
    {
        int unattributed = 0;
        int allowed = 0;
        foreach (CapturedLog entry in entries.ToList())
        {
            if (context.IsReviewed(entry))
            {
                continue;
            }

            context.MarkReviewed(entry);
            if (honourExpectations && context.IsExpected(entry))
            {
                continue;
            }

            switch (context.LogPolicy.Classify(entry, policy, out string reason))
            {
                case LogVerdict.Failure:
                    Report.Fail("log", policy == SuiteLogPolicy.ModMentions ? entry.Describe() : $"{entry.Describe()} [{reason}]");
                    break;
                case LogVerdict.Foreign:
                    Report.Info($"foreign error ({reason}): {entry.Describe()}");
                    break;
                case LogVerdict.Allowed:
                    allowed++;
                    break;
                case LogVerdict.Unattributed:
                    if (++unattributed <= MaxUnattributedListed)
                    {
                        Report.Info("error not attributed to the mod: " + entry.Describe());
                    }

                    break;
                case LogVerdict.Ignored:
                default:
                    break;
            }
        }

        if (unattributed > MaxUnattributedListed)
        {
            Report.Info($"{unattributed - MaxUnattributedListed} more error(s) not attributed to the mod");
        }

        if (allowed > 0)
        {
            Report.Info($"{allowed} error(s) matched the reviewed allowlist");
        }
    }

    private void Finish()
    {
        if (finished)
        {
            return;
        }

        finished = true;
        DisposeRunning();
        LogCapture.DrainInto(context.Logs);
        Report.BeginScope("summary", isStep: false);
        ReviewLogs(context.Logs, honourExpectations: false, SuiteLogPolicy.ModMentions);
        try
        {
            GameFixtures.EndScreenshots();
        }
        catch (Exception ex)
        {
            Report.Info("could not leave screenshot mode: " + ex.Message);
        }

        if (context.HasHarmony)
        {
            context.Harmony.UnpatchAll(ValidationContext.OwnHarmonyId);
        }
        VanillaReadObservation.Stop();
        CorporatePriceObservation.Stop();
        UnconsumedQualityObservation.Stop();

        Report.Info($"{context.Frame} frames, {context.Logs.Count} log entries captured, test assembly {Assembly.GetExecutingAssembly().Location}");
        Report.Done(runClock.Elapsed);
        LogCapture.Uninstall();
    }

    private static int CurrentTick() => Current.Game?.tickManager?.TicksGame ?? 0;

    /// <summary>计划中的一项：套件的一个步骤；尚未实现的套件没有步骤，<see cref="Step"/> 为 null。</summary>
    private sealed record PlannedStep(ValidationSuite Suite, ValidationStep? Step);
}
