// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Protocol;

/// <summary>
/// From 0.9.3 (FIX-124): one record a presence prompt signs, as an operator action announces it before its first prompt and names it
/// at the prompt: its record type, the key a person knows it by (a work-order key, a version, a branch, a record id) and what signing
/// it authorizes, in a sentence.
/// </summary>
/// <param name="Type">The record type, such as acceptance, operation or release.</param>
/// <param name="Key">What the record is about, as a person knows it.</param>
/// <param name="Purpose">What signing it authorizes, in a sentence without a full stop.</param>
public sealed record PresenceSignature(string Type, string Key, string Purpose)
{
    /// <summary>The signature as one line names it: its type and key, then what it authorizes.</summary>
    public string Describe() => $"{Type} {Key}: {Purpose}";
}

/// <summary>
/// From 0.9.3 (FIX-124): one notice of the presence prompts an operator action asks for on the runtime's machine, in order. Every
/// signature made there with a key that needs its owner's presence is counted against the action that asks for it, so a client can
/// show which signature of how many each prompt is and what it signs:
/// <c>announced</c> before the first prompt, listing every record the action will sign; <c>added</c> when it must sign more than it
/// announced, before the first of them; <c>revised</c> when its path changed before the signatures it had not yet asked for, before the
/// next of them; <c>prompt</c> as each prompt is shown; and <c>finished</c> when the action ends, with how many
/// it signed. A notice is a hint for the person at that machine and never authority; the records are what was signed.
/// </summary>
/// <param name="Sequence">The notice's position, increasing within one runtime.</param>
/// <param name="Kind">announced, added, revised, prompt or finished.</param>
/// <param name="Action">The operator action, such as "operator close" or "release submit 0.9.3".</param>
/// <param name="Count">How many signatures the action needs, as announced so far.</param>
/// <param name="Made">How many signatures it had made when the notice was raised.</param>
/// <param name="Signatures">For announced, every record; for added, the records added; for revised, the records that now remain; for a prompt, the one it signs; for finished, none.</param>
/// <param name="Text">The notice as a person reads it, one line or more, ready to print.</param>
/// <param name="Index">For a prompt, which signature it is, from 1.</param>
public sealed record PresenceNotice(long Sequence, string Kind, string Action, int Count, int Made, IReadOnlyList<PresenceSignature> Signatures, string Text, int? Index = null);

/// <summary>From 0.9.3 (FIX-124): a read of the presence notices after a position, waiting for one up to the seconds given.</summary>
/// <param name="After">The position the reader holds; the runtime's newest when omitted, so the read delivers only what comes next.</param>
/// <param name="Wait">Seconds to wait for a notice, 0 to 25; 0 by default.</param>
public sealed record PresencePromptsRequest(long? After = null, int? Wait = null);

/// <summary>
/// From 0.9.3 (FIX-124): the presence notices after the position given, oldest first, and the position to continue from. A position
/// newer than the runtime's (a runtime started again) delivers every notice it keeps. A problem means nothing was read.
/// </summary>
/// <param name="Notices">The notices delivered.</param>
/// <param name="Cursor">The position to continue from.</param>
/// <param name="Problem">Why nothing was read.</param>
public sealed record PresencePromptsResponse(IReadOnlyList<PresenceNotice> Notices, long Cursor, Problem? Problem = null);

/// <summary>From 0.9.3 (FIX-124): the vocabulary of presence notices.</summary>
public static class PresenceNoticeVocabulary
{
    /// <summary>The longest a read waits, in seconds.</summary>
    public const int MaxWaitSeconds = 25;

    /// <summary>How many notices a runtime keeps for its readers; older ones are dropped.</summary>
    public const int Kept = 256;

    /// <summary>Every kind of notice, in the order an action raises them.</summary>
    public static ImmutableArray<string> Kinds { get; } = ["announced", "added", "revised", "prompt", "finished"];
}

public partial interface IEstateApi
{
    /// <summary>
    /// From 0.9.3 (FIX-124): the presence notices of operator actions on the runtime's machine after a position, waiting up to the seconds
    /// given for one. Only the local socket reads them; a device-signed request is refused. A runtime that predates them has none.
    /// </summary>
    PresencePromptsResponse PresencePrompts(PresencePromptsRequest request) =>
        new([], request?.After ?? 0, new Problem("unavailable", "This runtime keeps no presence notices.", []));

    /// <summary>The cancellable read bridge of the presence notices.</summary>
    Task<PresencePromptsResponse> PresencePromptsAsync(PresencePromptsRequest request, CancellationToken cancellationToken = default) =>
        ClientOperation.RunSynchronousAsync(nameof(PresencePrompts), () => PresencePrompts(request), false, this as IClientOperationLifetime, cancellationToken);
}
