// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>A durable request for an operator's action, including its exact draft and destination.</summary>
public sealed class OperatorRequestType : IRecordType
{
    private static readonly string[] IdentityFields = ["to", "askedBy", "sourceAsk", "sourceAskHash"];
    /// <inheritdoc />
    public string Name => "operator-request";
    /// <inheritdoc />
    public string Summary => "An action requested of an operator: why, recommendation, exact content by hash, destination and the work or release it serves. A changed draft supersedes the request; a signed close or observation preserves its outcome.";
    /// <inheritdoc />
    public string? SubjectType => null;
    /// <inheritdoc />
    public bool SubjectHashRequired => true;
    /// <inheritdoc />
    public long SinceFormat => 3;
    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("to", "ulid", true, "The operator asked.", 3),
        new("askedBy", "ulid", true, "Who asked: the author, or the original ask's author when carried over.", 3),
        new("action", "string", true, "The action requested.", 3),
        new("why", "string", true, "Why the action is needed.", 3),
        new("recommendation", "string", true, "The recommendation, presented first.", 3),
        new("consequence", "string", true, "What waits while the request stays open.", 3),
        new("kind", "action|publication", true, "A publication request is its recorded intent before the external effect; it dispatches nothing.", 3),
        new("destination", "string", true, "The intended destination, without machine paths.", 3),
        new("content", "object[]", true, "At least one exact item: kind (text|command|file), hash, text for text/command or name for a file. Text hashes cover UTF-8 bytes.", 3),
        new("sourceAsk", "ulid", false, "The exact original ask carried over; the ask remains readable and is not answered by this request.", 3),
        new("sourceAskHash", "hash", false, "The exact carried ask's hash; required with sourceAsk.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? to = body.Ulid("to", true);
        string? askedBy = body.Ulid("askedBy", true);
        body.Text("action", true);
        body.Text("why", true);
        body.Text("recommendation", true);
        body.Text("consequence", true, 2000);
        body.Enum("kind", true, "action", "publication");
        body.Text("destination", true, 1000);
        foreach (BodyReader item in body.NestedArray("content", true, allowEmpty: false) ?? [])
        {
            string? kind = item.Enum("kind", true, "text", "command", "file");
            string? hash = item.Hash("hash", true);
            string? text = item.Text("text", kind is "text" or "command", 40000);
            item.Text("name", kind == "file", 500);
            item.NoOtherKeys();
            if (text is not null && hash != ContentHash.Sha256(Encoding.UTF8.GetBytes(text)))
                context.Error("operator-request.content", "The content hash must match the exact UTF-8 text or command.");
            if (kind == "file" && text is not null)
                context.Error("operator-request.content", "A file item names its file and byte hash; inline content uses text or command.");
        }
        string? source = body.Ulid("sourceAsk", false);
        string? sourceHash = body.Hash("sourceAskHash", source is not null);
        body.NoOtherKeys();
        context.RequireReference("to", to, "principal");
        context.RequireReference("askedBy", askedBy, "principal");
        if (!context.HasRole(to, Roles.Operator) || context.Principal(to) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            context.Error("operator-request.to", "A request is put to a human operator.");
        if (context.Principal(context.Record.By) is null)
            context.Error("operator-request.author", "The requester must be a known principal.");
        if (context.Record.Subject?.Type is not ("work-order" or "release"))
            context.Error("operator-request.subject", "A request pins the work order or release it serves.");

        RecordEnvelope? previous = context.Find(context.Record.Supersedes);
        if (previous is { Type: "operator-request" })
        {
            if (previous.By != context.Record.By || previous.Subject != context.Record.Subject ||
                IdentityFields.Any(f => EstateModel.BodyString(previous, f) != EstateModel.BodyString(context.Record, f)))
                context.Error("operator-request.revision", "A revision keeps its author, original requester, operator, source ask and subject.");
            if (OperatorRequests.Closures(context.Model, previous).Any())
                context.Error("operator-request.closed", "A closed request cannot be revised; a new action needs a new request.");
        }
        if (source is null)
        {
            if (sourceHash is not null || askedBy != context.Record.By)
                context.Error("operator-request.requester", "Without a source ask, askedBy is the author and sourceAskHash is absent.");
            return;
        }
        RecordEnvelope? ask = context.RequireReference("sourceAsk", source, "ask");
        if (ask is null) return;
        if (ask.ComputedHash != sourceHash || askedBy != ask.By || to != EstateModel.BodyString(ask, "to") ||
            context.Record.By != ask.By && context.Record.By != to)
            context.Error("operator-request.source", "Carry-over pins the original ask and requester, and is recorded by its asker or the operator asked.");
        RecordEnvelope first = previous is null ? context.Record : context.Model.ById[context.Model.Chain(previous)[0]];
        if (EstateModel.BodyString(ask, "state") == "withdrawn" || string.CompareOrdinal(ask.At, first.At) > 0 ||
            context.Model.Records.Any(r => r.Type == "ask" && r.Supersedes == ask.Id && string.CompareOrdinal(r.At, first.At) <= 0) ||
            context.Model.Records.Any(r => r.Type == "decision" && string.CompareOrdinal(r.At, first.At) <= 0 &&
                r.Body.TryGetProperty("answers", out var answers) && answers.ValueKind == System.Text.Json.JsonValueKind.Array &&
                answers.EnumerateArray().Any(a => a.ValueKind == System.Text.Json.JsonValueKind.String && context.Model.Chain(ask).Contains(a.GetString()!))))
            context.Error("operator-request.source", "Only an open or notified ask can be carried over.");
        string root = context.Model.Chain(ask)[0];
        if (context.Model.Current(Name).Any(r => r.Id != context.Record.Id && context.Model.Chain(r)[0] != first.Id &&
            context.Find(EstateModel.BodyString(r, "sourceAsk")) is { Type: "ask" } other && context.Model.Chain(other)[0] == root))
            context.Error("operator-request.duplicate", "This ask already has a carried request.");
    }
}
