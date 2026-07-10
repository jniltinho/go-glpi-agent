using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Mapping;

namespace DotnetGlpiAgent.Protocol.Serialization;

public static class NativeJsonSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static byte[] SerializeInventory(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProtocolContent content = InventoryContentMapper.Map(snapshot);
        var message = new NativeInventoryMessage(
            snapshot.Identity.DeviceId,
            "inventory",
            "Computer",
            content.Values,
            snapshot.Account.Tag);
        return JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
    }

    public static byte[] SerializeContact(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.SerializeToUtf8Bytes(
            new ContactMessage(
                snapshot.Identity.DeviceId,
                "contact",
                snapshot.Identity.DeviceName,
                snapshot.Account.Tag,
                snapshot.Identity.AgentVersion,
                ["inventory"],
                ["inventory"]),
            SerializerOptions);
    }

    public static ContactResponse ParseContactResponse(ReadOnlySpan<byte> response)
    {
        ContactResponse? result = JsonSerializer.Deserialize<ContactResponse>(response, SerializerOptions);
        return result ?? throw new JsonException("The CONTACT response was empty.");
    }

    private sealed record NativeInventoryMessage(
        [property: JsonPropertyName("deviceid"), JsonPropertyOrder(0)] string DeviceId,
        [property: JsonPropertyName("action"), JsonPropertyOrder(1)] string Action,
        [property: JsonPropertyName("itemtype"), JsonPropertyOrder(2)] string ItemType,
        [property: JsonPropertyName("content"), JsonPropertyOrder(3)] IReadOnlyDictionary<string, object> Content,
        [property: JsonPropertyName("tag"), JsonPropertyOrder(4)] string? Tag);

    // GLPI 11 rejects a CONTACT without "version" (400 "JSON not well formed!"),
    // so it is always emitted alongside "name".
    private sealed record ContactMessage(
        [property: JsonPropertyName("deviceid"), JsonPropertyOrder(0)] string DeviceId,
        [property: JsonPropertyName("action"), JsonPropertyOrder(1)] string Action,
        [property: JsonPropertyName("name"), JsonPropertyOrder(2)] string Name,
        [property: JsonPropertyName("tag"), JsonPropertyOrder(3)] string? Tag,
        [property: JsonPropertyName("version"), JsonPropertyOrder(4)] string Version,
        [property: JsonPropertyName("installed-tasks"), JsonPropertyOrder(5)] IReadOnlyList<string> InstalledTasks,
        [property: JsonPropertyName("enabled-tasks"), JsonPropertyOrder(6)] IReadOnlyList<string> EnabledTasks);
}

// Expiration is either a duration string (e.g. "24" hours) or an absolute timestamp.
public sealed record ContactResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("expiration")] JsonElement Expiration,
    [property: JsonPropertyName("tasks")] JsonElement Tasks,
    [property: JsonPropertyName("disabled")] JsonElement Disabled)
{
    public bool RequestsInventory()
    {
        if (Tasks.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        // Native answers use either ["inventory"] or {"inventory":{...}}.
        return Tasks.ToString().Contains("inventory", StringComparison.OrdinalIgnoreCase);
    }
}
