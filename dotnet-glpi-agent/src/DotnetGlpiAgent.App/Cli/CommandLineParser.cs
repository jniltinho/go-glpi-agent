namespace DotnetGlpiAgent.App.Cli;

public sealed record ParsedCommandLine(
    string Command,
    string? ConfigurationFile,
    IReadOnlyDictionary<string, string?> Values,
    bool ShowHelp);

public sealed class CommandLineException : Exception
{
    public CommandLineException(string message)
        : base(message)
    {
    }
}

public static class CommandLineParser
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "help",
        "run",
        "validate-config",
        "version",
    };

    private static readonly HashSet<string> BooleanOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "debug",
        "force",
        "lazy",
        "no-compression",
        "no-ssl-check",
        "scan-processes",
        "scan-profiles",
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "backend-collect-timeout",
        "ca-cert-file",
        "client-cert-file",
        "client-cert-password",
        "compression",
        "delaytime",
        "local",
        "logfile",
        "logger",
        "max-concurrency",
        "no-category",
        "password",
        "proxy",
        "proxy-password",
        "proxy-user",
        "server",
        "tag",
        "timeout",
        "user",
        "vardir",
    };

    public static ParsedCommandLine Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 1 && arguments[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCommandLine("version", null, new Dictionary<string, string?>(), false);
        }

        string command = arguments.Count > 0 && !arguments[0].StartsWith('-')
            ? arguments[0]
            : string.Empty;
        if (command.Length > 0 && !Commands.Contains(command))
        {
            throw new CommandLineException($"Unknown command '{command}'.");
        }

        int index = command.Length == 0 ? 0 : 1;
        string? configurationFile = null;
        bool showHelp = command.Equals("help", StringComparison.OrdinalIgnoreCase);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        while (index < arguments.Count)
        {
            string argument = arguments[index++];
            if (argument is "--help" or "-h")
            {
                showHelp = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CommandLineException($"Unexpected argument '{argument}'.");
            }

            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            string name = (equals < 0 ? argument[2..] : argument[2..equals]).ToLowerInvariant();
            string? inlineValue = equals < 0 ? null : argument[(equals + 1)..];
            if (name == "config")
            {
                configurationFile = inlineValue ?? ReadValue(arguments, ref index, name);
                continue;
            }

            if (BooleanOptions.Contains(name))
            {
                values[name] = inlineValue ?? "true";
                continue;
            }

            if (!ValueOptions.Contains(name))
            {
                throw new CommandLineException($"Unknown option '--{name}'.");
            }

            string value = inlineValue ?? ReadValue(arguments, ref index, name);
            if (name == "no-category" && values.TryGetValue(name, out string? previous))
            {
                values[name] = string.Join(',', previous, value);
            }
            else
            {
                values[name] = value;
            }
        }

        return new ParsedCommandLine(command, configurationFile, values, showHelp);
    }

    private static string ReadValue(IReadOnlyList<string> arguments, ref int index, string option)
    {
        if (index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CommandLineException($"Option '--{option}' requires a value.");
        }

        return arguments[index++];
    }
}
