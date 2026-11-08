// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>
/// What writing and adopting history checkpoints reads from the records (design/signed-history.md §§ 9 and 10; OPS-07): when an interval
/// checkpoint falls due, what adoption would witness and anchor, adoption listed under what waits on the operator until it counts, and the
/// history line every surface shows.
/// </summary>
public class HistoryWritingTests
{
    [Fact]
    public void AnIntervalFallsDueAtFiveHundredTailRecordsOrADayAndNeverBeforeAdoption()
    {
        using var f = new HistoryFixture();
        Assert.False(SignedHistory.IntervalDue(f.Load(), f.Estate.Clock.AddDays(30)));

        RecordEnvelope adoption = f.Adopt();
        // The adoption itself is the tail's one record until the next checkpoint lists it.
        Assert.False(SignedHistory.IntervalDue(f.Load(), adoption.AtTime.AddHours(23).AddMinutes(59)));
        Assert.True(SignedHistory.IntervalDue(f.Load(), adoption.AtTime.AddHours(24)));

        for (int i = 0; i < SignedHistory.IntervalRecords - 2; i++)
            f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("INT-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), signer: f.OperatorSigner);
        EstateModel model = f.Load();
        Assert.Equal(SignedHistory.IntervalRecords - 1, SignedHistory.Of(model).Tail);
        Assert.Equal(adoption.At, SignedHistory.Of(model).OldestTailAt);
        Assert.False(SignedHistory.IntervalDue(model, f.Estate.Clock, adding: 0));
        Assert.True(SignedHistory.IntervalDue(model, f.Estate.Clock));

        // The next checkpoint lists the tail, and the interval starts again from it.
        RecordEnvelope next = f.Checkpoint(trigger: "interval");
        model = f.Load();
        Assert.Equal(1, SignedHistory.Of(model).Tail);
        Assert.Equal(next.At, SignedHistory.Of(model).OldestTailAt);
        Assert.False(SignedHistory.IntervalDue(model, next.AtTime.AddHours(1)));
    }

    [Fact]
    public void AdoptionAnchorsTheUnsignedRecordsThatCountAndListsWhatItLeavesAsItIs()
    {
        using var f = new HistoryFixture();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.BuilderId, HistoryFixture.Order("ADO-1"), signer: f.BuilderSigner);
        RecordEnvelope admitted = f.Load().Records.Single(r => r.Type == "admission" && r.Subject?.Id == f.BuilderDevice.Id);
        f.Estate.Add("admission", f.Estate.OperatorId, HistoryFixture.Admission(f.Estate.BuilderId, "Revoked.", "revoked", RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1))),
            f.BuilderDevice.Reference, supersedes: admitted.Id, signer: f.OperatorSigner);
        RecordEnvelope charter = f.Require();
        // Dated after the revision requiring signatures, an unsigned record counts for nothing, and adoption leaves it so.
        RecordEnvelope late = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("ADO-2", "Late"));
        EstateModel model = f.Load();

        AdoptionPlan plan = SignedHistory.PlanAdoption(model);
        Assert.False(plan.Adopted);
        Assert.Equal(model.Records.Count, plan.Witnessed);
        Assert.Equal(new[] { f.Estate.OperatorId, f.Estate.Estate.Id, f.Estate.BuilderId }.Order(StringComparer.Ordinal), plan.Anchored.Select(r => r.Id));
        Assert.All(plan.Anchored, r => Assert.Empty(r.Signatures));
        Assert.Equal(late.Id, Assert.Single(plan.NotCounting).Id);
        (RecordEnvelope revoked, string device) = Assert.Single(plan.ByRevokedDevices);
        Assert.Equal((order.Id, f.BuilderDevice.Id), (revoked.Id, device));
        Assert.DoesNotContain(plan.Anchored, r => r.Id == charter.Id);

        // Adopting as planned changes no reading: what counted counts, and what did not stays so.
        File.Delete(f.PathOf(late));
        RecordEnvelope adoption = f.Checkpoint(trigger: "adoption", anchored: SignedHistory.PlanAdoption(f.Load()).Anchored);
        EstateModel adopted = f.Load();
        Assert.True(SignedHistory.Adopted(adopted));
        Assert.False(adopted.Findings.HasErrors, string.Join('\n', adopted.Findings));
        Assert.Equal(3, adoption.Body.GetProperty("anchored").GetArrayLength());
        Assert.True(SignedHistory.PlanAdoption(adopted).Adopted);
    }

    [Fact]
    public void AdoptionWaitsOnTheOperatorOnceTheEstateRunsAReleaseThatWritesCheckpoints()
    {
        using var f = new HistoryFixture();
        Assert.DoesNotContain(Items(f.Load()), i => i["kind"]!.GetValue<string>() == "history-adoption");
        Pin(f, "0.9.1");
        Assert.DoesNotContain(Items(f.Load()), i => i["kind"]!.GetValue<string>() == "history-adoption");

        Pin(f, "0.9.2");
        JsonObject item = Assert.Single(Items(f.Load()), i => i["kind"]!.GetValue<string>() == "history-adoption");
        Assert.Equal("not adopted", item["state"]!.GetValue<string>());
        Assert.Equal("vericairn estate checkpoint --adopt --check", item["inspect"]!.GetValue<string>());
        Assert.Contains("vericairn estate checkpoint --adopt --expect-plan <the plan hash the check prints> --by " + f.Estate.OperatorId, item["waitsOn"]![0]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("Adopt history checkpoints", OperatorActions.RenderText(OperatorActions.Derive(f.Load())), StringComparison.Ordinal);

        // An agent's adoption counts for nothing, so adoption still waits on the operator.
        f.Checkpoint(by: f.Estate.BuilderId, signer: f.BuilderSigner, trigger: "adoption", anchored: SignedHistory.PlanAdoption(f.Load()).Anchored);
        Assert.False(SignedHistory.Adopted(f.Load()));
        Assert.Single(Items(f.Load()), i => i["kind"]!.GetValue<string>() == "history-adoption");

        File.Delete(f.PathOf(f.Load().Records.Single(r => r.Type == "history-checkpoint")));
        f.Adopt();
        Assert.DoesNotContain(Items(f.Load()), i => i["kind"]!.GetValue<string>() == "history-adoption");
    }

    [Fact]
    public void StatusAndTheCanonicalDocumentGiveTheSameHistoryLine()
    {
        using var f = new HistoryFixture();
        EstateModel fresh = f.Load();
        Assert.Contains($"History: not adopted · no heads · 0 witnessed · tail {fresh.Records.Count} · no problems\n", EstateStatus.Render(fresh, new FindingList()), StringComparison.Ordinal);
        Assert.Equal(Line(fresh), HistoryLine.FromStatus(Document(fresh)));

        f.Adopt();
        RecordEnvelope order = f.Estate.Add("work-order", f.Estate.OperatorId, HistoryFixture.Order("LIN-1"), signer: f.OperatorSigner);
        RecordEnvelope head = f.Checkpoint();
        EstateModel adopted = f.Load();
        string line = $"History: adopted · head {head.ComputedHash[7..23]} · {adopted.Records.Count - 1} witnessed · tail 1 · no problems";
        Assert.Equal(line, Line(adopted));
        Assert.Equal(line, HistoryLine.FromStatus(Document(adopted)));
        Assert.Equal(16, HistoryLine.ShortHash(head.ComputedHash).Length);

        // A removed witnessed record is a standing problem on both, and leads status.
        f.Remove(order);
        EstateModel removed = f.Load();
        string text = EstateStatus.Render(removed, new FindingList());
        Assert.Contains(" · 1 problem with what history checkpoints witness; release submission waits until each is settled\n", text, StringComparison.Ordinal);
        Assert.Equal(HistoryLine.FromStatus(Document(removed)), Line(removed));
        Assert.Equal("History: adopted · head " + HistoryLine.ShortHash(head.ComputedHash) + $" · {removed.Records.Count} witnessed · tail 1 · 1 problem", HistoryLine.FromStatus(Document(removed)));
        Assert.Null(HistoryLine.FromStatus(default));
    }

    // The history line status prints, without the sentence that follows it while a problem stands.
    private static string Line(EstateModel model)
    {
        string line = EstateStatus.Render(model, new FindingList()).Split('\n').Single(l => l.StartsWith("History: ", StringComparison.Ordinal));
        int problems = line.IndexOf(" with what", StringComparison.Ordinal);
        return problems < 0 ? line : line[..problems];
    }

    private static JsonElement Document(EstateModel model)
    {
        using JsonDocument document = JsonDocument.Parse(EstateStatus.RenderJson(model, new FindingList()));
        return document.RootElement.Clone();
    }

    private static List<JsonObject> Items(EstateModel model) => [.. OperatorActions.Derive(model)["items"]!.AsArray().OfType<JsonObject>()];

    // The manifest of an estate pinned to a published release, as an upgrade leaves it.
    private static void Pin(HistoryFixture f, string version)
    {
        var manifest = new JsonObject
        {
            ["estate"] = f.Estate.Estate.Id, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3,
            ["vericairn"] = new JsonObject
            {
                ["mode"] = "release", ["repository"] = "Vericairn/vericairn", ["version"] = version,
                ["artifacts"] = new JsonObject { ["linux-x64"] = "sha256:" + new string('1', 64) },
            },
        };
        using JsonDocument document = JsonDocument.Parse(manifest.ToJsonString());
        File.WriteAllBytes(Path.Combine(f.Root, EstateLayout.ManifestPath), CanonicalJson.SerializePrettyUtf8(document.RootElement));
    }
}
