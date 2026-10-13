// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>An identified release of a product: exact commit, artifacts by hash, evidence, compatibility and status.</summary>
public sealed class ReleaseType : IRecordType
{
    /// <summary>Release states: a candidate is recorded before any publication; published is recorded after the publication is observed; withdrawn keeps the identity.</summary>
    public static readonly string[] Statuses = ["candidate", "published", "withdrawn"];

    /// <summary>Runtime identifiers an artifact may target.</summary>
    public static readonly string[] Platforms = ["any", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64"];

    /// <summary>Keys a published revision may change relative to the candidate it supersedes; everything else must be identical.</summary>
    private static readonly string[] PublicationKeys = ["status", "publishedAt", "url"];

    /// <summary>
    /// True when <paramref name="latest"/> is the publication of the release candidate <paramref name="candidate"/>: it supersedes it
    /// directly and differs only in status, publishedAt and url. An acceptance of the candidate then applies to it by design.
    /// </summary>
    public static bool IsPublicationOf(RecordEnvelope candidate, RecordEnvelope latest)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(latest);
        if (candidate.Type != "release" || latest.Type != "release" || latest.Supersedes != candidate.Id || EstateModel.BodyString(latest, "status") != "published")
        {
            return false;
        }

        var left = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var right = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Text.Json.JsonProperty property in candidate.Body.EnumerateObject().Where(p => Array.IndexOf(PublicationKeys, p.Name) < 0))
        {
            left[property.Name] = property.Value.GetRawText();
        }

        foreach (System.Text.Json.JsonProperty property in latest.Body.EnumerateObject().Where(p => Array.IndexOf(PublicationKeys, p.Name) < 0))
        {
            right[property.Name] = property.Value.GetRawText();
        }

        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) && value == pair.Value);
    }

    /// <inheritdoc />
    public string Name => "release";

    /// <inheritdoc />
    public string Summary => "An identified release of a product: version, exact commit, artifacts by hash and platform, software bill of materials, notices, evidence, record-format compatibility, signing state, limitations and status. A candidate precedes publication; a published revision supersedes it after the operator's acceptance and the observed publication.";

    /// <inheritdoc />
    public string? SubjectType => "product";

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("version", "semver", true, "Product version."),
        new("repository", "owner/name", true, "Source repository."),
        new("commit", "hex", true, "The exact commit built (40 hex digits)."),
        new("status", string.Join('|', Statuses), true, "candidate, published (needs the operator's acceptance of the candidate) or withdrawn."),
        new("artifacts", "object[]", true, "name, hash, bytes, platform; the distributable files. May be empty only for a candidate."),
        new("recordFormat", "object", true, "reads (integer[]) and writes (integer): which record formats this release handles."),
        new("sbom", "hash", false, "Hash of the software bill of materials artifact."),
        new("notices", "hash", false, "Hash of the third-party notices artifact."),
        new("evidence", "object[]", true, "summary, optional hash and location: test runs, qualification, cold starts."),
        new("qualified", "string[]", true, "Platforms whose qualification is recorded for this release; may be empty."),
        new("signing", "string", true, "For example 'unsigned developer build' until signing material exists."),
        new("limitations", "string[]", true, "Known limitations; may be empty."),
        new("publishedAt", "timestamp", false, "When the publication was observed (published only)."),
        new("url", "string", false, "Where the published release is observed (published only)."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("version", true, 100, Patterns.SemanticVersion, "a semantic version");
        body.Text("repository", true, 200, Patterns.GitHubRepository, "owner/name");
        body.Text("commit", true, 40, Patterns.Commit, "a 40-character lower-case hex commit id");
        string? status = body.Enum("status", true, Statuses);
        IReadOnlyList<BodyReader>? artifacts = body.NestedArray("artifacts", true, allowEmpty: true);
        foreach (BodyReader artifact in artifacts ?? [])
        {
            artifact.Text("name", true, 300);
            artifact.Hash("hash", true);
            artifact.Number("bytes", true, 0, 1L << 40);
            artifact.Enum("platform", true, Platforms);
            artifact.NoOtherKeys();
        }

        BodyReader? format = body.Nested("recordFormat", true);
        if (format is not null)
        {
            format.Number("writes", true, 1, 1000);
            if (format.Has("reads"))
            {
                format.Allow("reads");
                if (!format.Element.TryGetProperty("reads", out System.Text.Json.JsonElement reads) || reads.ValueKind != System.Text.Json.JsonValueKind.Array || !reads.EnumerateArray().All(r => r.ValueKind == System.Text.Json.JsonValueKind.Number) || reads.GetArrayLength() == 0)
                {
                    format.Error("body.type", "reads", "'reads' must be a non-empty array of integers.");
                }
            }
            else
            {
                format.Error("body.missing", "reads", "'reads' is required.");
            }

            format.NoOtherKeys();
        }

        body.Hash("sbom", false);
        body.Hash("notices", false);
        foreach (BodyReader evidence in body.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 500);
            evidence.NoOtherKeys();
        }

        body.StringArray("qualified", true, allowEmpty: true, maxItemLength: 50);
        body.Text("signing", true, 200);
        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        string? publishedAt = body.Timestamp("publishedAt", status == "published");
        body.Text("url", status == "published", 500);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("release.author", $"Recording principal {context.Record.By} has no principal record.");
            return;
        }

        if (!context.HasRole(context.Record.By, Roles.Operator) && !context.Model.HasScope(context.Record.By, Scopes.LocalEngineering))
        {
            context.Error("release.author", "Releases are recorded by the operator or under the 'local-engineering' scope.");
        }

        if (status != "candidate" && (artifacts is null || artifacts.Count == 0))
        {
            context.Error("release.artifacts", "A published or withdrawn release names its artifacts.");
        }

        if (status == "published")
        {
            RecordEnvelope? candidate = context.Record.Supersedes is null ? null : context.Find(context.Record.Supersedes);
            if (candidate is null || candidate.Type != Name)
            {
                context.Error("release.candidate", "A published release supersedes the candidate record that was accepted.");
                return;
            }

            bool accepted = context.Model.OfType("acceptance").Any(a => a.Subject?.Id == candidate.Id && a.Subject.Hash == candidate.ComputedHash && context.HasRole(a.By, Roles.Operator));
            if (!accepted)
            {
                context.Error("release.unaccepted", "Publication follows the operator's acceptance of the exact candidate; none is recorded.");
            }

            if (publishedAt is not null && string.CompareOrdinal(publishedAt, candidate.At) < 0)
            {
                context.Error("release.published-at", "A release cannot be published before its candidate was recorded.");
            }
        }

        if (context.Model.IsCurrent(context.Record))
        {
            string? version = EstateModel.BodyString(context.Record, "version");
            if (context.Model.Current(Name).Any(other => other.Id != context.Record.Id && other.Subject?.Id == context.Record.Subject?.Id && EstateModel.BodyString(other, "version") == version))
            {
                context.Error("release.duplicate-version", $"Another current release of this product already has version {version}.");
            }
        }
    }
}
