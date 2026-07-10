using System.IO.Compression;
using System.Net;
using System.Security.Authentication;
using System.Text;
using DotnetGlpiAgent.Core.Configuration;
using DotnetGlpiAgent.Core.Inventory;
using DotnetGlpiAgent.Protocol.Transport;

namespace DotnetGlpiAgent.Protocol.Tests;

public sealed class GlpiClientTests
{
    [Fact]
    public async Task SubmitAsync_NativeSuccess_SendsIdentityAndCorrelationHeaders()
    {
        var handler = new ScriptedHandler(
            // Real GLPI 10/11 shape: expiration is hours string; tasks is an object map.
            JsonResponse("{\"status\":\"ok\",\"expiration\":\"24\",\"tasks\":{\"inventory\":{\"server\":\"glpi\",\"version\":\"10.0.26\"}}}"),
            JsonResponse("{\"status\":\"ok\"}"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(SubmissionState.Accepted, result.State);
        Assert.Equal(GlpiProtocolKind.NativeJson, result.Protocol);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, static request => Assert.Equal("3c33b875-71b4-4016-a95a-e62d012c4e5b", request.AgentId));
        Assert.All(handler.Requests, static request => Assert.Equal("protocol-client-test", request.CorrelationId));
        Assert.Contains("\"action\":\"inventory\"", handler.Requests[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_Pending_PollsWithNoBodyAndRequestId()
    {
        HttpResponseMessage pending = JsonResponse("{\"status\":\"pending\"}");
        pending.Headers.Add("GLPI-Request-ID", "request-123");
        var handler = new ScriptedHandler(
            pending,
            JsonResponse("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}"),
            JsonResponse("{\"status\":\"ok\"}"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http, pollInterval: TimeSpan.Zero);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(SubmissionState.Accepted, result.State);
        CapturedRequest poll = handler.Requests[1];
        Assert.Equal(HttpMethod.Get, poll.Method);
        Assert.Empty(poll.Body);
        Assert.Equal("request-123", poll.RequestId);
    }

    [Fact]
    public async Task SubmitAsync_ExplicitLegacyResponse_UsesPrologThenLegacyInventory()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.UnsupportedMediaType),
            XmlResponse("<REPLY><PROLOG_FREQ>24</PROLOG_FREQ></REPLY>"),
            XmlResponse("<REPLY><RESPONSE>SEND</RESPONSE></REPLY>"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(GlpiProtocolKind.LegacyXml, result.Protocol);
        Assert.Equal(SubmissionState.Accepted, result.State);
        Assert.Contains("<QUERY>PROLOG</QUERY>", handler.Requests[1].Text, StringComparison.Ordinal);
        Assert.Contains("<QUERY>INVENTORY</QUERY>", handler.Requests[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_AuthenticationFailure_DoesNotFallback()
    {
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal(GlpiFailureKind.Authentication, exception.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_TlsFailure_DoesNotFallback()
    {
        var handler = new ScriptedHandler(new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "TLS failed.",
            new AuthenticationException("Untrusted certificate.")));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal(GlpiFailureKind.Tls, exception.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_TlsHandshakeFailureWithoutAuthenticationInner_IsClassifiedAsTls()
    {
        var handler = new ScriptedHandler(new HttpRequestException(
            HttpRequestError.SecureConnectionError,
            "Handshake reset.",
            new IOException("Connection reset by peer.")));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal(GlpiFailureKind.Tls, exception.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_MalformedSuccessHtmlBody_DoesNotFallbackToLegacy()
    {
        var handler = new ScriptedHandler(
            Response(HttpStatusCode.OK, "<html><body>proxy login</body></html>", "text/html"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal("contact-invalid-json", exception.DiagnosticCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_PendingExpirationHoursString_IsHonoredAsDeadline()
    {
        // "0" hours means the pending answer is already expired: polling must stop.
        HttpResponseMessage pending = JsonResponse("{\"status\":\"pending\",\"expiration\":\"0\"}");
        pending.Headers.Add("GLPI-Request-ID", "request-123");
        var handler = new ScriptedHandler(pending);
        using var http = new HttpClient(handler);
        using var client = CreateClient(http, pollInterval: TimeSpan.Zero);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal("pending-poll-limit", exception.DiagnosticCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_SchemaError_IsCategorizedWithoutLegacyFallback()
    {
        var handler = new ScriptedHandler(
            JsonResponse("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}"),
            JsonResponse("{\"status\":\"error\",\"message\":\"JSON does not validate against schema\"}"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiProtocolException exception = await Assert.ThrowsAsync<GlpiProtocolException>(
            async () => await client.SubmitAsync(Snapshot(), CancellationToken.None));

        Assert.Equal(GlpiFailureKind.Schema, exception.Kind);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SubmitAsync_AmbiguousInventoryUpload_IsNotRetried()
    {
        var handler = new ScriptedHandler(
            JsonResponse("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}"),
            new HttpRequestException("Connection closed after upload."));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(SubmissionState.Indeterminate, result.State);
        Assert.Equal("inventory-upload-indeterminate", result.DiagnosticCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SubmitAsync_ControlRequestRetriesAreBounded()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            JsonResponse("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}"),
            JsonResponse("{\"status\":\"ok\"}"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http, retries: 1);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(SubmissionState.Accepted, result.State);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(CompressionKind.Gzip, "application/x-compress-gzip")]
    [InlineData(CompressionKind.Zlib, "application/x-compress-zlib")]
    [InlineData(CompressionKind.None, "application/json")]
    public async Task SubmitAsync_UsesConfiguredCompression(CompressionKind compression, string expectedContentType)
    {
        var handler = new ScriptedHandler(
            JsonResponse("{\"status\":\"ok\",\"tasks\":[\"inventory\"]}"),
            JsonResponse("{\"status\":\"ok\"}"));
        using var http = new HttpClient(handler);
        using var client = CreateClient(http, compression: compression);

        GlpiSubmissionResult result = await client.SubmitAsync(Snapshot(), CancellationToken.None);

        Assert.Equal(SubmissionState.Accepted, result.State);
        Assert.All(handler.Requests, request => Assert.Equal(expectedContentType, request.ContentType));
        Assert.Contains("\"action\":\"contact\"", handler.Requests[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_CancellationDuringPendingPolling_StopsImmediately()
    {
        HttpResponseMessage pending = JsonResponse("{\"status\":\"pending\"}");
        pending.Headers.Add("GLPI-Request-ID", "request-123");
        var handler = new ScriptedHandler(pending);
        using var http = new HttpClient(handler);
        using var client = CreateClient(http, pollInterval: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.SubmitAsync(Snapshot(), cancellation.Token));

        Assert.Single(handler.Requests);
    }

    private static GlpiClient CreateClient(
        HttpClient http,
        CompressionKind compression = CompressionKind.None,
        TimeSpan? pollInterval = null,
        int retries = 0)
    {
        return new GlpiClient(
            http,
            new GlpiProtocolOptions
            {
                Server = new Uri("https://glpi.example/front/inventory.php"),
                Compression = compression,
                PollInterval = pollInterval ?? TimeSpan.Zero,
                MaximumPollDuration = TimeSpan.FromSeconds(2),
                MaximumPollAttempts = 4,
                MaximumControlRetries = retries,
                RetryDelay = TimeSpan.Zero,
            });
    }

    private static InventorySnapshot Snapshot()
    {
        return new InventorySnapshot
        {
            Identity = new AgentIdentity(
                Guid.Parse("3c33b875-71b4-4016-a95a-e62d012c4e5b"),
                "WINDOWS-2026-07-10-12-00-00",
                "host",
                "Dotnet-GLPI-Agent/0.1.0"),
            Account = new InventoryAccount("lab"),
            Collection = new CollectionMetadata(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                true,
                [],
                "protocol-client-test"),
        };
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return Response(HttpStatusCode.OK, body, "application/json");
    }

    private static HttpResponseMessage XmlResponse(string body)
    {
        return Response(HttpStatusCode.OK, body, "application/xml");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string contentType)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
    }

    private sealed class ScriptedHandler(params object[] script) : HttpMessageHandler
    {
        private readonly Queue<object> _script = new(script);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(await CapturedRequest.CreateAsync(request, cancellationToken));
            if (_script.Count == 0)
            {
                throw new InvalidOperationException("The test HTTP script is exhausted.");
            }

            object next = _script.Dequeue();
            return next switch
            {
                HttpResponseMessage response => response,
                Exception exception => throw exception,
                _ => throw new InvalidOperationException("The test HTTP script contains an unsupported entry."),
            };
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        byte[] Body,
        string Text,
        string? ContentType,
        string? AgentId,
        string? CorrelationId,
        string? RequestId)
    {
        public static async ValueTask<CapturedRequest> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            string? contentType = request.Content?.Headers.ContentType?.MediaType;
            byte[] decompressed = Decompress(body, contentType);
            return new CapturedRequest(
                request.Method,
                body,
                Encoding.UTF8.GetString(decompressed),
                contentType,
                Header(request, "GLPI-Agent-ID"),
                Header(request, "X-Correlation-ID"),
                Header(request, "GLPI-Request-ID"));
        }

        private static string? Header(HttpRequestMessage request, string name)
        {
            return request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.Single() : null;
        }

        private static byte[] Decompress(byte[] body, string? contentType)
        {
            if (contentType is not ("application/x-compress-gzip" or "application/x-compress-zlib"))
            {
                return body;
            }

            using var input = new MemoryStream(body);
            using Stream decompressor = contentType == "application/x-compress-gzip"
                ? new GZipStream(input, CompressionMode.Decompress)
                : new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            decompressor.CopyTo(output);
            return output.ToArray();
        }
    }
}
