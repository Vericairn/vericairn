// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>
/// The declaration of a living document, from record format 2 (the estate's design/living-documents.md § 4): a document whose claims
/// follow facts that change, what makes it due, how due it gets, and who may clear it. A revision supersedes the declaration.
/// </summary>
public sealed class LivingDocumentType : IRecordType
{
    /// <summary>Written by a person or an agent, or produced by a generator.</summary>
    public static readonly string[] Kinds = ["authored", "generated"];

    /// <summary>What a due document does: an attention item, also an estate check warning, also a closed release gate.</summary>
    public static readonly string[] Levels = ["remind", "warn", "refuse"];

    /// <summary>The foundation's triggers; a pack's are named namespace/name.</summary>
    public static readonly string[] Triggers = ["covers", "release", "upgrade", "decision", "accepted"];

    /// <summary>The foundation's checks; a pack's are named namespace/name.</summary>
    public static readonly string[] Checks = ["names-version", "matches-generator", "links-resolve"];

    /// <summary>Who may clear a due document.</summary>
    public static readonly string[] Clearers = ["any", "independent", "operator"];

    /// <summary>The foundation's source: a file of the estate's own repository.</summary>
    public const string EstateFile = "estate-file";

    /// <inheritdoc />
    public string Name => "living-document";

    /// <inheritdoc />
    public string Summary => "Declares a living document: its path and repository, whether it is authored or generated, the triggers that make it due and at which level (remind, warn or refuse), its generated regions, its checks and who may clear it.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => false;

    /// <inheritdoc />
    public long SinceFormat => 2;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("path", "path", true, "The document's repository-relative path.", 2),
        new("repository", "ulid", false, "The resource record of the repository that holds it; absent for the estate's own repository.", 2),
        new("title", "string", true, "The document's title.", 2),
        new("kind", string.Join('|', Kinds), true, "authored by a person or an agent, or generated from its inputs.", 2),
        new("generator", "object", false, "For a generated document, required: id (such as vericairn.mcp/orientation) and inputs (string[]).", 2),
        new("source", "string", true, "How its content is read at a revision: estate-file, or a pack's source such as vericairn.software/git-file.", 2),
        new("triggers", "object[]", true, "At least one {on, level (remind|warn|refuse), ...}: covers with paths, release with product, upgrade, decision, accepted with keys, or a pack's trigger named namespace/name.", 2),
        new("regions", "object[]", false, "Generated regions: id, generator and optionally its inputs (string[]); marked <!-- vericairn:generated <id> --> and <!-- vericairn:end <id> --> in the document.", 2),
        new("clearedBy", string.Join('|', Clearers), false, "Who may clear it: any admitted principal (the default), independent (not the one that revised its content), or operator.", 2),
        new("checks", "string[]", false, "Checks run at each clearance: names-version, matches-generator, links-resolve, or a pack's named namespace/name.", 2),
        new("summary", "string", true, "Why it is living: the facts it states.", 2),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? path = body.Text("path", true, 500, Patterns.RelativePath, "a normalized repository-relative path");
        string? repository = body.Ulid("repository", false);
        body.Text("title", true, 300);
        string? kind = body.Enum("kind", true, Kinds);
        BodyReader? generator = body.Nested("generator", false);
        string? source = body.Text("source", true, 200);
        IReadOnlyList<BodyReader>? triggers = body.NestedArray("triggers", true, allowEmpty: false);
        IReadOnlyList<BodyReader>? regions = body.NestedArray("regions", false, allowEmpty: false);
        body.Enum("clearedBy", false, Clearers);
        IReadOnlyList<string>? checks = body.StringArray("checks", false, allowEmpty: false, maxItemLength: 200);
        body.Text("summary", true, 4000);
        body.NoOtherKeys();

        context.RequireReference("repository", repository, "resource");
        if (source is not null && source != EstateFile && !Patterns.Contribution.IsMatch(source))
        {
            body.Error("document.source", "source", $"'{source}' is neither estate-file nor a pack's source named namespace/name.");
        }
        else if (source == EstateFile && repository is not null)
        {
            body.Error("document.source", "source", "An estate file is in the estate's own repository; a document of another repository is read through a pack's source.");
        }

        if (generator is not null)
        {
            generator.Text("id", true, 200, Patterns.Contribution, "a generator named namespace/name");
            generator.StringArray("inputs", true, allowEmpty: true, maxItemLength: 500);
            generator.NoOtherKeys();
        }

        if ((kind == "generated") != (generator is not null) && kind is not null)
        {
            body.Error("document.generator", "generator", "A generated document names its generator, and an authored one names none.");
        }

        foreach (BodyReader trigger in triggers ?? [])
        {
            Trigger(context, trigger);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (BodyReader region in regions ?? [])
        {
            string? id = region.Text("id", true, 100, Patterns.Slug, "a slug");
            region.Text("generator", true, 200, Patterns.Contribution, "a generator named namespace/name");
            region.StringArray("inputs", false, allowEmpty: false, maxItemLength: 500);
            region.NoOtherKeys();
            if (id is not null && !ids.Add(id))
            {
                region.Error("document.region", "id", $"Region '{id}' is declared twice.");
            }
        }

        foreach (string check in checks ?? [])
        {
            if (Array.IndexOf(Checks, check) < 0 && !Patterns.Contribution.IsMatch(check))
            {
                body.Error("document.check", "checks", $"'{check}' is neither one of the foundation's checks ({string.Join(", ", Checks)}) nor a pack's named namespace/name.");
            }
        }

        if (checks is not null && checks.Contains("matches-generator") && kind != "generated" && regions is null)
        {
            body.Error("document.check", "checks", "matches-generator compares generated content, and this document has no generator and no generated region.");
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("document.author", $"Authoring principal {context.Record.By} has no principal record.");
        }

        if (path is null)
        {
            return;
        }

        // One declaration per document: a revision supersedes it and keeps its path and repository.
        RecordEnvelope? previous = context.Record.Supersedes is null ? null : context.Find(context.Record.Supersedes);
        if (previous is not null && previous.Type == Name && (EstateModel.BodyString(previous, "path") != path || EstateModel.BodyString(previous, "repository") != repository))
        {
            context.Error("document.path-changed", "A declaration's revision keeps its path and its repository; another document is another declaration.");
        }

        RecordEnvelope? other = !context.Model.IsCurrent(context.Record) ? null : context.Model.Current(Name).FirstOrDefault(d => d.Id != context.Record.Id
            && EstateModel.BodyString(d, "path") == path && EstateModel.BodyString(d, "repository") == repository);
        if (other is not null)
        {
            context.Error("document.declared-twice", $"{path} is already declared by {other.Id}; revise that declaration instead.");
        }
    }

    /// <summary>The foundation's triggers have exact parameters; a pack's trigger carries its own, which its pack reads.</summary>
    private static void Trigger(ValidationContext context, BodyReader trigger)
    {
        string? on = trigger.Text("on", true, 200);
        trigger.Enum("level", true, Levels);
        if (on is not null && Patterns.Contribution.IsMatch(on))
        {
            return;
        }

        switch (on)
        {
            case "covers":
                trigger.StringArray("paths", true, allowEmpty: false, maxItemLength: 500, itemPattern: Patterns.RelativePath, patternHint: "normalized repository-relative paths");
                break;
            case "release":
                context.RequireReference("triggers.product", trigger.Ulid("product", true), "product");
                break;
            case "accepted":
                trigger.StringArray("keys", true, allowEmpty: false, maxItemLength: 100, itemPattern: Patterns.WorkKey, patternHint: "work-order keys");
                break;
            case "upgrade" or "decision" or null:
                break;
            default:
                trigger.Error("document.trigger", "on", $"'{on}' is neither one of the foundation's triggers ({string.Join(", ", Triggers)}) nor a pack's named namespace/name.");
                return;
        }

        trigger.NoOtherKeys();
    }
}
