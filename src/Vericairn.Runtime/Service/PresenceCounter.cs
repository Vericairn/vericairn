// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Service;

/// <summary>
/// The presence prompts of one runtime (FIX-124). Every signature made on this machine with a key that needs its owner's presence is
/// counted against the operator action that asks for it: the action states every record it will sign, and before its first prompt the
/// runtime announces them; each prompt is named "Signature k of n" with what it signs, in a notice and in the reason the key's own
/// prompt shows; a signature the action did not announce is announced as an addition before its prompt, so the count stays true; and
/// when the action ends, how many it signed. Notices are kept in memory for the clients on this machine and are never records.
/// </summary>
internal sealed class PresenceCounter
{
    private readonly AsyncLocal<PresenceTally?> current = new();

    /// <summary>The notices clients read.</summary>
    internal PresenceNotices Notices { get; } = new();

    /// <summary>Whether an action's tally is open on this flow.</summary>
    internal bool Counting => current.Value is not null;

    /// <summary>
    /// Opens an operator action's tally with the records it plans to sign with presence, in order; disposing it says how many it signed.
    /// Inside another action's tally it opens nothing: the outer action has counted its records.
    /// </summary>
    internal IDisposable Action(string action, IEnumerable<PresenceSignature>? planned = null) => Action(action, planned, null);

    /// <summary>
    /// From 0.9.4 (ID-16): opens an action's tally as <see cref="Action(string, IEnumerable{PresenceSignature}?)"/> does, with what composes
    /// its announced list for a presence session (design/presence-sessions.md § 5), which the shared signing boundary composes at the
    /// action's first signature that needs presence. Inside another action's tally it opens nothing.
    /// </summary>
    internal IDisposable Action(string action, IEnumerable<PresenceSignature>? planned, PresenceSessionAction? session)
    {
        if (current.Value is not null) return Nothing.Instance;
        var tally = new PresenceTally(action) { Session = session };
        if (planned is not null) tally.Planned.AddRange(planned);
        current.Value = tally;
        return new Scope(this, tally);
    }

    /// <summary>The open action's tally on this flow, or null.</summary>
    internal PresenceTally? Current => current.Value;

    /// <summary>Called when an action's tally closes, before its last notice, so its presence session ends with it.</summary>
    internal Action<PresenceTally>? Closing { get; set; }

    /// <summary>
    /// From 0.9.4 (ID-16): counts a signature made under the action's presence session and names it in a notice: "Signature k of up to n,
    /// under the session's check", with what it signs and the record's short hash. No prompt is shown for it. From 0.9.4 (CODE-80) the
    /// notice is <c>signed</c>, with the session, its seconds left and the short hash as fields.
    /// </summary>
    internal void SessionSigned(PresenceTally tally, PresenceSignature actual, string shortHash, int total, bool upTo, PresenceSessionNotice? session = null)
    {
        lock (tally)
        {
            if (!tally.Announced) tally.Announced = true;
            int index = tally.Asked + 1;
            tally.Asked = index;
            tally.Signed++;
            if (index > tally.Planned.Count) tally.Planned.Add(actual);
            else tally.Planned[index - 1] = actual;
            string count = string.Create(CultureInfo.InvariantCulture, $"Signature {index} of {(upTo ? "up to " : string.Empty)}{Math.Max(total, index)}, under the session's check");
            Notices.Publish("signed", tally.Action, Math.Max(total, tally.Planned.Count), tally.Signed, [actual], $"{count}: {actual.Describe()} ({shortHash})", index, session, shortHash);
        }
    }

    /// <summary>
    /// From 0.9.4 (ID-16): raises a notice of the action's presence session: its list, its check, a renewal or its end. From 0.9.4
    /// (CODE-80) with what the notice says of the session: its terms and the runtime's descriptions, its nonce, or why it ended.
    /// </summary>
    internal PresenceNotice SessionNotice(PresenceTally tally, string kind, int total, IReadOnlyList<PresenceSignature> signatures, string text, PresenceSessionNotice? session = null)
    {
        lock (tally)
        {
            tally.Announced = true;
            return Notices.Publish(kind, tally.Action, total, tally.Signed, signatures, text, null, session);
        }
    }

    /// <summary>
    /// Adds signatures the open action now knows it needs next, before those it still plans: before its announcement they join it;
    /// after, they are announced as an addition, with why, before the first of them is asked for. Without an open action nothing is added.
    /// </summary>
    internal void Expect(IReadOnlyList<PresenceSignature> more, string why)
    {
        if (more.Count == 0 || current.Value is not { } tally) return;
        lock (tally)
        {
            int from = tally.Asked;
            tally.Planned.InsertRange(from, more);
            if (tally.Announced) Notices.Publish("added", tally.Action, tally.Planned.Count, tally.Signed, more, Added(tally, from, from + more.Count, why));
        }
    }

    /// <summary>
    /// Replaces the signatures the open action has not yet asked for, when its path changed before them: after its announcement the
    /// change is announced, with why, before the next prompt. Without an open action nothing changes.
    /// </summary>
    internal void Revise(IReadOnlyList<PresenceSignature> rest, string why)
    {
        if (current.Value is not { } tally) return;
        lock (tally)
        {
            int from = tally.Asked;
            tally.Planned.RemoveRange(from, tally.Planned.Count - from);
            tally.Planned.AddRange(rest);
            if (!tally.Announced) return;
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"{tally.Action} now needs {tally.Planned.Count} {(tally.Planned.Count == 1 ? "signature" : "signatures")} in all, because {why}");
            text.Append(rest.Count == 0 ? "; it asks for no more." : ". The rest are:");
            for (int i = from; i < tally.Planned.Count; i++) text.Append(CultureInfo.InvariantCulture, $"\n  {i + 1}. {tally.Planned[i].Describe()}");
            Notices.Publish("revised", tally.Action, tally.Planned.Count, tally.Signed, rest, text.ToString());
        }
    }

    /// <summary>The purpose that starts a trial signature: one made only to prove the device can still sign, never stored.</summary>
    internal const string TrialPurpose = "a trial signature, never stored, ";

    private static bool IsTrial(PresenceSignature signature) => signature.Purpose.StartsWith(TrialPurpose, StringComparison.Ordinal);

    /// <summary>
    /// Names the prompt for a signature about to be made with presence, announcing the action first when it has not been, and
    /// returns the reason the key's prompt shows. A signature outside any action is an action of its own, which <paramref name="own"/>
    /// returns for <see cref="Signed"/> to close.
    /// </summary>
    internal string Prompt(PresenceSignature actual, out PresenceTally? own) => Prompt(actual, null, out own);

    /// <summary>
    /// Names the prompt as <see cref="Prompt(PresenceSignature, out PresenceTally?)"/> does; from 0.9.4 (CODE-80) its notice carries the
    /// record's short hash, which the helper's own label shows, when the record's bytes are known.
    /// </summary>
    internal string Prompt(PresenceSignature actual, string? shortHash, out PresenceTally? own)
    {
        own = null;
        PresenceTally tally = current.Value ?? (own = new PresenceTally("Writing the " + actual.Type + " record"));
        lock (tally)
        {
            if (!tally.Announced)
            {
                if (tally.Planned.Count == 0) tally.Planned.Add(actual);
                tally.Announced = true;
                Notices.Publish("announced", tally.Action, tally.Planned.Count, tally.Signed, [.. tally.Planned], Announced(tally));
            }

            if (tally.Session is { } session) session.Checks++;
            int index = tally.Asked + 1;
            if (index > tally.Planned.Count)
            {
                tally.Planned.Add(actual);
                Notices.Publish("added", tally.Action, tally.Planned.Count, tally.Signed, [actual],
                    Added(tally, index - 1, index, "it has one more record to sign than it announced"));
            }

            // The prompt keeps the announced wording when it signs the record announced at this place, or a trial signature announced
            // there; otherwise it names what it actually signs, and that takes the announced record's place.
            PresenceSignature planned = tally.Planned[index - 1];
            bool same = string.Equals(planned.Type, actual.Type, StringComparison.Ordinal)
                && (string.Equals(planned.Purpose, actual.Purpose, StringComparison.Ordinal) || IsTrial(planned));
            PresenceSignature named = same ? planned : actual;
            if (!same) tally.Planned[index - 1] = actual;

            tally.Asked = index;
            string count = string.Create(CultureInfo.InvariantCulture, $"Signature {index} of {tally.Planned.Count}");
            Notices.Publish("prompt", tally.Action, tally.Planned.Count, tally.Signed, [named], count + ": " + named.Describe(), index, null, shortHash);
            return PresenceReason.Text($"sign Vericairn {count.ToLowerInvariant()}: {named.Type} {named.Key}, {named.Purpose}");
        }
    }

    /// <summary>Counts a signature made after its prompt; for a signature outside any action, closes its own tally.</summary>
    internal void Signed(PresenceTally? own)
    {
        PresenceTally? tally = own ?? current.Value;
        if (tally is null) return;
        lock (tally) tally.Signed++;
        if (own is not null) Finish(own);
    }

    /// <summary>Closes the tally of a signature outside any action whose prompt was not answered with a signature.</summary>
    internal void Failed(PresenceTally? own)
    {
        if (own is not null) Finish(own);
    }

    private void Finish(PresenceTally tally)
    {
        lock (tally)
        {
            if (!tally.Announced || tally.Finished) return;
            tally.Finished = true;
            int count = tally.Planned.Count;
            // From 0.9.4 (ID-16) an action that used a presence session says how many checks its signatures took.
            if (tally.Session is { SessionChecks: > 0 } session)
            {
                string checks = session.Checks == 1 ? "one presence check" : string.Create(CultureInfo.InvariantCulture, $"{session.Checks} presence checks");
                string made = session.Failed || tally.Asked > tally.Signed
                    ? string.Create(CultureInfo.InvariantCulture, $"Stopped after {tally.Signed} {(tally.Signed == 1 ? "signature" : "signatures")}, made with {checks}.")
                    : string.Create(CultureInfo.InvariantCulture, $"Signed {tally.Signed} of {tally.Signed} with {checks}.");
                Notices.Publish("finished", tally.Action, Math.Max(count, tally.Signed), tally.Signed, [], made);
                return;
            }

            string text = tally.Signed == count
                ? string.Create(CultureInfo.InvariantCulture, $"Signed {count} of {count}.")
                : tally.Asked > tally.Signed
                    ? string.Create(CultureInfo.InvariantCulture, $"Stopped at signature {tally.Asked} of {count}, which was not made; {tally.Signed} {(tally.Signed == 1 ? "was" : "were")} signed.")
                    : string.Create(CultureInfo.InvariantCulture, $"Signed {tally.Signed} of the {count} announced; the action needed no more this time.");
            Notices.Publish("finished", tally.Action, count, tally.Signed, [], text);
        }
    }

    private static string Announced(PresenceTally tally)
    {
        int count = tally.Planned.Count;
        var text = new StringBuilder();
        text.Append(count == 1
            ? $"{tally.Action} asks for your presence once, for the one record it signs:"
            : string.Create(CultureInfo.InvariantCulture, $"{tally.Action} asks for your presence {Times(count)}, once for each record it signs:"));
        for (int i = 0; i < count; i++) text.Append(CultureInfo.InvariantCulture, $"\n  {i + 1}. {tally.Planned[i].Describe()}");
        return text.ToString();
    }

    private static string Added(PresenceTally tally, int from, int to, string why)
    {
        int added = to - from;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{tally.Action} needs {added} more {(added == 1 ? "signature" : "signatures")} than it announced, {tally.Planned.Count} in all, because {why}:");
        for (int i = from; i < to; i++) text.Append(CultureInfo.InvariantCulture, $"\n  {i + 1}. {tally.Planned[i].Describe()}");
        return text.ToString();
    }

    private static string Times(int count) => count == 1 ? "once" : count == 2 ? "twice" : count.ToString(CultureInfo.InvariantCulture) + " times";

    private sealed class Scope(PresenceCounter owner, PresenceTally tally) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(owner.current.Value, tally)) owner.current.Value = null;
            owner.Closing?.Invoke(tally);
            owner.Finish(tally);
        }
    }

    private sealed class Nothing : IDisposable
    {
        internal static readonly Nothing Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>One operator action's presence tally: what it announced, how many prompts it has shown and how many signatures it made.</summary>
internal sealed class PresenceTally(string action)
{
    internal string Action { get; } = action;

    internal List<PresenceSignature> Planned { get; } = [];

    internal int Asked { get; set; }

    internal int Signed { get; set; }

    internal bool Announced { get; set; }

    internal bool Finished { get; set; }

    /// <summary>From 0.9.4 (ID-16): the action's presence session state, when it is one of the action vocabulary's; null otherwise.</summary>
    internal PresenceSessionAction? Session { get; init; }
}

/// <summary>
/// The presence notices of one runtime, kept in memory, newest last, for the clients on its machine to read in order. From 0.9.4
/// (CODE-80) it knows whether a client follows them, a read that waits being under way or having ended a moment ago, and which notices a
/// read has delivered, so a session's terms reach a following client before the check's nonce (design/presence-sessions.md § 6).
/// </summary>
internal sealed class PresenceNotices
{
    /// <summary>How long after a waiting read ends a client still counts as following: the moment it takes to show a batch and read again.</summary>
    internal static readonly TimeSpan FollowGap = TimeSpan.FromSeconds(1);

    private readonly object sync = new();
    private readonly List<PresenceNotice> kept = [];
    private long head;
    private long delivered;
    private int waiting;
    private long followEnded = long.MinValue;
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource read = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The clock the follow gap is measured on; tests replace it.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>How many reads that wait are under way now.</summary>
    internal int Waiting
    {
        get
        {
            lock (sync) return waiting;
        }
    }

    /// <summary>Raises a notice in order and wakes every waiting reader.</summary>
    internal PresenceNotice Publish(string kind, string action, int count, int signed, IReadOnlyList<PresenceSignature> signatures, string text, int? index = null,
        PresenceSessionNotice? session = null, string? shortHash = null)
    {
        PresenceNotice notice;
        TaskCompletionSource signal;
        lock (sync)
        {
            notice = new PresenceNotice(++head, kind, action, count, signed, signatures, text, index) { Session = session, ShortHash = shortHash };
            kept.Add(notice);
            if (kept.Count > PresenceNoticeVocabulary.Kept) kept.RemoveRange(0, kept.Count - PresenceNoticeVocabulary.Kept);
            signal = changed;
            changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
        return notice;
    }

    /// <summary>
    /// From 0.9.4 (CODE-80): when a client follows the notices, waits until a read has delivered the notice at this position, or the bound
    /// passes; true when it was delivered. With no client following it returns false at once: nobody would show the notice sooner.
    /// </summary>
    internal bool AwaitDelivery(long sequence, TimeSpan bound)
    {
        long until = Clock.GetTimestamp();
        while (true)
        {
            Task signal;
            lock (sync)
            {
                if (delivered >= sequence) return true;
                bool following = waiting > 0 || followEnded != long.MinValue && Clock.GetElapsedTime(followEnded) < FollowGap;
                if (!following || Clock.GetElapsedTime(until) >= bound) return false;
                signal = read.Task;
            }

            TimeSpan left = bound - Clock.GetElapsedTime(until);
            if (left <= TimeSpan.Zero) return false;
            try
            {
                signal.Wait(left < FollowGap ? left : FollowGap);
            }
            catch (AggregateException)
            {
                return false;
            }
        }
    }

    /// <summary>The notices after a position, waiting up to the seconds asked for one; a position newer than this runtime's reads every notice kept.</summary>
    internal async Task<PresencePromptsResponse> ReadAsync(PresencePromptsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        int wait = request.Wait ?? 0;
        if (wait is < 0 or > PresenceNoticeVocabulary.MaxWaitSeconds)
            return new([], request.After ?? 0, new Problem("invalid", $"wait is a whole number of seconds from 0 to {PresenceNoticeVocabulary.MaxWaitSeconds}.", []));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(wait));
        long? after = request.After;
        // A read that waits is a client following the notices; one that does not, such as a read of the head, is not.
        if (wait > 0)
        {
            lock (sync) waiting++;
        }

        try
        {
            while (true)
            {
                Task signal;
                lock (sync)
                {
                    long from = after is not long position ? head : position > head ? 0 : position;
                    after = from;
                    List<PresenceNotice> notices = [.. kept.Where(n => n.Sequence > from)];
                    if (notices.Count > 0 || wait == 0 || deadline.IsCancellationRequested)
                    {
                        if (notices.Count > 0) Delivered(notices[^1].Sequence);
                        return new(notices, notices.Count > 0 ? notices[^1].Sequence : head);
                    }

                    signal = changed.Task;
                }

                try
                {
                    await signal.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The wait ended with nothing new: the next pass answers with what there is.
                }
            }
        }
        finally
        {
            if (wait > 0)
            {
                lock (sync)
                {
                    waiting--;
                    followEnded = Clock.GetTimestamp();
                }
            }
        }
    }

    // Caller holds sync: a read delivered every notice up to this position.
    private void Delivered(long sequence)
    {
        if (sequence <= delivered) return;
        delivered = sequence;
        TaskCompletionSource signal = read;
        read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
    }
}
