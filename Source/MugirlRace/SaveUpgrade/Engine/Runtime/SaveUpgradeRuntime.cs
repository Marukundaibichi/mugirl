using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 游戏内驱动升级会话：读档钩子调用这里。会话只存在于 <c>Game.LoadGame</c> 前缀到终结器之间（同一个读档工作线程），
/// 所有阶段都以它为门控；任何异常都不能传出到 <c>SavedGameLoaderNow</c>（那会中止读档回到主菜单）。
/// </summary>
internal static class SaveUpgradeRuntime
{
    // StaticCacheLifecycle: 每次读档一个会话，由 Game.LoadGame 的终结器清除；文件名由 LoadGameFromSaveFileNow 前缀写入、
    // LoadGame 前缀取走。读档在长事件工作线程上运行，字段用 Volatile/Interlocked 访问。
    private static SaveUpgradeSession? activeSession;
    private static string? pendingFileName;
    private static int sessionCounter;

    /// <summary>当前读档的会话；不在读档中时为 null。</summary>
    internal static SaveUpgradeSession? ActiveSession => Volatile.Read(ref activeSession);

    /// <summary><c>SavedGameLoaderNow.LoadGameFromSaveFileNow</c> 前缀：记下正在读取的存档名（不含扩展名）。</summary>
    internal static void CaptureFileName(string? fileName) => Volatile.Write(ref pendingFileName, fileName);

    /// <summary><c>Game.LoadGame</c> 前缀：清除上一次读档的诊断，通知生命周期，识别存档、备份并运行 DOM 流水线。</summary>
    internal static void BeginLoad()
    {
        string? fileName = Interlocked.Exchange(ref pendingFileName, null);
        SaveUpgradeDiagnostics.Publish(null);
        GameLifecycle.Raise(GameLifecycleEvent.BeforeLoad);
        try
        {
            Start(fileName);
        }
        catch (Exception exception)
        {
            MugirlLog.Exception("Save upgrade failed before the save was read.", exception);
            if (ActiveSession is { } session)
            {
                Fail(session, "Framework.LoadGame", exception.ToString());
            }
            else
            {
                PublishWithoutSession("Framework.LoadGame", exception.ToString());
            }
        }
    }

    /// <summary><c>ScribeLoader.FinalizeLoading</c> 后缀（阶段 A）。</summary>
    internal static void RunPhaseA()
    {
        if (ActiveSession is { PhaseARan: false } session)
        {
            RunPhase(session, UpgradePhase.A);
        }
    }

    /// <summary>
    /// <c>Game.FinalizeInit</c> 前缀（阶段 B）：先让重置了状态图的 Lord 重新进入起始 LordToil、按默认策略结束旧版进行中的工作，
    /// 再运行模块步骤。
    /// </summary>
    internal static void RunPhaseB()
    {
        if (ActiveSession is not { PhaseBRan: false } session)
        {
            return;
        }

        if (session.IsLegacyRun && session.Tables is { } tables)
        {
            // 即使 DOM 升级中途失败也执行：已应用的 Lord 重置需要进入起始 LordToil，Toil 下标错位与升级是否完成无关。
            session.RunAction(LordResetPolicy.StepId, UpgradePhase.B, () => LordResetPolicy.Apply(session.Data.ResetLordIds));
            InFlightJobSelection selection = InFlightJobSelection.For(tables);
            session.RunAction(InFlightJobPolicy.StepId, UpgradePhase.B, () => InFlightJobPolicy.Apply(tables, selection));
        }

        RunPhase(session, UpgradePhase.B);
    }

    /// <summary>
    /// <c>Game.LoadGame</c> 后缀（阶段 C，在全部 <c>GameComponent.LoadedGame</c> 之后）：运行模块步骤，确定结果，交给游戏组件并记录摘要，
    /// 然后发出生命周期的 <see cref="GameLifecycleEvent.LoadedGame"/>（与 <see cref="GameLifecycleEvent.AddedToExistingGame"/>），
    /// 处理器看到的是本次读档的诊断。
    /// </summary>
    internal static void CompleteLoad()
    {
        bool addedToExistingGame = false;
        if (ActiveSession is { Completed: false } session)
        {
            RunPhase(session, UpgradePhase.C);
            try
            {
                session.Complete();
                UpgradeReport report = session.Report;
                string json = report.ToJson();
                SaveUpgradeDiagnostics.Publish(json);
                bool verified = session.Verified && SaveUpgradeInstallation.Status != InstallationStatus.Broken;
                Game? game = Current.Game;
                GameComponent_Mugirl? hub = game?.GetComponent<GameComponent_Mugirl>();
                hub?.AcceptLoad(
                    report.SourceSchema,
                    verified,
                    json,
                    session.IsLegacyRun && session.Verified ? Record(session) : null,
                    verified ? null : StampProblem(session));
                LogSummary(session);
                if (report.Failed)
                {
                    Notify(session);
                }
                else if (game is not null && hub is not null && session.TryClaimCompletionNotice(verified))
                {
                    SaveUpgradeNotices.UpgradeCompleted(game, report.Backup!);
                }

                addedToExistingGame = report.State == UpgradeState.NoMugirl;
            }
            catch (Exception exception)
            {
                MugirlLog.Exception("Save upgrade could not complete the load.", exception);
            }
        }

        GameLifecycle.Raise(GameLifecycleEvent.LoadedGame);
        if (addedToExistingGame)
        {
            GameLifecycle.Raise(GameLifecycleEvent.AddedToExistingGame);
        }
    }

    /// <summary><c>Game.LoadGame</c> 终结器：清除会话；读档中途抛出时也留下诊断报告。</summary>
    internal static void EndLoad()
    {
        Volatile.Write(ref pendingFileName, null);
        SaveUpgradeSession? session = Interlocked.Exchange(ref activeSession, null);
        if (session is { Completed: false })
        {
            SaveUpgradeDiagnostics.Publish(session.Report.ToJson());
        }
    }

    private static void Start(string? fileName)
    {
        if (Scribe.mode != LoadSaveMode.LoadingVars
            || Scribe.loader?.curXmlParent is not XmlElement { Name: "game" } game
            || game.OwnerDocument?.DocumentElement?.Name != "savegame")
        {
            const string Problem = "Game.LoadGame did not start at the <game> element of a savegame.";
            MugirlLog.Error("Save upgrade skipped: " + Problem);
            PublishWithoutSession(SaveUpgradeSession.DetectStepId, Problem);
            return;
        }

        string? path = fileName is null ? null : GenFilePaths.FilePathForSavedGame(fileName);
        SaveUpgradeSession session = SaveUpgradeSession.Start(Interlocked.Increment(ref sessionCounter), game.OwnerDocument, path);
        ValidationReadTrace.Begin(session);
        Volatile.Write(ref activeSession, session);
        UpgradeReport report = session.Report;
        if (report.Failed)
        {
            Fail(session, report.FailedStep!, report.Error!);
            return;
        }

        if (report.State == UpgradeState.Newer)
        {
            MugirlLog.Warning(report.Summary() + " The save was written by a newer Mugirl and is left untouched.");
            SaveUpgradeNotices.NewerSave(report.SourceSchema);
            return;
        }

        if (!session.IsLegacyRun)
        {
            return;
        }

        if (!UpgradeTables.TryGetDefault(out UpgradeTables? tables, out Exception? error))
        {
            Fail(session, "Framework.Tables", error.ToString());
            return;
        }

        if (!Backup(session))
        {
            return;
        }
        session.RunDom(tables, DefDatabaseCatalog.Instance);
        if (report.Failed)
        {
            Fail(session, report.FailedStep!, report.Error!);
            return;
        }

        MugirlLog.DevMessage(() => "Save upgrade DOM pass:\n" + report.Details());
        if (report.State == UpgradeState.MooGirlEra)
        {
            SaveUpgradeNotices.MooGirlEra();
        }
    }

    private static void RunPhase(SaveUpgradeSession session, UpgradePhase phase)
    {
        bool failedBefore = session.Report.Failed;
        try
        {
            if (session.Tables is { } tables)
            {
                session.RunPostLoad(phase, tables.PostLoadSteps, () => new PostLoadContext(Current.Game, tables, session.Data, session.SourceSchema));
            }
            else
            {
                session.MarkPhase(phase);
            }
        }
        catch (Exception exception)
        {
            session.Report.Fail("Framework.Phase" + phase, exception.ToString());
        }

        if (!failedBefore && session.Report.Failed)
        {
            Fail(session, session.Report.FailedStep!, session.Report.Error!);
        }
    }

    private static bool Backup(SaveUpgradeSession session)
    {
        if (session.FilePath is not { } path)
        {
            Fail(session, "Framework.Backup", "The save file name is unknown; upgrading without the required original backup is not safe.");
            return false;
        }

        try
        {
            session.Report.Backup = SaveBackup.Ensure(path, session.SourceSchema);
            if (session.Report.Backup is null)
            {
                Fail(session, "Framework.Backup", "The original save file was not found; no backup was made.");
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Fail(session, "Framework.Backup", "Could not back up the original save " + path + ": " + exception.Message);
            return false;
        }
    }

    private static void Fail(SaveUpgradeSession session, string stepId, string error)
    {
        session.Report.Fail(stepId, error);
        if (session.FailureNotified)
        {
            return;
        }

        session.FailureNotified = true;
        MugirlLog.Error("Save upgrade failed at " + stepId + ". Do not overwrite this save.\n" + error);
        SaveUpgradeNotices.UpgradeFailed(stepId);
    }

    // 读档在建立会话之前就无法继续：仍然发布一份失败报告，诊断不会停留在上一次读档。
    private static void PublishWithoutSession(string stepId, string error)
    {
        var report = new UpgradeReport(Interlocked.Increment(ref sessionCounter));
        report.Fail(stepId, error);
        SaveUpgradeDiagnostics.Publish(report.ToJson());
    }

    private static void Notify(SaveUpgradeSession session)
    {
        if (!session.FailureNotified)
        {
            Fail(session, session.Report.FailedStep!, session.Report.Error!);
        }
    }

    private static void LogSummary(SaveUpgradeSession session)
    {
        UpgradeReport report = session.Report;
        if (session.IsLegacyRun)
        {
            MugirlLog.Message(report.Summary());
            MugirlLog.DevMessage(() => "Save upgrade steps:\n" + report.Details());
        }
        else
        {
            MugirlLog.DevMessage(() => report.Summary());
        }
    }

    // 版本戳不能写成当前版本的原因（保存时由游戏组件记录一次）。
    private static UnverifiedStamp StampProblem(SaveUpgradeSession session)
    {
        UpgradeReport report = session.Report;
        if (report.State == UpgradeState.Newer)
        {
            return new UnverifiedStamp(
                "this save was written by a newer Mugirl (save schema " + report.SourceSchema + "); saving with this version keeps that stamp but loses the newer data.",
                IsError: false);
        }

        if (report.Failed)
        {
            return new UnverifiedStamp("the save upgrade failed at " + report.FailedStep + "; the next load upgrades this save again.", IsError: true);
        }

        return new UnverifiedStamp("the save upgrade hooks are not installed; the next load upgrades this save again.", IsError: true);
    }

    private static SaveUpgradeRecord Record(SaveUpgradeSession session) => new()
    {
        fromSchema = session.SourceSchema,
        toSchema = SaveSchema.Current,
        stepIds = [.. session.Report.Steps.Where(step => step.Actions > 0).Select(step => step.Id)],
        gameVersion = session.Report.GameVersion,
    };
}
