using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mugirl.SaveUpgrade;

/// <summary>一次读档的升级报告：状态、各步骤动作数与耗时、备份文件。写入日志、诊断 JSON 与存档中的升级记录。</summary>
internal sealed class UpgradeReport(int session)
{
    private readonly List<StepReport> steps = [];

    /// <summary>本进程内的读档序号。</summary>
    internal int Session { get; } = session;

    internal UpgradeState State { get; set; } = UpgradeState.NotLegacy;

    /// <summary>失败的步骤 id；未失败时为 null。</summary>
    internal string? FailedStep { get; private set; }

    /// <summary>失败原因（含异常）；未失败时为 null。</summary>
    internal string? Error { get; private set; }

    /// <summary>存档的来源结构版本；新游戏为 <see cref="SaveSchema.NewGame"/>。</summary>
    internal int SourceSchema { get; set; }

    /// <summary>升级前的原始存档备份路径；没有备份时为 null。</summary>
    internal string? Backup { get; set; }

    /// <summary>存档的游戏版本（诊断用）。</summary>
    internal string? GameVersion { get; set; }

    internal IReadOnlyList<StepReport> Steps => steps;

    internal long TotalMilliseconds => steps.Sum(step => step.Milliseconds);

    internal int TotalActions => steps.Sum(step => step.Actions);

    internal bool Failed => State == UpgradeState.Failed;

    internal void Add(StepReport step)
    {
        ArgumentNullException.ThrowIfNull(step);
        steps.Add(step);
    }

    /// <summary>记录失败；只保留第一个失败。</summary>
    internal void Fail(string stepId, string error)
    {
        if (Failed)
        {
            return;
        }

        State = UpgradeState.Failed;
        FailedStep = stepId;
        Error = error;
    }

    /// <summary>诊断 JSON：<c>{"session","state","failedStep","steps":[{"id","phase","actions","ms"}],"totalMs","backup"}</c>。</summary>
    internal string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("session", Session);
            writer.WriteString("state", State.ToString());
            writer.WriteString("failedStep", FailedStep);
            writer.WriteStartArray("steps");
            foreach (StepReport step in steps)
            {
                writer.WriteStartObject();
                writer.WriteString("id", step.Id);
                writer.WriteString("phase", PhaseText(step.Phase));
                writer.WriteNumber("actions", step.Actions);
                writer.WriteNumber("ms", step.Milliseconds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("totalMs", TotalMilliseconds);
            writer.WriteString("backup", Backup);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>一行摘要（写入日志）。</summary>
    internal string Summary()
    {
        var builder = new StringBuilder(160);
        builder.Append("Save upgrade ").Append(State)
            .Append(": schema ").Append(SourceSchema.ToString(CultureInfo.InvariantCulture))
            .Append(" -> ").Append(State is UpgradeState.Upgraded or UpgradeState.MooGirlEra ? SaveSchema.Current.ToString(CultureInfo.InvariantCulture) : "unchanged")
            .Append(", ").Append(steps.Count(step => step.Actions > 0).ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(steps.Count.ToString(CultureInfo.InvariantCulture)).Append(" steps changed ")
            .Append(TotalActions.ToString(CultureInfo.InvariantCulture)).Append(" items in ")
            .Append(TotalMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(" ms");
        if (GameVersion is not null)
        {
            builder.Append(", written by game ").Append(GameVersion);
        }

        if (Backup is not null)
        {
            builder.Append(", backup ").Append(Backup);
        }

        if (FailedStep is not null)
        {
            builder.Append(", failed at ").Append(FailedStep);
        }

        return builder.Append('.').ToString();
    }

    /// <summary>各步骤明细（开发者模式）。</summary>
    internal string Details() =>
        string.Join("\n", steps.Select(step => "  " + PhaseText(step.Phase) + " " + step.Id + ": " + step.Actions.ToString(CultureInfo.InvariantCulture)
            + " actions, " + step.Milliseconds.ToString(CultureInfo.InvariantCulture) + " ms"));

    internal static string PhaseText(UpgradePhase phase) => phase == UpgradePhase.Dom ? "dom" : phase.ToString();
}
