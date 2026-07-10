using System.Diagnostics;

using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;

namespace DotnetGlpiAgent.Core.Collection;

public sealed class InventoryCollectorOrchestrator
{
    private readonly int _maximumConcurrency;
    private readonly TimeProvider _timeProvider;

    public InventoryCollectorOrchestrator(
        int maximumConcurrency,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _maximumConcurrency = maximumConcurrency;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CollectionRunResult> CollectAsync(
        IEnumerable<IInventoryCollector> collectors,
        AgentOptions options,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        DateTimeOffset started = _timeProvider.GetUtcNow();
        IInventoryCollector[] ordered = collectors
            .OrderBy(static collector => collector.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static collector => collector.Name, StringComparer.Ordinal)
            .ToArray();
        using var gate = new SemaphoreSlim(_maximumConcurrency, _maximumConcurrency);

        Task<CollectorExecutionResult>[] tasks = ordered
            .Select(collector => ExecuteAsync(
                collector,
                options,
                correlationId,
                gate,
                cancellationToken))
            .ToArray();
        CollectorExecutionResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return new CollectionRunResult(started, _timeProvider.GetUtcNow(), results);
    }

    private async Task<CollectorExecutionResult> ExecuteAsync(
        IInventoryCollector collector,
        AgentOptions options,
        string correlationId,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result(collector, CollectionState.Cancelled, started, null, "cancelled", "Collection was cancelled before the collector started.");
        }

        try
        {
            CollectorSupport support;
            try
            {
                support = await collector.GetSupportAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Result(collector, CollectionState.Cancelled, started, null, "cancelled", "Collection was cancelled during the support check.");
            }

            if (support.State != CollectorSupportState.Supported)
            {
                CollectionState state = support.State == CollectorSupportState.Unavailable
                    ? CollectionState.Unavailable
                    : CollectionState.Disabled;
                return Result(collector, state, started, null, support.DiagnosticCode, null);
            }

            TimeSpan timeout = collector.Timeout > TimeSpan.Zero
                ? collector.Timeout
                : options.CollectorTimeout;
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellation.CancelAfter(timeout);
            DateTimeOffset deadline = _timeProvider.GetUtcNow().Add(timeout);
            var context = new CollectorContext(options, deadline, correlationId);
            long executionStarted = Stopwatch.GetTimestamp();

            try
            {
                InventoryContribution contribution = await collector.CollectAsync(
                    context,
                    linkedCancellation.Token).ConfigureAwait(false);
                if (Stopwatch.GetElapsedTime(executionStarted) > timeout)
                {
                    return Result(
                        collector,
                        CollectionState.TimedOut,
                        started,
                        null,
                        "deadline-exceeded",
                        "The collector completed after its deadline; its contribution was discarded.");
                }

                return Result(collector, CollectionState.Success, started, contribution, null, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result(collector, CollectionState.Cancelled, started, null, "cancelled", "Collection was cancelled.");
            }
            catch (OperationCanceledException)
            {
                return Result(collector, CollectionState.TimedOut, started, null, "deadline-exceeded", "The collector exceeded its deadline.");
            }
            catch (CollectorFailureException exception)
            {
                return Result(
                    collector,
                    exception.State,
                    started,
                    null,
                    exception.DiagnosticCode,
                    exception.Message);
            }
            catch (Exception exception)
            {
                return Result(
                    collector,
                    CollectionState.Failed,
                    started,
                    null,
                    "unexpected-collector-error",
                    exception.GetType().Name);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static CollectorExecutionResult Result(
        IInventoryCollector collector,
        CollectionState state,
        long started,
        InventoryContribution? contribution,
        string? diagnosticCode,
        string? diagnosticMessage)
    {
        return new CollectorExecutionResult(
            collector.Name,
            collector.Category,
            state,
            Stopwatch.GetElapsedTime(started),
            contribution,
            diagnosticCode,
            diagnosticMessage);
    }
}
