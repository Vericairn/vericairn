// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public class WorkProgressTests
{
    [Fact]
    public void InterimNotesReplaceLiveStateAndACompletionIsIdempotentAcrossReopen()
    {
        using var estate = new TestEstate(recordFormat: 2);
        string attempt;
        int records;
        using (EstateService service = EstateService.Open(estate.Root))
        {
            attempt = Attempt(estate, service);
            records = service.Session.Load().Records.Count;
            Assert.True(service.WorkProgress(new WorkProgressRequest(attempt, estate.BuilderId, "build", "Compiling.")).Ok);
            WorkProgressResponse latest = service.WorkProgress(new WorkProgressRequest(attempt, estate.BuilderId, "build", "Checking the build."));
            Assert.True(latest.Ok);
            Assert.Null(latest.Written);
            Assert.Equal(records, service.Session.Load().Records.Count);
            Assert.Contains("Checking the build.", service.Progress().Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Compiling.", service.Progress().Text, StringComparison.Ordinal);
            Assert.Equal("Checking the build.", service.Progress().Json.GetProperty("agentClaims")[0].GetProperty("note").GetString());
            Assert.Empty(service.Runs().Runs);
        }

        using EstateService reopened = EstateService.Open(estate.Root);
        Assert.Contains("Checking the build.", reopened.Progress().Text, StringComparison.Ordinal);
        var complete = new WorkProgressRequest(attempt, estate.BuilderId, "build", "Build passed.", Completed: true);
        WorkProgressResponse first = reopened.WorkProgress(complete);
        Assert.True(first.Ok, first.Problem?.Message);
        Assert.Equal(first.Written, reopened.WorkProgress(complete).Written);
        Assert.Equal(records + 1, reopened.Session.Load().Records.Count);
        Assert.False(reopened.WorkProgress(complete with { Note = "Changed claim." }).Ok);
        Assert.False(reopened.WorkProgress(complete with { Completed = false }).Ok);
        Assert.True(reopened.WorkProgress(complete with { Phase = "tests", Completed = false, Note = "Testing." }).Ok);
        Assert.True(reopened.WorkProgress(complete).Ok);
        Assert.Contains("Testing.", reopened.Progress().Text, StringComparison.Ordinal);
        reopened.Session.Live.SetMeta("work-progress:" + attempt, "null");
        Assert.Contains("Build passed.", reopened.Progress().Text, StringComparison.Ordinal);
        Assert.Contains("[work-progress " + first.Written!.Id, reopened.Progress().Text, StringComparison.Ordinal);
        Assert.Equal(0, reopened.Check().Errors);
    }

    [Fact]
    public void ClaimsRejectAnotherContributorUnportableTextAndClosedAttemptsWithoutWrites()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        string attempt = Attempt(estate, service);
        int records = service.Session.Load().Records.Count;
        var request = new WorkProgressRequest(attempt, estate.BuilderId, "build", "Building.");
        Assert.False(service.WorkProgress(request with { By = estate.OperatorId }).Ok);
        Assert.False(service.WorkProgress(request with { Phase = " " }).Ok);
        Assert.False(service.WorkProgress(request with { Note = new string('x', 2001) }).Ok);
        Assert.False(service.WorkProgress(request with { Note = "See " + string.Join('/', "", "Users", "example", "work") }).Ok);
        Assert.Equal(records, service.Session.Load().Records.Count);
        Assert.Empty(service.Progress().Json.GetProperty("agentClaims").EnumerateArray());
        RunEstate.Ok(service.Result(new ResultRequest(attempt, estate.BuilderId, RunEstate.Json("""{"outcome":"completed","summary":"Done.","outputs":[],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"Review it."}"""))));
        Assert.False(service.WorkProgress(request).Ok);
        Assert.Equal(records + 1, service.Session.Load().Records.Count);
    }

    [Fact]
    public void ACompletedPhaseIsAnAttributableClaimAndCannotBeRecordedTwice()
    {
        using var estate = new TestEstate(recordFormat: 2);
        using EstateService service = EstateService.Open(estate.Root);
        string attempt = Attempt(estate, service);
        const string body = """{"phase":"implementation","note":"The widget builds.","completedAt":"2026-10-16T11:00:00Z"}""";
        RecordWritten written = RunEstate.Ok(service.NewRecord(new NewRecordRequest("work-progress", estate.BuilderId, RunEstate.Json(body), "attempt:" + attempt + ":" + service.Record(attempt)!.Hash)));
        Assert.Equal("work-progress", service.Record(written.Id)!.Type);
        Assert.False(service.NewRecord(new NewRecordRequest("work-progress", estate.BuilderId, RunEstate.Json(body), "attempt:" + attempt + ":" + service.Record(attempt)!.Hash)).Ok);
        Assert.False(service.NewRecord(new NewRecordRequest("work-progress", estate.OperatorId, RunEstate.Json(body.Replace("implementation", "review", StringComparison.Ordinal)), "attempt:" + attempt + ":" + service.Record(attempt)!.Hash)).Ok);
        Assert.Equal(0, service.Check().Errors);
    }

    private static string Attempt(TestEstate estate, EstateService service)
    {
        RunEstate.Ok(service.NewRecord(new NewRecordRequest("work-order", estate.OperatorId, RunEstate.Json("""{"key":"W-1","kind":"ordinary","title":"Widget","intent":"Build it.","scope":["widget"],"exitQuestion":"Built?","inputs":[],"deliverables":["widget"]}"""))));
        string assignment = RunEstate.Ok(service.Assign(new AssignRequest("W-1", estate.BuilderId, estate.OperatorId, RunEstate.Json(RunEstate.Brief)))).Id;
        return RunEstate.Ok(service.Attempt(new AttemptRequest(assignment, estate.BuilderId, "widget repository, work branch", "Build it."))).Id;
    }
}
