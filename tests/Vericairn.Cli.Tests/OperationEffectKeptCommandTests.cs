// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Service;
using static Vericairn.Records.Tests.OperationEffectKeptTests;

namespace Vericairn.Cli.Tests;

/// <summary>FIX-109 on the surfaces issue #338 used: records new refuses the revision, and records validate and estate check read old ones.</summary>
[Collection("cli")]
public sealed class OperationEffectKeptCommandTests
{
    public OperationEffectKeptCommandTests() => CliHarness.Run("--help");

    [Theory]
    [InlineData("intent")]
    [InlineData("target")]
    public void RecordsNewRefusesARevisionThatChangesTheEffect(string changed)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        using EstateService service = EstateService.Open(estate.Root);
        // `--body -` reads the raw standard input stream, which a test cannot supply, so each body is a file.
        string directory = Directory.CreateTempSubdirectory("vericairn-operation-").FullName;
        try
        {
            string[] Args(string name, JsonObject body)
            {
                string path = Path.Combine(directory, name + ".json");
                File.WriteAllText(path, body.ToJsonString());
                return ["--estate", estate.Root, "--json", "records", "new", "operation", "--by", estate.OperatorId, "--supersedes", dispatched.Id, "--body", path];
            }

            JsonObject revision = Body("uncertain", intent.At);
            if (changed == "intent")
            {
                revision["intent"] = "Publish synthetic media item 0001 and its companion to the example destination.";
            }
            else
            {
                revision["target"]!["destination"] = "https://example.invalid/media/0002";
                revision["target"]!["idempotencyKey"] = "synthetic-key-0002";
            }

            CliHarness.Outcome refused = CliHarness.RunWithApi(service, null, Args("changed", revision));
            Assert.True(refused.Code == 1, $"{refused.Code}: {refused.Out}{refused.Err}");
            Assert.Contains(changed == "intent" ? IntentCode : TargetCode, refused.Out + refused.Err, StringComparison.Ordinal);
            Assert.Equal(2, estate.Load().OfType("operation").Count());

            CliHarness.Outcome accepted = CliHarness.RunWithApi(service, null, Args("kept", Body("uncertain", intent.At)));
            Assert.True(accepted.Code == 0, $"{accepted.Code}: {accepted.Out}{accepted.Err}");
            Assert.Equal(3, estate.Load().OfType("operation").Count());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("check")]
    [InlineData("validate")]
    public void AnExistingRevisionThatChangedTheEffectChecksAsInformation(string command)
    {
        using var estate = new TestEstate(3);
        (RecordEnvelope intent, RecordEnvelope dispatched) = Dispatch(estate);
        JsonObject revision = Body("uncertain", intent.At);
        revision["intent"] = "Publish synthetic media item 0001 and its companion to the example destination.";
        revision["target"]!["destination"] = "https://example.invalid/media/0002";
        estate.Add("operation", estate.OperatorId, revision.ToJsonString(), supersedes: dispatched.Id);
        using EstateService service = EstateService.Open(estate.Root);
        string[] family = command == "check" ? ["estate", "check"] : ["records", "validate"];

        CliHarness.Outcome check = CliHarness.RunWithApi(service, null, ["--estate", estate.Root, "--json", .. family]);

        Assert.True(check.Code == 0, $"{check.Code}: {check.Out}{check.Err}");
        FindingsResponse findings = check.Json.Deserialize<FindingsResponse>(ProtocolJson.Options)!;
        Assert.Equal(0, findings.Errors);
        Assert.Equal("info", Assert.Single(findings.Findings, f => f.Code == IntentCode).Severity);
        Assert.Equal("info", Assert.Single(findings.Findings, f => f.Code == TargetCode).Severity);
    }
}
