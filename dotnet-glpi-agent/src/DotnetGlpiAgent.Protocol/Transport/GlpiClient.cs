using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Serialization;

namespace DotnetGlpiAgent.Protocol.Transport;

public sealed class GlpiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly GlpiProtocolOptions _options;
    private readonly bool _ownsClient;
    private bool _disposed;

    public GlpiClient(HttpClient httpClient, GlpiProtocolOptions options, bool ownsClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsClient = ownsClient;
        ValidateOptions(options);
    }

    public async ValueTask<GlpiSubmissionResult> SubmitAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIdentity(snapshot);

        ProtocolResponse contact = await SendControlWithRetryAsync(
            () => CreatePostRequest(
                NativeJsonSerializer.SerializeContact(snapshot),
                snapshot,
                snapshot.Collection.CorrelationId,
                null,
                _options.Compression),
            cancellationToken).ConfigureAwait(false);
        contact = await ResolvePendingAsync(contact, snapshot, cancellationToken).ConfigureAwait(false);

        if (IsExplicitLegacyResponse(contact))
        {
            return await SubmitLegacyAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        EnsureSuccess(contact, "CONTACT");
        ContactResponse contactMessage;
        try
        {
            contactMessage = NativeJsonSerializer.ParseContactResponse(contact.Body);
        }
        catch (JsonException exception)
        {
            throw new GlpiProtocolException(
                GlpiFailureKind.Protocol,
                "contact-invalid-json",
                "The GLPI CONTACT response was not a valid native protocol message.",
                exception);
        }

        if (IsContactError(contactMessage))
        {
            throw new GlpiProtocolException(
                GlpiFailureKind.Protocol,
                "contact-status-error",
                "The GLPI CONTACT response reported status=error.");
        }

        if (!contactMessage.RequestsInventory() && _options.Lazy && !_options.Force)
        {
            return new GlpiSubmissionResult(GlpiProtocolKind.NativeJson, SubmissionState.Skipped, (int)contact.StatusCode);
        }

        byte[] body = NativeJsonSerializer.SerializeInventory(snapshot);
        ProtocolResponse inventoryResponse;
        try
        {
            inventoryResponse = await SendOnceAsync(
                CreatePostRequest(
                    body,
                    snapshot,
                    snapshot.Collection.CorrelationId,
                    null,
                    _options.Compression),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GlpiSubmissionResult(
                GlpiProtocolKind.NativeJson,
                SubmissionState.Indeterminate,
                DiagnosticCode: "inventory-upload-timeout");
        }
        catch (HttpRequestException)
        {
            return new GlpiSubmissionResult(
                GlpiProtocolKind.NativeJson,
                SubmissionState.Indeterminate,
                DiagnosticCode: "inventory-upload-indeterminate");
        }

        inventoryResponse = await ResolvePendingAsync(inventoryResponse, snapshot, cancellationToken).ConfigureAwait(false);
        if (!inventoryResponse.IsSuccessStatusCode)
        {
            EnsureSuccess(inventoryResponse, "inventory");
        }

        ValidateNativeAnswer(inventoryResponse.Body);
        return new GlpiSubmissionResult(
            GlpiProtocolKind.NativeJson,
            SubmissionState.Accepted,
            (int)inventoryResponse.StatusCode);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async ValueTask<GlpiSubmissionResult> SubmitLegacyAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ProtocolResponse prolog = await SendControlWithRetryAsync(
            () => CreatePostRequest(
                LegacyXmlSerializer.SerializeProlog(snapshot.Identity.DeviceId),
                snapshot,
                snapshot.Collection.CorrelationId,
                null,
                CompressionKind.Zlib,
                legacy: true),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(prolog, "legacy PROLOG");

        ProtocolResponse inventory;
        try
        {
            inventory = await SendOnceAsync(
                CreatePostRequest(
                    LegacyXmlSerializer.SerializeInventory(snapshot),
                    snapshot,
                    snapshot.Collection.CorrelationId,
                    null,
                    CompressionKind.Zlib,
                    legacy: true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GlpiSubmissionResult(
                GlpiProtocolKind.LegacyXml,
                SubmissionState.Indeterminate,
                DiagnosticCode: "legacy-inventory-upload-timeout");
        }
        catch (HttpRequestException)
        {
            return new GlpiSubmissionResult(
                GlpiProtocolKind.LegacyXml,
                SubmissionState.Indeterminate,
                DiagnosticCode: "legacy-inventory-upload-indeterminate");
        }

        EnsureSuccess(inventory, "legacy inventory");
        return new GlpiSubmissionResult(
            GlpiProtocolKind.LegacyXml,
            SubmissionState.Accepted,
            (int)inventory.StatusCode);
    }

    private async ValueTask<ProtocolResponse> SendControlWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                ProtocolResponse response = await SendOnceAsync(requestFactory(), cancellationToken).ConfigureAwait(false);
                if (!IsRetryable(response.StatusCode) || attempt >= _options.MaximumControlRetries)
                {
                    return response;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.MaximumControlRetries)
                {
                    throw new GlpiProtocolException(
                        GlpiFailureKind.Timeout,
                        "control-request-timeout",
                        "A GLPI control request timed out.");
                }
            }
            catch (HttpRequestException exception)
            {
                if (attempt >= _options.MaximumControlRetries)
                {
                    throw ClassifyTransportException(exception);
                }
            }

            await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ProtocolResponse> ResolvePendingAsync(
        ProtocolResponse response,
        InventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        int attempts = 0;
        while (IsPending(response.Body, out DateTimeOffset? expiration))
        {
            if (string.IsNullOrWhiteSpace(response.RequestId))
            {
                throw new GlpiProtocolException(
                    GlpiFailureKind.Protocol,
                    "pending-request-id-missing",
                    "The server returned pending without a GLPI-Request-ID header.");
            }

            if (attempts >= _options.MaximumPollAttempts
                || DateTimeOffset.UtcNow - started >= _options.MaximumPollDuration
                || expiration is not null && DateTimeOffset.UtcNow >= expiration)
            {
                throw new GlpiProtocolException(
                    GlpiFailureKind.Timeout,
                    "pending-poll-limit",
                    "The pending GLPI request exceeded its polling limit.");
            }

            attempts++;
            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            response = await SendControlWithRetryAsync(
                () => CreateGetRequest(
                    snapshot,
                    snapshot.Collection.CorrelationId,
                    response.RequestId!),
                cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async ValueTask<ProtocolResponse> SendOnceAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false))
        {
            byte[] raw = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            byte[] body = Decompress(raw, response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentEncoding);
            string? requestId = response.Headers.TryGetValues("GLPI-Request-ID", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;
            return new ProtocolResponse(response.StatusCode, body, requestId);
        }
    }

    private HttpRequestMessage CreatePostRequest(
        byte[] body,
        InventorySnapshot snapshot,
        string correlationId,
        string? requestId,
        CompressionKind compression,
        bool legacy = false)
    {
        (byte[] payload, string contentType) = Compress(body, compression, legacy);
        var request = new HttpRequestMessage(HttpMethod.Post, _options.Server)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        AddHeaders(request, snapshot, correlationId, requestId);
        return request;
    }

    private HttpRequestMessage CreateGetRequest(
        InventorySnapshot snapshot,
        string correlationId,
        string requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.Server);
        AddHeaders(request, snapshot, correlationId, requestId);
        return request;
    }

    private void AddHeaders(
        HttpRequestMessage request,
        InventorySnapshot snapshot,
        string correlationId,
        string? requestId)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", snapshot.Identity.AgentVersion);
        request.Headers.Add("GLPI-Agent-ID", snapshot.Identity.AgentId.ToString("D", CultureInfo.InvariantCulture));
        request.Headers.Add("X-Correlation-ID", correlationId);
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.Add("GLPI-Request-ID", requestId);
        }

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.UserName}:{_options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    private static void ValidateOptions(GlpiProtocolOptions options)
    {
        if (options.Server.Scheme is not ("http" or "https"))
        {
            throw new GlpiProtocolException(GlpiFailureKind.Configuration, "server-uri-invalid", "The GLPI server must use HTTP or HTTPS.");
        }

        if (options.MaximumPollAttempts < 1
            || options.MaximumPollDuration <= TimeSpan.Zero
            || options.PollInterval < TimeSpan.Zero
            || options.MaximumControlRetries < 0
            || options.RetryDelay < TimeSpan.Zero)
        {
            throw new GlpiProtocolException(GlpiFailureKind.Configuration, "protocol-limits-invalid", "GLPI retry and polling limits are invalid.");
        }
    }

    private static void ValidateIdentity(InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Identity.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.Identity.DeviceId))
        {
            throw new GlpiProtocolException(
                GlpiFailureKind.Configuration,
                "identity-invalid",
                "A persistent agent ID and device ID are required before contacting GLPI.");
        }
    }

    private static bool IsExplicitLegacyResponse(ProtocolResponse response)
    {
        if (response.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.UnsupportedMediaType
            or HttpStatusCode.NotImplemented)
        {
            return true;
        }

        if (response.IsSuccessStatusCode && !LooksLikeJson(response.Body))
        {
            // A malformed success (proxy HTML page, empty body) must not downgrade;
            // only an actual legacy XML answer identifies a legacy endpoint.
            return Encoding.UTF8.GetString(response.Body).Contains("<REPLY", StringComparison.OrdinalIgnoreCase);
        }

        string body = Encoding.UTF8.GetString(response.Body);
        return response.StatusCode == HttpStatusCode.BadRequest
            && body.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            && body.Contains("protocol", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSuccess(ProtocolResponse response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        GlpiFailureKind kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => GlpiFailureKind.Authentication,
            HttpStatusCode.TooManyRequests => GlpiFailureKind.RateLimited,
            >= HttpStatusCode.InternalServerError => GlpiFailureKind.Server,
            _ => GlpiFailureKind.Protocol,
        };
        string code = kind switch
        {
            GlpiFailureKind.Authentication => "authentication-failed",
            GlpiFailureKind.RateLimited => "rate-limited",
            GlpiFailureKind.Server => "server-error",
            _ => "protocol-rejected",
        };
        throw new GlpiProtocolException(
            kind,
            code,
            $"GLPI rejected the {operation} request with HTTP {(int)response.StatusCode}.");
    }

    private static GlpiProtocolException ClassifyTransportException(HttpRequestException exception)
    {
        bool tls = exception.HttpRequestError is HttpRequestError.SecureConnectionError
            || exception.InnerException is AuthenticationException;
        return new GlpiProtocolException(
            tls ? GlpiFailureKind.Tls : GlpiFailureKind.Transport,
            tls ? "tls-failed" : "transport-failed",
            tls ? "TLS validation or negotiation with GLPI failed." : "The GLPI server could not be reached.",
            exception);
    }

    private static bool IsContactError(ContactResponse contact)
    {
        return contact.Status?.Equals("error", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void ValidateNativeAnswer(byte[] body)
    {
        if (body.Length == 0)
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string text = root.ToString();
            bool error = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("status", out JsonElement status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString()?.Equals("error", StringComparison.OrdinalIgnoreCase) == true;
            if (!error)
            {
                return;
            }

            bool schema = text.Contains("schema", StringComparison.OrdinalIgnoreCase)
                || text.Contains("validate", StringComparison.OrdinalIgnoreCase);
            throw new GlpiProtocolException(
                schema ? GlpiFailureKind.Schema : GlpiFailureKind.Protocol,
                schema ? "schema-rejected" : "server-answer-error",
                schema ? "GLPI rejected the inventory schema." : "GLPI returned a protocol error.");
        }
        catch (JsonException exception)
        {
            throw new GlpiProtocolException(
                GlpiFailureKind.Protocol,
                "server-answer-invalid-json",
                "GLPI returned an invalid native protocol answer.",
                exception);
        }
    }

    private static bool IsPending(byte[] body, out DateTimeOffset? expiration)
    {
        expiration = null;
        if (!LooksLikeJson(body))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out JsonElement status)
                || status.ValueKind != JsonValueKind.String
                || !string.Equals(status.GetString(), "pending", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (root.TryGetProperty("expiration", out JsonElement value))
            {
                expiration = ParseExpiration(value);
            }

            return true;
        }
    }

    // GLPI sends expiration as a delay in hours ("24" or 24), not a timestamp.
    private static DateTimeOffset? ParseExpiration(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double hours))
        {
            return DateTimeOffset.UtcNow.AddHours(hours);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hoursFromText))
            {
                return DateTimeOffset.UtcNow.AddHours(hoursFromText);
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool LooksLikeJson(byte[] body)
    {
        ReadOnlySpan<byte> value = body;
        while (value.Length > 0 && value[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            value = value[1..];
        }

        return value.Length > 0 && value[0] is (byte)'{' or (byte)'[';
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static (byte[] Body, string ContentType) Compress(
        byte[] body,
        CompressionKind compression,
        bool legacy)
    {
        // The native protocol defaults to uncompressed application/json: GLPI 11
        // does not inflate a zlib native body and answers 400 "JSON not well
        // formed!". zlib stays on the legacy XML/PROLOG flow; an explicit
        // Gzip/Zlib on the native path is still honored for servers that accept it.
        CompressionKind selected = legacy
            ? CompressionKind.Zlib
            : compression == CompressionKind.Auto ? CompressionKind.None : compression;
        if (selected == CompressionKind.None)
        {
            return (body, legacy ? "application/xml" : "application/json");
        }

        using var output = new MemoryStream();
        using (Stream compressor = selected == CompressionKind.Gzip
            ? new GZipStream(output, CompressionLevel.SmallestSize, true)
            : new ZLibStream(output, CompressionLevel.SmallestSize, true))
        {
            compressor.Write(body);
        }

        return (
            output.ToArray(),
            selected == CompressionKind.Gzip ? "application/x-compress-gzip" : "application/x-compress-zlib");
    }

    private static byte[] Decompress(
        byte[] body,
        string? mediaType,
        ICollection<string> contentEncodings)
    {
        bool gzip = string.Equals(mediaType, "application/x-compress-gzip", StringComparison.OrdinalIgnoreCase)
            || contentEncodings.Contains("gzip", StringComparer.OrdinalIgnoreCase);
        bool zlib = string.Equals(mediaType, "application/x-compress-zlib", StringComparison.OrdinalIgnoreCase)
            || contentEncodings.Contains("deflate", StringComparer.OrdinalIgnoreCase);
        if (!gzip && !zlib)
        {
            return body;
        }

        try
        {
            using var input = new MemoryStream(body);
            using Stream decompressor = gzip ? new GZipStream(input, CompressionMode.Decompress) : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            decompressor.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return body;
        }
    }

    private sealed record ProtocolResponse(HttpStatusCode StatusCode, byte[] Body, string? RequestId)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }
}
