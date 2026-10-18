// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.IO.Pipes;
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
        http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri("http://vericairn/"), Timeout = Timeout.InfiniteTimeSpan };
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
        http = new HttpClient(chain, disposeHandler: true) { BaseAddress = new Uri($"https://{loopback.Host}:{loopback.Port}/"), Timeout = Timeout.InfiniteTimeSpan };
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
        http = new HttpClient(new SigningHandler(signer, handler, connection), disposeHandler: true) { BaseAddress = new Uri($"http://{connection.Address}/"), Timeout = Timeout.InfiniteTimeSpan };
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
    public HealthResponse Health() => HealthAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public CommunicationProfile Communication(string? recipient = null, string? assignment = null, CommunicationValues? view = null) => CommunicationAsync(recipient, assignment, view).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome CommunicationSet(CommunicationSetRequest request) => CommunicationSetAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome CommunicationDelegate(CommunicationDelegateRequest request) => CommunicationDelegateAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public StatusResponse Status() => StatusAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome ObserveEstate(EstateObserveRequest request) => ObserveEstateAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ProgressResponse Progress() => ProgressAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public HandoffResponse Handoff(long? maxBytes = null) => HandoffAsync(maxBytes).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ResumeResponse ResumeHandoff(HandoffPackage package) => ResumeHandoffAsync(package).GetAwaiter().GetResult();

    /// <inheritdoc />
    public BriefResponse? BriefContext(string workOrder, long? maxBytes = null, string? recipient = null, string? assignment = null, CommunicationValues? view = null) => BriefContextAsync(workOrder, maxBytes, recipient, assignment, view).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ContextResponse Context(string workOrder, long? maxBytes = null) => ContextAsync(workOrder, maxBytes).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string pin) => ContextReadAsync(pin).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ContextReadResponse ContextRead(string manifest, string source) => ContextReadAsync(manifest, source).GetAwaiter().GetResult();

    /// <inheritdoc />
    public FindingsResponse Validate() => ValidateAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public FindingsResponse Check() => CheckAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public RecordResponse? Record(string id) => RecordAsync(id).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<RecordResponse> Records(string type) => RecordsAsync(type).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome NewRecord(NewRecordRequest request) => NewRecordAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public DecisionsResponse Decisions() => DecisionsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public DecisionResponse? Decision(string id) => DecisionAsync(id).GetAwaiter().GetResult();

    /// <inheritdoc />
    public BriefResponse? BriefFor(string workOrder, string? recipient = null, string? assignment = null, CommunicationValues? view = null) => BriefForAsync(workOrder, recipient, assignment, view).GetAwaiter().GetResult();

    /// <inheritdoc />
    public BriefResponse? Brief(string workOrder) => BriefAsync(workOrder).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ObserveResponse? Observe(string workOrder) => ObserveAsync(workOrder).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome Assign(AssignRequest request) => AssignAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome Attempt(AttemptRequest request) => AttemptAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WorkProgressResponse WorkProgress(WorkProgressRequest request) => WorkProgressAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome Result(ResultRequest request) => ResultAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome Amend(AmendRequest request) => AmendAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<DeviceRow> Devices() => DevicesAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceCreate(DeviceCreateRequest request) => DeviceCreateAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceAdmit(AdmitRequest request) => DeviceAdmitAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceRename(DeviceRenameRequest request) => DeviceRenameAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceRotate(DeviceRotateRequest request) => DeviceRotateAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceRecovery(DeviceRecoveryRequest request) => DeviceRecoveryAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DeviceRevoke(RevokeRequest request) => DeviceRevokeAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<ReleaseRow> Releases() => ReleasesAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<DocumentRow> Documents() => DocumentsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome DocumentReview(string declaration, DocumentReviewRequest request) => DocumentReviewAsync(declaration, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public AsksResponse Asks(string? principal = null) => AsksAsync(principal).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<GoalRow> Goals() => GoalsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public GoalResponse? Goal(string id) => GoalAsync(id).GetAwaiter().GetResult();

    /// <inheritdoc />
    public NextResponse? GoalNext(string goal) => GoalNextAsync(goal).GetAwaiter().GetResult();

    /// <inheritdoc />
    public GoalStepResponse GoalStep(string goal, GoalStepRequest request) => GoalStepAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public TakeoverResponse WorkTakeover(TakeoverRequest request) => WorkTakeoverAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalPropose(GoalProposeRequest request) => GoalProposeAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalActivate(string goal, GoalActionRequest request) => GoalActivateAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalPause(string goal, GoalActionRequest request) => GoalPauseAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalResume(string goal, GoalActionRequest request) => GoalResumeAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalStop(string goal, GoalActionRequest request) => GoalStopAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalEvaluate(string goal, GoalEvaluateRequest request) => GoalEvaluateAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome GoalAccept(string goal, GoalAcceptRequest request) => GoalAcceptAsync(goal, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Providers() => ProvidersAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<ProviderRow> Launchers() => LaunchersAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public WriteOutcome QualifyHarness(HarnessQualifyRequest request) => QualifyHarnessAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public RunResponse RunStart(RunStartRequest request) => RunStartAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public RunsResponse Runs() => RunsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public RunStopResponse RunStop(string run, RunStopRequest request) => RunStopAsync(run, request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ReleaseResponse ReleaseSubmit(ReleaseSubmitRequest request) => ReleaseSubmitAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public ReleaseResponse ReleaseObserve(ReleaseObserveRequest request) => ReleaseObserveAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public (UpgradeResponse? Plan, Problem? Problem) Upgrade(UpgradeRequest request) => UpgradeAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public (ExportResponse? Export, Problem? Problem) Export(ExportRequest request) => ExportAsync(request).GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<ReconcileTask> Reconcile() => ReconcileAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public string RecoverInspect() => RecoverInspectAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public IReadOnlyList<ObjectRow> Objects() => ObjectsAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public (ObjectStored? Stored, Problem? Problem) ObjectPut(byte[] bytes) => ObjectPutAsync(bytes).GetAwaiter().GetResult();

    /// <inheritdoc />
    public byte[]? ObjectGet(string hash) => ObjectGetAsync(hash).GetAwaiter().GetResult();

    /// <inheritdoc />
    public FindingsResponse ObjectsVerify() => ObjectsVerifyAsync().GetAwaiter().GetResult();

    /// <summary>Asks the daemon to stop.</summary>
    public void Shutdown() => ShutdownAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Asks the daemon for a pairing code under an administrator's authority, from 0.6.0: on the runtime's own machine, over the local
    /// socket or loopback. The code is shown once; a problem when it cannot be issued.
    /// </summary>
    public (PairingIssued? Issued, Problem? Problem) IssuePairing(PairingIssueRequest request) => IssuePairingAsync(request).GetAwaiter().GetResult();

    /// <summary>Replaces the runtime certificate now, from 0.6.0; on the runtime's own machine. A problem when the daemon serves no TLS profile.</summary>
    public (CertificateRotated? Rotated, Problem? Problem) RotateCertificate(CertificateRotateRequest request) => RotateCertificateAsync(request).GetAwaiter().GetResult();

    /// <summary>The runtime identity's proof for a nonce over this connection's certificate, from 0.6.0; null when the runtime has no identity.</summary>
    public RuntimeIdentityProof? RuntimeIdentity(string nonce) => RuntimeIdentityAsync(nonce).GetAwaiter().GetResult();

    /// <summary>The OpenAPI document the daemon serves.</summary>
    public string OpenApi() => OpenApiAsync().GetAwaiter().GetResult();

    /// <inheritdoc />
    public void Dispose()
    {
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
