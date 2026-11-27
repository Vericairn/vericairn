// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Providers;

/// <summary>
/// From 0.9.4 (WATCH-06): one progress target as its adapter is given it: the target's name, the settings its configuration gives
/// the adapter (for GitHub Discussions, the repository and the category), the title of the place, the marker the runtime ends every
/// text with, by which the adapter finds the place it keeps, and the place the runtime last knew, if any.
/// </summary>
/// <param name="Name">The target's name in its configuration.</param>
/// <param name="Settings">The adapter's settings, by name, as its descriptor declares them.</param>
/// <param name="Title">The place's title.</param>
/// <param name="Marker">The text that marks the place as this target's, carried in its text as an HTML comment.</param>
/// <param name="Known">The place's identity as the runtime last recorded it on this machine; null when it knows none.</param>
public sealed record ProgressTargetSpec(string Name, IReadOnlyDictionary<string, string> Settings, string Title, string Marker, string? Known = null);

/// <summary>From 0.9.4 (WATCH-06): where a progress target lives: its identity at the adapter's service, its address, and its current text.</summary>
/// <param name="Id">The place's identity at the service, such as a discussion's node identity.</param>
/// <param name="Url">Where a person reads it.</param>
/// <param name="Body">Its text as the service holds it now; null when the adapter did not read it.</param>
public sealed record ProgressTargetPlace(string Id, string Url, string? Body = null);

/// <summary>
/// From 0.9.4 (WATCH-06): the destination a progress target names, as its adapter observed it: whether only the destination's own
/// people can read it, and the place this target keeps there, or null when it keeps none yet.
/// </summary>
/// <param name="Private">True only when the service reports the destination readable by its own members alone; false when anyone may read it, or the adapter cannot tell.</param>
/// <param name="Place">The target's place, found by its known identity or by its marker; null when there is none.</param>
public sealed record ProgressTargetDestination(bool Private, ProgressTargetPlace? Place);

/// <summary>
/// From 0.9.4 (WATCH-06): progress targets, contract version 1: where the estate's progress report is placed, chosen by a target's
/// configuration as products choose their other providers, by id, with nothing ever selecting another (decision
/// 01M7827BCHFVDY212W7SSBRB12). An adapter decides only where and how a text is placed: it creates the target's place once, replaces
/// its text, adds a comment and reports where it lives. What the text says, and what a public target may carry, stay the runtime's.
/// An adapter never reads or stores a credential, never passes the service's own text on except a place's current text, and reports
/// a refusal before any effect with <see cref="ProviderRefusedException"/> and an effect it cannot confirm with
/// <see cref="ProviderUncertainException"/>. The runtime locates the place again to reconcile an uncertain creation. The contract
/// has no comment receipt lookup, so the follower keeps an uncertain comment for a person to check.
/// </summary>
public interface IProgressTargetAdapter
{
    /// <summary>The version implemented by every adapter.</summary>
    const string Contract = "vericairn.adapter.progress-target/1";

    /// <summary>The stable adapter id a target's configuration names, for example github-discussions.</summary>
    string Adapter { get; }

    /// <summary>The adapter's capabilities, limitations and settings.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Observes the destination and finds the target's place: the known one when the spec names it, which must still be in the
    /// destination the settings name, else the one whose text carries the marker. Reads only; throws <see cref="ProviderRefusedException"/>
    /// when the destination cannot be observed completely.
    /// </summary>
    ProgressTargetDestination Locate(ProgressTargetSpec target);

    /// <summary>Creates the target's place with its first text. Called only when <see cref="Locate"/> found none.</summary>
    ProgressTargetPlace Create(ProgressTargetSpec target, string body);

    /// <summary>Replaces the place's text with another.</summary>
    ProgressTargetPlace Replace(ProgressTargetSpec target, ProgressTargetPlace place, string body);

    /// <summary>Adds a comment to the place, which notifies its readers; returns where the comment lives.</summary>
    string Comment(ProgressTargetSpec target, ProgressTargetPlace place, string body);
}
