using System.Reflection;
using System.Runtime.CompilerServices;
using Mugirl.Features.Lances;
using Verse;

namespace Mugirl.Tests.Lances;

/// <summary>
/// 落地拖尾绘制补丁的全局快速门：门关着时任何 Pawn 的收回进度都只能是 0，与逐人物查表的结果相同；门开着时照常逐人物计算。
/// 用最小游戏状态提供 tick。会改进程级的 Current，所以放在不并行的集合里，结束时还原。
/// </summary>
[Collection("Combat hooks")]
public sealed class LanceMotionBlurRecoveryGateTests
{
    [Fact]
    public void GateIsOpenExactlyWhileSomeLandingCanStillRetract()
    {
        using var clock = new GameClock();
        Pawn first = NewPawn(31);
        Pawn second = NewPawn(32);
        clock.Tick = 1000;
        Assert.False(LanceMotionBlur.AnyRecoveryActive());

        LanceMotionBlur.BeginRecovery(first, new Vector3(1f, 0f, 0f));
        for (int tick = 1000; tick <= 1030; tick++)
        {
            clock.Tick = tick;
            if (tick == 1010)
            {
                LanceMotionBlur.BeginRecovery(second, new Vector3(0f, 0f, 1f));
            }

            bool open = LanceMotionBlur.AnyRecoveryActive();
            float firstScale = LanceMotionBlur.RecoveryScale(first);
            float secondScale = LanceMotionBlur.RecoveryScale(second);
            Assert.Equal(tick < 1028, open);
            Assert.Equal(tick < 1018, firstScale > 0f);
            Assert.Equal(tick is >= 1010 and < 1028, secondScale > 0f);
        }
    }

    [Fact]
    public void AnEarlierTickKeepsTheGateOpenAndScalesStillComeFromTheRecords()
    {
        using var clock = new GameClock();
        Pawn landed = NewPawn(33);
        Pawn bystander = NewPawn(34);
        clock.Tick = 5000;
        LanceMotionBlur.BeginRecovery(landed, new Vector3(1f, 0f, 0f));

        // 换到 tick 更小的一局：窗口不提前关，逐人物计算与不设门时相同。
        clock.Tick = 100;
        Assert.True(LanceMotionBlur.AnyRecoveryActive());
        Assert.Equal(0f, LanceMotionBlur.RecoveryScale(bystander));
        Assert.Equal(0f, LanceMotionBlur.RecoveryScale(landed));

        clock.Tick = 5018;
        Assert.False(LanceMotionBlur.AnyRecoveryActive());
    }

    [Fact]
    public void OutsideAGameTheGateStaysClosed()
    {
        using var clock = new GameClock();
        Pawn landed = NewPawn(35);
        clock.Tick = 2000;
        LanceMotionBlur.BeginRecovery(landed, new Vector3(1f, 0f, 0f));

        Current.ProgramState = ProgramState.Entry;
        Assert.False(LanceMotionBlur.AnyRecoveryActive());
        Assert.Equal(0f, LanceMotionBlur.RecoveryScale(landed));

        Current.ProgramState = ProgramState.Playing;
        Assert.True(LanceMotionBlur.AnyRecoveryActive());
    }

    private static Pawn NewPawn(int id)
    {
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = "LanceGatePawn";
        return new Pawn { def = definition, thingIDNumber = id };
    }

    // 最小游戏状态：只提供 Find.TickManager.TicksGame。创建时先关掉前面测试留下的收回窗口。
    private sealed class GameClock : IDisposable
    {
        private static readonly FieldInfo TicksGame = typeof(TickManager).GetField("ticksGameInt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly Game previousGame = Current.Game;
        private readonly ProgramState previousState = Current.ProgramState;
        private readonly TickManager manager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));

        internal GameClock()
        {
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            game.tickManager = manager;
            Current.Game = game;
            Current.ProgramState = ProgramState.Playing;
            Tick = int.MaxValue / 2;
            Assert.False(LanceMotionBlur.AnyRecoveryActive());
        }

        internal int Tick
        {
            set => TicksGame.SetValue(manager, value);
        }

        public void Dispose()
        {
            Current.ProgramState = previousState;
            Current.Game = previousGame;
        }
    }
}
