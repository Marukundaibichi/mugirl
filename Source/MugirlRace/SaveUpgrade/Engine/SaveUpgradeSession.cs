using System.Diagnostics;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 一次读档的升级会话：识别 Mod 与版本戳，对旧档运行 DOM 流水线，记录读档后各阶段，最后给出结果。
/// 本类不读取 Verse 状态；游戏内由 <see cref="SaveUpgradeRuntime"/> 驱动，离线由 <see cref="SaveUpgradeOffline"/> 驱动。
/// </summary>
internal sealed class SaveUpgradeSession
{
    /// <summary>识别存档失败时的步骤 id。</summary>
    internal const string DetectStepId = "Framework.Detect";

    private SaveUpgradeSession(int id, SaveDocument? document, string? filePath)
    {
        Report = new UpgradeReport(id);
        Document = document;
        FilePath = filePath;
    }

    internal SaveDocument? Document { get; }

    /// <summary>正在读取的存档文件；不知道时为 null（不做备份）。</summary>
    internal string? FilePath { get; }

    internal UpgradeReport Report { get; }

    /// <summary>DOM 步骤留给读档后步骤的标志与数据。</summary>
    internal SessionData Data { get; } = new();

    internal LoadIdRemapper? LoadIds => Data.LoadIds;

    internal ValidationReadTrace? ReadTrace { get; set; }

    /// <summary>本存档适用的表格视图（按来源版本过滤）；DOM 流水线没有运行时为 null。</summary>
    internal UpgradeTables? Tables { get; private set; }

    /// <summary>存档有本 Mod 且版本戳低于当前版本：需要运行升级。</summary>
    internal bool IsLegacyRun { get; private set; }

    internal int SourceSchema => Report.SourceSchema;

    internal bool PhaseARan { get; private set; }

    internal bool PhaseBRan { get; private set; }

    internal bool Completed { get; private set; }

    /// <summary>玩家已收到本次读档的失败提示（每次读档最多一次）。</summary>
    internal bool FailureNotified { get; set; }

    private bool completionNoticeClaimed;

    /// <summary>只有已备份且获准写入当前戳的成功升级可领取一次信件；新档通过结构戳直接跳过。</summary>
    internal bool TryClaimCompletionNotice(bool stampVerified)
    {
        if (!stampVerified || !Verified || !IsLegacyRun || Report.State != UpgradeState.Upgraded
            || string.IsNullOrWhiteSpace(Report.Backup) || completionNoticeClaimed)
        {
            return false;
        }
        completionNoticeClaimed = true;
        return true;
    }

    /// <summary>升级是否完整结束（或无需升级）：只有这时版本戳才能写成当前版本。</summary>
    internal bool Verified => Completed && Report.State is UpgradeState.NotLegacy or UpgradeState.Upgraded or UpgradeState.MooGirlEra or UpgradeState.NoMugirl;

    /// <summary>识别 <paramref name="xml"/>：Mod 是否在列表中、版本戳与来源版本。</summary>
    internal static SaveUpgradeSession Start(int id, XmlDocument xml, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(xml);
        SaveDocument? document = SaveDocument.TryCreate(xml);
        var session = new SaveUpgradeSession(id, document, filePath);
        UpgradeReport report = session.Report;
        if (document is null)
        {
            report.Fail(DetectStepId, "The document is not a savegame with a <game> element.");
            return session;
        }

        report.GameVersion = document.GameVersion;
        if (!document.HasMugirl)
        {
            report.State = UpgradeState.NoMugirl;
            return session;
        }

        int? stamp = document.ReadStamp();
        if (stamp is not { } schema)
        {
            report.Fail(DetectStepId, "The " + SaveSchema.StampLabel + " stamp is not a non-negative integer.");
            return session;
        }

        report.SourceSchema = schema;
        if (schema > SaveSchema.Current)
        {
            report.State = UpgradeState.Newer;
        }
        else if (schema == SaveSchema.Current)
        {
            report.State = UpgradeState.NotLegacy;
        }
        else
        {
            report.State = UpgradeState.Upgraded;
            session.IsLegacyRun = true;
        }

        return session;
    }

    /// <summary>对旧档运行 DOM 流水线；只使用结构版本高于来源版本的行与步骤。</summary>
    internal void RunDom(UpgradeTables tables, IDefCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (!IsLegacyRun || Report.Failed)
        {
            return;
        }

        Tables = tables.ForSource(SourceSchema);
        var context = new UpgradeContext(Document!, Tables, catalog, Data, SourceSchema);
        UpgradePipeline.RunDom(context, Report);
        if (!Report.Failed)
        {
            try
            {
                Data.LoadIds = LoadIdStep.CreateRemapper(context);
            }
            catch (Exception exception)
            {
                Report.Fail(LoadIdStep.StepId, "Preparing reference aliases failed: " + exception);
            }
        }

        if (!Report.Failed && Data.HasFlag(MooGirlEraStep.DetectedFlag))
        {
            Report.State = UpgradeState.MooGirlEra;
        }
    }

    /// <summary>运行 <paramref name="phase"/> 阶段的模块步骤（只在旧档升级未失败时）；<paramref name="steps"/> 已按来源版本过滤。</summary>
    internal void RunPostLoad(UpgradePhase phase, IReadOnlyList<PostLoadUpgradeStep> steps, Func<PostLoadContext> context)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(context);
        MarkPhase(phase);
        if (!IsLegacyRun || Report.Failed)
        {
            return;
        }

        PostLoadContext? created = null;
        foreach (PostLoadUpgradeStep step in steps)
        {
            if (step.Phase != phase)
            {
                continue;
            }

            created ??= context();
            if (!RunAction(step.Id, phase, () => step.Run(created)))
            {
                return;
            }
        }
    }

    /// <summary>运行一个读档后动作并记入报告；抛出异常时记为失败并返回 false。</summary>
    internal bool RunAction(string id, UpgradePhase phase, Func<int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            int actions = action();
            Report.Add(new StepReport(id, phase, actions, watch.ElapsedMilliseconds));
            return true;
        }
        catch (Exception exception)
        {
            Report.Fail(id, exception.ToString());
            return false;
        }
    }

    internal void MarkPhase(UpgradePhase phase)
    {
        if (phase == UpgradePhase.A)
        {
            PhaseARan = true;
        }
        else if (phase == UpgradePhase.B)
        {
            PhaseBRan = true;
        }
    }

    /// <summary>阶段 C：确认各阶段都运行过，给出最终结果。</summary>
    internal void Complete()
    {
        if (Completed)
        {
            return;
        }

        if (IsLegacyRun && !Report.Failed)
        {
            if (!PhaseARan)
            {
                Report.Fail("Framework.PhaseA", "Phase A (ScribeLoader.FinalizeLoading postfix) did not run.");
            }
            else if (!PhaseBRan)
            {
                Report.Fail("Framework.PhaseB", "Phase B (Game.FinalizeInit prefix) did not run.");
            }
        }

        Completed = true;
    }
}
