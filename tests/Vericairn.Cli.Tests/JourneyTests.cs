// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Hashing;

namespace Vericairn.Cli.Tests;

/// <summary>Journeys from the design, run headless through the CLI: every step is a record and nothing lives outside the repository.</summary>
[Collection("cli")]
public class JourneyTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Brief = """{"outcome":"A widget that builds and passes its tests.","sources":[],"permittedChanges":["the widget sources and tests"],"prohibitedEffects":["no publication","no spending"],"workspace":["example repository, main branch"],"limits":["one working session"],"expectedOutputs":["widget.txt"],"reviewCriteria":["build passes","tests pass","output hash verified"],"recovery":"Record a result before stopping; whoever continues reads the brief and the last result."}""";
    private const string PartialResult = """{"outcome":"partial","summary":"Implemented half of the widget before the usage limit was reached.","outputs":[],"evidence":[],"checks":[{"name":"tests","outcome":"not-performed","summary":"Not run before the limit."}],"limitations":["Tests were not run."],"unresolvedEffects":["A publication to the example feed may have been dispatched; reconcile it under its original identity before any retry."],"nextStep":"Reconcile the possible dispatch, then run the tests and finish the widget."}""";

    [Fact]
    public void J03_ContinueWorkWithAnotherAgent()
    {
        string a = CliHarness.TempDirectory("j03-a");
        string scratch = CliHarness.TempDirectory("j03-scratch");
        string b = string.Empty;
        try
        {
            string op = Init(a);
            string agentOne = Admit(a, op, "Agent One", "agent");
            string agentTwo = Admit(a, op, "Agent Two", "agent");
            string builder = Admit(a, op, "Builder", "agent", "builder");
            string orderBody = CliHarness.WriteJson(scratch, "order.json", """{"key":"W-1","kind":"ordinary","title":"Build the widget","intent":"Prove that work transfers between contributors through records alone.","scope":["widget sources and tests"],"exitQuestion":"Does the widget build and pass its tests?","inputs":[],"deliverables":["widget.txt"]}""");
            CliHarness.Ok("--estate", a, "--json", "records", "new", "work-order", "--by", builder, "--body", orderBody);

            string briefPath = CliHarness.WriteJson(scratch, "brief.json", Brief);
            string assignmentOne = CliHarness.Ok("--estate", a, "--json", "work", "assign", "W-1", "--contributor", agentOne, "--by", op, "--brief", briefPath).Id;
            string attemptOne = CliHarness.Ok("--estate", a, "--json", "work", "attempt", assignmentOne, "--by", agentOne, "--workspace", "a clone of example at main", "--note", "Implement the widget.").Id;
            string partialPath = CliHarness.WriteJson(scratch, "partial.json", PartialResult);
            CliHarness.Ok("--estate", a, "--json", "work", "result", attemptOne, "--by", agentOne, "--body", partialPath);

            // The brief carries everything the next contributor needs, including the unresolved effect and the next permitted step.
            string brief = CliHarness.Ok("--estate", a, "work", "brief", "W-1").Out;
            Assert.Contains("A widget that builds and passes its tests.", brief, StringComparison.Ordinal);
            Assert.Contains("partial, awaiting review", brief, StringComparison.Ordinal);
            Assert.Contains("unresolved effect: A publication to the example feed may have been dispatched", brief, StringComparison.Ordinal);
            Assert.Contains("next step: Reconcile the possible dispatch", brief, StringComparison.Ordinal);
            Assert.DoesNotContain(a, brief, StringComparison.Ordinal);

            // The operator assigns the second agent; the second agent works in a fresh clone.
            string assignmentTwo = CliHarness.Ok("--estate", a, "--json", "work", "assign", "W-1", "--contributor", agentTwo, "--by", op, "--brief", briefPath).Id;
            b = CliHarness.Clone(a, "j03-b");
            Assert.Contains("next step: Reconcile the possible dispatch", CliHarness.Ok("--estate", b, "work", "brief", "W-1").Out, StringComparison.Ordinal);
            string attemptTwo = CliHarness.Ok("--estate", b, "--json", "work", "attempt", assignmentTwo, "--by", agentTwo, "--workspace", "a fresh clone of example at main", "--note", "Reconcile the possible dispatch, then finish the widget.").Id;
            string widget = Path.Combine(b, "docs", "widget.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(widget)!);
            File.WriteAllText(widget, "widget\n");
            string hash = ContentHash.Sha256File(widget);
            string completedPath = CliHarness.WriteJson(scratch, "completed.json", $$"""{"outcome":"completed","summary":"Reconciled the dispatch (nothing had been published) and finished the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7,"location":"docs/widget.txt"}],"evidence":[{"summary":"Feed lookup showed no publication under the original operation identity."}],"checks":[{"name":"build","outcome":"passed","summary":"Built cleanly."},{"name":"tests","outcome":"passed","summary":"All tests pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""");
            CliHarness.Outcome resultTwo = CliHarness.Ok("--estate", b, "--json", "work", "result", attemptTwo, "--by", agentTwo, "--body", completedPath);

            // The operator accepts that exact result by hash.
            string acceptPath = CliHarness.WriteJson(scratch, "accept.json", """{"statement":"Accepted the exact result after inspecting the output hash and the reconciliation evidence.","basis":[],"self":false}""");
            CliHarness.Ok("--estate", b, "--json", "records", "new", "acceptance", "--by", op, "--subject", $"result:{resultTwo.Id}:{resultTwo.Hash}", "--body", acceptPath);
            JsonElement statusB = CliHarness.Ok("--estate", b, "--json", "status").Json;
            Dictionary<string, (string Outcome, string State)> assignments = statusB.GetProperty("assignments").EnumerateArray().ToDictionary(x => x.GetProperty("contributor").GetString()!, x => (x.GetProperty("outcome").GetString()!, x.GetProperty("state").GetString()!));
            Assert.Equal(("completed", "accepted"), assignments[agentTwo]);
            Assert.Equal(("partial", "awaiting review"), assignments[agentOne]);

            // Merging the clone back is a pure file addition, and both repositories then render the same status.
            Assert.True(CliHarness.MergeRecords(b, a) >= 3);
            Directory.CreateDirectory(Path.Combine(a, "docs"));
            File.Copy(widget, Path.Combine(a, "docs", "widget.txt"));
            CliHarness.Outcome check = CliHarness.Run("--estate", a, "estate", "check");
            Assert.True(check.Code == 0, check.Out);
            Assert.Equal(CliHarness.Ok("--estate", b, "--json", "status").Out, CliHarness.Ok("--estate", a, "--json", "status").Out);
            Assert.Contains("partial, awaiting review", CliHarness.Ok("--estate", a, "work", "observe", "W-1").Out, StringComparison.Ordinal);
            Assert.Equal(["docs", "estate", "records"], Directory.EnumerateDirectories(a).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            CliHarness.Delete(a, b, scratch);
        }
    }

    [Fact]
    public void J01_CreateAndDeliverALocalSoftwareProductHeadless()
    {
        string root = CliHarness.TempDirectory("j01");
        string scratch = CliHarness.TempDirectory("j01-scratch");
        try
        {
            string op = Init(root);
            string builder = Admit(root, op, "Builder", "agent", "builder");
            string decisionPath = CliHarness.WriteJson(scratch, "decision.json", $$"""{"title":"Standing delegation","stake":"low","notAuthorized":[],"statement":"Build and accept ordinary work yourself.","summary":"The builder engineers and self-accepts ordinary work.","synthesis":"Standing delegation for ordinary work; milestones and releases stay with the operator.","effects":["builder self-accepts ordinary work"],"affects":[],"decidedBy":"{{op}}","decidedAt":"2026-10-13T12:00:00Z"}""");
            string decision = CliHarness.Ok("--estate", root, "--json", "records", "new", "decision", "--by", builder, "--body", decisionPath).Id;
            string authorityPath = CliHarness.WriteJson(scratch, "authority.json", $$"""{"grantee":"{{builder}}","scope":["local-engineering","self-accept-ordinary-work"],"reserved":["publication","spending"],"conditions":["self-review is labeled"],"basis":"{{decision}}"}""");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "authority", "--by", op, "--body", authorityPath);

            string orderPath = CliHarness.WriteJson(scratch, "order.json", """{"key":"W-2","kind":"ordinary","title":"Deliver the widget","intent":"Produce an identified, checked, accepted widget without any vendor account.","scope":["widget"],"exitQuestion":"Is the exact widget built, checked and accepted?","inputs":[],"deliverables":["widget.txt"]}""");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "work-order", "--by", builder, "--body", orderPath);
            string briefPath = CliHarness.WriteJson(scratch, "brief.json", Brief);
            string assignment = CliHarness.Ok("--estate", root, "--json", "work", "assign", "W-2", "--contributor", builder, "--by", builder, "--brief", briefPath).Id;
            string attempt = CliHarness.Ok("--estate", root, "--json", "work", "attempt", assignment, "--by", builder, "--workspace", "example repository, main branch", "--note", "Build, test, record.").Id;
            string widget = Path.Combine(root, "docs", "widget.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(widget)!);
            File.WriteAllText(widget, "widget\n");
            string hash = ContentHash.Sha256File(widget);
            string resultPath = CliHarness.WriteJson(scratch, "result.json", $$"""{"outcome":"completed","summary":"Built and tested the widget.","outputs":[{"name":"widget.txt","hash":"{{hash}}","bytes":7,"location":"docs/widget.txt"}],"evidence":[],"checks":[{"name":"build","outcome":"passed","summary":"Built cleanly."},{"name":"tests","outcome":"passed","summary":"All tests pass."}],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""");
            CliHarness.Outcome result = CliHarness.Ok("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", resultPath);

            // A wrong hash is refused: the output named must be the file present.
            string wrongPath = CliHarness.WriteJson(scratch, "wrong.json", $$"""{"outcome":"completed","summary":"Built.","outputs":[{"name":"widget.txt","hash":"sha256:{{new string('0', 64)}}","location":"docs/widget.txt"}],"evidence":[],"checks":[],"limitations":[],"unresolvedEffects":[],"nextStep":"none"}""");
            Assert.NotEqual(0, CliHarness.Run("--estate", root, "--json", "work", "result", attempt, "--by", builder, "--body", wrongPath).Code);

            string acceptResultPath = CliHarness.WriteJson(scratch, "accept-result.json", """{"statement":"Self-accepted the exact result under standing delegation.","basis":[],"self":true}""");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", builder, "--subject", $"result:{result.Id}:{result.Hash}", "--body", acceptResultPath);
            JsonElement order = CliHarness.Ok("--estate", root, "--json", "work", "brief", "W-2").Json.GetProperty("workOrder");
            string acceptOrderPath = CliHarness.WriteJson(scratch, "accept-order.json", """{"statement":"Self-accepted: the exit question is answered yes by the accepted result.","basis":[],"self":true}""");
            CliHarness.Ok("--estate", root, "--json", "records", "new", "acceptance", "--by", builder, "--subject", $"work-order:{order.GetProperty("id").GetString()}:{order.GetProperty("hash").GetString()}", "--body", acceptOrderPath);

            JsonElement status = CliHarness.Ok("--estate", root, "--json", "status").Json;
            Assert.Equal("accepted", status.GetProperty("workOrders").EnumerateArray().Single().GetProperty("status").GetString());
            Assert.Equal("accepted", status.GetProperty("assignments").EnumerateArray().Single().GetProperty("state").GetString());
            Assert.Contains("completed, accepted", CliHarness.Ok("--estate", root, "work", "observe", "W-2").Out, StringComparison.Ordinal);
            Assert.Equal(0, CliHarness.Run("--estate", root, "estate", "check").Code);
        }
        finally
        {
            CliHarness.Delete(root, scratch);
        }
    }

    private static string Init(string root)
    {
        CliHarness.Outcome init = CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example", "--purpose", "Journey tests.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
        return init.Json.GetProperty("operator").GetString()!;
    }

    private static string Admit(string root, string op, string name, string kind, string role = "contributor")
    {
        string agent = kind == "agent" ? ""","agent":{"vendor":"Example","product":"Example Agent"}""" : string.Empty;
        string path = Path.Combine(root, "..", $"principal-{name.Replace(' ', '-')}-{Records.Identity.Ulid.NewUlid().Value}.json");
        File.WriteAllText(path, $$"""{"kind":"{{kind}}","name":"{{name}}","roles":["{{role}}"]{{agent}}}""");
        try
        {
            return CliHarness.Ok("--estate", root, "--json", "records", "new", "principal", "--by", op, "--body", path).Id;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
