// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    // The derivation a command's calls share while it runs, and how many command scopes are open; both under the gate (FIX-152).
    private CommandDerivation? command;
    private int commands;

    /// <summary>
    /// From 0.9.4 (FIX-152; design/performance.md § 3.5): opens the scope of one command, within which human status's calls (status,
    /// communication, attention) and progress share one derivation of the repository checks, the policy sections and the progress
    /// projection for one judged model. The derivation reads the working tree (the repository walk, the writing scan) and the clock, which
    /// no stamp covers, so it is never kept with the model: it ends with the scope, and a call outside any scope, such as each request a
    /// daemon serves, derives its own. Disposing the result ends the scope.
    /// </summary>
    internal IDisposable Command()
    {
        lock (gate)
        {
            if (commands++ == 0) command = new CommandDerivation();
        }

        return new CommandScope(this);
    }

    private void EndCommand()
    {
        lock (gate)
        {
            if (commands > 0 && --commands == 0) command = null;
        }
    }

    // The open command's derivation for this model: a model judged afresh within the command starts it again. Caller holds the gate.
    private CommandDerivation? DerivationOf(EstateModel model)
    {
        if (command is null) return null;
        if (!ReferenceEquals(command.Model, model)) command = new CommandDerivation { Model = model };
        return command;
    }

    /// <summary>The repository checks of a judged model, run once in an open command and copied to each caller, which may add to them.</summary>
    private FindingList SharedRepositoryChecks(EstateModel model)
    {
        CommandDerivation? derivation = DerivationOf(model);
        if (derivation is null) return RepositoryChecks(model);
        derivation.Checks ??= RepositoryChecks(model);
        var copy = new FindingList();
        foreach (Finding finding in derivation.Checks) copy.Add(finding);
        return copy;
    }

    /// <summary>The policy sections of a judged model, read once in an open command.</summary>
    private PolicySections SharedPolicySections(EstateModel model, Action<string, TimeSpan>? observe = null)
    {
        CommandDerivation? derivation = DerivationOf(model);
        if (derivation is null) return ReadPolicySections(model, observe);
        return derivation.Sections ??= ReadPolicySections(model, observe);
    }

    /// <summary>The progress projection over a judged model's repository checks, derived once in an open command.</summary>
    private ProgressResponse SharedProgress(EstateModel model)
    {
        CommandDerivation? derivation = DerivationOf(model);
        if (derivation is null) return ProgressCore(model, SharedRepositoryChecks(model));
        return derivation.Progress ??= ProgressCore(model, SharedRepositoryChecks(model));
    }

    private sealed class CommandDerivation
    {
        internal EstateModel? Model { get; init; }

        internal FindingList? Checks { get; set; }

        internal PolicySections? Sections { get; set; }

        internal ProgressResponse? Progress { get; set; }
    }

    private sealed class CommandScope(EstateService service) : IDisposable
    {
        private EstateService? owner = service;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.EndCommand();
    }
}
