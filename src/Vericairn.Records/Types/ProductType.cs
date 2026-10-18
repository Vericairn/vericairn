// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Records.Types;

/// <summary>A product the estate owns, with its editions and surfaces.</summary>
public sealed class ProductType : IRecordType
{
    /// <inheritdoc />
    public string Name => "product";

    /// <summary>The roles an artifact a product's release declares can have.</summary>
    public static readonly ImmutableArray<string> ReleaseRoles = ["distributable", "sbom", "notices", "documentation", "other"];

    /// <summary>The platform identifiers a declared artifact can be built for.</summary>
    public static readonly ImmutableArray<string> ReleasePlatforms = ["linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64"];

    /// <inheritdoc />
    public string Summary => "A product the estate owns: name, summary, editions and surfaces, and optionally how its releases are made (the destination and the artifacts a complete release carries). Details live in design documents.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("name", "string", true, "Product name."),
        new("slug", "slug", true, "Identifier."),
        new("summary", "string", true, "The product in a sentence."),
        new("editions", "object[]", true, "name, slug, summary, optional license (SPDX expression)."),
        new("surfaces", "object[]", true, "name, slug, summary: runtime, Workbench, Remote, CLI, SDK, websites."),
        new("release", "object", false, "How the product's releases are made: destination (provider id and its settings, string values), checksums (the asset that lists the others' hashes) and artifacts (name with {version} and optionally {platform}, role distributable|sbom|notices|documentation|other, platforms). Read by release 0.4.2, the first published release to read it, and later (0.4.0, which introduced it, was withdrawn)."),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        body.Text("name", true, 200);
        body.Text("slug", true, 100, Patterns.Slug, "a lower-case kebab-case slug");
        body.Text("summary", true, 2000);
        foreach (BodyReader edition in body.NestedArray("editions", true, allowEmpty: false) ?? [])
        {
            edition.Text("name", true, 100);
            edition.Text("slug", true, 100, Patterns.Slug, "a slug");
            edition.Text("summary", true, 2000);
            edition.Text("license", false, 200);
            edition.NoOtherKeys();
        }

        foreach (BodyReader surface in body.NestedArray("surfaces", true, allowEmpty: false) ?? [])
        {
            surface.Text("name", true, 100);
            surface.Text("slug", true, 100, Patterns.Slug, "a slug");
            surface.Text("summary", true, 2000);
            surface.NoOtherKeys();
        }

        BodyReader? release = body.Nested("release", false);
        if (release is not null)
        {
            BodyReader? destination = release.Nested("destination", false);
            if (destination is not null)
            {
                destination.Text("provider", true, 100, Patterns.Slug, "a lower-case kebab-case provider id");
                BodyReader? settings = destination.Nested("settings", false);
                if (settings is not null)
                {
                    foreach (System.Text.Json.JsonProperty property in settings.Element.EnumerateObject())
                    {
                        settings.Allow(property.Name);
                        if (property.Name.Length > 100 || property.Value.ValueKind != System.Text.Json.JsonValueKind.String || property.Value.GetString()!.Length > 500)
                        {
                            settings.Error("body.type", property.Name, "Destination settings are strings of at most 500 characters under names of at most 100.");
                        }
                    }

                    settings.NoOtherKeys();
                }

                destination.NoOtherKeys();
            }

            release.Text("checksums", false, 200);
            foreach (BodyReader artifact in release.NestedArray("artifacts", false, allowEmpty: true) ?? [])
            {
                string? name = artifact.Text("name", true, 300);
                artifact.Enum("role", true, ReleaseRoles);
                IReadOnlyList<string>? platforms = artifact.StringArray("platforms", false, allowEmpty: false, maxItemLength: 50);
                foreach (string platform in platforms ?? [])
                {
                    if (!ReleasePlatforms.Contains(platform, StringComparer.Ordinal))
                    {
                        artifact.Error("body.value", "platforms", $"'{platform}' is not one of {string.Join(", ", ReleasePlatforms)}.");
                    }
                }

                if (name is not null && !name.Contains("{version}", StringComparison.Ordinal))
                {
                    artifact.Error("body.value", "name", "An artifact's name carries {version}.");
                }

                if (name is not null && name.Contains("{platform}", StringComparison.Ordinal) != (platforms is not null))
                {
                    artifact.Error("body.value", "platforms", "Platforms are listed exactly when the name carries {platform}.");
                }

                artifact.NoOtherKeys();
            }

            release.NoOtherKeys();
        }

        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("product.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }
}
