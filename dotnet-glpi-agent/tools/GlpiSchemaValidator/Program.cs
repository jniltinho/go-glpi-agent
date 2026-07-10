using NJsonSchema;
using NJsonSchema.Validation;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: glpi-schema-validator SCHEMA INVENTORY");
    return 2;
}

try
{
    JsonSchema schema = await JsonSchema.FromFileAsync(Path.GetFullPath(args[0]));
    string inventory = await File.ReadAllTextAsync(Path.GetFullPath(args[1]));
    ICollection<ValidationError> errors = schema.Validate(inventory);
    string[] messages = errors
        .SelectMany(Flatten)
        .Select(static error => $"{NormalizePath(error.Path)}: {error.Kind}")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    foreach (string message in messages)
    {
        Console.Error.WriteLine(message);
    }

    if (messages.Length > 0)
    {
        return 1;
    }

    Console.WriteLine("Inventory validates against the supplied GLPI schema.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static IEnumerable<ValidationError> Flatten(ValidationError error)
{
    IEnumerable<ValidationError> children = error switch
    {
        ChildSchemaValidationError child => child.Errors.Values.SelectMany(static values => values),
        MultiTypeValidationError multiType => multiType.Errors.Values.SelectMany(static values => values),
        _ => [],
    };
    ValidationError[] nested = children.SelectMany(Flatten).ToArray();
    return nested.Length == 0 ? [error] : nested;
}

static string NormalizePath(string? path)
{
    return string.IsNullOrWhiteSpace(path) ? "$" : $"$.{path.TrimStart('#', '.', '/').Replace('/', '.')}";
}
