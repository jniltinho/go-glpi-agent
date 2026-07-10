using DotnetGlpiAgent.App.Runtime;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Diagnostics;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Serialization;
using DotnetGlpiAgent.Protocol.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotnetGlpiAgent.App.Service;

public sealed partial class AgentServiceWorker : BackgroundService
{
    private static readonly TimeSpan MaximumInitialDelay = TimeSpan.FromMinutes(5);
    private readonly AgentOptions _options;
    private readonly IAppRuntimeFactory _runtimeFactory;
    private readonly ServiceCycleScheduler _scheduler;
    private readonly AgentLogger _fileLogger;
    private readonly ILogger<AgentServiceWorker> _eventLogger;

    public AgentServiceWorker(
        AgentOptions options,
        IAppRuntimeFactory runtimeFactory,
        ServiceCycleScheduler scheduler,
        AgentLogger fileLogger,
        ILogger<AgentServiceWorker> eventLogger)
    {
        _options = options;
        _runtimeFactory = runtimeFactory;
        _scheduler = scheduler;
        _fileLogger = fileLogger;
        _eventLogger = eventLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogServiceStarted(_eventLogger);
        await _fileLogger.LogAsync(
            AgentLogLevel.Information,
            "service-started",
            "Dotnet GLPI Agent service started.",
            cancellationToken: stoppingToken).ConfigureAwait(false);
        try
        {
            await _scheduler.RunAsync(
                RunCycleAsync,
                _options.Delay,
                _options.Force ? TimeSpan.Zero : Min(_options.Delay, MaximumInitialDelay),
                IsExpectedError,
                LogExpectedCycleErrorAsync,
                stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal SCM stop/shutdown path.
        }
        finally
        {
            LogServiceStopped(_eventLogger);
            await _fileLogger.LogAsync(
                AgentLogLevel.Information,
                "service-stopped",
                "Dotnet GLPI Agent service stopped.",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        IAppRuntime runtime = _runtimeFactory.Create(_options);
        InventorySnapshot snapshot = await runtime.CollectAsync(_options, cancellationToken).ConfigureAwait(false);
        using IDisposable scope = CorrelationContext.BeginScope(snapshot.Collection.CorrelationId);
        await _fileLogger.LogAsync(
            AgentLogLevel.Information,
            "inventory-collected",
            snapshot.Collection.IsComplete ? "Inventory collection completed." : "Inventory collection completed with omissions.",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (_options.LocalPath is not null)
        {
            await LocalInventoryWriter.WriteAsync(snapshot, _options.LocalPath, cancellationToken).ConfigureAwait(false);
        }

        if (_options.Server is not null)
        {
            GlpiSubmissionResult submission = await runtime.SubmitAsync(
                snapshot,
                _options,
                cancellationToken).ConfigureAwait(false);
            AgentLogLevel level = submission.State == SubmissionState.Accepted
                ? AgentLogLevel.Information
                : AgentLogLevel.Warning;
            await _fileLogger.LogAsync(
                level,
                "inventory-submission",
                $"Inventory submission completed with state {submission.State}.",
                new Dictionary<string, string>
                {
                    ["protocol"] = submission.Protocol.ToString(),
                    ["state"] = submission.State.ToString(),
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsExpectedError(Exception exception)
    {
        return exception is GlpiProtocolException
            or AgentConfigurationException
            or IOException
            or UnauthorizedAccessException;
    }

    private async Task LogExpectedCycleErrorAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogExpectedCycleError(_eventLogger, exception.GetType().Name);
        await _fileLogger.LogAsync(
            AgentLogLevel.Warning,
            "inventory-cycle-failed",
            "Inventory cycle failed with an expected error.",
            exception: exception,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first <= second ? first : second;

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Dotnet GLPI Agent service started.")]
    private static partial void LogServiceStarted(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Inventory cycle failed with an expected error: {errorType}")]
    private static partial void LogExpectedCycleError(ILogger logger, string errorType);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Dotnet GLPI Agent service stopped.")]
    private static partial void LogServiceStopped(ILogger logger);
}
