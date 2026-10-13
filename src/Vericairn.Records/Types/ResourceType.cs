// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

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

            if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri?.Scheme != "https")
            {
                location.Error("body.pattern", "url", "'url' must be an absolute https URL.");
            }
        }

        body.Enum("visibility", true, "public", "private", "n/a");
        body.Text("notes", false, 4000);
        body.NoOtherKeys();
    }
}
