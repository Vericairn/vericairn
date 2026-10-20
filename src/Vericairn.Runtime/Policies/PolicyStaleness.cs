// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Policies.PolicyInputs;

namespace Vericairn.Runtime.Policies;

internal static partial class FoundationPolicyKinds
{
    private static IEnumerable<PolicyFinding> Stale(PolicyRuleContext context, bool parametersOnly = false)
    {
        string @class = Choice(context.Parameters, "class", "planned-without-release", "result-awaiting-review", "assignment-without-attempt", "open-ask", "obligation-past-gate", "retirement-past-condition");
        Shape(context.Parameters, @class switch
        {
            "result-awaiting-review" => ["class", "maxAgeDays", "reviewRequirement"],
            "obligation-past-gate" or "retirement-past-condition" => ["class", "maxAgeDays", "gates"],
            _ => ["class", "maxAgeDays"],
        });
        long days = Integer(context.Parameters, "maxAgeDays", maximum: long.MaxValue);
        if (@class == "result-awaiting-review") Choice(context.Parameters, "reviewRequirement", "any", "independent", "operator");
        if (parametersOnly)
        {
            if (@class is "obligation-past-gate" or "retirement-past-condition")
                _ = Gated(context, @class, days, parametersOnly: true).ToArray();
            yield break;
        }
        DateTimeOffset at = context.Basis.Time!.Value;
        EstateModel model = context.Model;
        RecordEnvelope[] orders = PolicyWork.Orders(context).ToArray();
        switch (@class)
        {
            case "planned-without-release":
                foreach (RecordEnvelope order in orders.Where(o => PolicyWork.Planned(model, o, at)))
                {
                    (string verdict, string reason, _) = PolicyWork.ReleasePlacement(model, order, at);
                    if (verdict == "unavailable") throw new PolicyInputException(reason);
                    if (verdict == "satisfied") continue;
                    RecordEnvelope anchor = PlannedAnchor(context, order);
                    yield return Age(context, order, @class, days, anchor.AtTime, anchor);
                }
                break;
            case "assignment-without-attempt":
                foreach (RecordEnvelope order in orders.Where(o => !PolicyWork.Accepted(model, o, at) && EstateModel.BodyString(PolicyWork.Status(model, o, at)!, "status") != "retired"))
                foreach (RecordEnvelope assignment in model.Current("assignment").Where(a => a.Subject is not null && model.Chain(order).Contains(a.Subject.Id, StringComparer.Ordinal)))
                    if (!model.OfType("attempt").Any(a => a.Subject == assignment.Reference)) yield return Age(context, assignment, @class, days, assignment.AtTime, assignment);
                break;
            case "result-awaiting-review":
                string requirement = Choice(context.Parameters, "reviewRequirement", "any", "independent", "operator");
                foreach (RecordEnvelope order in orders.Where(o => EstateModel.BodyString(PolicyWork.Status(model, o, at)!, "status") != "retired"))
                foreach (RecordEnvelope result in PolicyWork.Results(model, order, at))
                    if (!model.Current("review").Any(r => PolicyWork.ReviewQualifies(model, r, result, requirement))) yield return Age(context, result, @class, days, result.AtTime, result);
                break;
            case "open-ask":
                foreach (RecordEnvelope ask in model.Current("ask").Where(a => PolicyWork.Selected(context, a) && Asks.IsWaiting(Asks.State(model, a).State)))
                {
                    RecordEnvelope first = model.ById[PolicyWork.Identity(model, ask)];
                    yield return Age(context, ask, @class, days, first.AtTime, first);
                }
                break;
            default:
                foreach (PolicyFinding finding in Gated(context, @class, days)) yield return finding;
                break;
        }
    }

    private static RecordEnvelope PlannedAnchor(PolicyRuleContext context, RecordEnvelope order)
    {
        EstateModel model = context.Model;
        string identity = PolicyWork.Identity(model, order);
        RecordEnvelope? anchor = null;
        foreach (IGrouping<string, RecordEnvelope> instant in model.Records.Where(r => string.CompareOrdinal(r.At, model.ById[identity].At) >= 0).GroupBy(r => r.At, StringComparer.Ordinal))
        {
            DateTimeOffset at = instant.First().AtTime;
            RecordEnvelope current = model.OfType("work-order").Single(r => PolicyWork.Identity(model, r) == identity && PolicyWork.CurrentAt(model, r, at));
            (string verdict, string reason, _) = PolicyWork.ReleasePlacement(model, current, at);
            if (verdict == "unavailable") throw new PolicyInputException(reason);
            bool waiting = PolicyWork.Planned(model, current, at) && verdict != "satisfied";
            if (!waiting) anchor = null;
            else anchor ??= instant.FirstOrDefault(r => r.Id == current.Id)
                ?? instant.FirstOrDefault(r => r.Type == "work-status" && r.Subject is { } s && model.Chain(current).Contains(s.Id, StringComparer.Ordinal))
                ?? instant.FirstOrDefault(r => PolicyWork.OrderOfEvent(model, r)?.Id == current.Id)
                ?? PolicyWork.ReleasePlacement(model, current, at).Evidence ?? current;
        }
        return anchor ?? order;
    }

    private static PolicyFinding Age(PolicyRuleContext context, RecordEnvelope subject, string item, long days, DateTimeOffset anchorTime, RecordEnvelope? anchor, IEnumerable<RecordReference>? evidence = null)
    {
        decimal seconds = (context.Basis.Time!.Value - anchorTime).Ticks / (decimal)TimeSpan.TicksPerSecond;
        return context.Finding(PolicyWork.Target(context.Model, subject), item, seconds >= days * 86400m ? "unmet" : "satisfied",
            seconds.ToString(CultureInfo.InvariantCulture) + " elapsed seconds.", "Less than " + Number(days) + " elapsed days.", "Resolve the waiting condition or record its authorized disposition.", anchor, evidence) with { AnchorAt = Time(anchorTime) };
    }

    private static IEnumerable<PolicyFinding> Gated(PolicyRuleContext context, string @class, long days, bool parametersOnly = false)
    {
        JsonElement[] gates = Array(context.Parameters, "gates", empty: true);
        bool obligations = @class == "obligation-past-gate";
        foreach (JsonElement gate in gates)
        {
            Shape(gate, obligations ? ["subject", "obligation", "conditions"] : ["subject", "conditions"]);
            Shape(Get(gate, "subject"), ["type", "id", "hash"]);
            RecordEnvelope subject = Exact(context.Model, Get(gate, "subject"));
            if (subject.Type != (obligations ? "decision" : "retirement")) throw new PolicyParameterException("Gate subject has the wrong record type.");
            if (obligations && !Items(Get(subject.Body, "obligations")).Any(o => Text(o, "key") == String(gate, "obligation"))) throw new PolicyParameterException("The exact decision has no such obligation.");
            foreach (JsonElement condition in Array(gate, "conditions")) ValidateCondition(condition);
        }
        if (gates.GroupBy(g => PolicyWork.Identity(context.Model, Exact(context.Model, Get(g, "subject"))) + ":" + Text(g, "obligation"), StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new PolicyParameterException("A gate subject is mapped more than once.");
        if (parametersOnly) yield break;
        var subjects = obligations
            ? context.Model.OfType("decision").SelectMany(d => Items(Get(d.Body, "obligations")).Select(o => (Record: d, Key: Text(o, "key") ?? "?", Prose: Text(o, "gates") ?? "No typed gate.")))
                .GroupBy(x => (Identity: PolicyWork.Identity(context.Model, x.Record), x.Key)).Select(g => g.Last())
            : context.Model.Current("retirement").Select(r => (Record: r, Key: "retirement", Prose: EstateModel.BodyString(r, "condition") ?? "No typed condition."));
        foreach (var subject in subjects)
        {
            string identity = PolicyWork.Identity(context.Model, subject.Record);
            JsonElement gate = gates.FirstOrDefault(g => PolicyWork.Identity(context.Model, Exact(context.Model, Get(g, "subject"))) == identity && (!obligations || Text(g, "obligation") == subject.Key));
            if (!PolicyWork.Selected(context, subject.Record) && gate.ValueKind == JsonValueKind.Undefined) continue;
            if (obligations && context.Model.Current("discharge").Any(d => d.Subject is { Type: "decision" } s && context.Model.ById.TryGetValue(s.Id, out RecordEnvelope? decision) && PolicyWork.Identity(context.Model, decision) == identity && EstateModel.BodyString(d, "obligation") == subject.Key)) continue;
            if (!obligations && EstateModel.BodyString(subject.Record, "retiredAt") is not null && RetirementRemoved(context, subject.Record)) continue;
            if (gate.ValueKind == JsonValueKind.Undefined)
            {
                yield return context.Finding(PolicyWork.Target(context.Model, subject.Record), subject.Key, "unavailable", subject.Prose, "An exact typed interpretation of the gate.", "Approve a typed gate mapping.", subject.Record);
                continue;
            }
            var reached = Array(gate, "conditions").Select(c => GateTime(context, c)).ToArray();
            if (reached.Any(r => r.At is null))
            {
                yield return context.Finding(PolicyWork.Target(context.Model, subject.Record), subject.Key, "satisfied", "The typed condition has not been reached.", "All gate conditions reached before age begins.", "Wait for the remaining gate events.", evidence: reached.Where(r => r.Record is not null).Select(r => r.Record!.Reference));
                continue;
            }
            var latest = reached.OrderBy(r => r.At).ThenBy(r => r.Record?.Id, StringComparer.Ordinal).Last();
            yield return Age(context, subject.Record, subject.Key, days, latest.At!.Value, latest.Record, reached.Where(r => r.Record is not null).Select(r => r.Record!.Reference));
        }
    }

    internal static bool RetirementRemoved(PolicyRuleContext context, RecordEnvelope retirement)
    {
        string estate = context.Model.CurrentEstate is { } root ? PolicyWork.Identity(context.Model, root) : throw new PolicyInputException("The estate repository identity is missing.");
        PolicyRepository repository = Repository(context.Basis, estate);
        string path = EstateModel.BodyString(retirement, "path") ?? throw new PolicyInputException("The retirement path is missing.");
        return !repository.Files.Any(f => f.Path == path || f.Path.StartsWith(path + "/", StringComparison.Ordinal));
    }

    private static void ValidateCondition(JsonElement condition)
    {
        string kind = Choice(condition, "kind", "at", "work-accepted", "release-published", "release-submission");
        Shape(condition, kind switch { "at" => ["kind", "at"], "work-accepted" => ["kind", "workOrder"], _ => ["kind", "product", "version"] });
        if (kind == "at") Timestamp(condition, "at");
        else if (kind == "work-accepted") Shape(Get(condition, "workOrder"), ["type", "id", "hash"]);
        else { String(condition, "product"); String(condition, "version"); }
    }

    private static (DateTimeOffset? At, RecordEnvelope? Record) GateTime(PolicyRuleContext context, JsonElement condition)
    {
        string kind = Text(condition, "kind")!;
        if (kind == "at") { DateTimeOffset time = Timestamp(condition, "at"); return (time <= context.Basis.Time ? time : null, null); }
        if (kind == "work-accepted")
        {
            RecordEnvelope work = Exact(context.Model, Get(condition, "workOrder"));
            if (work.Type != "work-order") throw new PolicyParameterException("An acceptance gate pins an exact work order.");
            RecordEnvelope? acceptance = context.Model.OfType("acceptance").FirstOrDefault(a => a.Subject == work.Reference || a.Subject is { Type: "result" } s && context.Model.ById.TryGetValue(s.Id, out RecordEnvelope? result) && s == result.Reference && WorkView.OrderOfResult(context.Model, result)?.Reference == work.Reference);
            return (acceptance?.AtTime, acceptance);
        }
        string product = Text(condition, "product")!;
        if (!context.Model.ById.TryGetValue(product, out RecordEnvelope? record) || record.Type != "product") throw new PolicyInputException("The gate product is unavailable.");
        string version = Text(condition, "version")!;
        if (kind == "release-published")
        {
            RecordEnvelope? publication = context.Model.OfType("release").FirstOrDefault(r => ReleaseMatchesProduct(context.Model, r, product) && EstateModel.BodyString(r, "version") == version && EstateModel.BodyString(r, "status") == "published");
            return (publication?.AtTime, publication);
        }
        RecordEnvelope? intent = context.Model.OfType("operation").FirstOrDefault(o => EstateModel.BodyString(o, "action") == Release.ReleaseFlow.DispatchAction &&
            Text(Get(o.Body, "target"), "product") == product && Text(Get(o.Body, "target"), "version") == version);
        if (intent is not null) return (intent.AtTime, intent);
        return context.Basis.Submission is { } submission && submission.Product == product && submission.Version == version ? (context.Basis.Time, null) : (null, null);
    }
}
