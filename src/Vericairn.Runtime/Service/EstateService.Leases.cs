// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Events;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Presence leases (ID-11; design/events-and-presence.md § 8): an agent session's transport registers it and renews its lease while its
/// process and connection live, never its model. Leases are owner-only runtime state in the live store, one runtime's observation as
/// worker heartbeats are, and the daemon keeps them beside the event journal. A session holds the assignments routing gave it and those
/// it recorded attempts or progress on. When its lease lapses while it holds work that still holds its step and is not settled, the
/// runtime raises a presence event and attention labeled a local observation; still lapsed after limits.watch.reassignAfterSeconds, it
/// records an attention ask to the goal's accountable principal recommending reassignment, as the stall rule does. A heartbeat first
/// restores the lease, and the ask is withdrawn. These are not the signing presence sessions of ID-14 to ID-16.
/// </summary>
public sealed partial class EstateService
{
    private const string LeasesKey = "presence:leases";
    private const int MaxLeases = 64;
    private const int MaxLeasesPerPrincipal = 8;

    /// <summary>How long a lapsed lease that holds nothing is still listed before it is dropped.</summary>
    private static readonly TimeSpan LapsedListed = TimeSpan.FromHours(1);

    private readonly ConcurrentQueue<string> presenceWritten = new();
    private readonly SemaphoreSlim presenceWork = new(0, 1);

    /// <summary>The clock leases are judged by: the daemon's, which it gives with the event journal; tests move it by hand.</summary>
    internal TimeProvider PresenceClock { get; set; } = TimeProvider.System;

    /// <summary>Told after each presence sweep; tests wait on it rather than on time.</summary>
    internal Action? PresenceSwept { get; set; }

    /// <summary>True when this runtime keeps presence: the daemon does, beside its event journal; an embedded runtime keeps none.</summary>
    internal bool PresenceKept => journal is not null;

    private const string PresenceUnavailable = "This runtime keeps no presence: the daemon keeps it beside the event journal. Start the runtime with 'vericairn runtime start' and register through it.";

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRegister(PresenceRegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PresenceKept)
        {
            return LeaseProblem("unavailable", PresenceUnavailable);
        }

        lock (gate)
        {
            if (session.IsReadOnly)
            {
                return LeaseProblem("denied", "This session is read-only; it keeps no presence.");
            }

            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is Problem denied)
            {
                return new PresenceLeaseResponse(null, denied.Message, denied);
            }

            RequestDeviceScope? device = requestDevice.Value is { Closed: false } scope ? scope : null;
            string? principal = request.Principal;
            if (device is not null)
            {
                DeviceStanding standing = Standing(model, device.Device)!;
                if (principal is not null && model.CurrentPrincipal(principal) is { } named && model.Chain(named)[0] != model.Chain(model.ById[standing.Principal])[0])
                {
                    return LeaseProblem("denied", $"Device {device.Device} registers presence only for its own principal, {standing.Principal}.");
                }

                principal = standing.Principal;
            }

            if (principal is null || model.CurrentPrincipal(principal) is not RecordEnvelope who)
            {
                return LeaseProblem("refused", "Presence names the principal its session acts for: an agent or a service principal of this estate. Nothing was registered.");
            }

            if (EstateModel.BodyString(who, "kind") is not ("agent" or "service"))
            {
                return LeaseProblem("refused", $"{PrincipalName(model, principal)} is a person, and people are never tracked by their connections: only an agent or a service principal's session registers presence. A person declares availability on their communication setting instead. Nothing was registered.");
            }

            principal = model.Chain(model.ById[principal])[0];
            if (LeaseRequestRefusal(request) is string invalid)
            {
                return LeaseProblem("refused", invalid + " Nothing was registered.");
            }

            DateTimeOffset now = PresenceClock.GetUtcNow();
            List<PresenceLeaseState> leases = ReadLeases();
            ExpireLeases(leases, now);
            if (leases.Count(l => l.Principal == principal && l.Live) >= MaxLeasesPerPrincipal)
            {
                return LeaseProblem("unavailable", $"{PrincipalName(model, principal)} already holds {MaxLeasesPerPrincipal} live sessions here; release one or let it lapse first. Nothing was registered.");
            }

            if (leases.Count >= MaxLeases && leases.Where(l => !l.Live && l.Holds.Count == 0 && l.Asks.Count == 0).OrderBy(l => l.LapsedAt).FirstOrDefault() is { } dropped)
            {
                leases.Remove(dropped);
            }

            if (leases.Count >= MaxLeases)
            {
                return LeaseProblem("unavailable", $"This runtime keeps at most {MaxLeases} sessions; nothing was registered.");
            }

            string lease = PresenceLeaseState.NewLease();
            string hash = PresenceLeaseState.HashOf(lease);
            int seconds = request.LeaseSeconds ?? PresenceVocabulary.DefaultLeaseSeconds;
            long at = now.ToUnixTimeSeconds();
            var state = new PresenceLeaseState
            {
                Session = hash[..32],
                Hash = hash,
                Principal = principal,
                Device = device?.Device,
                Label = request.Label?.Trim() is { Length: > 0 } label ? label : null,
                Roles = [.. (request.Roles ?? []).Distinct(StringComparer.Ordinal)],
                Capacity = request.Capacity ?? 1,
                LeaseSeconds = seconds,
                RegisteredAt = at,
                RenewedAt = at,
                ExpiresAt = at + seconds,
            };
            leases.Add(state);
            WriteLeases(leases);
            return new PresenceLeaseResponse(LeaseView(state, lease), $"Session {state.Session} of {PrincipalName(model, principal)} is registered until {Time(state.ExpiresAt)}; its transport renews the lease every {seconds / 3} seconds.");
        }
    }

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceHeartbeat(string lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!PresenceKept)
        {
            return LeaseProblem("unavailable", PresenceUnavailable);
        }

        lock (gate)
        {
            if (LeaseOf(lease, out EstateModel model, out List<PresenceLeaseState> leases) is not PresenceLeaseState state)
            {
                return LeaseNotFound(model);
            }

            DateTimeOffset now = PresenceClock.GetUtcNow();
            bool restored = state.LapsedAt is not null;
            state.RenewedAt = now.ToUnixTimeSeconds();
            state.ExpiresAt = state.RenewedAt + state.LeaseSeconds;
            state.LapsedAt = null;
            WriteLeases(leases);
            if (restored)
            {
                // The asks its lapse raised are withdrawn by the next sweep, outside this request, since they are the runtime's own records.
                MarkEventsDirty();
                MarkPresenceDirty();
            }

            return new PresenceLeaseResponse(LeaseView(state, lease), restored
                ? $"Session {state.Session} is live again until {Time(state.ExpiresAt)}: its lapse is over, and any ask it raised is withdrawn."
                : $"Session {state.Session} is renewed until {Time(state.ExpiresAt)}.");
        }
    }

    /// <inheritdoc />
    public PresenceLeaseResponse PresenceRelease(string lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!PresenceKept)
        {
            return LeaseProblem("unavailable", PresenceUnavailable);
        }

        lock (gate)
        {
            if (LeaseOf(lease, out EstateModel model, out List<PresenceLeaseState> leases) is not PresenceLeaseState state)
            {
                return LeaseNotFound(model);
            }

            leases.Remove(state);
            WriteLeases(leases);
            MarkEventsDirty();
            MarkPresenceDirty();
            return new PresenceLeaseResponse(null, $"Session {state.Session} is released; a release is no lapse.");
        }
    }

    /// <inheritdoc />
    public PresenceResponse Presence(string? principal = null)
    {
        DateTimeOffset now = PresenceClock.GetUtcNow();
        if (!PresenceKept)
        {
            return new PresenceResponse([], RecordParser.FormatTimestamp(now), new Problem("unavailable", PresenceUnavailable, []));
        }

        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is Problem denied)
            {
                return new PresenceResponse([], RecordParser.FormatTimestamp(now), denied);
            }

            string? named = principal is null ? null : model.CurrentPrincipal(principal) is { } record ? model.Chain(record)[0] : principal;
            List<PresenceLeaseState> leases = ReadLeases();
            AttributeWrites(model, leases);
            ExpireLeases(leases, now);
            var holdings = LapsedHoldings(model, leases, now).ToLookup(h => h.Lease.Session, StringComparer.Ordinal);
            List<PresenceEntry> entries = [.. leases.Where(l => named is null || l.Principal == named).OrderBy(l => l.RegisteredAt).ThenBy(l => l.Session, StringComparer.Ordinal)
                .Select(l => new PresenceEntry(l.Session, l.Principal, l.Label, l.Roles, l.Capacity, l.Live ? "live" : "lapsed", Time(l.RegisteredAt), Time(l.RenewedAt), Time(l.ExpiresAt),
                    [.. l.Holds.Where(h => OpenHolding(model, l.Principal, h) is not null)], [.. l.Asks.Values.Where(a => holdings[l.Session].Any(h => h.Ask == a))]))];
            return new PresenceResponse(entries, RecordParser.FormatTimestamp(now));
        }
    }

    /// <summary>
    /// The presence sweep: on each tick, and soon after a heartbeat restores a lease or a release ends one, it marks the leases that
    /// expired lapsed, records the ask for each lapse holding work that has stood for its goal's reassignAfterSeconds, withdraws the
    /// asks whose lapse is over, and drops lapsed leases that hold nothing. A failed sweep is told to the log and tried again; it never
    /// stops the runtime. The daemon runs it while it serves.
    /// </summary>
    public async Task RunPresenceSweepAsync(TimeSpan tick, Action<string>? log, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await presenceWork.WaitAsync(tick, cancellationToken).ConfigureAwait(false);
                SweepPresence();
            }
            catch (Exception exception) when (exception is OperationCanceledException && cancellationToken.IsCancellationRequested || exception is ObjectDisposedException && closeRequested)
            {
                break;
            }
            catch (Exception exception) when (!Fatal(exception))
            {
                log?.Invoke($"presence could not be swept: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    /// <summary>One presence sweep, as the daemon's loop runs it; tests call it after moving the clock.</summary>
    internal void SweepPresence()
    {
        if (!PresenceKept)
        {
            return;
        }

        lock (gate)
        {
            if (session.IsReadOnly)
            {
                return;
            }

            DateTimeOffset now = PresenceClock.GetUtcNow();
            EstateModel model = session.Load();
            List<PresenceLeaseState> leases = ReadLeases();
            string before = PresenceLeaseState.Write(leases);
            AttributeWrites(model, leases);
            bool lapsedNow = ExpireLeases(leases, now);

            // What a lease holds stays only while it holds its step and is unsettled.
            foreach (PresenceLeaseState lease in leases)
            {
                lease.Holds.RemoveAll(h => OpenHolding(model, lease.Principal, h) is null && !lease.Asks.ContainsKey(h));
            }

            List<LapsedHolding> holdings = LapsedHoldings(model, leases, now);
            foreach (LapsedHolding holding in holdings.Where(h => h.Ask is null))
            {
                string identity = model.Chain(holding.Assignment)[0];
                if (holding.Lease.Asks.ContainsKey(identity) || now < holding.Since.AddSeconds(Routing.ReassignAfterSeconds(model, holding.Goal)))
                {
                    continue;
                }

                if (RecordLapseAskLocked(model, holding) is string ask)
                {
                    holding.Lease.Asks[identity] = ask;
                    model = session.Load();
                }
            }

            // An ask whose lapse is over (the lease restored, a live session of its principal holding the step, the step handed over or
            // settled) is withdrawn by its asker, unless a decision answered it.
            var standing = holdings.Select(h => (h.Lease.Session, model.Chain(h.Assignment)[0])).ToHashSet();
            foreach (PresenceLeaseState lease in leases)
            {
                foreach ((string identity, string ask) in lease.Asks.ToArray())
                {
                    if (standing.Contains((lease.Session, identity)))
                    {
                        continue;
                    }

                    if (WithdrawLapseAskLocked(model, ask, lease) is not false)
                    {
                        lease.Asks.Remove(identity);
                        model = session.Load();
                    }
                }

                lease.Holds.RemoveAll(h => OpenHolding(model, lease.Principal, h) is null && !lease.Asks.ContainsKey(h));
            }

            long cutoff = now.Add(-LapsedListed).ToUnixTimeSeconds();
            leases.RemoveAll(l => l.LapsedAt is long lapsed && lapsed <= cutoff && l.Holds.Count == 0 && l.Asks.Count == 0);
            if (PresenceLeaseState.Write(leases) != before)
            {
                WriteLeases(leases);
            }

            if (lapsedNow || holdings.Count > 0)
            {
                MarkEventsDirty();
            }
        }

        PresenceSwept?.Invoke();
    }

    /// <summary>Asks the sweep loop for a sweep soon.</summary>
    private void MarkPresenceDirty()
    {
        try
        {
            if (presenceWork.CurrentCount == 0)
            {
                presenceWork.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // A sweep is already asked for.
        }
        catch (ObjectDisposedException)
        {
            // The runtime is closing.
        }
    }

    /// <summary>
    /// After an interim progress claim by a principal on an attempt, each of its live sessions holds the attempt's assignment. A recorded
    /// attempt or completed phase reaches the leases through the records written here. Caller holds the gate.
    /// </summary>
    private void HoldForSessionsLocked(EstateModel model, string principal, RecordEnvelope assignment)
    {
        if (!PresenceKept || session.IsReadOnly || model.CurrentPrincipal(principal) is not { } who)
        {
            return;
        }

        List<PresenceLeaseState> leases = ReadLeases();
        if (Hold(leases, model.Chain(who)[0], model.Chain(model.Latest(assignment))[0]))
        {
            WriteLeases(leases);
            MarkEventsDirty();
        }
    }

    /// <summary>
    /// Attention for a principal from lapsed sessions holding work, a local observation: for the principal the lapse is asked of, and
    /// for every operator.
    /// </summary>
    private IEnumerable<(string Kind, string Subject, string Why, string State, string Next, string Type, string Id, string? Hash, string? At, AttentionReference? Target, string? Basis)>
        LeaseAttention(EstateModel model, string principal)
    {
        if (!PresenceKept)
        {
            yield break;
        }

        DateTimeOffset now = PresenceClock.GetUtcNow();
        List<PresenceLeaseState> leases = ReadLeases();
        AttributeWrites(model, leases);
        ExpireLeases(leases, now);
        foreach (LapsedHolding holding in LapsedHoldings(model, leases, now))
        {
            if (holding.Asked != principal && !model.HasRole(principal, Roles.Operator))
            {
                continue;
            }

            RecordEnvelope assignment = holding.Assignment;
            string key = WorkView.OrderOfAssignment(model, assignment) is { } order ? EstateModel.BodyString(model.Latest(order), "key") ?? order.Id : "?";
            string agent = PrincipalName(model, holding.Lease.Principal);
            string session = holding.Lease.Label is { } label ? $"{label} ({holding.Lease.Session})" : holding.Lease.Session;
            int after = Routing.ReassignAfterSeconds(model, holding.Goal);
            string what = holding.Reason == "no-attempt"
                ? $"routing gave session {session} of {agent} assignment {assignment.Id} of {key}, and no attempt followed within {PresenceVocabulary.SessionAttemptSeconds} seconds"
                : $"session {session} of {agent} lapsed at {RecordParser.FormatTimestamp(holding.Since)}: its transport renewed its lease no more, and it holds assignment {assignment.Id} of {key}";
            string asked = holding.Ask is string ask
                ? $"Ask {ask} asks {PrincipalName(model, holding.Asked)} whether to hand the step to another principal of its role."
                : $"Still lapsed {after} seconds after that, {PrincipalName(model, holding.Asked)} is asked whether to hand the step to another principal of its role.";
            yield return ("incident", $"session {holding.Lease.Session}: assignment {assignment.Id}",
                $"A local observation of this runtime: {what}. {asked} Presence, liveness and progress claims are separate facts; a heartbeat restores the lease.",
                holding.Ask is null ? "lapsed" : "asked",
                $"Hand the step over: vericairn work takeover {assignment.Id} --by <person> --contributor <principal of its role>{(holding.Ask is null ? string.Empty : " --basis " + holding.Ask)}; or wait for the session, whose heartbeat withdraws the ask.",
                "assignment", assignment.Id, assignment.ComputedHash, RecordParser.FormatTimestamp(holding.Since), null,
                JsonSerializer.Serialize(new { holding.Lease.Session, holding.Reason, since = holding.Since.ToUnixTimeSeconds(), holding.Ask }, ProtocolJson.Options));
        }
    }

    /// <summary>The presence projection of the event journal: one item for each lapse holding work, about the session, to the principal asked.</summary>
    private Dictionary<string, EventItem> PresenceItems(EstateModel model, EventRelations relations)
    {
        var items = new Dictionary<string, EventItem>(StringComparer.Ordinal);
        DateTimeOffset now = PresenceClock.GetUtcNow();
        List<PresenceLeaseState> leases = ReadLeases();
        AttributeWrites(model, leases);
        ExpireLeases(leases, now);
        foreach (LapsedHolding holding in LapsedHoldings(model, leases, now))
        {
            RecordEnvelope assignment = holding.Assignment;
            var sources = new List<EventReference> { new(assignment.Type, assignment.Id, assignment.ComputedHash) };
            if (holding.Ask is string ask && model.ById.TryGetValue(ask, out RecordEnvelope? asked))
            {
                sources.Add(new EventReference(asked.Type, asked.Id, asked.ComputedHash));
            }

            string identity = model.Chain(assignment)[0];
            items[holding.Lease.Session + "/" + identity] = new EventItem("presence", new EventReference("session", journal!.Name("session", holding.Lease.Session)), sources,
                holding.Reason + (holding.Ask is null ? string.Empty : ":asked"), holding.Asked, "incident", false, relations.Of(assignment));
        }

        return items;
    }

    /// <summary>One lapse holding work: the lapsed lease, the open assignment it holds, since when, the goal and who is asked.</summary>
    /// <param name="Lease">The lease.</param>
    /// <param name="Assignment">The assignment at its current revision.</param>
    /// <param name="Since">When the lapse began: the lease's expiry, or for a step routing gave a live session, five minutes after it with no attempt.</param>
    /// <param name="Reason">lapsed, or no-attempt.</param>
    /// <param name="Goal">The goal the step belongs to, when it does.</param>
    /// <param name="Asked">Who is asked: the goal's accountable principal, or without a goal the estate's root operator.</param>
    /// <param name="Ask">The ask recorded about it, when one stands.</param>
    private sealed record LapsedHolding(PresenceLeaseState Lease, RecordEnvelope Assignment, DateTimeOffset Since, string Reason, RecordEnvelope? Goal, string? Asked, string? Ask);

    /// <summary>
    /// Every lapse holding work now: each lapsed lease's open assignments that no live session of the same principal holds, and each
    /// step routing gave a live session with no attempt within five minutes.
    /// </summary>
    private static List<LapsedHolding> LapsedHoldings(EstateModel model, List<PresenceLeaseState> leases, DateTimeOffset now)
    {
        var holdings = new List<LapsedHolding>();
        var live = leases.Where(l => l.Live).SelectMany(l => l.Holds).ToHashSet(StringComparer.Ordinal);
        foreach (PresenceLeaseState lease in leases.OrderBy(l => l.RegisteredAt).ThenBy(l => l.Session, StringComparer.Ordinal))
        {
            foreach (string identity in lease.Holds)
            {
                if (OpenHolding(model, lease.Principal, identity) is not RecordEnvelope assignment)
                {
                    continue;
                }

                if (!lease.Live && !live.Contains(identity))
                {
                    holdings.Add(Holding(lease, assignment, DateTimeOffset.FromUnixTimeSeconds(lease.LapsedAt!.Value), "lapsed", identity));
                }
                else if (lease.Live && EstateModel.BodyString(assignment, "routedWith") == "session" && WorkView.AttemptsOf(model, assignment).Count == 0
                    && now >= assignment.AtTime.AddSeconds(PresenceVocabulary.SessionAttemptSeconds))
                {
                    holdings.Add(Holding(lease, assignment, assignment.AtTime.AddSeconds(PresenceVocabulary.SessionAttemptSeconds), "no-attempt", identity));
                }
            }
        }

        return holdings;

        LapsedHolding Holding(PresenceLeaseState lease, RecordEnvelope assignment, DateTimeOffset since, string reason, string identity)
        {
            RecordEnvelope? order = WorkView.OrderOfAssignment(model, assignment);
            IReadOnlyList<RecordEnvelope> goals = order is null ? [] : Routing.GoalsOf(model, model.Latest(order));
            RecordEnvelope? goal = goals.Count == 0 ? null : goals[0];
            string? ask = lease.Asks.TryGetValue(identity, out string? recorded) && model.ById.TryGetValue(recorded, out RecordEnvelope? record)
                && global::Vericairn.Records.Estate.Asks.IsWaiting(global::Vericairn.Records.Estate.Asks.State(model, record).State) ? recorded : null;
            return new LapsedHolding(lease, assignment, since, reason, goal, LapseAsked(model, goal), ask);
        }
    }

    /// <summary>
    /// The assignment a session's principal holds, when it is open: current, of that principal, still holding its step, and unsettled
    /// (no attempt yet, or its latest attempt without a result); null otherwise.
    /// </summary>
    private static RecordEnvelope? OpenHolding(EstateModel model, string principal, string identity)
    {
        if (!model.ById.TryGetValue(identity, out RecordEnvelope? record) || record.Type != "assignment")
        {
            return null;
        }

        RecordEnvelope assignment = model.Latest(record);
        if (!model.IsCurrent(assignment) || EstateModel.BodyString(assignment, "contributor") is not string contributor
            || model.CurrentPrincipal(contributor) is not { } holder || model.Chain(holder)[0] != principal || Routing.Displaced(model, assignment))
        {
            return null;
        }

        IReadOnlyList<RecordEnvelope> attempts = WorkView.AttemptsOf(model, assignment);
        return attempts.Count == 0 || WorkView.ResultOf(model, attempts[^1]) is null ? assignment : null;
    }

    /// <summary>Who a lapse is asked of: the goal's accountable principal, as the stall rule asks; without a goal, the estate's root operator.</summary>
    private static string? LapseAsked(EstateModel model, RecordEnvelope? goal) =>
        goal is null ? model.RootOperator : GoalRules.AccepterOf(model, goal).Principal ?? Routing.Decider(model, goal);

    /// <summary>
    /// Records the ask a lapse holding work becomes: to the goal's accountable principal, about the assignment, recommending its hand-over;
    /// authored by the principal that made the assignment, else the one asked, else the root operator, whichever this machine can sign
    /// for. Null when none can be recorded, and the attention stays. Caller holds the gate.
    /// </summary>
    private string? RecordLapseAskLocked(EstateModel model, LapsedHolding holding)
    {
        if (holding.Asked is not string to)
        {
            return null;
        }

        RecordEnvelope assignment = holding.Assignment;
        string key = WorkView.OrderOfAssignment(model, assignment) is { } order ? EstateModel.BodyString(model.Latest(order), "key") ?? order.Id : assignment.Id;
        string session = holding.Lease.Label is { } label ? $"{label} ({holding.Lease.Session})" : holding.Lease.Session;
        string agent = PrincipalName(model, holding.Lease.Principal);
        string since = RecordParser.FormatTimestamp(holding.Since);
        var body = new JsonObject
        {
            ["to"] = to,
            ["question"] = Clip(holding.Reason == "no-attempt"
                ? $"Session {session} of {agent} was given assignment {assignment.Id} of {key} and recorded no attempt by {since}. Hand the step to another principal of its role?"
                : $"Session {session} of {agent}, which holds assignment {assignment.Id} of {key}, lapsed at {since}: its transport renewed its lease no more. Hand the step to another principal of its role?", 4000),
            ["recommendation"] = Clip($"Hand the step to another principal the roster names for its role, citing this ask: vericairn work takeover {assignment.Id} --by <person> --contributor <principal> --basis <this ask>. Or wait for the session: a heartbeat from it withdraws this ask.", 4000),
            ["consequence"] = Clip($"{key} waits with the lapsed session; nothing is handed over without a person. After a hand-over, an attempt, progress claim or result from the lapsed session is refused.", 2000),
            ["options"] = new JsonArray("hand it over", "wait for the session"),
            ["state"] = "open",
        };
        if (holding.Goal is not null)
        {
            body["goal"] = holding.Goal.Id;
        }

        foreach (string by in new[] { assignment.By, to, model.RootOperator }.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            WriteOutcome written = WriteLocked(new RecordDraft("ask", by, Element(body), assignment.Reference), model);
            if (written.Ok)
            {
                return written.Written!.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Withdraws a lapse ask by its asker, keeping its question: true when withdrawn, null when nothing is left to withdraw (it is no
    /// longer waiting, or a decision answered it, which automatic cleanup never revises), false when it could not be written now.
    /// Caller holds the gate.
    /// </summary>
    private bool? WithdrawLapseAskLocked(EstateModel model, string ask, PresenceLeaseState lease)
    {
        if (!model.ById.TryGetValue(ask, out RecordEnvelope? record) || record.Type != "ask")
        {
            return null;
        }

        RecordEnvelope latest = model.Latest(record);
        if (!global::Vericairn.Records.Estate.Asks.IsWaiting(global::Vericairn.Records.Estate.Asks.State(model, latest).State) || model.DecisionsAnswering(model.Chain(latest)).Any())
        {
            return null;
        }

        JsonObject body = JsonNode.Parse(latest.Body.GetRawText())!.AsObject();
        body["state"] = "withdrawn";
        body["recommendation"] = Clip($"No longer needed: the lapse of session {lease.Session} is over (its lease was renewed, a live session of its principal holds the step, or the step was handed over or settled).", 4000);
        return WriteLocked(new RecordDraft("ask", latest.By, Element(body), latest.Subject, latest.Id), model).Ok ? true : false;
    }

    /// <summary>Reads the attempts and completed phases written here since the last read into the holdings of their authors' live sessions.</summary>
    private void AttributeWrites(EstateModel model, List<PresenceLeaseState> leases)
    {
        var drained = new List<string>();
        while (presenceWritten.TryDequeue(out string? id))
        {
            drained.Add(id);
        }

        bool changed = false;
        foreach (string id in drained)
        {
            if (!model.ById.TryGetValue(id, out RecordEnvelope? record))
            {
                continue;
            }

            RecordEnvelope? attempt = record.Type == "attempt" ? record : record.Type == "work-progress" && record.Subject is { } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? of) ? of : null;
            if (attempt is null || WorkView.AssignmentOfAttempt(model, attempt) is not RecordEnvelope assignment || model.CurrentPrincipal(record.By) is not { } who)
            {
                continue;
            }

            changed |= Hold(leases, model.Chain(who)[0], model.Chain(model.Latest(assignment))[0]);
        }

        if (changed && !session.IsReadOnly)
        {
            WriteLeases(leases);
        }
    }

    /// <summary>Each live lease of a principal holds an assignment; true when one did not before.</summary>
    private static bool Hold(List<PresenceLeaseState> leases, string principal, string identity)
    {
        bool changed = false;
        foreach (PresenceLeaseState lease in leases.Where(l => l.Live && l.Principal == principal && !l.Holds.Contains(identity, StringComparer.Ordinal)))
        {
            lease.Holds.Add(identity);
            changed = true;
        }

        return changed;
    }

    /// <summary>Marks the live leases that expired lapsed at their expiry; true when one did.</summary>
    private static bool ExpireLeases(List<PresenceLeaseState> leases, DateTimeOffset now)
    {
        bool lapsed = false;
        foreach (PresenceLeaseState lease in leases.Where(l => l.Live && now.ToUnixTimeSeconds() >= l.ExpiresAt))
        {
            lease.LapsedAt = lease.ExpiresAt;
            lapsed = true;
        }

        return lapsed;
    }

    /// <summary>The lease a requester holds with an identity: the principal (and the device) that registered it; null otherwise. Caller holds the gate.</summary>
    private PresenceLeaseState? LeaseOf(string lease, out EstateModel model, out List<PresenceLeaseState> leases)
    {
        model = session.Load();
        leases = [];
        if (session.IsReadOnly || RequestDeviceRefusal(model) is not null)
        {
            return null;
        }

        string? device = requestDevice.Value is { Closed: false } scope ? scope.Device : null;
        leases = ReadLeases();
        PresenceLeaseState? state = leases.FirstOrDefault(l => l.Matches(lease));
        if (state is null || !string.Equals(state.Device, device, StringComparison.Ordinal))
        {
            return null;
        }

        if (device is not null && Standing(model, device) is not { Admission: "admitted" } standing)
        {
            return null;
        }

        return model.CurrentPrincipal(state.Principal) is null ? null : state;
    }

    private PresenceLeaseResponse LeaseNotFound(EstateModel model) =>
        RequestDeviceRefusal(model) is Problem denied ? new PresenceLeaseResponse(null, denied.Message, denied)
            : LeaseProblem("not-found", "No lease this requester holds has that identity: it lapsed and was dropped, was released or was never made. Register the session again.");

    private List<PresenceLeaseState> ReadLeases() => PresenceLeaseState.Read(session.Live.Meta(LeasesKey));

    private void WriteLeases(List<PresenceLeaseState> leases) => session.Live.SetMeta(LeasesKey, PresenceLeaseState.Write(leases));

    private static PresenceLease LeaseView(PresenceLeaseState state, string lease) =>
        new(lease, state.Session, state.Principal, state.Label, state.Roles, state.Capacity, state.LeaseSeconds, Math.Max(1, state.LeaseSeconds / 3), Time(state.RegisteredAt), Time(state.ExpiresAt));

    private static string Time(long seconds) => RecordParser.FormatTimestamp(DateTimeOffset.FromUnixTimeSeconds(seconds));

    private static PresenceLeaseResponse LeaseProblem(string code, string message) => new(null, message, new Problem(code, message, []));

    /// <summary>
    /// The displaced-holder fence at the write boundary (ID-11; design/events-and-presence.md § 10): an attempt on an assignment, or a
    /// result or progress claim on an attempt of one, that no longer holds its step, a later assignment of it to another principal holding
    /// it, is refused, whatever time it states, so a displaced holder's late write never counts. Judged against the records as they stand when it is written, never against its own date; a
    /// result another runtime recorded before the hand-over reached it is labeled displaced instead (WorkView). Null when it may be written.
    /// </summary>
    private static string? DisplacedWriteRefusal(RecordDraft draft, EstateModel model)
    {
        if (draft.Type is not ("attempt" or "result" or "work-progress") || draft.Subject is null || !model.ById.TryGetValue(draft.Subject.Id, out RecordEnvelope? subject))
        {
            return null;
        }

        RecordEnvelope? assignment = draft.Type == "attempt" ? (subject.Type == "assignment" ? subject : null) : subject.Type == "attempt" ? WorkView.AssignmentOfAttempt(model, subject) : null;
        if (assignment is null || Routing.DisplacedBy(model, assignment) is not RecordEnvelope holder)
        {
            return null;
        }

        // Only the displaced holder's own write is fenced here; anyone else's is refused by its record's own rule, which names why.
        if (model.CurrentPrincipal(draft.By) is not { } author || model.CurrentPrincipal(EstateModel.BodyString(model.Latest(assignment), "contributor")) is not { } contributor
            || model.Chain(author)[0] != model.Chain(contributor)[0])
        {
            return null;
        }

        string what = draft.Type switch { "attempt" => "an attempt", "result" => "a result", _ => "a progress claim" };
        string key = WorkView.OrderOfAssignment(model, assignment) is { } order ? EstateModel.BodyString(model.Latest(order), "key") ?? order.Id : assignment.Id;
        return $"Assignment {assignment.Id} no longer holds its step of {key}: assignment {holder.Id} of {PrincipalName(model, EstateModel.BodyString(holder, "contributor"))} holds it now. Only the step's current holder records {what}, whatever time it states; nothing was written.";
    }

    /// <summary>Why a registration's label, roles, capacity or lease length is refused; null when they are within their bounds.</summary>
    private static string? LeaseRequestRefusal(PresenceRegisterRequest request)
    {
        if (request.Label is string label && (label.Trim().Length > PresenceVocabulary.MaxLabelLength || label.Any(c => char.IsControl(c) || c is '/' or '\\') || label.TrimStart().StartsWith('~')))
        {
            return $"A session's label is at most {PresenceVocabulary.MaxLabelLength} characters and names no machine path: no slash, backslash, home directory or control character.";
        }

        if (request.Roles is { } roles && (roles.Count > PresenceVocabulary.MaxRoles || roles.Any(r => r is null || r.Length > 60 || !Patterns.Slug.IsMatch(r))))
        {
            return $"A session offers at most {PresenceVocabulary.MaxRoles} roles, each a roster role's name: lowercase letters, digits and hyphens.";
        }

        if (request.Capacity is int capacity && capacity is < 1 or > PresenceVocabulary.MaxCapacity)
        {
            return $"A session's capacity is 1 to {PresenceVocabulary.MaxCapacity} steps at once.";
        }

        return request.LeaseSeconds is int seconds && seconds is < PresenceVocabulary.MinLeaseSeconds or > PresenceVocabulary.MaxLeaseSeconds
            ? $"A lease lasts {PresenceVocabulary.MinLeaseSeconds} to {PresenceVocabulary.MaxLeaseSeconds} seconds without a heartbeat."
            : null;
    }
}
