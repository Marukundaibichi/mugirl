namespace MugirlRace.Validation.Reporting;

/// <summary>一项检查的结论与一行摘要。</summary>
internal sealed record CheckResult(string Id, string Title, CheckStatus Status, string Summary);
