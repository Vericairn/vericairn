// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ContextPrepareCommand : Command
{
    public ContextPrepareCommand(CommandContext context)
        : base("prepare", "Compute the context package for a work order without writing.")
    {
        var work = new Argument<string>("work-order");
        var allocation = new Option<long?>("--max-bytes") { Description = "Remaining context input allocation in bytes, separate from the run spending cap. Mandatory overflow is refused." };
        var search = new Option<string[]>("--search") { Description = "A search query, after the explicit selection: its top three new candidates become optional sources pinned by exact hash, and the manifest records the search. Repeatable, at most five; candidates grant no authority.", AllowMultipleArgumentsPerToken = false };
        Arguments.Add(work);
        Options.Add(allocation);
        Options.Add(search);
        SetAction(result => context.WithApi(result.GetValue(context.EstateOption)!, api =>
        {
            string[] searches = result.GetValue(search) ?? [];
            ContextResponse response = searches.Length > 0
                ? api.Context(new ContextRequest(result.GetValue(work)!, result.GetValue(allocation), searches))
                : api.Context(result.GetValue(work)!, result.GetValue(allocation));
            context.Output.Write(result.GetValue(context.JsonOption) ? JsonSerializer.Serialize(response.Package is null ? (object)response.Problem! : response.Package, ProtocolJson.Options) : response.Text);
            return response.Problem is null ? CommandContext.Clean : CommandContext.Failed;
        }));
    }
}
