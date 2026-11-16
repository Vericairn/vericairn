// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;
using Vericairn.Sdk;

namespace Vericairn.Daemon.Tests;

/// <summary>
/// ID-16 (0.9.4): the daemon names the local-socket profile, one of the two a presence session opens on, from the listener that accepted
/// the connection; and a release build starts the runtime's service with the .NET diagnostics channel off. The helper is an in-process
/// stand-in and the build, platform and requirement are a test host's.
/// </summary>
[Collection("daemon")]
public sealed class PresenceSessionDaemonTests
{
    public PresenceSessionDaemonTests() => DaemonTestHome.Ensure();

    [Fact]
    public async Task AnOperatorCloseOverTheLocalSocketSignsUnderOnePresenceCheck()
    {
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        await using DaemonHost host = await DaemonTestHome.StartAsync(f.Estate.Root, loopback: false, cancellationToken: TestContext.Current.CancellationToken);
        var custody = new RecordingCustody(VericairnHome.Path());
        var helpers = new List<PresenceHelperStandIn>();
        host.Service.KeyStores = [custody];
        host.Service.PresenceRequired = device => device == f.Device;
        host.Service.PresenceHost = new PresenceSessionHost
        {
            Build = PresenceSessionBuild.Parse("ABCDE12345", "vericairn-enclave", "26"), MacOS = () => true, MacOSMajor = () => 26, Requirement = new Accepting(),
            HelperOf = _ => "helper", Setting = _ => PresenceSetting.Default, SameUserAgentRun = () => false, CheckWait = TimeSpan.Zero,
            Start = signer =>
            {
                var key = new FileKeyStore(VericairnHome.Path());
                var helper = new PresenceHelperStandIn(digest => key.Sign(signer.DeviceId, digest), key.PublicKey(signer.DeviceId)!);
                helpers.Add(helper);
                return helper;
            },
        };
        // The daemon's service names no profile of its own: only its local socket's listener does.
        Assert.False(host.Service.HostsEmbedded);
        using var client = new VericairnClient(host.Endpoint);
        OperatorCloseCandidatesResponse listed = client.OperatorCloseCandidates();

        OperatorCloseResponse closed = client.OperatorClose(new OperatorCloseRequest(f.Estate.OperatorId, [.. listed.Items.Select(i => i.ToSelection())], All: true));

        Assert.True(closed.Ok, closed.Problem?.Message);
        Assert.Equal(2, closed.Written.Count);
        PresenceHelperStandIn session = Assert.Single(helpers);
        Assert.Equal(2, session.SignedHashes.Count);
        Assert.Empty(custody.Reasons);
    }

    [Fact]
    public void OnlyAConnectionTheLocalSocketAccepted()
    {
        Assert.False(DaemonHost.OverLocalSocket(null));
        Assert.False(DaemonHost.OverLocalSocket(new Dictionary<object, object?>()));
    }

    [Fact]
    public void AReleaseBuildsServiceStartsTheRuntimeWithTheDiagnosticsChannelOff()
    {
        IReadOnlyDictionary<string, string> release = PresenceSessionBuild.Parse("ABCDE12345", null, "26").RuntimeEnvironment;
        Assert.Equal("0", release["DOTNET_EnableDiagnostics"]);
        string[] launcher = ["/opt/vericairn/vericairn"];
        string root = Path.Combine(Path.GetTempPath(), "estate");
        ServicePlan mac = ServiceInstaller.Plan("macos", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 501, release);
        Assert.Contains("<key>EnvironmentVariables</key>\n  <dict>\n    <key>DOTNET_EnableDiagnostics</key><string>0</string>\n  </dict>\n", mac.UnitContent, StringComparison.Ordinal);
        ServicePlan linux = ServiceInstaller.Plan("linux", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 1000, release);
        Assert.Contains("[Service]\nEnvironment=\"DOTNET_EnableDiagnostics=0\"\nExecStart=", linux.UnitContent, StringComparison.Ordinal);

        // A development build's environment is empty, and its plans are the ones before 0.9.4.
        IReadOnlyDictionary<string, string> development = PresenceSessionBuild.Parse(null, null, null).RuntimeEnvironment;
        Assert.Empty(development);
        Assert.Equal(ServiceInstaller.Plan("macos", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 501).UnitContent,
            ServiceInstaller.Plan("macos", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 501, development).UnitContent);
        Assert.DoesNotContain("EnvironmentVariables", ServiceInstaller.Plan("macos", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 501).UnitContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment=", ServiceInstaller.Plan("linux", root, "01ESTATE", launcher, "/home/someone", "/home/someone/vericairn", 1000).UnitContent, StringComparison.Ordinal);
    }

    private sealed class Accepting : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => null;
    }
}
