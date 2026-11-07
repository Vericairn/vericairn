// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// The record rules of security reports read before a release (OPS-11): a product's <c>securityReports</c> declaration and the
/// operator's <c>triage</c> observation of one advisory revision. Records name an advisory only by its identifier, repository and
/// the hash of the provider's response; its summary, description, reporter and credits never enter a record.
/// </summary>
public static class SecurityReportRules
{
    /// <summary>The field a product declares the repositories it reads in.</summary>
    public const string ProductField = "securityReports";

    /// <summary>The observation kind of a triage.</summary>
    public const string TriageKind = "triage";

    /// <summary>The most repositories one product declaration reads.</summary>
    public const int MaxRepositories = 8;

    /// <summary>The longest reason a triage carries, in the operator's own words.</summary>
    public const int MaxReason = 500;

    /// <summary>What a triage concludes: a vulnerability to fix, a duplicate of another report, or no action, with a reason.</summary>
    public static readonly ImmutableArray<string> Dispositions = ["security", "duplicate", "no-action"];

    /// <summary>The fact names a triage carries, and no others.</summary>
    public static readonly ImmutableArray<string> TriageFacts = ["advisory", "repository", "contentHash", "disposition", "reason"];

    /// <summary>Validates a product's declaration and that only the operator declares, changes or removes it.</summary>
    public static void Product(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        if (!body.Since(3, ProductField)) return;
        BodyReader? reports = body.Nested(ProductField, false);
        if (reports is not null)
        {
            reports.Text("provider", true, 100, Patterns.Slug, "a lower-case kebab-case provider id");
            IReadOnlyList<string>? repositories = reports.StringArray("repositories", true, allowEmpty: false, maxItemLength: Identity.Ulid.Length);
            reports.NoOtherKeys();
            if (repositories is not null)
            {
                if (repositories.Count > MaxRepositories || repositories.Distinct(StringComparer.Ordinal).Count() != repositories.Count)
                    reports.Error("product.security-reports", "repositories", $"Declare one to {MaxRepositories} distinct repository resources.");
                foreach (string id in repositories)
                {
                    RecordEnvelope? resource = context.RequireReference(ProductField + ".repositories", id, "resource");
                    if (resource is not null && GitHubLocation(resource) is null)
                        reports.Error("product.security-reports", "repositories", $"Resource {id} is not a repository resource located on GitHub.");
                }
            }
        }

        // The declaration holds every release while a report waits; lifting it is the operator's recorded act.
        if (ProductType.Changed(context, ProductField) && !context.HasRole(context.Record.By, Roles.Operator))
            context.Error("product.security-reports-author", "A product's security-reports declaration is declared, changed or removed only by the operator.");
    }

    /// <summary>Validates a triage observation's facts, subject and author.</summary>
    public static void Triage(ValidationContext context, BodyReader? facts)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (facts is null) return;
        string? repository = facts.Text("repository", true, 200, Patterns.GitHubRepository, "owner/name");
        facts.Text("advisory", true, 19, Patterns.GitHubAdvisory, "a GHSA identifier such as GHSA-xxxx-xxxx-xxxx");
        facts.Hash("contentHash", true);
        string? disposition = facts.Enum("disposition", true, [.. Dispositions]);
        string? reason = facts.Text("reason", false, MaxReason);
        if (disposition == "no-action" && reason is null)
            facts.Error("triage.reason", "reason", "A triage of no-action gives its reason, in the operator's own words.");
        foreach (JsonProperty property in facts.Element.EnumerateObject())
            if (!TriageFacts.Contains(property.Name, StringComparer.Ordinal))
                facts.Error("triage.facts", property.Name, "A triage names its advisory by identifier, repository and content hash with its disposition and reason only.");

        RecordEnvelope? subject = context.Find(context.Record.Subject?.Id);
        if (context.Record.Subject?.Type != "resource" || subject is null || GitHubLocation(subject) is not { } located
            || repository is not null && !string.Equals(located, repository, StringComparison.OrdinalIgnoreCase))
            context.Error("triage.subject", "A triage pins the exact repository resource on GitHub whose advisory it names.");

        // No security principal role is defined yet, so only the operator triages a security report.
        if (!context.HasRole(context.Record.By, Roles.Operator))
            context.Error("triage.author", "Only the operator records a security report's triage.");
    }

    /// <summary>A repository resource's GitHub owner/name, or null.</summary>
    public static string? GitHubLocation(RecordEnvelope resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.Type == "resource" && EstateModel.BodyString(resource, "kind") == "repository"
            && resource.Body.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.Object
            && location.TryGetProperty("github", out JsonElement github) && github.ValueKind == JsonValueKind.String
            && Patterns.GitHubRepository.IsMatch(github.GetString()!) ? github.GetString() : null;
    }
}
