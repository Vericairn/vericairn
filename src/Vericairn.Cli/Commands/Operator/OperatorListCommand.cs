// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;

namespace Vericairn.Cli;

internal sealed class OperatorListCommand : Command
{
    public OperatorListCommand(CommandContext context)
        : base("list", "List every open operator ask, pending operator acceptance and recorded waiting reserved action, with why, what it waits on, exact commands and required JSON input. Reads record and sign nothing. Communication settings change presentation, never the queue; --json returns the complete record-derived view.")
    {
        SetAction(parseResult => context.WithApi(parseResult.GetValue(context.EstateOption)!, api =>
        {
            CommunicationSelection selection = context.Presentation.Read(parseResult);
            OperatorActionsResponse response = api.OperatorActions(selection.Recipient, selection.Assignment, selection.Values("status"));
            context.Output.Write(parseResult.GetValue(context.JsonOption) ? CanonicalJson.SerializePretty(response.Json) : response.Text);
            return response.HasErrors ? CommandContext.HasErrors : CommandContext.Clean;
        }));
    }
}
