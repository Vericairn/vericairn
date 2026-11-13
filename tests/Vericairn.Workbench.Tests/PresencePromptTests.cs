// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Workbench.Tests;

/// <summary>
/// FIX-124: a submission from Workbench shows the presence prompts its record asks for on the runtime's machine: the announcement in
/// the activity log before the prompt, "Signature k of n" with what it signs in the status, and how many were signed. The operator's
/// file key is held by a recording stand-in for custody that needs presence, so no test asks anyone.
/// </summary>
[Collection("workbench")]
public class PresencePromptTests(HeadlessFixture fixture)
{
    private const string Order = """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Exercise presence prompts.","scope":["widget"],"exitQuestion":"Does it build?","inputs":[],"deliverables":["widget.txt"]}""";

    [Fact]
    public async Task AReviewSentFromWorkbenchShowsItsAnnouncementItsPromptAndHowManyWereSigned()
    {
        string root = Seed.Temp("wb-presence");
        string scratch = Seed.Temp("wb-presence-scratch");
        string op = Seed.Init(root);
        string device = Seed.Id(Seed.Cli("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file"));
        Seed.Cli("--estate", root, "--json", "devices", "admit", device, "--by", op, "--note", "The operator's own device.");
        Seed.Cli("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", Seed.Write(scratch, "order.json", Order));
        string order = Seed.LastId(root, "work-order");
        ServiceConnection? connection = null;
        var shell = new ShellViewModel(r => connection = new ServiceConnection(r), new FakeRuntimeManager(), new Preferences(Seed.Temp("wb-prefs-presence"))) { ActingOverride = op };
        await fixture.UiAsync(() => shell.OpenEstateAsync(root, cancellationToken: TestContext.Current.CancellationToken));
        var custody = new Recording(VericairnHome.Path());
        connection!.Service!.KeyStores = [custody];
        connection.Service.PresenceRequired = id => id == device;

        ReviewWorkspace workspace = (await fixture.UiAsync(() => shell.OpenReviewAsync(order, cancellationToken: TestContext.Current.CancellationToken)))!;
        workspace.ReviewSummary = "Read the order.";
        await fixture.UiAsync(() => shell.SubmitReviewAsync(workspace, TestContext.Current.CancellationToken));

        Assert.StartsWith("Written: review", workspace.Status, StringComparison.Ordinal);
        string[] log = [.. shell.Activity.Reverse().Select(line => line[(line.IndexOf("  ", StringComparison.Ordinal) + 2)..])];
        int announced = Array.IndexOf(log, "Writing the review record asks for your presence once, for the one record it signs:");
        int prompt = Array.IndexOf(log, $"Signature 1 of 1: review W-1: records your review of work-order {order}");
        int signed = Array.IndexOf(log, "Signed 1 of 1.");
        Assert.True(announced >= 0 && announced < prompt && prompt < signed, string.Join("\n", log));
        Assert.Equal($"  1. review W-1: records your review of work-order {order}", log[announced + 1]);
        Assert.Equal("Signed 1 of 1.", shell.PresencePrompt);
        Assert.Equal($"sign Vericairn signature 1 of 1: review W-1, records your review of work-order {order}", Assert.Single(custody.Reasons));
    }

    private sealed class Recording(string home) : IDeviceKeyStore
    {
        private readonly FileKeyStore inner = new(home);

        public List<string?> Reasons { get; } = [];

        public string Kind => SecureEnclaveCustody.Kind;

        public bool Has(string deviceId) => inner.Has(deviceId);

        public byte[] Create(string deviceId) => inner.Create(deviceId);

        public byte[]? PublicKey(string deviceId) => inner.PublicKey(deviceId);

        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest)
        {
            Reasons.Add(null);
            return inner.Sign(deviceId, digest);
        }

        public byte[] Sign(string deviceId, ReadOnlySpan<byte> digest, string reason)
        {
            Reasons.Add(reason);
            return inner.Sign(deviceId, digest);
        }

        public void Remove(string deviceId) => inner.Remove(deviceId);

        public void Promote(string keyId, string deviceId) => inner.Promote(keyId, deviceId);
    }
}
