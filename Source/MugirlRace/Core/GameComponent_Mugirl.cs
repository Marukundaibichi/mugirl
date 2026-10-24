using Mugirl.SaveUpgrade;
using UpgradeSchema = Mugirl.SaveUpgrade.SaveSchema;

namespace Mugirl.Core;

/// <summary>
/// 本 Mod 的游戏级中心组件：写存档结构版本戳，保存最近一次升级记录。
/// 类名与版本戳标签写入存档，永久不改。
/// </summary>
/// <remarks>
/// <para>
/// 版本戳总是写出（<c>forceSave</c>），并且只在可信时写当前版本：新游戏，或本局读档的升级会话已完整结束
/// （包括存档本来就是当前版本、存档原本没有本 Mod）。否则写回读入时的版本并记录一次原因，下次读档会重新升级而不是把旧结构当成新结构。
/// </para>
/// <para>
/// 原版每次读档构造游戏组件 2–3 次（两次 <c>new Game()</c> 的 FillComponents，加上存档中的实例），构造函数必须没有副作用。
/// 加入旧存档时组件由 FillComponents 创建，本次读档不会调用 <see cref="ExposeData"/>，所以字段默认值表示“未知”而不是“当前”。
/// </para>
/// </remarks>
public sealed class GameComponent_Mugirl : GameComponent
{
    private int storedSchema;
    private SaveUpgradeRecord? lastUpgrade;
    private int sourceSchema;
    private bool schemaVerified;
    private string? lastUpgradeReportJson;
    private UnverifiedStamp? unverifiedStamp;
    private bool unverifiedStampLogged;

    public GameComponent_Mugirl(Game game)
    {
    }

    /// <summary>保存时将写入的版本戳（读档后为读入的版本，升级完成后为当前版本）。</summary>
    public int SaveSchema => schemaVerified ? UpgradeSchema.Current : storedSchema;

    /// <summary>读档时检测到的来源版本；新游戏为 -1。</summary>
    public int SourceSchema => sourceSchema;

    /// <summary>本局最近一次读档的升级报告 JSON；新游戏为 null。</summary>
    public string? LastUpgradeReportJson => lastUpgradeReportJson;

    /// <summary>存档中记录的最近一次升级；从未升级过时为 null。</summary>
    internal SaveUpgradeRecord? LastUpgrade => lastUpgrade;

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            int stamp = SaveSchema;
            if (!schemaVerified)
            {
                LogUnverifiedStamp(stamp);
            }

            Scribe_Values.Look(ref stamp, UpgradeSchema.StampLabel, 0, forceSave: true);
        }
        else
        {
            Scribe_Values.Look(ref storedSchema, UpgradeSchema.StampLabel, 0, forceSave: true);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                sourceSchema = storedSchema;
            }
        }

        Scribe_Deep.Look(ref lastUpgrade, "lastUpgrade");
    }

    public override void StartedNewGame()
    {
        sourceSchema = UpgradeSchema.NewGame;
        schemaVerified = true;
    }

    /// <summary>读档升级会话结束（阶段 C）时写入结果。</summary>
    /// <param name="verified">会话已完整结束且安装检查没有失败：之后保存写当前版本戳。</param>
    /// <param name="record">本次读档完成的升级；没有升级时为 null（保留存档中原有的记录）。</param>
    /// <param name="problem">不能写当前版本戳的原因；<paramref name="verified"/> 为 true 时为 null。</param>
    internal void AcceptLoad(int source, bool verified, string reportJson, SaveUpgradeRecord? record, UnverifiedStamp? problem)
    {
        sourceSchema = source;
        if (source >= 0)
        {
            // FillComponents 补建的实例可能没有读取自己的 ExposeData；失败或较新存档仍须写回检测到的来源戳。
            storedSchema = source;
        }
        schemaVerified = verified;
        lastUpgradeReportJson = reportJson;
        unverifiedStamp = verified ? null : problem;
        unverifiedStampLogged = false;
        if (record is not null)
        {
            lastUpgrade = record;
        }
    }

    // 每局记录一次：同一进程中的另一局游戏会再次记录自己的原因。
    private void LogUnverifiedStamp(int stamp)
    {
        if (unverifiedStampLogged)
        {
            return;
        }

        unverifiedStampLogged = true;
        UnverifiedStamp problem = unverifiedStamp
            ?? new UnverifiedStamp("this game was loaded without a save upgrade session; the next load upgrades this save again.", IsError: true);
        string message = "Saving schema stamp " + stamp + " instead of " + UpgradeSchema.Current + ": " + problem.Reason;
        if (problem.IsError)
        {
            MugirlLog.Error(message);
        }
        else
        {
            MugirlLog.Warning(message);
        }
    }
}
