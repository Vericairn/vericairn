// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>The single record-derived queue of operator requests; reads never close a request.</summary>
public static class OperatorRequests
{
    /// <summary>At most three requests are presented in one delivery.</summary>
    public const int PerDelivery = 3;

    /// <summary>All outcomes pinned to any revision of this request, including observed effects.</summary>
    public static IEnumerable<RecordEnvelope> Closures(EstateModel model, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<string> chain = model.Chain(request);
        return model.Records.Where(r => r.Subject is { Type: "operator-request" } subject && chain.Contains(subject.Id) &&
            (r.Type == "operator-request-close" || r.Type == "observation" && EstateModel.BodyString(r, "kind") == "operator-request"));
    }

    /// <summary>The current request that carries an ask, if any. The original ask remains readable and unanswered.</summary>
    public static RecordEnvelope? Carried(EstateModel model, RecordEnvelope ask)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ask);
        IReadOnlyList<string> chain = model.Chain(ask);
        return model.Current("operator-request").FirstOrDefault(r => EstateModel.BodyString(r, "sourceAsk") is string id && chain.Contains(id));
    }

    /// <summary>Every open request in original request order, optionally addressed to one operator.</summary>
    public static IReadOnlyList<RecordEnvelope> Open(EstateModel model, string? to = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Current("operator-request").Where(r => (to is null || EstateModel.BodyString(r, "to") == to) && !Closures(model, r).Any())
            .OrderBy(r => model.ById[model.Chain(r)[0]].At, StringComparer.Ordinal)
            .ThenBy(r => model.Chain(r)[0], StringComparer.Ordinal).ToList();
    }

    /// <summary>Complete queue membership with three delivered bodies and the rest identified in order.</summary>
    public static JsonObject Derive(EstateModel model, string? to = null)
    {
        IReadOnlyList<RecordEnvelope> open = Open(model, to);
        return new JsonObject
        {
            ["format"] = "vericairn.operator-requests/1", ["perDelivery"] = PerDelivery, ["to"] = to, ["waiting"] = open.Count,
            ["delivered"] = new JsonArray(open.Take(PerDelivery).Select(r => (JsonNode)Item(model, r)).ToArray()),
            ["queued"] = new JsonArray(open.Skip(PerDelivery).Select(r => (JsonNode)new JsonObject
            {
                ["id"] = model.Chain(r)[0], ["revision"] = r.Id, ["hash"] = r.ComputedHash,
            }).ToArray()),
        };
    }

    /// <summary>The recommendation first, with every remaining identity visible behind the delivery.</summary>
    public static string RenderText(EstateModel model, string? to = null)
    {
        IReadOnlyList<RecordEnvelope> open = Open(model, to);
        var text = new StringBuilder("Operator requests: ").Append(open.Count.ToString(CultureInfo.InvariantCulture)).Append(" open\n");
        foreach (RecordEnvelope request in open.Take(PerDelivery))
        {
            text.Append("  Recommendation: ").Append(EstateModel.BodyString(request, "recommendation")).Append('\n')
                .Append("  Action: ").Append(EstateModel.BodyString(request, "action")).Append(" [operator-request ").Append(request.Id).Append("]\n")
                .Append("  Why: ").Append(EstateModel.BodyString(request, "why")).Append('\n')
                .Append("  Destination: ").Append(EstateModel.BodyString(request, "destination")).Append('\n')
                .Append("  If unanswered: ").Append(EstateModel.BodyString(request, "consequence")).Append('\n')
                .Append("  Asked by: ").Append(EstateModel.BodyString(request, "askedBy")).Append("; to: ").Append(EstateModel.BodyString(request, "to")).Append('\n')
                .Append("  Inspect exact content: vericairn records show ").Append(request.Id).Append('\n')
                .Append("  Done: vericairn operator done ").Append(request.Id).Append(" --by ").Append(EstateModel.BodyString(request, "to"))
                .Append(" --expected ").Append(request.ComputedHash)
                .Append(EstateModel.BodyString(request, "kind") == "publication" ? " --link <observed-destination>\n" : "\n")
                .Append("  Dismiss: vericairn operator dismiss ").Append(request.Id).Append(" --by ").Append(EstateModel.BodyString(request, "to"))
                .Append(" --expected ").Append(request.ComputedHash).Append(" --reason <your-reason>\n");
        }
        if (open.Count > PerDelivery)
            text.Append("  Waiting in order: ").Append(string.Join(", ", open.Skip(PerDelivery).Select(r => r.Id))).Append("; each follows when an earlier request closes.\n");
        return text.ToString();
    }

    /// <summary>An exact request and its recorded body, without reinterpreting its content.</summary>
    public static JsonObject Item(EstateModel model, RecordEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["id"] = model.Chain(request)[0], ["revision"] = request.Id, ["hash"] = request.ComputedHash,
            ["by"] = request.By, ["at"] = request.At, ["body"] = JsonNode.Parse(request.Body.GetRawText()),
            ["subject"] = request.Subject is null ? null : new JsonObject { ["type"] = request.Subject.Type, ["id"] = request.Subject.Id, ["hash"] = request.Subject.Hash },
        };
    }
}
