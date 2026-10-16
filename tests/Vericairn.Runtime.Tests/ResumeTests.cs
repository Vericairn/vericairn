// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public class ResumeTests
{
    [Fact]
    public void AnUnreadableCurrentPinIsReportedWithoutRejectingTheRetainedPackage()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        HandoffPackage package = service.Handoff().Package!;
        File.WriteAllText(Path.Combine(estate.Root, EstateLayout.ManifestPath), "{");

        ResumeResponse resume = service.ResumeHandoff(package);
        Assert.Null(resume.Problem);
        Assert.True(resume.HasErrors);
        Assert.True(resume.Json!.Value.GetProperty("pin").GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, resume.Json.Value.GetProperty("pin").GetProperty("after").ValueKind);
        Assert.Contains("manifest.json", resume.Text, StringComparison.Ordinal);
        Assert.Contains(package.Hash, resume.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACurrentRecordAtTheWrongPathIsComparedAndItsActualLocationIsNamed()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        using EstateService service = EstateService.Open(estate.Root);
        HandoffPackage package = service.Handoff().Package!;
        string misplaced = "records/work-order/2026-10/misplaced.json";
        File.Move(Path.Combine(estate.Root, order.ExpectedPath), Path.Combine(estate.Root, misplaced));

        ResumeResponse resume = service.ResumeHandoff(package);
        Assert.Null(resume.Problem);
        Assert.True(resume.HasErrors);
        JsonElement changed = Assert.Single(resume.Json!.Value.GetProperty("replaced").EnumerateArray());
        Assert.Equal(misplaced, changed.GetProperty("path").GetString());
        Assert.Equal(order.ExpectedPath, changed.GetProperty("previousPath").GetString());
        Assert.Contains("record.path", resume.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumeFindsBackdatedAdditionsEverySupersessionPinAndObservationChanges()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        using EstateService service = EstateService.Open(estate.Root);
        HandoffPackage package = service.Handoff().Package!;
        ResumeResponse unchanged = service.ResumeHandoff(package);
        Assert.Null(unchanged.Problem);
        Assert.False(unchanged.Json!.Value.GetProperty("changed").GetBoolean());
        Assert.Contains("The world may have moved", unchanged.Text, StringComparison.Ordinal);

        RecordEnvelope second = estate.Add("work-order", estate.OperatorId, HandoffTests.Order.Replace("Handoff", "Second", StringComparison.Ordinal), supersedes: order.Id);
        RecordEnvelope third = estate.Add("work-order", estate.OperatorId, HandoffTests.Order.Replace("Handoff", "Third", StringComparison.Ordinal), supersedes: second.Id);
        estate.SetClock(new DateTimeOffset(2026, 10, 13, 12, 0, 3, TimeSpan.Zero));
        RecordEnvelope observation = estate.Add("observation", estate.BuilderId, """{"kind":"repository","asOf":"2026-10-13T12:00:03Z","summary":"The checkout changed.","facts":{"dirty":true},"limitations":["Recorded only."]}""", estate.Estate.Reference);
        string manifest = Path.Combine(estate.Root, EstateLayout.ManifestPath);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("0123456789abcdef0123456789abcdef01234567", new string('b', 40), StringComparison.Ordinal));
        int count = estate.Load().Records.Count;
        ResumeResponse resume = service.ResumeHandoff(package);
        Assert.Null(resume.Problem);
        JsonElement view = resume.Json!.Value;
        Assert.True(view.GetProperty("changed").GetBoolean());
        Assert.Equal(new[] { second.Id, third.Id, observation.Id }.Order(StringComparer.Ordinal), view.GetProperty("added").EnumerateArray().Select(r => r.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(2, view.GetProperty("superseded").GetArrayLength());
        Assert.Contains(view.GetProperty("superseded").EnumerateArray(), r => r.GetProperty("previous").GetString() == second.Id && r.GetProperty("successor").GetString() == third.Id);
        Assert.True(view.GetProperty("pin").GetProperty("changed").GetBoolean());
        Assert.NotEmpty(view.GetProperty("observedChanges").EnumerateArray());
        Assert.Contains(observation.Id, resume.Text, StringComparison.Ordinal);
        Assert.Contains("The checkout changed.", resume.Text, StringComparison.Ordinal);
        Assert.Equal(count, estate.Load().Records.Count);
        Assert.Equal(package.Hash, ContentHash.Sha256(CanonicalJson.SerializePrettyUtf8(package.Payload)));
    }

    [Fact]
    public void ACopyMissingAnOriginalOrChangingItInPlaceIsShownAsIncomplete()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, HandoffTests.Order);
        using EstateService service = EstateService.Open(estate.Root);
        HandoffPackage package = service.Handoff().Package!;
        string path = Path.Combine(estate.Root, order.ExpectedPath);
        byte[] original = File.ReadAllBytes(path);
        File.Delete(path);
        ResumeResponse missing = service.ResumeHandoff(package);
        Assert.Null(missing.Problem);
        Assert.True(missing.HasErrors);
        Assert.Equal(order.Id, Assert.Single(missing.Json!.Value.GetProperty("missing").EnumerateArray()).GetProperty("id").GetString());
        File.WriteAllBytes(path, original);
        File.WriteAllText(path, File.ReadAllText(path).Replace("Continue from records.", "Changed in place.", StringComparison.Ordinal));
        ResumeResponse changed = service.ResumeHandoff(package);
        Assert.True(changed.HasErrors);
        Assert.Equal(order.Id, Assert.Single(changed.Json!.Value.GetProperty("replaced").EnumerateArray()).GetProperty("id").GetString());
        Assert.NotEmpty(changed.Json.Value.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public void InvalidForeignAndTruncatedPackagesNeverBecomeBaselines()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using var other = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        using EstateService foreign = EstateService.Open(other.Root);
        HandoffPackage package = service.Handoff().Package!;
        Assert.Equal("invalid-handoff", service.ResumeHandoff(package with { Bytes = 1 }).Problem!.Code);
        Assert.Equal("invalid-handoff", service.ResumeHandoff(package with { Hash = "sha256:" + new string('0', 64) }).Problem!.Code);
        Assert.Equal("invalid-handoff", foreign.ResumeHandoff(package).Problem!.Code);
        Assert.Equal("invalid-handoff", service.ResumeHandoff(new HandoffPackage("", 0, default)).Problem!.Code);
        JsonObject payload = JsonNode.Parse(package.Payload.GetRawText())!.AsObject();
        payload["originals"]!.AsArray().RemoveAt(0);
        JsonElement truncated = JsonSerializer.SerializeToElement(payload);
        byte[] bytes = CanonicalJson.SerializePrettyUtf8(truncated);
        Assert.Equal("invalid-handoff", service.ResumeHandoff(new HandoffPackage(ContentHash.Sha256(bytes), bytes.LongLength, truncated)).Problem!.Code);
    }
}
