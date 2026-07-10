using NJsonSchema;
using NJsonSchema.Validation;

namespace DotnetGlpiAgent.Protocol.Tests;

public static class GlpiSchemaValidator
{
    public static async ValueTask<IReadOnlyList<string>> ValidateAsync(
        ReadOnlyMemory<byte> inventory,
        string schemaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaPath);
        string schemaJson = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        JsonSchema schema = await JsonSchema.FromJsonAsync(schemaJson, cancellationToken: cancellationToken);
        string inventoryJson = System.Text.Encoding.UTF8.GetString(inventory.Span);
        ICollection<ValidationError> errors = schema.Validate(inventoryJson);
        return errors
            .SelectMany(Flatten)
            .Select(static error => $"{NormalizePath(error.Path)}: {error.Kind}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<ValidationError> Flatten(ValidationError error)
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

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "$" : $"$.{path.TrimStart('#', '.', '/').Replace('/', '.')}";
    }
}
