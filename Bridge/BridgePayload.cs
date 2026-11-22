using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameLauncher.Bridge;

/// <summary>
/// Small, explicit accessors for the <c>payload</c> object of a bridge request.
/// <para>
/// Handlers never deserialise a whole request into a mutable DTO and then trust
/// it: they read the fields they own and validate them individually.
/// </para>
/// </summary>
public static class BridgePayload
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static JsonElement? OptionalProperty(this JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return payload.TryGetProperty(name, out JsonElement value) ? value : null;
    }

    public static string? GetString(this JsonElement payload, string name)
    {
        JsonElement? property = payload.OptionalProperty(name);
        return property is { } value && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static Guid? GetGuid(this JsonElement payload, string name)
    {
        string? raw = payload.GetString(name);
        return Guid.TryParse(raw, out Guid parsed) ? parsed : null;
    }

    public static int GetInt32(this JsonElement payload, string name, int fallback)
    {
        JsonElement? property = payload.OptionalProperty(name);
        return property is { } value && value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out int parsed) ? parsed : fallback
            : fallback;
    }

    public static bool GetBoolean(this JsonElement payload, string name, bool fallback)
    {
        JsonElement? property = payload.OptionalProperty(name);
        return property is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback
            }
            : fallback;
    }

    /// <summary>
    /// Deserialises a nested payload object into <typeparamref name="T"/>. Returns
    /// <c>null</c> when it is absent or not an object.
    /// </summary>
    public static T? GetObject<T>(this JsonElement payload, string name)
        where T : class
    {
        JsonElement? property = payload.OptionalProperty(name);
        if (property is not { } value || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return value.Deserialize<T>(WebOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a whole <see cref="GameItem"/>-shaped payload object into a model.
    /// Unknown or malformed fields are ignored; the caller validates the result.
    /// </summary>
    public static Models.GameItem? GetGame(this JsonElement payload, string name = "game")
    {
        return payload.GetObject<Models.GameItem>(name);
    }
}
