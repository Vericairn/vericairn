// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Cli;

internal sealed class ContextReadCommand : Command
{
    public ContextReadCommand(CommandContext context)
        : base("read", "Read and verify a pinned original, never silently its latest revision.")
    {
        var manifest = new Argument<string>("pin") { Description = "Self-contained pinned identity from the package." };
        var source = new Option<string?>("--source") { Description = "Legacy manifest handles are unavailable; prepare context again for a pin." };
        var original = new Option<bool>("--original") { Required = true, Description = "Return only the pinned original." };
        var output = new Option<string?>("--out") { Description = "Write exact bytes to a new file; never overwrite." };
        Arguments.Add(manifest);
        Options.Add(source);
        Options.Add(original);
        Options.Add(output);
        SetAction(result => context.WithApi(result.GetValue(context.EstateOption)!, api =>
        {
            if (!result.GetValue(original))
            {
                return context.Fail("Context retrieval requires --original.");
            }

            string pin = result.GetValue(manifest)!;
            string? legacySource = result.GetValue(source);
            ContextReadResponse response = legacySource is null ? api.ContextRead(pin) : api.ContextRead(pin, legacySource);
            return Write(context, response, result.GetValue(output), result.GetValue(context.JsonOption));
        }));
    }

    private static int Write(CommandContext context, ContextReadResponse response, string? output, bool json)
    {
        if (response.Problem is not null)
        {
            context.Output.Write(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : response.Problem.Message + "\n");
            return CommandContext.Failed;
        }

        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(response.Content!);
        }

        if (json)
        {
            context.Output.Write(JsonSerializer.Serialize(response, ProtocolJson.Options));
        }
        else if (output is null)
        {
            using Stream stream = Console.OpenStandardOutput();
            stream.Write(response.Content!);
        }

        return CommandContext.Clean;
    }
}
