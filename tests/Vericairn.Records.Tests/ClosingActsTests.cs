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
/// its device, a key rotation the revision whose key it replaces, a narrowing grant revision the revision it narrows; nothing else needs
/// a closing; and adoption's check compares the reading of today with each record read at its own time.
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
