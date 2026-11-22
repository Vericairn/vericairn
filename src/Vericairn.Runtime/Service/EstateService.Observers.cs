// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Xml;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Store;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Automated observers (OPS-20, decision 01M7DH28FXQ8JVT7BRVCEBNBBT). A quiet system does not prove that a check succeeded: CI on the main
/// line, a scheduled workflow, an intake watcher or a status refresher can stop without anyone noticing, and an exhausted quota or a
/// disabled schedule reads as silence. So the operator declares each observer on the machine its reports reach, with the interval it
/// reports at and the grace its silence is allowed, and each report says whether it observed and passed, observed a failure, or could
/// not observe and why. Silence past the interval plus the grace, a failure and not observing are each a failed check in the operator's
/// attention; only a recent report that it observed and passed is quiet. Declarations and reports are this machine's, kept beside the
/// clone's live store, never records: status JSON stays the same on every machine, and human status shows them as this machine's.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>What an observer watches: CI on the main line, a scheduled workflow, an intake watcher, a status refresher, or another.</summary>
    internal static readonly string[] ObserverKinds = ["ci", "scheduled-workflow", "intake", "status-refresh", "other"];

    /// <summary>What a report says: it observed and passed, it observed a failure, or it could not observe.</summary>
    internal static readonly string[] ObserverOutcomes = ["passed", "failed", "not-observing"];

    /// <summary>Why an observer could not observe: its quota is exhausted, its schedule is disabled, or it was otherwise unavailable.</summary>
    internal static readonly string[] ObserverReasons = ["quota-exhausted", "schedule-disabled", "unavailable"];

    /// <summary>The shortest interval an observer declares.</summary>
    internal const int MinimumObserverInterval = 60;

    /// <summary>The longest interval an observer declares: 31 days, for a monthly schedule.</summary>
    internal const int MaximumObserverInterval = 31 * 86400;

    /// <summary>The longest grace an observer's silence is allowed: 7 days.</summary>
    internal const int MaximumObserverGrace = 7 * 86400;

    private const int ObserverTextLimit = 300;

    // How far ahead of the runtime's clock a report may say it was made: clocks of reporters on this machine differ by little.
    private static readonly TimeSpan ObserverSkew = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public ObserversResponse Observers()
    {
        lock (gate)
        {
            DateTimeOffset now = PolicyClock.GetUtcNow();
            (IReadOnlyList<ObserverStore.Entry>? entries, Problem? problem) = ReadObservers();
            if (problem is not null) return ObserverProblem(problem, now);
            return ObserverAnswer(entries!, now, null);
        }
    }

    /// <inheritdoc />
    public ObserversResponse ObserverDeclare(ObserverDeclareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            DateTimeOffset now = PolicyClock.GetUtcNow();
            if (ObserverWriteRefusal(request.By, operatorOnly: true) is { } denied) return ObserverProblem(denied, now);
            int grace = request.GraceSeconds ?? request.IntervalSeconds / 4;
            if (request.Name is null || request.Name.Length > 64 || !Patterns.Slug.IsMatch(request.Name))
                return ObserverProblem(new("refused", "Name the observer by a lower-case kebab-case name of at most 64 characters.", []), now);
            if (!ObserverKinds.Contains(request.Kind, StringComparer.Ordinal))
                return ObserverProblem(new("refused", "An observer's kind is one of " + string.Join(", ", ObserverKinds) + ".", []), now);
            if (request.IntervalSeconds is < MinimumObserverInterval or > MaximumObserverInterval)
                return ObserverProblem(new("refused", $"An observer reports at an interval from {MinimumObserverInterval} seconds to 31 days.", []), now);
            if (grace is < 0 or > MaximumObserverGrace)
                return ObserverProblem(new("refused", "An observer's grace is from none to 7 days.", []), now);
            if (TextRefusal(request.Summary, "summary") is { } summary) return ObserverProblem(summary, now);
            (IReadOnlyList<ObserverStore.Entry>? entries, Problem? unreadable) = ReadObservers();
            if (unreadable is not null) return ObserverProblem(unreadable, now);
            ObserverStore.Entry? kept = entries!.FirstOrDefault(o => o.Name == request.Name);
            if (kept is null && entries!.Count >= ObserverStore.MaxObservers)
                return ObserverProblem(new("refused", $"At most {ObserverStore.MaxObservers} observers are declared on one machine for one estate; withdraw one first.", []), now);
            // A changed declaration starts its expectation again from now; the latest report it already had still counts.
            var entry = new ObserverStore.Entry(request.Name, request.Kind, request.IntervalSeconds, grace, request.Summary, RecordParser.FormatTimestamp(now), request.By, kept?.LastReport);
            if (WriteObservers([.. entries!.Where(o => o.Name != request.Name), entry]) is { } failed) return ObserverProblem(failed, now);
            return ObserverAnswer([.. entries!.Where(o => o.Name != request.Name), entry], now,
                $"Observer {request.Name} is {(kept is null ? "declared" : "declared again")} on this machine: it reports every {Duration(request.IntervalSeconds)}, and its silence past a further {Duration(grace)} is attention.");
        }
    }

    /// <inheritdoc />
    public ObserversResponse ObserverReport(ObserverReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            DateTimeOffset now = PolicyClock.GetUtcNow();
            if (ObserverWriteRefusal(request.By, operatorOnly: false) is { } denied) return ObserverProblem(denied, now);
            if (!ObserverOutcomes.Contains(request.Outcome, StringComparer.Ordinal))
                return ObserverProblem(new("refused", "A report's outcome is passed, failed or not-observing.", []), now);
            if (request.Outcome == "not-observing" ? !ObserverReasons.Contains(request.Reason, StringComparer.Ordinal) : request.Reason is not null)
                return ObserverProblem(new("refused", "A report that the observer could not observe names why: " + string.Join(", ", ObserverReasons) + "; a passed or failed report names no reason.", []), now);
            if (TextRefusal(request.Detail, "detail") is { } detail) return ObserverProblem(detail, now);
            DateTimeOffset at = now;
            if (request.At is not null && (!ReadAt(request.At, out at) || at > now + ObserverSkew))
                return ObserverProblem(new("refused", "A report's time is an RFC 3339 UTC timestamp to the second, such as 2026-11-21T09:30:00Z, no later than now.", []), now);
            (IReadOnlyList<ObserverStore.Entry>? entries, Problem? unreadable) = ReadObservers();
            if (unreadable is not null) return ObserverProblem(unreadable, now);
            ObserverStore.Entry? kept = entries!.FirstOrDefault(o => o.Name == request.Name);
            if (kept is null)
                return ObserverProblem(new("not-found", $"No observer named {request.Name} is declared on this machine for this estate; the operator declares it first with vericairn observers declare.", []), now);
            if (kept.LastReport is { } latest && ReadAt(latest.At, out DateTimeOffset previous) && at < previous)
                return ObserverAnswer(entries!, now, $"This report of observer {request.Name} was made at {RecordParser.FormatTimestamp(at)}, before its latest report at {latest.At}, which stands.");
            var report = new ObserverStore.Report(RecordParser.FormatTimestamp(at), request.Outcome, request.Reason, request.By, request.Detail, RecordParser.FormatTimestamp(now));
            ObserverStore.Entry[] updated = [.. entries!.Where(o => o.Name != request.Name), kept with { LastReport = report }];
            if (WriteObservers(updated) is { } failed) return ObserverProblem(failed, now);
            return ObserverAnswer(updated, now, $"The report of observer {request.Name} ({request.Outcome}{(request.Reason is null ? string.Empty : ", " + request.Reason)}) at {report.At} is kept on this machine.");
        }
    }

    /// <inheritdoc />
    public ObserversResponse ObserverWithdraw(ObserverWithdrawRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            DateTimeOffset now = PolicyClock.GetUtcNow();
            if (ObserverWriteRefusal(request.By, operatorOnly: true) is { } denied) return ObserverProblem(denied, now);
            if (string.IsNullOrWhiteSpace(request.Reason)) return ObserverProblem(new("refused", "Withdrawing an observer gives the reason, in at most 300 characters.", []), now);
            if (TextRefusal(request.Reason, "reason") is { } reason) return ObserverProblem(reason, now);
            (IReadOnlyList<ObserverStore.Entry>? entries, Problem? unreadable) = ReadObservers();
            if (unreadable is not null) return ObserverProblem(unreadable, now);
            if (!entries!.Any(o => o.Name == request.Name))
                return ObserverProblem(new("not-found", $"No observer named {request.Name} is declared on this machine for this estate.", []), now);
            ObserverStore.Entry[] remaining = [.. entries!.Where(o => o.Name != request.Name)];
            if (WriteObservers(remaining) is { } failed) return ObserverProblem(failed, now);
            return ObserverAnswer(remaining, now, $"Observer {request.Name} is withdrawn on this machine ({request.Reason.Trim()}); its silence is no longer attention.");
        }
    }

    /// <summary>
    /// An observer's state as of a time: awaiting until its first report is due, observing while its latest report passed and is recent
    /// enough, failed or not-observing as its latest report says, and silent once no report came within its interval and grace. Silence
    /// counts from the latest report, or from the declaration when none came, so an observer that never reported is silent too.
    /// </summary>
    internal static ObserverRow EvaluateObserver(ObserverStore.Entry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ObserverStore.Report? last = entry.LastReport;
        DateTimeOffset from = last is not null && ReadAt(last.At, out DateTimeOffset reported) ? reported
            : ReadAt(entry.DeclaredAt, out DateTimeOffset declared) ? declared : DateTimeOffset.MinValue;
        DateTimeOffset expected = from == DateTimeOffset.MinValue ? from : from.AddSeconds((long)entry.IntervalSeconds + entry.GraceSeconds);
        string cadence = $"every {Duration(entry.IntervalSeconds)}, with {Duration(entry.GraceSeconds)} of grace";
        (string state, string why) = (last, now > expected) switch
        {
            (null, false) => ("awaiting", $"Declared at {entry.DeclaredAt} to report {cadence}; its first report is due by {RecordParser.FormatTimestamp(expected)}."),
            (null, true) => ("silent", $"It has not reported since it was declared at {entry.DeclaredAt}; it reports {cadence}, so a report was due by {Stamp(expected)}."),
            (_, true) => ("silent", $"It has not reported since {last!.At}, when it reported {Outcome(last)}; it reports {cadence}, so a report was due by {Stamp(expected)}."),
            ({ Outcome: "passed" }, false) => ("observing", $"Its latest report, at {last!.At}, passed; the next is due by {RecordParser.FormatTimestamp(expected)}."),
            ({ Outcome: "failed" }, false) => ("failed", $"Its latest report, at {last!.At}, observed a failure{Detail(last)}."),
            _ => ("not-observing", $"Its latest report, at {last!.At}, says it could not observe: {ReasonText(last.Reason)}{Detail(last)}. Not observing is never a pass."),
        };
        ObserverReport? shown = last is null ? null : new(last.At, last.Outcome, last.Reason, last.By, last.Detail, last.ReceivedAt);
        return new(entry.Name, entry.Kind, entry.IntervalSeconds, entry.GraceSeconds, entry.Summary, entry.DeclaredAt, entry.DeclaredBy, shown, state,
            Stamp(expected), state is not ("awaiting" or "observing"), why);
    }

    /// <summary>Attention for the operator: each observer that is silent, failed or not observing, and declarations this machine cannot read.</summary>
    private IEnumerable<(string Kind, string Subject, string Why, string State, string Next, string Type, string Id, string? Hash, string? At, AttentionReference? Target, string Basis)>
        ObserverNotices(EstateModel model, string principal)
    {
        if (!model.HasRole(principal, Roles.Operator)) yield break;
        (IReadOnlyList<ObserverStore.Entry>? entries, Problem? problem) = ReadObservers();
        if (problem is not null)
        {
            yield return ("failed check", "observers on this machine", problem.Message + " Until they read again, no declared observer's silence can be seen here.",
                "unreadable", "Restore or remove the observers file beside this clone's live store, then declare the observers again.", "observers", "store", null, null, null, problem.Message);
            yield break;
        }

        DateTimeOffset now = PolicyClock.GetUtcNow();
        foreach (ObserverStore.Entry entry in entries!)
        {
            ObserverRow row = EvaluateObserver(entry, now);
            if (!row.Attention) continue;
            string next = row.State switch
            {
                "silent" => $"Find why observer {entry.Name} stopped reporting and restore it; its next report clears this. If it was retired, withdraw it: vericairn observers withdraw {entry.Name} --reason <why> --by <operator>",
                "failed" => $"Inspect what observer {entry.Name} observed failing; its next passing report clears this.",
                _ => $"Restore observer {entry.Name}: {ReasonText(entry.LastReport?.Reason)}. Only a report that it observed and passed clears this.",
            };
            // Elapsed silence advances on every read; only a new report, outcome or declaration renews the notice.
            string basis = string.Join(":", entry.Name, row.State, entry.DeclaredAt, entry.IntervalSeconds, entry.GraceSeconds, entry.LastReport?.At ?? "none",
                entry.LastReport?.Outcome ?? "none", entry.LastReport?.Reason ?? "none");
            yield return ("failed check", "observer " + entry.Name, $"Observer {entry.Name} ({entry.Kind}) is {row.State.Replace('-', ' ')}: {row.Why}", row.State, next,
                "observer", entry.Name, null, entry.LastReport?.At ?? entry.DeclaredAt, null, basis);
        }
    }

    private (IReadOnlyList<ObserverStore.Entry>? Entries, Problem? Problem) ReadObservers()
    {
        try
        {
            IReadOnlyList<ObserverStore.Entry> entries = ObserversOfThisClone().Read();
            // Only closed forms are shown or judged: an entry edited outside the runtime is refused whole rather than read in part.
            if (entries.Any(e => e.Name is null || !Patterns.Slug.IsMatch(e.Name) || !ObserverKinds.Contains(e.Kind, StringComparer.Ordinal)
                || e.IntervalSeconds is < MinimumObserverInterval or > MaximumObserverInterval || e.GraceSeconds is < 0 or > MaximumObserverGrace
                || !RecordParser.IsTimestamp(e.DeclaredAt) || !Ulid.IsValid(e.DeclaredBy ?? string.Empty)
                || e.LastReport is { } r && (!RecordParser.IsTimestamp(r.At) || !ObserverOutcomes.Contains(r.Outcome, StringComparer.Ordinal) || !Ulid.IsValid(r.By ?? string.Empty))))
                return (null, new Problem("unavailable", "This machine's declared observers are malformed.", []));
            return (entries, null);
        }
        catch (Exception exception) when (ObserverStore.Unavailable(exception))
        {
            return (null, new Problem("unavailable", "This machine's declared observers cannot be read: " + ObserverStore.Describe(exception) + ".", []));
        }
    }

    private Problem? WriteObservers(IReadOnlyList<ObserverStore.Entry> entries)
    {
        try
        {
            ObserversOfThisClone().Write(entries);
        }
        catch (Exception exception) when (ObserverStore.Unavailable(exception))
        {
            return new Problem("unavailable", "This machine's observers could not be kept: " + ObserverStore.Describe(exception) + ". Nothing changed.", []);
        }

        // The operator's attention changes with no record written; ask the event journal to derive it now.
        MarkEventsDirty();
        return null;
    }

    private ObserverStore ObserversOfThisClone() => new(session.Root, session.Manifest.Estate, session.Manifest.Profile, session.Home);

    private Problem? ObserverWriteRefusal(string? by, bool operatorOnly)
    {
        if (session.IsReadOnly) return new("denied", "This session is read-only; nothing was kept.", []);
        EstateModel model = session.Load();
        if (RequestDeviceRefusal(model) is { } denied) return denied;
        if (by is null || !Ulid.IsValid(by) || model.CurrentPrincipal(by) is null)
            return new("refused", "Name the principal acting by its ULID, a principal of this estate.", []);
        return operatorOnly && !model.HasRole(by, Roles.Operator)
            ? new("denied", "Observers are declared and withdrawn by the operator only; the observer itself reports.", []) : null;
    }

    private static Problem? TextRefusal(string? text, string field) =>
        text is not null && (text.Length is 0 or > ObserverTextLimit || text.Any(char.IsControl))
            ? new Problem("refused", $"An observer's {field} is at most {ObserverTextLimit} characters on one line.", []) : null;

    private static ObserversResponse ObserverAnswer(IReadOnlyList<ObserverStore.Entry> entries, DateTimeOffset now, string? message)
    {
        ObserverRow[] rows = [.. entries.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => EvaluateObserver(e, now))];
        int attention = rows.Count(r => r.Attention);
        string summary = rows.Length == 0 ? "No observer is declared on this machine for this estate."
            : $"{rows.Length} observer{(rows.Length == 1 ? " is" : "s are")} declared on this machine; {(attention == 0 ? "none needs" : attention + (attention == 1 ? " needs" : " need"))} attention.";
        return new(true, message is null ? summary : message + " " + summary, RecordParser.FormatTimestamp(now), rows);
    }

    private static ObserversResponse ObserverProblem(Problem problem, DateTimeOffset now) => new(false, problem.Message, RecordParser.FormatTimestamp(now), [], problem);

    /// <summary>A number of seconds as an ISO 8601 duration, as the command line takes it: PT1H, P1D, PT15M.</summary>
    internal static string Duration(int seconds) => XmlConvert.ToString(TimeSpan.FromSeconds(seconds));

    private static string Stamp(DateTimeOffset at) => at == DateTimeOffset.MinValue ? "unknown" : RecordParser.FormatTimestamp(at);

    private static string Outcome(ObserverStore.Report report) => report.Outcome == "not-observing" ? "that it could not observe (" + report.Reason + ")" : report.Outcome;

    private static string Detail(ObserverStore.Report report) => report.Detail is null ? string.Empty : ": " + report.Detail;

    private static string ReasonText(string? reason) => reason switch
    {
        "quota-exhausted" => "its quota is exhausted",
        "schedule-disabled" => "its schedule is disabled",
        _ => "it was unavailable",
    };
}
