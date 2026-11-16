// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.IO.Compression;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// CODE-80 (0.9.4): the command line's presence session surfaces (design/presence-sessions.md §§ 7, 10 and 14): the device owner's setting
/// with <c>devices presence</c>, kept owner-only in this user's machine state; the session ledger with <c>devices sessions</c>, which no
/// export or support bundle carries; and a session shown on standard error from its terms, with the fingerprint computed here and each
/// signature as it is made. The helper is the in-process stand-in, so nobody is asked for presence.
/// </summary>
[Collection("cli")]
public sealed class PresenceSessionCommandTests
{
    private const string Session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";

    private static readonly string[] OperatorRole = ["operator"];

    [Fact]
    public void DevicesPresenceKeepsTheOwnersSettingOwnerOnlyAndTheStricterOfItAndTheChartersWins()
    {
        _ = CliHarness.TempDirectory("presence-setting");
        using var estate = new TestEstate(3);
        string device = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "create", "--principal", estate.OperatorId, "--name", "Operator laptop", "--custody", "file").Id;

        CliHarness.Outcome shown = CliHarness.Ok("--estate", estate.Root, "devices", "presence", device);
        Assert.Contains($"device {device} (Operator laptop): an operator action may ask one presence check for the list it announces, a session of at most 300 s", shown.Out, StringComparison.Ordinal);
        Assert.Contains("this device's signatures ask no presence check, so the setting changes nothing for it", shown.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(PresenceSettings.PathOf()));

        CliHarness.Ok("--estate", estate.Root, "devices", "presence", device, "--per-signature");
        Assert.Equal(new PresenceSetting(true, null), PresenceSettings.Read(device));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PresenceSettings.PathOf()));
        JsonElement set = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "presence", device, "--per-action", "--limit", "90").Json;
        Assert.Equal(("per-action", 90, "per-action"), (set.GetProperty("presence").GetString(), set.GetProperty("limitSeconds").GetInt32(), set.GetProperty("effective").GetString()));
        Assert.Equal(JsonValueKind.Null, set.GetProperty("charter").ValueKind);
        Assert.Equal(new PresenceSetting(false, 90), PresenceSettings.Read(device));

        // Each refusal writes nothing.
        foreach (string[] refused in new[]
        {
            new[] { "--per-signature", "--per-action" }, ["--per-action", "--limit", "59"], ["--per-action", "--limit", "301"], ["--per-signature", "--limit", "90"],
        })
        {
            Assert.NotEqual(0, CliHarness.Run(["--estate", estate.Root, "devices", "presence", device, .. refused]).Code);
            Assert.Equal(new PresenceSetting(false, 90), PresenceSettings.Read(device));
        }

        Assert.Contains("is not a device of this estate", CliHarness.Run("--estate", estate.Root, "devices", "presence", "01J9Z3K4M5N6P7Q8R9S0T1V2W9", "--per-signature").Err, StringComparison.Ordinal);

        // The estate's charter asks one check per signature: it wins over the owner's per-action setting.
        estate.Add("estate", estate.OperatorId, JsonSerializer.Serialize(new
        {
            name = "Test Estate", slug = "test-estate", purpose = "Exercise the record format.", @operator = estate.OperatorId, recordFormat = 3,
            policies = new { signatures = "optional", releaseAcceptance = OperatorRole, presence = "per-signature" },
        }), supersedes: estate.Estate.Id);
        JsonElement charter = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "presence", device).Json;
        Assert.Equal(("per-action", "per-signature", "per-signature"), (charter.GetProperty("presence").GetString(), charter.GetProperty("charter").GetString(), charter.GetProperty("effective").GetString()));
        Assert.Contains("the estate's charter asks one presence check per signature for every device, which wins", CliHarness.Ok("--estate", estate.Root, "devices", "presence", device).Out, StringComparison.Ordinal);

        // A settings file that does not read counts as per-signature for every device: no setting is written over it.
        string settings = PresenceSettings.PathOf();
        File.WriteAllText(settings, "{ not json");
        CliHarness.Outcome damaged = CliHarness.Run("--estate", estate.Root, "devices", "presence", device, "--per-action", "--limit", "120");
        Assert.NotEqual(0, damaged.Code);
        Assert.Contains(settings, damaged.Err, StringComparison.Ordinal);
        Assert.Contains("nothing was written", damaged.Err, StringComparison.Ordinal);
        Assert.Contains("remove it to return every device on this machine to the default", damaged.Err, StringComparison.Ordinal);
        Assert.Equal("{ not json", File.ReadAllText(settings));
        // Removed, as the refusal says, the file starts again with the default for every device.
        File.Delete(settings);
        CliHarness.Ok("--estate", estate.Root, "devices", "presence", device, "--per-action", "--limit", "90");

        // A key this machine does not hold is set where it is held.
        new FileKeyStore(VericairnHome.Path()).Remove(device);
        Assert.Contains("is not held on this machine", CliHarness.Run("--estate", estate.Root, "devices", "presence", device, "--per-signature").Err, StringComparison.Ordinal);
        Assert.Equal(new PresenceSetting(false, 90), PresenceSettings.Read(device));
    }

    [Fact]
    public void DevicesSessionsListsTheLedgerWhichNoExportOrSupportBundleCarries()
    {
        string scratch = CliHarness.TempDirectory("presence-ledger");
        using var estate = new TestEstate(3);
        string device = CliHarness.Ok("--estate", estate.Root, "--json", "devices", "create", "--principal", estate.OperatorId, "--name", "Operator laptop", "--custody", "file").Id;
        string terms = "{\"action\":\"operator close\",\"marker\":\"TERMS-" + Guid.NewGuid().ToString("N") + "\",\"session\":\"" + Session + "\"}";
        var ledger = new PresenceSessionLedger(PresenceSessionLedger.DirectoryOf(estate.Root));
        string first = "01J9Z3K4M5N6P7Q8R9S0T1V2X1", second = "01J9Z3K4M5N6P7Q8R9S0T1V2X2", hash = "sha256:" + new string('a', 64);
        ledger.Append(new("2026-11-16T10:00:00Z", Session, "announced") { Action = "operator close", Named = "operator close", Principal = estate.OperatorId, Device = device, Terms = terms, LimitSeconds = 300 });
        ledger.Append(new("2026-11-16T10:00:01Z", Session, "prompting") { Nonce = "AAECAwQFBgcICQoLDA0ODw", Fingerprint = "E72R-85FF-KDKK" });
        ledger.Append(new("2026-11-16T10:00:05Z", Session, "opened") { OpenedAt = "2026-11-16T10:00:05Z", LimitSeconds = 300 });
        ledger.Append(new("2026-11-16T10:00:06Z", Session, "signed") { Record = first, Type = "acceptance", Hash = hash, Entry = 0 });
        ledger.Append(new("2026-11-16T10:00:07Z", Session, "signed") { Record = second, Type = "acceptance", Hash = hash, Entry = 1 });
        ledger.Append(new("2026-11-16T10:00:08Z", Session, "ended") { Reason = "lock", Detail = "The presence session ended: the screen locked or the user session switched." });
        try
        {
            string listed = CliHarness.Ok("--estate", estate.Root, "devices", "sessions").Out;
            Assert.Contains($"2026-11-16T10:00:00Z  session {Session}  operator close  device {device}  limit 300 s  fingerprint E72R-85FF-KDKK  opened 2026-11-16T10:00:05Z  2 signed  ended 2026-11-16T10:00:08Z (lock)", listed, StringComparison.Ordinal);
            Assert.Contains($"  signed acceptance {first}  {hash}", listed, StringComparison.Ordinal);
            Assert.Contains("  The presence session ended: the screen locked or the user session switched.", listed, StringComparison.Ordinal);
            Assert.Contains("this machine's runtime has opened no presence session for device 01J9Z3K4M5N6P7Q8R9S0T1V2W9", CliHarness.Ok("--estate", estate.Root, "devices", "sessions", "01J9Z3K4M5N6P7Q8R9S0T1V2W9").Out, StringComparison.Ordinal);
            JsonElement json = Assert.Single(CliHarness.Ok("--estate", estate.Root, "--json", "devices", "sessions", device).Json.EnumerateArray());
            Assert.Equal((Session, terms, 2, "lock"), (json.GetProperty("session").GetString(), json.GetProperty("terms").GetString(), json.GetProperty("signed").GetArrayLength(), json.GetProperty("ended").GetString()));

            // The ledger is machine state: a support bundle and an export carry nothing of it.
            CliHarness.Outcome bundle = CliHarness.Ok("--estate", estate.Root, "support", "bundle");
            string zip = Path.Combine(scratch, "export.zip");
            CliHarness.Ok("--estate", estate.Root, "--json", "estate", "export", "--out", zip);
            using ZipArchive archive = ZipFile.OpenRead(zip);
            foreach (string never in new[] { Session, terms, "E72R-85FF-KDKK", PresenceSessionLedger.FileName })
            {
                Assert.DoesNotContain(never, bundle.Out, StringComparison.Ordinal);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    Assert.DoesNotContain(never, entry.FullName, StringComparison.Ordinal);
                    using var content = new StreamReader(entry.Open());
                    Assert.DoesNotContain(never, content.ReadToEnd(), StringComparison.Ordinal);
                }
            }
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public async Task TheCommandLineShowsASessionsListFromItsTermsItsOwnFingerprintAndEachSignature()
    {
        _ = CliHarness.TempDirectory("presence-display");
        using var f = new OperatorCloseFixture();
        f.Add("STEP-1");
        f.Add("STEP-2");
        var helpers = new List<PresenceHelperStandIn>();
        StandIns(f, helpers);
        long start = f.Api.PresencePrompts(new()).Cursor;
        using var output = new StringWriter();

        OperatorCloseResponse closed;
        using (PresenceDisplay display = PresenceDisplay.Start(f.Api, output))
        {
            // The display follows: its read waits before the action starts.
            using var hang = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            hang.CancelAfter(TestTiming.HangTimeout);
            while (f.Api.PresenceNotices.Waiting == 0) await Task.Delay(10, hang.Token);
            closed = f.Api.OperatorClose(f.Request(), TestContext.Current.CancellationToken);
        }

        Assert.True(closed.Ok, closed.Problem?.Message);
        PresenceNotice check = Assert.Single(f.Api.PresencePrompts(new(start)).Notices, n => n.Kind == "session");
        string fingerprint = PresenceSessionRules.Fingerprint(System.Buffers.Text.Base64Url.DecodeFromChars(check.Session!.Nonce), Assert.Single(helpers).Terms[0]);
        string shown = output.ToString().ReplaceLineEndings("\n");
        Assert.Contains("operator close asks for your presence once, for one presence session of 2 signatures in at most 300 s, covering only this list, read here from its terms:", shown, StringComparison.Ordinal);
        Assert.Contains("\n  1. acceptance by exact hash ", shown, StringComparison.Ordinal);
        Assert.Contains("\nruntime:   describes entry 1: acceptance STEP-1: accepts result ", shown, StringComparison.Ordinal);
        Assert.Contains("\n  In all, counted here from the terms: 2 signatures: 2 acceptance records by exact hash.", shown, StringComparison.Ordinal);
        Assert.Contains($"Presence check for operator close: the system dialog should show fingerprint {fingerprint}, computed here from the list above and the check's nonce.", shown, StringComparison.Ordinal);
        Assert.Contains("\nruntime: Signature 1 of 2, under the session's check: acceptance STEP-1: ", shown, StringComparison.Ordinal);
        Assert.Contains("\n  Its short hash is entry 2 of the list above, by exact hash.\n", shown, StringComparison.Ordinal);
        Assert.EndsWith("\nruntime: Signed 2 of 2 with one presence check.\n", shown, StringComparison.Ordinal);
        // Every line is the command line's own derivation or marked as the runtime's.
        Assert.All(shown.TrimEnd('\n').Split('\n'), line => Assert.True(line.StartsWith("runtime: ", StringComparison.Ordinal) || line.StartsWith("operator close ", StringComparison.Ordinal)
            || line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("Presence check for operator close: ", StringComparison.Ordinal) || line == "Anything outside this list asks again.", line));
        // The runtime's own fingerprint sentence, which a client before 0.9.4 prints, is not what this display shows.
        Assert.DoesNotContain("the system dialog shows fingerprint", shown, StringComparison.Ordinal);
    }

    private static void StandIns(OperatorCloseFixture f, List<PresenceHelperStandIn> helpers)
    {
        f.Api.KeyStores = [new RecordingCustody(VericairnHome.Path())];
        f.Api.PresenceRequired = device => device == f.Device;
        f.Api.HostsEmbedded = true;
        f.Api.PresenceHost = new PresenceSessionHost
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
    }

    private sealed class Accepting : IPresenceHelperRequirement
    {
        public string? Problem(string helperPath, string requirement) => null;
    }
}
