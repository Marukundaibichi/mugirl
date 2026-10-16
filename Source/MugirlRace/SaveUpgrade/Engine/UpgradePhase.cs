namespace Mugirl.SaveUpgrade;

/// <summary>升级步骤运行的阶段。</summary>
internal enum UpgradePhase
{
    /// <summary>读档前改写 XML（<c>Game.LoadGame</c> 前缀，Scribe 读取任何数据之前）。</summary>
    Dom,

    /// <summary>引用解析与 PostLoadInit 之后、生成到地图之前（<c>ScribeLoader.FinalizeLoading</c> 后缀）。</summary>
    A,

    /// <summary>地图生成与 MapComponent.FinalizeInit 之后、GameComponent.FinalizeInit 之前（<c>Game.FinalizeInit</c> 前缀）。</summary>
    B,

    /// <summary>GameComponent.LoadedGame 之后（<c>Game.LoadGame</c> 后缀）。</summary>
    C,
}
