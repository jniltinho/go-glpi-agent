using DotnetGlpiAgent.App.Runtime;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Serialization;
using DotnetGlpiAgent.Protocol.Transport;

namespace DotnetGlpiAgent.App.Cli;

public sealed class CommandApp
{
    private readonly IAppRuntimeFactory _runtimeFactory;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public CommandApp(
        IAppRuntimeFactory runtimeFactory,
        TextWriter output,
        TextWriter error)
    {
        _runtimeFactory = runtimeFactory;
        _output = output;
        _error = error;
    }

    public async ValueTask<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        AgentOptions? options = null;
        try
        {
            ParsedCommandLine parsed = CommandLineParser.Parse(arguments);
            if (parsed.ShowHelp || parsed.Command.Length == 0)
            {
                await _output.WriteLineAsync(HelpText);
                return parsed.ShowHelp ? ExitCodes.Success : ExitCodes.Usage;
            }

            if (parsed.Command.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                await _output.WriteLineAsync($"Dotnet GLPI Agent {AgentVersion.Value}");
                return ExitCodes.Success;
            }

            EffectiveAgentConfiguration configuration = AppConfigurationResolver.Build(parsed);
            options = configuration.Options;
            foreach (string warning in configuration.Warnings)
            {
                await _error.WriteLineAsync($"warning: {warning}");
            }

            if (parsed.Command.Equals("validate-config", StringComparison.OrdinalIgnoreCase))
            {
                foreach ((string key, string value) in configuration.RedactedValues.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    await _output.WriteLineAsync($"{key}={value}");
                }

                return ExitCodes.Success;
            }

            return await RunInventoryAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("error: operation cancelled");
            return ExitCodes.Cancelled;
        }
        catch (CommandLineException exception)
        {
            await _error.WriteLineAsync($"error: {exception.Message}");
            return ExitCodes.Usage;
        }
        catch (AgentConfigurationException exception)
        {
            await _error.WriteLineAsync($"error: {exception.Message}");
            return ExitCodes.Configuration;
        }
        catch (GlpiProtocolException exception)
        {
            await _error.WriteLineAsync($"error [{exception.DiagnosticCode}]: {exception.Message}");
            return exception.Kind is GlpiFailureKind.Authentication or GlpiFailureKind.Tls
                ? ExitCodes.Security
                : ExitCodes.Transport;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync(options?.Debug == true ? exception.ToString() : $"error: {exception.Message}");
            return ExitCodes.Unexpected;
        }
    }

    private async ValueTask<int> RunInventoryAsync(
        AgentOptions options,
        CancellationToken cancellationToken)
    {
        if (options.LocalPath is null && options.Server is null)
        {
            throw new AgentConfigurationException("The run command requires a local or server target.");
        }

        IAppRuntime runtime = _runtimeFactory.Create(options);
        InventorySnapshot snapshot = await runtime.CollectAsync(options, cancellationToken).ConfigureAwait(false);
        foreach (CategoryCollectionStatus status in snapshot.Collection.Categories.Where(static status => status.State is not (
                     CollectionState.Success or CollectionState.Unavailable or CollectionState.Disabled)))
        {
            await _error.WriteLineAsync($"warning [{status.Collector}]: {status.State} ({status.DiagnosticCode})");
        }

        if (options.LocalPath is not null)
        {
            LocalInventoryFiles files = await LocalInventoryWriter.WriteAsync(
                snapshot,
                options.LocalPath,
                cancellationToken).ConfigureAwait(false);
            await _error.WriteLineAsync($"inventory JSON: {files.JsonPath}");
            await _error.WriteLineAsync($"inventory XML: {files.XmlPath}");
        }

        if (options.Server is not null)
        {
            GlpiSubmissionResult submission = await runtime.SubmitAsync(
                snapshot,
                options,
                cancellationToken).ConfigureAwait(false);
            await _error.WriteLineAsync($"GLPI submission: {submission.Protocol} {submission.State}");
            if (submission.State == SubmissionState.Indeterminate)
            {
                return ExitCodes.Indeterminate;
            }

            if (submission.State == SubmissionState.Rejected)
            {
                return ExitCodes.Transport;
            }
        }

        return snapshot.Collection.IsComplete ? ExitCodes.Success : ExitCodes.PartialInventory;
    }

    private const string HelpText = """
        Dotnet GLPI Agent

        Usage:
          dotnet-glpi-agent version
          dotnet-glpi-agent validate-config [--config FILE] [options]
          dotnet-glpi-agent run [--config FILE] (--local DIRECTORY | --server URL) [options]

        Common options:
          --server URL               GLPI inventory endpoint
          --local DIRECTORY          Write deterministic JSON and XML inventories
          --tag VALUE                GLPI entity tag
          --force                    Force inventory submission
          --no-category LIST         Exclude comma-separated collector categories
          --scan-processes           Include process inventory
          --scan-profiles            Inspect unloaded user profiles
          --debug                    Print detailed unexpected errors
        """;
}

public static class ExitCodes
{
    public const int Success = 0;
    public const int Unexpected = 1;
    public const int Usage = 2;
    public const int Configuration = 3;
    public const int PartialInventory = 4;
    public const int Transport = 5;
    public const int Security = 6;
    public const int Indeterminate = 7;
    public const int Cancelled = 130;
}
