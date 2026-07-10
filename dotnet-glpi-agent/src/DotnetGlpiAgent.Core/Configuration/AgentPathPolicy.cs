namespace DotnetGlpiAgent.Core.Configuration;

public static class AgentPathPolicy
{
    public static string EnsureWithinRoot(string path, string trustedRoot, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        string fullRoot = Path.GetFullPath(trustedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(fullRoot, fullPath);

        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new AgentConfigurationException($"The {description} path is outside the trusted configuration root.");
        }

        return fullPath;
    }
}

public sealed record AgentPaths(
    string InstallationDirectory,
    string ConfigurationDirectory,
    string StateDirectory,
    string LogDirectory,
    string MainConfigFile)
{
    public static AgentPaths FromWindowsRoots(string programFiles, string programData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(programData);

        string installation = Path.Combine(programFiles, "DotnetGlpiAgent");
        string configuration = Path.Combine(programData, "DotnetGlpiAgent");
        return new AgentPaths(
            installation,
            configuration,
            Path.Combine(configuration, "state"),
            Path.Combine(configuration, "logs"),
            Path.Combine(configuration, "agent.cfg"));
    }

    public static AgentPaths ForCurrentWindowsHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows default paths are available only on Windows.");
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return FromWindowsRoots(programFiles, programData);
    }
}
