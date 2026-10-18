// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CommunicationDelegateCommand : Command
{
    public CommunicationDelegateCommand(CommandContext context)
        : base("delegate", "Explicitly grant or revoke changes to one's own communication setting; grants no other permission.")
    {
        var operations = new CommunicationOperations(context);
        var owner = new Option<string>("--as") { Description = "The recipient granting consent.", Required = true };
        var grantee = new Option<string>("--to") { Description = "The delegated principal.", Required = true };
        var revoke = new Option<bool>("--revoke") { Description = "Revoke the current delegation." };
        var prior = new Option<string?>("--expected") { Description = "Expected prior delegation hash; required when revising a grant." };
        var expires = new Option<string?>("--expires") { Description = "Expiry as a UTC record timestamp." };
        Options.Add(owner);
        Options.Add(grantee);
        Options.Add(revoke);
        Options.Add(prior);
        Options.Add(expires);
        SetAction(p => operations.CommunicationDelegate(p.GetValue(context.EstateOption)!, p.GetValue(owner)!, p.GetValue(grantee)!, p.GetValue(context.Presentation.Assignment), p.GetValue(revoke), p.GetValue(prior), p.GetValue(expires), p.GetValue(context.JsonOption)));
    }
}
