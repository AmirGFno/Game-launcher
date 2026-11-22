using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameLauncher.Bridge;

/// <summary>
/// The single, explicit list of message types the desktop host accepts.
/// <para>
/// This is a closed registry, not a dispatch-by-name scheme: a message whose
/// <c>type</c> is not listed here is rejected before any handler runs, so the web
/// layer can never invoke an arbitrary method or reach a service by name.
/// </para>
/// </summary>
public static class BridgeContract
{
    /// <summary>Bumped whenever the envelope or a payload shape changes incompatibly.</summary>
    public const int Version = 1;

    /// <summary>Value the trusted frontend puts in <c>source</c>.</summary>
    public const string TrustedSource = "gfno-app";

    // ---- Library ----------------------------------------------------------
    public const string LibraryGet = "library.get";
    public const string LibraryCreate = "library.create";
    public const string LibraryUpdate = "library.update";
    public const string LibraryDelete = "library.delete";

    // ---- Game actions -----------------------------------------------------
    public const string GameLaunch = "game.launch";
    public const string GameOpenFolder = "game.openFolder";
    public const string GameDetect = "game.detect";

    // ---- Native dialogs ---------------------------------------------------
    public const string DialogOpenFile = "dialog.openFile";
    public const string DialogOpenFolder = "dialog.openFolder";

    // ---- Artwork ----------------------------------------------------------
    public const string ImageDownload = "image.download";
    public const string ImageThumbnail = "image.thumbnail";
    public const string ImageDominantColor = "image.dominantColor";

    // ---- Settings ---------------------------------------------------------
    public const string SettingsGet = "settings.get";
    public const string SettingsSave = "settings.save";

    // ---- Performance Center ----------------------------------------------
    public const string PerformanceStatus = "performance.status";
    public const string PerformanceStart = "performance.start";
    public const string PerformanceStop = "performance.stop";
    public const string PerformanceRecommendations = "performance.recommendations";

    // ---- Cache ------------------------------------------------------------
    public const string CachePreview = "cache.preview";
    public const string CacheClean = "cache.clean";

    // ---- Window -----------------------------------------------------------
    public const string WindowMinimize = "window.minimize";
    public const string WindowMaximize = "window.restore";
    public const string WindowClose = "window.close";
    public const string WindowSetFullscreen = "window.setFullscreen";

    // ---- Application ------------------------------------------------------
    public const string SystemOpenPath = "system.openPath";
    public const string AppVersion = "app.version";
    public const string AppRuntimeStatus = "app.runtimeStatus";

    /// <summary>Every type a frontend may send. Order is irrelevant.</summary>
    public static readonly IReadOnlySet<string> RequestTypes = new HashSet<string>(
        StringComparer.Ordinal)
    {
        LibraryGet, LibraryCreate, LibraryUpdate, LibraryDelete,
        GameLaunch, GameOpenFolder, GameDetect,
        DialogOpenFile, DialogOpenFolder,
        ImageDownload, ImageThumbnail, ImageDominantColor,
        SettingsGet, SettingsSave,
        PerformanceStatus, PerformanceStart, PerformanceStop, PerformanceRecommendations,
        CachePreview, CacheClean,
        WindowMinimize, WindowMaximize, WindowClose, WindowSetFullscreen,
        SystemOpenPath, AppVersion, AppRuntimeStatus
    };

    /// <summary>
    /// True when <paramref name="type"/> is a registered request type. Anything
    /// else is rejected, which is what makes the bridge a closed surface.
    /// </summary>
    public static bool IsKnownRequestType(string? type)
        => !string.IsNullOrWhiteSpace(type) && RequestTypes.Contains(type!);
}

/// <summary>
/// A request that arrived over <c>WebMessageReceived</c>, already validated.
/// </summary>
/// <param name="Type">One of <see cref="BridgeContract.RequestTypes"/>.</param>
/// <param name="RequestId">Opaque correlation id supplied by the frontend.</param>
/// <param name="Payload">Untyped payload; each handler deserialises what it needs.</param>
public sealed record BridgeRequest(string Type, string RequestId, JsonElement Payload);

/// <summary>
/// A response posted back to the frontend.
/// </summary>
public sealed record BridgeResponse(
    string Type,
    string RequestId,
    bool Success,
    JsonElement? Payload,
    string? Error);

/// <summary>
/// An event the backend pushes to the frontend without a prior request.
/// </summary>
public sealed record BridgeEvent(string Type, JsonElement Payload);

/// <summary>
/// Parses and validates the incoming message envelope.
/// <para>
/// The parser is deliberately strict: malformed JSON, a missing correlation id, a
/// version mismatch or an unknown <c>type</c> are all rejected with a reason the
/// host can surface. It never throws for bad input.
/// </para>
/// </summary>
public static class BridgeMessageParser
{
    /// <summary>
    /// Parses a raw web message. Returns <c>false</c> and a human-readable reason
    /// when the message is not a valid bridge request.
    /// </summary>
    public static bool TryParse(string? json, out BridgeRequest? request, out string error)
    {
        request = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The message was empty.";
            return false;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            error = "The message is not valid JSON.";
            return false;
        }

        if (node is not JsonObject obj)
        {
            error = "The message must be a JSON object.";
            return false;
        }

        if (!obj.TryGetPropertyValue("version", out JsonNode? versionNode)
            || versionNode is not JsonValue versionValue
            || !versionValue.TryGetValue<int>(out int version)
            || version != BridgeContract.Version)
        {
            error = $"Unsupported bridge version. Expected {BridgeContract.Version}.";
            return false;
        }

        if (!obj.TryGetPropertyValue("source", out JsonNode? sourceNode)
            || sourceNode is not JsonValue sourceValue
            || !sourceValue.TryGetValue<string>(out string? source)
            || source != BridgeContract.TrustedSource)
        {
            error = "The message did not come from the trusted application frontend.";
            return false;
        }

        if (!obj.TryGetPropertyValue("type", out JsonNode? typeNode)
            || typeNode is not JsonValue typeValue
            || !typeValue.TryGetValue<string>(out string? type))
        {
            error = "The message has no type.";
            return false;
        }

        if (!BridgeContract.IsKnownRequestType(type))
        {
            error = $"Unknown message type: {type ?? "(empty)"}";
            return false;
        }

        if (!obj.TryGetPropertyValue("requestId", out JsonNode? idNode)
            || idNode is not JsonValue idValue
            || !idValue.TryGetValue<string>(out string? requestId)
            || string.IsNullOrWhiteSpace(requestId))
        {
            error = "The message has no requestId.";
            return false;
        }


        JsonElement payload = default;
        if (obj.TryGetPropertyValue("payload", out JsonNode? payloadNode) && payloadNode is not null)
        {
            payload = JsonSerializer.SerializeToElement(payloadNode);
        }

        request = new BridgeRequest(type!, requestId, payload);
        return true;
    }

    /// <summary>Serialises a response envelope.</summary>
    public static string SerializeResponse(BridgeResponse response)
    {
        var obj = new JsonObject
        {
            ["version"] = BridgeContract.Version,
            ["source"] = "gfno-host",
            ["type"] = response.Type,
            ["requestId"] = response.RequestId,
            ["success"] = response.Success
        };

        if (response.Payload is { } payload)
        {
            obj["payload"] = JsonNode.Parse(payload.GetRawText());
        }

        if (!string.IsNullOrEmpty(response.Error))
        {
            obj["error"] = response.Error;
        }

        return obj.ToJsonString(JsonDefaults);
    }

    /// <summary>Serialises a backend-initiated event envelope.</summary>
    public static string SerializeEvent(BridgeEvent bridgeEvent)
    {
        var obj = new JsonObject
        {
            ["version"] = BridgeContract.Version,
            ["source"] = "gfno-host",
            ["type"] = bridgeEvent.Type
        };

        obj["payload"] = JsonNode.Parse(bridgeEvent.Payload.GetRawText());

        return obj.ToJsonString(JsonDefaults);
    }

    private static readonly JsonSerializerOptions JsonDefaults = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };
}
