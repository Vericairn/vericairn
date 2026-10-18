// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;

namespace Vericairn.Cli;

internal sealed class RecordsShowCommand : Command
{
    public RecordsShowCommand(CommandContext context)
        : base("show", "Read one complete original record by id; presentation depth never changes it.")
    {
        var id = new Argument<string>("id") { Description = "Exact record id." };
        Arguments.Add(id);
        SetAction(result => context.WithApi(result.GetValue(context.EstateOption)!, api =>
        {
            RecordResponse? record = api.Record(result.GetValue(id)!);
            if (record is null) { return context.Fail("No record has id " + result.GetValue(id) + "."); }
            context.Output.Write(CanonicalJson.SerializePretty(record.Envelope));
            return CommandContext.Clean;
        }));
    }
}
