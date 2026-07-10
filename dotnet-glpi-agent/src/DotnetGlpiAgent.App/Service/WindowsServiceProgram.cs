using DotnetGlpiAgent.App.Cli;
using DotnetGlpiAgent.App.Runtime;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Diagnostics;
using DotnetGlpiAgent.Windows.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotnetGlpiAgent.App.Service;

public static class WindowsServiceProgram
{
    public const string ServiceName = "DotnetGlpiAgent";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string[] serviceArguments = arguments.Count > 0
            && arguments[0].Equals("service", StringComparison.OrdinalIgnoreCase)
            ? arguments.Skip(1).ToArray()
            : arguments.ToArray();
        ParsedCommandLine parsed = CommandLineParser.Parse(["run", .. serviceArguments]);
        EffectiveAgentConfiguration configuration = AppConfigurationResolver.Build(parsed);
        AgentOptions options = configuration.Options;
        if (options.Server is null && options.LocalPath is null)
        {
            throw new AgentConfigurationException("The Windows Service requires a local or server target.");
        }

        AgentPaths paths = AgentPaths.ForCurrentWindowsHost();
        ValidateProtectedServicePaths(parsed, options, paths);
        var hardener = new WindowsFilePermissionHardener();
        string logDirectory = paths.LogDirectory;
        var fileSink = new RollingFileLogSink(
            logDirectory,
            "agent.log",
            10 * 1024 * 1024,
            5,
            hardener);
        var redactor = new SecretRedactor(
        [
            options.Password,
            options.ProxyPassword,
            options.ClientCertificatePassword,
        ]);
        var agentLogger = new AgentLogger([fileSink], redactor);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(service => service.ServiceName = ServiceName);
        builder.Services.Configure<HostOptions>(host =>
        {
            host.ShutdownTimeout = TimeSpan.FromSeconds(30);
            host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IAppRuntimeFactory, AppRuntimeFactory>();
        builder.Services.AddSingleton<IInitialDelayProvider, CryptographicInitialDelayProvider>();
        builder.Services.AddSingleton<ServiceCycleScheduler>();
        builder.Services.AddSingleton(agentLogger);
        builder.Services.AddHostedService<AgentServiceWorker>();

        using IHost host = builder.Build();
        try
        {
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        finally
        {
            fileSink.Dispose();
        }
    }

    private static void ValidateProtectedServicePaths(
        ParsedCommandLine parsed,
        AgentOptions options,
        AgentPaths paths)
    {
        var policy = new WindowsPathSecurityPolicy(new WindowsFileAccessControlInspector());
        string executable = Environment.ProcessPath
            ?? throw new AgentConfigurationException("The service executable path could not be resolved.");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        policy.ValidateTrustedPath(executable, programFiles, "service executable");

        string configuration = parsed.ConfigurationFile is null
            ? paths.MainConfigFile
            : Path.GetFullPath(parsed.ConfigurationFile);
        policy.ValidateTrustedPath(configuration, paths.ConfigurationDirectory, "service configuration");
        if (!string.IsNullOrWhiteSpace(options.StateDirectory))
        {
            policy.ValidateTrustedPath(options.StateDirectory, paths.ConfigurationDirectory, "service state");
        }
    }
}
