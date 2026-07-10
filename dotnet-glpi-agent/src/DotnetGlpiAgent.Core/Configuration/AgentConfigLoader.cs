using System.Text;

namespace DotnetGlpiAgent.Core.Configuration;

public interface IAgentConfigLoader
{
    IReadOnlyList<AgentConfigEntry> Load(string path, string? trustedRoot = null);
}

public sealed class AgentConfigLoader : IAgentConfigLoader
{
    private const int MaximumIncludeDepth = 16;

    public IReadOnlyList<AgentConfigEntry> Load(string path, string? trustedRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string root = Path.GetFullPath(trustedRoot ?? Path.GetDirectoryName(fullPath)!);
        var entries = new List<AgentConfigEntry>();
        var loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadFile(AgentPathPolicy.EnsureWithinRoot(fullPath, root, "configuration"), root, 0, entries, loadedFiles);
        return entries;
    }

    private static void LoadFile(
        string path,
        string trustedRoot,
        int depth,
        List<AgentConfigEntry> entries,
        HashSet<string> loadedFiles)
    {
        if (depth > MaximumIncludeDepth)
        {
            throw new AgentConfigurationException("The configuration include depth exceeds the supported limit.");
        }

        string fullPath = AgentPathPolicy.EnsureWithinRoot(path, trustedRoot, "included configuration");
        if (!loadedFiles.Add(fullPath))
        {
            return;
        }

        using var reader = new StreamReader(fullPath, Encoding.UTF8, true);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            if (TryReadInclude(trimmed, out string include))
            {
                LoadInclude(include, fullPath, trustedRoot, depth + 1, entries, loadedFiles);
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = trimmed[..separator].Trim().ToLowerInvariant();
            string value = Unquote(trimmed[(separator + 1)..].Trim());
            entries.Add(new AgentConfigEntry(key, value, fullPath, lineNumber));
        }
    }

    private static void LoadInclude(
        string include,
        string currentFile,
        string trustedRoot,
        int depth,
        List<AgentConfigEntry> entries,
        HashSet<string> loadedFiles)
    {
        string candidate = Path.IsPathRooted(include)
            ? include
            : Path.Combine(Path.GetDirectoryName(currentFile)!, include);
        string fullPath = AgentPathPolicy.EnsureWithinRoot(candidate, trustedRoot, "included configuration");

        if (Directory.Exists(fullPath))
        {
            foreach (string file in Directory.EnumerateFiles(fullPath, "*.cfg", SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static file => file, StringComparer.Ordinal))
            {
                LoadFile(file, trustedRoot, depth, entries, loadedFiles);
            }

            return;
        }

        LoadFile(fullPath, trustedRoot, depth, entries, loadedFiles);
    }

    private static bool TryReadInclude(string line, out string include)
    {
        include = string.Empty;
        if (!line.StartsWith("include", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string remainder = line["include".Length..].TrimStart();
        if (remainder.StartsWith('='))
        {
            remainder = remainder[1..].TrimStart();
        }

        if (remainder.Length == 0)
        {
            return false;
        }

        include = Unquote(RemoveInlineComment(remainder));
        return include.Length > 0;
    }

    private static string RemoveInlineComment(string value)
    {
        if (value.StartsWith('"') || value.StartsWith('\''))
        {
            return value;
        }

        int comment = value.IndexOf('#');
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

public sealed class AgentConfigurationException : Exception
{
    public AgentConfigurationException(string message)
        : base(message)
    {
    }

    public AgentConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
