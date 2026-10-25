// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Canonical;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Format-3 prompt definitions and literal substitution, shared by validation and authoring.</summary>
public static partial class SavedPrompts
{
    /// <summary>The stable source:key syntax.</summary>
    public static Regex IdentityPattern { get; } = IdentityRegex();

    /// <summary>Reads the common definition fields; the caller owns lifecycle and unknown-field checks.</summary>
    public static void ValidateDefinition(BodyReader body)
    {
        ArgumentNullException.ThrowIfNull(body);
        body.Text("key", true, 80, Patterns.Slug, "a lower-case kebab-case key");
        body.Text("title", true, 200);
        body.Text("description", true, 2000);
        string? text = body.Text("text", true, 20000);
        string? category = body.Text("category", true, 200);
        if (category is not null && category.Split('/').Any(s => string.IsNullOrWhiteSpace(s) || s != s.Trim() || s is "." or ".." || s.Any(char.IsControl)))
            body.Error("prompt.category", "category", "Use nonempty slash-separated category segments without leading or trailing spaces.");
        body.StringArray("tags", true, maxItemLength: 80);
        IReadOnlyList<string>? roles = body.StringArray("roles", true, maxItemLength: 80);
        if (roles is not null && roles.Any(r => !Roles.All.Contains(r, StringComparer.Ordinal)))
            body.Error("prompt.roles", "roles", "Suited roles must be known principal roles.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader placeholder in body.NestedArray("placeholders", true) ?? [])
        {
            string? name = placeholder.Text("name", true, 64, NameRegex(), "an ASCII letter followed by letters, digits or underscores");
            placeholder.Text("description", true, 1000);
            placeholder.NoOtherKeys();
            if (name is not null && !names.Add(name)) placeholder.Error("prompt.placeholder", "name", "A placeholder is declared once.");
        }
        if (text is not null)
        {
            HashSet<string> used = PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            if (!names.SetEquals(used)) body.Error("prompt.placeholder", "placeholders", "Declare exactly the {name} placeholders used in the text.");
        }
    }

    /// <summary>Substitutes every declared argument once, literally, without interpreting values as templates or commands.</summary>
    public static string Render(JsonElement definition, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] names = definition.GetProperty("placeholders").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();
        if (values.Count != names.Length || names.Any(n => !values.TryGetValue(n, out string? value) || string.IsNullOrWhiteSpace(value) || value.Length > 4000))
            throw new ArgumentException("Supply exactly one nonempty value of at most 4000 characters for every declared placeholder.", nameof(values));
        string rendered = PlaceholderRegex().Replace(definition.GetProperty("text").GetString()!, match => values[match.Groups[1].Value]);
        if (rendered.Length > 40000) throw new ArgumentException("The rendered prompt exceeds 40000 characters.", nameof(values));
        return rendered;
    }

    /// <summary>Reads the immutable identity of a copied or used built-in definition.</summary>
    public static void ValidateOrigin(BodyReader origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        origin.Text("source", true, 80, Patterns.Slug);
        origin.Text("key", true, 80, Patterns.Slug);
        origin.Text("version", true, 200);
        origin.Hash("hash", true);
        origin.NoOtherKeys();
    }

    internal static void RequireSigned(ValidationContext context)
    {
        if (!SignatureChecks.IsAuthentic(context.Model, context.Record))
            context.Error("prompt.signature", "Prompt records and uses require the author's admitted device signature.");
    }

    internal static bool MayWrite(ValidationContext context) => MayWrite(context.Model, context.Record.By, context.Record.AtTime, historical: true);

    internal static bool MayWrite(EstateModel model, string by, DateTimeOffset at, bool historical = false)
    {
        if (PolicyAuthority.OperatorAt(model, by, RecordParser.FormatTimestamp(at))) return true;
        return (historical ? model.OfType("authority") : model.Current("authority")).Any(grant => grant.AtTime <= at
            && EstateModel.BodyString(grant, "grantee") == by && !EstateModel.Expired(grant, at)
            && grant.Body.TryGetProperty("scope", out JsonElement scopes) && scopes.ValueKind == JsonValueKind.Array
            && scopes.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.String && s.GetString() == Scopes.ManagePrompts)
            && !PolicyRecords.Items(PolicyRecords.Property(grant.Body, "conditions")).Any()
            && !PolicyRecords.Contains(PolicyRecords.Property(grant.Body, "reserved"), Scopes.ManagePrompts)
            && PolicyAuthority.OperatorAt(model, grant.By, grant.At) && SignatureChecks.IsAuthentic(model, grant)
            // Record timestamps have second precision. A later revocation in that same second cannot invalidate an earlier write.
            // Authoring also checks the current head, so a revoked grant cannot authorize another new write in that second.
            && !model.OfType("authority").Any(next => next.Id != grant.Id && next.AtTime < at
                && model.Chain(next).Contains(grant.Id, StringComparer.Ordinal)));
    }

    internal static void ValidateRevision(ValidationContext context, string type, IReadOnlyList<string> fields)
    {
        bool Same(RecordEnvelope r) => fields.All(f => EstateModel.BodyString(r, f) == EstateModel.BodyString(context.Record, f));
        if (context.Find(context.Record.Supersedes) is { } prior && !Same(prior))
            context.Error("prompt.identity", "A revision preserves its prompt identity and preference owner and scope.");
        if (context.Model.IsCurrent(context.Record) && context.Model.Current(type).Any(r => r.Id != context.Record.Id && Same(r)))
            context.Error("prompt.conflict", "Several current revisions have the same prompt identity or preference scope; reconcile them before use.");
    }

    internal static bool SameField(JsonElement left, JsonElement right, string field)
    {
        bool hasLeft = left.TryGetProperty(field, out JsonElement a);
        bool hasRight = right.TryGetProperty(field, out JsonElement b);
        return hasLeft == hasRight && (!hasLeft || CanonicalJson.Serialize(a) == CanonicalJson.Serialize(b));
    }

    /// <summary>A clearly bounded comment for a brief; the original terms remain authoritative.</summary>
    public static string Addendum(JsonElement use)
    {
        var text = new StringBuilder("\n\n## Saved prompt addendum\n\nComment only. The assignment, admitted cap and permitted effects remain authoritative; changes require an amendment.\n\n");
        text.Append("Source: ").Append(use.GetProperty("source").GetString()).Append(':').Append(use.GetProperty("key").GetString())
            .Append("; revision/version: ").Append(use.GetProperty("version").GetString()).Append("; hash: ").Append(use.GetProperty("hash").GetString()).Append(".\n\n");
        foreach (string line in use.GetProperty("rendered").GetString()!.Split('\n')) text.Append("> ").Append(line).Append('\n');
        return text.ToString();
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]*:[a-z][a-z0-9-]*$")]
    private static partial Regex IdentityRegex();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex NameRegex();
    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();
}
