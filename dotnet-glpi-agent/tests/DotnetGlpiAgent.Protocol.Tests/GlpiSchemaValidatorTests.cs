using System.Text;

namespace DotnetGlpiAgent.Protocol.Tests;

public sealed class GlpiSchemaValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ReportsExactInvalidInstancePath()
    {
        string schemaPath = Path.Combine(Path.GetTempPath(), $"glpi-schema-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                schemaPath,
                "{\"type\":\"object\",\"required\":[\"content\"],\"properties\":{\"content\":{\"type\":\"object\",\"properties\":{\"cpus\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"core\":{\"type\":\"integer\"}}}}}}}}",
                CancellationToken.None);

            IReadOnlyList<string> result = await GlpiSchemaValidator.ValidateAsync(
                Encoding.UTF8.GetBytes("{\"content\":{\"cpus\":[{\"core\":\"four\"}]}}"),
                schemaPath,
                CancellationToken.None);

            string error = Assert.Single(result);
            Assert.Contains("content.cpus[0].core", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IntegerExpected", error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_UsesExternallySuppliedGlpiSchemaWhenAvailable()
    {
        string? schemaPath = Environment.GetEnvironmentVariable("GLPI_INVENTORY_SCHEMA");
        if (string.IsNullOrWhiteSpace(schemaPath) || !File.Exists(schemaPath))
        {
            return;
        }

        byte[] inventory = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-inventory.json"));
        IReadOnlyList<string> result = await GlpiSchemaValidator.ValidateAsync(
            inventory,
            schemaPath,
            CancellationToken.None);

        Assert.True(result.Count == 0, string.Join(Environment.NewLine, result));
    }
}
