namespace DotnetGlpiAgent.Core.Configuration;

public static class AgentConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        if (options.Delay < TimeSpan.Zero)
        {
            throw new AgentConfigurationException("delaytime must not be negative.");
        }

        if (options.CollectorTimeout <= TimeSpan.Zero)
        {
            throw new AgentConfigurationException("backend-collect-timeout must be greater than zero.");
        }

        if (options.HttpTimeout <= TimeSpan.Zero)
        {
            throw new AgentConfigurationException("timeout must be greater than zero.");
        }

        if (options.MaxConcurrency is < 1 or > 32)
        {
            throw new AgentConfigurationException("max-concurrency must be between 1 and 32.");
        }

        ValidateHttpUri(options.Server, "server");
        ValidateHttpUri(options.Proxy, "proxy");

        if (options.Logger == LogProvider.File && string.IsNullOrWhiteSpace(options.LogFile))
        {
            throw new AgentConfigurationException("logfile is required when logger is File.");
        }

        if (options.AllowInsecureTls)
        {
            warnings.Add("TLS certificate validation is explicitly disabled.");
        }

        return warnings;
    }

    private static void ValidateHttpUri(Uri? uri, string key)
    {
        if (uri is not null && uri.Scheme is not ("http" or "https"))
        {
            throw new AgentConfigurationException($"Configuration key '{key}' must use HTTP or HTTPS.");
        }
    }
}
