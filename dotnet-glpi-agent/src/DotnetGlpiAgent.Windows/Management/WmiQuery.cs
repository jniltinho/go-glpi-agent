namespace DotnetGlpiAgent.Windows.Management;

public sealed record WmiQuery(
    string NamespacePath,
    string ClassName,
    IReadOnlyList<string> Properties,
    string? WhereClause = null,
    TimeSpan? Timeout = null)
{
    public string ToWql()
    {
        ValidateIdentifier(ClassName, nameof(ClassName));
        if (Properties.Count == 0)
        {
            throw new ArgumentException("At least one WMI property is required.", nameof(Properties));
        }

        foreach (string property in Properties)
        {
            ValidateIdentifier(property, nameof(Properties));
        }

        string selection = string.Join(", ", Properties);
        return string.IsNullOrWhiteSpace(WhereClause)
            ? $"SELECT {selection} FROM {ClassName}"
            : $"SELECT {selection} FROM {ClassName} WHERE {WhereClause}";
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || value.Skip(1).Any(static character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ArgumentException("WMI identifiers may contain only ASCII letters, digits, and underscores.", parameterName);
        }
    }
}
