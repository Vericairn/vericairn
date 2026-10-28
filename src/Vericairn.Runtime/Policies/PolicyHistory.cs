// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.CompilerServices;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

// These indexes belong to one immutable evaluation model. A new basis gets new indexes, including after a removal,
// reconciliation, or changed signature. No result or authority judgment is carried between record snapshots.
internal sealed class PolicyHistory
{
    private static readonly ConditionalWeakTable<EstateModel, PolicyHistory> Histories = new();
    private readonly EstateModel model;
    private readonly ILookup<string, RecordEnvelope> orders;
    private readonly ILookup<(string Type, string Order), RecordEnvelope> aboutOrders;
    private readonly ILookup<string, RecordEnvelope> resultAcceptances;
    private readonly ILookup<(string Product, string? Version), RecordEnvelope> releases;
    private readonly ILookup<(string Order, DateTimeOffset At), RecordEnvelope> workEvents;

    private PolicyHistory(EstateModel model)
    {
        this.model = model;
        orders = model.OfType("work-order").ToLookup(r => PolicyWork.Identity(model, r), StringComparer.Ordinal);
        aboutOrders = model.OfType("work-status").Concat(model.OfType("assignment"))
            .Where(r => r.Subject is { } s && model.ById.ContainsKey(s.Id))
            .ToLookup(r => (r.Type, PolicyWork.Identity(model, model.ById[r.Subject!.Id])));
        resultAcceptances = model.OfType("acceptance").Where(a => a.Subject is { Type: "result" })
            .Select(a => (Acceptance: a, Order: model.ById.TryGetValue(a.Subject!.Id, out RecordEnvelope? result)
                ? WorkView.OrderOfResult(model, result) : null))
            .Where(x => x.Order is not null).ToLookup(x => x.Order!.Id, x => x.Acceptance, StringComparer.Ordinal);
        releases = model.OfType("release").Where(r => r.Subject is { Type: "product" })
            .ToLookup(r => (LivingDocuments.ProductIdentity(model, r.Subject!.Id), EstateModel.BodyString(r, "version")));
        workEvents = model.OfType("assignment").Concat(model.OfType("attempt")).Concat(model.OfType("result"))
            .Select(r => (Record: r, Order: PolicyWork.OrderOfEvent(model, r))).Where(x => x.Order is not null)
            .ToLookup(x => (x.Order!.Id, x.Record.AtTime), x => x.Record);
    }

    internal static PolicyHistory For(EstateModel model) => Histories.GetValue(model, m => new(m));

    internal IEnumerable<RecordEnvelope> Revisions(string identity) => orders[identity];

    internal IEnumerable<RecordEnvelope> AboutOrder(RecordEnvelope order, string type)
    {
        IReadOnlyList<string> chain = model.Chain(order);
        return aboutOrders[(type, chain[0])].Where(r => chain.Contains(r.Subject!.Id, StringComparer.Ordinal));
    }

    internal IEnumerable<RecordEnvelope> ResultAcceptances(RecordEnvelope order) => resultAcceptances[order.Id];

    internal IEnumerable<RecordEnvelope> Releases(string? product, string? version) => product is null ? []
        : releases[(LivingDocuments.ProductIdentity(model, product), version)];

    // PolicyBasis orders equal-time records by id, then hash. Selecting the first related event preserves that tie break
    // without grouping or walking unrelated records at the same instant.
    internal RecordEnvelope? EventAt(RecordEnvelope order, DateTimeOffset at) => workEvents[(order.Id, at)]
        .OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.ComputedHash, StringComparer.Ordinal).FirstOrDefault();
}
