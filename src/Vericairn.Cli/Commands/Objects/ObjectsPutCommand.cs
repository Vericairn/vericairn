// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class ObjectsPutCommand : Command
{
    public ObjectsPutCommand(CommandContext context)
        : base("put", "Store a file's bytes as an object named by their hash.")
    {
        var fileArgument = new Argument<string>("file") { Description = "File whose bytes become an object." };
        Arguments.Add(fileArgument);
        SetAction(parseResult => Execute(context, parseResult.GetValue(context.EstateOption)!, parseResult.GetValue(fileArgument)!, parseResult.GetValue(context.JsonOption)));
    }

    private static int Execute(CommandContext context, string root, string file, bool json)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return context.Fail(exception.Message);
        }

        return context.WithApi(root, api =>
        {
            (ObjectStored? stored, Problem? problem) = api.ObjectPut(bytes);
            if (stored is null)
            {
                return context.Fail(problem?.Message ?? "refused");
            }

            context.Output.WriteLine(json ? JsonSerializer.Serialize(new { hash = stored.Hash, bytes = stored.Bytes, store = stored.Store }) : $"{stored.Hash}  {stored.Bytes} bytes  ({stored.Store})");
            return Clean;
        });
    }
}
