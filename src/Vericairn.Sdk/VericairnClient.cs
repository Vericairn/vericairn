// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

/// <summary>
/// The typed client of the Vericairn API. Over the local socket (a Unix domain socket or a named pipe) the operating system
/// identifies the caller; over loopback TLS the client pins the runtime certificate and signs every request with a device key.
/// It presents, requests and observes; it never evaluates authority.
/// </summary>
public sealed class VericairnClient : IEstateApi, IDisposable
{
    private readonly HttpClient http;

    /// <summary>Connects over the local socket of a published endpoint.</summary>
    public VericairnClient(DaemonEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
        Transport = endpoint.Kind;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (endpoint.Kind == "pipe")
                {
                    var pipe = new NamedPipeClientStream(".", endpoint.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
                    return pipe;
                }

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.Address), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri("http://vericairn/"), Timeout = TimeSpan.FromMinutes(5) };
    }

    private VericairnClient(DaemonEndpoint endpoint, LoopbackEndpoint loopback, IRequestSigner? signer)
    {
        Endpoint = endpoint;
        Transport = "loopback";
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && Fingerprint(certificate) == loopback.Certificate,
            },
        };
        HttpMessageHandler chain = signer is null ? handler : new SigningHandler(signer, handler);
        http = new HttpClient(chain, disposeHandler: true) { BaseAddress = new Uri($"https://{loopback.Host}:{loopback.Port}/"), Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>The endpoint this client talks to.</summary>
    public DaemonEndpoint Endpoint { get; }

    /// <summary>unix, pipe or loopback.</summary>
    public string Transport { get; }

    /// <summary>Connects over loopback TLS pinned to the runtime certificate; every request is signed by the device, and an unsigned client is denied.</summary>
    public static VericairnClient Loopback(DaemonEndpoint endpoint, IRequestSigner? signer)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        LoopbackEndpoint loopback = endpoint.Loopback ?? throw new InvalidOperationException("The daemon publishes no loopback endpoint.");
        return new VericairnClient(endpoint, loopback, signer);
    }

    /// <summary>True when a daemon answers at the endpoint's local socket.</summary>
    public static bool IsAlive(DaemonEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            using var client = new VericairnClient(endpoint);
            return client.Health().Api == ApiVersion.Current;
        }
        catch (Exception exception) when (exception is HttpRequestException or SocketException or IOException or TimeoutException or TaskCanceledException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public HealthResponse Health() => Get<HealthResponse>("/v1/health")!;

    /// <inheritdoc />
    public StatusResponse Status() => Get<StatusResponse>("/v1/status")!;

    /// <inheritdoc />
    public FindingsResponse Validate() => Get<FindingsResponse>("/v1/validate")!;

    /// <inheritdoc />
    public FindingsResponse Check() => Get<FindingsResponse>("/v1/check")!;

    /// <inheritdoc />
    public RecordResponse? Record(string id) => Get<RecordResponse>("/v1/records/" + Uri.EscapeDataString(id), allowNotFound: true);

    /// <inheritdoc />
    public IReadOnlyList<RecordResponse> Records(string type) => Get<List<RecordResponse>>("/v1/records?type=" + Uri.EscapeDataString(type))!;

    /// <inheritdoc />
    public WriteOutcome NewRecord(NewRecordRequest request) => Post<NewRecordRequest, WriteOutcome>("/v1/records", request)!;

    /// <inheritdoc />
    public DecisionsResponse Decisions() => Get<DecisionsResponse>("/v1/decisions")!;

    /// <inheritdoc />
    public DecisionResponse? Decision(string id) => Get<DecisionResponse>("/v1/decisions/" + Uri.EscapeDataString(id), allowNotFound: true);

    /// <inheritdoc />
    public BriefResponse? Brief(string workOrder) => Get<BriefResponse>("/v1/work/" + Uri.EscapeDataString(workOrder) + "/brief", allowNotFound: true);

    /// <inheritdoc />
    public ObserveResponse? Observe(string workOrder) => Get<ObserveResponse>("/v1/work/" + Uri.EscapeDataString(workOrder), allowNotFound: true);

    /// <inheritdoc />
    public WriteOutcome Assign(AssignRequest request) => Post<AssignRequest, WriteOutcome>("/v1/work/assign", request)!;

    /// <inheritdoc />
    public WriteOutcome Attempt(AttemptRequest request) => Post<AttemptRequest, WriteOutcome>("/v1/work/attempt", request)!;

    /// <inheritdoc />
    public WriteOutcome Result(ResultRequest request) => Post<ResultRequest, WriteOutcome>("/v1/work/result", request)!;

    /// <inheritdoc />
    public WriteOutcome Amend(AmendRequest request) => Post<AmendRequest, WriteOutcome>("/v1/work/amend", request)!;

    /// <inheritdoc />
    public IReadOnlyList<DeviceRow> Devices() => Get<List<DeviceRow>>("/v1/devices")!;

    /// <inheritdoc />
    public WriteOutcome DeviceCreate(DeviceCreateRequest request) => Post<DeviceCreateRequest, WriteOutcome>("/v1/devices", request)!;

    /// <inheritdoc />
    public WriteOutcome DeviceAdmit(AdmitRequest request) => Post<AdmitRequest, WriteOutcome>("/v1/devices/admit", request)!;

    /// <inheritdoc />
    public WriteOutcome DeviceRevoke(RevokeRequest request) => Post<RevokeRequest, WriteOutcome>("/v1/devices/revoke", request)!;

    /// <inheritdoc />
    public IReadOnlyList<ReleaseRow> Releases() => Get<List<ReleaseRow>>("/v1/releases")!;

    /// <inheritdoc />
    public (UpgradeResponse? Plan, Problem? Problem) Upgrade(UpgradeRequest request) => PostOrProblem<UpgradeRequest, UpgradeResponse>("/v1/estate/upgrade", request);

    /// <inheritdoc />
    public (ExportResponse? Export, Problem? Problem) Export(ExportRequest request) => PostOrProblem<ExportRequest, ExportResponse>("/v1/estate/export", request);

    /// <inheritdoc />
    public IReadOnlyList<ReconcileTask> Reconcile() => Get<List<ReconcileTask>>("/v1/reconcile")!;

    /// <inheritdoc />
    public string RecoverInspect()
    {
        using HttpResponseMessage response = http.GetAsync(new Uri("/v1/recover/inspect", UriKind.Relative)).GetAwaiter().GetResult();
        return response.EnsureSuccessStatusCode().Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public IReadOnlyList<ObjectRow> Objects() => Get<List<ObjectRow>>("/v1/objects")!;

    /// <inheritdoc />
    public (ObjectStored? Stored, Problem? Problem) ObjectPut(byte[] bytes)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using HttpResponseMessage response = http.PostAsync(new Uri("/v1/objects", UriKind.Relative), content).GetAwaiter().GetResult();
        return response.IsSuccessStatusCode
            ? (response.Content.ReadFromJsonAsync<ObjectStored>(ProtocolJson.Options).GetAwaiter().GetResult(), null)
            : (null, ReadProblem(response));
    }

    /// <inheritdoc />
    public byte[]? ObjectGet(string hash)
    {
        using HttpResponseMessage response = http.GetAsync(new Uri("/v1/objects/" + Uri.EscapeDataString(hash), UriKind.Relative)).GetAwaiter().GetResult();
        return response.StatusCode == HttpStatusCode.NotFound ? null : response.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public FindingsResponse ObjectsVerify() => Get<FindingsResponse>("/v1/objects/verify")!;

    /// <summary>Asks the daemon to stop.</summary>
    public void Shutdown()
    {
        using HttpResponseMessage response = http.PostAsync(new Uri("/v1/shutdown", UriKind.Relative), null).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The OpenAPI document the daemon serves.</summary>
    public string OpenApi()
    {
        using HttpResponseMessage response = http.GetAsync(new Uri(OpenApiDocument.Path, UriKind.Relative)).GetAwaiter().GetResult();
        return response.EnsureSuccessStatusCode().Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();

    private static string Fingerprint(X509Certificate certificate) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()));

    private static Problem ReadProblem(HttpResponseMessage response)
    {
        try
        {
            return response.Content.ReadFromJsonAsync<Problem>(ProtocolJson.Options).GetAwaiter().GetResult() ?? new Problem("refused", response.ReasonPhrase ?? "refused", []);
        }
        catch (JsonException)
        {
            return new Problem("refused", response.ReasonPhrase ?? "refused", []);
        }
    }

    private T? Get<T>(string path, bool allowNotFound = false)
        where T : class
    {
        using HttpResponseMessage response = http.GetAsync(new Uri(path, UriKind.Relative)).GetAwaiter().GetResult();
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return response.Content.ReadFromJsonAsync<T>(ProtocolJson.Options).GetAwaiter().GetResult();
    }

    private TResponse? Post<TRequest, TResponse>(string path, TRequest request)
        where TResponse : class
    {
        using HttpResponseMessage response = http.PostAsJsonAsync(new Uri(path, UriKind.Relative), request, ProtocolJson.Options).GetAwaiter().GetResult();
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound or HttpStatusCode.OK)
        {
            return response.Content.ReadFromJsonAsync<TResponse>(ProtocolJson.Options).GetAwaiter().GetResult();
        }

        response.EnsureSuccessStatusCode();
        return response.Content.ReadFromJsonAsync<TResponse>(ProtocolJson.Options).GetAwaiter().GetResult();
    }

    private (TResponse? Response, Problem? Problem) PostOrProblem<TRequest, TResponse>(string path, TRequest request)
        where TResponse : class
    {
        using HttpResponseMessage response = http.PostAsJsonAsync(new Uri(path, UriKind.Relative), request, ProtocolJson.Options).GetAwaiter().GetResult();
        if (response.IsSuccessStatusCode)
        {
            return (response.Content.ReadFromJsonAsync<TResponse>(ProtocolJson.Options).GetAwaiter().GetResult(), null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Unauthorized)
        {
            return (null, ReadProblem(response));
        }

        response.EnsureSuccessStatusCode();
        return (null, null);
    }

    /// <summary>Adds the device, time and signature headers to every request.</summary>
    private sealed class SigningHandler : DelegatingHandler
    {
        private readonly IRequestSigner signer;

        public SigningHandler(IRequestSigner signer, HttpMessageHandler inner)
            : base(inner)
        {
            this.signer = signer;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
            byte[] digest = RequestSigning.Digest(request.Method.Method, request.RequestUri!.PathAndQuery, signedAt, body);
            request.Headers.TryAddWithoutValidation(RequestSigning.DeviceHeader, signer.DeviceId);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignedAtHeader, signedAt);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, Base64Url.EncodeToString(signer.Sign(digest)));
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
