// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewBrief(string version, string? product = null, string? decision = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            return RequestDeviceRefusal(model) is { } denied
                ? new("", Problem: denied)
                : ReleaseReviewPreparation.Prepare(model, version, product, decision).Response;
        }
    }

    /// <inheritdoc />
    public ReleaseReviewBriefResponse ReleaseReviewAssign(string version, ReleaseReviewAssignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (RequestDeviceRefusal(model) is { } denied) return new("", Problem: denied);
            var (response, order) = ReleaseReviewPreparation.Prepare(model, version, request.Product, request.Decision);
            if (!response.Ok) return response;
            if (request.Expected != response.Hash)
                return response with { Problem = new("conflict", "The prepared review brief changed. Read it again before assigning.", []) };

            var body = new JsonObject
            {
                ["contributor"] = response.Reviewer,
                ["brief"] = JsonNode.Parse(response.Brief!.Value.GetRawText()),
            };
            WriteOutcome written = WriteLocked(new RecordDraft("assignment", request.By, Element(body), order!.Reference, At: PolicyClock.GetUtcNow()), model);
            return response with { Assignment = written.Written, Problem = written.Problem };
        }
    }
}
