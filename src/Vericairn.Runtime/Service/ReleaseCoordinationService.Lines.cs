// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

internal sealed partial class ReleaseCoordinationService
{
    private static readonly string[] ReleaseMetadataKeys = ["releaseLine", "releaseChannel", "releaseEndOfSupport", "releaseBackport", "releaseBackportHash"];

    private static void AddReleaseMetadata(JsonObject target, ReleaseSubmitRequest request)
    {
        if (request.Line is not null) target["releaseLine"] = request.Line;
        if (request.Channel is not null) target["releaseChannel"] = request.Channel;
        if (request.EndOfSupport is not null) target["releaseEndOfSupport"] = request.EndOfSupport;
        if (request.BackportOf is not null)
        {
            target["releaseBackport"] = request.BackportOf.Id;
            target["releaseBackportHash"] = request.BackportOf.Hash;
        }
    }

    private static void CopyReleaseMetadata(JsonElement target, JsonObject candidate)
    {
        foreach ((string source, string field) in new[] { ("releaseLine", "line"), ("releaseChannel", "channel"), ("releaseEndOfSupport", "endOfSupport") })
            if (target.TryGetProperty(source, out JsonElement value)) candidate[field] = JsonNode.Parse(value.GetRawText());
        if (target.TryGetProperty("releaseBackport", out JsonElement id))
            candidate["backportOf"] = new JsonObject
            {
                ["id"] = JsonNode.Parse(id.GetRawText()),
                ["hash"] = target.TryGetProperty("releaseBackportHash", out JsonElement hash) ? JsonNode.Parse(hash.GetRawText()) : null,
            };
    }

    private FindingDto[] CheckReleaseMetadata(EstateModel model, ReleaseSubmitRequest request, string? commit, string destination)
    {
        bool declared = model.ById.GetValueOrDefault(request.Product)?.Body.TryGetProperty("lines", out _) == true;
        bool existingLine = model.OfType("release").Any(r => r.Subject is { Type: "product" } product
            && LivingDocuments.ProductIdentity(model, product.Id) == LivingDocuments.ProductIdentity(model, request.Product)
            && (r.Body.TryGetProperty("line", out _) || r.Body.TryGetProperty("channel", out _)));
        if (!declared && !existingLine && request.Line is null && request.Channel is null && request.EndOfSupport is null && request.BackportOf is null) return [];
        if (!Ulid.IsValid(request.By) || !Ulid.IsValid(request.Product)) return [];
        var target = new JsonObject();
        AddReleaseMetadata(target, request);
        var body = new JsonObject
        {
            ["version"] = request.Version, ["repository"] = destination, ["commit"] = commit ?? new string('0', 40), ["status"] = "candidate",
            ["artifacts"] = new JsonArray(), ["recordFormat"] = new JsonObject { ["reads"] = new JsonArray(1, 2, 3), ["writes"] = 3 },
            ["evidence"] = new JsonArray(), ["qualified"] = new JsonArray(), ["signing"] = "unsigned developer build", ["limitations"] = new JsonArray(),
        };
        CopyReleaseMetadata(Element(target), body);
        RecordEnvelope candidate = RecordAuthor.Create(new("release", request.By, Element(body), new("product", request.Product, null),
            At: now(), Format: model.Manifest?.RecordFormat ?? RecordFormats.First)).Envelope;
        var findings = new FindingList();
        new ReleaseType().Validate(new(model, candidate, findings));
        return findings.Select(f => new FindingDto("error", f.Code, "release", f.Message)).ToArray();
    }

    private static string? PublicationCommand(IReleaseProvider provider, string destination, string tag, JsonElement body)
    {
        string channel = ReleaseLines.Channel(body);
        try { return provider.ManualPublicationCommand(destination, tag, channel); }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string UnsupportedPublication(JsonElement body) =>
        $"The provider does not support a publication command for channel '{ReleaseLines.Channel(body)}'. Install a provider that supports this channel before publication.";
}
