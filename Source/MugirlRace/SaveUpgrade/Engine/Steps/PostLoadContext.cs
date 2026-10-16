namespace Mugirl.SaveUpgrade;

/// <summary>读档后步骤的运行环境。</summary>
internal sealed class PostLoadContext(Game game, UpgradeTables tables, SessionData data, int sourceSchema)
{
    internal Game Game { get; } = game;

    internal UpgradeTables Tables { get; } = tables;

    /// <summary>DOM 步骤留下的标志与数据。</summary>
    internal SessionData Data { get; } = data;

    /// <summary>存档的来源结构版本。</summary>
    internal int SourceSchema { get; } = sourceSchema;
}
