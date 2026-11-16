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
/// <para>
/// From 0.9.4 (CODE-80) an action that signs under a presence session (design/presence-sessions.md § 7) raises its notices with
/// <see cref="Session"/>: <c>announced</c> carries the session's terms before its check, and a renewal is <c>renewed</c>, with new terms
/// and its reason; <c>session</c> carries the nonce once the check is asked, from which, and from terms it received before it, a client
/// computes the fingerprint the system dialog shows; each signature under the session is <c>signed</c>, with its index and short hash;
/// and <c>ended</c> says why the session ended. A client that predates them prints each notice's text.
/// </para>
/// </summary>
/// <param name="Sequence">The notice's position, increasing within one runtime.</param>
/// <param name="Kind">announced, added, revised, prompt or finished; from 0.9.4 also session, signed, renewed or ended.</param>
/// <param name="Action">The operator action, such as "operator close" or "release submit 0.9.3".</param>
/// <param name="Count">How many signatures the action needs, as announced so far.</param>
/// <param name="Made">How many signatures it had made when the notice was raised.</param>
/// <param name="Signatures">For announced, every record; for added, the records added; for revised, the records that now remain; for a prompt, the one it signs; for finished, none.</param>
/// <param name="Text">The notice as a person reads it, one line or more, ready to print.</param>
/// <param name="Index">For a prompt, which signature it is, from 1; from 0.9.4 also for a signature under a session.</param>
public sealed record PresenceNotice(long Sequence, string Kind, string Action, int Count, int Made, IReadOnlyList<PresenceSignature> Signatures, string Text, int? Index = null)
{
    /// <summary>
    /// From 0.9.4 (CODE-80): the presence session the notice is about, for <c>announced</c> and <c>renewed</c> of a session's list,
    /// <c>session</c>, <c>signed</c> and <c>ended</c>; null for every other notice and for an action that asks one check per signature.
    /// </summary>
    public PresenceSessionNotice? Session { get; init; }

    /// <summary>
    /// From 0.9.4 (CODE-80): for a signature, the first twelve hexadecimal digits of the signed record's content hash: for a <c>signed</c>
    /// notice and a <c>prompt</c> of a record. For a record written after the check it is an audit trail, not something to compare before
    /// approving.
    /// </summary>
    public string? ShortHash { get; init; }
}

/// <summary>
/// From 0.9.4 (CODE-80): what a presence notice says of its session (design/presence-sessions.md §§ 6 and 7). The terms are the exact
/// compact canonical text the runtime hands the release helper, which hold types, hashes, counts and fixed values and never a record's
/// bytes; a client derives the counts, each kind entry's summary, each exact entry's type and short hash, and the fingerprint from them
/// itself. The descriptions are the runtime's own words, which the terms do not cover, and a client shows them as the runtime's.
/// </summary>
/// <param name="Id">The session's id, a ULID, as its terms state it: the notices of one session share it.</param>
/// <param name="LimitSeconds">The session's limit: the shorter of the device owner's and 300 seconds, never under 60.</param>
/// <param name="Terms">For announced and renewed, the terms' compact canonical text, before the nonce; null on every later notice.</param>
/// <param name="Descriptions">For announced and renewed, the runtime's description of each entry, in the terms' order.</param>
/// <param name="Nonce">For session, the helper's nonce, 16 bytes in base64url without padding, published once the check is asked.</param>
/// <param name="SecondsLeft">For signed, the seconds the session had left, as the helper counted them.</param>
/// <param name="Reason">
/// For renewed, why the session is renewed, in words; for ended, why it ended: closed, limit, lock, sleep, refusal, parent, input,
/// declined or silent.
/// </param>
public sealed record PresenceSessionNotice(string Id, int LimitSeconds, string? Terms = null, IReadOnlyList<PresenceEntryDescription>? Descriptions = null,
    string? Nonce = null, int? SecondsLeft = null, string? Reason = null);

/// <summary>
/// From 0.9.4 (CODE-80): the runtime's description of one entry of a session's list: what the record is about, what signing it
/// authorizes and what it changes, and whether it is a review by the agent harness that authored the reviewed work. The terms cover none
/// of it: a client shows it labeled as the runtime's description.
/// </summary>
/// <param name="Entry">The entry's index in the terms, from 0.</param>
/// <param name="Type">The record type, as the terms state it.</param>
/// <param name="Key">What the record is about, as a person knows it.</param>
/// <param name="Purpose">What signing it authorizes, in a sentence without a full stop.</param>
/// <param name="Changes">One line on what the record changes.</param>
/// <param name="Flagged">Whether the record is, or rests on, a review by the same agent harness that authored the reviewed work.</param>
public sealed record PresenceEntryDescription(int Entry, string Type, string Key, string Purpose, string? Changes = null, bool Flagged = false);

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

    /// <summary>
    /// Every kind of notice: FIX-124's, in the order an action raises them, then from 0.9.4 (CODE-80) a presence session's: the check
    /// asked with its nonce, a signature under the session, a renewal with its new terms, and the session's end.
    /// </summary>
    public static ImmutableArray<string> Kinds { get; } = ["announced", "added", "revised", "prompt", "finished", "session", "signed", "renewed", "ended"];

    /// <summary>From 0.9.4 (CODE-80): the reasons an <c>ended</c> notice may give.</summary>
    public static ImmutableArray<string> EndReasons { get; } = ["closed", "limit", "lock", "sleep", "refusal", "parent", "input", "declined", "silent"];
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
