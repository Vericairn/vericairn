// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Cli;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

/// <summary>
/// A release of a small product on the software pack's release flow from the command line, with a test release provider (the
/// estate's design/release-procedure.md § 10): its placed work accepted, the guides checked before freezing, the set and its merge
/// handed on, the submission dispatched and the candidate observed through the provider, the Reviewer's review, the operator's
/// acceptance and publication each put to the operator only when next, the documentation pass and the carrier closed.
/// </summary>
[Collection("cli")]
public class ReleaseFlowJourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Version = "0.2.0";
    private const string Repository = "Example/widget";
    private const string Product = """{"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];

    [Fact]
    public void AReleaseRunsFromPlacedWorkToTheClosedCarrierOnTheFlowWithATestProvider()
    {
        string root = CliHarness.TempDirectory("release-flow");
        string scratch = CliHarness.TempDirectory("release-flow-scratch");
        string guides = CliHarness.TempDirectory("release-flow-guides");
        IReleaseProvider? previous = Commands.ReleaseProvider;
        var provider = new FakeReleaseProvider();
        Commands.ReleaseProvider = provider;
        try
        {
            string op = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Releases on the flow.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string builder = Principal(root, op, scratch, "Builder", "builder");
            string reviewer = Principal(root, op, scratch, "Reviewer", "reviewer");
            Signers(root, op, builder);
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", op, "--body", CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Delegate","stake":"low","notAuthorized":[],"statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""")).Id;
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision}}"}"""));
            string product = CliHarness.Ok("--estate", root, "--json", "records", "new", "product", "--by", op, "--body", CliHarness.WriteJson(scratch, "product.json", Product)).Id;

            // The carrier and its placed work, accepted.
            string carrier = Placed(root, scratch, op, "REL-1", new JsonObject { ["releasePlan"] = new JsonObject { ["product"] = product, ["version"] = Version } });
            string member = Placed(root, scratch, op, "W-1", new JsonObject { ["placement"] = new JsonObject { ["kind"] = "release", ["carrier"] = carrier, ["product"] = product, ["version"] = Version } });
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{member}:{HashOf(root, member)}", "--body", CliHarness.WriteJson(scratch, "accept-w1.json", """{"statement":"Accepted W-1.","basis":[],"self":true}"""));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "roster", "--by", op, "--body", CliHarness.WriteJson(scratch, "roster.json",
                $$"""{"roles":[{"role":"release-manager","principals":["{{builder}}"]},{"role":"submitter","principals":["{{builder}}"]},{"role":"reviewer","principals":["{{reviewer}}"]},{"role":"decider","principals":["{{op}}"]},{"role":"publisher","principals":["{{op}}"]}]}"""));

            string goal = CliHarness.Ok("--estate", root, "--json", "goal", "propose", "--by", op, "--title", "Release " + Version, "--objective", "Release the widget.",
                "--done", $$"""{"type":"acceptance","subject":"{{carrier}}"}""", "--work-order", carrier, "--budget", "wallMinutes=600", "--budget", "attemptsPerStep=2",
                "--flow", "vericairn.software/release@1.0.0", "--subject", "carrier=" + carrier, "--subject", "product=" + product, "--subject", "version=" + Version,
                "--subject", "repository=" + Repository).Id;
            CliHarness.Ok("--estate", root, "--json", "goal", "activate", goal, "--by", op);
            Assert.Equal(("done", "next"), (State(root, goal, "placed-work"), State(root, goal, "docs-before-freeze")));

            // The guides are checked before freezing and the checks recorded at their clean commit.
            File.WriteAllText(Path.Combine(guides, "documents.json"), """["install.md"]""");
            Directory.CreateDirectory(Path.Combine(guides, "docs"));
            File.WriteAllText(Path.Combine(guides, "docs", "install.md"), "# Installing Widget " + Version + "\n");
            Directory.CreateDirectory(Path.Combine(guides, "docs", "releases"));
            File.Move(Path.Combine(guides, "documents.json"), Path.Combine(guides, "docs", "releases", "documents.json"));
            Git(guides, "init", "--initial-branch=main");
            Git(guides, "add", "--all");
            Git(guides, "commit", "-m", "Guides for " + Version);
            CliHarness.Ok("--estate", root, "docs", "pass", Version, "--before", "--documents", Path.Combine(guides, "docs", "releases", "documents.json"), "--product", "widget", "--record", "--by", builder);
            Assert.Equal(("done", "next"), (State(root, goal, "docs-before-freeze"), State(root, goal, "docs-set")));

            // The set and its merge are the release manager's work, handed on by hash.
            WorkStep(root, scratch, op, builder, carrier, "docs-set", ("manifest", 'a', "application/json"), ("notes", 'b', "text/markdown"));
            WorkStep(root, scratch, op, builder, carrier, "release-commit", ("frozen-index", 'c', "application/json"));
            Assert.Equal(("next", "blocked", "blocked"), (State(root, goal, "reviews-at-commit"), State(root, goal, "gate"), State(root, goal, "submit")));

            // Submitted through the test provider: the intent is recorded before the dispatch, and the gate and the submission are done.
            CliHarness.Outcome submitted = ReleaseJourney.OkHeld(provider.HeadSha, "--estate", root, "--json", "release", "submit", "--provider", "github-releases", "--product", product, "--version", Version, "--repository", Repository, "--by", builder);
            Assert.Equal("dispatched", submitted.Json.GetProperty("status").GetString());
            Assert.Equal(("done", "done", "done"), (State(root, goal, "reviews-at-commit"), State(root, goal, "gate"), State(root, goal, "submit")));
            Assert.Equal("next", State(root, goal, "candidate"));

            // The provider's run completes: the candidate is observed, and the review is next; no reserved step shows a command yet.
            provider.CompleteRun(Repository, submitted.Json.GetProperty("run").GetString()!, true, Version);
            CliHarness.Outcome built = CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", Version, "--repository", Repository, "--by", builder, "--qualified", "linux-x64", "--limitation", "Nothing signed.");
            string candidate = built.Json.GetProperty("candidate").GetProperty("id").GetString()!;
            string candidateHash = built.Json.GetProperty("candidate").GetProperty("hash").GetString()!;
            Assert.Equal(("done", "next", "blocked"), (State(root, goal, "candidate"), State(root, goal, "review"), State(root, goal, "accept")));
            Assert.All(Steps(root, goal), s => Assert.Equal(JsonValueKind.Null, s.GetProperty("command").ValueKind));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "review", "--by", reviewer, "--subject", $"release:{candidate}:{candidateHash}",
                "--body", CliHarness.WriteJson(scratch, "release-review.json", """{"independence":"independent-agent","outcome":"pass","summary":"Reviewed the exact candidate.","findings":[]}"""));

            // Acceptance is next: goal step puts it to the operator with the candidate's exact hash, and publication still shows nothing.
            string accept = $"vericairn records new acceptance --by <operator-id> --subject release:{candidate}:{candidateHash} --body accept-release.json";
            JsonElement accepting = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("request", "accept"), (accepting.GetProperty("outcome").GetString(), accepting.GetProperty("step").GetString()));
            Assert.Contains(accept, CliHarness.Ok("--estate", root, "operator", "list").Out, StringComparison.Ordinal);
            Assert.DoesNotContain("--draft=false", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"release:{candidate}:{candidateHash}", "--body", CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted 0.2.0 from the recorded evidence.","basis":[],"self":false}"""));
            Assert.Equal("done", State(root, goal, "accept"));

            // Publication is next: its request carries the provider's stable-channel command, which the operator runs and observes.
            JsonElement publishing = CliHarness.Ok("--estate", root, "--json", "goal", "step", goal, "--by", op).Json.GetProperty("step");
            Assert.Equal(("request", "publish"), (publishing.GetProperty("outcome").GetString(), publishing.GetProperty("step").GetString()));
            Assert.Contains($"gh release edit v{Version} -R {Repository} --draft=false --latest && vericairn release observe {Version} --by <operator-id>", CliHarness.Ok("--estate", root, "operator", "list").Out, StringComparison.Ordinal);
            provider.Publish(Repository, "v" + Version);
            Assert.Equal("published", CliHarness.Ok("--estate", root, "--json", "release", "observe", "--version", Version, "--repository", Repository, "--by", op).Json.GetProperty("status").GetString());
            Assert.Equal(("done", "done", "done", "done"), (State(root, goal, "publish"), State(root, goal, "observe"), State(root, goal, "review"), State(root, goal, "accept")));

            // Nothing the publication made due is open, and the operator closes the carrier: the run is complete.
            Assert.Equal(("done", "stopped"), (State(root, goal, "docs-after"), State(root, goal, "close")));
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"work-order:{carrier}:{HashOf(root, carrier)}", "--body", CliHarness.WriteJson(scratch, "close.json", """{"statement":"Release 0.2.0 is published and nothing it made due is open.","basis":[],"self":true}"""));
            Assert.Contains("the run is complete", CliHarness.Ok("--estate", root, "goal", "show", goal).Out, StringComparison.Ordinal);
            CliHarness.Outcome check = CliHarness.Run("--estate", root, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
        }
        finally
        {
            Commands.ReleaseProvider = previous;
            CliHarness.Delete(root, scratch, guides);
        }
    }

    private static string Principal(string root, string op, string scratch, string name, string role) =>
        CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", CliHarness.WriteJson(scratch, $"principal-{name}.json", $$$"""{"kind":"agent","name":"{{{name}}}","roles":["{{{role}}}"],"agent":{"vendor":"Example","product":"Example Agent"}}""")).Id;

    /// <summary>Admitted signing devices for the operator and the builder, as the release journey's signers.</summary>
    private static void Signers(string root, params string[] principals)
    {
        using EstateService api = EstateService.Open(root);
        string op = principals[0];
        foreach (string principal in principals)
        {
            WriteOutcome device = api.DeviceCreate(new(principal, "Fixture release signer", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            WriteOutcome admitted = api.DeviceAdmit(new(device.Written!.Id, op, "Admit the fixture signer."));
            Assert.True(admitted.Ok, admitted.Problem?.Message);
        }
    }

    /// <summary>A work order with a release plan or a placement, recorded with the canonical body hash placement authoring expects.</summary>
    private static string Placed(string root, string scratch, string op, string key, JsonObject extra)
    {
        var body = new JsonObject
        {
            ["key"] = key, ["kind"] = "ordinary", ["title"] = "Order " + key, ["intent"] = "Carry " + key + ".", ["scope"] = new JsonArray("widget"),
            ["exitQuestion"] = "Is " + key + " done?", ["inputs"] = new JsonArray(), ["deliverables"] = new JsonArray("the work"),
        };
        foreach ((string name, JsonNode? value) in extra.ToList())
        {
            extra.Remove(name);
            body[name] = value;
        }

        string expected = PolicyBasis.Hash(JsonSerializer.SerializeToElement(body));
        return CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--expected", expected, "--body", CliHarness.WriteJson(scratch, key + ".json", body.ToJsonString())).Id;
    }

    /// <summary>The release manager's assignment of a work step, its attempt and its completed result naming each output by hash.</summary>
    private static void WorkStep(string root, string scratch, string op, string builder, string carrier, string step, params (string Name, char Hash, string MediaType)[] outputs)
    {
        string assignment = CliHarness.Ok("--estate", root, "--json", "records", "new", "assignment", "--by", op, "--subject", $"work-order:{carrier}:{HashOf(root, carrier)}", "--body", CliHarness.WriteJson(scratch, step + "-assignment.json", $$$"""
            {"contributor":"{{{builder}}}","step":{"key":"{{{step}}}"},"brief":{"outcome":"Carry step {{{step}}}.","sources":[],"permittedChanges":["docs/"],"prohibitedEffects":["publication"],"workspace":["the widget repository"],"limits":[],"expectedOutputs":["the step's outputs"],"reviewCriteria":["the outputs exist"],"recovery":"Carry it again."}}
            """)).Id;
        string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "the widget repository, a work branch", "--note", "Carry the step.").Id;
        var named = new JsonArray(outputs.Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["hash"] = "sha256:" + new string(o.Hash, 64), ["mediaType"] = o.MediaType }).ToArray());
        var body = new JsonObject
        {
            ["outcome"] = "completed", ["summary"] = "Carried the step.", ["outputs"] = named, ["evidence"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["limitations"] = new JsonArray(), ["unresolvedEffects"] = new JsonArray(), ["nextStep"] = "Hand it on.",
        };
        CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", CliHarness.WriteJson(scratch, step + "-result.json", body.ToJsonString()));
    }

    private static string HashOf(string root, string id) => EstateLoader.Load(root).ById[id].ComputedHash;

    private static JsonElement[] Steps(string root, string goal) =>
        CliHarness.Ok("--estate", root, "--json", "goal", "show", goal).Json.GetProperty("flow").GetProperty("steps").EnumerateArray().ToArray();

    private static string? State(string root, string goal, string key) =>
        Assert.Single(Steps(root, goal), s => s.GetProperty("key").GetString() == key).GetProperty("state").GetString();

    private static void Git(string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (string argument in GitOptions.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }
}
