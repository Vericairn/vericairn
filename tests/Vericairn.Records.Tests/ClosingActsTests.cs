// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.History;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

/// <summary>
/// The acts a closing precedes and what the at-time reading changes (design/signed-history.md §§ 8 and 10; OPS-07): a revocation names
/// its device, a key rotation the revision whose key it replaces, a narrowing grant revision the revision it replaces and narrows, the
/// nearest one below it that counts; nothing else needs a closing; and adoption's check compares the reading of today with each record
/// read at its own time.
/// </summary>
public class ClosingActsTests
{
    [Fact]
    public void ARevocationARotationAndANarrowingGrantRevisionEachNameWhatTheirClosingCloses()
    {
        using var f = new HistoryFixture();
        (RecordEnvelope device, RecordEnvelope admission) = f.Admitted(f.Estate.BuilderId);
        RecordEnvelope revocation = f.Revoke(device, admission);
        (RecordEnvelope rotation, _) = f.Rotate(f.BuilderDevice, f.BuilderSigner);
        RecordEnvelope basis = f.Decision();
        RecordEnvelope grant = f.Grant(f.Estate.BuilderId, basis, [Scopes.LocalEngineering, Scopes.AcceptWork]);
        RecordEnvelope narrowed = f.Grant(f.Estate.BuilderId, basis, [Scopes.LocalEngineering], supersedes: grant.Id);
        RecordEnvelope widened = f.Grant(f.Estate.BuilderId, basis, [Scopes.LocalEngineering, Scopes.Qualify], supersedes: narrowed.Id);
        EstateModel model = f.Load();

        Assert.Equal(("device", device.Id), ClosingActs.For(model, revocation));
        Assert.Equal(("key", f.BuilderDevice.Id), ClosingActs.For(model, rotation));
        Assert.Equal(("grant", grant.Id), ClosingActs.For(model, narrowed));
        Assert.Null(ClosingActs.For(model, widened));
        // An admission, a first record and a rename close nothing.
        Assert.Null(ClosingActs.For(model, admission));
        Assert.Null(ClosingActs.For(model, grant));
        JsonObject renamed = HistoryFixture.Body(device);
        renamed["name"] = "Renamed";
        Assert.Null(ClosingActs.For(model, "device", JsonSerializer.SerializeToElement(renamed), null, device.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGrantRevisionOnTopOfOneThatDoesNotCountIsComparedWithAndNamesTheRevisionItReplaces(bool unsignedBelow)
    {
        using var f = new HistoryFixture();
        RecordEnvelope basis = f.Decision();
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Administrator","roles":["reviewer"]}""", signer: f.OperatorSigner);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope grant = f.Grant(administrator.Id, basis, [Scopes.Administer, Scopes.LocalEngineering]);
        RecordEnvelope below = unsignedBelow ? UnsignedBelow(f, administrator, basis, grant) : RefusedBelow(f, administrator, basis, grant);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        // Compared with the revision below it, it changes nothing; compared with the grant it replaces, it takes administer away.
        RecordEnvelope narrowed = f.Grant(administrator.Id, basis, [Scopes.LocalEngineering], supersedes: below.Id);
        EstateModel model = f.Load();

        Assert.False(model.Counts(model.ById[below.Id]));
        Assert.True(model.Counts(model.ById[narrowed.Id]));
        Assert.Equal(grant.Id, model.Trust.AttachedTo(model.ById[narrowed.Id])!.Id);
        Assert.False(model.HasScope(administrator.Id, Scopes.Administer));
        Assert.Equal(("grant", grant.Id), ClosingActs.For(model, model.ById[narrowed.Id]));

        // Written as a file without its closing, it is warned, naming the grant it replaces, until a closing names that grant.
        Finding missing = Assert.Single(model.Findings, x => x.Code == "history.closing-missing");
        Assert.Equal(narrowed.ExpectedPath, missing.Location);
        Assert.Contains($"This revision narrows grant revision {grant.Id}", missing.Message, StringComparison.Ordinal);
        Assert.Contains($"its closes naming grant {grant.Id}", missing.Message, StringComparison.Ordinal);
        RecordEnvelope closing = f.Close("grant", grant.Id);
        model = f.Load();
        Assert.Contains(model.Trust.Closings, c => c.Record.Id == closing.Id && c.Closes(model.ById[grant.Id]));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-missing" or "history.closing-unfollowed");

        // A revision that keeps what the grant it replaces gave narrows nothing, whatever the revision below it said.
        RecordEnvelope kept = f.Grant(administrator.Id, basis, [Scopes.Administer, Scopes.LocalEngineering], supersedes: below.Id);
        Assert.Null(ClosingActs.For(f.Load(), kept));
    }

    [Fact]
    public void AGrantRevisionWithNoRevisionBelowItThatCountsNarrowsNothing()
    {
        using var f = new HistoryFixture();
        RecordEnvelope basis = f.Decision();
        f.Require();
        f.Adopt();
        // Unsigned under required signatures in an adopted estate: neither the first record of the line nor its revision counts.
        RecordEnvelope first = f.Estate.Add("authority", f.Estate.OperatorId, GrantBody(f.Estate.BuilderId, basis, Scopes.LocalEngineering, Scopes.AcceptWork));
        RecordEnvelope unsigned = f.Estate.Add("authority", f.Estate.OperatorId, GrantBody(f.Estate.BuilderId, basis, Scopes.LocalEngineering, Scopes.AcceptWork, Scopes.Qualify), supersedes: first.Id);
        EstateModel model = f.Load();
        Assert.False(model.Counts(model.ById[first.Id]));
        Assert.False(model.Counts(model.ById[unsigned.Id]));
        Assert.Null(ClosingActs.For(model, "authority", JsonSerializer.SerializeToElement(JsonNode.Parse(GrantBody(f.Estate.BuilderId, basis, Scopes.LocalEngineering))), null, unsigned.Id));
    }

    [Fact]
    public void AClosingBeforeANarrowingOnTopOfARevisionRefusedForItsDateClosesTheGrantItReplaces()
    {
        using var f = new HistoryFixture();
        RecordEnvelope basis = f.Decision();
        RecordEnvelope administrator = f.Estate.Add("principal", f.Estate.OperatorId, """{"kind":"human","name":"Administrator","roles":["reviewer"]}""", signer: f.OperatorSigner);
        (RecordEnvelope device, _) = f.Admitted(administrator.Id, "The administrator's device");
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));
        RecordEnvelope grant = f.Grant(administrator.Id, basis, [Scopes.Administer, Scopes.LocalEngineering]);
        RecordEnvelope refused = RefusedBelow(f, administrator, basis, grant);
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(10));

        // The closing the narrowing needs names the grant it replaces, not the refused revision it supersedes.
        string narrowing = GrantBody(administrator.Id, basis, Scopes.LocalEngineering);
        Assert.Equal(("grant", grant.Id), ClosingActs.For(f.Load(), "authority", JsonSerializer.SerializeToElement(JsonNode.Parse(narrowing)), null, refused.Id));
        RecordEnvelope closing = f.Close("grant", grant.Id);
        RecordEnvelope narrowed = f.Estate.Add("authority", f.Estate.OperatorId, narrowing, supersedes: refused.Id, signer: f.OperatorSigner);
        DateTimeOffset resume = f.Estate.Clock;
        // Written after the closing and dated while the grant was in force: the closed grant no longer accounts for it.
        f.Estate.SetClock(grant.AtTime.AddMinutes(1));
        RecordEnvelope backdated = f.Estate.Add("work-order", administrator.Id, HistoryFixture.Order("AUR-9", "Dated into the grant"), signer: f.Signer(device));
        f.Estate.SetClock(resume);
        EstateModel model = f.Load();

        Closing acting = Assert.Single(model.Trust.Closings);
        Assert.Equal(closing.Id, acting.Record.Id);
        Assert.True(acting.Closes(model.ById[grant.Id]));
        Assert.True(acting.Closes(model.ById[refused.Id]));
        Assert.True(model.Counts(model.ById[narrowed.Id]));
        Assert.True(model.Trust.Followed(acting));
        Assert.DoesNotContain(model.Findings, x => x.Code is "history.closing-missing" or "history.closing-unfollowed");
        Assert.False(model.Counts(model.ById[backdated.Id]));
        Assert.Contains(model.Findings, x => x.Code == "history.after-closing" && x.Location == backdated.ExpectedPath);
    }

    /// <summary>A narrowing of the grant dated before it, which counts for nothing, before the estate adopts.</summary>
    private static RecordEnvelope RefusedBelow(HistoryFixture f, RecordEnvelope administrator, RecordEnvelope basis, RecordEnvelope grant)
    {
        f.Estate.SetClock(grant.AtTime.AddMinutes(-5));
        RecordEnvelope refused = f.Grant(administrator.Id, basis, [Scopes.LocalEngineering], supersedes: grant.Id);
        f.Estate.SetClock(grant.AtTime.AddMinutes(5));
        f.Require();
        f.Adopt();
        return refused;
    }

    /// <summary>An unsigned narrowing of the grant, which counts for nothing under required signatures once the estate has adopted.</summary>
    private static RecordEnvelope UnsignedBelow(HistoryFixture f, RecordEnvelope administrator, RecordEnvelope basis, RecordEnvelope grant)
    {
        f.Require();
        f.Adopt();
        f.Estate.SetClock(f.Estate.Clock.AddMinutes(5));
        return f.Estate.Add("authority", f.Estate.OperatorId, GrantBody(administrator.Id, basis, Scopes.LocalEngineering), supersedes: grant.Id);
    }

    private static string GrantBody(string grantee, RecordEnvelope basis, params string[] scopes) =>
        new JsonObject { ["grantee"] = grantee, ["scope"] = new JsonArray([.. scopes.Select(s => (JsonNode)s)]), ["reserved"] = new JsonArray(), ["conditions"] = new JsonArray(), ["basis"] = basis.Id }.ToJsonString();

    [Fact]
    public void AGrantRevisionNarrowsUnlessItKeepsOrWidensEveryPart()
    {
        static JsonElement Body(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        JsonElement grant = Body("""{"grantee":"A","scope":["x","y"],"reserved":["publication"],"conditions":["c"],"basis":"B","expires":"2027-01-01T00:00:00Z"}""");
        Assert.False(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["y","x","z"],"reserved":[],"conditions":[],"basis":"C"}""")));
        Assert.False(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["x","y"],"reserved":["publication"],"conditions":["c"],"basis":"B","expires":"2028-01-01T00:00:00Z"}""")));
        Assert.True(ClosingActs.Narrows(grant, Body("""{"grantee":"Z","scope":["x","y"],"reserved":["publication"],"conditions":["c"],"basis":"B"}""")));
        Assert.True(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["x"],"reserved":["publication"],"conditions":["c"],"basis":"B"}""")));
        Assert.True(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["x","y"],"reserved":["publication","spending"],"conditions":["c"],"basis":"B"}""")));
        Assert.True(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["x","y"],"reserved":["publication"],"conditions":["c","d"],"basis":"B"}""")));
        Assert.True(ClosingActs.Narrows(grant, Body("""{"grantee":"A","scope":["x","y"],"reserved":["publication"],"conditions":["c"],"basis":"B","expires":"2026-12-01T00:00:00Z"}""")));
        Assert.True(ClosingActs.Narrows(Body("""{"grantee":"A","scope":["x"],"reserved":[],"conditions":[],"basis":"B"}"""), Body("""{"grantee":"A","scope":["x"],"reserved":[],"conditions":[],"basis":"B","expires":"2027-01-01T00:00:00Z"}""")));
        Assert.True(ClosingActs.Narrows(Body("""{"grantee":"A","scope":["x"],"reserved":[],"conditions":[],"basis":"B","bounds":[{"scope":"x","n":1}]}"""), Body("""{"grantee":"A","scope":["x"],"reserved":[],"conditions":[],"basis":"B","bounds":[{"scope":"x","n":2}]}""")));
        Assert.False(ClosingActs.Narrows(Body("""{"grantee":"A","scope":["x"],"reserved":[],"conditions":[],"basis":"B","bounds":[{"scope":"x","n":1}]}"""), Body("""{"grantee":"A","scope":["x"],"basis":"C","conditions":[],"reserved":[],"bounds":[ {"n":1,"scope":"x"} ]}""")));
    }

    [Fact]
    public void TheAtTimeReadingListsARecordThatHoldsOnlyByALaterGrant()
    {
        using var f = new HistoryFixture();
        RecordEnvelope basis = f.Decision();
        string builder = f.Estate.BuilderId;
        DateTimeOffset start = f.Estate.Clock;

        // Dated before the builder held administer: today's reading admits through the later grant; read at its own time it does not.
        (RecordEnvelope early, RecordEnvelope earlyAdmission) = f.Admitted(f.Estate.OperatorId, "An early device");
        RecordEnvelope backdated = f.Estate.Add("admission", builder, HistoryFixture.Admission(f.Estate.OperatorId, "Revoked before the grant.", "revoked", RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1))),
            early.Reference, supersedes: earlyAdmission.Id, signer: f.BuilderSigner);
        f.Estate.SetClock(start.AddHours(1));
        f.Grant(builder, basis, [Scopes.Administer]);

        EstateModel today = f.Load();
        Assert.DoesNotContain(today.Findings, x => x.Severity == Severity.Error && x.Location.StartsWith(backdated.ExpectedPath, StringComparison.Ordinal));
        EstateModel atTime = AtRecordTime(f);
        Assert.True(atTime.AuthorityAtRecordTime);
        Assert.False(today.AuthorityAtRecordTime);
        (RecordEnvelope record, string note) = Assert.Single(AtTimeReading.Changes(today, atTime));
        Assert.Equal(backdated.Id, record.Id);
        Assert.StartsWith("read at its own time, admission.author: ", note, StringComparison.Ordinal);

        // A model read as today never reads at record time, so nothing differs from itself.
        Assert.Empty(AtTimeReading.Changes(today, f.Load()));
    }

    [Fact]
    public void TheAtTimeReadingListsARecordALaterNarrowingUndid()
    {
        using var f = new HistoryFixture();
        RecordEnvelope basis = f.Decision();
        string builder = f.Estate.BuilderId;
        RecordEnvelope grant = f.Grant(builder, basis, [Scopes.Administer]);

        // Written while the builder held administer, which a later narrowing took away: today it fails; at its own time it holds.
        (RecordEnvelope device, RecordEnvelope admission) = f.Admitted(f.Estate.OperatorId, "A later device");
        RecordEnvelope undone = f.Estate.Add("admission", builder, HistoryFixture.Admission(f.Estate.OperatorId, "Revoked under the grant.", "revoked", RecordParser.FormatTimestamp(f.Estate.Clock.AddSeconds(1))),
            device.Reference, supersedes: admission.Id, signer: f.BuilderSigner);
        f.Grant(builder, basis, [Scopes.LocalEngineering], supersedes: grant.Id);

        (RecordEnvelope record, string note) = Assert.Single(AtTimeReading.Changes(f.Load(), AtRecordTime(f)));
        Assert.Equal(undone.Id, record.Id);
        Assert.StartsWith("read today, admission.author: ", note, StringComparison.Ordinal);
        Assert.EndsWith("At its own time this does not hold.", note, StringComparison.Ordinal);
    }

    private static EstateModel AtRecordTime(HistoryFixture f) => EstateLoader.LoadAtRecordTime(f.Root, EstateLoader.EnumerateRecordFiles(f.Root), EstateLoader.FileObjects(f.Root));
}
