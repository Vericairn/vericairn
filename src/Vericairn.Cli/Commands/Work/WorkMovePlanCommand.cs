// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class WorkMovePlanCommand : Command
{
    public WorkMovePlanCommand(CommandContext context) : base("move-plan", "Move every planned member of a release carrier together, checking the complete prepared set before writing.")
    {
        var key = new Argument<string>("carrier");
        var by = new Option<string>("--by") { Required = true, Description = "Acting principal id." };
        var expected = new Option<string>("--expected") { Required = true, Description = "Expected current source carrier hash." };
        var work = new Option<string>("--expected-work") { Required = true, Description = "JSON file mapping every planned member's exact revision id to its expected hash; {} for an empty plan." };
        var to = new Option<string?>("--to") { Description = "Destination carrier key or id; choose this or --version." };
        var target = new Option<string?>("--expected-target") { Description = "Expected destination carrier hash, required with --to." };
        var version = new Option<string?>("--version") { Description = "New version for the source carrier and its planned placements; choose this or --to." };
        Arguments.Add(key);
        Options.Add(by);
        Options.Add(expected);
        Options.Add(work);
        Options.Add(to);
        Options.Add(target);
        Options.Add(version);
        SetAction(p => context.WithBody(p.GetValue(work)!, body =>
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Any(property =>
                property.Value.ValueKind != JsonValueKind.String || !hashes.TryAdd(property.Name, property.Value.GetString()!)))
                return context.Fail("Expected work must be a JSON object with distinct revision ids and string hashes.");
            return context.WithApi(p.GetValue(context.EstateOption)!, api =>
            {
                WorkMovePlanResponse response = api.WorkMovePlan(p.GetValue(key)!, new(p.GetValue(by)!, p.GetValue(expected)!, hashes,
                    p.GetValue(to), p.GetValue(target), p.GetValue(version)));
                if (p.GetValue(context.JsonOption)) context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
                else
                {
                    foreach (RecordWritten record in response.Written) context.Output.WriteLine($"{record.Path}  {record.Id}  {record.Hash}");
                    if (response.Problem is { } problem)
                        foreach (FindingDto finding in problem.Findings) context.Output.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
                    else context.Output.WriteLine($"Planned work moved; {response.Written.Count} revisions recorded.");
                }
                if (response.Problem is { } failed) context.Error.WriteLine("error: " + failed.Message);
                return response.Problem is { Code: "rejected" } ? HasErrors : response.Ok ? Clean : Failed;
            });
        }));
    }
}
