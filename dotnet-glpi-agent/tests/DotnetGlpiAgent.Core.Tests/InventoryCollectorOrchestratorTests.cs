using System.Diagnostics;

using DotnetGlpiAgent.Core.Collection;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class InventoryCollectorOrchestratorTests
{
    [Fact]
    public async Task CollectAsync_IsolatesSuccessAccessDeniedMalformedAndTimeoutCollectors()
    {
        IInventoryCollector[] collectors =
        [
            FakeCollector.Success("success"),
            FakeCollector.Failure("denied", CollectionState.AccessDenied, "access-denied"),
            FakeCollector.Failure("malformed", CollectionState.Malformed, "malformed-data"),
            FakeCollector.TimingOut("timeout", TimeSpan.FromMilliseconds(20)),
        ];

        CollectionRunResult result = await new InventoryCollectorOrchestrator(4).CollectAsync(
            collectors,
            Options(),
            "cycle-1");

        Assert.Collection(
            result.Results,
            item => Assert.Equal(CollectionState.AccessDenied, item.State),
            item => Assert.Equal(CollectionState.Malformed, item.State),
            item => Assert.Equal(CollectionState.Success, item.State),
            item => Assert.Equal(CollectionState.TimedOut, item.State));
        Assert.Single(result.Contributions);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task CollectAsync_ObservesNonCancellableWorkAndDiscardsLateContribution()
    {
        var collector = new FakeCollector(
            "non-cancellable",
            InventoryCategory.Hardware,
            TimeSpan.FromMilliseconds(10),
            CollectorSupport.Supported,
            async (_, _) =>
            {
                await Task.Delay(60, CancellationToken.None);
                return Contribution("non-cancellable");
            });
        var stopwatch = Stopwatch.StartNew();

        CollectionRunResult result = await new InventoryCollectorOrchestrator(1).CollectAsync(
            [collector],
            Options(),
            "cycle-2");

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(50));
        CollectorExecutionResult execution = Assert.Single(result.Results);
        Assert.Equal(CollectionState.TimedOut, execution.State);
        Assert.Null(execution.Contribution);
        Assert.True(collector.Completed);
    }

    [Fact]
    public async Task CollectAsync_BoundsConcurrentCollectors()
    {
        var concurrency = new ConcurrencyTracker();
        IInventoryCollector[] collectors = Enumerable.Range(0, 10)
            .Select(index => FakeCollector.Tracked($"collector-{index:D2}", concurrency))
            .ToArray();

        CollectionRunResult result = await new InventoryCollectorOrchestrator(2).CollectAsync(
            collectors,
            Options(),
            "cycle-3");

        Assert.Equal(10, result.Contributions.Count);
        Assert.InRange(concurrency.Maximum, 1, 2);
    }

    [Fact]
    public async Task CollectAsync_ReportsUnavailableAndDisabledWithoutCollection()
    {
        FakeCollector unavailable = FakeCollector.Unsupported("unavailable", CollectorSupportState.Unavailable);
        FakeCollector disabled = FakeCollector.Unsupported("disabled", CollectorSupportState.Disabled);

        CollectionRunResult result = await new InventoryCollectorOrchestrator(2).CollectAsync(
            [unavailable, disabled],
            Options(),
            "cycle-4");

        Assert.Collection(
            result.Results,
            item => Assert.Equal(CollectionState.Disabled, item.State),
            item => Assert.Equal(CollectionState.Unavailable, item.State));
        Assert.True(result.IsComplete);
        Assert.False(unavailable.WasCollected);
        Assert.False(disabled.WasCollected);
    }

    [Fact]
    public async Task CollectAsync_ObservesCleanupAfterExternalCancellation()
    {
        bool cleanupObserved = false;
        var collector = new FakeCollector(
            "cancelled",
            InventoryCategory.Network,
            TimeSpan.FromSeconds(1),
            CollectorSupport.Supported,
            async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                    return Contribution("cancelled");
                }
                finally
                {
                    cleanupObserved = true;
                }
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        CollectionRunResult result = await new InventoryCollectorOrchestrator(1).CollectAsync(
            [collector],
            Options(),
            "cycle-5",
            cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(cleanupObserved);
        Assert.Equal(CollectionState.Cancelled, Assert.Single(result.Results).State);
    }

    private static AgentOptions Options() => new() { CollectorTimeout = TimeSpan.FromSeconds(1) };

    private static InventoryContribution Contribution(string source) => new() { Source = source };

    private sealed class FakeCollector : IInventoryCollector
    {
        private readonly Func<CollectorContext, CancellationToken, ValueTask<InventoryContribution>> _collect;
        private readonly CollectorSupport _support;

        public FakeCollector(
            string name,
            InventoryCategory category,
            TimeSpan timeout,
            CollectorSupport support,
            Func<CollectorContext, CancellationToken, ValueTask<InventoryContribution>> collect)
        {
            Name = name;
            Category = category;
            Timeout = timeout;
            _support = support;
            _collect = collect;
        }

        public string Name { get; }

        public InventoryCategory Category { get; }

        public TimeSpan Timeout { get; }

        public bool WasCollected { get; private set; }

        public bool Completed { get; private set; }

        public ValueTask<CollectorSupport> GetSupportAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_support);
        }

        public async ValueTask<InventoryContribution> CollectAsync(
            CollectorContext context,
            CancellationToken cancellationToken)
        {
            WasCollected = true;
            try
            {
                return await _collect(context, cancellationToken);
            }
            finally
            {
                Completed = true;
            }
        }

        public static FakeCollector Success(string name)
        {
            return new FakeCollector(
                name,
                InventoryCategory.Hardware,
                TimeSpan.FromSeconds(1),
                CollectorSupport.Supported,
                (_, _) => ValueTask.FromResult(Contribution(name)));
        }

        public static FakeCollector Failure(string name, CollectionState state, string code)
        {
            return new FakeCollector(
                name,
                InventoryCategory.Hardware,
                TimeSpan.FromSeconds(1),
                CollectorSupport.Supported,
                (_, _) => ValueTask.FromException<InventoryContribution>(
                    new CollectorFailureException(state, code, $"Collector diagnostic: {code}.")));
        }

        public static FakeCollector TimingOut(string name, TimeSpan timeout)
        {
            return new FakeCollector(
                name,
                InventoryCategory.Hardware,
                timeout,
                CollectorSupport.Supported,
                async (_, cancellationToken) =>
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                    return Contribution(name);
                });
        }

        public static FakeCollector Unsupported(string name, CollectorSupportState state)
        {
            return new FakeCollector(
                name,
                InventoryCategory.Hardware,
                TimeSpan.FromSeconds(1),
                new CollectorSupport(state, $"{name}-support"),
                (_, _) => ValueTask.FromResult(Contribution(name)));
        }

        public static FakeCollector Tracked(string name, ConcurrencyTracker tracker)
        {
            return new FakeCollector(
                name,
                InventoryCategory.Hardware,
                TimeSpan.FromSeconds(1),
                CollectorSupport.Supported,
                async (_, cancellationToken) =>
                {
                    tracker.Enter();
                    try
                    {
                        await Task.Delay(20, cancellationToken);
                        return Contribution(name);
                    }
                    finally
                    {
                        tracker.Exit();
                    }
                });
        }
    }

    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _maximum;

        public int Maximum => Volatile.Read(ref _maximum);

        public void Enter()
        {
            int current = Interlocked.Increment(ref _current);
            int maximum;
            while (current > (maximum = Volatile.Read(ref _maximum)))
            {
                if (Interlocked.CompareExchange(ref _maximum, current, maximum) == maximum)
                {
                    break;
                }
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }
}
