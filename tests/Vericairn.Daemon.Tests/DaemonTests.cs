// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Security;
using System.Numerics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>The daemon serves the API over a local socket; the SDK sees exactly what the embedded runtime sees; the contract matches the routes.</summary>
[Collection("daemon")]
public class DaemonTests
{
    public DaemonTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task TheDaemonServesWhatTheEmbeddedRuntimeSeesAndTheClientWritesThroughIt()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        DaemonEndpoint? discovered = DaemonHost.Discover(estate.Root);
        Assert.NotNull(discovered);
        Assert.Equal(host.Endpoint.Address, discovered.Address);
        Assert.Equal(OperatingSystem.IsWindows() ? "pipe" : "unix", discovered.Kind);
        Assert.True(VericairnClient.IsAlive(discovered));

        using var client = new VericairnClient(discovered);
        HealthResponse health = client.Health();
        Assert.Equal(ApiVersion.Current, health.Api);
        Assert.Equal(estate.Estate.Id, health.Estate);
        Assert.Equal(Environment.ProcessId, health.ProcessId);
        Assert.Equal("repository-synced", health.Profile);

        // Same status bytes through the socket as in-process.
        Assert.Equal(CanonicalJson.SerializePretty(host.Service.Status().Json), CanonicalJson.SerializePretty(client.Status().Json));
        Assert.Equal(host.Service.Check().Summary, client.Check().Summary);

        // A record authored through the API lands in the repository and is visible to both.
        using JsonDocument body = JsonDocument.Parse("""{"key":"W-1","kind":"ordinary","title":"Through the socket","intent":"API.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        WriteOutcome written = client.NewRecord(new NewRecordRequest("work-order", estate.BuilderId, body.RootElement));
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.True(File.Exists(Path.Combine(estate.Root, written.Written!.Path.Replace('/', Path.DirectorySeparatorChar))));
        RecordResponse? record = client.Record(written.Written.Id);
        Assert.NotNull(record);
        Assert.Equal(written.Written.Hash, record.Hash);
        Assert.True(record.Current);
        Assert.Null(client.Record("01NOPE"));
        Assert.Contains("# Brief: W-1", client.Brief("W-1")!.Markdown, StringComparison.Ordinal);
        Assert.Null(client.Brief("NOPE"));
        Assert.Equal(host.Service.Observe("W-1")!.Text, client.Observe("W-1")!.Text);

        // A rejected record comes back with findings, not an exception, and leaves nothing behind.
        using JsonDocument bad = JsonDocument.Parse("""{"key":"W-1","kind":"ordinary","title":"Duplicate key","intent":"API.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        WriteOutcome rejected = client.NewRecord(new NewRecordRequest("work-order", estate.BuilderId, bad.RootElement));
        Assert.False(rejected.Ok);
        Assert.Equal("rejected", rejected.Problem!.Code);
        Assert.Contains(rejected.Problem.Findings, f => f.Code == "work.duplicate-key");
        Assert.Single(Directory.EnumerateFiles(Path.Combine(estate.Root, "records", "work-order"), "*.json", SearchOption.AllDirectories));

        // A refusal names the permitted next action; a missing subject is not found.
        Assert.Equal("refused", client.NewRecord(new NewRecordRequest("nonsense", estate.BuilderId, body.RootElement)).Problem!.Code);
        Assert.Equal("not-found", client.Attempt(new AttemptRequest("01NOPE", estate.BuilderId, "x", "y")).Problem!.Code);

        // Decisions, releases, reconcile and recovery answer through the API too.
        Assert.Equal(host.Service.Decisions().Text, client.Decisions().Text);
        Assert.Null(client.Decision("01NOPE"));
        Assert.Empty(client.Releases());
        Assert.Empty(client.Reconcile());
        Assert.Equal(host.Service.RecoverInspect(), client.RecoverInspect());
        Assert.Equal("refused", client.Upgrade(new UpgradeRequest("9.9.9", null, true)).Problem!.Code);

        // Objects round-trip through the API with verification.
        (ObjectStored? stored, Problem? problem) = client.ObjectPut([1, 2, 3]);
        Assert.Null(problem);
        Assert.Equal(new byte[] { 1, 2, 3 }, client.ObjectGet(stored!.Hash));
        Assert.Null(client.ObjectGet("sha256:" + new string('0', 64)));
        Assert.Single(client.Objects());
        Assert.Equal(0, client.ObjectsVerify().Errors);

        // An export written by the daemon verifies; it carries records and bytes only.
        string exportPath = Path.Combine(Path.GetTempPath(), "vericairn-daemon-export-" + Records.Identity.Ulid.NewUlid().Value + ".zip");
        try
        {
            (ExportResponse? export, Problem? exportProblem) = client.Export(new ExportRequest(exportPath, estate.OperatorId));
            Assert.Null(exportProblem);
            Assert.Equal(4, export!.Records);
            Assert.Equal(1, export.Objects);
            Assert.True(File.Exists(exportPath));
            Assert.Equal("refused", client.Export(new ExportRequest(exportPath, estate.OperatorId)).Problem!.Code);
        }
        finally
        {
            File.Delete(exportPath);
        }

        // The contract names every route the daemon serves.
        using JsonDocument document = JsonDocument.Parse(client.OpenApi());
        foreach ((string method, string path) in OpenApiDocument.Routes)
        {
            Assert.True(document.RootElement.GetProperty("paths").TryGetProperty(path, out JsonElement item), path);
            Assert.True(item.TryGetProperty(method.ToLowerInvariant(), out _), method + " " + path);
        }

        // Shutdown through the API stops the daemon and removes the endpoint file. The daemon stops answering before its stop
        // finishes removing the file, so the test awaits that same stop (StopAsync returns the stop in flight, never a second one)
        // before it looks for the file; a second stop is harmless.
        client.Shutdown();
        await DaemonTestHome.WaitUntilGoneAsync(discovered);
        await host.StopAsync();
        Assert.Null(DaemonHost.Discover(estate.Root));
        await host.StopAsync();
    }

    [Fact]
    public async Task EveryMappedRouteIsInTheContractAndEveryContractRouteIsMapped()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(host.Endpoint.Loopback);
        HashSet<(string Method, string Path)> mapped = host.MappedRoutes().Where(r => r.Path != OpenApiDocument.Path).ToHashSet();
        HashSet<(string Method, string Path)> contract = OpenApiDocument.Routes.ToHashSet();
        Assert.NotEmpty(mapped);
        Assert.Empty(contract.Except(mapped));
        Assert.Empty(mapped.Except(contract));

        // The document itself is well formed: one operation id per operation, a request schema for every body.
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        Assert.Equal("3.0.3", document.RootElement.GetProperty("openapi").GetString());
        var operationIds = new List<string>();
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                operationIds.Add(operation.Value.GetProperty("operationId").GetString()!);
                if (operation.Value.TryGetProperty("requestBody", out JsonElement requestBody))
                {
                    string reference = requestBody.GetProperty("content").EnumerateObject().Single().Value.GetProperty("schema").GetProperty("$ref").GetString()!;
                    Assert.True(document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty(reference.Split('/')[^1], out _), reference);
                }
            }
        }

        Assert.Equal(operationIds.Count, operationIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task LoopbackIsPinnedToTheRuntimeCertificateAndAcceptsOnlyRequestsSignedByAnAdmittedDevice()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        DaemonEndpoint endpoint = DaemonHost.Discover(estate.Root)!;
        Assert.NotNull(endpoint.Loopback);
        Assert.StartsWith("sha256:", endpoint.Loopback.Certificate, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(DaemonHost.EndpointFilePath(estate.Root))!, RuntimeCertificate.FileName)));

        // A device for the builder, created and admitted through the local socket.
        using var socket = new VericairnClient(endpoint);
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(estate.BuilderId, "Test device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        WriteOutcome admission = socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, estate.OperatorId, "Loopback test."));
        Assert.True(admission.Ok, admission.Problem?.Message);
        DeviceRequestSigner? signer = DeviceRequestSigner.Find(device.Written.Id);
        Assert.NotNull(signer);

        // Signed requests over pinned TLS see the same estate; writes go through the same authority.
        using VericairnClient loopback = VericairnClient.Loopback(endpoint, signer);
        Assert.Equal("loopback", loopback.Transport);
        Assert.Equal(socket.Health().Estate, loopback.Health().Estate);
        Assert.Equal(CanonicalJson.SerializePretty(socket.Status().Json), CanonicalJson.SerializePretty(loopback.Status().Json));
        using JsonDocument body = JsonDocument.Parse("""{"key":"W-2","kind":"ordinary","title":"Over loopback","intent":"Signed.","scope":["x"],"exitQuestion":"?","inputs":[],"deliverables":["y"]}""");
        WriteOutcome written = loopback.NewRecord(new NewRecordRequest("work-order", estate.BuilderId, body.RootElement));
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.Contains("# Brief: W-2", loopback.Brief("W-2")!.Markdown, StringComparison.Ordinal);
        (ObjectStored? stored, _) = loopback.ObjectPut([9, 8, 7]);
        Assert.Equal(new byte[] { 9, 8, 7 }, loopback.ObjectGet(stored!.Hash));

        // Unsigned requests are denied with the reason; the local socket needs no signature.
        using VericairnClient unsigned = VericairnClient.Loopback(endpoint, null);
        HttpRequestException denied = Assert.Throws<HttpRequestException>(() => unsigned.Health());
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // A certificate other than the runtime's is refused before any request is sent.
        DaemonEndpoint impostor = endpoint with { Loopback = endpoint.Loopback with { Certificate = "sha256:" + new string('0', 64) } };
        using VericairnClient pinned = VericairnClient.Loopback(impostor, signer);
        Assert.ThrowsAny<HttpRequestException>(() => pinned.Health());

        // Revocation ends loopback access from now on, with no restart. A revocation names the second it takes effect, and the
        // device's own records of that second would fall after it, so the test waits for the next second like an operator would.
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        WriteOutcome revocation = socket.DeviceRevoke(new RevokeRequest(device.Written.Id, estate.OperatorId, "Loopback test over."));
        Assert.True(revocation.Ok, revocation.Problem?.Message);
        HttpRequestException revoked = Assert.Throws<HttpRequestException>(() => loopback.Health());
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task EveryFormOfAnAcceptedLoopbackSignatureIsRefusedAsARepeatAndAFreshSignatureIsNot()
    {
        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        DaemonEndpoint endpoint = DaemonHost.Discover(estate.Root)!;
        using var socket = new VericairnClient(endpoint);
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(estate.BuilderId, "Test device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        WriteOutcome admission = socket.DeviceAdmit(new AdmitRequest(device.Written!.Id, estate.OperatorId, "Replay test."));
        Assert.True(admission.Ok, admission.Problem?.Message);
        DeviceRequestSigner signer = DeviceRequestSigner.Find(device.Written.Id)!;
        string publicKey = EstateModel.BodyString(estate.Load().ById[device.Written.Id], "publicKey")!;

        // One request, signed once and accepted.
        string signedAt = RequestSigning.FormatTime(DateTimeOffset.UtcNow);
        byte[] digest = RequestSigning.Digest("GET", "/v1/health", signedAt, []);
        byte[] signature = signer.Sign(digest);
        using HttpClient http = PinnedLoopback(endpoint);
        Assert.Equal(HttpStatusCode.OK, (await SendSigned(http, signer.DeviceId, signedAt, Base64Url.EncodeToString(signature))).Status);

        // ECDSA signatures are malleable: (r, n - s) verifies over the same request as (r, s) does. A copy carrying that twin, one
        // carrying the same bytes spelled with base64 padding, and the exact copy are all the signature already used.
        byte[] twin = Malleate(signature);
        Assert.True(RecordSigning.Verify(publicKey, digest, Base64Url.EncodeToString(twin)));
        foreach (string copy in new[] { Base64Url.EncodeToString(twin), Base64Url.EncodeToString(signature) + "==", Base64Url.EncodeToString(signature) })
        {
            (HttpStatusCode status, string body) = await SendSigned(http, signer.DeviceId, signedAt, copy);
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Contains("already used", body, StringComparison.Ordinal);
        }

        // The device signing the same request again is a new signature, and is accepted: a client may send one request twice in a second.
        Assert.Equal(HttpStatusCode.OK, (await SendSigned(http, signer.DeviceId, signedAt, Base64Url.EncodeToString(signer.Sign(digest)))).Status);
    }

    [Fact]
    public async Task ADocumentReviewsStatedIndependenceTravelsThroughTheApi()
    {
        // The request's independence, added to API v1 in 0.6.0, is in the contract and reaches the runtime through the socket: an
        // estate file's revisions decide its reviewer's independence, so a statement that differs is refused and one that agrees is
        // recorded.
        using JsonDocument contract = JsonDocument.Parse(OpenApiDocument.Json);
        Assert.True(contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DocumentReviewRequest").GetProperty("properties").TryGetProperty("independence", out _));
        using var estate = new TestEstate(recordFormat: 2);
        estate.WriteFile("docs/guide.md", "# Guide\n");
        string declaration = estate.Add("living-document", estate.OperatorId, """{"path":"docs/guide.md","title":"Guide","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"Says how to use it."}""").Id;
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        using var client = new VericairnClient(host.Endpoint);
        WriteOutcome refused = client.DocumentReview(declaration, new DocumentReviewRequest(estate.BuilderId, "current", "Read it.", Independence: "self"));
        Assert.Equal("refused", refused.Problem?.Code);
        Assert.Contains("independent-agent for this review, not self", refused.Problem!.Message, StringComparison.Ordinal);
        WriteOutcome written = client.DocumentReview(declaration, new DocumentReviewRequest(estate.BuilderId, "current", "Read it.", Independence: "independent-agent"));
        Assert.True(written.Ok, written.Problem?.Message);
        Assert.Equal("independent-agent", client.Record(written.Written!.Id)!.Envelope.GetProperty("body").GetProperty("independence").GetString());
    }

    [Fact]
    public void TheContractNamesEveryProblemCodeAClientCanReceiveAndMarksConflictReserved()
    {
        using JsonDocument document = JsonDocument.Parse(OpenApiDocument.Json);
        string description = document.RootElement.GetProperty("info").GetProperty("description").GetString()!;
        Match codes = Regex.Match(description, @"stable code \((?<codes>[^)]*)\)");
        Assert.True(codes.Success, description);
        string[] listed = codes.Groups["codes"].Value.Split([',', ';'], StringSplitOptions.TrimEntries);
        foreach (string code in new[] { "refused", "rejected", "not-found", "invalid", "denied", "provider" })
        {
            Assert.Contains(code, listed);
        }

        Assert.Contains("conflict is reserved and nothing answers with it yet", listed);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task OnWindowsTheNamedPipeAllowsOnlyTheUserThatStartedTheDaemon()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The local socket is a named pipe on Windows only; elsewhere it is an owner-only Unix domain socket.");
        }

        using var estate = new TestEstate();
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("pipe", host.Endpoint.Kind);
        using var client = new NamedPipeClientStream(".", host.Endpoint.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, TestContext.Current.CancellationToken);

        // Kestrel's named-pipe transport creates the pipe with CurrentUserOnly, its default: the access list allows the owner of the
        // daemon's process token, the user's own account, or for an elevated process the owner Windows gives it (the Administrators
        // group by default). No one else is allowed, LocalSystem aside.
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        IdentityReference[] daemonsUser = [current.User!, current.Owner!];
        IdentityReference[] allowed = [.. daemonsUser, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)];
        PipeSecurity security = client.GetAccessControl();
        Assert.Contains(security.GetOwner(typeof(SecurityIdentifier))!, daemonsUser);
        List<AccessRule> allows = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<AccessRule>().Where(r => r.AccessControlType == AccessControlType.Allow).ToList();
        Assert.Contains(allows, r => daemonsUser.Contains(r.IdentityReference));
        Assert.All(allows, r => Assert.Contains(r.IdentityReference, allowed));
    }

    [Fact]
    public async Task ARotatedDeviceSignsRequestsWithItsNewKeyAndTheReplacedKeyIsRefused()
    {
        using var estate = new TestEstate(recordFormat: 2);
        await using DaemonHost host = await DaemonHost.StartAsync(estate.Root, cancellationToken: TestContext.Current.CancellationToken);
        DaemonEndpoint endpoint = DaemonHost.Discover(estate.Root)!;
        using var socket = new VericairnClient(endpoint);
        WriteOutcome device = socket.DeviceCreate(new DeviceCreateRequest(estate.BuilderId, "Test device", "file"));
        Assert.True(device.Ok, device.Problem?.Message);
        string id = device.Written!.Id;
        // In record format 2 every admission is signed, so the operator's own device, the first authority, comes first.
        WriteOutcome operatorDevice = socket.DeviceCreate(new DeviceCreateRequest(estate.OperatorId, "Operator device", "file"));
        Assert.True(operatorDevice.Ok, operatorDevice.Problem?.Message);
        Assert.True(socket.DeviceAdmit(new AdmitRequest(operatorDevice.Written!.Id, estate.OperatorId, "The first authority.")).Ok);
        Assert.True(socket.DeviceAdmit(new AdmitRequest(id, estate.OperatorId, "Rotation test.")).Ok);

        // The key the rotation replaces is kept aside, to show the daemon refuses it afterwards.
        string keys = Path.Combine(VericairnHome.Path(), "keys");
        string retired = Records.Identity.Ulid.NewUlid().Value;
        File.Copy(Path.Combine(keys, id + ".p8"), Path.Combine(keys, retired + ".p8"));
        var store = new FileKeyStore(VericairnHome.Path());
        try
        {
            WriteOutcome renamed = socket.DeviceRename(new DeviceRenameRequest(id, "Test device, renamed"));
            Assert.True(renamed.Ok, renamed.Problem?.Message);
            WriteOutcome rotated = socket.DeviceRotate(new DeviceRotateRequest(id));
            Assert.True(rotated.Ok, rotated.Problem?.Message);
            DeviceRow row = Assert.Single(socket.Devices(), d => d.Id == id);
            Assert.Equal("Test device, renamed", row.Name);
            Assert.Equal("admitted", row.Admission);
            Assert.True(row.HeldHere);

            // Requests signed with the new key are accepted; the replaced key is refused although it names the same device.
            using VericairnClient current = VericairnClient.Loopback(endpoint, DeviceRequestSigner.Find(id));
            Assert.Equal(socket.Health().Estate, current.Health().Estate);
            using VericairnClient replaced = VericairnClient.Loopback(endpoint, new KeySigner(store, retired, id));
            HttpRequestException refused = Assert.Throws<HttpRequestException>(() => replaced.Health());
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));
        }
        finally
        {
            store.Remove(retired);
        }
    }

    [Fact]
    public async Task ARunningDaemonRefusesASecondOwnerOfTheSameEstate()
    {
        using var estate = new TestEstate();
        await using DaemonHost first = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using DaemonHost second = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        });
        Assert.Contains("already serves", refused.Message, StringComparison.Ordinal);
        Assert.True(VericairnClient.IsAlive(first.Endpoint));

        // After the first stops, a stale endpoint file or socket never blocks the next owner.
        await first.StopAsync();
        File.WriteAllText(DaemonHost.EndpointFilePath(estate.Root), JsonSerializer.Serialize(first.Endpoint, ProtocolJson.Options));
        await using DaemonHost next = await DaemonHost.StartAsync(estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(VericairnClient.IsAlive(next.Endpoint));
    }

    [Fact]
    public void TheRuntimeCertificateIsKeptOwnerOnlyAndRenewedWhenCloseToExpiry()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vericairn-certificate-" + Records.Identity.Ulid.NewUlid().Value);
        try
        {
            using var first = RuntimeCertificate.Load(directory);
            using var again = RuntimeCertificate.Load(directory);
            Assert.Equal(RuntimeCertificate.Fingerprint(first), RuntimeCertificate.Fingerprint(again));
            Assert.True(first.HasPrivateKey);
            Assert.True(first.NotAfter.ToUniversalTime() - DateTime.UtcNow > RuntimeCertificate.Validity - TimeSpan.FromDays(1));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, RuntimeCertificate.FileName)));
            }

            File.WriteAllText(Path.Combine(directory, RuntimeCertificate.FileName), "not a certificate");
            using var replaced = RuntimeCertificate.Load(directory);
            Assert.NotEqual(RuntimeCertificate.Fingerprint(first), RuntimeCertificate.Fingerprint(replaced));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ServicePlansNameThePerUserServiceManagerOfEachPlatform()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\Estates\example estate" : "/home/someone/estates/example estate";
        string[] launcher = [OperatingSystem.IsWindows() ? @"C:\Tools\vericairn.exe" : "/usr/local/bin/vericairn"];
        ServicePlan mac = ServiceInstaller.Plan("macos", root, "01ESTATE", launcher, "/Users/someone", "/Users/someone/Library/Application Support/Vericairn", 501);
        Assert.Equal("org.vericairn.runtime.01estate", mac.Label);
        Assert.EndsWith(Path.Combine("Library", "LaunchAgents", "org.vericairn.runtime.01estate.plist"), mac.UnitPath, StringComparison.Ordinal);
        Assert.Contains("<string>--estate</string>", mac.UnitContent, StringComparison.Ordinal);
        Assert.Contains($"<string>{root}</string>", mac.UnitContent, StringComparison.Ordinal);
        Assert.Contains("<key>KeepAlive</key>", mac.UnitContent, StringComparison.Ordinal);
        Assert.Equal(["launchctl", "bootstrap", "gui/501", mac.UnitPath], mac.Install.Single());
        Assert.Equal(["launchctl", "bootout", "gui/501/org.vericairn.runtime.01estate"], mac.Uninstall.Single());

        ServicePlan linux = ServiceInstaller.Plan("linux", root, "01ESTATE", launcher, "/home/someone", "/home/someone/.config/vericairn", 1000);
        Assert.Equal("vericairn-runtime-01estate", linux.Label);
        Assert.Contains(Path.Combine("systemd", "user", "vericairn-runtime-01estate.service"), linux.UnitPath, StringComparison.Ordinal);
        Assert.Contains($"ExecStart=\"{launcher[0].Replace("\\", "\\\\", StringComparison.Ordinal)}\" \"--estate\" \"{root.Replace("\\", "\\\\", StringComparison.Ordinal)}\" \"runtime\" \"serve\"", linux.UnitContent, StringComparison.Ordinal);
        Assert.Contains("Restart=on-failure", linux.UnitContent, StringComparison.Ordinal);
        Assert.Equal(["systemctl", "--user", "enable", "--now", "vericairn-runtime-01estate.service"], linux.Install[1]);
        Assert.Equal(["systemctl", "--user", "daemon-reload"], linux.AfterUninstall.Single());

        ServicePlan windows = ServiceInstaller.Plan("windows", root, "01ESTATE", launcher, @"C:\Users\someone", @"C:\Users\someone\AppData\Local\Vericairn", null);
        Assert.Equal("Vericairn Runtime 01ESTATE", windows.Label);
        Assert.EndsWith(".xml", windows.UnitPath, StringComparison.Ordinal);
        Assert.Contains("<LogonTrigger>", windows.UnitContent, StringComparison.Ordinal);
        Assert.Contains($"<Arguments>--estate &quot;{root}&quot; runtime serve</Arguments>", windows.UnitContent, StringComparison.Ordinal);
        Assert.Equal(["schtasks", "/Create", "/F", "/TN", windows.Label, "/XML", windows.UnitPath], windows.Install[0]);
        Assert.Equal(["schtasks", "/Delete", "/F", "/TN", windows.Label], windows.Uninstall[1]);

        Assert.Throws<PlatformNotSupportedException>(() => ServiceInstaller.Plan("other", root, "01ESTATE", launcher, "/home/someone", "/home/someone/.config/vericairn", null));
        Assert.NotEmpty(ServiceInstaller.CurrentLauncher());
    }

    /// <summary>A client of the daemon's loopback TLS, pinned to the runtime certificate as the SDK pins it, that signs nothing itself.</summary>
    private static HttpClient PinnedLoopback(DaemonEndpoint endpoint)
    {
        LoopbackEndpoint loopback = endpoint.Loopback!;
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && "sha256:" + Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData())) == loopback.Certificate,
            },
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"https://{loopback.Host}:{loopback.Port}/") };
    }

    /// <summary>Sends GET /v1/health with the three request-signing headers as given.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> SendSigned(HttpClient http, string device, string signedAt, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/health", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(RequestSigning.DeviceHeader, device);
        request.Headers.TryAddWithoutValidation(RequestSigning.SignedAtHeader, signedAt);
        request.Headers.TryAddWithoutValidation(RequestSigning.SignatureHeader, signature);
        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The twin of an IEEE P1363 ECDSA P-256 signature (r, s): (r, n - s), which verifies wherever the signature does.</summary>
    private static byte[] Malleate(byte[] signature)
    {
        BigInteger order = BigInteger.Parse("0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        BigInteger twin = order - new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        byte[] copy = (byte[])signature.Clone();
        Array.Clear(copy, 32, 32);
        twin.TryWriteBytes(copy.AsSpan(64 - twin.GetByteCount(isUnsigned: true)), out _, isUnsigned: true, isBigEndian: true);
        return copy;
    }

    /// <summary>Signs requests in a device's name with a key held under another id: the key a rotation replaced.</summary>
    private sealed class KeySigner(IDeviceKeyStore store, string keyId, string deviceId) : IRequestSigner
    {
        public string DeviceId => deviceId;

        public byte[] Sign(ReadOnlySpan<byte> digest) => store.Sign(keyId, digest);
    }
}
