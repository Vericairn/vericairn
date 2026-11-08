// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// The golden history-checkpoint estate: a synthetic format-3 estate whose adoption checkpoint anchors its unsigned records, an
/// agent's interval checkpoint, a closing checkpoint that acknowledges a removed record, and a last checkpoint after the revocation
/// it closes. Its devices were made in a disposable key store for the fixture; only public keys and signatures are retained.
/// </summary>
public class HistoryGoldenFixtureTests
{
    internal const string Name = "history-checkpoint-estate";

    [Fact]
    public void TheGoldenHistoryEstateValidatesWitnessesItsHistoryAndRoundTripsEveryByte()
    {
        string root = FixtureRoot();
        EstateModel model = EstateLoader.Load(root);
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));
        Assert.Equal(3, model.Manifest!.RecordFormat);
        HistoryState history = SignedHistory.Of(model);
        Assert.True(history.Adopted);
        RecordEnvelope[] checkpoints = [.. model.OfType("history-checkpoint").OrderBy(r => r.Id, StringComparer.Ordinal)];
        Assert.Equal(4, checkpoints.Length);
        Assert.Equal(checkpoints[^1].Id, Assert.Single(history.Heads).Id);
        Assert.Equal(1, history.Tail);
        // Everything but the last checkpoint is witnessed; the acknowledged removal is not counted.
        Assert.Equal(model.Records.Count - 1, history.Witnessed);
        Assert.All(checkpoints, c => Assert.True(model.Counts(c) && SignatureChecks.IsAuthentic(model, c)));
        Assert.Equal(model.Records.Count, model.CountingRecords.Count);
        // Every format-3 field of the type is present in the fixture.
        foreach (FieldDescription field in new HistoryCheckpointType().Fields)
            Assert.Contains(checkpoints, c => c.Body.TryGetProperty(field.Name, out _));
        Assert.Contains(checkpoints, c => EstateModel.BodyString(c, "trigger") == "closing" && c.Body.TryGetProperty("removed", out _));
        Assert.True(model.Records.Where(r => r.Signatures.Count == 0).All(r => SignatureChecks.Anchored(model).Contains(SignatureChecks.AnchorKey(r.Id, r.ComputedHash))));

        foreach (RecordEnvelope record in model.Records)
        {
            byte[] original = File.ReadAllBytes(Path.Combine(root, record.ExpectedPath));
            Assert.Equal(original, RecordAuthor.Format(original));
            AuthoredRecord rebuilt = RecordAuthor.Create(new RecordDraft(record.Type, record.By, record.Body, record.Subject, record.Supersedes, record.AtTime, record.Id, record.Format));
            Assert.Equal(record.ComputedHash, rebuilt.Envelope.ComputedHash);
            Assert.Equal(CanonicalJson.Serialize(record.Body), CanonicalJson.Serialize(rebuilt.Envelope.Body));
        }

        foreach (RecordEnvelope checkpoint in checkpoints)
        foreach (JsonElement hash in checkpoint.Body.GetProperty("inventory").EnumerateArray())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, EstateLayout.ObjectsDirectory, hash.GetString()![ContentHash.Prefix.Length..]));
            Assert.Equal(hash.GetString(), ContentHash.Sha256(bytes));
            Assert.Empty(HistoryInventory.Read(bytes).Problems);
            Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheGoldenHistoryInventoryPinsEveryFixtureByte()
    {
        string root = FixtureRoot();
        Dictionary<string, string> expected = File.ReadAllLines(Path.Combine(root, "inventory.sha256")).Where(l => l.Length > 0)
            .Select(l => l.Split("  ", 2, StringSplitOptions.None)).ToDictionary(p => p[1], p => p[0], StringComparer.Ordinal);
        string[] actual = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => p != "inventory.sha256" && !p.StartsWith(".vericairn/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual);
        Assert.All(expected, item => Assert.Equal(item.Value, ContentHash.Sha256File(Path.Combine(root, item.Key))));
    }

    [Fact]
    public void ARemovedOrAlteredGoldenRecordIsCaughtOnACopy()
    {
        string copy = Path.Combine(Path.GetTempPath(), "vericairn-history-golden-" + Identity.Ulid.NewUlid().Value);
        try
        {
            HistoryCheckpointTests.CopyDirectory(FixtureRoot(), copy);
            EstateModel model = EstateLoader.Load(copy);
            RecordEnvelope revocation = model.OfType("admission").Single(a => EstateModel.BodyString(a, "status") == "revoked");
            File.Delete(Path.Combine(copy, revocation.ExpectedPath));
            Assert.Contains(EstateLoader.Load(copy).Findings, f => f.Code == "history.removed" && f.Location == revocation.ExpectedPath);
        }
        finally { Directory.Delete(copy, recursive: true); }
    }

    [Fact]
    public void WriteTheGoldenHistoryEstateWhenAsked()
    {
        string? output = Environment.GetEnvironmentVariable("VERICAIRN_WRITE_HISTORY_FIXTURE");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(output), "Set VERICAIRN_WRITE_HISTORY_FIXTURE to a new directory to regenerate the golden history estate there.");
        Write(output!);
    }

    /// <summary>Writes a new golden history estate into a directory that does not exist yet, with fresh disposable keys.</summary>
    internal static void Write(string output)
    {
        if (Directory.Exists(output)) throw new ArgumentException("Write the fixture into a new directory.");
        using var f = new HistoryFixture();
        RecordEnvelope builder2 = f.Device(f.Estate.BuilderId, "Second build machine");
        RecordEnvelope admitted = f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "The builder's second machine."), builder2.Reference, signer: f.OperatorSigner);
        f.Estate.Add("decision", f.Estate.OperatorId, $$"""{"title":"Adopt history checkpoints","statement":"Adopt checkpoints for this fixture.","summary":"A synthetic decision.","synthesis":"The fixture exercises every checkpoint field.","effects":["adopt"],"affects":[],"decidedBy":"{{f.Estate.OperatorId}}","decidedAt":"{{RecordParser.FormatTimestamp(f.Estate.Clock)}}","stake":"medium","notAuthorized":["publication"]}""", signer: f.OperatorSigner);
        f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("GOLD-1", "Recorded before signatures were required"));
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(5));
        f.Require();
        f.Adopt();
        RecordEnvelope secret = f.Estate.Add("resource", f.Estate.OperatorId, """{"kind":"repository","name":"Withdrawn source","role":"A record that had to go.","location":{"github":"Example/withdrawn"},"visibility":"private"}""", signer: f.OperatorSigner);
        f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("GOLD-2", "Recorded by the builder"), signer: f.BuilderSigner);
        f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        f.Remove(secret);
        f.Checkpoint(trigger: "closing", removed: [(secret.Id, secret.ComputedHash, "It named a source that had to be withdrawn.")], closes: ("device", builder2.Id));
        string revokedAt = RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1));
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Revoked after its closing.", "revoked", revokedAt), builder2.Reference, supersedes: admitted.Id, signer: f.OperatorSigner);
        f.Checkpoint(f.Estate.BuilderId, f.BuilderSigner, "interval");
        EstateModel model = f.Load();
        Assert.False(model.Findings.HasErrors, string.Join('\n', model.Findings));

        foreach (string file in Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(f.Root, p).Replace('\\', '/').StartsWith(".vericairn/", StringComparison.Ordinal)))
        {
            string target = Path.Combine(output, Path.GetRelativePath(f.Root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(output, "inventory.sha256"), string.Join('\n', Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(p => (Path: Path.GetRelativePath(output, p).Replace('\\', '/'), Hash: ContentHash.Sha256File(p)))
            .Where(p => p.Path != "inventory.sha256").OrderBy(p => p.Path, StringComparer.Ordinal).Select(p => p.Hash + "  " + p.Path)) + "\n");
        EstateModel written = EstateLoader.Load(output);
        Assert.False(written.Findings.HasErrors, string.Join('\n', written.Findings));
    }

    internal static string FixtureRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return Path.Combine(root, "tests", "fixtures", Name);
    }
}
