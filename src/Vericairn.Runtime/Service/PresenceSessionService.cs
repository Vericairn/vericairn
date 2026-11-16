// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Runtime.Service;

/// <summary>
/// What one composition of an action's announced list yields (design/presence-sessions.md § 5): the records the action holds now,
/// authored completely in advance, the kind entries of the closed set for the records its mechanics write later, and the references a
/// kind record may supersede or observe that the session does not sign.
/// </summary>
internal sealed class PresenceSessionPlan
{
    /// <summary>The action's name from the vocabulary.</summary>
    internal required string Action { get; init; }

    /// <summary>The runtime's identity of the action, as UTF-8 under SHA-256 in the terms.</summary>
    internal required string Identity { get; init; }

    /// <summary>The records the action holds now, in the order it signs them.</summary>
    internal IReadOnlyList<RecordDraft> Exact { get; init; } = [];

    /// <summary>The kind entries, in announcement order; an observation's or a merge's index names another kind entry of this list.</summary>
    internal IReadOnlyList<PresencePlannedKind> Kinds { get; init; } = [];

    /// <summary>The records a kind record may supersede or observe, by id.</summary>
    internal IReadOnlyList<string> References { get; init; } = [];
}

/// <summary>A kind entry as an action plans it, its indices naming other kind entries of the same plan, with one line on what it changes.</summary>
/// <param name="Entry">The entry; <see cref="PresenceListEntry.Observes"/> and <see cref="PresenceListEntry.PullRequestFrom"/> index the plan's kinds.</param>
/// <param name="Summary">What the entry covers, in words.</param>
/// <param name="Changes">One line on what its records change.</param>
internal sealed record PresencePlannedKind(PresenceListEntry Entry, string Summary, string Changes);

/// <summary>What a composition is asked from: the record about to be signed and the records the action's sessions signed already.</summary>
internal sealed record PresenceComposeContext(RecordDraft Signing, IReadOnlySet<string> Signed);

/// <summary>
/// One operator action's presence session state, carried by its tally: what composes its list, the session open now, how many checks
/// its signatures took, the records signed under its sessions, why its next list is announced, and an effect ahead of its next signature.
/// </summary>
internal sealed class PresenceSessionAction(Func<PresenceComposeContext, PresenceSessionPlan?> compose, bool redatable = false)
{
    /// <summary>Composes the action's list from everything it knows now, under the estate's gate.</summary>
    internal Func<PresenceComposeContext, PresenceSessionPlan?> Compose { get; } = compose;

    /// <summary>Whether an exact draft not yet signed may be authored again with a new time for a later list (§ 5).</summary>
    internal bool Redatable { get; } = redatable;

    internal PresenceOpenSession? Open { get; set; }

    /// <summary>Presence checks the action's signatures took: sessions opened and per-signature prompts.</summary>
    internal int Checks { get; set; }

    internal int SessionsOpened { get; set; }

    /// <summary>Presence checks asked to open a session, answered or not.</summary>
    internal int SessionChecks { get; set; }

    internal bool Failed { get; set; }

    /// <summary>The ids of the records signed under the action's sessions.</summary>
    internal HashSet<string> Signed { get; } = new(StringComparer.Ordinal);

    /// <summary>Why the next list is announced, when it renews one: the reason the renewal's notice gives.</summary>
    internal string? Renewal { get; set; }

    /// <summary>The bound the runtime sets on an effect its next signature dispatches (§ 9), until that signature.</summary>
    internal TimeSpan? EffectAhead { get; set; }

    /// <summary>Why sessions are off for this action, once found; one check per signature then.</summary>
    internal string? Off { get; set; }

    /// <summary>The ids of the exact drafts the action holds, in the order it signs them.</summary>
    internal List<string> ExactIds { get; } = [];

    /// <summary>New times given to exact drafts not yet signed, by draft id, for a later list (§ 5).</summary>
    internal Dictionary<string, DateTimeOffset> Redated { get; } = new(StringComparer.Ordinal);

    /// <summary>The draft as the action signs it now: with its new time when a later list authored it again.</summary>
    internal RecordDraft Current(RecordDraft draft) => draft.Id is string id && Redated.TryGetValue(id, out DateTimeOffset at) ? draft with { At = at } : draft;
}

/// <summary>An open presence session: the helper's client, the checks the runtime applies before each record, and its clock.</summary>
internal sealed class PresenceOpenSession(PresenceSessionClient client, PresenceSessionRules rules, PresenceSessionOpening opening, string device,
    IReadOnlyList<string> exactIds, TimeProvider clock)
{
    private readonly long started = clock.GetTimestamp();

    internal PresenceSessionClient Client { get; } = client;

    internal PresenceSessionRules Rules { get; } = rules;

    internal PresenceSessionOpening Opening { get; } = opening;

    internal string Device { get; } = device;

    /// <summary>The ids of the exact entries the list holds.</summary>
    internal IReadOnlyList<string> ExactIds { get; } = exactIds;

    /// <summary>Seconds since the helper answered that the session opened, on this process's monotonic clock.</summary>
    internal double Elapsed(TimeProvider now) => now.GetElapsedTime(started).TotalSeconds;
}

/// <summary>
/// What a runtime's presence sessions depend on outside it, which tests replace: the build, the platform and its macOS version, the
/// release helper's requirement check, how a helper is started for a key, the owner's settings, the agent condition and the clock.
/// </summary>
internal sealed class PresenceSessionHost
{
    /// <summary>The build's release identity and qualified macOS versions.</summary>
    internal PresenceSessionBuild Build { get; init; } = PresenceSessionBuild.Current;

    /// <summary>Whether this machine runs macOS.</summary>
    internal Func<bool> MacOS { get; init; } = OperatingSystem.IsMacOS;

    /// <summary>This Mac's macOS major version.</summary>
    internal Func<int?> MacOSMajor { get; init; } = () => OperatingSystem.IsMacOS() ? Environment.OSVersion.Version.Major : null;

    /// <summary>The release helper's requirement check, or null where none can run.</summary>
    internal IPresenceHelperRequirement? Requirement { get; init; } = OperatingSystem.IsMacOS() ? new SecurityFrameworkRequirement() : null;

    /// <summary>Starts a session's helper for a key the signer holds, or null where it cannot: the release helper with the key's blob.</summary>
    internal Func<StoreSigner, IPresenceHelperChannel?> Start { get; init; } = StartReleaseHelper;

    /// <summary>The helper a signer's key would be signed by, whose requirement is checked; null when the key is not in the enclave.</summary>
    internal Func<StoreSigner, string?> HelperOf { get; init; } = signer => OperatingSystem.IsMacOS() && signer.Store is SecureEnclaveKeyStore enclave ? enclave.Helper : null;

    /// <summary>The device owner's setting on this machine.</summary>
    internal Func<string, PresenceSetting> Setting { get; init; } = device => PresenceSettings.Read(device);

    /// <summary>Whether an agent run this machine's worker started is unsettled under this operating-system user (decision 4).</summary>
    internal Func<bool> SameUserAgentRun { get; init; } = () => Workers.CustodyGuard.UnsettledAgentRuns();

    /// <summary>The clock a session's elapsed time is read on.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How long a check waits for the person; the protocol's 300 seconds outside tests.</summary>
    internal TimeSpan? CheckWait { get; init; }

    private static IPresenceHelperChannel? StartReleaseHelper(StoreSigner signer)
    {
        if (!OperatingSystem.IsMacOS() || signer.Store is not SecureEnclaveKeyStore enclave) return null;
        return StartOnMac(enclave, signer.KeyId);
    }

    [SupportedOSPlatform("macos")]
    private static PresenceHelperProcess StartOnMac(SecureEnclaveKeyStore enclave, string keyId) =>
        PresenceHelperProcess.Start(enclave.Helper, ["session", enclave.BlobPath(keyId)]);

    /// <summary>
    /// Why this machine and build open no presence session for any device, or null when they may (design/presence-sessions.md § 11):
    /// another platform, a development build, a release that lists no macOS version or not this one, or a helper that is missing or does
    /// not meet the release requirement. <paramref name="helper"/> is the helper beside the command line, when known.
    /// </summary>
    internal string? MachineOff(string? helper)
    {
        if (!MacOS()) return "presence sessions open only for a Mac's Secure Enclave keys";
        if (!Build.Release) return "this is a development build, which carries no release identity; only a release build's signed helper opens a session";
        if (Build.MacOS.Count == 0) return "this release lists no macOS version qualified for presence sessions";
        int? major = MacOSMajor();
        if (major is null || !Build.MacOS.Contains(major.Value))
            return $"this release has not qualified macOS {major?.ToString(CultureInfo.InvariantCulture) ?? "of this version"} for presence sessions";
        if (helper is null) return "the release helper is not beside the command line";
        if (Requirement is null || Build.Requirement is not string requirement) return "the release helper's signature cannot be checked here";
        return Requirement.Problem(helper, requirement) is string problem ? "the release helper does not meet the release requirement: " + problem : null;
    }
}

/// <summary>
/// From 0.9.4 (ID-16): the runtime's presence sessions (design/presence-sessions.md §§ 5, 7, 9 and 10). At the shared signing boundary,
/// an action of the vocabulary composes its announced list at its first signature that needs presence; one presence check opens a session
/// of the release helper bound to that action's flow, that list, that device and its limit; each record is checked here before it is sent
/// and signed only when the list covers it, and every returned signature is verified against the device's key. A record the list does not
/// cover, and any after the session's lapse, is announced in a new list with its reason and asks a new check; an effect is not dispatched
/// while the session could lapse before its receipt. A runtime holds at most one session per device; another action's signatures with
/// that device ask one check each. Every other type, action, profile and build keeps one check per signature.
/// </summary>
internal sealed class PresenceSessionService
{
    /// <summary>The margin an effect needs beyond its deadline before it is dispatched under a session.</summary>
    internal static readonly TimeSpan EffectMargin = TimeSpan.FromSeconds(60);

    /// <summary>The bound the runtime sets on a push, an opening, a merge and a release build's dispatch.</summary>
    internal static readonly TimeSpan EffectDeadline = TimeSpan.FromMinutes(2);

    private readonly PresenceCounter counter;
    private readonly ConcurrentDictionary<string, PresenceSessionAction> devices = new(StringComparer.Ordinal);
    private readonly List<PresenceOutsideList> outside = [];

    internal PresenceSessionService(PresenceCounter counter)
    {
        this.counter = counter;
        counter.Closing = Close;
    }

    /// <summary>The host; tests replace it.</summary>
    internal PresenceSessionHost Host { get; set; } = new();

    /// <summary>The refusals that ended a session, newest last, kept in memory for attention (presence.outside-list).</summary>
    internal IReadOnlyList<PresenceOutsideList> Outside
    {
        get
        {
            lock (outside) return [.. outside];
        }
    }

    /// <summary>Raised when a refusal is kept, so attention is derived again.</summary>
    internal Action? OutsideChanged { get; set; }

    /// <summary>Marks the action's next signature as the one that dispatches an effect with this bound (§ 9).</summary>
    internal void EffectAhead(TimeSpan deadline)
    {
        if (counter.Current?.Session is { } action) action.EffectAhead = deadline;
    }

    /// <summary>
    /// The draft an exact action signs now: when the list that holds it is not the one open (a split action's next list), or the session's
    /// limit is near, the session ends here, before the record is authored, and drafts not yet signed take a new time when the action allows
    /// it, so the next list is announced, just before its own check, with every exact entry inside the helper's window.
    /// </summary>
    internal RecordDraft Draft(RecordDraft draft)
    {
        if (counter.Current?.Session is not { } action) return draft;
        if (action.Open is { } open)
        {
            bool held = draft.Id is string id && (open.ExactIds.Contains(id, StringComparer.Ordinal) || action.Signed.Contains(id));
            bool near = open.Elapsed(Host.Clock) >= open.Opening.LimitSeconds - 10;
            if (!held || near)
            {
                EndOpen(action, near ? "the session's limit is near" : "the action's next list of at most 16 exact entries is announced before its own check");
                if (action.Redatable) Redate(action);
            }
        }
        else if (action.SessionsOpened > 0 && action.Redatable && draft.Id is string id && !action.Signed.Contains(id))
        {
            Redate(action);
        }

        return action.Current(draft);
    }

    /// <summary>Gives every exact draft not yet signed the present time, for the next list (§ 5).</summary>
    internal void Redate(PresenceSessionAction action)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(Host.Clock.GetUtcNow().ToUnixTimeSeconds());
        foreach (string id in action.ExactIds)
        {
            if (!action.Signed.Contains(id)) action.Redated[id] = now;
        }
    }

    /// <summary>
    /// Signs one record under the action's presence session, opening or renewing it as the list requires; null when the record keeps its
    /// own check: no session applies to the action, the device or this build, or the record cannot be in a list. A declined or unanswered
    /// check, or a refusal by the helper, fails the signature, and the action stops as a declined prompt stops it.
    /// </summary>
    internal RecordSignature? Sign(PresenceSessionSigning signing)
    {
        ArgumentNullException.ThrowIfNull(signing);
        PresenceTally? tally = counter.Current;
        if (tally?.Session is not { } action) return null;
        // After a declined or unanswered check, or a refusal, the action asks nothing more: it never falls back to one check per signature.
        if (action.Failed) throw new PresenceSessionEnded(null, "declined", "The action's presence check was declined, went unanswered or ended in a refusal; it asks for no more signatures.");
        // An effect ahead belongs to this signature alone, whether or not a session signs it.
        TimeSpan? effect = action.EffectAhead;
        action.EffectAhead = null;
        if (action.Off is not null) return null;
        if (devices.TryGetValue(signing.Device, out PresenceSessionAction? holder) && !ReferenceEquals(holder, action)) return null;
        if (action.Open is { } other && other.Device != signing.Device) return null;

        byte[] content = signing.Content;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (action.Open is null || action.Open.Client.Ended)
            {
                if (action.Open is { } over) EndOpen(action, action.Renewal ?? Why(over.Client.EndReason));
                if (signing.WhyOff() is string off)
                {
                    action.Off = off;
                    return null;
                }

                if (!OpenFor(tally, action, signing)) return null;
            }

            PresenceOpenSession open = action.Open!;
            if (open.Elapsed(Host.Clock) >= open.Opening.LimitSeconds - 1)
            {
                EndOpen(action, "the session reached its limit");
                continue;
            }

            int entry;
            PresenceRecord record;
            try
            {
                entry = open.Rules.Match(content, open.Opening.OpenedAt, open.Elapsed(Host.Clock), out record);
            }
            catch (PresenceSessionRefusal refusal) when (refusal.Code is not "record.form")
            {
                EndOpen(action, Uncovered(refusal.Code));
                continue;
            }

            // Before the record that dispatches an effect, the session must outlast the effect's bound and a minute, or be renewed first
            // when a fresh session's limit is that long; with a shorter limit the effect goes under this session and its receipt under a renewal.
            if (effect is TimeSpan deadline)
            {
                TimeSpan margin = deadline + EffectMargin;
                int seconds;
                try
                {
                    (_, seconds) = open.Client.Status();
                }
                catch (PresenceSessionEnded ended) when (Lapsed(ended) && attempt == 0)
                {
                    // The session lapsed before the effect: a lock, sleep, switch of user session, its limit or a helper gone. It renews.
                    EndOpen(action, Why(ended.Reason));
                    continue;
                }
                catch (PresenceSessionEnded)
                {
                    action.Failed = true;
                    EndOpen(action, null);
                    throw;
                }

                if (seconds < margin.TotalSeconds && signing.Limit >= margin.TotalSeconds && attempt == 0)
                {
                    EndOpen(action, $"fewer than {margin.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds remain, the effect's bound and a minute, so the session is renewed before the effect is dispatched");
                    continue;
                }

                effect = null;
            }

            PresenceSessionSignature signed;
            try
            {
                signed = open.Client.Sign(content);
            }
            catch (PresenceSessionEnded ended) when (Lapsed(ended) && attempt == 0)
            {
                EndOpen(action, Why(ended.Reason));
                continue;
            }
            catch (PresenceSessionEnded ended) when (ended.Code is string code && code.StartsWith("record.", StringComparison.Ordinal))
            {
                // The helper refused what the runtime's own checks admitted: a defect or an attack. Nothing is signed; attention is raised.
                Raise(new PresenceOutsideList(tally.Action, signing.Principal, record.Type, signing.Describe.Key, record.Hash, code, ended.Message,
                    DateTimeOffset.FromUnixTimeSeconds(Host.Clock.GetUtcNow().ToUnixTimeSeconds())));
                action.Failed = true;
                EndOpen(action, null);
                throw;
            }
            catch (PresenceSessionEnded)
            {
                action.Failed = true;
                EndOpen(action, null);
                throw;
            }

            open.Rules.Signed(record, entry);
            action.Signed.Add(record.Id);
            counter.SessionSigned(tally, signing.Describe, record.ShortHash, action.Signed.Count + open.Rules.Left, open.Rules.UpTo || action.SessionsOpened > 1);
            return new RecordSignature(RecordSigning.Algorithm, signing.Device, PresenceText.Base64UrlText(signed.Signature));
        }

        return null;
    }

    // A session that lapsed rather than refused: it reached its limit, the screen locked or the user session switched, the Mac slept, or
    // the helper is gone or stopped answering. The next signature renews with a new list and one more check.
    private static bool Lapsed(PresenceSessionEnded ended) =>
        ended.Code is null or "session.ended" && ended.Reason is "limit" or "lock" or "sleep" or "input" or "silent";

    private static string Why(string? reason) => reason switch
    {
        "limit" => "the session reached its limit",
        "lock" => "the session ended when the screen locked or the user session switched",
        "sleep" => "the session ended when the Mac slept",
        "silent" => "the helper stopped answering and was stopped",
        _ => "the helper ended the session",
    };

    private static string Uncovered(string code) => code switch
    {
        "record.time" => "the record is dated outside the session's window",
        "record.author" => "the record takes an id the session holds",
        _ => "the action needs a signature its list does not cover",
    };

    // Composes the action's list for the record about to be signed, announces it and asks its one check; false when the record keeps its own.
    private bool OpenFor(PresenceTally tally, PresenceSessionAction action, PresenceSessionSigning signing)
    {
        PresenceSessionList? list = signing.ComposeList(action, Host.SameUserAgentRun());
        if (list is null || list.Total < 2) return false;
        if (!devices.TryAdd(signing.Device, action) && !ReferenceEquals(devices.GetValueOrDefault(signing.Device), action)) return false;
        string session = Ulid.NewUlid(Host.Clock.GetUtcNow()).Value;
        byte[] terms = PresenceTermsWriter.Write(list.Action, list.ActionIdentity, signing.Principal, signing.Device, signing.PublicKey, session, signing.Limit, list.Entries, list.References);
        IReadOnlyList<byte[]> bytes = PresenceTermsWriter.Bytes(list.Entries, list.References);
        PresenceSessionRules rules;
        try
        {
            rules = PresenceSessionRules.Open(terms, bytes, Host.Clock.GetUtcNow().ToUnixTimeSeconds());
            rules.Match(signing.Content, Host.Clock.GetUtcNow().ToUnixTimeSeconds(), 0, out _);
        }
        catch (PresenceSessionRefusal)
        {
            // A list the runtime's own checks refuse, or one that does not cover the record now, opens nothing: the record keeps its own check.
            devices.TryRemove(new KeyValuePair<string, PresenceSessionAction>(signing.Device, action));
            return false;
        }

        IPresenceHelperChannel? channel;
        try
        {
            channel = Host.Start(signing.Signer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            channel = null;
        }

        if (channel is null)
        {
            action.Off = "the release helper could not be started";
            devices.TryRemove(new KeyValuePair<string, PresenceSessionAction>(signing.Device, action));
            return false;
        }

        string? renewal = action.Renewal;
        action.Renewal = null;
        counter.SessionNotice(tally, renewal is null ? "announced" : "revised", list.Total, list.Signatures, list.Announcement(tally.Action, signing.Limit, renewal));
        var client = new PresenceSessionClient(channel, signing.PublicKey);
        PresenceSessionOpening opening;
        bool prompted = false;
        try
        {
            opening = client.Open(terms, bytes, (nonce, fingerprint) =>
            {
                prompted = true;
                action.Checks++;
                action.SessionChecks++;
                counter.SessionNotice(tally, "prompt", list.Total, [],
                    string.Create(CultureInfo.InvariantCulture, $"Presence check for {tally.Action}: the system dialog shows fingerprint {fingerprint}; approve it once for {(rules.UpTo ? "up to " : string.Empty)}{list.Total} signatures in {signing.Limit} s. (nonce {nonce})"));
            }, Host.CheckWait);
        }
        catch (PresenceSessionEnded ended) when (!prompted && ended.Code is not null && !ended.Declined)
        {
            // Refused before any dialog (helper.unsigned, terms.*): no check was asked, so the action keeps one check per signature.
            client.Dispose();
            action.Off = $"the helper refused to open a session ({ended.Code})";
            devices.TryRemove(new KeyValuePair<string, PresenceSessionAction>(signing.Device, action));
            return false;
        }
        catch (PresenceSessionEnded)
        {
            // A declined or unanswered check signs nothing, and never falls back to one check per signature.
            client.Dispose();
            action.Failed = true;
            devices.TryRemove(new KeyValuePair<string, PresenceSessionAction>(signing.Device, action));
            throw;
        }

        action.SessionsOpened++;
        action.Open = new PresenceOpenSession(client, rules, opening, signing.Device, list.ExactIds, Host.Clock);
        return true;
    }

    private void EndOpen(PresenceSessionAction action, string? renewal)
    {
        if (action.Open is { } open)
        {
            action.Open = null;
            open.Client.Dispose();
            devices.TryRemove(new KeyValuePair<string, PresenceSessionAction>(open.Device, action));
        }

        if (renewal is not null) action.Renewal = renewal;
    }

    // An action's tally closes: its session ends with it.
    private void Close(PresenceTally tally)
    {
        if (tally.Session is { } action) EndOpen(action, null);
    }

    private void Raise(PresenceOutsideList refusal)
    {
        lock (outside)
        {
            outside.Add(refusal);
            if (outside.Count > 32) outside.RemoveAt(0);
        }

        OutsideChanged?.Invoke();
    }
}

/// <summary>A refusal by the helper of a record the runtime's checks admitted: attention presence.outside-list, kept in memory.</summary>
internal sealed record PresenceOutsideList(string Action, string Principal, string Type, string Key, string Hash, string Code, string Detail, DateTimeOffset At);

/// <summary>A composed list ready for terms: its entries and references, what each covers in words, and the exact entries' ids.</summary>
internal sealed class PresenceSessionList
{
    internal required string Action { get; init; }

    internal required string ActionIdentity { get; init; }

    internal required IReadOnlyList<PresenceListEntry> Entries { get; init; }

    internal required IReadOnlyList<PresenceListReference> References { get; init; }

    /// <summary>For each entry, what it covers and one line on what it changes, with a flag when its review is by the harness that authored the work.</summary>
    internal required IReadOnlyList<(PresenceSignature Signature, string Changes, bool Flagged)> Lines { get; init; }

    internal required IReadOnlyList<string> ExactIds { get; init; }

    internal int Total => Entries.Sum(e => e.Signatures);

    internal IReadOnlyList<PresenceSignature> Signatures => [.. Lines.Select(l => l.Signature)];

    /// <summary>The list as a notice announces it, before the check: each entry with what it changes; a renewal with its reason.</summary>
    internal string Announcement(string action, int limit, string? renewal)
    {
        bool upTo = Entries.Any(e => !e.Exact && e.AtMost);
        var text = new StringBuilder();
        string covered = string.Create(CultureInfo.InvariantCulture, $"{(upTo ? "up to " : string.Empty)}{Total} signatures in at most {limit} s");
        text.Append(renewal is null
            ? $"{action} asks for your presence once, for one presence session of {covered}, covering only this list:"
            : $"{action} renews its presence session because {renewal}, and asks once more, for {covered}, covering only this list:");
        for (int i = 0; i < Lines.Count; i++)
        {
            (PresenceSignature signature, string changes, bool flagged) = Lines[i];
            PresenceListEntry entry = Entries[i];
            string shape = entry.Exact ? "exact " + entry.Hash![7..19] : string.Create(CultureInfo.InvariantCulture, $"{(entry.AtMost ? "up to " : string.Empty)}{entry.Count} by kind");
            text.Append(CultureInfo.InvariantCulture, $"\n  {i + 1}. {signature.Describe()} ({shape})");
            text.Append("\n     changes: ").Append(changes);
            if (flagged) text.Append("\n     flagged: a review here is by the same agent harness that authored the work it reviews");
        }

        text.Append("\nAnything outside this list asks again.");
        return text.ToString();
    }
}

/// <summary>
/// One signature the shared signing boundary asks of a presence session: the signer, the record's canonical bytes and how a person knows
/// it, and the estate's facts the session needs, read under the gate. The estate service supplies the composition.
/// </summary>
internal sealed class PresenceSessionSigning
{
    internal required StoreSigner Signer { get; init; }

    internal required string Device { get; init; }

    internal required string Principal { get; init; }

    internal required string PublicKey { get; init; }

    internal required byte[] Content { get; init; }

    internal required PresenceSignature Describe { get; init; }

    /// <summary>The session's limit: the shorter of the owner's and 300 seconds, never under 60.</summary>
    internal required int Limit { get; init; }

    /// <summary>Why no session applies to this signature, or null when one may.</summary>
    internal required Func<string?> WhyOff { get; init; }

    /// <summary>Composes the list for this signature, or null when none covers it; the flag is the agent condition.</summary>
    internal required Func<PresenceSessionAction, bool, PresenceSessionList?> ComposeList { get; init; }
}
