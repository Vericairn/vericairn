// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

internal static class PackageRecordRules
{
    internal static readonly string[] Reasons = ["Legacy", "CriticalBugs", "Other"];
    internal static readonly string[] DeprecationFields = ["reasons", "deprecationMessage", "alternatePackage", "alternateRange"];
    internal static bool IsAction(RecordEnvelope record) => EstateModel.BodyString(record, "action") is "package-unlist" or "package-deprecate";
    internal static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    internal static bool Has(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out _);
    internal static bool DeclaresReserved(RecordEnvelope product, string? version) => product.Body.TryGetProperty("packages", out JsonElement packages)
        && packages.ValueKind == JsonValueKind.Object && packages.TryGetProperty("reserved", out JsonElement reserved) && reserved.ValueKind == JsonValueKind.Array
        && reserved.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), version, StringComparison.OrdinalIgnoreCase));

    internal static void Observation(ValidationContext context, string? kind, BodyReader? facts)
    {
        IReadOnlyList<BodyReader>? evidence = context.Body.Since(3, "evidence")
            ? context.Body.NestedArray("evidence", kind == "package", allowEmpty: false) : null;
        if (kind != "package")
        {
            if (evidence is not null) context.Error("observation.evidence", "Registry response evidence belongs to a package observation.");
            return;
        }
        if (evidence is { Count: > 32 }) context.Error("observation.evidence", "A package observation carries at most thirty-two registry responses.");
        long bytes = 0;
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader response in evidence ?? [])
        {
            string? url = response.Text("url", true, 500);
            response.Hash("hash", true);
            long? status = response.Number("status", true, 200, 404);
            bytes += response.Number("bytes", true, 0, 1024 * 1024) ?? 0;
            response.Enum("encoding", true, "identity", "gzip");
            response.NoOtherKeys();
            if (!PackageIdentity.PublicUrl(url) || !urls.Add(url!) || status is not (200 or 404))
                context.Error("observation.evidence", "Registry responses have distinct public nuget.org API URLs and status 200 or 404.");
        }
        if (bytes > 8 * 1024 * 1024 || !urls.Contains("https://api.nuget.org/v3/index.json") || urls.Count < 3)
            context.Error("observation.evidence", "Retain the service index, registration and package-content responses within eight MiB.");
        if (facts is null) return;
        Pins(context, facts.Element);
        string? publication = facts.Enum("publication", true, "published", "not-found");
        string? listing = facts.Enum("listing", true, "listed", "unlisted", "unknown");
        string? deprecation = facts.Enum("deprecation", true, "deprecated", "not-deprecated", "unknown");
        if (publication == "not-found" && (listing != "unknown" || deprecation != "unknown")
            || publication == "published" && (listing == "unknown" || deprecation == "unknown"))
            context.Error("observation.package-state", "An absent version has unknown listing and deprecation; a published version has explicit registry state.");
        if (publication == "published" && !PackageIdentity.PublicUrl(facts.Text("packageContent", true, 500)))
            context.Error("observation.package-content", "Published metadata names its public package-content URL.");
        if (deprecation == "deprecated")
        {
            IReadOnlyList<string>? reasons = facts.StringArray("reasons", true, allowEmpty: false, maxItemLength: 100);
            if (reasons is not null && (reasons.Count > 3 || reasons.Any(r => !Reasons.Contains(r, StringComparer.Ordinal))))
                context.Error("observation.deprecation", "Deprecation reasons are NuGet's Legacy, CriticalBugs or Other.");
        }
        else if (DeprecationFields.Any(facts.Has))
            context.Error("observation.deprecation", "Only an observed deprecation carries deprecation metadata.");
        if (facts.Has("alternatePackage") && !PackageIdentity.ValidId(Text(facts.Element, "alternatePackage"))
            || facts.Has("alternateRange") && !facts.Has("alternatePackage"))
            context.Error("observation.deprecation", "An alternate range belongs to a named alternate package.");
        RecordEnvelope? subject = context.Find(context.Record.Subject?.Id);
        if (subject is { Type: "operation" } && IsAction(subject) && subject.Supersedes is null)
        {
            JsonElement target = subject.Body.GetProperty("target");
            foreach (string field in new[] { "product", "productHash", "release", "releaseHash", "reserved", "package", "version", "provider" })
                if (Text(facts.Element, field) != Text(target, field)) context.Error("observation.package-intent", "The observation must keep the original intent's package and version pins.");
            if (string.CompareOrdinal(EstateModel.BodyString(context.Record, "asOf"), subject.At) < 0)
                context.Error("observation.package-intent", "The observed outcome follows its recorded intent.");
        }
        else if (Has(facts.Element, "reserved")
            ? subject is not { Type: "product" } || context.Record.Subject != subject.Reference || subject.Id != Text(facts.Element, "product")
            : subject is not { Type: "release" } || subject.Id != Text(facts.Element, "release"))
            context.Error("observation.package-subject", "A package observation pins its release, its reserved version's exact product declaration, or the original account-holder intent.");
        if (string.CompareOrdinal(EstateModel.BodyString(context.Record, "asOf"), context.Record.At) > 0)
            context.Error("observation.future", "A package observation cannot report a future state.");
    }

    internal static void Operation(ValidationContext context)
    {
        if (!IsAction(context.Record)) return;
        if (context.Format < 3 || !context.HasRole(context.Record.By, Roles.Operator))
            context.Error("operation.package-author", "Account-holder intents require record format 3 and the estate's operator.");
        JsonElement target = context.Record.Body.TryGetProperty("target", out JsonElement t) ? t : default;
        Pins(context, target);
        string? action = EstateModel.BodyString(context.Record, "action");
        if (action == "package-deprecate")
        {
            string[] reasons = (Text(target, "reasons") ?? "").Split(',');
            if (reasons.Length is < 1 or > 3 || reasons.Any(r => !Reasons.Contains(r, StringComparer.Ordinal))
                || reasons.Distinct(StringComparer.Ordinal).Count() != reasons.Length)
                context.Error("operation.package-deprecation", "Deprecation intent names distinct NuGet reasons.");
            if (Text(target, "alternatePackage") is { } alternate && !PackageIdentity.ValidId(alternate)
                || Text(target, "alternateRange") is not null && Text(target, "alternatePackage") is null)
                context.Error("operation.package-deprecation", "An alternate range belongs to a named alternate package.");
        }
        string subjectField = Has(target, "reserved") ? "product" : "release";
        if (EstateModel.BodyString(context.Record, "provider") != "nuget-org"
            || context.Record.Subject?.Id != Text(target, subjectField) || context.Record.Subject?.Hash != Text(target, subjectField + "Hash")
            || context.Record.Subject?.Type != subjectField)
            context.Error("operation.package-target", "The operation pins the release or reserved version's exact product declaration and the nuget.org provider.");
        string? status = EstateModel.BodyString(context.Record, "status");
        if (status is not ("not-started" or "uncertain" or "observed-success") || context.Record.Body.TryGetProperty("dispatchedAt", out _))
            context.Error("operation.package-status", "An account-holder operation records intent and observed outcome, never a claimed registry dispatch.");
        if (context.Find(context.Record.Supersedes) is { } previous && previous.Body.TryGetProperty("target", out JsonElement oldTarget)
            && CanonicalJson.Serialize(oldTarget) != CanonicalJson.Serialize(target))
            context.Error("operation.package-target", "An account-holder operation keeps every original target and intended deprecation fact.");
        if (status == "not-started") return;
        JsonElement observed = context.Record.Body.TryGetProperty("observed", out JsonElement o) ? o : default;
        RecordEnvelope? observation = context.Find(Text(observed, "reference"));
        string first = context.Model.Chain(context.Record)[0];
        if (observation is not { Type: "observation" } || EstateModel.BodyString(observation, "kind") != "package"
            || observation.Subject?.Id != first || !observation.Body.TryGetProperty("facts", out JsonElement facts))
            context.Error("operation.package-observation", "An outcome cites a package observation against the original intent.");
        else if (status == "observed-success" && !Matches(action!, target, facts))
            context.Error("operation.package-outcome", "A successful outcome requires the intended registry state to have been observed.");
        if (observation is not null && (!context.Record.Body.TryGetProperty("evidence", out JsonElement evidence)
            || evidence.ValueKind != JsonValueKind.Array || !evidence.EnumerateArray().Any(e => Text(e, "hash") == observation.ComputedHash && Text(e, "location") == observation.ExpectedPath)))
            context.Error("operation.package-evidence", "The outcome retains the exact observation hash and location.");
    }

    internal static bool Matches(string action, JsonElement target, JsonElement facts)
    {
        if (Text(facts, "publication") != "published") return false;
        if (action == "package-unlist") return Text(facts, "listing") == "unlisted";
        if (Text(facts, "deprecation") != "deprecated" || !facts.TryGetProperty("reasons", out JsonElement reasons) || reasons.ValueKind != JsonValueKind.Array) return false;
        string[] actual = reasons.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual((Text(target, "reasons") ?? "").Split(',').Order(StringComparer.Ordinal), StringComparer.Ordinal)) return false;
        return (Text(target, "deprecationMessage") ?? "") == (Text(facts, "deprecationMessage") ?? "")
            && string.Equals(Text(target, "alternatePackage"), Text(facts, "alternatePackage"), StringComparison.OrdinalIgnoreCase)
            && Text(target, "alternateRange") == Text(facts, "alternateRange");
    }

    private static void Pins(ValidationContext context, JsonElement value)
    {
        if (Text(value, "provider") != "nuget-org" || !PackageIdentity.ValidId(Text(value, "package")) || !PackageIdentity.ValidVersion(Text(value, "version")))
            context.Error("package.identity", "Registry facts name nuget-org, a package id and a valid NuGet version.");
        RecordEnvelope? product = context.Find(Text(value, "product"));
        RecordEnvelope? release = context.Find(Text(value, "release"));
        if (product is not { Type: "product" } || product.ComputedHash != Text(value, "productHash")
            || !product.Body.TryGetProperty("packages", out JsonElement declaration) || Text(declaration, "provider") != "nuget-org"
            || !declaration.TryGetProperty("ids", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array
            || !ids.EnumerateArray().Any(id => id.ValueKind == JsonValueKind.String && string.Equals(id.GetString(), Text(value, "package"), StringComparison.OrdinalIgnoreCase)))
            context.Error("package.product", "Registry facts pin a product declaration containing this package.");
        if (Has(value, "reserved"))
        {
            string? reserved = Text(value, "reserved");
            if (Has(value, "release") || Has(value, "releaseHash") || !PackageIdentity.ValidVersion(reserved)
                || !string.Equals(reserved, Text(value, "version"), StringComparison.OrdinalIgnoreCase)
                || product is not { Type: "product" } || !DeclaresReserved(product, reserved))
                context.Error("package.reserved", "Reserved registry facts name a version declared reserved in the exact pinned product, without release pins.");
            return;
        }
        if (release is not { Type: "release" } || release.ComputedHash != Text(value, "releaseHash")
            || !string.Equals(EstateModel.BodyString(release, "version"), Text(value, "version"), StringComparison.OrdinalIgnoreCase)
            || product is null || release.Subject is null || !context.Model.Chain(product).Contains(release.Subject.Id, StringComparer.Ordinal))
            context.Error("package.release", "Registry facts pin the product's exact release version.");
    }
}
