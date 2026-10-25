// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Tests;

[Collection("home")]
public sealed class DecisionRunProseTests
{
    [Fact]
    public void RefusesModelIdentityInEveryProseFieldFromAValidSettledRun()
    {
        using var run = new RunEstate();
        RunResponse started = run.Start("generic", "generic", run.Report("decision-prose"));
        Assert.True(started.Ok, started.Message);
        run.Settled(started.Run);
        string term = AgentRuns.Find(run.Estate.Load(), started.Run)!.Intent.Body.GetProperty("execution").GetProperty("requested").GetProperty("model").GetString()!;
        run.Service.Providers.Register(new GitCommandAdapter());
        RecordEnvelope ask = run.Estate.Add("ask", run.Estate.BuilderId, JsonSerializer.Serialize(new
        {
            to = run.Estate.OperatorId, question = "Keep the guide?", recommendation = "Keep it.", consequence = "It waits.", state = "open",
        }));
        RunEstate.Git(run.Estate.Root, "init", "-q");
        RunEstate.Git(run.Estate.Root, "config", "core.autocrlf", "false");
        RunEstate.Git(run.Estate.Root, "add", ".");
        RunEstate.Git(run.Estate.Root, "commit", "-qm", "Initial decision fixture");
        string basis = RunEstate.Git(run.Estate.Root, "rev-parse", "HEAD");
        var request = new DecisionNewRequest(run.Estate.BuilderId, ask.Id, "Keep the guide.", RecordParser.FormatTimestamp(DateTimeOffset.UtcNow),
            basis, "Keep the guide", "Keep the guide as it is.", "The operator keeps the existing guide.", ["Keep the guide."], []);
        Assert.Equal(0, run.Service.Check().Errors);
        int before = run.Service.Session.Load().Records.Count;
        string prose = "Recorded (" + term.ToUpperInvariant() + ").";
        foreach (DecisionNewRequest invalid in new[]
        {
            request with { Title = prose }, request with { Statement = prose }, request with { Summary = prose },
            request with { Synthesis = prose }, request with { Effects = [prose] }, request with { NotAuthorized = [prose] },
        })
        {
            DecisionNewResponse refused = run.Service.DecisionFromAsk(invalid);
            Assert.False(refused.Ok);
            Assert.Null(refused.Written);
            Assert.Empty(refused.Pins);
            Assert.Contains("model identity", refused.Problem!.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(term, refused.Problem.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, run.Service.Session.Load().Records.Count);
        }
        DecisionNewResponse accepted = run.Service.DecisionFromAsk(request);
        Assert.True(accepted.Ok, JsonSerializer.Serialize(accepted.Problem));
        Assert.Equal(0, run.Service.Check().Errors);
    }
}
