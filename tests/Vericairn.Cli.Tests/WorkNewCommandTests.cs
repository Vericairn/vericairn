// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class WorkNewCommandTests
{
    [Fact]
    public void FlagsAloneCreateASignedWorkOrderAndReturnTheRecordReceipt()
    {
        string root = CliHarness.TempDirectory("work-new");
        try
        {
            string op = Init(root);
            string device = CliHarness.Ok("--estate", root, "--json", "devices", "create", "--principal", op, "--name", "Operator device", "--custody", "file").Id;
            CliHarness.Ok("--estate", root, "devices", "admit", device, "--by", op, "--note", "First authority.");
            CliHarness.Outcome receipt = CliHarness.Ok("--estate", root, "--json", "work", "new", "--by", op,
                "--key", "W-1", "--title", "Build the widget", "--intent", "Make a useful widget.",
                "--scope", "widget", "--exit-question", "Does it work?", "--deliverable", "widget.txt");
            Assert.Equal(["path", "id", "hash"], receipt.Json.EnumerateObject().Select(p => p.Name));
            using JsonDocument stored = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, receipt.Json.GetProperty("path").GetString()!)));
            JsonElement record = stored.RootElement;
            Assert.Equal(op, record.GetProperty("by").GetString());
            Assert.Equal(receipt.Id, record.GetProperty("id").GetString());
            Assert.Equal(receipt.Hash, record.GetProperty("hash").GetString());
            Assert.Equal(device, Assert.Single(record.GetProperty("signatures").EnumerateArray()).GetProperty("device").GetString());
            Assert.Equal(CanonicalJson.SerializePretty(WorkNewCases.Body()), CanonicalJson.SerializePretty(record.GetProperty("body")));
            CliHarness.Ok("--estate", root, "estate", "check");
        }
        finally { CliHarness.Delete(root); }
    }

    [Fact]
    public void RepeatedFlagsAndMilestoneKindSurviveAndTextPrintsTheIdAndHash()
    {
        string root = CliHarness.TempDirectory("work-new-repeated");
        try
        {
            string op = Init(root);
            string first = CliHarness.Ok(["--estate", root, "--json", "work", "new", .. Flags(WorkNewCases.Valid(op))]).Id;
            string second = CliHarness.Ok(["--estate", root, "--json", "work", "new", .. Flags(WorkNewCases.Valid(op) with { Key = "W-2" })]).Id;
            var request = WorkNewCases.Valid(op) with
            {
                Key = "M-1", Kind = "milestone", Scope = ["widget sources", "widget tests"], Deliverables = ["widget.txt", "test results"],
                After = [first, second], Inputs = ["work-order:" + first, "principal:" + op],
            };
            CliHarness.Outcome text = CliHarness.Ok(["--estate", root, "work", "new", .. Flags(request)]);
            string file = Directory.GetFiles(Path.Combine(root, "records", "work-order"), "*.json", SearchOption.AllDirectories)
                .Single(path => { using JsonDocument record = JsonDocument.Parse(File.ReadAllBytes(path)); return record.RootElement.GetProperty("body").GetProperty("key").GetString() == "M-1"; });
            using JsonDocument stored = JsonDocument.Parse(File.ReadAllBytes(file));
            JsonElement envelope = stored.RootElement;
            Assert.Contains(envelope.GetProperty("id").GetString()!, text.Out, StringComparison.Ordinal);
            Assert.Contains(envelope.GetProperty("hash").GetString()!, text.Out, StringComparison.Ordinal);
            JsonElement body = envelope.GetProperty("body");
            Assert.Equal("milestone", body.GetProperty("kind").GetString());
            Assert.Equal(request.Scope, body.GetProperty("scope").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(request.Deliverables, body.GetProperty("deliverables").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal(request.After, body.GetProperty("after").EnumerateArray().Select(v => v.GetString()));
            Assert.Equal([("work-order", first), ("principal", op)], body.GetProperty("inputs").EnumerateArray()
                .Select(v => (v.GetProperty("type").GetString(), v.GetProperty("id").GetString())));
            CliHarness.Ok("--estate", root, "estate", "check");
        }
        finally { CliHarness.Delete(root); }
    }

    [Theory]
    [MemberData(nameof(WorkNewCases.Refusals), MemberType = typeof(WorkNewCases))]
    public void RefusalsMatchRecordsNewIncludingEveryMessageAndWriteNothing(string fault, string finding)
    {
        string root = CliHarness.TempDirectory("work-new-refusal");
        string scratch = CliHarness.TempDirectory("work-new-body");
        try
        {
            string op = Init(root);
            (WorkNewRequest request, JsonElement body) = WorkNewCases.Invalid(fault, op);
            string path = CliHarness.WriteJson(scratch, "order.json", body.GetRawText());
            string[] before = Directory.GetFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories);
            CliHarness.Outcome flags = CliHarness.Run(["--estate", root, "--json", "work", "new", .. Flags(request)]);
            CliHarness.Outcome generic = CliHarness.Run("--estate", root, "--json", "records", "new", "work-order", "--by", op, "--body", path);
            Assert.Equal(1, flags.Code);
            Assert.Equal(generic.Code, flags.Code);
            Assert.Equal(generic.Err, flags.Err);
            Assert.Equal(generic.Json.GetProperty("summary").GetString(), flags.Json.GetProperty("summary").GetString());
            Assert.Equal(generic.Json.GetProperty("errors").GetInt32(), flags.Json.GetProperty("errors").GetInt32());
            Assert.Equal(generic.Json.GetProperty("warnings").GetInt32(), flags.Json.GetProperty("warnings").GetInt32());
            Assert.Equal(Findings(generic.Json), Findings(flags.Json));
            Assert.Contains(flags.Json.GetProperty("findings").EnumerateArray(), f => f.GetProperty("Code").GetString() == finding);
            Assert.Equal(before, Directory.GetFiles(Path.Combine(root, "records"), "*.json", SearchOption.AllDirectories));
        }
        finally { CliHarness.Delete(root, scratch); }
    }

    private static string Init(string root) => CliHarness.Ok("--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
        "--purpose", "Work orders from flags.", "--operator-name", "Operator", "--organization", "Example", "--commit", "0123456789abcdef0123456789abcdef01234567")
        .Json.GetProperty("operator").GetString()!;

    private static string[] Flags(WorkNewRequest request)
    {
        var flags = new List<string> { "--by", request.By };
        foreach ((string name, string? value) in new[] { ("--key", request.Key), ("--title", request.Title), ("--intent", request.Intent), ("--exit-question", request.ExitQuestion), ("--kind", request.Kind) })
            if (value is not null) flags.AddRange([name, value]);
        foreach ((string name, IReadOnlyList<string>? values) in new[] { ("--scope", request.Scope), ("--deliverable", request.Deliverables), ("--after", request.After), ("--input", request.Inputs) })
            foreach (string value in values ?? []) flags.AddRange([name, value]);
        return flags.ToArray();
    }

    private static IEnumerable<(string Severity, string Code, string Field, string Message)> Findings(JsonElement response) => response.GetProperty("findings").EnumerateArray().Select(f =>
    {
        string location = f.GetProperty("Location").GetString()!;
        return (f.GetProperty("severity").GetString()!, f.GetProperty("Code").GetString()!,
            location.Contains('#', StringComparison.Ordinal) ? location[location.IndexOf('#', StringComparison.Ordinal)..] : location, f.GetProperty("Message").GetString()!);
    });
}
