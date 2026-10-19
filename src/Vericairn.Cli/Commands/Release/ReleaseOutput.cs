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
        if (response.Problem is not null && !json)
        {
            if (response.Policies is { } policies) context.Output.Write(policies.Text);
            return response.Problem.Findings.Count > 0 ? context.ReportOutcome(WriteOutcome.Fail(response.Problem), json) : context.Fail(response.Message);
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else
        {
            foreach (RecordWritten? written in new[] { response.Operation, response.Candidate, response.Release })
            {
                if (written is not null)
                {
                    context.Output.WriteLine($"{written.Path}  {written.Id}  {written.Hash}");
                }
            }

            context.Output.WriteLine(response.Message);
        }

        return response.Ok ? Clean : HasErrors;
    }
}
