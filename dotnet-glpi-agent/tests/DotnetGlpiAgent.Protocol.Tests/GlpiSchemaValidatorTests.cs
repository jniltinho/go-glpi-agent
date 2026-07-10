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

    [Theory]
    [InlineData("glpi10")]
    [InlineData("glpi11")]
    public async Task ValidateAsync_FixtureInventoryPassesPinnedGlpiSchemas(string glpiVersion)
    {
        // GLPI_INVENTORY_SCHEMA overrides the committed container-extracted schemas.
        string? schemaPath = Environment.GetEnvironmentVariable("GLPI_INVENTORY_SCHEMA");
        if (string.IsNullOrWhiteSpace(schemaPath) || !File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(FindProjectRoot(), "test", "glpi", "artifacts", glpiVersion, "inventory.schema.json");
        }

        Assert.True(File.Exists(schemaPath), $"Missing pinned GLPI schema: {schemaPath}");

        byte[] inventory = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "native-inventory.json"));
        IReadOnlyList<string> result = await GlpiSchemaValidator.ValidateAsync(
            inventory,
            schemaPath,
            CancellationToken.None);

        Assert.True(result.Count == 0, string.Join(Environment.NewLine, result));
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotnetGlpiAgent.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the .NET project root.");
    }
}
