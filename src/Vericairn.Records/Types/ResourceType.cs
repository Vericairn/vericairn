// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>A repository, service, storage or account the estate uses.</summary>
public sealed class ResourceType : IRecordType
{
    /// <inheritdoc />
    public string Name => "resource";

    /// <inheritdoc />
    public string Summary => "A repository, service, storage location or account that the estate uses, with its role and visibility.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", "repository|service|storage|account", true, "What the resource is."),
        new("name", "string", true, "Display name."),
        new("role", "string", true, "What the estate uses it for."),
        new("location", "object", true, "github: owner/name, url, and/or rootCommit (the first commit of a repository with no remote); never a machine path."),
        new("visibility", "public|private|n/a", true, "Who can see it."),
        new("notes", "string", false, "Free text."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Enum("kind", true, "repository", "service", "storage", "account");
        body.Text("name", true, 200);
        body.Text("role", true, 2000);
        BodyReader? location = body.Nested("location", true);
        if (location is not null)
        {
            string? github = location.Text("github", false, 200, Patterns.GitHubRepository, "owner/name");
            string? url = location.Text("url", false, 2000);
            string? rootCommit = location.Text("rootCommit", false, 40, Patterns.Commit, "a 40-character lower-case hex commit id");
            location.NoOtherKeys();
            if (github is null && url is null && rootCommit is null)
            {
                location.Error("body.missing", "github", "A location needs 'github', 'url' or 'rootCommit'.");
            }

            // Any other scheme is refused too: http, and file, which a rooted path on Linux and macOS parses as.
            if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps))
            {
                location.Error("body.pattern", "url", "'url' must be an absolute https URL.");
            }
        }

        RepositoryChange(context);

        body.Enum("visibility", true, "public", "private", "n/a");
        body.Text("notes", false, 4000);
        body.NoOtherKeys();
    }

    /// <summary>The code of a repository change by an author without the operator role or the 'administer' scope.</summary>
    public const string LocationAuthorCode = "resource.location-author";

    /// <summary>The code of a change to a repository a security-reports declaration names, by an author other than the operator.</summary>
    public const string SecurityReportsAuthorCode = "resource.security-reports-author";

    // A repository's location is where checked changes push and merge and where security reports are read. Each revision is judged
    // against the declarations and grants in force at its own time, so a rule never reaches back to a revision written before it:
    // while a product's security-reports declaration names the resource, only the operator changes its kind or location, and a
    // repository's location moves only by the operator or an administer grant, as before 0.9.2. From 0.9.2 the write boundary also
    // refuses a new revision that turns a resource into or out of a repository without that authority, or that a later declaration
    // would forbid; a revision already in the estate keeps its reading, and validation reports it as information.
    private static void RepositoryChange(ValidationContext context)
    {
        if (context.Record.Format < 3 || context.Record.Supersedes is null) return;
        bool repository = Estate.EstateModel.BodyString(context.Record, "kind") == "repository";
        bool was = context.Find(context.Record.Supersedes) is { } previous && Estate.EstateModel.BodyString(previous, "kind") == "repository";
        bool located = ProductType.Changed(context, "location");
        if (!repository && !was || !located && !ProductType.Changed(context, "kind")) return;
        bool byOperator = context.HasRole(context.Record.By, Roles.Operator);
        IReadOnlyList<string> lineage = context.Model.Chain(context.Model.Latest(context.Record));
        if (SecurityReportRules.DeclaresAt(context.Model, lineage, context.Record.AtTime))
        {
            if (!byOperator)
                context.Error(SecurityReportsAuthorCode, "A repository resource that a product's security-reports declaration names changes its kind or location only by the operator.");
            return;
        }

        bool administers = byOperator || context.HasScope(context.Record.By, Scopes.Administer);
        if (repository && located && !administers)
            context.Error(LocationAuthorCode, "A repository resource's location is changed only by the operator or a principal with the 'administer' scope.");
        else if (!administers)
            context.Findings.Info(LocationAuthorCode, context.Location, "From 0.9.2 a new revision that turns a resource into or out of a repository, or moves a former repository, needs the operator or the 'administer' scope; a revision already in the estate keeps its reading.");
        if (!byOperator && SecurityReportRules.DeclaresAt(context.Model, lineage, DateTimeOffset.MaxValue))
            context.Findings.Info(SecurityReportsAuthorCode, context.Location, "A product's security-reports declaration now names this resource, so a new revision changing its kind or location needs the operator; this revision, written before the declaration, keeps its reading.");
    }
}
