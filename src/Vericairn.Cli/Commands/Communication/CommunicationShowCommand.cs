// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class CommunicationShowCommand : Command
{
    public CommunicationShowCommand(CommandContext context)
        : base("show", "Show effective values, contributing revisions, conflicts and freshness.")
    {
        var operations = new CommunicationOperations(context);
        var preview = new Option<bool>("--preview") { Description = "Show the shared status, decision, result and handoff examples." };
        Options.Add(preview);
        SetAction(p => operations.CommunicationShow(p.GetValue(context.EstateOption)!, context.Presentation.Read(p), p.GetValue(preview), p.GetValue(context.JsonOption)));
    }
}
