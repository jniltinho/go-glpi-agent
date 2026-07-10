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
    string DataDirectory,
    string StateDirectory,
    string LogDirectory,
    string MainConfigFile)
{
    // agent.cfg lives next to the binary; mutable state/logs live under
    // ProgramData so the installation directory stays read-only at runtime.
    public static AgentPaths FromWindowsRoots(string installationDirectory, string programData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(programData);

        string data = Path.Combine(programData, "DotnetGlpiAgent");
        return new AgentPaths(
            installationDirectory,
            installationDirectory,
            data,
            Path.Combine(data, "state"),
            Path.Combine(data, "logs"),
            Path.Combine(installationDirectory, "agent.cfg"));
    }

    public static AgentPaths ForCurrentWindowsHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows default paths are available only on Windows.");
        }

        string installation = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return FromWindowsRoots(installation, programData);
    }
}
