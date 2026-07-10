using System.Security.Cryptography;

namespace DotnetGlpiAgent.App.Service;

public interface IInitialDelayProvider
{
    TimeSpan GetDelay(TimeSpan maximumDelay);
}

public sealed class CryptographicInitialDelayProvider : IInitialDelayProvider
{
    public TimeSpan GetDelay(TimeSpan maximumDelay)
    {
        if (maximumDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        int maximumMilliseconds = (int)Math.Min(maximumDelay.TotalMilliseconds, int.MaxValue - 1D);
        return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(maximumMilliseconds + 1));
    }
}

public sealed class ServiceCycleScheduler : IDisposable
{
    private readonly IInitialDelayProvider _initialDelayProvider;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private bool _disposed;

    public ServiceCycleScheduler(
        IInitialDelayProvider initialDelayProvider,
        TimeProvider? timeProvider = null)
    {
        _initialDelayProvider = initialDelayProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(
        Func<CancellationToken, Task> cycle,
        TimeSpan interval,
        TimeSpan maximumInitialDelay,
        Func<Exception, bool> isExpectedError,
        Func<Exception, CancellationToken, Task> onExpectedError,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(isExpectedError);
        ArgumentNullException.ThrowIfNull(onExpectedError);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);

        TimeSpan initialDelay = _initialDelayProvider.GetDelay(maximumInitialDelay);
        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleCycleAsync(cycle, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (isExpectedError(exception))
            {
                await onExpectedError(exception, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(interval, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> RunSingleCycleAsync(
        Func<CancellationToken, Task> cycle,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cycle);
        if (!await _cycleLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await cycle(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cycleLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
