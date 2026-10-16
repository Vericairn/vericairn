// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class HandoffJourneyTests
{
    [Fact]
    public void ANewSurfaceResumesTheRetainedPackageWithoutSubmittingWork()
    {
        using var estate = new HandoffEstate();
        string package = CliHarness.Ok("--estate", estate.Root, "--json", "handoff").Out;
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Continue?","recommendation":"Review first.","consequence":"Work waits.","state":"open"}""");
        int count = estate.Load().Records.Count;
        string scratch = CliHarness.TempDirectory("resume-input");
        try
        {
            string path = CliHarness.WriteJson(scratch, "handoff.json", package);
            CliHarness.Outcome result = CliHarness.Ok("--estate", estate.Root, "--json", "resume", path);
            Assert.Equal(ask.Id, Assert.Single(result.Json.GetProperty("added").EnumerateArray()).GetProperty("id").GetString());
            Assert.Contains("The world may have moved", CliHarness.Ok("--estate", estate.Root, "resume", path).Out, StringComparison.Ordinal);
            Assert.Equal(count, estate.Load().Records.Count);
            File.WriteAllText(path, "{}");
            Assert.Equal(2, CliHarness.Run("--estate", estate.Root, "resume", path).Code);
        }
        finally
        {
            CliHarness.Delete(scratch);
        }
    }

    [Fact]
    public void HandoffKeepsEveryOriginalAndIsIdenticalOnAnotherCopy()
    {
        using var estate = new HandoffEstate();
        RecordEnvelope ask = estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Continue?","recommendation":"Review first.","consequence":"Work waits.","state":"open"}""");
        string copy = CliHarness.Clone(estate.Root, "handoff-copy");
        string scratch = CliHarness.TempDirectory("handoff-output");
        try
        {
            string path = Path.Combine(scratch, "handoff.json");
            CliHarness.Outcome first = CliHarness.Ok("--estate", estate.Root, "--json", "handoff", "--out", path);
            Assert.Equal(first.Out, File.ReadAllText(path));
            Assert.Equal(first.Out, CliHarness.Ok("--estate", copy, "--json", "handoff").Out);
            JsonElement package = first.Json;
            JsonElement payload = package.GetProperty("payload");
            Assert.Equal("vericairn.handoff/1", payload.GetProperty("format").GetString());
            Assert.Equal(estate.Load().Records.Count, payload.GetProperty("originals").GetArrayLength());
            JsonElement original = payload.GetProperty("originals").EnumerateArray().Single(r => r.GetProperty("id").GetString() == ask.Id);
            byte[] bytes = Convert.FromBase64String(original.GetProperty("content").GetString()!);
            Assert.Equal(File.ReadAllBytes(Path.Combine(estate.Root, ask.ExpectedPath)), bytes);
            Assert.Equal(ask.ComputedHash, original.GetProperty("hash").GetString());
            Assert.Equal(ContentHash.Sha256(bytes), original.GetProperty("byteHash").GetString());
            Assert.Empty(payload.GetProperty("withheld").EnumerateArray());
            string text = CliHarness.Ok("--estate", estate.Root, "handoff").Out;
            Assert.Contains("Continue?", text, StringComparison.Ordinal);
            Assert.Contains("Limit not set", text, StringComparison.Ordinal);
            Assert.Contains("Originals (complete)", text, StringComparison.Ordinal);
            Assert.DoesNotContain(estate.Root, text, StringComparison.Ordinal);

            // Never replace a retained package, including when the new request is refused.
            Assert.Equal(2, CliHarness.Run("--estate", estate.Root, "handoff", "--out", path).Code);
            Assert.Equal(first.Out, File.ReadAllText(path));
            Assert.Equal(2, CliHarness.Run("--estate", estate.Root, "handoff", "--max-bytes", "1").Code);
        }
        finally
        {
            CliHarness.Delete(copy, scratch);
        }
    }

    [Fact]
    public void MissingDecisionAndOpenWorkAreAllNamedAndNoPackageIsWritten()
    {
        using var estate = new HandoffEstate();
        const string decision = "01M50G0KGHZ7ER868WQ8BYZYWW";
        const string order = "01M525VYH6Q1D6NA62QDXA19WA";
        estate.Add("work-order", estate.OperatorId, $$"""{"key":"H-1","kind":"ordinary","title":"Hand over","intent":"Continue.","scope":["work"],"exitQuestion":"Complete?","inputs":[{"type":"decision","id":"{{decision}}"},{"type":"work-order","id":"{{order}}"}],"deliverables":["result"]}""");
        string scratch = CliHarness.TempDirectory("handoff-refused");
        try
        {
            string path = Path.Combine(scratch, "handoff.json");
            CliHarness.Outcome refused = CliHarness.Run("--estate", estate.Root, "--json", "handoff", "--out", path);
            Assert.Equal(2, refused.Code);
            Assert.Contains(decision, refused.Out, StringComparison.Ordinal);
            Assert.Contains(order, refused.Out, StringComparison.Ordinal);
            Assert.False(File.Exists(path));
        }
        finally
        {
            CliHarness.Delete(scratch);
        }
    }
}

internal sealed class HandoffEstate : IDisposable
{
    private DateTimeOffset clock = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public HandoffEstate()
    {
        Root = CliHarness.TempDirectory("handoff-estate");
        OperatorId = Ulid.NewUlid(clock).Value;
        Add("principal", OperatorId, """{"kind":"human","name":"Operator","roles":["operator"]}""", OperatorId);
        RecordEnvelope estate = Add("estate", OperatorId, $$$"""{"name":"Example","slug":"example","purpose":"Handoff.","operator":"{{{OperatorId}}}","recordFormat":2,"policies":{"signatures":"optional","releaseAcceptance":["operator"]}}""");
        BuilderId = Add("principal", OperatorId, """{"kind":"agent","name":"Builder","roles":["builder"],"agent":{"vendor":"Example","product":"Example"}}""").Id;
        Directory.CreateDirectory(Path.Combine(Root, "estate"));
        using JsonDocument manifest = JsonDocument.Parse($$$"""{"estate":"{{{estate.Id}}}","format":"vericairn.estate-manifest/1","recordFormat":2,"vericairn":{"mode":"source","repository":"Example/vericairn","commit":"0123456789abcdef0123456789abcdef01234567"}}""");
        File.WriteAllBytes(Path.Combine(Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(manifest.RootElement));
    }

    public string Root { get; }
    public string OperatorId { get; }
    public string BuilderId { get; }

    public RecordEnvelope Add(string type, string by, string body, string? id = null)
    {
        using JsonDocument json = JsonDocument.Parse(body);
        clock = clock.AddSeconds(1);
        AuthoredRecord record = RecordAuthor.Create(new RecordDraft(type, by, json.RootElement, null, null, clock, id, 2));
        RecordAuthor.Write(Root, record);
        return record.Envelope;
    }

    public EstateModel Load() => EstateLoader.Load(Root);
    public void Dispose() => CliHarness.Delete(Root);
}
