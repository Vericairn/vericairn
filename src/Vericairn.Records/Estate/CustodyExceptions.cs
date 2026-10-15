// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// The sponsor's exceptions to the custody precondition (the estate's design/goals.md § 10). Unattended work of a goal is refused on a
/// machine where a key of an operator or of the goal's sponsor signs without its owner's presence, unless the sponsor recorded an
/// exception for that goal: a custody-exception record about the goal's exact revision, by who decides for the goal, with its reason
/// and an expiry not yet reached. The guard and every surface that would show its refusal read this one rule.
/// </summary>
public static class CustodyExceptions
{
    /// <summary>
    /// The custody exception in force for a goal at a time: a current one about the goal's exact current revision, recorded by who
    /// decides for the goal, whose expiry is later; the latest recorded when several are; null when none is.
    /// </summary>
    public static RecordEnvelope? InForce(EstateModel model, RecordEnvelope goal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        RecordEnvelope current = model.Latest(goal);
        return model.Current("custody-exception")
            .Where(e => Covers(model, e, current, at))
            .OrderBy(e => e.At, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// True when a custody exception covers a goal revision at a time: it is about that exact revision, its author decides for the
    /// goal, and it had not expired then.
    /// </summary>
    public static bool Covers(EstateModel model, RecordEnvelope exception, RecordEnvelope goal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(goal);
        return exception.Type == "custody-exception"
            && exception.Subject?.Id == goal.Id && exception.Subject.Hash == goal.ComputedHash
            && GoalRules.AccepterOf(model, goal).Decides(model, exception.By)
            && Expires(exception) is DateTimeOffset expires && at < expires;
    }

    /// <summary>
    /// True when a custody exception covered a goal revision when a record was written, as the validation of a run's intent judges it
    /// at any later time: it covered that revision then (<see cref="Covers"/>), and no revision of the exception had been recorded
    /// before the record, since a revision replaces what the exception says. A run recorded under an exception stays valid when the
    /// exception is revised or expires afterwards.
    /// </summary>
    public static bool CoveredWhen(EstateModel model, RecordEnvelope exception, RecordEnvelope goal, RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(record);
        bool revised = model.SupersededBy.TryGetValue(exception.Id, out string? next) && model.ById.TryGetValue(next, out RecordEnvelope? successor) && GoalRules.Before(successor, record);
        return !revised && Covers(model, exception, goal, record.AtTime);
    }

    /// <summary>When an exception expires, or null when it states no readable expiry.</summary>
    public static DateTimeOffset? Expires(RecordEnvelope exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return EstateModel.BodyString(exception, "expires") is string expires && DateTimeOffset.TryParseExact(expires, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at) ? at : null;
    }

    /// <summary>An exception in words: whose, until when, and why.</summary>
    public static string Describe(EstateModel model, RecordEnvelope exception)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(exception);
        string name = model.CurrentPrincipal(exception.By) is RecordEnvelope who ? EstateModel.BodyString(who, "name") ?? exception.By : exception.By;
        return $"the custody exception {exception.Id} that {name} recorded for the goal, in force until {EstateModel.BodyString(exception, "expires")}: {EstateModel.BodyString(exception, "reason")}";
    }
}
