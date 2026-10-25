// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Templates;

/// <summary>Versioned regions of authored first-party instances. These are not whole-document companions.</summary>
internal sealed class FirstPartyTemplateGenerators : IDocumentGenerator
{
    internal const string Prefix = "vericairn.estate/";
    internal const string Revision = Prefix + "revision-line-v1";
    internal const string Roadmap = Prefix + "roadmap-releases-v1";
    internal static readonly IReadOnlyList<IDocumentGenerator> All = new[]
    {
        "revision-line", "operator-words", "design-index", "open-decisions", "design-decisions", "design-work", "roadmap-releases", "published-releases",
    }.Select(name => (IDocumentGenerator)new FirstPartyTemplateGenerators(name)).ToArray();

    private readonly string name;
    private FirstPartyTemplateGenerators(string name)
    {
        this.name = name;
        Id = Prefix + name + "-v1";
        ImmutableArray<string> types = name switch
        {
            "revision-line" or "design-index" => ["document-revision"],
            "operator-words" or "open-decisions" or "design-decisions" => ["ask", "decision"],
            "design-work" => ["work-order", "decision", "document-revision"],
            "roadmap-releases" => ["work-order"],
            _ => ["release"],
        };
        Descriptor = new(Id, 1, Id, CompanionImplementation.Hash(Id), ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            types, ["record"], ["current"], "text/markdown", "Ordinal identities and paths; release placement groups by product, version and carrier",
            "Internal", "single") { AllowsRepositoryOutput = false };
    }

    public string Id { get; }
    public DocumentGeneratorDescriptor Descriptor { get; }

    public string? GenerateRegion(EstateModel model, RecordEnvelope declaration, string region, IReadOnlyList<string> inputs)
    {
        if (inputs.Count != 1 || inputs[0] != "version=1" || EstateModel.BodyString(declaration, "repository") is not null || model.Findings.HasErrors)
            return null;
        string path = EstateModel.BodyString(declaration, "path")!;
        RecordEnvelope[] decisions = model.Current("decision").Where(d => TJson.Strings(d.Body, "documents").Contains(path, StringComparer.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        return name switch
        {
            "revision-line" => RevisionLine(model.Current("document-revision").SingleOrDefault(r => EstateModel.BodyString(r, "path") == path)),
            "roadmap-releases" => RoadmapRows(model.Current("work-order").Select(r => r.Body)),
            "design-index" => Index(model),
            "open-decisions" => Decisions(model, model.Current("ask"), model.Current("decision")),
            "design-decisions" or "operator-words" => Decisions(model,
                model.Current("ask").Where(a => decisions.Any(d => TJson.Strings(d.Body, "answers").Any(id => model.Chain(a).Contains(id, StringComparer.Ordinal)))), decisions),
            "design-work" => Work(model, path, decisions),
            "published-releases" => Releases(model, declaration),
            _ => null,
        };
    }

    internal static string RevisionLine(RecordEnvelope? revision) => revision is null ? "No document revision has been recorded.\n"
        : RevisionLine(revision.Body.GetProperty("revision").GetInt64(), revision.At[..10], EstateModel.BodyString(revision, "summary")!);

    internal static string RevisionLine(long revision, string date, string summary) =>
        "Revision " + revision.ToString(CultureInfo.InvariantCulture) + ", " + date + ": " + DocumentMarkdown.Text(summary) + "\n";

    internal static string RoadmapRows(IEnumerable<JsonElement> work)
    {
        var text = new StringBuilder("| Product | Release | Carrier | Work orders |\n|---|---|---|---|\n");
        var groups = work.Where(w => TJson.Text(TJson.Object(w, "placement"), "kind") == "release")
            .GroupBy(w => (Product: TJson.Text(TJson.Object(w, "placement"), "product")!,
                Version: TJson.Text(TJson.Object(w, "placement"), "version")!, Carrier: TJson.Text(TJson.Object(w, "placement"), "carrier")!))
            .OrderBy(g => g.Key.Product, StringComparer.Ordinal).ThenBy(g => g.Key.Version, StringComparer.Ordinal).ThenBy(g => g.Key.Carrier, StringComparer.Ordinal);
        foreach (var group in groups)
            text.Append("| ").Append(DocumentMarkdown.Text(group.Key.Product)).Append(" | ").Append(DocumentMarkdown.Text(group.Key.Version))
                .Append(" | ").Append(DocumentMarkdown.Text(group.Key.Carrier)).Append(" | ")
                .AppendJoin(", ", group.Select(w => TJson.Text(w, "key")!).Order(StringComparer.Ordinal).Select(DocumentMarkdown.Text)).Append(" |\n");
        return text.ToString();
    }

    private static string Index(EstateModel model)
    {
        var text = new StringBuilder("| Design | Title |\n|---|---|\n");
        foreach (RecordEnvelope revision in model.Current("document-revision").Where(r => EstateModel.BodyString(r, "path") is { } path
                && path.StartsWith("design/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal))
            .OrderBy(r => EstateModel.BodyString(r, "path"), StringComparer.Ordinal))
            text.Append("| ").Append(DocumentMarkdown.Text(EstateModel.BodyString(revision, "path")!)).Append(" | ")
                .Append(DocumentMarkdown.Text(EstateModel.BodyString(revision, "title")!)).Append(" |\n");
        return text.ToString();
    }

    private static string Decisions(EstateModel model, IEnumerable<RecordEnvelope> asks, IEnumerable<RecordEnvelope> decisions)
    {
        RecordEnvelope[] answers = decisions.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        var text = new StringBuilder();
        foreach (RecordEnvelope ask in asks.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            string state = Asks.State(model, ask).State;
            text.Append("### Ask ").Append(ask.Id).Append(" (refer to this identity)\n\n");
            text.Append("State: ").Append(state).Append(". Recipient: ").Append(EstateModel.BodyString(ask, "to")).Append(".\n\n");
            foreach (string field in new[] { "recommendation", "question", "consequence" })
                text.Append(field).Append(":\n\n").Append(Verbatim(EstateModel.BodyString(ask, field)!));
            foreach (string option in TJson.Strings(ask.Body, "options")) text.Append("Option:\n\n").Append(Verbatim(option));
            string[] linked = answers.Where(d => TJson.Strings(d.Body, "answers").Any(id => model.Chain(ask).Contains(id, StringComparer.Ordinal))).Select(d => d.Id).ToArray();
            if (linked.Length > 0) text.Append("Answered by: ").AppendJoin(", ", linked).Append(".\n\n");
        }
        foreach (RecordEnvelope decision in answers)
            text.Append("### Decision ").Append(decision.Id).Append(" — ").Append(DocumentMarkdown.Text(EstateModel.BodyString(decision, "title")!))
                .Append("\n\nOperator's words:\n\n").Append(Verbatim(EstateModel.BodyString(decision, "statement")!));
        return text.Length == 0 ? "No matching asks or decisions have been recorded.\n" : text.ToString();
    }

    // Ordinary words remain readable in a longer fence. Escaped JSON retains bytes that canonical Markdown
    // would otherwise normalize, or that resemble ownership markers. The byte hash distinguishes terminal newlines.
    private static string Verbatim(string value)
    {
        int longest = 0, run = 0;
        foreach (char c in value) { run = c == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new('`', Math.Max(3, longest + 1));
        string quoted = fence + "text\n" + value + (value.EndsWith('\n') ? "" : "\n") + fence + "\n\n";
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
        if (Encoding.UTF8.GetString(DocumentMarkdown.Bytes(quoted)) != quoted.TrimEnd('\n') + "\n"
            || value.Contains("<!-- vericairn:", StringComparison.Ordinal))
            quoted = "Exact text as a JSON string:\n\n```json\n" + JsonSerializer.Serialize(value) + "\n```\n\n";
        return "Recorded text: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " UTF-8 bytes; " + ContentHash.Sha256(bytes) + ".\n\n" + quoted;
    }

    private static string Work(EstateModel model, string path, IReadOnlyList<RecordEnvelope> decisions)
    {
        HashSet<string> references = decisions.SelectMany(d => TJson.Array(d.Body, "affects")).Select(r => TJson.Text(r, "id")!).ToHashSet(StringComparer.Ordinal);
        HashSet<string> pins = model.OfType("document-revision").Where(r => EstateModel.BodyString(r, "path") == path).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var text = new StringBuilder("| Work order | Title | Record |\n|---|---|---|\n");
        foreach (RecordEnvelope work in model.Current("work-order").Where(w => model.Chain(w).Any(references.Contains)
                || TJson.Array(w.Body, "inputs").Any(i => pins.Contains(TJson.Text(i, "id")!))).OrderBy(w => EstateModel.BodyString(w, "key"), StringComparer.Ordinal))
            text.Append("| ").Append(DocumentMarkdown.Text(EstateModel.BodyString(work, "key")!)).Append(" | ")
                .Append(DocumentMarkdown.Text(EstateModel.BodyString(work, "title")!)).Append(" | ").Append(work.Id).Append(" |\n");
        return text.ToString();
    }

    private static string Releases(EstateModel model, RecordEnvelope declaration)
    {
        string? product = TJson.Text(TJson.Object(TJson.Object(declaration.Body, "template"), "scope"), "product");
        var text = new StringBuilder("| Product | Version | Published | Release |\n|---|---|---|---|\n");
        foreach (RecordEnvelope release in model.Current("release").Where(r => EstateModel.BodyString(r, "status") == "published"
                && (product is null || r.Subject is not null && model.Chain(model.ById[r.Subject.Id]).Contains(product, StringComparer.Ordinal)))
            .OrderByDescending(r => EstateModel.BodyString(r, "publishedAt"), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
            text.Append("| ").Append(release.Subject!.Id).Append(" | ").Append(DocumentMarkdown.Text(EstateModel.BodyString(release, "version")!))
                .Append(" | ").Append(DocumentMarkdown.Text(EstateModel.BodyString(release, "publishedAt")!)).Append(" | ").Append(release.Id).Append(" |\n");
        return text.ToString();
    }
}
