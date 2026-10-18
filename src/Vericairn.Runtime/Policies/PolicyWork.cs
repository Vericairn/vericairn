// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal static class PolicyWork
{
    internal static string Identity(EstateModel model, RecordEnvelope record) => PolicyRecords.Identity(model, record);
    internal static PolicyTarget Target(EstateModel model, RecordEnvelope record) => new("record", Identity(model, record), record.Reference);
    internal static bool CurrentAt(EstateModel model, RecordEnvelope record, DateTimeOffset at) => record.AtTime <= at &&
        (!model.SupersededBy.TryGetValue(record.Id, out string? next) || !model.ById.TryGetValue(next, out RecordEnvelope? successor) || successor.AtTime > at);

    internal static IEnumerable<RecordEnvelope> Orders(PolicyRuleContext context)
    {
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        string kind = Choice(selection, "kind", "estate", "product", "work-orders");
        if (kind == "work-orders")
        {
            foreach (string id in Strings(selection, "workOrders"))
                if (!context.Model.ById.TryGetValue(id, out RecordEnvelope? order) || order.Type != "work-order") throw new PolicyInputException("A selected work order is missing: " + id + ".");
        }
        if (kind == "product" && (!context.Model.ById.TryGetValue(String(selection, "product"), out RecordEnvelope? product) || product.Type != "product"))
            throw new PolicyInputException("The selected product is missing.");
        return context.Model.Current("work-order").Where(o => Selected(context, o));
    }

    internal static bool Selected(PolicyRuleContext context, RecordEnvelope record)
    {
        EstateModel model = context.Model;
        JsonElement selection = Get(context.Policy.Body, "appliesTo");
        string? kind = Text(selection, "kind");
        if (kind == "estate") return true;
        RecordEnvelope? order = record.Type switch
        {
            "work-order" => model.Latest(record),
            "assignment" => WorkView.OrderOfAssignment(model, record),
            "result" => WorkView.OrderOfResult(model, record),
            _ => record.Subject is { Type: "work-order" } reference && model.ById.TryGetValue(reference.Id, out RecordEnvelope? found) ? model.Latest(found) : null,
        };
        if (kind == "work-orders") return order is not null && PolicyRecords.Contains(Get(selection, "workOrders"), Identity(model, order));
        if (kind != "product") throw new PolicyParameterException("This record kind requires an estate, product or work-orders selection.");
        string product = String(selection, "product");
        if (record.Subject is { Type: "product" } subject && subject.Id == product) return true;
        if (order is null) return false;
        order = model.Latest(order);
        JsonElement placement = Get(order.Body, "placement");
        if (Text(Get(order.Body, "releasePlan"), "product") == product || Text(placement, "kind") == "release" && Text(placement, "product") == product) return true;
        return Text(placement, "kind") == "backlog" && PlacementRegistries(context).SelectMany(p => Items(Get(p.Parameters, "backlogs")))
            .Any(b => Text(b, "key") == Text(placement, "key") && Text(b, "product") == product);
    }

    internal static IEnumerable<(RecordEnvelope Policy, JsonElement Parameters)> PlacementRegistries(PolicyRuleContext context) =>
        context.Model.Current("estate-policy").Where(p => EstateModel.BodyString(p, "state") == "active" && DutyRecords.Time(EstateModel.BodyString(p, "startsAt")) <= context.Basis.Time)
            .SelectMany(p => Items(Get(p.Body, "rules")).Where(r => Text(r, "kind") == FoundationPolicyKinds.Placement && Get(r, "version").TryGetInt64Safe(out long v) && v == 1)
                .Select(r => (p, Get(r, "parameters"))));

    internal static RecordEnvelope? Status(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        IReadOnlyList<string> lineage = model.Chain(order);
        RecordEnvelope[] states = model.OfType("work-status").Where(s => s.Subject is not null && lineage.Contains(s.Subject.Id, StringComparer.Ordinal) && CurrentAt(model, s, at))
            .OrderBy(s => s.At, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (states.Length == 0) return null;
        RecordEnvelope latest = states[^1];
        if (states.Where(s => s.At == latest.At).Select(s => EstateModel.BodyString(s, "status")).Distinct(StringComparer.Ordinal).Count() > 1)
            throw new PolicyInputException("Conflicting work states at the same recorded time.");
        return latest;
    }

    internal static bool Accepted(EstateModel model, RecordEnvelope order, DateTimeOffset at) =>
        model.OfType("acceptance").Any(a => CurrentAt(model, a, at) && a.Subject is { } subject &&
            (subject == order.Reference || model.ById.TryGetValue(subject.Id, out RecordEnvelope? result) && result.Type == "result" &&
                subject == result.Reference && CurrentAt(model, result, at) && WorkView.OrderOfResult(model, result)?.Id == order.Id));

    internal static bool Planned(EstateModel model, RecordEnvelope order, DateTimeOffset at) =>
        !Accepted(model, order, at) && (Status(model, order, at) is not { } status || EstateModel.BodyString(status, "status") == "planned");

    internal static (string Verdict, string Reason, RecordEnvelope? Evidence) ReleasePlacement(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        JsonElement placement = Get(order.Body, "placement");
        if (Text(placement, "kind") != "release") return ("unmet", "No release placement.", null);
        if (!model.ById.TryGetValue(Text(placement, "carrier") ?? "", out RecordEnvelope? identity)) return ("unmet", "The release carrier is dangling.", null);
        RecordEnvelope[] carriers = model.OfType("work-order").Where(r => Identity(model, r) == identity.Id && CurrentAt(model, r, at)).ToArray();
        if (carriers.Length != 1) return ("unavailable", "The release carrier history is missing or conflicting.", identity);
        RecordEnvelope carrier = carriers[0];
        if (Status(model, carrier, at) is { } status && EstateModel.BodyString(status, "status") == "retired") return ("unmet", "The release carrier is retired.", status);
        JsonElement plan = Get(carrier.Body, "releasePlan");
        if (Text(plan, "product") != Text(placement, "product") || Text(plan, "version") != Text(placement, "version")) return ("unmet", "The carrier's current plan contradicts this placement.", carrier);
        RecordEnvelope? ended = model.OfType("release").FirstOrDefault(r => r.AtTime <= at && r.Subject?.Id == Text(plan, "product") &&
            EstateModel.BodyString(r, "version") == Text(plan, "version") && EstateModel.BodyString(r, "status") is "published" or "withdrawn");
        return ended is null ? ("satisfied", "The exact release placement is valid.", carrier) : ("unmet", "The release target has been published or withdrawn.", ended);
    }

    internal static IReadOnlyList<RecordEnvelope> Results(EstateModel model, RecordEnvelope order, DateTimeOffset at)
    {
        List<RecordEnvelope> results = [];
        foreach (RecordEnvelope assignment in model.OfType("assignment").Where(a => CurrentAt(model, a, at) && a.Subject is not null && model.Chain(order).Contains(a.Subject.Id, StringComparer.Ordinal)))
        {
            RecordEnvelope[] attempts = model.OfType("attempt").Where(a => a.Subject == assignment.Reference && CurrentAt(model, a, at)).OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
            if (attempts.Length == 0) continue;
            RecordEnvelope attempt = attempts[^1];
            if (attempts.Count(a => a.At == attempt.At) > 1) throw new PolicyInputException("The latest attempt is ambiguous.");
            results.AddRange(model.OfType("result").Where(r => r.Subject == attempt.Reference && CurrentAt(model, r, at)));
        }
        if (results.Count > 1) throw new PolicyInputException("The current submitted result is ambiguous.");
        return results;
    }

    internal static bool ReviewQualifies(EstateModel model, RecordEnvelope review, RecordEnvelope result, string requirement) =>
        review.Subject == result.Reference && (requirement == "any" || review.By != result.By &&
            (requirement == "independent" && EstateModel.BodyString(review, "independence") is "independent-agent" or "operator" ||
             requirement == "operator" && EstateModel.BodyString(review, "independence") == "operator" && PolicyAuthority.OperatorAt(model, review.By, review.At)));
}
