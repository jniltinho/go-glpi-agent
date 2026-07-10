using DotnetGlpiAgent.Core.Configuration;

namespace DotnetGlpiAgent.App.Cli;

public static class AppConfigurationResolver
{
    public static EffectiveAgentConfiguration Build(ParsedCommandLine parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        string? configurationFile = parsed.ConfigurationFile;
        string? trustedRoot = null;
        if (!string.IsNullOrWhiteSpace(configurationFile))
        {
            configurationFile = Path.GetFullPath(configurationFile);
            if (!File.Exists(configurationFile))
            {
                throw new AgentConfigurationException("The specified configuration file does not exist.");
            }

            trustedRoot = Path.GetDirectoryName(configurationFile);
        }
        else if (OperatingSystem.IsWindows())
        {
            AgentPaths paths = AgentPaths.ForCurrentWindowsHost();
            if (File.Exists(paths.MainConfigFile))
            {
                configurationFile = paths.MainConfigFile;
                trustedRoot = paths.ConfigurationDirectory;
            }
        }

        return new AgentConfigurationBuilder().Build(
            configurationFile,
            commandLine: parsed.Values,
            trustedConfigurationRoot: trustedRoot);
    }
}
