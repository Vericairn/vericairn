// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

namespace Vericairn.Records.Types;

/// <summary>A product the estate owns, with its editions and surfaces.</summary>
public sealed class ProductType : IRecordType
{
    /// <inheritdoc />
    public string Name => "product";

    /// <inheritdoc />
    public string Summary => "A product the estate owns: name, summary, editions and surfaces. Details live in design documents.";

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

        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("product.author", $"Authoring principal {context.Record.By} has no principal record.");
        }
    }
}
