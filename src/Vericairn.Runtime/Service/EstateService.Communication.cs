// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public CommunicationProfile Communication(string? recipient = null, string? assignment = null, CommunicationValues? view = null)
    {
        lock (gate)
        {
            EstateModel model = session.Load();
            string? unavailable = recipient is not null && (model.CurrentPrincipal(recipient) is not { } person || EstateModel.BodyString(person, "kind") != "human")
                ? "The named communication recipient is not a known person."
                : assignment is not null && (recipient is null || !model.ById.TryGetValue(assignment, out RecordEnvelope? assigned) || assigned.Type != "assignment")
                ? "An assignment override needs a known recipient and an exact assignment record."
                : null;
            if (unavailable is not null)
            {
                return new CommunicationProfile(recipient, assignment, new Dictionary<string, CommunicationValue>(), [], [], Unavailable: unavailable);
            }

            List<RecordEnvelope> scoped = model.OfType("communication-setting")
                .Where(r => EstateModel.BodyString(r, "scope") == "estate" ||
                    (recipient is not null && EstateModel.BodyString(r, "recipient") == recipient &&
                     (EstateModel.BodyString(r, "scope") == "person" || (assignment is not null && EstateModel.BodyString(r, "assignment") == assignment))))
                .OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            List<CommunicationLayer> layers = [];
            HashSet<string> reconciliations = model.OfType("reconciliation").Where(r => ReferencesCommunication(model, r))
                .Select(r => r.ExpectedPath).ToHashSet(StringComparer.Ordinal);
            List<Finding> invalid = model.Findings.Where(f => f.Severity == Severity.Error && f.Code is not ("communication.conflict" or "reconcile.successors") &&
                (f.Location.StartsWith("records/communication-", StringComparison.Ordinal) || reconciliations.Any(path => f.Location == path || f.Location.StartsWith(path + "#", StringComparison.Ordinal)))).ToList();
            if (invalid.Count > 0)
            {
                return new CommunicationProfile(recipient, assignment, new Dictionary<string, CommunicationValue>(), [], [],
                    Unavailable: "Communication sources are invalid; no preference can safely be applied.\n" + string.Join('\n', invalid));
            }

            try
            {
                layers = scoped.Select(r => new CommunicationLayer(EstateModel.BodyString(r, "scope")!,
                    JsonSerializer.Deserialize<CommunicationValues>(r.Body.GetProperty("values"), ProtocolJson.Options)!,
                    r.Id, r.ComputedHash, r.By, EstateModel.BodyString(r, "basis"), model.IsCurrent(r), r.Supersedes,
                    EstateModel.BodyString(r, "authority"), EstateModel.BodyString(r, "authorityHash"), EstateModel.BodyString(r, "basisHash"))).ToList();
                return CommunicationResolution.Resolve(recipient, assignment, layers, view) with
                {
                    ObservedAt = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow),
                    RevisionHash = ContentHash.Sha256(Encoding.UTF8.GetBytes(string.Join('\n', layers.Select(r => r.Hash)))),
                };
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return new CommunicationProfile(recipient, assignment, new Dictionary<string, CommunicationValue>(), layers, [], Unavailable: exception.Message);
            }
        }
    }

    private static bool ReferencesCommunication(EstateModel model, RecordEnvelope record)
    {
        bool Communication(string? id) => id is not null && model.ById.TryGetValue(id, out RecordEnvelope? source) &&
            source.Type is "communication-setting" or "communication-delegation";
        return Communication(record.Subject?.Id) || Communication(EstateModel.BodyString(record, "kept")) ||
            (record.Body.TryGetProperty("withdrawn", out JsonElement withdrawn) && withdrawn.ValueKind == JsonValueKind.Array &&
             withdrawn.EnumerateArray().Any(id => id.ValueKind == JsonValueKind.String && Communication(id.GetString())));
    }

    /// <inheritdoc />
    public WriteOutcome CommunicationSet(CommunicationSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (session.Manifest.RecordFormat < 2)
            {
                return Refuse("Communication settings require record format 2 and a COMMS-01-capable reader.");
            }

            if (!Ulid.IsValid(request.By) || request.Values is null)
            {
                return Refuse("A communication change needs its actual author and values.");
            }

            try
            {
                CommunicationResolution.Validate(request.Values);
            }
            catch (ArgumentException exception)
            {
                return Refuse(exception.Message);
            }

            List<RecordEnvelope> previous = model.Current("communication-setting").Where(r =>
                EstateModel.BodyString(r, "scope") == request.Scope && EstateModel.BodyString(r, "recipient") == request.Recipient &&
                EstateModel.BodyString(r, "assignment") == request.Assignment).ToList();
            if (CommunicationConflict(previous, request.Expected) is { } conflict)
            {
                return conflict;
            }

            var values = new JsonObject();
            foreach ((string field, string? value) in request.Values.Fields)
            {
                if (value is not null)
                {
                    values[field] = value;
                }
            }

            var body = new JsonObject { ["scope"] = request.Scope, ["values"] = values };
            if (request.Recipient is not null) { body["recipient"] = request.Recipient; }
            if (request.Assignment is not null) { body["assignment"] = request.Assignment; }
            if (request.Authority is not null)
            {
                if (!model.ById.TryGetValue(request.Authority, out RecordEnvelope? grant) || grant.Type != "communication-delegation" || !model.IsCurrent(grant) || EstateModel.BodyString(grant, "status") != "active")
                {
                    return Refuse("The explicit communication delegation is unavailable, superseded or revoked; ask the recipient for a current grant.");
                }

                body["authority"] = grant.Id;
                body["authorityHash"] = grant.ComputedHash;
            }

            if (request.Basis is not null)
            {
                if (!model.ById.TryGetValue(request.Basis, out RecordEnvelope? basis) || basis.Type != "decision")
                {
                    return Refuse("The adoption basis must name an existing decision.");
                }

                body["basis"] = basis.Id;
                body["basisHash"] = basis.ComputedHash;
            }

            return WriteLocked(new RecordDraft("communication-setting", request.By, Element(body), Supersedes: previous.SingleOrDefault()?.Id), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome CommunicationDelegate(CommunicationDelegateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (session.Manifest.RecordFormat < 2 || !Ulid.IsValid(request.By))
            {
                return Refuse("Communication delegation requires record format 2 and its recipient as the author.");
            }

            List<RecordEnvelope> previous = model.Current("communication-delegation").Where(r =>
                EstateModel.BodyString(r, "scope") == request.Scope && EstateModel.BodyString(r, "recipient") == request.Recipient &&
                EstateModel.BodyString(r, "assignment") == request.Assignment && EstateModel.BodyString(r, "grantee") == request.Grantee).ToList();
            if (CommunicationConflict(previous, request.Expected) is { } conflict)
            {
                return conflict;
            }

            var body = new JsonObject { ["scope"] = request.Scope, ["recipient"] = request.Recipient, ["grantee"] = request.Grantee, ["status"] = request.Status };
            if (request.Assignment is not null) { body["assignment"] = request.Assignment; }
            if (request.Expires is not null) { body["expires"] = request.Expires; }
            return WriteLocked(new RecordDraft("communication-delegation", request.By, Element(body), Supersedes: previous.SingleOrDefault()?.Id), model);
        }
    }

    private static WriteOutcome? CommunicationConflict(List<RecordEnvelope> previous, string? expected) =>
        previous.Count > 1 || (previous.Count == 0 ? expected is not null : previous[0].ComputedHash != expected)
            ? WriteOutcome.Fail(new Problem("conflict", "The communication scope changed or has divergent revisions. Read its current revisions, resolve any conflict, and submit with the expected prior hash.", []))
            : null;

    private static string? CommunicationWriteRefusal(RecordDraft draft, EstateModel model)
    {
        if (draft.Type != "communication-setting" || draft.Body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Field(string name) => draft.Body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? authority = Field("authority");
        if (authority is null && (Field("scope") == "estate" || Field("recipient") == draft.By))
        {
            return null;
        }

        // Historical validation preserves earlier authorized records. A new write, even through records/new with a claimed
        // earlier timestamp, needs consent that is in force now; backdating cannot revive revoked or expired authority.
        return authority is not null && model.ById.TryGetValue(authority, out RecordEnvelope? grant) && grant.Type == "communication-delegation" &&
            model.IsCurrent(grant) && EstateModel.BodyString(grant, "status") == "active" && !EstateModel.Expired(grant, DateTimeOffset.UtcNow)
            ? null : "A new communication write requires an explicit delegation that is current, active and unexpired now; a historical timestamp cannot revive consent.";
    }
}
