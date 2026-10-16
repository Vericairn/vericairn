// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// What a release contained, derived from the records of its window: the one derivation that <c>vericairn docs notes</c> renders as a
/// release's notes and the progress report lists for each published release. A window holds the records dated after its beginning and
/// up to its end, so consecutive windows share no record and leave none out. A result of the window is part of what the release is known
/// to contain when its exact revision or its work order's current revision is accepted. Superseded results are omitted;
/// any other current result is a claim to inspect.
/// </summary>
public static class ReleaseContents
{
    /// <summary>A current result of a window whose exact revision or current work order is accepted.</summary>
    /// <param name="Result">The result.</param>
    /// <param name="Acceptance">The earliest current acceptance of its exact revision, or otherwise of its work order's current revision.</param>
    /// <param name="Order">The current revision of the work order it is toward; null when its attempt and assignment chain is broken.</param>
    public sealed record Accepted(RecordEnvelope Result, RecordEnvelope Acceptance, RecordEnvelope? Order);

    /// <summary>A published release and the window of records it contains.</summary>
    /// <param name="Release">The published release record.</param>
    /// <param name="Previous">The publication before it of the same product, where its window begins; null for the product's first, whose window begins with the estate.</param>
    /// <param name="Window">The records of its window, by time and then by identifier.</param>
    public sealed record Published(RecordEnvelope Release, RecordEnvelope? Previous, IReadOnlyList<RecordEnvelope> Window);

    /// <summary>
    /// The records dated after <paramref name="after"/> and up to <paramref name="upTo"/>, by time and then by identifier; with no
    /// beginning, every record up to the end.
    /// </summary>
    public static IReadOnlyList<RecordEnvelope> Window(EstateModel model, string? after, string upTo)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(upTo);
        return model.Records
            .Where(r => (after is null || string.CompareOrdinal(r.At, after) > 0) && string.CompareOrdinal(r.At, upTo) <= 0)
            .OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The current results of a window: those whose exact revision or current work order is accepted, and the others, each in the window's order.</summary>
    public static (IReadOnlyList<Accepted> Accepted, IReadOnlyList<RecordEnvelope> NotAccepted) Results(EstateModel model, IReadOnlyList<RecordEnvelope> window)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(window);
        var acceptances = new Dictionary<(string Id, string? Hash), RecordEnvelope>();
        foreach (RecordEnvelope acceptance in model.Current("acceptance").Where(a => a.Subject is { Type: "result" or "work-order" }).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            acceptances.TryAdd((acceptance.Subject!.Id, acceptance.Subject.Hash), acceptance);
        }

        var accepted = new List<Accepted>();
        var notAccepted = new List<RecordEnvelope>();
        foreach (RecordEnvelope result in window.Where(r => r.Type == "result" && model.IsCurrent(r)))
        {
            RecordEnvelope? order = OrderOf(model, result);
            RecordEnvelope? current = order is null ? null : model.Latest(order);
            if (acceptances.TryGetValue((result.Id, result.ComputedHash), out RecordEnvelope? acceptance) ||
                (current is not null && acceptances.TryGetValue((current.Id, current.ComputedHash), out acceptance)))
            {
                accepted.Add(new Accepted(result, acceptance, current));
            }
            else
            {
                notAccepted.Add(result);
            }
        }

        return (accepted, notAccepted);
    }

    /// <summary>The work order a result is toward, following its attempt and that attempt's assignment, at the revision they name; null when the chain is broken.</summary>
    public static RecordEnvelope? OrderOf(EstateModel model, RecordEnvelope result)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(result);
        RecordEnvelope? attempt = result.Subject is null ? null : model.ById.GetValueOrDefault(result.Subject.Id);
        RecordEnvelope? assignment = attempt?.Subject is null ? null : model.ById.GetValueOrDefault(attempt.Subject.Id);
        return assignment?.Subject is null ? null : model.ById.GetValueOrDefault(assignment.Subject.Id);
    }

    /// <summary>
    /// Every current published release, by product and then by the time of its publication record, with its window: from the publication
    /// before it of the same product (any revision of the product record) up to its own, which is where <c>vericairn docs notes</c>
    /// begins when no documentation set of the earlier release is beside the notes. A product's first window begins with the estate.
    /// </summary>
    public static IReadOnlyList<Published> PublishedReleases(EstateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var releases = new List<Published>();
        foreach (IGrouping<string, RecordEnvelope> product in model.Current("release")
            .Where(r => EstateModel.BodyString(r, "status") == "published")
            .GroupBy(r => r.Subject is null ? string.Empty : LivingDocuments.ProductIdentity(model, r.Subject.Id), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            RecordEnvelope? previous = null;
            foreach (RecordEnvelope release in product.OrderBy(r => r.At, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
            {
                releases.Add(new Published(release, previous, Window(model, previous?.At, release.At)));
                previous = release;
            }
        }

        return releases;
    }
}
