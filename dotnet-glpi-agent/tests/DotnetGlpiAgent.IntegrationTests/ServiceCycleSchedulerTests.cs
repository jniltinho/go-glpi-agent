using DotnetGlpiAgent.App.Service;

namespace DotnetGlpiAgent.IntegrationTests;

public sealed class ServiceCycleSchedulerTests
{
    [Fact]
    public async Task RunSingleCycleAsync_PreventsOverlappingCycles()
    {
        using var scheduler = new ServiceCycleScheduler(new ZeroDelayProvider());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        int maximumRunning = 0;
        async Task Cycle(CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref running);
            maximumRunning = Math.Max(maximumRunning, current);
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref running);
        }

        ValueTask<bool> first = scheduler.RunSingleCycleAsync(Cycle, CancellationToken.None);
        await entered.Task;
        bool second = await scheduler.RunSingleCycleAsync(Cycle, CancellationToken.None);
        release.SetResult();

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, maximumRunning);
    }

    [Fact]
    public async Task RunAsync_ExpectedErrorSurvivesAndRunsNextCycle()
    {
        using var scheduler = new ServiceCycleScheduler(new ZeroDelayProvider());
        using var cancellation = new CancellationTokenSource();
        int cycles = 0;
        int expectedErrors = 0;
        Task Cycle(CancellationToken _)
        {
            cycles++;
            if (cycles == 1)
            {
                throw new IOException("Expected test error.");
            }

            cancellation.Cancel();
            return Task.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.RunAsync(
            Cycle,
            TimeSpan.Zero,
            TimeSpan.Zero,
            static exception => exception is IOException,
            (exception, _) =>
            {
                expectedErrors++;
                return Task.CompletedTask;
            },
            cancellation.Token));

        Assert.Equal(2, cycles);
        Assert.Equal(1, expectedErrors);
    }

    [Fact]
    public async Task RunAsync_UnexpectedErrorEscapesAndStopsLoop()
    {
        using var scheduler = new ServiceCycleScheduler(new ZeroDelayProvider());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RunAsync(
            static _ => throw new InvalidOperationException("Unexpected test error."),
            TimeSpan.Zero,
            TimeSpan.Zero,
            static _ => false,
            static (_, _) => Task.CompletedTask,
            CancellationToken.None));

        Assert.Equal("Unexpected test error.", exception.Message);
    }

    [Fact]
    public async Task RunSingleCycleAsync_PropagatesStopCancellation()
    {
        using var scheduler = new ServiceCycleScheduler(new ZeroDelayProvider());
        using var cancellation = new CancellationTokenSource();
        ValueTask<bool> cycle = scheduler.RunSingleCycleAsync(
            static async token => await Task.Delay(Timeout.InfiniteTimeSpan, token),
            cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cycle);
    }

    [Fact]
    public void CryptographicInitialDelayProvider_StaysWithinBound()
    {
        var provider = new CryptographicInitialDelayProvider();
        TimeSpan maximum = TimeSpan.FromSeconds(5);

        TimeSpan result = provider.GetDelay(maximum);

        Assert.InRange(result, TimeSpan.Zero, maximum);
    }

    private sealed class ZeroDelayProvider : IInitialDelayProvider
    {
        public TimeSpan GetDelay(TimeSpan maximumDelay) => TimeSpan.Zero;
    }
}
