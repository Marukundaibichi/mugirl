namespace Mugirl.Core;

/// <summary>每局游戏的生命周期事件；功能用 <see cref="GameLifecycleHookAttribute"/> 订阅，重置进程级的每局状态。</summary>
internal enum GameLifecycleEvent
{
    /// <summary>读档开始、任何存档数据被读取之前（存档升级的 DOM 改写之前）。上一局的状态不得泄漏进新读档的 ExposeData。</summary>
    BeforeLoad,

    /// <summary>新游戏开始生成之前（<c>Game.InitNewGame</c> 前缀）。</summary>
    BeforeNewGame,

    /// <summary>新游戏或读档的 <c>GameComponent.FinalizeInit</c>（地图已生成，读档升级的阶段 B 已完成）。</summary>
    FinalizeInit,

    /// <summary>剧情状态初始化中的瞬态重置点。保持原回调位置，各功能按显式顺序清空每局缓存。</summary>
    TransientStateReset,

    /// <summary>新游戏开始（<c>GameComponent.StartedNewGame</c>，在 FinalizeInit 之后）。</summary>
    StartedNewGame,

    /// <summary>
    /// 读档完成（<c>Game.LoadGame</c> 后缀，在 FinalizeInit、全部 <c>GameComponent.LoadedGame</c> 与存档升级阶段 C 之后）；
    /// 此时 <see cref="GameComponent_Mugirl"/> 已收到本次读档的升级结果。
    /// </summary>
    LoadedGame,

    /// <summary>读入的存档原本没有本 Mod（Mod 新加入已有存档），紧接在 <see cref="LoadedGame"/> 之后触发。</summary>
    AddedToExistingGame,
}
