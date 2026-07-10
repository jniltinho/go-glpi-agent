using System.Text.RegularExpressions;

namespace DotnetGlpiAgent.Core.Diagnostics;

public sealed partial class SecretRedactor
{
    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "client-cert-password",
        "client-certificate-password",
        "client_secret",
        "client-secret",
        "password",
        "proxy-password",
        "token",
    };

    private readonly string[] _secrets;

    public SecretRedactor(IEnumerable<string?>? secrets = null)
    {
        _secrets = (secrets ?? [])
            .Where(static secret => !string.IsNullOrEmpty(secret))
            .Select(static secret => secret!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static secret => secret.Length)
            .ToArray();
    }

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string redacted = AuthorizationRegex().Replace(value, "$1<redacted>");
        redacted = SecretAssignmentRegex().Replace(redacted, "$1$2<redacted>");
        redacted = UriUserInfoRegex().Replace(redacted, "$1<redacted>@");

        foreach (string secret in _secrets)
        {
            redacted = redacted.Replace(secret, "<redacted>", StringComparison.Ordinal);
        }

        return redacted;
    }

    public IReadOnlyDictionary<string, string> RedactProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return properties.ToDictionary(
            static pair => pair.Key,
            pair => SecretPropertyNames.Contains(pair.Key) ? "<redacted>" : Redact(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*(?:basic|bearer)\\s+)[^\\s,;]+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(?i)\\b(password|passwd|token|client[_-]?secret|proxy[_-]?password)([\"']?\\s*[=:]\\s*[\"']?)[^\\s,;\"']+", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(https?://)[^/@\\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex UriUserInfoRegex();
}
