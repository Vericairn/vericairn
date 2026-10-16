// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// Pairing under an administrator's authority (design/identity-and-connectivity.md § 4): a short-lived, single-use code limited in
/// attempts authenticates the exchange both ways; the device pins the runtime's identity, and the runtime records the device's key,
/// principal, descriptive identity and permitted profiles, and its admission.
/// </summary>
[Collection("daemon")]
public class PairingTests
{
    public PairingTests()
    {
        DaemonTestHome.Ensure();
    }

    [Fact]
    public async Task APairingRecordsTheDeviceWithItsOwnKeyAsProofAndItsAdmissionUnderTheAdministrator()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", issued.Code);
        Assert.Equal(["local-network"], issued.Profiles);
        Assert.Equal(PairingProtocol.Attempts, issued.Attempts);
        Assert.Equal(harness.Address, issued.Address);
        Assert.Equal(RuntimeIdentityScheme.Fingerprint(harness.Host.IdentityPublicKey!), issued.Identity);
        Assert.Equal("Test Builder", issued.PrincipalName);

        RuntimeConnection connection = harness.Connections.Add(issued.Address, issued.Code.ToLowerInvariant(), new PairingOptions("Build laptop"));

        // The device pinned the runtime's identity and the estate; its key is held on its own machine only.
        Assert.Equal(harness.Host.IdentityPublicKey, connection.Identity);
        Assert.Equal(harness.Estate.Estate.Id, connection.Estate);
        Assert.Equal("Test Estate", connection.EstateName);
        Assert.True(harness.DeviceKeys.Has(connection.Device));
        Assert.False(new FileKeyStore(VericairnHome.Path()).Has(connection.Device));
        RuntimeConnection kept = Assert.Single(harness.Connections.List());
        Assert.Equal((connection.Id, connection.Address, connection.Device, connection.Identity), (kept.Id, kept.Address, kept.Device, kept.Identity));
        Assert.Equal(["local-network"], kept.Profiles);

        // The runtime recorded the device as it presented itself, signed by the key it presented, and its admission by the operator,
        // signed by the operator's admitted device; the estate is valid with both.
        EstateModel model = harness.Estate.Load();
        RecordEnvelope device = model.ById[connection.Device];
        Assert.Equal("device", device.Type);
        Assert.Equal(harness.Estate.BuilderId, device.By);
        Assert.Equal("Build laptop", EstateModel.BodyString(device, "name"));
        Assert.Equal(VericairnHome.Platform(), EstateModel.BodyString(device, "platform"));
        Assert.Equal("file-owner-only", EstateModel.BodyString(device, "custody"));
        Assert.Equal(connection.DevicePublicKey, EstateModel.BodyString(device, "publicKey"));
        Assert.Equal(["local-network"], device.Body.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(connection.Device, Assert.Single(device.Signatures).Device);
        RecordEnvelope admission = Assert.Single(model.About(connection.Device, "admission"));
        Assert.Equal(harness.Estate.OperatorId, admission.By);
        Assert.Equal(device.ComputedHash, admission.Subject!.Hash);
        Assert.Equal(harness.OperatorDevice, Assert.Single(admission.Signatures).Device);
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Equal(0, harness.Socket.Check().Errors);
        DeviceRow row = Assert.Single(harness.Socket.Devices(), d => d.Id == connection.Device);
        Assert.Equal("admitted", row.Admission);
        Assert.False(row.HeldHere);
        Assert.Equal(["local-network"], row.Profiles);
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(connection).State);
    }

    [Fact]
    public async Task AnExpiredPairingCodeIsRefused()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);
        harness.Time.Advance(PairingProtocol.Lifetime);
        PairingException expired = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone")));
        Assert.Equal("denied", expired.Code);
        Assert.Contains("expired", expired.Message, StringComparison.Ordinal);
        NothingPaired(harness);
    }

    [Fact]
    public async Task AUsedPairingCodeIsRefused()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);
        RuntimeConnection first = harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone"));
        PairingException reused = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Second phone")));
        Assert.Equal("denied", reused.Code);
        Assert.Contains("used already", reused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(first.Device, reused.Message, StringComparison.Ordinal);
        Assert.Single(harness.Socket.Devices(), d => d.Principal == harness.Estate.BuilderId);
        Assert.Single(harness.Connections.List());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(harness.DeviceHome, "keys")));
    }

    [Fact]
    public async Task AWrongPairingCodeIsRefusedAndRecordsNothing()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);

        // Mistyped, or never issued here: no code the runtime holds matches the hello, which is refused alike, and nothing is made.
        string code = issued.Code.Replace("-", string.Empty, StringComparison.Ordinal);
        string mistyped = code[..^1] + (code[^1] == '0' ? '1' : '0');
        foreach (string wrongCode in new[] { mistyped, PairingProtocol.NewCode() })
        {
            PairingException wrong = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, wrongCode, new PairingOptions("Phone")));
            Assert.Equal("denied", wrong.Code);
            Assert.Contains("No pairing here matches this code", wrong.Message, StringComparison.Ordinal);
        }

        NothingPaired(harness);

        // Whoever holds the code but changes the confirmation is refused, and the exchange ends; each such confirmation uses an attempt.
        using HttpClient pairing = PairingClient(harness);
        (PairingHello hello, PairingChallenge challenge) = await Hello(harness, pairing, issued.Code);
        var forged = new PairingConfirm(challenge.RuntimeNonce, new PairingDevice("Planted", "linux", "file-owner-only", harness.Host.IdentityPublicKey!, "none"), "AAAA", PairingProtocol.NewNonce());
        (HttpStatusCode status, Problem? problem) = await Confirm(pairing, forged);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("does not match this pairing code", problem!.Message, StringComparison.Ordinal);
        Assert.Contains("No exchange of a pairing code is open", (await Confirm(pairing, forged)).Problem!.Message, StringComparison.Ordinal);
        Assert.NotNull(hello);
        NothingPaired(harness);

        // After five confirmations that did not match, the right code is refused too.
        for (int attempt = 2; attempt <= PairingProtocol.Attempts; attempt++)
        {
            (_, PairingChallenge next) = await Hello(harness, pairing, issued.Code);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Confirm(pairing, forged with { RuntimeNonce = next.RuntimeNonce })).Status);
        }

        PairingException spent = Assert.Throws<PairingException>(() => harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone")));
        Assert.Contains("confirmations that did not match it", spent.Message, StringComparison.Ordinal);
        NothingPaired(harness);
    }

    [Fact]
    public async Task AtMostSixteenCodesWaitAndARestartedRuntimeForgetsThemAll()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        List<PairingIssued> waiting = [.. Enumerable.Range(0, 16).Select(_ => harness.Issue(harness.Estate.BuilderId))];
        (PairingIssued? seventeenth, Problem? problem) = harness.Socket.IssuePairing(new PairingIssueRequest(harness.Estate.BuilderId, harness.Estate.OperatorId));
        Assert.Null(seventeenth);
        Assert.Contains("16 pairing codes are waiting", problem!.Message, StringComparison.Ordinal);

        // An expired code no longer waits.
        harness.Time.Advance(PairingProtocol.Lifetime);
        Assert.NotNull(harness.Issue(harness.Estate.BuilderId));

        // The codes live in the daemon's memory only: after a restart none of them pairs anything.
        PairingIssued last = harness.Issue(harness.Estate.BuilderId);
        await harness.Host.StopAsync();
        await using DaemonHost restarted = await DaemonHost.StartAsync(harness.Estate.Root, options: new DaemonOptions { LocalNetwork = new IPEndPoint(IPAddress.Loopback, 0), IdentityStore = new FileKeyStore(VericairnHome.Path()) }, cancellationToken: TestContext.Current.CancellationToken);
        string address = $"{restarted.Endpoint.LocalNetwork!.Host}:{restarted.Endpoint.LocalNetwork.Port}";
        PairingException forgotten = Assert.Throws<PairingException>(() => harness.Connections.Add(address, last.Code, new PairingOptions("Phone")));
        Assert.Contains("No pairing here matches this code", forgotten.Message, StringComparison.Ordinal);
        Assert.Equal(harness.Host.IdentityPublicKey, restarted.IdentityPublicKey);
    }

    [Fact]
    public async Task ACodeIsIssuedOnlyUnderAnAdministratorsAuthority()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        string op = harness.Estate.OperatorId;
        string builder = harness.Estate.BuilderId;
        Assert.Contains("neither an operator nor holds the 'administer' scope", Refused(harness, new PairingIssueRequest(builder, builder)), StringComparison.Ordinal);
        Assert.Contains("No principal", Refused(harness, new PairingIssueRequest(Records.Identity.Ulid.NewUlid().Value, op)), StringComparison.Ordinal);
        Assert.Contains("Unknown profile 'bluetooth'", Refused(harness, new PairingIssueRequest(builder, op, ["bluetooth"])), StringComparison.Ordinal);

        // A person holding the 'administer' scope pairs devices too, and the admission is theirs, signed by their admitted device here.
        using JsonDocument person = JsonDocument.Parse("""{"kind":"human","name":"Second Administrator","roles":["contributor"]}""");
        string admin = harness.Socket.NewRecord(new NewRecordRequest("principal", op, person.RootElement)).Written!.Id;
        using JsonDocument decision = JsonDocument.Parse($$"""{"title":"Administration","statement":"Administer devices.","summary":"A second administrator.","synthesis":"A grant to administer.","effects":["administer"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-15T00:00:00Z","stake":"medium","notAuthorized":[]}""");
        string basis = harness.Socket.NewRecord(new NewRecordRequest("decision", op, decision.RootElement)).Written!.Id;
        using JsonDocument grant = JsonDocument.Parse($$"""{"grantee":"{{admin}}","scope":["administer"],"reserved":[],"conditions":[],"basis":"{{basis}}"}""");
        Assert.True(harness.Socket.NewRecord(new NewRecordRequest("authority", op, grant.RootElement)).Ok);
        Assert.Contains("principal " + admin + " has none", Refused(harness, new PairingIssueRequest(builder, admin)), StringComparison.Ordinal);
        WriteOutcome laptop = harness.Socket.DeviceCreate(new DeviceCreateRequest(admin, "Administrator laptop", "file", Presence: false));
        Assert.True(harness.Socket.DeviceAdmit(new AdmitRequest(laptop.Written!.Id, op, "The second administrator's laptop.")).Ok);
        (PairingIssued? issued, Problem? problem) = harness.Socket.IssuePairing(new PairingIssueRequest(builder, admin));
        Assert.True(issued is not null, problem?.Message);
        RuntimeConnection connection = harness.Connections.Add(issued.Address, issued.Code, new PairingOptions("Phone"));
        RecordEnvelope admission = Assert.Single(harness.Estate.Load().About(connection.Device, "admission"));
        Assert.Equal(admin, admission.By);
        Assert.Equal(laptop.Written.Id, Assert.Single(admission.Signatures).Device);
    }

    [Fact]
    public async Task AnyoneBetweenTheDeviceAndTheRuntimeIsFoundOutAndNothingIsRecorded()
    {
        await using LocalNetworkHarness harness = await LocalNetworkHarness.StartAsync();
        PairingIssued issued = harness.Issue(harness.Estate.BuilderId);

        // A relay that ends TLS with its own certificate and passes the exchange on to the runtime: the device's hello is over the
        // relay's certificate, which is not the one the runtime presented, so it matches no code there, and nothing goes further.
        string directory = Path.Combine(harness.DeviceHome, "relay");
        using X509Certificate2 own = RuntimeCertificate.Load(directory);
        using HttpClient upstream = PairingClient(harness);
        await using var relay = new Relay(own, upstream);
        PairingException found = Assert.Throws<PairingException>(() => harness.Connections.Add(relay.Address, issued.Code, new PairingOptions("Phone")));
        Assert.Equal("denied", found.Code);
        Assert.Contains("No pairing here matches this code over this connection", found.Message, StringComparison.Ordinal);
        NothingPaired(harness);

        // The code is still the device's to use directly, with all its attempts.
        RuntimeConnection connection = harness.Connections.Add(harness.Address, issued.Code, new PairingOptions("Phone"));
        Assert.Equal(ConnectionStates.Current, harness.Connections.Probe(connection).State);
    }

    [Fact]
    public void ThePairingCodeIsSixteenCharactersAllOfThemSecret()
    {
        HashSet<string> codes = [.. Enumerable.Range(0, 64).Select(_ => PairingProtocol.NewCode())];
        Assert.Equal(64, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{16}$", c));
        Assert.Equal(80, PairingProtocol.CodeLength * 5);
        string code = codes.First();
        string shown = PairingProtocol.Display(code);
        Assert.Matches("^([0-9A-HJKMNP-TV-Z]{4}-){3}[0-9A-HJKMNP-TV-Z]{4}$", shown);

        // As typed: case, hyphens and spaces do not matter, and I, L and O read as 1, 1 and 0.
        Assert.True(PairingProtocol.TryParse(" " + shown.ToLowerInvariant().Replace("-", " - ", StringComparison.Ordinal) + " ", out string secret));
        Assert.Equal(code, secret);
        Assert.True(PairingProtocol.TryParse("ABCD-EFGH-JKMN-PQRO", out string read));
        Assert.EndsWith("PQR0", read, StringComparison.Ordinal);
        Assert.True(PairingProtocol.TryParse("ilIL-0000-0000-0000", out string ones));
        Assert.StartsWith("1111", ones, StringComparison.Ordinal);
        Assert.False(PairingProtocol.TryParse("ABCD-EFGH-JKMN-PQRU", out _));
        Assert.False(PairingProtocol.TryParse("ABCD-EFGH-JKMN-PQR", out _));
        Assert.False(PairingProtocol.TryParse("ABCD-EFGH-JKMN-PQRST", out _));

        // Each side's key differs, and a different code gives different keys.
        (byte[] runtime, byte[] device) = PairingProtocol.Keys(secret);
        Assert.NotEqual(runtime, device);
        Assert.NotEqual(runtime, PairingProtocol.Keys(secret[..^1] + (secret[^1] == '0' ? '1' : '0')).Runtime);
    }

    private static string Refused(LocalNetworkHarness harness, PairingIssueRequest request)
    {
        (PairingIssued? issued, Problem? problem) = harness.Socket.IssuePairing(request);
        Assert.Null(issued);
        Assert.Equal("refused", problem!.Code);
        return problem.Message;
    }

    /// <summary>Nothing of a failed pairing remains: no device beyond the operator's, no key and no connection on the device's machine.</summary>
    private static void NothingPaired(LocalNetworkHarness harness)
    {
        Assert.Equal([harness.OperatorDevice], harness.Socket.Devices().Select(d => d.Id));
        Assert.Empty(harness.Connections.List());
        string keys = Path.Combine(harness.DeviceHome, "keys");
        Assert.True(!Directory.Exists(keys) || !Directory.EnumerateFiles(keys).Any());
    }

    /// <summary>A client of the pairing exchange without any credential, pinned to the runtime's certificate.</summary>
    internal static HttpClient PairingClient(LocalNetworkHarness harness)
    {
        string pinned = harness.Host.Endpoint.Loopback!.Certificate;
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null && RuntimeIdentityScheme.CertificateFingerprint(certificate.GetRawCertData()) == pinned,
            },
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri($"https://{harness.Address}/") };
    }

    /// <summary>A hello that proves the code, over the runtime certificate the pinned client sees, and the challenge it gets.</summary>
    internal static async Task<(PairingHello Hello, PairingChallenge Challenge)> Hello(LocalNetworkHarness harness, HttpClient client, string code)
    {
        Assert.True(PairingProtocol.TryParse(code, out string secret));
        (_, byte[] deviceKey) = PairingProtocol.Keys(secret);
        string nonce = PairingProtocol.NewNonce();
        var hello = new PairingHello(nonce, PairingProtocol.HelloMac(deviceKey, nonce, harness.Host.Endpoint.Loopback!.Certificate));
        using HttpResponseMessage response = await client.PostAsJsonAsync(new Uri("/v1/pairing/hello", UriKind.Relative), hello, ProtocolJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (hello, (await response.Content.ReadFromJsonAsync<PairingChallenge>(ProtocolJson.Options, TestContext.Current.CancellationToken))!);
    }

    internal static async Task<(HttpStatusCode Status, Problem? Problem)> Confirm(HttpClient client, PairingConfirm confirm)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(new Uri("/v1/pairing/confirm", UriKind.Relative), confirm, ProtocolJson.Options, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<Problem>(ProtocolJson.Options, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A TLS-terminating relay on 127.0.0.1: it presents its own certificate to whoever connects and passes each pairing request on
    /// to the runtime, returning the runtime's answer unchanged.
    /// </summary>
    private sealed class Relay : IAsyncDisposable
    {
        private readonly WebApplication app;

        public Relay(X509Certificate2 certificate, HttpClient upstream)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrelHttpsConfiguration();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            app = builder.Build();
            app.Run(async context =>
            {
                using var buffer = new MemoryStream();
                await context.Request.Body.CopyToAsync(buffer);
                using var content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                using HttpResponseMessage answer = await upstream.PostAsync(new Uri(context.Request.Path.Value!, UriKind.Relative), content);
                context.Response.StatusCode = (int)answer.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.Body.WriteAsync(await answer.Content.ReadAsByteArrayAsync());
            });
            app.StartAsync().GetAwaiter().GetResult();
        }

        public string Address => new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Authority;

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
