// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class RecordPublicationTests
{
    private const string Order = """{"key":"W-1","kind":"ordinary","title":"Task","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RejectedRecordsAreAbsentDuringJudgmentAndAfterReopen(bool localOnly, bool contentFailure)
    {
        using var estate = new TestEstate();
        using EstateService service = Open(estate, localOnly);
        JsonObject body = JsonNode.Parse(Order)!.AsObject();
        if (contentFailure) body["password"] = "a placeholder, never a credential";
        else body.Remove("key");

        AuthoredRecord? proposed = null;
        bool publishedDuringJudgment = false;
        service.BeforeValidatingRecords = records =>
        {
            proposed = Assert.Single(records);
            publishedDuringJudgment = Published(service, proposed);
        };
        WriteOutcome outcome = service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, JsonSerializer.SerializeToElement(body)));
        Assert.False(publishedDuringJudgment, "A reader saw the proposed record before judgment.");
        Assert.NotNull(proposed);
        Assert.False(outcome.Ok);
        Assert.Equal("rejected", outcome.Problem!.Code);
        if (contentFailure) Assert.Contains(outcome.Problem.Findings, finding => finding.Code == "secrets.key");
        Assert.False(Published(service, proposed));
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Null(reopened.Record(proposed.Envelope.Id));
        Assert.Equal(0, reopened.Check().Errors);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AFailedValidationNeverLeavesAPublishedRecord(bool localOnly, bool accessDenied)
    {
        using var estate = new TestEstate();
        using EstateService service = Open(estate, localOnly);
        AuthoredRecord? proposed = null;
        service.BeforeValidatingRecords = records =>
        {
            proposed = Assert.Single(records);
            if (accessDenied) throw new UnauthorizedAccessException("Injected validation read denial.");
            throw new IOException("Injected validation read failure.");
        };

        WriteOutcome? outcome = null;
        Exception? failure = Record.Exception(() => outcome = service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, JsonSerializer.Deserialize<JsonElement>(Order))));
        Assert.NotNull(proposed);
        Assert.False(Published(service, proposed), "A failed judgment left a record published.");
        Assert.Null(failure);
        Assert.False(outcome!.Ok);
        Assert.Equal("refused", outcome.Problem!.Code);
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Null(reopened.Record(proposed.Envelope.Id));
        Assert.Equal(0, reopened.Check().Errors);
    }

    [Fact]
    public void AFailedPublicationDoesNotLeaveTheValidatedRecord()
    {
        using var estate = new TestEstate();
        using EstateService service = EstateService.Open(estate.Root);
        AuthoredRecord? proposed = null;
        service.BeforeValidatingRecords = records =>
        {
            proposed = Assert.Single(records);
            // Make staging fail after judgment, without depending on platform permissions.
            string staging = Path.Combine(estate.Root, ".vericairn", "record-staging");
            Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
            File.WriteAllText(staging, "An injected I/O obstruction.");
        };
        WriteOutcome outcome = service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, JsonSerializer.Deserialize<JsonElement>(Order)));
        Assert.NotNull(proposed);
        Assert.False(Published(service, proposed));
        Assert.False(outcome.Ok);
        Assert.Equal("refused", outcome.Problem!.Code);
        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Null(reopened.Record(proposed.Envelope.Id));
    }

    private static bool Published(EstateService service, AuthoredRecord record) =>
        File.Exists(Path.Combine(service.Root, record.RelativePath)) || service.Session.Live.Has(record.RelativePath);

    private static EstateService Open(TestEstate estate, bool localOnly)
    {
        if (localOnly)
        {
            IReadOnlyList<RecordFile> files = EstateLoader.EnumerateRecordFiles(estate.Root);
            string manifest = Path.Combine(estate.Root, EstateLayout.ManifestPath);
            JsonObject body = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            body["profile"] = "local-only";
            File.WriteAllText(manifest, body.ToJsonString());
            using var session = Store.EstateSession.Open(estate.Root);
            foreach (RecordFile file in files) session.Live.Put(file.Path, file.Bytes);
            Directory.Delete(Path.Combine(estate.Root, "records"), recursive: true);
        }

        return EstateService.Open(estate.Root);
    }
}
