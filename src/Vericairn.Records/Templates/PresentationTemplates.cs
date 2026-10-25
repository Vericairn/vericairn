// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using Vericairn.Presentation;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Templates;

/// <summary>A complete bound presentation and the deterministic checks on its required sections.</summary>
public sealed record TemplatePresentationResult(string Text, string Hash, long Bytes, IReadOnlyList<TemplateCheckResult> Checks);

/// <summary>First-party content forms on the template catalogue, generator and check registries. Registration is not adoption.</summary>
public static class PresentationTemplates
{
    /// <summary>The communication contract identity.</summary>
    public const string Communication = PresentationContent.Communication;
    /// <summary>The context brief contract identity.</summary>
    public const string Context = PresentationContent.Context;
    /// <summary>The software commit contract identity.</summary>
    public const string Commit = PresentationContent.Commit;
    /// <summary>The complete review contract identity.</summary>
    public const string Review = PresentationContent.Review;

    static PresentationTemplates()
    {
        TemplatePrimitives.RegisterCheck(new CompleteSource());
        DocumentGenerators.Register(new PresentationGenerator());
        Definitions = PresentationContent.Ids.Select(id => TemplateDefinition.Read(PresentationContent.Definition(id))).ToArray();
        if (Definitions.Any(d => d.Hash != PresentationContent.Hash(d.Id)))
            throw new InvalidOperationException("Installed presentation definitions must retain their canonical source identities.");
    }

    /// <summary>The four immutable versioned definitions; none selects itself for an estate.</summary>
    public static IReadOnlyList<TemplateDefinition> Definitions { get; }

    /// <summary>Resolve one explicit current estate adoption. Disabled or absent defaults preserve the previous output.</summary>
    public static RecordEnvelope? Adoption(EstateModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        TemplateDefinition definition = Definitions.Single(d => d.Id == id);
        RecordEnvelope[] candidates = model.Current("template-defaults").Where(r =>
            TJson.Text(TJson.Object(r.Body, "template"), "id") == id && TJson.Text(TJson.Object(r.Body, "scope"), "kind") == "estate"
            && TJson.Text(TJson.Object(r.Body, "options"), "adoption") == "enabled").ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1 || !Matches(candidates[0], definition)
            || model.Findings.Any(f => f.Severity == Severity.Error))
            throw new InvalidOperationException("The explicit presentation adoption is conflicted, invalid or unavailable; resolve it before relying on a template.");
        return candidates[0];
    }

    /// <summary>Whether the retained adoption pins this exact contract and explicitly enables it.</summary>
    public static bool Matches(RecordEnvelope adoption, TemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(definition);
        var pin = TJson.Object(adoption.Body, "template");
        return adoption.Type == "template-defaults" && TJson.Text(pin, "id") == definition.Id
            && TJson.Text(pin, "version") == definition.Version && TJson.Text(pin, "hash") == definition.Hash
            && TJson.Text(TJson.Object(adoption.Body, "scope"), "kind") == "estate"
            && TJson.Text(TJson.Object(adoption.Body, "options"), "adoption") == "enabled";
    }

    /// <summary>Prepare or inspect a presentation against its separately bound originals, never a candidate's own claims.</summary>
    public static TemplatePresentationResult Render(TemplatePin pin, string depth, RecordReference adoption,
        IReadOnlyDictionary<string, string> sections, long? maxBytes = null, string? candidate = null)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(adoption);
        ArgumentNullException.ThrowIfNull(sections);
        TemplateDefinition definition = Definitions.SingleOrDefault(d => d.Id == pin.Id && d.Version == pin.Version && d.Hash == pin.Hash)
            ?? throw new InvalidOperationException("The exact installed presentation contract is unavailable.");
        (string prepared, long bytes, IReadOnlyDictionary<string, string> complete) = PresentationContent.Render(pin.Id, depth, adoption.ToString(), sections, maxBytes);
        string text = candidate ?? prepared;
        var checks = new List<TemplateCheckResult>();
        try
        {
            IReadOnlyDictionary<string, TemplateSectionContent> parts = TemplateSections.Read(text, definition.Elements);
            foreach (var section in definition.Elements.Where(e => TJson.Text(e, "presence") == "required"))
            {
                string id = TJson.Text(section, "id")!;
                string expected = complete[id];
                bool generated = TJson.Text(TJson.Object(section, "synthesis"), "mode") == "generated";
                string block;
                if (generated)
                {
                    IDocumentGenerator generator = DocumentGenerators.Find(PresentationGenerator.Name)!;
                    byte[] raw = Encoding.UTF8.GetBytes(expected);
                    var snapshot = new DocumentSourceSnapshot("template-section", id, ContentHash.Sha256(raw), null, null, [.. raw], null, null, null, null, null, null);
                    DocumentRenderResult fresh = generator.Generate(new(DocumentGeneration.InterfaceVersion, generator.Descriptor!,
                        ImmutableDictionary<string, string>.Empty, "bound-presentation", id, [snapshot]));
                    block = fresh.Content ?? throw new InvalidOperationException("A required presentation generator is unavailable.");
                }
                else block = "\n" + PresentationContent.Block(expected);
                var source = new TemplateSource(id, "bound-presentation", id, ContentHash.Sha256(Encoding.UTF8.GetBytes(block)),
                    Encoding.UTF8.GetByteCount(block), block, "available", []);
                TemplateCheckResult check = TemplatePrimitives.Check(CompleteSource.Name)!.Check(parts.GetValueOrDefault(id)?.Content ?? "", [source]);
                checks.Add(check with { Id = id + "/complete-source" });
            }
        }
        catch (InvalidOperationException)
        {
            checks.Add(new("structure", "failed", "The presentation has missing, duplicated or ambiguous section boundaries."));
        }
        checks.Add(new("presentation-envelope", prepared == text ? "passed" : "failed", "The pinned contract, adoption, depth, section order and complete measured rendition must match the separately prepared basis."));
        return new(text, ContentHash.Sha256(Encoding.UTF8.GetBytes(text)), candidate is null ? bytes : Encoding.UTF8.GetByteCount(text), checks);
    }

    private sealed class CompleteSource : ITemplateCheck
    {
        internal const string Name = "vericairn.templates/complete-source";
        public string Id => Name;
        public TemplateCheckResult Check(string content, IReadOnlyList<TemplateSource> sources)
        {
            if (sources.Count != 1 || sources[0] is not { Status: "available", Content: { } expected } source
                || source.Hash != ContentHash.Sha256(Encoding.UTF8.GetBytes(expected)) || source.Bytes != Encoding.UTF8.GetByteCount(expected))
                return new(Id, "not-performed", "The complete separately bound source is unavailable.");
            return new(Id, content == expected ? "passed" : "failed", "Every required fact and exact original reference must be retained at every depth.");
        }
    }

    private sealed class PresentationGenerator : IDocumentGenerator
    {
        internal const string Name = "vericairn.templates/presentation";
        public string Id => Name;
        public DocumentGeneratorDescriptor Descriptor { get; } = new(Name, 1, "vericairn.presentation/1",
            ContentHash.Sha256(Encoding.UTF8.GetBytes("vericairn.presentation/1:exact-bound-section")), ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            [], ["template-section"], ["bound-presentation"], "text/markdown", "template section order", "Internal", "single") { AllowsRepositoryOutput = false };
        public DocumentRenderResult Generate(DocumentGenerationContext context)
        {
            if (context.InterfaceVersion != DocumentGeneration.InterfaceVersion || context.Selection != "bound-presentation" || context.Sources.Length != 1
                || context.Sources[0].Kind != "template-section" || context.Sources[0].Identity != context.Region
                || ContentHash.Sha256(context.Sources[0].Bytes.AsSpan()) != context.Sources[0].Hash)
                return DocumentRenderResult.Unavailable("A complete, exact section must be bound before presentation.");
            return DocumentRenderResult.Success(PresentationContent.Block(new UTF8Encoding(false, true).GetString(context.Sources[0].Bytes.AsSpan())));
        }
    }
}
