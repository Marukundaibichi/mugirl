namespace Mugirl.Control.Upgrade;

// C.save-upgrade 负对照的新构建一侧：清单（SaveUpgrade/ControlSaveUpgrade.cs）与控制语料
// （ModRoot/Source/Tests/MugirlRace.Validation/Baselines/legacy）引用的类型。

/// <summary>正确的改名目标（有 public (Game) 构造函数）。</summary>
public class NewComponent : GameComponent
{
    public NewComponent(Game game)
    {
    }
}

/// <summary>改名目标的存档根错误：旧类型是 GameComponent，它是 MapComponent。</summary>
public class WrongRootTarget : MapComponent
{
    public WrongRootTarget(Map map)
        : base(map)
    {
    }
}

/// <summary>改名目标缺少游戏使用的 (Game) 构造函数。</summary>
public class NoCtorTarget : GameComponent
{
}

/// <summary>名字复用：这个名字同时是另一个旧类型的名字。</summary>
public class LegacyOther : GameComponent
{
    public LegacyOther(Game game)
    {
    }
}

/// <summary>存档枚举：旧构建还有成员 Paused。</summary>
public enum ControlState
{
    Idle,
    Running,
}

/// <summary>
/// 标签 owner：kept 不变；oldName 改名到它从不读写的 neverRead；stillRead 已改名却仍在读；drift 的默认值从 5 改成 7；
/// kindChanged 从 Deep 改成 References。
/// </summary>
public class LabelOwner : IExposable
{
    private int kept;
    private int stillRead;
    private int drift = 7;
    private int drift2 = 9;
    private int forced = 2;
    private ControlState state;
    private Pawn other;

    public void ExposeData()
    {
        Scribe_Values.Look(ref kept, "kept");
        Scribe_Values.Look(ref stillRead, "stillRead");
        Scribe_Values.Look(ref drift, "drift", 7);
        Scribe_Values.Look(ref drift2, "drift2", 9);
        Scribe_Values.Look(ref forced, "forced", 2);
        Scribe_Values.Look(ref state, "state");
        Scribe_References.Look(ref other, "kindChanged");
    }
}

/// <summary>设置类改了完整名，清单却没有 settings.fileClass 映射。</summary>
public class ControlSettings : ModSettings
{
    private float volume = 1f;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref volume, "volume", 1f);
    }
}

/// <summary>旧 OldOwner 的新名（正对照）：legacyCount 改名为 count，legacyFlag 由步骤消费，slots 不变。</summary>
public class RenamedOwner : IExposable
{
    private int count;
    private List<Slot> slots = [];

    public void ExposeData()
    {
        Scribe_Values.Look(ref count, "count");
        Scribe_Collections.Look(ref slots, "slots", LookMode.Deep);
    }
}

/// <summary>经存档图到达的嵌套对象（没有 Class）：oldSlotLabel 改名为 slotLabel（正对照）。</summary>
public class Slot : IExposable
{
    private string slotLabel;

    public void ExposeData() => Scribe_Values.Look(ref slotLabel, "slotLabel");
}

/// <summary>旧 OldMode 的新名：成员 Legacy 改名为 Current（正对照）。</summary>
public enum Mode
{
    Current,
}

/// <summary>独立文档（剧本文件）中的类型：改名需要 GetBackCompatibleType 后缀，负对照构建没有它。</summary>
public class NewScenPart : ScenPart
{
}

/// <summary>升级层之外仍以字符串引用已改名的旧类型名（领域代码中的旧名读取者）。</summary>
public static class LeftoverLegacyName
{
    public static string Name() => "Mugirl.Control.Upgrade.OldComponent";
}
