// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    // The command scope of the calling flow (FIX-152): the command line opens one on its thread before its calls, and every call it makes
    // there, or on a task it starts, reads it; a request a daemon serves has none. It is the flow's, not the runtime's, so ending it waits
    // for nothing (a read stopped at its request limit may still hold the gate), and a call that outlives its command, as such a read
    // does, never reaches the next command's derivation.
    private static readonly AsyncLocal<CommandScope?> CurrentCommand = new();

    /// <summary>
    /// From 0.9.4 (FIX-152; design/performance.md § 3.5): opens the scope of one command, within which human status's calls (status,
    /// communication, attention) and progress share one derivation of the repository checks, the policy sections and the progress
    /// projection for one judged model. The derivation reads the working tree (the repository walk, the writing scan) and the clock, which
    /// no stamp covers, so it is never kept with the model: it ends with the scope, and a call outside any scope, such as each request a
    /// daemon serves, derives its own. Disposing the result ends the scope; a scope opened within this runtime's open scope on the same
    /// flow is that scope.
    /// </summary>
    internal IDisposable Command()
    {
        if (ScopeOf(CurrentCommand.Value) is not null) return NestedCommand.Instance;
        var scope = new CommandScope(this, CurrentCommand.Value);
        CurrentCommand.Value = scope;
        return scope;
    }

    // This runtime's open scope among the calling flow's, if it has one; an ended scope shares nothing, so a call that outlives its
    // command derives its own from then on.
    private CommandScope? ScopeOf(CommandScope? scope)
    {
        while (scope is not null && (scope.Ended || !ReferenceEquals(scope.Owner, this))) scope = scope.Outer;
        return scope;
    }

    // The calling flow's derivation for this model: a model judged afresh within the command starts it again. Caller holds the gate, which
    // guards the derivation and its contents.
    private CommandDerivation? DerivationOf(EstateModel model)
    {
        if (ScopeOf(CurrentCommand.Value) is not { } scope) return null;
        if (!ReferenceEquals(scope.Derivation.Model, model)) scope.Derivation = new CommandDerivation { Model = model };
        return scope.Derivation;
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

    private sealed class CommandScope(EstateService owner, CommandScope? outer) : IDisposable
    {
        private int ended;

        internal bool Ended => Volatile.Read(ref ended) != 0;

        internal EstateService Owner { get; } = owner;

        internal CommandScope? Outer { get; } = outer;

        internal CommandDerivation Derivation { get; set; } = new();

        // Ends the scope on the flow that opened it, restoring the scope it was opened within.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0 && ReferenceEquals(CurrentCommand.Value, this)) CurrentCommand.Value = Outer;
        }
    }

    private sealed class NestedCommand : IDisposable
    {
        internal static readonly NestedCommand Instance = new();

        public void Dispose() { }
    }
}
