// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>Milestone M3's additions in record format 2: possibilities, obligations and their discharge, credential references, the evidence vocabularies and qualifications.</summary>
public class M3AdditionsTests
{
    [Fact]
    public void APossibilityIsAnIdeaWithADispositionNeverAnApproval()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope order = estate.Add("work-order", estate.OperatorId, """{"key":"IDEA-1","kind":"ordinary","title":"Adopted","intent":"Work.","scope":["work"],"exitQuestion":"Done?","inputs":[],"deliverables":["work"]}""");
        RecordEnvelope idea = estate.Add("possibility", estate.BuilderId, """{"title":"A books pack","idea":"Ship a books pack.","origin":"The operator, in conversation.","disposition":"open"}""");
        estate.Add("possibility", estate.BuilderId, $$"""{"adoptedAs":{"type":"work-order","id":"{{order.Id}}"},"title":"A books pack","idea":"Ship a books pack.","origin":"The operator, in conversation.","disposition":"adopted","reason":"Planned for M5."}""", supersedes: idea.Id);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        using var wrong = new TestEstate(recordFormat: 2);
        RecordEnvelope unexplained = wrong.Add("possibility", wrong.BuilderId, """{"title":"Parked","idea":"Later.","origin":"The builder.","disposition":"parked"}""");
        RecordEnvelope unplaced = wrong.Add("possibility", wrong.BuilderId, """{"title":"Adopted","idea":"Now.","origin":"The builder.","disposition":"adopted","reason":"Yes."}""");
        RecordEnvelope wrongKind = wrong.Add("possibility", wrong.BuilderId, $$"""{"adoptedAs":{"type":"principal","id":"{{wrong.BuilderId}}"},"title":"Adopted","idea":"Now.","origin":"The builder.","disposition":"adopted","reason":"Yes."}""");
        List<Finding> findings = wrong.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location == unexplained.ExpectedPath + "#body.reason");
        Assert.Contains(findings, f => f.Code == "body.missing" && f.Location == unplaced.ExpectedPath + "#body.adoptedAs");
        Assert.Contains(findings, f => f.Code == "possibility.adopted-as" && f.Location.StartsWith(wrongKind.ExpectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void AnObligationADecisionLeavesIsDischargedOnceByItsOwnerWithEvidence()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, Decision(estate, $$""","obligations":[{"key":"notes","summary":"Name the change in the release notes.","owner":"{{estate.BuilderId}}","resolution":"evidence","discharge":"The 0.5.0 release notes name it.","gates":"release 0.5.0"}]"""));
        var subject = new RecordReference("decision", decision.Id, null);
        RecordEnvelope discharge = estate.Add("discharge", estate.BuilderId, """{"obligation":"notes","summary":"Named in the notes.","evidence":[{"summary":"docs/releases/0.5.0/release-notes.md names it."}]}""", subject);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        RecordEnvelope again = estate.Add("discharge", estate.OperatorId, """{"obligation":"notes","summary":"Again.","evidence":[{"summary":"The same notes."}]}""", subject);
        RecordEnvelope unknown = estate.Add("discharge", estate.BuilderId, """{"obligation":"elsewhere","summary":"Nothing owed.","evidence":[{"summary":"x"}]}""", subject);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "discharge.duplicate");
        Assert.Contains(findings, f => f.Code == "discharge.obligation" && f.Location == unknown.ExpectedPath);
        Assert.NotEqual(discharge.Id, again.Id);

        using var other = new TestEstate(recordFormat: 2);
        RecordEnvelope owned = other.Add("decision", other.BuilderId, Decision(other, $$""","obligations":[{"key":"notes","summary":"x","owner":"{{other.OperatorId}}","resolution":"action","discharge":"y"},{"key":"notes","summary":"x","owner":"{{other.OperatorId}}","resolution":"action","discharge":"y"}]"""));
        RecordEnvelope notOwner = other.Add("discharge", other.BuilderId, """{"obligation":"notes","summary":"Not mine.","evidence":[{"summary":"x"}]}""", new RecordReference("decision", owned.Id, null));
        findings = other.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "decision.obligation" && f.Location.StartsWith(owned.ExpectedPath, StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Code == "discharge.author" && f.Location == notOwner.ExpectedPath);
    }

    [Fact]
    public void ACredentialIsNamedByReferenceNeverByItsValue()
    {
        using var estate = new TestEstate(recordFormat: 2);
        estate.Add("credential-reference", estate.OperatorId, Credential(estate, "github-release-token"));
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        RecordEnvelope twice = estate.Add("credential-reference", estate.OperatorId, Credential(estate, "github-release-token"));
        RecordEnvelope backwards = estate.Add("credential-reference", estate.OperatorId, Credential(estate, "apple-signing", rotateBy: "2026-01-01T00:00:00Z"));
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "credential.duplicate" && f.Location == twice.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "credential.rotation" && f.Location == backwards.ExpectedPath + "#body.rotateBy");
    }

    [Fact]
    public void AVerificationSaysHowItsEvidenceWasObtainedAndWhenTheClaimExpires()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Example","role":"source of the example","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope reported = estate.Add("verification", estate.OperatorId, Verification(""" ,"evidenceClass":"report","observer":"provider","expires":"2027-01-01T00:00:00Z" """), resource.Reference);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error && f.Location.StartsWith(reported.ExpectedPath, StringComparison.Ordinal));

        RecordEnvelope stale = estate.Add("verification", estate.OperatorId, Verification(""" ,"evidenceClass":"rumor","expires":"2026-01-01T00:00:00Z" """), resource.Reference);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "body.enum" && f.Location == stale.ExpectedPath + "#body.evidenceClass");
        Assert.Contains(findings, f => f.Code == "verification.expires" && f.Location == stale.ExpectedPath + "#body.expires");

        using var early = new TestEstate();
        RecordEnvelope earlyResource = early.Add("resource", early.OperatorId, """{"kind":"repository","name":"Example","role":"source of the example","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope formatOne = early.Add("verification", early.OperatorId, Verification(""" ,"evidenceClass":"report" """), earlyResource.Reference);
        Assert.Contains(early.Load().Findings, f => f.Code == "body.format" && f.Location == formatOne.ExpectedPath + "#body.evidenceClass");
    }

    [Fact]
    public void AQualificationRestsOnEvidenceAndANonClaimIsAResult()
    {
        using var estate = new TestEstate(recordFormat: 2);
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Example","role":"source of the example","location":{"github":"Example/example"},"visibility":"private"}""");
        RecordEnvelope run = estate.Add("verification", estate.OperatorId, Verification(string.Empty), resource.Reference);
        RecordEnvelope product = estate.Add("product", estate.OperatorId, """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""");
        string artifacts = """[{"name":"vericairn-0.9.0-linux-x64.tar.gz","hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","bytes":10,"platform":"linux-x64"}]""";
        RecordEnvelope release = estate.Add("release", estate.OperatorId, $$"""{"version":"0.9.0","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":{{artifacts}},"recordFormat":{"reads":[1,2],"writes":2},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}""", new RecordReference("product", product.Id, null));
        var subject = new RecordReference("release", release.Id, null);
        estate.Add("qualification", estate.OperatorId, $$"""{"profile":"linux-x64","outcome":"qualified","evidence":[{"type":"verification","id":"{{run.Id}}"}],"summary":"The test matrix passed at the release commit.","limitations":[]}""", subject);
        estate.Add("qualification", estate.OperatorId, """{"profile":"Workbench on Linux","outcome":"non-claim","evidence":[],"summary":"Not observed natively.","limitations":["No GNOME or KDE run."]}""", subject);
        Assert.DoesNotContain(estate.Load().Findings, f => f.Severity == Severity.Error);

        RecordEnvelope bare = estate.Add("qualification", estate.OperatorId, """{"profile":"osx-arm64","outcome":"qualified","evidence":[],"summary":"Claimed.","limitations":[]}""", subject);
        RecordEnvelope wrongEvidence = estate.Add("qualification", estate.OperatorId, $$"""{"profile":"win-x64","outcome":"measured-in-part","evidence":[{"type":"decision","id":"{{product.Id}}"}],"summary":"x","limitations":[]}""", subject);
        List<Finding> findings = estate.Load().Findings.ToList();
        Assert.Contains(findings, f => f.Code == "qualification.evidence" && f.Location == bare.ExpectedPath);
        Assert.Contains(findings, f => f.Code == "qualification.evidence" && f.Location.StartsWith(wrongEvidence.ExpectedPath, StringComparison.Ordinal));
    }

    private static string Decision(TestEstate estate, string extra) =>
        $$"""{"title":"Ship","statement":"Ship it.","summary":"Ship.","synthesis":"The operator decides to ship.","effects":["ship"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z","stake":"medium","notAuthorized":[]{{extra}}}""";

    private static string Credential(TestEstate estate, string name, string rotateBy = "2027-01-01T00:00:00Z") =>
        $$"""{"name":"{{name}}","purpose":"Publish releases.","kind":"api-token","holder":"{{estate.OperatorId}}","store":"An Actions secret of Example/example","scopes":["contents: write"],"check":"gh auth status","status":"active","rotatedAt":"2026-10-01T00:00:00Z","rotateBy":"{{rotateBy}}"}""";

    private static string Verification(string extra) =>
        $$"""{"method":"github-actions","commit":"0123456789abcdef0123456789abcdef01234567","environment":{"runner":"ubuntu-latest"},"role":"automation","outcome":"passed","summary":"The run passed.","evidence":[],"limitations":[]{{extra}}}""";
}
