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
/// identifies the caller; over loopback TLS the client pins the runtime certificate and signs every request with a device key; over
/// the local network a paired device presents a certificate its key issued, has the runtime identity it pinned prove each
/// connection, and signs every request. It presents, requests and observes; it never evaluates authority.
/// </summary>
public sealed partial class VericairnClient : IEstateApi, IDisposable
{
    private readonly HttpClient http;
    private readonly HttpClient asyncHttp;
    private readonly LocalNetworkCredential? credential;

    /// <summary>Connects over the local socket of a published endpoint.</summary>
    public VericairnClient(DaemonEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
        Transport = endpoint.Kind;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (endpoint.Kind == "pipe")
                {
                    var pipe = new NamedPipeClientStream(".", endpoint.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
                        return pipe;
                    }
                    catch
                    {
                        pipe.Dispose();
                        throw;
                    }
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
        asyncHttp = AsyncTransport(handler, http.BaseAddress);
    }

    private VericairnClient(DaemonEndpoint endpoint, LoopbackEndpoint loopback, IRequestSigner? signer)
    {
        Endpoint = endpoint;
        Transport = "loopback";
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                CertificateChainPolicy = LocalNetworkTransport.OfflineChainPolicy(),
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && Fingerprint(certificate) == loopback.Certificate,
            },
        };
        HttpMessageHandler chain = signer is null ? handler : new SigningHandler(signer, handler);
        http = new HttpClient(chain, disposeHandler: true) { BaseAddress = new Uri($"https://{loopback.Host}:{loopback.Port}/"), Timeout = TimeSpan.FromMinutes(5) };
        asyncHttp = AsyncTransport(chain, http.BaseAddress);
    }

    private VericairnClient(RuntimeConnection connection, IRequestSigner signer, TimeSpan? timeout)
    {
        Endpoint = new DaemonEndpoint("local-network", connection.Address, 0, string.Empty, string.Empty, ApiVersion.Current);
        Transport = "local-network";
        OperationTimeout = _ => timeout;
        credential = new LocalNetworkCredential(signer, Base64Url.DecodeFromChars(connection.DevicePublicKey));
        LocalNetworkCredential own = credential;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = timeout ?? TimeSpan.FromSeconds(30),
            ConnectCallback = (_, cancellationToken) => LocalNetworkTransport.ConnectAsync(connection, signer, own, cancellationToken),
        };

        // TLS and the identity proof happen in the connect callback, so the requests themselves go over that stream as plain HTTP,
        // each signed for this estate and the runtime identity the device paired with.
        http = new HttpClient(new SigningHandler(signer, handler, connection), disposeHandler: true) { BaseAddress = new Uri($"http://{connection.Address}/"), Timeout = timeout ?? TimeSpan.FromMinutes(5) };
        var asyncHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = timeout ?? TimeSpan.FromSeconds(30),
            ConnectCallback = (_, cancellationToken) => LocalNetworkTransport.ConnectAsync(connection, signer, own, cancellationToken, classifyRefusal: true),
        };
        asyncHttp = new HttpClient(new SigningHandler(signer, asyncHandler, connection), disposeHandler: true)
            { BaseAddress = http.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>The endpoint this client talks to.</summary>
    public DaemonEndpoint Endpoint { get; }

    /// <summary>unix, pipe, loopback or local-network.</summary>
    public string Transport { get; }

    /// <summary>Connects over loopback TLS pinned to the runtime certificate; every request is signed by the device, and an unsigned client is denied.</summary>
    public static VericairnClient Loopback(DaemonEndpoint endpoint, IRequestSigner? signer)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        LoopbackEndpoint loopback = endpoint.Loopback ?? throw new InvalidOperationException("The daemon publishes no loopback endpoint.");
        return new VericairnClient(endpoint, loopback, signer);
    }

    /// <summary>
    /// Connects to a paired runtime over the local network, from 0.6.0: mutual TLS with a certificate the device's key issued, the
    /// runtime identity pinned at pairing proving each connection before anything else is sent, and every request device-signed. A
    /// refusal before any request surfaces as an <see cref="HttpRequestException"/> whose inner <see cref="RuntimeConnectionException"/>
    /// says denied or unavailable.
    /// </summary>
    public static VericairnClient LocalNetwork(RuntimeConnection connection, IRequestSigner signer, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(signer);
        if (!RuntimeConnection.TryParseAddress(connection.Address, out _, out _))
        {
            throw new ArgumentException($"'{connection.Address}' is not an address and port.", nameof(connection));
        }

        return new VericairnClient(connection, signer, timeout);
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
    public CommunicationProfile Communication(string? recipient = null, string? assignment = null, CommunicationValues? view = null)
    {
        try
        {
            return Get<CommunicationProfile>("/v1/communication" + CommunicationQuery(recipient, assignment, view))!;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new CommunicationProfile(recipient, assignment, new Dictionary<string, CommunicationValue>(), [], [], Unavailable: "This runtime does not support communication settings.");
        }
    }

    /// <inheritdoc />
    public WriteOutcome CommunicationSet(CommunicationSetRequest request) => Post<CommunicationSetRequest, WriteOutcome>("/v1/communication", request)!;

    /// <inheritdoc />
    public WriteOutcome CommunicationDelegate(CommunicationDelegateRequest request) => Post<CommunicationDelegateRequest, WriteOutcome>("/v1/communication/delegations", request)!;

    /// <inheritdoc />
    public StatusResponse Status() => Get<StatusResponse>("/v1/status")!;

    /// <inheritdoc />
    public WriteOutcome ObserveEstate(EstateObserveRequest request) => Post<EstateObserveRequest, WriteOutcome>("/v1/status/observations", request)!;

    /// <inheritdoc />
    public ProgressResponse Progress() => Get<ProgressResponse>("/v1/progress")!;

    /// <inheritdoc />
    public HandoffResponse Handoff(long? maxBytes = null) => Get<HandoffResponse>("/v1/handoff" + (maxBytes is null ? string.Empty : "?maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;

    /// <inheritdoc />
    public ResumeResponse ResumeHandoff(HandoffPackage package) => Post<HandoffPackage, ResumeResponse>("/v1/resume", package)!;

    /// <inheritdoc />
    public BriefResponse? BriefContext(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null)
    {
        string query = CommunicationQuery(recipient, assignment, view);
        if (maxBytes is not null)
        {
            query += (query.Length == 0 ? "?" : "&") + "maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Get<BriefResponse>("/v1/context/work/" + Uri.EscapeDataString(workOrder) + "/brief" + query, allowNotFound: true);
    }

    /// <inheritdoc />
    public ContextResponse Context(string workOrder, long? maxBytes = null) => Get<ContextResponse>("/v1/context/work/" + Uri.EscapeDataString(workOrder) +
        (maxBytes is null ? string.Empty : "?maxBytes=" + maxBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string pin) => Get<ContextReadResponse>("/v1/context/original/" + Uri.EscapeDataString(pin))!;

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string manifest, string source) => Get<ContextReadResponse>("/v1/context/" + Uri.EscapeDataString(manifest) + "/sources/" + Uri.EscapeDataString(source))!;

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
    public BriefResponse? BriefFor(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null) =>
        Get<BriefResponse>("/v1/work/" + Uri.EscapeDataString(workOrder) + "/brief" + CommunicationQuery(recipient, assignment, view), allowNotFound: true);

    /// <inheritdoc />
    public BriefResponse? Brief(string workOrder) => Get<BriefResponse>("/v1/work/" + Uri.EscapeDataString(workOrder) + "/brief", allowNotFound: true);

    /// <inheritdoc />
    public ObserveResponse? Observe(string workOrder) => Get<ObserveResponse>("/v1/work/" + Uri.EscapeDataString(workOrder), allowNotFound: true);

    /// <inheritdoc />
    public WriteOutcome Assign(AssignRequest request) => Post<AssignRequest, WriteOutcome>("/v1/work/assign", request)!;

    /// <inheritdoc />
    public WriteOutcome Attempt(AttemptRequest request) => Post<AttemptRequest, WriteOutcome>("/v1/work/attempt", request)!;

    /// <inheritdoc />
    public WorkProgressResponse WorkProgress(WorkProgressRequest request) => Post<WorkProgressRequest, WorkProgressResponse>("/v1/work/progress", request)!;

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
    public WriteOutcome DeviceRename(DeviceRenameRequest request) => Post<DeviceRenameRequest, WriteOutcome>("/v1/devices/rename", request)!;

    /// <inheritdoc />
    public WriteOutcome DeviceRotate(DeviceRotateRequest request) => Post<DeviceRotateRequest, WriteOutcome>("/v1/devices/rotate", request)!;

    /// <inheritdoc />
    public WriteOutcome DeviceRecovery(DeviceRecoveryRequest request) => Post<DeviceRecoveryRequest, WriteOutcome>("/v1/devices/recovery", request)!;

    /// <inheritdoc />
    public WriteOutcome DeviceRevoke(RevokeRequest request) => Post<RevokeRequest, WriteOutcome>("/v1/devices/revoke", request)!;

    /// <inheritdoc />
    public IReadOnlyList<ReleaseRow> Releases() => Get<List<ReleaseRow>>("/v1/releases")!;

    /// <inheritdoc />
    public IReadOnlyList<DocumentRow> Documents() => Get<List<DocumentRow>>("/v1/documents")!;

    /// <inheritdoc />
    public WriteOutcome DocumentReview(string declaration, DocumentReviewRequest request) => Post<DocumentReviewRequest, WriteOutcome>("/v1/documents/" + Uri.EscapeDataString(declaration) + "/reviews", request)!;

    /// <inheritdoc />
    public AsksResponse Asks(string? principal = null) => Get<AsksResponse>(principal is null ? "/v1/asks" : "/v1/asks?to=" + Uri.EscapeDataString(principal))!;

    /// <inheritdoc />
    public OperatorActionsResponse OperatorActions(string? recipient = null, string? assignment = null, CommunicationValues? view = null) =>
        Get<OperatorActionsResponse>("/v1/operator/actions" + CommunicationQuery(recipient, assignment, view))!;

    /// <inheritdoc />
    public OperatorRecordResponse OperatorRecord(OperatorRecordRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = http.PostAsJsonAsync(new Uri("/v1/operator/record", UriKind.Relative), request, ProtocolJson.Options, cancellationToken).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadFromJsonAsync<OperatorRecordResponse>(ProtocolJson.Options, cancellationToken).GetAwaiter().GetResult()!;
    }

    /// <inheritdoc />
    public IReadOnlyList<GoalRow> Goals() => Get<List<GoalRow>>("/v1/goals")!;

    /// <inheritdoc />
    public GoalResponse? Goal(string id) => Get<GoalResponse>("/v1/goals/" + Uri.EscapeDataString(id), allowNotFound: true);

    /// <inheritdoc />
    public NextResponse? GoalNext(string goal) => Get<NextResponse>(GoalPath(goal, "next"), allowNotFound: true);

    /// <inheritdoc />
    public GoalStepResponse GoalStep(string goal, GoalStepRequest request) => Post<GoalStepRequest, GoalStepResponse>(GoalPath(goal, "step"), request)!;

    /// <inheritdoc />
    public TakeoverResponse WorkTakeover(TakeoverRequest request) => Post<TakeoverRequest, TakeoverResponse>("/v1/work/takeover", request)!;

    /// <inheritdoc />
    public WriteOutcome GoalPropose(GoalProposeRequest request) => Post<GoalProposeRequest, WriteOutcome>("/v1/goals", request)!;

    /// <inheritdoc />
    public WriteOutcome GoalActivate(string goal, GoalActionRequest request) => Post<GoalActionRequest, WriteOutcome>(GoalPath(goal, "activate"), request)!;

    /// <inheritdoc />
    public WriteOutcome GoalPause(string goal, GoalActionRequest request) => Post<GoalActionRequest, WriteOutcome>(GoalPath(goal, "pause"), request)!;

    /// <inheritdoc />
    public WriteOutcome GoalResume(string goal, GoalActionRequest request) => Post<GoalActionRequest, WriteOutcome>(GoalPath(goal, "resume"), request)!;

    /// <inheritdoc />
    public WriteOutcome GoalStop(string goal, GoalActionRequest request) => Post<GoalActionRequest, WriteOutcome>(GoalPath(goal, "stop"), request)!;

    /// <inheritdoc />
    public WriteOutcome GoalEvaluate(string goal, GoalEvaluateRequest request) => Post<GoalEvaluateRequest, WriteOutcome>(GoalPath(goal, "evaluate"), request)!;

    /// <inheritdoc />
    public WriteOutcome GoalAccept(string goal, GoalAcceptRequest request) => Post<GoalAcceptRequest, WriteOutcome>(GoalPath(goal, "accept"), request)!;

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Providers() => Get<List<ProviderRow>>("/v1/providers")!;

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Launchers() => Get<List<ProviderRow>>("/v1/launchers")!;

    /// <inheritdoc />
    public WriteOutcome QualifyHarness(HarnessQualifyRequest request) => Post<HarnessQualifyRequest, WriteOutcome>("/v1/harness-qualifications", request)!;

    /// <inheritdoc />
    public RunResponse RunStart(RunStartRequest request) => Post<RunStartRequest, RunResponse>("/v1/runs", request)!;

    /// <inheritdoc />
    public RunsResponse Runs() => Get<RunsResponse>("/v1/runs")!;

    /// <inheritdoc />
    public RunStopResponse RunStop(string run, RunStopRequest request) => Post<RunStopRequest, RunStopResponse>("/v1/runs/" + Uri.EscapeDataString(run) + "/stop", request)!;

    /// <inheritdoc />
    public ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request) => Post<ReleaseSubmitRequest, ReleaseResponse>("/v1/releases/submit", request)!;

    /// <inheritdoc />
    public ReleaseResponse ReleaseObserve(ReleaseObserveRequest request) => Post<ReleaseObserveRequest, ReleaseResponse>("/v1/releases/observe", request)!;

    /// <inheritdoc />
    public ChangeResponse ChangesPush(ChangesPushRequest request) => Post<ChangesPushRequest, ChangeResponse>("/v1/changes/push", request)!;

    /// <inheritdoc />
    public ChangeResponse ChangesOpen(ChangesOpenRequest request) => Post<ChangesOpenRequest, ChangeResponse>("/v1/changes/open", request)!;

    /// <inheritdoc />
    public ChangeResponse ChangesMerge(ChangesMergeRequest request) => Post<ChangesMergeRequest, ChangeResponse>("/v1/changes/merge", request)!;

    /// <inheritdoc />
    public ChangeResponse ChangesObserve(ChangesObserveRequest request) => Post<ChangesObserveRequest, ChangeResponse>("/v1/changes/observe", request)!;


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

    /// <summary>
    /// Asks the daemon for a pairing code under an administrator's authority, from 0.6.0: on the runtime's own machine, over the local
    /// socket or loopback. The code is shown once; a problem when it cannot be issued.
    /// </summary>
    public (PairingIssued? Issued, Problem? Problem) IssuePairing(PairingIssueRequest request) => PostOrProblem<PairingIssueRequest, PairingIssued>("/v1/pairings", request);

    /// <summary>Replaces the runtime certificate now, from 0.6.0; on the runtime's own machine. A problem when the daemon serves no TLS profile.</summary>
    public (CertificateRotated? Rotated, Problem? Problem) RotateCertificate(CertificateRotateRequest request) => PostOrProblem<CertificateRotateRequest, CertificateRotated>("/v1/runtime/certificate", request);

    /// <summary>The runtime identity's proof for a nonce over this connection's certificate, from 0.6.0; null when the runtime has no identity.</summary>
    public RuntimeIdentityProof? RuntimeIdentity(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        return Get<RuntimeIdentityProof>(RuntimeIdentityScheme.Path + "?nonce=" + Uri.EscapeDataString(nonce), allowNotFound: true);
    }

    /// <summary>The OpenAPI document the daemon serves.</summary>
    public string OpenApi()
    {
        using HttpResponseMessage response = http.GetAsync(new Uri(OpenApiDocument.Path, UriKind.Relative)).GetAwaiter().GetResult();
        return response.EnsureSuccessStatusCode().Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        asyncHttp.Dispose();
        http.Dispose();
        credential?.Dispose();
    }

    private static string CommunicationQuery(string? recipient, string? assignment, CommunicationValues? view)
    {
        var query = new Dictionary<string, string?> { ["recipient"] = recipient, ["assignment"] = assignment };
        foreach ((string key, string? value) in (view ?? new CommunicationValues()).Fields)
        {
            query[key] = value;
        }

        string suffix = string.Join('&', query.Where(p => p.Value is not null).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!)));
        return suffix.Length == 0 ? string.Empty : "?" + suffix;
    }

    private static string GoalPath(string goal, string action) => "/v1/goals/" + Uri.EscapeDataString(goal) + "/" + action;

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
        if (response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound or HttpStatusCode.OK or HttpStatusCode.Conflict)
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

    /// <summary>
    /// Adds the device, time and signature headers to every request: version 1 on loopback, and version 2, bound to the estate and the
    /// pinned runtime identity, over the local network, except for the identity proof's request.
    /// </summary>
    private sealed class SigningHandler : DelegatingHandler
    {
        private readonly IRequestSigner signer;
        private readonly RuntimeConnection? connection;

        public SigningHandler(IRequestSigner signer, HttpMessageHandler inner, RuntimeConnection? connection = null)
            : base(inner)
        {
            this.signer = signer;
            this.connection = connection;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
            string target = request.RequestUri!.PathAndQuery;

            // The identity proof's request is signed as on loopback: it asks which identity answers, before anything has proven it.
            byte[] digest = connection is null || request.RequestUri.AbsolutePath == RuntimeIdentityScheme.Path
                ? RequestSigning.Digest(request.Method.Method, target, signedAt, body)
                : RequestSigning.LocalNetworkDigest(connection.Estate, connection.Identity, request.Method.Method, target, signedAt, body);
            request.Headers.TryAddWithoutValidation(RequestSigning.DeviceHeader, signer.DeviceId);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignedAtHeader, signedAt);
            request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, Base64Url.EncodeToString(signer.Sign(digest)));
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
