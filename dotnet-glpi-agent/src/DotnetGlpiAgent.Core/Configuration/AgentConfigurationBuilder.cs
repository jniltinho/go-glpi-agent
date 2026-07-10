using System.Collections;
using System.Globalization;

namespace DotnetGlpiAgent.Core.Configuration;

public sealed class AgentConfigurationBuilder
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "backend-collect-timeout",
        "ca-cert-file",
        "client-cert-file",
        "client-cert-password",
        "compression",
        "debug",
        "delaytime",
        "force",
        "lazy",
        "local",
        "logfile",
        "logger",
        "max-concurrency",
        "no-category",
        "no-compression",
        "no-ssl-check",
        "password",
        "proxy",
        "proxy-password",
        "proxy-user",
        "scan-processes",
        "scan-profiles",
        "server",
        "tag",
        "timeout",
        "user",
        "vardir",
    };

    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "client-cert-password",
        "password",
        "proxy-password",
    };

    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["backend-collect-timeout"] = "180",
        ["compression"] = "auto",
        ["debug"] = "false",
        ["delaytime"] = "3600",
        ["force"] = "false",
        ["lazy"] = "false",
        ["logger"] = "console",
        ["max-concurrency"] = "4",
        ["no-ssl-check"] = "false",
        ["scan-processes"] = "false",
        ["scan-profiles"] = "false",
        ["timeout"] = "180",
    };

    private readonly IAgentConfigLoader _loader;

    public AgentConfigurationBuilder(IAgentConfigLoader? loader = null)
    {
        _loader = loader ?? new AgentConfigLoader();
    }

    public EffectiveAgentConfiguration Build(
        string? configurationFile = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyDictionary<string, string?>? commandLine = null,
        string? trustedConfigurationRoot = null)
    {
        var values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        var origins = Defaults.Keys.ToDictionary(
            static key => key,
            static _ => new ConfigurationOrigin(ConfigurationSource.Default),
            StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(configurationFile))
        {
            foreach (AgentConfigEntry entry in _loader.Load(configurationFile, trustedConfigurationRoot))
            {
                Apply(values, origins, warnings, entry.Key, entry.Value, new ConfigurationOrigin(
                    ConfigurationSource.File,
                    entry.FilePath,
                    entry.LineNumber));
            }
        }

        foreach ((string key, string? value) in environment ?? ReadProcessEnvironment())
        {
            if (value is null)
            {
                continue;
            }

            string normalizedKey = NormalizeEnvironmentKey(key);
            if (normalizedKey.Length == 0)
            {
                continue;
            }

            Apply(values, origins, warnings, normalizedKey, value, new ConfigurationOrigin(
                ConfigurationSource.Environment,
                key));
        }

        if (commandLine is not null)
        {
            foreach ((string key, string? value) in commandLine)
            {
                if (value is not null)
                {
                    Apply(values, origins, warnings, NormalizeKey(key), value, new ConfigurationOrigin(ConfigurationSource.CommandLine));
                }
            }
        }

        AgentOptions options = CreateOptions(values);
        warnings.AddRange(AgentConfigurationValidator.Validate(options));

        IReadOnlyDictionary<string, string> redacted = values.ToDictionary(
            static pair => pair.Key,
            pair => SecretKeys.Contains(pair.Key) && pair.Value.Length > 0 ? "<redacted>" : pair.Value,
            StringComparer.OrdinalIgnoreCase);

        return new EffectiveAgentConfiguration(options, origins, warnings.Distinct(StringComparer.Ordinal).ToArray(), redacted);
    }

    private static void Apply(
        Dictionary<string, string> values,
        Dictionary<string, ConfigurationOrigin> origins,
        List<string> warnings,
        string key,
        string value,
        ConfigurationOrigin origin)
    {
        string normalizedKey = NormalizeKey(key);
        if (!KnownKeys.Contains(normalizedKey))
        {
            warnings.Add($"Unknown configuration key '{normalizedKey}'.");
            return;
        }

        if (normalizedKey.Equals("no-category", StringComparison.OrdinalIgnoreCase)
            && values.TryGetValue(normalizedKey, out string? existing)
            && origin.Source == ConfigurationSource.File)
        {
            values[normalizedKey] = string.Join(',', existing, value).TrimStart(',');
        }
        else
        {
            values[normalizedKey] = value.Trim();
        }

        origins[normalizedKey] = origin;
    }

    private static AgentOptions CreateOptions(IReadOnlyDictionary<string, string> values)
    {
        return new AgentOptions
        {
            Server = ParseOptionalUri(values, "server"),
            LocalPath = GetOptional(values, "local"),
            Tag = GetOptional(values, "tag"),
            Delay = TimeSpan.FromSeconds(ParseInt(values, "delaytime", 3600)),
            Lazy = ParseBoolean(values, "lazy"),
            Force = ParseBoolean(values, "force"),
            CollectorTimeout = TimeSpan.FromSeconds(ParseInt(values, "backend-collect-timeout", 180)),
            HttpTimeout = TimeSpan.FromSeconds(ParseInt(values, "timeout", 180)),
            ExcludedCategories = ParseSet(values, "no-category"),
            ScanProcesses = ParseBoolean(values, "scan-processes"),
            ScanProfiles = ParseBoolean(values, "scan-profiles"),
            StateDirectory = GetOptional(values, "vardir"),
            UserName = GetOptional(values, "user"),
            Password = GetOptional(values, "password"),
            Proxy = ParseOptionalUri(values, "proxy"),
            ProxyUserName = GetOptional(values, "proxy-user"),
            ProxyPassword = GetOptional(values, "proxy-password"),
            AllowInsecureTls = ParseBoolean(values, "no-ssl-check"),
            CaCertificateFile = GetOptional(values, "ca-cert-file"),
            ClientCertificateFile = GetOptional(values, "client-cert-file"),
            ClientCertificatePassword = GetOptional(values, "client-cert-password"),
            Compression = ParseCompression(values),
            Logger = ParseLogger(values),
            LogFile = GetOptional(values, "logfile"),
            Debug = ParseBoolean(values, "debug"),
            MaxConcurrency = ParseInt(values, "max-concurrency", 4),
        };
    }

    private static Dictionary<string, string?> ReadProcessEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string? key = entry.Key as string;
            if (key?.StartsWith("GLPI_AGENT_", StringComparison.OrdinalIgnoreCase) == true)
            {
                result[key] = entry.Value?.ToString();
            }
        }

        return result;
    }

    private static string NormalizeEnvironmentKey(string key)
    {
        const string prefix = "GLPI_AGENT_";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return NormalizeKey(key[prefix.Length..].Replace('_', '-'));
    }

    private static string NormalizeKey(string key) => key.Trim().TrimStart('-').ToLowerInvariant();

    private static string? GetOptional(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string key, int defaultValue)
    {
        return values.TryGetValue(key, out string? value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : defaultValue;
    }

    private static bool ParseBoolean(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            && value.Trim().ToUpperInvariant() is "1" or "ON" or "TRUE" or "YES";
    }

    private static Uri? ParseOptionalUri(IReadOnlyDictionary<string, string> values, string key)
    {
        string? value = GetOptional(values, key);
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new AgentConfigurationException($"Configuration key '{key}' must contain an absolute URI.");
        }

        return uri;
    }

    private static HashSet<string> ParseSet(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static CompressionKind ParseCompression(IReadOnlyDictionary<string, string> values)
    {
        if (ParseBoolean(values, "no-compression"))
        {
            return CompressionKind.None;
        }

        return GetOptional(values, "compression")?.ToUpperInvariant() switch
        {
            "GZIP" => CompressionKind.Gzip,
            "NONE" => CompressionKind.None,
            "ZLIB" => CompressionKind.Zlib,
            _ => CompressionKind.Auto,
        };
    }

    private static LogProvider ParseLogger(IReadOnlyDictionary<string, string> values)
    {
        return GetOptional(values, "logger")?.ToUpperInvariant() switch
        {
            "EVENTLOG" or "EVENT-LOG" => LogProvider.EventLog,
            "FILE" => LogProvider.File,
            _ => LogProvider.Console,
        };
    }
}
