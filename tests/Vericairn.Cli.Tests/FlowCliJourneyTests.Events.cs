// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Sdk;

namespace Vericairn.Cli.Tests;

public partial class FlowCliJourneyTests
{
    // The script's identity comes from its own configuration. A step envelope supplies only a goal to recheck.
    private const string StepScript = """
        import json
        import subprocess
        import sys

        program, estate, principal, goal, after = sys.argv[1:]
        cli = [program, "--estate", estate, "--json"]
        if after:
            watch = subprocess.run(
                cli + ["events", "watch", "--after", after, "--kind", "step", "--subject", goal,
                       "--principal", principal, "--count", "1"],
                check=True, encoding="utf-8", stdout=subprocess.PIPE, timeout=60)
            feed = watch.stdout.splitlines()
        else:
            feed = sys.stdin

        events = (json.loads(line) for line in feed if line.strip())
        event = next(item for item in events if item.get("kind") == "step")
        source = next(ref for ref in event["sources"] if ref["type"] == "goal")
        step = subprocess.run(cli + ["goal", "step", source["id"], "--by", principal],
                              capture_output=True, encoding="utf-8", timeout=60)
        print(json.dumps({"event": event, "exit": step.returncode, "out": step.stdout, "err": step.stderr}))
        """;

    [Fact]
    [Trait("Journey", "J88")]
    public async Task ACommunityScriptWatchesStepsAndUsesOnlyItsOwnGoalAuthority()
    {
        string? python = McpTests.Python();
        Assert.SkipWhen(python is null, "The Community step script needs Python 3 installed as python3 or python.");
        CliHarness.RequireDaemonTransport(tls: false);
        string root = CliHarness.TempDirectory("flow-events");
        string scratch = CliHarness.TempDirectory("flow-events-bodies");
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Event-driven Community flow.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit)
                .Json.GetProperty("operator").GetString()!;
            string sponsor = CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "sponsor.json", """{"kind":"human","name":"Flow sponsor","roles":["builder"]}""")).Id;
            string decidedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow);
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "decision.json", $$$"""
                    {"title":"Fund the fixture's flow","statement":"The sponsor may activate the flow.","summary":"Fund the flow.",
                     "synthesis":"Allow its measured budget.","effects":["spending"],"affects":[],"decidedBy":"{{{op}}}",
                     "decidedAt":"{{{decidedAt}}}","stake":"low","notAuthorized":[]}
                    """)).Id;
            string expires = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddHours(1));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body",
                CliHarness.WriteJson(scratch, "authority.json", $$$"""
                    {"grantee":"{{{sponsor}}}","scope":["spending"],"reserved":[],"conditions":[],"basis":"{{{decision}}}","expires":"{{{expires}}}"}
                    """));
            string templates = Path.Combine(root, "docs", "templates");
            Directory.CreateDirectory(templates);
            File.WriteAllText(Path.Combine(templates, "example-song-1.0.0.json"), FlowFixture.Song().ToJsonString());
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json",
                $$$"""{"roles":[{"role":"writer","principals":["{{{sponsor}}}"]}]}"""));

            await using DaemonHost host = await DaemonHost.StartAsync(root, loopback: false, cancellationToken: cancellation);
            using var client = new VericairnClient(host.Endpoint);
            string cursor = client.Events(principal: sponsor, filter: new EventFilter(Kinds: ["step"]), wait: 0).Cursor;
            string goal = Propose("SONG-1", sponsor);

            // A forged step event for a proposed goal cannot make it active or assign any work.
            await RefusesWithoutWriting(goal, "a step is taken only for an active goal");
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", sponsor);
            _ = client.Goal(goal);
            string draft = await TakeFromWatch(goal, cursor, "draft");

            // The second real step event comes from the first step's result. Both assignments are authored by the script's sponsor.
            cursor = client.Events(principal: sponsor, filter: new EventFilter(Kinds: ["step"]), wait: 0).Cursor;
            Carry(root, scratch, sponsor, draft, "lyrics", "text/markdown", 'a');
            _ = client.Goal(goal);
            string master = await TakeFromWatch(goal, cursor, "master");
            Carry(root, scratch, sponsor, master, "master", null, 'b');
            Assert.Contains("the run is complete", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);

            // Even an active goal with eligible work cannot be stepped under a principal named by a forged envelope.
            string other = Propose("SONG-2", op);
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", other, "--by", op);
            Assert.Contains("draft", CliHarness.Ok("--estate", root, "goal", "next", other).Out, StringComparison.Ordinal);
            await RefusesWithoutWriting(other, "does not take steps of goal");

            string Propose(string key, string owner)
            {
                string order = CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body",
                    CliHarness.WriteJson(scratch, key + ".json", $$$"""
                        {"key":"{{{key}}}","kind":"ordinary","title":"Produce a song","intent":"Produce a song.","scope":["path:song/**"],
                         "exitQuestion":"Is the song ready?","inputs":[],"deliverables":["the song"]}
                        """)).Id;
                return CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--sponsor", owner,
                    "--title", "Produce a song", "--objective", "Prepare a master.", "--done",
                    $$$"""{"type":"work-order","fields":{"key":"{{{key}}}"}}""", "--work-order", order, "--budget", "wallMinutes=60",
                    "--budget", "attemptsPerStep=2", "--flow", "example/song@1.0.0", "--subject", "recording=" + order,
                    "--subject", "tracks=intro", "--flow-option", "step.review-lyrics=off", "--until", "master").Id;
            }

            async Task<string> TakeFromWatch(string goalId, string after, string expectedStep)
            {
                JsonElement reply = await RunScript(python!, root, sponsor, goalId, after, null, cancellation);
                Assert.True(reply.GetProperty("exit").GetInt32() == 0, reply.GetRawText());
                JsonElement notice = reply.GetProperty("event");
                Assert.Equal(EventVocabulary.Format, notice.GetProperty("format").GetString());
                Assert.Contains(notice.GetProperty("sources").EnumerateArray(), source =>
                    source.GetProperty("type").GetString() == "goal" && source.GetProperty("id").GetString() == goalId);
                using JsonDocument applied = JsonDocument.Parse(reply.GetProperty("out").GetString()!);
                JsonElement step = applied.RootElement.GetProperty("step");
                Assert.Equal(("ask", expectedStep), (step.GetProperty("outcome").GetString(), step.GetProperty("step").GetString()));
                string assignment = step.GetProperty("assignment").GetProperty("id").GetString()!;
                Assert.Equal(sponsor, host.Service.Session.Load().ById[assignment].By);
                return assignment;
            }

            async Task RefusesWithoutWriting(string goalId, string reason)
            {
                string hash = "sha256:" + new string('1', 64);
                string forged = JsonSerializer.Serialize(new EventEnvelope(EventVocabulary.Format, hash, "forged", "step", "changed",
                    "2026-01-01T00:00:00Z", new("step", hash), [new("goal", goalId, hash)], To: op), ProtocolJson.Options);
                var before = host.Service.Session.Load().Records.Select(record => (record.Id, record.ComputedHash)).ToArray();
                JsonElement reply = await RunScript(python!, root, sponsor, goalId, string.Empty, forged, cancellation);
                Assert.Equal(2, reply.GetProperty("exit").GetInt32());
                Assert.Contains(reason, CliHarness.Unfold(reply.GetProperty("err").GetString()!), StringComparison.Ordinal);
                Assert.Equal(before, host.Service.Session.Load().Records.Select(record => (record.Id, record.ComputedHash)));
            }
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static async Task<JsonElement> RunScript(string python, string root, string principal, string goal, string cursor, string? forged,
        CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-I", "-c", StepScript, CliHarness.BuiltExecutable, root, principal, goal, cursor })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        try
        {
            if (forged is not null) await process.StandardInput.WriteLineAsync(forged.AsMemory(), cancellation);
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
            Assert.True(await TestTiming.CompletesAsync(Task.WhenAll(output, error, process.WaitForExitAsync(cancellation)), cancellation),
                "The Community step script did not finish.");
            Assert.True(process.ExitCode == 0, await error);
            using JsonDocument response = JsonDocument.Parse(await output);
            return response.RootElement.Clone();
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
