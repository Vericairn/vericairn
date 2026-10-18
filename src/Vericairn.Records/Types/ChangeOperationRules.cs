// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static class ChangeOperationRules
{
    internal static bool IsChange(RecordEnvelope record) => EstateModel.BodyString(record, "action") is
        "change-push" or "change-open" or "change-merge" or "change-observe";

    internal static bool AuthorizedWhen(ValidationContext context) => context.HasRole(context.Record.By, Roles.Operator)
        || context.Model.OfType("authority").Any(grant => EstateModel.BodyString(grant, "grantee") == context.Record.By
            && GoalRules.Before(grant, context.Record) && !EstateModel.Expired(grant, context.Record.AtTime)
            && grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array
            && scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == Scopes.RepositoryWrite)
            && (!context.Model.SupersededBy.TryGetValue(grant.Id, out string? next) || !GoalRules.Before(context.Model.ById[next], context.Record)));

    internal static void Read(ValidationContext context)
    {
        BodyReader body = context.Body;
        IReadOnlyList<string>? required = body.Since(3, "requiredChecks") ? body.StringArray("requiredChecks", false, allowEmpty: true, maxItemLength: 200) : null;
        IReadOnlyList<BodyReader>? checks = body.Since(3, "checks") ? body.NestedArray("checks", false, allowEmpty: true) : null;
        foreach (BodyReader check in checks ?? [])
        {
            check.Text("name", true, 200);
            check.Text("head", true, 40, Patterns.Commit, "an exact commit");
            check.Enum("status", true, "queued", "in_progress", "completed", "unknown");
            check.Enum("conclusion", false, "success", "failure", "cancelled", "skipped", "neutral", "timed_out", "action_required", "stale", "startup_failure", "unknown");
            check.NoOtherKeys();
        }
        BodyReader? scan = body.Since(3, "scan") ? body.Nested("scan", false) : null;
        if (scan is not null)
        {
            scan.Text("tool", true, 100, Patterns.Slug, "a tool id");
            scan.Text("version", true, 100);
            scan.Text("range", true, 82);
            scan.Enum("verdict", true, "passed", "findings", "unavailable", "incomplete");
            scan.Number("findingCount", false, 0, int.MaxValue);
            scan.NoOtherKeys();
        }
        if (!IsChange(context.Record))
        {
            if (required is not null || checks is not null || scan is not null)
                context.Error("operation.change-evidence", "Checks and scans belong to repository change operations.");
            return;
        }

        if (context.Format < 3)
        {
            context.Error("operation.format", "Repository change operations belong to record format 3; an estate records them once it is upgraded to a release that writes that format.");
            return;
        }

        JsonElement target = context.Record.Body.TryGetProperty("target", out JsonElement namedTarget) && namedTarget.ValueKind == JsonValueKind.Object ? namedTarget : default;
        string? head = Text(target, "head");
        if (head is null || !Patterns.Commit.IsMatch(head)) context.Error("operation.change-head", "A repository operation pins its exact head.");
        string? productId = Text(target, "product");
        RecordEnvelope? product = context.Find(productId);
        if (product is not { Type: "product" } || product.ComputedHash != Text(target, "productHash"))
            context.Error("operation.change-product", "A repository operation pins the product declaration by id and hash.");
        else if (product.Body.TryGetProperty("changes", out JsonElement declaration) && declaration.ValueKind == JsonValueKind.Object)
        {
            if (Text(declaration, "repository") != Text(target, "repository") || Text(declaration, "target") != Text(target, "target")
                || Text(declaration, "provider") != EstateModel.BodyString(context.Record, "provider"))
                context.Error("operation.change-product", "The repository, target and provider must match the pinned declaration.");
            string[] names = declaration.TryGetProperty("requiredChecks", out JsonElement list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToArray() : [];
            if (required is null || !names.SequenceEqual(required, StringComparer.Ordinal))
                context.Error("operation.change-checks", "Required checks must match the pinned declaration.");
        }
        else context.Error("operation.change-product", "The product declares no repository changes.");

        RecordEnvelope? repository = context.Find(Text(target, "repository"));
        if (repository is not { Type: "resource" } || repository.ComputedHash != Text(target, "repositoryHash")
            || !repository.Body.TryGetProperty("location", out JsonElement location) || location.ValueKind != JsonValueKind.Object
            || !location.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == Text(target, "destination")))
            context.Error("operation.change-repository", "A repository operation pins its repository and a destination declared by that resource.");

        string? action = EstateModel.BodyString(context.Record, "action");
        string? status = EstateModel.BodyString(context.Record, "status");
        if (status == "canceled" && (!context.HasRole(context.Record.By, Roles.Operator)
            || string.IsNullOrWhiteSpace(EstateModel.BodyString(context.Record, "reconciliation"))))
            context.Error("operation.change-cancel", "Only the operator can cancel a repository intent, recording an explicit no-effect reconciliation.");
        if (status is "dispatched" or "observed-success")
        {
            if (action == "change-merge" && (required is not { Count: > 0 } || checks is null
                || required.Any(name => !checks.Any(c => Text(c.Element, "name") == name))
                || checks.Any(c => Text(c.Element, "head") != head || Text(c.Element, "status") != "completed" || Text(c.Element, "conclusion") != "success")))
                context.Error("operation.merge-checks", "A merge requires every declared check and every reported check to succeed at its exact head.");
            if (action == "change-push" && (scan is null || Text(scan.Element, "verdict") != "passed"
                || Text(scan.Element, "range") != Text(target, "base") + ".." + head
                || !scan.Element.TryGetProperty("findingCount", out JsonElement count) || !count.TryGetInt32(out int n) || n != 0))
                context.Error("operation.push-scan", "A push requires a successful zero-finding scan of exactly its range.");
        }

        if (context.Find(context.Record.Supersedes) is { } previous && IsChange(previous))
        {
            if (!previous.Body.TryGetProperty("target", out JsonElement oldTarget) || oldTarget.ValueKind != JsonValueKind.Object || target.ValueKind != JsonValueKind.Object) return;
            bool retry = EstateModel.BodyString(previous, "status") is "known-failure" or "canceled"
                && status == "uncertain" && !string.IsNullOrWhiteSpace(EstateModel.BodyString(context.Record, "reconciliation"));
            foreach (JsonProperty fact in oldTarget.EnumerateObject())
            {
                if (retry && fact.Name is "base" or "remoteHead" or "targetHead") continue;
                if (!target.TryGetProperty(fact.Name, out JsonElement kept) || CanonicalJson.Serialize(kept) != CanonicalJson.Serialize(fact.Value))
                    context.Error("operation.change-target", "A repository operation keeps every pinned target fact; a different head needs a new intent.");
            }
            if (action is "change-push" or "change-merge" && EstateModel.BodyString(previous, "status") is "dispatched" or "uncertain" && !retry)
            {
                foreach (string field in new[] { "scan", "checks", "requiredChecks" })
                    if (previous.Body.TryGetProperty(field, out JsonElement old) && (!context.Record.Body.TryGetProperty(field, out JsonElement kept)
                        || CanonicalJson.Serialize(old) != CanonicalJson.Serialize(kept)))
                        context.Error("operation.change-evidence-kept", "A dispatched change retains the checks and scan on which it relied.");
            }
        }
    }

    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
}
