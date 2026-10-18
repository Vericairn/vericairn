// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

public class ReferenceCoverageTests
{
    private static readonly PolicyKindRegistry Registry = new([ReferenceCoverage.Registration(PolicyBasis.Hash("software implementation"))]);
    private static readonly string[] Categories = ["command", "record-type", "api-operation"];
    private const string RegisterPath = "design/vocabulary.md";
    private const string Terms = """
        # Vocabulary

        | Term | Meaning | Not to be confused with | Owning document and section | Status |
        | --- | --- | --- | --- | --- |
        | Runtime | The estate's live state owner. | A client | `docs/software.md` (product), Runtime | current |
        | Release | A version of a product. | A candidate | `design/releases.md` § 1 | current |

        ## Generated reference coverage

        | Category | Reference | Term |
        | --- | --- | --- |
        """;
    private const string Rows = """
        | command | `vericairn status` | Runtime |
        | record-type | `release` | Release |
        | api-operation | `GET /v1/status` | Runtime |
        """;
    private static string Register => Terms + "\n" + Rows + "\n";

    [Fact]
    public void EveryExactInventoryItemMapsToOneCurrentConceptAndWarnNeverHolds()
    {
        PolicyFinding covered = Assert.Single(Evaluate(Register).Findings);
        Assert.Equal("satisfied", covered.Verdict);
        Assert.Contains("3 exact reference items", covered.Actual, StringComparison.Ordinal);
        Assert.Contains(ReferenceInventoryTests.Commit, covered.Actual, StringComparison.Ordinal);
        PolicyEvaluation due = Evaluate(Terms + "\n");
        Assert.Equal(["api-operation:GET /v1/status", "command:vericairn status", "record-type:release"], due.Findings.Select(f => f.Item));
        Assert.All(due.Findings, f => { Assert.Equal("unmet", f.Verdict); Assert.Equal("warn", f.Level); Assert.Equal(Severity.Warning, f.Severity); });
        Assert.Equal(0, due.Holds);
        Assert.Equal(3, due.Warnings);
    }

    [Theory]
    [InlineData("vericairn status", "Vericairn status")]
    [InlineData("vericairn status", "vericairn *")]
    [InlineData("vericairn status", "vericairn")]
    [InlineData("vericairn status", "vericairn status --json")]
    [InlineData("GET /v1/status", "get /v1/status")]
    [InlineData("GET /v1/status", "GET  /v1/status")]
    [InlineData("GET /v1/status", "GET /v1/Status")]
    [InlineData("`vericairn status`", "`  vericairn status  `")]
    public void CaseWhitespaceFlagsAndFamilyWildcardsAreNotCoverage(string original, string replacement)
    {
        PolicyEvaluation result = Evaluate(Register.Replace(original, replacement, StringComparison.Ordinal));
        Assert.Single(result.Findings, f => f.Verdict == "unmet");
        Assert.Contains(result.Findings, f => f.Verdict == "satisfied" && f.Actual.Contains("historical coverage only", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("| Runtime | The estate's live state owner.", "| Runtime | ")]
    [InlineData("`docs/software.md` (product), Runtime", "")]
    [InlineData("`docs/software.md` (product), Runtime", "`docs/software.md`")]
    [InlineData("Runtime | current |", "Runtime | historical |")]
    [InlineData("Runtime | current |", "Runtime | avoid |")]
    [InlineData("| Runtime |", "| runtime |")]
    public void MissingDefinitionsOwningSectionsAndCurrentTermsFailEachAffectedReference(string original, string replacement)
    {
        string text = Terms.Replace(original, replacement, StringComparison.Ordinal) + "\n" + Rows;
        PolicyEvaluation result = Evaluate(text);
        Assert.Equal(2, result.Findings.Count(f => f.Verdict == "unmet"));
        Assert.Equal(["api-operation:GET /v1/status", "command:vericairn status"], result.Findings.Select(f => f.Item));
        Assert.True(result.Complete);
    }

    [Fact]
    public void DuplicateCurrentTermsAndAmbiguousMappingsFailWithoutDoubleCounting()
    {
        string duplicate = "| Runtime | Another definition. | Other | `docs/software.md`, Runtime | current |\n";
        string text = Register.Replace("\n\n## Generated", "\n" + duplicate + "\n## Generated", StringComparison.Ordinal);
        Assert.Equal(2, Evaluate(text).Findings.Count(f => f.Verdict == "unmet"));
        PolicyFinding mapping = Assert.Single(Evaluate(Register + "| command | `vericairn status` | Release |\n").Findings);
        Assert.Equal("command:vericairn status", mapping.Item);
        Assert.Contains("Ambiguous", mapping.Actual, StringComparison.Ordinal);
        string historical = duplicate.Replace("current", "historical", StringComparison.Ordinal);
        Assert.Equal("satisfied", Assert.Single(Evaluate(Register.Replace("\n\n## Generated", "\n" + historical + "\n## Generated", StringComparison.Ordinal)).Findings).Verdict);
    }

    [Fact]
    public void SeveralOperationsMayUseTheSameTermAndHistoricalMappingsAreInformational()
    {
        PolicyEvaluation result = Evaluate(Register + "| command | `vericairn retired` | Runtime |\n");
        Assert.Equal(0, result.Warnings);
        PolicyFinding historical = Assert.Single(result.Findings, f => f.Item != "");
        Assert.Equal(Severity.Info, historical.Severity);
        Assert.Contains("absent from build commit", historical.Actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("| Category | Reference | Term |", "| Category | Reference | Wrong |")]
    [InlineData("| --- | --- | --- |\n| command", "| --- | --- |\n| command")]
    [InlineData("| command | `vericairn status` | Runtime |", "| command | `vericairn status` | Runtime | Extra |")]
    [InlineData("| command | `vericairn status` | Runtime |", "| command | `vericairn status | Runtime |")]
    [InlineData("| Term | Meaning |", "| Term | Definition |")]
    [InlineData("## Generated reference coverage", "## Another section")]
    public void MalformedSelectedTablesAreUnavailableWithNoPartialCoverage(string original, string replacement)
    {
        AssertUnavailable(Evaluate(Register.Replace(original, replacement, StringComparison.Ordinal)));
    }

    [Fact]
    public void DuplicateSectionsAndFencedExampleTablesCannotSupplyCoverage()
    {
        AssertUnavailable(Evaluate(Register + "\n## Generated reference coverage\n"));
        AssertUnavailable(Evaluate(Register + "\n| Category | Reference | Term |\n| --- | --- | --- |\n"));
        AssertUnavailable(Evaluate(Register + "| command | `vericairn status` | Runtime |\n"));
        AssertUnavailable(Evaluate("~~~md\n" + Register + "\n~~~\n"));
        Assert.Equal("satisfied", Assert.Single(Evaluate(Register + "\n```md\n" + Register + "\n```\n").Findings).Verdict);
    }

    [Fact]
    public void EscapedSeparatorsAndCodeSpansPreserveTheExactTermName()
    {
        string text = Register.Replace("| Runtime |", "| `Run\\|time` |", StringComparison.Ordinal);
        Assert.Equal("satisfied", Assert.Single(Evaluate(text).Findings).Verdict);
        text = Register.Replace("| Runtime |", "| ``Run|`time`` |", StringComparison.Ordinal);
        Assert.Equal("satisfied", Assert.Single(Evaluate(text).Findings).Verdict);
        text = Register.Replace("`vericairn status`", "`` vericairn status ``", StringComparison.Ordinal);
        Assert.Equal("satisfied", Assert.Single(Evaluate(text).Findings).Verdict);
    }

    [Fact]
    public void MissingHashMismatchedOrWrongCommitSourcesDoNotPartlyCountEvenUnselectedCategories()
    {
        PolicyRepository source = ReferenceInventoryTests.Repository();
        AssertUnavailable(Evaluate(Register, source with { Files = [.. source.Files.Skip(1)] }));
        AssertUnavailable(Evaluate(Register, source with { Files = [.. source.Files.Select(f => f.Path == "api-reference.md" ? f with { Bytes = [1, 2, 3] } : f)] }, categories: ["record-type"]));
        AssertUnavailable(Evaluate(Register, source with { Revision = new string('b', 40) }));
        AssertUnavailable(Evaluate(Register, registerFile: PolicyBasisTests.File(RegisterPath, Register) with { Available = false }));
        AssertUnavailable(Evaluate(Register, registerFile: PolicyBasisTests.File(RegisterPath, Register) with { IsLink = true }));
    }

    [Fact]
    public void CategorySelectionIsExplicitAndParametersAreClosed()
    {
        PolicyFinding finding = Assert.Single(Evaluate(Terms + "\n", categories: ["record-type"]).Findings);
        Assert.Equal("record-type:release", finding.Item);
        Assert.Equal(1, Evaluate(Register, categories: ["commands"]).Errors);
        Assert.Equal(1, Evaluate(Register, categories: []).Errors);
        Assert.Equal(1, Evaluate(Register, categories: ["command", "command"]).Errors);
    }

    [Fact]
    public void SubmissionUsesTheExactCandidateCommitAndPreviewIsExplicit()
    {
        var submission = new PolicySubmission(new("release", "candidate", PolicyBasis.Hash("candidate")), "product", "0.9.0", new string('b', 40));
        AssertUnavailable(Evaluate(Register, submission: submission));
        PolicyRepository candidate = ReferenceInventoryTests.Repository(commit: submission.Commit);
        Assert.Equal("satisfied", Assert.Single(Evaluate(Register, candidate, submission: submission).Findings).Verdict);
        Assert.Equal(PolicyTimeOrigin.Preview, Evaluate(Register, candidate).Origin);
    }

    [Fact]
    public void StatusUsesThePinnedReleaseAndTheBasisCopiesAndHashesThatPin()
    {
        PolicyRepository source = ReferenceInventoryTests.Repository();
        var pin = new EstateManifest { Estate = "estate", RecordFormat = 3, Profile = "repository-synced", Mode = "release", Repository = "Example/product", Version = "0.8.0" };
        RecordEnvelope release = R("release", "publication", new { status = "published", version = "0.8.0", repository = "Example/product", commit = ReferenceInventoryTests.Commit }, subject: new("product", "product", null));
        PolicyEvaluation result = Evaluate(Register, source, pin: pin, records: [release]);
        Assert.Equal("satisfied", Assert.Single(result.Findings).Verdict);
        Assert.Equal(release.Reference, result.Findings[0].Anchor);
        AssertUnavailable(Evaluate(Register, ReferenceInventoryTests.Repository(commit: new string('b', 40)), pin: pin, records: [release]));
        var wrong = new EstateManifest { Estate = "estate", RecordFormat = 3, Profile = "repository-synced", Mode = "release", Repository = "Example/product", Version = "0.9.0" };
        PolicyEvaluation changed = Evaluate(Register, source, pin: wrong, records: [release]);
        AssertUnavailable(changed);
        Assert.NotEqual(result.Basis, changed.Basis);
    }

    [Fact]
    public void PackRemovalAndParameterSelectionConflictsStayUnavailable()
    {
        PolicyBasis basis = MakeBasis(Register);
        AssertUnavailable(PolicyEvaluator.Evaluate(basis, new([])));
        JsonObject body = JsonNode.Parse(basis.Model.Current("estate-policy").Single().Body.GetRawText())!.AsObject();
        body["appliesTo"] = new JsonObject { ["kind"] = "product", ["product"] = "different" };
        PolicyBasis wrong = MakeBasis(Register, policy: R("estate-policy", "policy", body));
        Assert.Equal(1, PolicyEvaluator.Evaluate(wrong, Registry).Errors);
    }

    [Fact]
    public void TheProductReleaseProfileCanNameAnExistingRepositoryWithoutAResourceSubject()
    {
        PolicyBasis basis = MakeBasis(Register);
        RecordEnvelope product = R("product", "product", new { release = new { destination = new { provider = "github-releases", settings = new { repository = "Example/product" } } } });
        RecordEnvelope resource = R("resource", "repository", new { kind = "repository", location = new { github = "Example/product" } });
        RecordEnvelope[] records = basis.Records.Select(r => r.Type == "product" ? product : r.Id == resource.Id ? resource : r).ToArray();
        PolicyBasis selected = new(PolicyBasisTests.Model(records), basis.AsOf, basis.Origin, basis.Repositories, implementations: Registry.Implementations);
        Assert.Equal("satisfied", Assert.Single(PolicyEvaluator.Evaluate(selected, Registry).Findings).Verdict);
        RecordEnvelope unrelated = R("resource", "repository", resource.Body, subject: new("product", "other", null));
        PolicyBasis conflict = new(PolicyBasisTests.Model(records.Select(r => r.Id == unrelated.Id ? unrelated : r)), basis.AsOf, basis.Origin, basis.Repositories, implementations: Registry.Implementations);
        AssertUnavailable(PolicyEvaluator.Evaluate(conflict, Registry));
    }

    [Fact]
    public void CompleteEvaluationsAreIndependentOfCultureAndInputOrder()
    {
        PolicyBasis basis = MakeBasis(Terms + "\n");
        string expected = PolicyEvaluator.Evaluate(basis, Registry).Canonical();
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                var reversed = new PolicyBasis(PolicyBasisTests.Model(basis.Records.Reverse()), basis.AsOf, basis.Origin, basis.Repositories.Reverse(), implementations: Registry.Implementations);
                Assert.Equal(expected, PolicyEvaluator.Evaluate(reversed, Registry).Canonical());
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    private static void AssertUnavailable(PolicyEvaluation result)
    {
        Assert.False(result.Complete);
        Assert.Equal("unavailable", Assert.Single(result.Findings).Verdict);
        Assert.Equal(0, Assert.Single(result.Rules).Subjects);
        Assert.Equal(0, result.Holds);
    }

    private static PolicyEvaluation Evaluate(string text, PolicyRepository? source = null, PolicyFile? registerFile = null, string[]? categories = null,
        PolicySubmission? submission = null, EstateManifest? pin = null, RecordEnvelope[]? records = null) =>
        PolicyEvaluator.Evaluate(MakeBasis(text, source, registerFile, categories, submission, pin, records), Registry);

    private static PolicyBasis MakeBasis(string text, PolicyRepository? source = null, PolicyFile? registerFile = null, string[]? categories = null,
        PolicySubmission? submission = null, EstateManifest? pin = null, RecordEnvelope[]? records = null, RecordEnvelope? policy = null)
    {
        RecordEnvelope declaration = P(ReferenceCoverage.Kind, new { product = "product", repository = "repository", referenceManifest = ReferenceInventory.FileName,
            registerRepository = "estate", registerPath = RegisterPath, categories = categories ?? Categories });
        JsonObject body = JsonNode.Parse(declaration.Body.GetRawText())!.AsObject(); body["level"] = "warn";
        RecordEnvelope resource = R("resource", "repository", new { kind = "repository", location = new { github = "Example/product" } }, subject: new("product", "product", null));
        PolicyBasis basis = Basis([policy ?? R("estate-policy", declaration.Id, body), resource, .. records ?? []]);
        basis.Model.Manifest = pin;
        PolicyTimeOrigin origin = submission is not null ? PolicyTimeOrigin.Submission : pin is not null ? PolicyTimeOrigin.Recorded : PolicyTimeOrigin.Preview;
        string? at = origin == PolicyTimeOrigin.Recorded ? basis.Records.Max(r => r.At) : basis.AsOf;
        return new(basis.Model, at, origin, [source ?? ReferenceInventoryTests.Repository(), Repository(registerFile ?? PolicyBasisTests.File(RegisterPath, text))], implementations: Registry.Implementations, submission: submission);
    }
}
