namespace DotnetGlpiAgent.Core.Configuration;

public sealed record AgentConfigEntry(
    string Key,
    string Value,
    string FilePath,
    int LineNumber);
