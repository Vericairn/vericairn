// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// CODE-80 (0.9.4): Workbench shows a presence session as the command line does (design/presence-sessions.md § 7): the list in its activity
/// log from the session's terms, with the runtime's descriptions labeled as the runtime's; the fingerprint the system dialog shows, which it
/// computes itself from the check's nonce and terms it read before it, in the status line; and each signature as it is made. The runtime
/// that serves it raises the session's notices while a submission signs, so they reach Workbench through its own reads.
/// </summary>
[Collection("workbench")]
public class PresenceSessionViewTests(HeadlessFixture fixture)
{
    private const string Order = """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Exercise presence sessions.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}""";

    private const string Session = "01J9Z3K4M5N6P7Q8R9S0T1V2W5";

    [Fact]
    public async Task ASessionsListFingerprintAndSignaturesAreShownAsTheyAreRaised()
    {
        string root = Seed.Temp("wb-session");
        string scratch = Seed.Temp("wb-session-scratch");
        string op = Seed.Init(root);
        string device = Seed.Id(Seed.Cli("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file"));
        Seed.Cli("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "The operator's own device.");
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "order.json", Order));
        string order = Seed.LastId(root, "work-order");
        ServiceConnection? connection = null;
        var shell = new ShellViewModel(r => connection = new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-session"))) { ActingOverride = op };
        MainWindow window = await fixture.Ui(() => { var w = new MainWindow(shell); w.Show(); return w; });
        try
        {
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        (string terms, string nonce, string fingerprint) = FingerprintVector();
        bool delivered = false;
        var custody = new Raising(VericairnHome.Path(), () =>
        {
            // The session's notices, raised as a runtime raises them: its terms, then, once a read has delivered them, its nonce and a signature.
            var notices = connection!.Service!.PresenceNotices;
            PresenceNotice listed = notices.Publish("announced", "operator record", 4, 0, [], "the runtime's own text", null,
                new PresenceSessionNotice(Session, 300, terms, [new PresenceEntryDescription(0, "acceptance", "STEP-1", "accepts result at its exact hash", "adds an acceptance about STEP-1")]));
            delivered = notices.AwaitDelivery(listed.Sequence, TestTiming.HangTimeout);
            notices.Publish("session", "operator record", 4, 0, [], "the runtime's own fingerprint sentence", null, new PresenceSessionNotice(Session, 300, Nonce: nonce));
            notices.Publish("signed", "operator record", 4, 1, [new("acceptance", "STEP-1", "accepts result at its exact hash")],
                "Signature 1 of 4, under the session's check: acceptance STEP-1: accepts result at its exact hash (0604e646bd69)", 1,
                new PresenceSessionNotice(Session, 300, SecondsLeft: 299), "0604e646bd69");
        });
        connection!.Service!.KeyStores = [custody];
        connection.Service.PresenceRequired = id => id == device;

        ReviewWorkspace workspace = (await fixture.UiAsync(() => shell.OpenReviewAsync(order, cancellationToken: TestContext.Current.CancellationToken)))!;
        workspace.ReviewSummary = "Read the order.";
        await fixture.UiAsync(() => shell.SubmitReviewAsync(workspace, TestContext.Current.CancellationToken));

        Assert.StartsWith("Written: review", workspace.Status, StringComparison.Ordinal);
        Assert.True(delivered);
        string[] log = [.. shell.Activity.Reverse().Select(line => line[(line.IndexOf("  ", StringComparison.Ordinal) + 2)..])];
        int listedAt = Array.IndexOf(log, "operator record asks for your presence once, for one presence session of 4 signatures in at most 300 s, covering only this list, read here from its terms:");
        int checkAt = Array.FindIndex(log, l => l.StartsWith($"Presence check for operator record: the system dialog should show fingerprint {fingerprint}, computed here", StringComparison.Ordinal));
        int signedAt = Array.IndexOf(log, "runtime: Signature 1 of 4, under the session's check: acceptance STEP-1: accepts result at its exact hash (0604e646bd69)");
        Assert.True(listedAt >= 0 && listedAt < checkAt && checkAt < signedAt, string.Join("\n", log));
        Assert.Equal("  1. acceptance by exact hash 0604e646bd69", log[listedAt + 1]);
        Assert.Equal("runtime:   describes entry 1: acceptance STEP-1: accepts result at its exact hash", log[listedAt + 2]);
        Assert.Equal("  Its short hash is entry 1 of the list above, by exact hash.", log[signedAt + 1]);
        Assert.Contains("  In all, counted here from the terms: 4 signatures: 1 acceptance record by exact hash; 3 push records by kind.", log);
        Assert.DoesNotContain(log, l => l.Contains("the runtime's own", StringComparison.Ordinal));
        Assert.Equal("runtime: Signed 1 of 1.", shell.PresencePrompt);

        // The activity log shows the runtime's lines in a style of their own, and the client's own lines without it.
        List<(string Text, bool Runtime)> shown = await fixture.Ui(() =>
        {
            window.UpdateLayout();
            return window.ActivityList.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is not null && t.Text.Length > 10 && t.Text[2] == ':').Select(t => (t.Text!, t.Classes.Contains("runtime"))).ToList();
        });
        Assert.True(shown.Any(l => l.Runtime && l.Text.EndsWith("  runtime: Signed 1 of 1.", StringComparison.Ordinal)), string.Join("\n", shown.Select(l => (l.Runtime ? "R " : "- ") + l.Text)));
        Assert.Contains(shown, l => !l.Runtime && l.Text.Contains("  Its short hash is entry 1 of the list above, by exact hash.", StringComparison.Ordinal));
        Assert.All(shown, l => Assert.Equal(l.Text[10..].StartsWith("runtime: ", StringComparison.Ordinal), l.Runtime));
        Assert.Equal(true, PresenceLineStyle.Runtime.Convert("runtime: Signature 1 of 1", typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, PresenceLineStyle.Runtime.Convert("Presence check for operator record: the system dialog should show fingerprint E72R-85FF-KDKK", typeof(bool), null, CultureInfo.InvariantCulture));
        }
        finally
        {
            await fixture.Ui(window.Close);
        }
    }

    // The design's fingerprint vector: its terms, its nonce and the fingerprint they give.
    private static (string Terms, string Nonce, string Fingerprint) FingerprintVector()
    {
        string? at = AppContext.BaseDirectory;
        while (at is not null && !File.Exists(Path.Combine(at, "Vericairn.slnx"))) at = Path.GetDirectoryName(at);
        using JsonDocument vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(at!, "tests", "fixtures", "presence-sessions", "vectors.json")));
        JsonElement vector = vectors.RootElement.GetProperty("fingerprints")[0];
        return (vector.GetProperty("termsText").GetString()!, vector.GetProperty("nonce").GetString()!, vector.GetProperty("fingerprint").GetString()!);
    }

    /// <summary>Custody that needs presence, by a file key, which raises the session's notices before its one signature.</summary>
    private sealed class Raising(string home, Action before) : IDeviceKeyStore
    {
        private readonly FileKeyStore inner = new(home);

        public string Kind => SecureEnclaveCustody.Kind;

        public bool Has(string deviceId) => inner.Has(deviceId);

        public byte[] Create(string deviceId) => inner.Create(deviceId);

        public byte[]? PublicKey(string deviceId) => inner.PublicKey(deviceId);

        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
        {
            before();
            return inner.Sign(deviceId, digest);
        }

        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest, string reason) => Sign(deviceId, digest);

        public void Remove(string deviceId) => inner.Remove(deviceId);

        public void Promote(string keyId, string deviceId) => inner.Promote(keyId, deviceId);
    }
}
