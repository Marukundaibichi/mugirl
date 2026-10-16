using System.Collections.Generic;
using Mugirl.Rendering;

namespace Mugirl.Tests.Rendering;

/// <summary>延后渲染刷新的调度：登记后 30 tick 补刷一次、300 tick 兜底一次，然后移除。</summary>
public sealed class PendingRenderRefreshScheduleTests
{
    private readonly PendingRenderRefreshSchedule<string> schedule = new();
    private readonly List<string> refreshed = [];
    private readonly HashSet<string> invalid = [];

    [Fact]
    public void Schedule_RefreshesAtEarlyDelayThenAtWindowEnd()
    {
        schedule.Schedule("a", 100);
        TickRange(100, 129);
        Assert.Empty(refreshed);

        Tick(130);
        Assert.Equal(new[] { "a" }, refreshed);

        TickRange(131, 399);
        Assert.Single(refreshed);

        Tick(400);
        Assert.Equal(new[] { "a", "a" }, refreshed);
        Assert.Equal(0, schedule.Count);

        TickRange(401, 1000);
        Assert.Equal(2, refreshed.Count);
    }

    [Fact]
    public void LateTick_PastTheWindowRefreshesOnceAndFinishes()
    {
        // 一次跳过整个窗口（例如游戏暂停后读档或长时间未 tick）：只刷新一次并直接结束，与 1.6 相同。
        schedule.Schedule("a", 0);
        Tick(1000);
        Assert.Equal(new[] { "a" }, refreshed);
        Assert.Equal(0, schedule.Count);
        Tick(1001);
        Assert.Single(refreshed);
    }

    [Fact]
    public void InvalidKey_IsDroppedWithoutRefresh()
    {
        schedule.Schedule("gone", 0);
        schedule.Schedule("kept", 0);
        invalid.Add("gone");
        Tick(30);
        Assert.Equal(new[] { "kept" }, refreshed);
        Assert.Equal(1, schedule.Count);
    }

    [Fact]
    public void Reschedule_RestartsTheWindow()
    {
        schedule.Schedule("a", 0);
        Tick(30);
        schedule.Schedule("a", 200);
        TickRange(31, 229);
        Assert.Single(refreshed);
        Tick(230);
        Assert.Equal(2, refreshed.Count);
        Tick(500);
        Assert.Equal(3, refreshed.Count);
        Assert.Equal(0, schedule.Count);
    }

    [Fact]
    public void RefreshCallback_MayScheduleAgain()
    {
        // 刷新回调中重新登记（例如外部刷新通知）不会被本轮覆盖，按新的计划执行。
        schedule.Schedule("a", 0);
        schedule.Tick(30, IsValid, key =>
        {
            refreshed.Add(key);
            schedule.Schedule("b", 30);
            schedule.Schedule(key, 30);
        });
        Assert.Equal(new[] { "a" }, refreshed);
        Assert.Equal(2, schedule.Count);
        Tick(60);
        Assert.Equal(new[] { "a", "a", "b" }, refreshed);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        schedule.Schedule("a", 0);
        schedule.Clear();
        TickRange(0, 400);
        Assert.Empty(refreshed);
        Assert.Equal(0, schedule.Count);
    }

    private bool IsValid(string key) => !invalid.Contains(key);

    private void Tick(int tick) => schedule.Tick(tick, IsValid, refreshed.Add);

    private void TickRange(int first, int last)
    {
        for (int tick = first; tick <= last; tick++)
        {
            Tick(tick);
        }
    }
}
