// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class ReleaseAndMigrationTests
{
    private const string Product = """{"name":"Example","slug":"example","summary":"A product.","editions":[{"name":"Community","slug":"community","summary":"Free."}],"surfaces":[{"name":"CLI","slug":"cli","summary":"Command line."}]}""";
    private const string Candidate = """{"version":"0.1.0","repository":"Example/example","commit":"0123456789abcdef0123456789abcdef01234567","status":"candidate","artifacts":[{"name":"vericairn-0.1.0-linux-x64.zip","hash":"sha256:1111111111111111111111111111111111111111111111111111111111111111","bytes":10,"platform":"linux-x64"}],"recordFormat":{"reads":[1],"writes":1},"evidence":[{"summary":"Tests passed."}],"qualified":["linux-x64"],"signing":"unsigned developer build","limitations":[]}""";

    [Fact]
    public void PublicationNeedsTheOperatorsAcceptanceOfTheExactCandidate()
    {
        using var estate = new TestEstate();
        Grant(estate);
        RecordEnvelope product = estate.Add("product", estate.OperatorId, Product);
        RecordEnvelope candidate = estate.Add("release", estate.BuilderId, Candidate, new RecordReference("product", product.Id, null));
        EstateModel model = estate.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains("Releases:", EstateStatus.Render(model, EstateChecks.Run(model)), StringComparison.Ordinal);

        string published = Candidate.Replace("\"status\":\"candidate\"", "\"status\":\"published\",\"publishedAt\":\"2026-10-13T20:00:00Z\",\"url\":\"https://example.invalid/releases/0.1.0\"", StringComparison.Ordinal);
        RecordEnvelope premature = estate.Add("release", estate.BuilderId, published, new RecordReference("product", product.Id, null), supersedes: candidate.Id);
        Assert.Contains(estate.Load().Findings, f => f.Code == "release.unaccepted" && f.Location.Contains(premature.Id, StringComparison.Ordinal));
        estate.Add("acceptance", estate.BuilderId, """{"statement":"I accept my own release.","basis":[],"self":true}""", candidate.Reference);
        Assert.Contains(estate.Load().Findings, f => f.Code == "acceptance.policy");

        using var accepted = new TestEstate();
        Grant(accepted);
        RecordEnvelope product2 = accepted.Add("product", accepted.OperatorId, Product);
        RecordEnvelope candidate2 = accepted.Add("release", accepted.BuilderId, Candidate, new RecordReference("product", product2.Id, null));
        accepted.Add("acceptance", accepted.OperatorId, """{"statement":"Accepted from the recorded evidence.","basis":[],"self":false}""", candidate2.Reference);
        RecordEnvelope release = accepted.Add("release", accepted.BuilderId, published, new RecordReference("product", product2.Id, null), supersedes: candidate2.Id);
        accepted.Add("migration", accepted.BuilderId, $$"""{"fromPin":"source Example/example@0123456789ab","toPin":"release 0.1.0","fromFormat":1,"toFormat":1,"migrations":[],"changes":["manifest pinned to release 0.1.0"],"release":"{{release.Id}}"}""");
        model = accepted.Load();
        Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
        Assert.Contains("\"releases\"", EstateStatus.RenderJson(model, EstateChecks.Run(model)), StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateVersionsAndBackwardMigrationsAreRejected()
    {
        using var estate = new TestEstate();
        Grant(estate);
        RecordEnvelope product = estate.Add("product", estate.OperatorId, Product);
        estate.Add("release", estate.BuilderId, Candidate, new RecordReference("product", product.Id, null));
        estate.Add("release", estate.BuilderId, Candidate, new RecordReference("product", product.Id, null));
        estate.Add("migration", estate.BuilderId, """{"fromPin":"release 0.2.0","toPin":"release 0.1.0","fromFormat":2,"toFormat":1,"migrations":[],"changes":[]}""");
        EstateModel model = estate.Load();
        Assert.Contains(model.Findings, f => f.Code == "release.duplicate-version");
        Assert.Contains(model.Findings, f => f.Code == "migration.backward");
    }

    private static void Grant(TestEstate estate)
    {
        RecordEnvelope decision = estate.Add("decision", estate.BuilderId, $$"""{"title":"Delegate","statement":"Engineer.","summary":"Delegation.","synthesis":"Standing delegation.","effects":["builder engineers"],"affects":[],"decidedBy":"{{estate.OperatorId}}","decidedAt":"2026-10-13T12:00:00Z"}""");
        estate.Add("authority", estate.OperatorId, $$"""{"grantee":"{{estate.BuilderId}}","scope":["local-engineering"],"reserved":["publication"],"conditions":["label self-review"],"basis":"{{decision.Id}}"}""");
    }
}
