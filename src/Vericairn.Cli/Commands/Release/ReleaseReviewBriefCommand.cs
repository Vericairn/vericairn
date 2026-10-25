// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ReleaseReviewBriefCommand : Command
{
    public ReleaseReviewBriefCommand(CommandContext context) : base("review-brief", "Write a candidate's full or focused review brief from its records; optionally assign the exact brief to the Reviewer.")
    {
        var version = new Argument<string>("version") { Description = "Candidate release version." };
        var product = new Option<string?>("--product") { Description = "Product id, required when the version is ambiguous." };
        var decision = new Option<string?>("--decision") { Description = "Current operator decision recording the review scope and exact candidate." };
        var output = new Option<string?>("--out") { Description = "Write the complete brief as UTF-8 text to this file." };
        var assign = new Option<bool>("--assign") { Description = "Record this brief as an assignment to the principal holding the reviewer role; starts no launcher." };
        var by = new Option<string?>("--by") { Description = "Assigning principal; required with --assign." };
        var expected = new Option<string?>("--expected") { Description = "Previously prepared brief hash; with --assign, refuse if the brief changed." };
        Arguments.Add(version);
        Options.Add(product); Options.Add(decision); Options.Add(output); Options.Add(assign); Options.Add(by); Options.Add(expected);
        SetAction(p =>
        {
            bool assigning = p.GetValue(assign);
            if (assigning && string.IsNullOrWhiteSpace(p.GetValue(by))) return context.Fail("--assign requires --by.");
            if (!assigning && (p.GetValue(by) is not null || p.GetValue(expected) is not null)) return context.Fail("--by and --expected require --assign.");
            return context.WithApi(p.GetValue(context.EstateOption)!, api =>
            {
                ReleaseReviewBriefResponse response = api.ReleaseReviewBrief(p.GetValue(version)!, p.GetValue(product), p.GetValue(decision));
                if (response.Ok && p.GetValue(expected) is { } hash && response.Hash != hash)
                    response = response with { Problem = new("conflict", "The prepared review brief changed. Read it again before assigning.", []) };
                // Finish the local export before any estate write, so a failed export cannot conceal an assignment receipt.
                if (response.Ok && p.GetValue(output) is { } path) File.WriteAllText(path, response.Text, new UTF8Encoding(false));
                if (response.Ok && assigning)
                    response = api.ReleaseReviewAssign(p.GetValue(version)!, new(p.GetValue(by)!, response.Hash!, p.GetValue(product), p.GetValue(decision)));
                if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                else if (response.Ok) context.Output.Write(response.Text);
                if (response.Assignment is { } written) context.Error.WriteLine($"Assignment {written.Id} to {response.Reviewer} at {written.Hash}.");
                if (response.Problem is { } problem)
                {
                    context.Error.WriteLine("error: " + problem.Message);
                    foreach (FindingDto finding in problem.Findings)
                        context.Error.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
                }
                return response.Problem is { Code: "rejected" } ? HasErrors : response.Ok ? Clean : Failed;
            }, readOnly: !assigning);
        });
    }
}
