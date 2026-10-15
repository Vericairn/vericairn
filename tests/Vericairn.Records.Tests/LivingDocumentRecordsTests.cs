// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>The living-document declaration and its clearance, record format 2 (the estate's design/living-documents.md §§ 4 and 7).</summary>
public class LivingDocumentRecordsTests
{
    private const string Hash = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string Covered = "sha256:2222222222222222222222222222222222222222222222222222222222222222";

    [Fact]
    public void ADeclarationNamesItsDocumentTriggersLevelsAndGenerators()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope repository = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Example","role":"source of the example","location":{"github":"Example/example"},"visibility":"public"}""");
        estate.Add("living-document", estate.OperatorId, Readme(product.Id));
        estate.Add("living-document", estate.OperatorId, $$"""{"path":"AGENTS.md","repository":"{{repository.Id}}","title":"Agent orientation","kind":"generated","generator":{"id":"vericairn.mcp/orientation","inputs":["the command tree"]},"source":"vericairn.software/git-file","triggers":[{"on":"release","level":"warn","product":"{{product.Id}}"},{"on":"vericairn.software/commands-changed","level":"remind","since":"0.4.2"}],"regions":[{"id":"commands","generator":"vericairn.mcp/orientation"}],"checks":["matches-generator","vericairn.software/notices-cover-locks"],"clearedBy":"independent","summary":"Generated from the build."}""");
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));

        // Refusals: an unknown trigger, a generator mismatch, an estate file in another repository, a comparison with nothing generated.
        RecordEnvelope unknown = estate.Add("living-document", estate.OperatorId, """{"path":"docs/a.md","title":"A","kind":"authored","source":"estate-file","triggers":[{"on":"sometimes","level":"warn"}],"summary":"x"}""");
        RecordEnvelope ungenerated = estate.Add("living-document", estate.OperatorId, """{"path":"docs/b.md","title":"B","kind":"generated","source":"estate-file","triggers":[{"on":"upgrade","level":"warn"}],"summary":"x"}""");
        RecordEnvelope misplaced = estate.Add("living-document", estate.OperatorId, $$"""{"path":"docs/c.md","repository":"{{repository.Id}}","title":"C","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"warn"}],"summary":"x"}""");
        RecordEnvelope compared = estate.Add("living-document", estate.OperatorId, """{"path":"docs/d.md","title":"D","kind":"authored","source":"estate-file","triggers":[{"on":"decision","level":"remind"}],"checks":["matches-generator"],"summary":"x"}""");
        RecordEnvelope escaping = estate.Add("living-document", estate.OperatorId, """{"path":"docs/e.md","title":"E","kind":"authored","source":"estate-file","triggers":[{"on":"covers","level":"warn","paths":["../outside"]}],"summary":"x"}""");
        RecordEnvelope twice = estate.Add("living-document", estate.OperatorId, Readme(product.Id));
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "document.trigger" && f.Location.StartsWith(unknown.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.generator" && f.Location.StartsWith(ungenerated.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.source" && f.Location.StartsWith(misplaced.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.check" && f.Location.StartsWith(compared.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.pattern" && f.Location.StartsWith(escaping.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.declared-twice" && f.Location.StartsWith(twice.ExpectedPath, StringComparison.Ordinal));

        // A format-1 estate has neither type.
        using var early = new TestEstate();
        RecordEnvelope old = early.Add("living-document", early.OperatorId, """{"path":"docs/a.md","title":"A","kind":"authored","source":"estate-file","triggers":[{"on":"upgrade","level":"warn"}],"summary":"x"}""");
        Assert.Contains(early.Load().Findings, f => f.Code == "record.type-format" && f.Location.StartsWith(old.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void ARevisionKeepsItsDocumentAndADecisionNamesTheDocumentsItChanges()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope readme = estate.Add("living-document", estate.OperatorId, Readme(product.Id));
        estate.Add("living-document", estate.OperatorId, Readme(product.Id).Replace("refuse", "warn", StringComparison.Ordinal), supersedes: readme.Id);
        Assert.False(estate.Load().Findings.HasErrors, string.Join("\n", estate.Load().Findings));
        RecordEnvelope latest = estate.Load().Latest(readme);
        RecordEnvelope moved = estate.Add("living-document", estate.OperatorId, latest.Body.GetRawText().Replace("\"README.md\"", "\"docs/README.md\"", StringComparison.Ordinal), supersedes: latest.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "document.path-changed" && f.Location.StartsWith(moved.ExpectedPath, StringComparison.Ordinal));

        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, Decision(estate, $$""","documents":["design/master-design.md","{{readme.Id}}"]"""));
        RecordEnvelope wrong = estate.Add("decision", estate.BuilderId, Decision(estate, ""","documents":["/etc/passwd"]"""));
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error && f.Location.StartsWith(decision.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "body.pattern" && f.Location.StartsWith(wrong.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AClearancePinsTheContentAnswersDeclaredTriggersAndMeetsClearedBy()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope product = Product(estate);
        RecordEnvelope readme = estate.Add("living-document", estate.OperatorId, Readme(product.Id).Replace("\"summary\"", "\"clearedBy\":\"independent\",\"summary\"", StringComparison.Ordinal));
        estate.Add("document-revision", estate.BuilderId, $$"""{"path":"README.md","title":"Readme","hash":"{{Hash}}","bytes":10,"mediaType":"text/markdown","revision":1,"summary":"First."}""");

        // The operator, who did not revise it, clears it as current with the covered identity and passing checks.
        RecordEnvelope cleared = estate.Add("document-review", estate.OperatorId, Review("current", "operator", $$""","covered":"{{Covered}}","answers":[{"on":"covers"}],"checks":[{"name":"names-version","outcome":"passed","summary":"Names 0.5.0."}]"""), readme.Reference);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(cleared.ExpectedPath, StringComparison.Ordinal));

        // The builder revised the file, so it is not independent of it, and this declaration asks for independence.
        RecordEnvelope self = estate.Add("document-review", estate.BuilderId, Review("current", "self", $$""","covered":"{{Covered}}","answers":[],"checks":[]"""), readme.Reference);
        RecordEnvelope claimed = estate.Add("document-review", estate.BuilderId, Review("current", "independent-agent", $$""","covered":"{{Covered}}","answers":[],"checks":[]"""), readme.Reference);
        RecordEnvelope stale = estate.Add("document-review", estate.BuilderId, Review("needs-change", "self", ""","answers":[],"checks":[]"""), readme.Reference);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "document.cleared-by" && f.Location.StartsWith(self.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.independence" && f.Location.StartsWith(claimed.ExpectedPath, StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Severity == Severity.Error && f.Location.StartsWith(stale.ExpectedPath, StringComparison.Ordinal));

        // A failed check refuses a clearance, a covers trigger needs the covered identity, an answer names a declared trigger,
        // and an estate file is reviewed in the working tree, not at a commit.
        RecordEnvelope failed = estate.Add("document-review", estate.OperatorId, Review("current", "operator", $$""","covered":"{{Covered}}","answers":[],"checks":[{"name":"links-resolve","outcome":"failed","summary":"A link is broken."}]"""), readme.Reference);
        RecordEnvelope uncovered = estate.Add("document-review", estate.OperatorId, Review("current", "operator", ""","answers":[],"checks":[]"""), readme.Reference);
        RecordEnvelope undeclared = estate.Add("document-review", estate.OperatorId, Review("current", "operator", $$""","covered":"{{Covered}}","answers":[{"on":"upgrade"}],"checks":[]"""), readme.Reference);
        RecordEnvelope committed = estate.Add("document-review", estate.OperatorId, Review("current", "operator", $$""","covered":"{{Covered}}","answers":[],"checks":[]""").Replace("\"bytes\":10", "\"bytes\":10,\"commit\":\"0123456789abcdef0123456789abcdef01234567\"", StringComparison.Ordinal), readme.Reference);
        findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "document.check-failed" && f.Location.StartsWith(failed.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.covered" && f.Location.StartsWith(uncovered.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.answers" && f.Location.StartsWith(undeclared.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "document.commit" && f.Location.StartsWith(committed.ExpectedPath, StringComparison.Ordinal));

        // A review that revises the content is the reviser's own.
        RecordEnvelope revised = estate.Add("document-review", estate.OperatorId, Review("revised", "operator", $$""","covered":"{{Covered}}","answers":[],"checks":[]"""), readme.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "document.independence" && f.Location.StartsWith(revised.ExpectedPath, StringComparison.Ordinal));
    }

    private static RecordEnvelope Product(TestEstate estate) =>
        estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");

    private static string Readme(string product) =>
        $$"""{"path":"README.md","title":"Readme","kind":"authored","source":"estate-file","triggers":[{"on":"release","level":"refuse","product":"{{product}}"},{"on":"covers","level":"warn","paths":["src","tools"]},{"on":"decision","level":"warn"},{"on":"accepted","level":"remind","keys":["REL-050"]}],"checks":["names-version","links-resolve"],"summary":"States what the product is and where its releases are."}""";

    private static string Review(string outcome, string independence, string extra) =>
        $$"""{"outcome":"{{outcome}}","content":{"hash":"{{Hash}}","bytes":10},"independence":"{{independence}}","summary":"Read it against the release.","findings":[]{{extra}}}""";

    private static string Decision(TestEstate estate, string extra) =>
        $$"""{"title":"Ship","statement":"Ship it.","summary":"Ship.","synthesis":"The operator decides to ship.","effects":["ship"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[]{{extra}}}""";
}
