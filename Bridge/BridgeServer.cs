using System.Text.Json;

namespace GameLauncher.Bridge;

/// <summary>
/// One backend operation the frontend may ask for.
/// </summary>
public interface IBridgeHandler
{
    /// <summary>The bridge message type this handler owns.</summary>
    string Type { get; }

    /// <summary>
    /// Runs the operation. Implementations return a serialisable payload or
    /// <c>null</c>. Expected failures are thrown as
    /// <see cref="BridgeHandlerException"/> so the server can report them without
    /// leaking exception details.
    /// </summary>
    Task<object?> HandleAsync(BridgeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A failure the frontend is meant to see. <see cref="Message"/> must be safe to
/// display and must never contain a stack trace.
/// </summary>
public sealed class BridgeHandlerException : Exception
{
    public BridgeHandlerException(string message) : base(message)
    {
    }
}

/// <summary>
/// Receives messages from the web frontend, validates them, dispatches them to the
/// one registered handler and posts the correlated response back.
/// <para>
/// Security properties:
/// <list type="bullet">
/// <item>The message must parse, carry the expected bridge version and the trusted
/// source marker, and have a non-empty correlation id.</item>
/// <item>Only types in <see cref="BridgeContract.RequestTypes"/> reach a handler -
/// there is no dispatch by method name and no reflection.</item>
/// <item>The origin is checked against the host the frontend is served from.</item>
/// <item>Handler exceptions become a generic error response; the stack trace is
/// dropped.</item>
/// </list>
/// </para>
/// </summary>
public sealed class BridgeServer
{
    private static readonly JsonSerializerOptions HandlerJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, IBridgeHandler> _handlers;
    private readonly Action<string> _postMessage;
    private readonly string _expectedOrigin;

    public BridgeServer(
        IEnumerable<IBridgeHandler> handlers,
        Action<string> postMessage,
        string expectedOrigin)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(postMessage);

        _handlers = handlers
            .Where(handler => BridgeContract.IsKnownRequestType(handler.Type))
            .ToDictionary(handler => handler.Type, StringComparer.Ordinal);

        _postMessage = postMessage;
        _expectedOrigin = expectedOrigin;
    }

    /// <summary>Types with a registered handler. Used by tests and diagnostics.</summary>
    public IReadOnlyCollection<string> RegisteredTypes => _handlers.Keys.ToArray();

    /// <summary>
    /// Handles one raw web message. Never throws: every failure path produces a
    /// response the frontend can render.
    /// </summary>
    public void HandleRawMessage(string? json, string? origin)
    {
        if (!string.IsNullOrEmpty(_expectedOrigin)
            && !string.Equals(origin, _expectedOrigin, StringComparison.OrdinalIgnoreCase))
        {
            Post(new BridgeResponse(
                "bridge.error",
                "unknown",
                Success: false,
                Payload: null,
                "The message did not originate from the trusted application host."));
            return;
        }

        if (!BridgeMessageParser.TryParse(json, out BridgeRequest? request, out string error))
        {
            Post(new BridgeResponse("bridge.error", "unknown", Success: false, Payload: null, error));
            return;
        }

        _ = Task.Run(() => DispatchAsync(request!));
    }

    /// <summary>Pushes a backend-initiated event to the frontend.</summary>
    public void PostEvent(string type, object? payload)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        try
        {
            var bridgeEvent = new BridgeEvent(type, ToElement(payload ?? new { }));
            PostRaw(BridgeMessageParser.SerializeEvent(bridgeEvent));
        }
        catch (Exception ex) when (ex is JsonException
                                  or NotSupportedException
                                  or InvalidOperationException)
        {
            // An unserialisable event must never take down the host.
        }
    }

    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private async Task DispatchAsync(BridgeRequest request)
    {
        // Serialize bridge transactions, not just individual file writes. Otherwise
        // two concurrent read-modify-save operations could lose a library update.
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try { await DispatchCoreAsync(request).ConfigureAwait(false); }
        finally { _operationGate.Release(); }
    }

    private async Task DispatchCoreAsync(BridgeRequest request)
    {
        string responseType = request.Type + ".result";

        try
        {
            if (!_handlers.TryGetValue(request.Type, out IBridgeHandler? handler))
            {
                Post(new BridgeResponse(
                    responseType,
                    request.RequestId,
                    Success: false,
                    Payload: null,
                    $"No handler is registered for \"{request.Type}\"."));
                return;
            }

            object? payload = await handler.HandleAsync(request, CancellationToken.None)
                                         .ConfigureAwait(false);

            Post(new BridgeResponse(
                responseType,
                request.RequestId,
                Success: true,
                ToElementOrNull(payload),
                null));
        }
        catch (BridgeHandlerException ex)
        {
            Post(new BridgeResponse(responseType, request.RequestId, Success: false, Payload: null, ex.Message));
        }
        catch (OperationCanceledException)
        {
            Post(new BridgeResponse(
                responseType,
                request.RequestId,
                Success: false,
                Payload: null,
                "The operation was cancelled."));
        }
        catch (Exception ex)
        {
            // Deliberately no stack trace and no exception type: the frontend only
            // gets a safe sentence.
            Post(new BridgeResponse(
                responseType,
                request.RequestId,
                Success: false,
                Payload: null,
                $"The request could not be completed: {ex.Message}"));
        }
    }

    private void Post(BridgeResponse response)
    {
        PostRaw(BridgeMessageParser.SerializeResponse(response));
    }

    private void PostRaw(string json)
    {
        try
        {
            _postMessage(json);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The web view is going away; nothing to deliver to.
        }
    }

    private static JsonElement? ToElementOrNull(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (payload is JsonElement element)
        {
            return element;
        }

        return JsonSerializer.SerializeToElement(payload, payload.GetType(), HandlerJsonOptions);
    }

    private static JsonElement ToElement(object? payload)
    {
        return ToElementOrNull(payload) ?? JsonSerializer.SerializeToElement(new { }, HandlerJsonOptions);
    }
}
