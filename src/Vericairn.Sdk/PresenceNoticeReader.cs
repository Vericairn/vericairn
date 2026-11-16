// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Signing;

namespace Vericairn.Sdk;

/// <summary>
/// From 0.9.4 (CODE-80): a presence session's list as a client reads it from the terms a notice carries (design/presence-sessions.md
/// §§ 6 and 7). The terms are checked as the release helper checks them before any dialog; the counts, each kind entry's summary from its
/// fixed values, each exact entry's type and short hash, and the fingerprint are derived here, from the terms themselves, never taken from
/// the runtime's words.
/// </summary>
internal sealed class PresenceSessionView
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private readonly byte[] terms;
    private readonly PresenceSessionRules rules;

    private PresenceSessionView(byte[] terms, PresenceSessionRules rules)
    {
        this.terms = terms;
        this.rules = rules;
    }

    /// <summary>The session's id, as the terms state it.</summary>
    internal string Session => rules.Session;

    /// <summary>The action's name from the vocabulary, as the terms state it.</summary>
    internal string Action => rules.Action;

    /// <summary>The session's limit in seconds.</summary>
    internal int LimitSeconds => rules.LimitSeconds;

    /// <summary>How many signatures the list covers, at most when <see cref="UpTo"/>.</summary>
    internal int Total => rules.Total;

    /// <summary>Whether any kind entry's count is at most.</summary>
    internal bool UpTo => rules.UpTo;

    /// <summary>The entries, in the terms' order.</summary>
    internal IReadOnlyList<PresenceTermsEntry> Entries => rules.Entries;

    /// <summary>The records a kind entry may supersede or observe that the session does not sign, by id, type and hash, as the terms list them.</summary>
    internal IReadOnlyList<PresenceTermsReference> References => rules.References;

    /// <summary>The references as one line: each recorded record's type, id and short hash; null when the terms list none.</summary>
    internal string? ReferenceLine() => References.Count == 0 ? null
        : "it may supersede or observe these recorded records, which it does not sign: " + string.Join("; ", References.Select(r => $"{r.Type} {r.Id} at {Short(r.Hash)}"));

    /// <summary>
    /// Reads terms as a notice carries them: their exact compact canonical text, checked as the helper checks terms at opening; null, with
    /// why, when they are not terms the helper would open a session for.
    /// </summary>
    internal static PresenceSessionView? Read(string? text, out string? problem)
    {
        problem = null;
        if (string.IsNullOrEmpty(text))
        {
            problem = "the notice carries no terms";
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            return new PresenceSessionView(bytes, PresenceSessionRules.ReadTerms(bytes));
        }
        catch (PresenceSessionRefusal refusal)
        {
            problem = $"{refusal.Message} ({refusal.Code})";
            return null;
        }
    }

    /// <summary>
    /// The list fingerprint (§ 6): the first 60 bits of SHA-256 over the ASCII bytes of the format's name, one zero byte, the 16-byte nonce
    /// and the 32-byte SHA-256 of the terms, as twelve Crockford base32 characters in three groups of four.
    /// </summary>
    internal static string Fingerprint(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> terms)
    {
        byte[] name = Encoding.ASCII.GetBytes("vericairn.presence-session/1");
        byte[] input = new byte[name.Length + 1 + nonce.Length + 32];
        name.CopyTo(input, 0);
        nonce.CopyTo(input.AsSpan(name.Length + 1));
        SHA256.HashData(terms).CopyTo(input, name.Length + 1 + nonce.Length);
        byte[] hash = SHA256.HashData(input);
        var text = new StringBuilder(14);
        for (int index = 0; index < 12; index++)
        {
            if (index is 4 or 8) text.Append('-');
            // Character k holds bits 5k to 5k + 4 of the hash, counted from its first byte's most significant bit.
            int bit = 5 * index, value = 0;
            for (int b = bit; b < bit + 5; b++) value = (value << 1) | ((hash[b >> 3] >> (7 - (b & 7))) & 1);
            text.Append(Alphabet[value]);
        }

        return text.ToString();
    }

    /// <summary>The fingerprint for a nonce as a notice carries it, base64url without padding; null when it is not 16 bytes.</summary>
    internal string? FingerprintFor(string? nonce)
    {
        byte[]? bytes = Nonce(nonce);
        return bytes is null ? null : Fingerprint(bytes, terms);
    }

    private static byte[]? Nonce(string? text)
    {
        if (text is not { Length: 22 } || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        try
        {
            byte[] bytes = System.Buffers.Text.Base64Url.DecodeFromChars(text);
            return bytes.Length == 16 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>One entry as read from the terms: an exact entry's type and short hash, or a kind entry's count, kind and fixed values.</summary>
    internal static string Summary(PresenceTermsEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Exact)
        {
            string action = entry.Action is null ? string.Empty : " " + Phrase(entry);
            return $"{entry.Type}{action} by exact hash {Short(entry.Hash)}";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{(entry.AtMost ? "up to " : string.Empty)}{entry.Count} {Phrase(entry)} {(entry.Count == 1 ? "record" : "records")} by kind");
        IReadOnlyDictionary<string, string> f = entry.Fixed;
        string? Value(string name) => f.TryGetValue(name, out string? value) ? value : null;
        switch (entry.Type)
        {
            case "history-checkpoint":
                text.Append(", trigger ").Append(entry.Trigger ?? "release");
                return text.ToString();
            case "release":
                text.Append(", version ").Append(Value("version")).Append(", commit ").Append(Commit(Value("commit"))).Append(", destination ").Append(Value("destination"))
                    .Append(", product ").Append(Value("product")).Append(", status ").Append(string.Join(" or ", entry.Statuses));
                return text.ToString();
        }

        switch (entry.Action)
        {
            case "dispatch-release-workflow":
                text.Append(", version ").Append(Value("version")).Append(", commit ").Append(Commit(Value("commit"))).Append(", workflow ").Append(Value("workflow"))
                    .Append(", destination ").Append(Value("destination")).Append(", product ").Append(Value("product"));
                break;
            case "change-observe":
                text.Append(" of ").Append(entry.Observation switch
                {
                    "push-head" => "the pushed head",
                    "pull-request" => "the pull request",
                    "target-run" => "the target branch's run",
                    _ => entry.Observation,
                });
                if (entry.Observes is int observed) text.Append(CultureInfo.InvariantCulture, $", observing entry {observed + 1}'s operations");
                if (entry.ObservesReference is string reference) text.Append(", observing recorded operation ").Append(reference);
                break;
            default:
                if (Value("branch") is string branch) text.Append(", branch ").Append(branch);
                if (Value("pullRequest") is string pull) text.Append(", pull request ").Append(pull);
                if (entry.PullRequestFrom is int from) text.Append(CultureInfo.InvariantCulture, $", the pull request entry {from + 1} opens");
                break;
        }

        if (Value("head") is string head) text.Append(", head ").Append(Commit(head));
        if (Value("repository") is string repository) text.Append(", repository ").Append(repository);
        if (Value("product") is string product && entry.Action != "dispatch-release-workflow") text.Append(", product ").Append(product);
        if (Value("productHash") is string productHash) text.Append(" at ").Append(Short(productHash));
        if (Value("provider") is string provider) text.Append(", through ").Append(provider);
        text.Append(entry.Statuses.Count == PresenceSessionProtocol.OperationStatuses.Length ? ", any of its six statuses" : ", status " + string.Join(" or ", entry.Statuses));
        return text.ToString();
    }

    /// <summary>The list counted by type from the terms: exact entries by type, kind entries by the closed set's phrases.</summary>
    internal string Counts()
    {
        var parts = new List<string>();
        foreach (IGrouping<string, PresenceTermsEntry> type in Entries.Where(e => e.Exact).GroupBy(e => e.Type + (e.Action is null ? string.Empty : " " + Phrase(e)), StringComparer.Ordinal))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{type.Count()} {type.Key} {(type.Count() == 1 ? "record" : "records")} by exact hash"));
        List<string> kinds = [.. Entries.Where(e => !e.Exact).GroupBy(Phrase, StringComparer.Ordinal).Select(g =>
        {
            bool atMost = g.Any(e => e.AtMost);
            return string.Create(CultureInfo.InvariantCulture, $"{(atMost ? "up to " : string.Empty)}{g.Sum(e => e.Count)} {g.Key}");
        })];
        if (kinds.Count > 0) parts.Add(Join(kinds) + " records by kind");
        return string.Create(CultureInfo.InvariantCulture, $"{(UpTo ? "up to " : string.Empty)}{Total} signatures: ") + string.Join("; ", parts);
    }

    /// <summary>The number of the exact entry whose short hash this is, from 1, or null.</summary>
    internal int? ExactEntry(string? shortHash)
    {
        if (shortHash is not { Length: 12 }) return null;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Exact && Short(Entries[i].Hash) == shortHash) return i + 1;
        }

        return null;
    }

    private static string Phrase(PresenceTermsEntry entry) => entry.Type switch
    {
        "history-checkpoint" => "history checkpoint",
        "release" => "release",
        _ => entry.Action switch
        {
            "change-push" => "push",
            "change-open" => "opening",
            "change-merge" => "merge",
            "change-observe" => "observation",
            "dispatch-release-workflow" => "release build",
            "agent-run" => "agent run",
            "delete-release-draft" => "intent to delete a withdrawn draft",
            _ => entry.Action ?? entry.Type,
        },
    };

    private static string Join(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Short(string? hash) => hash is { Length: 71 } ? hash[7..19] : hash ?? "?";

    private static string Commit(string? commit) => commit is { Length: > 12 } ? commit[..12] : commit ?? "?";
}

/// <summary>
/// What a client shows for a presence notice, one line or more: the notice's kind, the text, and whether it belongs in a status line.
/// Every line taken from the runtime begins with <see cref="PresenceNoticeReader.RuntimeMarker"/>; a line without it is one the client
/// derived itself.
/// </summary>
/// <param name="Kind">The notice's kind.</param>
/// <param name="Text">What to show, one line or more.</param>
/// <param name="Status">Whether it names a check or a signature, which a status line shows as it happens.</param>
internal sealed record PresenceLine(string Kind, string Text, bool Status);

/// <summary>
/// From 0.9.4 (CODE-80): turns the presence notices a client reads, batch by batch in order, into what the command line and Workbench show
/// (design/presence-sessions.md § 7). A session's list is shown from its terms, counted and summarized here; when the check is asked, the
/// fingerprint the system dialog shows is computed here from the nonce and the terms, and only for terms this reader received in an earlier
/// read than the nonce: terms that arrive with the nonce or after it, or that the helper would refuse, get a warning in place of a
/// fingerprint, so a runtime cannot choose the list after it learns the nonce. Only what the client derives is shown in its own form.
/// Everything the runtime wrote (a notice's text, its action, the descriptions, a reason) is shown with <see cref="RuntimeMarker"/> at the
/// start of every line it occupies, its control and format characters shown escaped and its line breaks, inside a field, shown as
/// <c>\n</c>; a notice the reader cannot check is shown that way whole, after a warning, never in the client's form.
/// </summary>
internal sealed class PresenceNoticeReader
{
    /// <summary>What begins every line a client shows that the runtime wrote; no line the client derives begins with it.</summary>
    internal const string RuntimeMarker = "runtime: ";

    private const int Kept = 32;
    private readonly Dictionary<string, Held> sessions = new(StringComparer.Ordinal);
    private readonly Queue<string> order = new();

    /// <summary>Whether a line shown, with or without a log's time before it, is the runtime's.</summary>
    internal static bool IsRuntimeLine(string? line)
    {
        if (line is null) return false;
        if (line.StartsWith(RuntimeMarker, StringComparison.Ordinal)) return true;
        // A log line: its time, two spaces, then the line.
        int at = line.IndexOf("  ", StringComparison.Ordinal);
        return at >= 0 && line.AsSpan(at + 2).StartsWith(RuntimeMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime string as one line a person can read: line breaks, tabs, every other control character, format characters (such as
    /// bidirectional overrides) and line and paragraph separators are shown escaped, so the string can neither break a line nor drive the
    /// terminal.
    /// </summary>
    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var shown = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '\n': shown.Append("\\n"); continue;
                case '\r': shown.Append("\\r"); continue;
                case '\t': shown.Append("\\t"); continue;
            }

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsControl(rune) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                shown.Append(CultureInfo.InvariantCulture, $"\\u{{{rune.Value:X4}}}");
            else
                shown.Append(rune.ToString());
        }

        return shown.ToString();
    }

    /// <summary>A runtime's text, one marked line for each of its lines, each sanitized.</summary>
    internal static string Runtime(string? text)
    {
        if (string.IsNullOrEmpty(text)) return RuntimeMarker + "(no text)";
        return string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => RuntimeMarker + Sanitize(line)));
    }

    /// <summary>The lines for one batch of notices, as one read delivered them, oldest first.</summary>
    internal IReadOnlyList<PresenceLine> Read(IEnumerable<PresenceNotice> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var lines = new List<PresenceLine>();
        var arrived = new List<Held>();
        foreach (PresenceNotice notice in batch)
        {
            if (Render(notice, arrived) is PresenceLine line) lines.Add(line);
        }

        // Terms count as received before a nonce only once the read that brought them is over.
        foreach (Held held in arrived) held.Before = true;
        return lines;
    }

    private PresenceLine? Render(PresenceNotice notice, List<Held> arrived)
    {
        PresenceSessionNotice? session = notice.Session;
        switch (notice.Kind)
        {
            case "announced" when session is not null:
            case "renewed":
                if (session?.Terms is null) return Unchecked(notice, "it is a presence session's list without its terms", false);
                return List(notice, session, arrived);
            case "session":
                if (session is null) return Unchecked(notice, "it is a presence check without its session", true);
                return Check(session);
            case "signed":
                if (session is null) return Unchecked(notice, "it is a signature under a presence session without its session", true);
                return Signed(notice, session);
            case "ended" when session?.Reason == "closed":
                // Its action's end or its renewal says the rest.
                return null;
            case "prompt":
                string hash = notice.ShortHash is null ? string.Empty : " (" + Sanitize(notice.ShortHash) + ")";
                return new PresenceLine(notice.Kind, Runtime(notice.Text) + hash, true);
            default:
                return new PresenceLine(notice.Kind, Runtime(notice.Text), false);
        }
    }

    // A notice of a session's kind whose shape the reader cannot check: a warning of the client's, then the runtime's text, marked.
    private static PresenceLine Unchecked(PresenceNotice notice, string why, bool status) =>
        new(notice.Kind, $"Warning: this display cannot check the next notice, since {why}; what follows is the runtime's text, unchecked, and no fingerprint is shown for it.\n"
            + Runtime(notice.Text), status);

    private PresenceLine Signed(PresenceNotice notice, PresenceSessionNotice session)
    {
        string text = Runtime(notice.Text);
        if (notice.ShortHash is string shortHash && sessions.TryGetValue(session.Id, out Held? of) && of.View?.ExactEntry(shortHash) is int entry)
            text += string.Create(CultureInfo.InvariantCulture, $"\n  Its short hash is entry {entry} of the list above, by exact hash.");
        return new PresenceLine(notice.Kind, text, true);
    }

    private PresenceLine List(PresenceNotice notice, PresenceSessionNotice session, List<Held> arrived)
    {
        PresenceSessionView? view = PresenceSessionView.Read(session.Terms, out string? problem);
        if (view is not null && view.Session != session.Id)
        {
            problem = "the notice names another session than its terms";
            view = null;
        }

        var held = new Held(view, problem);
        Keep(session.Id, held);
        arrived.Add(held);
        if (view is null)
            return new PresenceLine(notice.Kind, $"Warning: this display cannot read a presence session's terms ({problem}), so it shows no list and no fingerprint for its check; "
                + "what follows is the runtime's text, unchecked.\n" + Runtime(notice.Text), false);

        var text = new StringBuilder();
        string covered = string.Create(CultureInfo.InvariantCulture, $"{(view.UpTo ? "up to " : string.Empty)}{view.Total} signatures in at most {view.LimitSeconds} s");
        text.Append(notice.Kind == "renewed"
            ? $"{view.Action} renews its presence session and asks once more, for {covered}, covering only this list, read here from its terms:"
            : $"{view.Action} asks for your presence once, for one presence session of {covered}, covering only this list, read here from its terms:");
        // The runtime's own words, each marked: its name for the action where it differs from the terms', and a renewal's reason.
        if (!string.Equals(notice.Action, view.Action, StringComparison.Ordinal)) text.Append('\n').Append(RuntimeMarker).Append("names the action ").Append(Sanitize(notice.Action));
        if (notice.Kind == "renewed" && session.Reason is string reason) text.Append('\n').Append(RuntimeMarker).Append("gives the reason: ").Append(Sanitize(reason));
        IReadOnlyList<PresenceEntryDescription> descriptions = session.Descriptions ?? [];
        for (int i = 0; i < view.Entries.Count; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n  {i + 1}. {PresenceSessionView.Summary(view.Entries[i])}");
            if (descriptions.FirstOrDefault(d => d.Entry == i) is not { } described) continue;
            text.Append(CultureInfo.InvariantCulture, $"\n{RuntimeMarker}  describes entry {i + 1}: ").Append(Sanitize(described.Type)).Append(' ').Append(Sanitize(described.Key))
                .Append(": ").Append(Sanitize(described.Purpose));
            if (described.Changes is string changes) text.Append(CultureInfo.InvariantCulture, $"\n{RuntimeMarker}  says entry {i + 1} changes: ").Append(Sanitize(changes));
            if (described.Flagged) text.Append(CultureInfo.InvariantCulture, $"\n{RuntimeMarker}  flags entry {i + 1}: a review there is by the same agent harness that authored the work it reviews");
        }

        if (view.ReferenceLine() is string references) text.Append("\n  Read here from the terms, ").Append(references).Append('.');
        text.Append("\n  In all, counted here from the terms: ").Append(view.Counts()).Append('.');
        text.Append("\nAnything outside this list asks again.");
        return new PresenceLine(notice.Kind, text.ToString(), false);
    }

    // The check: a fingerprint the client computed, or its warning; never the runtime's words.
    private PresenceLine Check(PresenceSessionNotice session)
    {
        const string Decline = "Approve the system dialog only if you asked for exactly this action, or decline it.";
        if (!sessions.TryGetValue(session.Id, out Held? held))
            return new PresenceLine("session", "Presence check: warning, no fingerprint is shown, because this display has not received the terms of the list this check covers. " + Decline, true);
        if (!held.Before)
            return new PresenceLine("session", "Presence check: warning, no fingerprint is shown, because this display received the list's terms only with the check's nonce, "
                + "so the list it shows may not be the one the system dialog covers. " + Decline, true);
        if (held.View is null)
            return new PresenceLine("session", $"Presence check: warning, no fingerprint is shown, because this display cannot read the list's terms ({held.Problem}). " + Decline, true);
        if (held.View.FingerprintFor(session.Nonce) is not string fingerprint)
            return new PresenceLine("session", "Presence check: warning, no fingerprint is shown, because the check's nonce is not 16 bytes in base64url. " + Decline, true);
        return new PresenceLine("session", $"Presence check for {held.View.Action}: the system dialog should show fingerprint {fingerprint}, computed here from the list above and the check's nonce. "
            + string.Create(CultureInfo.InvariantCulture, $"Approve it once, only if the fingerprints match and the list is what you asked for; it covers {(held.View.UpTo ? "up to " : string.Empty)}{held.View.Total} signatures in at most {held.View.LimitSeconds} s."), true);
    }

    private void Keep(string id, Held held)
    {
        if (!sessions.ContainsKey(id)) order.Enqueue(id);
        sessions[id] = held;
        while (order.Count > Kept) sessions.Remove(order.Dequeue());
    }

    private sealed class Held(PresenceSessionView? view, string? problem)
    {
        internal PresenceSessionView? View { get; } = view;

        internal string? Problem { get; } = problem;

        /// <summary>Whether the read that brought the terms is over, so they were received before any later notice's nonce.</summary>
        internal bool Before { get; set; }
    }
}
