// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal static class ReleaseOutput
{
    /// <summary>Prints a release operation's outcome exactly as the runtime reported it: a problem fails, a rejected record lists its findings, and the exit code says whether the request advanced.</summary>
    public static int Write(CommandContext context, ReleaseResponse response, bool json)
    {
        if (response.Problem is not null) context.Error.WriteLine("error: " + response.Message);
        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else
        {
            if (response.Check is { } check)
            {
                context.Output.WriteLine($"Checked commit: {check.Commit ?? "unavailable"}; product hash: {check.Expected ?? "unavailable"}");
                context.Output.WriteLine($"Client build: {check.ClientBuild ?? "unavailable"}; runtime build: {check.RuntimeBuild}");
                context.Output.WriteLine($"Retained basis would be {check.BasisBytes} bytes; nothing retained.");
            }
            foreach (RecordWritten? written in new[] { response.Operation, response.Candidate, response.Release })
            {
                if (written is not null)
                {
                    context.Output.WriteLine($"{written.Path}  {written.Id}  {written.Hash}");
                }
            }

            if (response.Problem is null) context.Output.WriteLine(response.Message);
            if (response.Problem is { } problem)
                foreach (FindingDto finding in problem.Findings)
                    context.Output.WriteLine($"{finding.Severity.ToUpperInvariant()} {finding.Code} {finding.Location}: {finding.Message}");
            if (response.Policies is { } policies) context.Output.Write(policies.Text);
        }

        return response.Problem is { Code: "rejected" } ? HasErrors : response.Problem is not null ? Failed : response.Ok ? Clean : HasErrors;
    }
}
