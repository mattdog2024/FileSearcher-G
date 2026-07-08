using FileTrace.Core.Scanning;

namespace FileTrace.Core.Tests.Scanning;

/// <summary>
/// 针对 <see cref="ScanPauseController"/> 暂停时长累计与 Reset 行为的单元测试。
/// 这些行为是索引卡片"已用时/预计剩余时间"展示能否准确的关键前置条件——
/// 如果暂停期间被错误地计入"有效耗时"，会导致处理速度被低估、剩余时间被高估。
/// </summary>
public class ScanPauseControllerTests
{
    [Fact]
    public void TotalPausedDuration_InitiallyZero()
    {
        var controller = new ScanPauseController();

        Assert.Equal(TimeSpan.Zero, controller.TotalPausedDuration);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task TotalPausedDuration_AccumulatesAcrossMultiplePauseResumeCycles()
    {
        var controller = new ScanPauseController();

        controller.Pause();
        await Task.Delay(30);
        controller.Resume();

        var afterFirstCycle = controller.TotalPausedDuration;
        Assert.True(afterFirstCycle >= TimeSpan.FromMilliseconds(20));

        controller.Pause();
        await Task.Delay(30);
        controller.Resume();

        var afterSecondCycle = controller.TotalPausedDuration;
        Assert.True(afterSecondCycle > afterFirstCycle);
    }

    [Fact]
    public async Task TotalPausedDuration_WhileCurrentlyPaused_IncludesOngoingPauseSegment()
    {
        var controller = new ScanPauseController();

        controller.Pause();
        await Task.Delay(30);

        // 仍处于暂停中（尚未调用 Resume），TotalPausedDuration 应该已经把
        // "正在进行中的这一段暂停"计算在内，而不是等到 Resume 才更新。
        Assert.True(controller.TotalPausedDuration >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void Pause_CalledTwiceInARow_DoesNotResetTheOngoingPauseSegment()
    {
        var controller = new ScanPauseController();

        controller.Pause();
        var firstPauseDuration = controller.TotalPausedDuration;
        controller.Pause(); // 重复调用不应该重置正在进行中的暂停计时

        Assert.True(controller.IsPaused);
        Assert.True(controller.TotalPausedDuration >= firstPauseDuration);
    }

    [Fact]
    public void Resume_WithoutPriorPause_DoesNotThrowAndKeepsStateConsistent()
    {
        var controller = new ScanPauseController();

        controller.Resume();

        Assert.False(controller.IsPaused);
        Assert.Equal(TimeSpan.Zero, controller.TotalPausedDuration);
    }

    [Fact]
    public async Task Reset_ClearsAccumulatedPauseDurationAndPausedState()
    {
        var controller = new ScanPauseController();

        controller.Pause();
        await Task.Delay(30);
        controller.Resume();
        Assert.True(controller.TotalPausedDuration > TimeSpan.Zero);

        controller.Reset();

        Assert.False(controller.IsPaused);
        Assert.Equal(TimeSpan.Zero, controller.TotalPausedDuration);
    }

    [Fact]
    public async Task Reset_WhileCurrentlyPaused_ClearsPausedStateAndUnblocksWaiters()
    {
        var controller = new ScanPauseController();
        controller.Pause();

        controller.Reset();

        Assert.False(controller.IsPaused);
        // WaitIfPaused 不应该再阻塞——用带超时的 Task.WhenAny 验证不会挂起测试。
        var waitTask = Task.Run(() => controller.WaitIfPaused(CancellationToken.None));
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(waitTask, completed);
    }
}
