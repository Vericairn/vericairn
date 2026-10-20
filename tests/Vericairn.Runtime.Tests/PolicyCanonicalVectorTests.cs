// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Policies;
using static Vericairn.Runtime.Tests.FoundationPolicyTests;

namespace Vericairn.Runtime.Tests;

// Fixed records, source bytes and expected hashes make these complete-output vectors portable between
// test processes and platforms. Signed proof journeys are covered separately with isolated test keys.
// The basis includes scanner contract vericairn.writing-spacing/3, including vectors without prose.
public class PolicyCanonicalVectorTests
{
    private static readonly string[] Paths = ["docs/*.md"];
    private static readonly string[] Cases = ["accepted-still-open", "published-plan-still-open"];
    private static readonly string[] Suffixes = [""];
    private static readonly string[] Checks = ["inventory"];

    [Fact]
    public void FoundationAndWritingCompleteOutputMatchesThePortableVector()
    {
        RecordEnvelope order = Order();
        RecordEnvelope carrier = R("work-order", "carrier", new { key = "BAD-3", title = "Open311", releasePlan = new { product = "product", version = "1.0.0" } });
        RecordEnvelope[] records =
        [
            order, carrier,
            R("work-status", "open", new { status = "planned" }, subject: carrier.Reference),
            R("acceptance", "acceptance", new { }, "2026-01-01T12:00:00Z", carrier.Reference),
            R("release", "publication", new { status = "published", version = "1.0.0" }, "2026-01-01T13:00:00Z", new("product", "product", null)),
            P(FoundationPolicyKinds.Placement, new { backlogs = Array.Empty<object>() }, "placement"),
            P(FoundationPolicyKinds.Staleness, new { @class = "planned-without-release", maxAgeDays = 1 }, "age"),
            P(FoundationPolicyKinds.KeyFamily, new { families = new[] { new { prefix = "TASK", separator = "-", digits = 2, suffixes = Suffixes } }, createdFrom = "2026-01-01T00:00:00Z" }, "keys"),
            P(FoundationPolicyKinds.Consistency, new { cases = Cases }, "states"),
            P(FoundationPolicyKinds.RevisionLine, new { selectors = Paths, style = "estate-design/1" }, "revision", new { kind = "repository", repository = "estate", selectors = Paths }),
        ];
        PolicyRepository repository = Repository(PolicyBasisTests.File("docs/a.md", "# A\nRevision 1, January 1, 2026. Initial.\nOpen311"),
            PolicyBasisTests.File("docs/b.md", "# B\nMissing revision.\n~~~\nOpen311\n~~~\n`Open311`"));
        PolicyBasis basis = Basis(records, repositories: [repository]);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, Kinds);
        Assert.Equal(10, evaluation.Findings.Length);
        Assert.Equal(2, WritingEvaluation.Evaluate(basis).WarningCount);
        Check(basis, "sha256:ca4bc01212dcb61cb79ad57d2c53ec1907bd703538d05d888a7f4044c0986b48", writing: true);
    }

    [Fact]
    public void CompleteAnchoredDebtMatchesThePortableVector()
    {
        const string subject = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        RecordEnvelope estate = R("estate", subject, new { @operator = "operator" });
        RecordEnvelope policy = P(DutyRecords.Kind, new
        {
            subject = new { type = "estate", id = subject }, schedule = new { kind = "interval", unit = "months", every = 1, firstDueAt = "2026-01-31T08:00:00Z" },
            graceDays = 3, renewal = "none", discharge = new { checks = Checks, review = "independent", records = Array.Empty<object>() },
        });
        var basis = new PolicyBasis(PolicyBasisTests.Model([estate, policy]), "2026-04-01T13:00:00Z", PolicyTimeOrigin.Preview, implementations: Kinds.Implementations);
        PolicyEvaluation evaluation = PolicyEvaluator.Evaluate(basis, Kinds);
        Assert.True(evaluation.Complete);
        Assert.Equal(4, evaluation.Findings.Length);
        Assert.Equal(2, evaluation.Holds);
        Check(basis, "sha256:95b4524bd70c4c602c5bf337b3b154c9f98f6c80923afd702486e5aabb998c63", writing: false);
    }

    private static void Check(PolicyBasis basis, string expected, bool writing)
    {
        CultureInfo saved = CultureInfo.CurrentCulture, savedUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                var reversed = new PolicyBasis(PolicyBasisTests.Model(basis.Records.Reverse()), basis.AsOf, basis.Origin,
                    basis.Repositories.Reverse().Select(r => r with { Files = [.. r.Files.Reverse()] }), implementations: basis.Implementations.Reverse());
                PolicyEvaluation policy = PolicyEvaluator.Evaluate(reversed, Kinds);
                string actual = writing ? PolicyBasis.Hash(new { policy, writing = WritingEvaluation.Evaluate(reversed) }) : PolicyBasis.Hash(policy);
                Assert.True(expected == actual, $"Expected {expected}; actual {actual} in {name}.");
            }
        }
        finally { CultureInfo.CurrentCulture = saved; CultureInfo.CurrentUICulture = savedUi; }
    }
}
